using System;
using System.Collections.Generic;
using System.IO;
using NBitcoin;
using NBitcoin.DataEncoders;
using UnityEngine;

public enum MultisigAddressType
{
    P2WSH,        // Native SegWit (Best practice, bech32: bc1q... / tb1q...)
    P2SH_P2WSH,   // Nested SegWit (Best compatibility: 3... / 2...)
    P2SH          // Legacy (p2sh: 3... / 2...)
}

[System.Serializable]
public class MultisigVaultConfig
{
    public string vaultId = System.Guid.NewGuid().ToString();
    public string vaultName = "Multisig Vault";
    public int requiredSignatures = 2; // M
    public int totalKeys = 2;          // N
    public int addressTypeInt = 0;      // 0 = P2WSH, 1 = P2SH_P2WSH, 2 = P2SH
    public bool isTestNet = true;

    public List<string> publicKeysHex = new List<string>();
    public string redeemScriptHex;
    public string multisigAddress;
    public string myPublicKeyHex;
    public string vaultMnemonic;        // Dedicated 12-word seed phrase for this vault (Key #1)
    public string key2Mnemonic;         // Dedicated/mock 12-word seed phrase for Key #2 (Co-signer)
    public string derivationPath = "m/48'/0'/0'/2'"; // BIP48 standard derivation path
    public string vaultSerial;          // Satolite 12-digit serial (SN + 12 digits)

    // Coordinator Policy (Watch-Only)
    public bool isCoordinator = false;  // true for Watch-Only Coordinator, false for Quorum Signer
    public string descriptor = "";      // Output descriptor string (if imported)

    public MultisigAddressType GetAddressType() => (MultisigAddressType)addressTypeInt;
}

[System.Serializable]
public class MultisigVaultList
{
    public List<MultisigVaultConfig> vaults = new List<MultisigVaultConfig>();
}

public class MultisigCore
{
    public static NBitcoin.Network GetNetwork(bool isTestNet)
    {
        return isTestNet ? NBitcoin.Network.TestNet : NBitcoin.Network.Main;
    }

    /// <summary>
    /// Generates the multisig script and derives the corresponding address based on type and network.
    /// </summary>
    public static (string address, string redeemScriptHex) GenerateMultisigAddress(
        int m, 
        List<PubKey> pubKeys, 
        MultisigAddressType addressType, 
        bool isTestNet)
    {
        if (pubKeys == null || pubKeys.Count < m || m <= 0)
            throw new ArgumentException("Invalid quorum or public key count.");

        var network = GetNetwork(isTestNet);
        
        // Deterministically sort public keys so all co-signers calculate the EXACT same address
        var sortedKeys = new List<PubKey>(pubKeys);
        sortedKeys.Sort((a, b) => string.Compare(a.ToHex().ToLowerInvariant(), b.ToHex().ToLowerInvariant(), StringComparison.Ordinal));

        // Create standard OP_M <pubkeys> OP_N OP_CHECKMULTISIG script
        Script redeemScript = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(m, sortedKeys.ToArray());
        string redeemScriptHex = redeemScript.ToHex();
        string addressStr = "";

        switch (addressType)
        {
            case MultisigAddressType.P2WSH:
                // Native SegWit (BIP141/BIP173) -> tb1q... / bc1q...
                var witHash = redeemScript.WitHash;
                var p2wshAddr = witHash.GetAddress(network);
                addressStr = p2wshAddr.ToString();
                break;

            case MultisigAddressType.P2SH_P2WSH:
                // Nested SegWit -> 2... (Testnet) / 3... (Mainnet)
                var witScript = redeemScript.WitHash.ScriptPubKey;
                var scriptId = witScript.Hash;
                var p2shP2wshAddr = scriptId.GetAddress(network);
                addressStr = p2shP2wshAddr.ToString();
                break;

            case MultisigAddressType.P2SH:
                // Legacy P2SH (BIP16) -> 2... (Testnet) / 3... (Mainnet)
                var p2shAddr = redeemScript.Hash.GetAddress(network);
                addressStr = p2shAddr.ToString();
                break;
        }

        return (addressStr, redeemScriptHex);
    }

    /// <summary>
    /// Decodes an existing Redeem Script (Hex) and extracts M, N, Public Keys, and the derived address.
    /// </summary>
    public static (int m, int n, List<PubKey> pubKeys, string address) ParseRedeemScript(
        string redeemScriptHex, 
        MultisigAddressType addressType, 
        bool isTestNet)
    {
        if (string.IsNullOrEmpty(redeemScriptHex))
            throw new ArgumentException("Redeem script is empty.");

        byte[] scriptBytes = Encoders.Hex.DecodeData(redeemScriptHex.Trim());
        Script script = new Script(scriptBytes);

        var param = PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(script);
        if (param == null || param.PubKeys == null || param.PubKeys.Length == 0)
            throw new Exception("Script is not a valid Bitcoin PayToMultiSig script.");

        var pubKeys = new List<PubKey>(param.PubKeys);
        var (addr, _) = GenerateMultisigAddress(param.SignatureCount, pubKeys, addressType, isTestNet);
        return (param.SignatureCount, param.PubKeys.Length, pubKeys, addr);
    }

    /// <summary>
    /// Generates a dedicated 12-word seed phrase for a new vault, keeping it isolated from the main wallet.
    /// </summary>
    public static (string mnemonic, PubKey pubKey, Key privateKey, string zpub) GenerateVaultKeypair(bool isTestNet)
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var masterKey = mnemonic.DeriveExtKey();
        int coinType = isTestNet ? 1 : 0;
        var accountKey = masterKey.Derive(new KeyPath($"m/84'/{coinType}'/0'"));
        var key = accountKey.Derive(0).Derive(0).PrivateKey;
        var network = GetNetwork(isTestNet);
        string xpub = accountKey.Neuter().ToString(network);
        string zpub = ConvertToSlip132(xpub, isTestNet);
        return (mnemonic.ToString(), key.PubKey, key, zpub);
    }

    /// <summary>
    /// Gets the private key to sign transactions for a specific vault.
    /// </summary>
    public static Key GetVaultPrivateKey(MultisigVaultConfig vault)
    {
        if (vault == null) return null;
        if (!string.IsNullOrEmpty(vault.vaultMnemonic))
        {
            try
            {
                var mnemonic = new Mnemonic(vault.vaultMnemonic, Wordlist.English);
                var masterKey = mnemonic.DeriveExtKey();
                int coinType = vault.isTestNet ? 1 : 0;
                var keyPath = new KeyPath($"m/84'/{coinType}'/0'/0/0");
                return masterKey.Derive(keyPath).PrivateKey;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MultisigCore] Failed to derive vault private key from mnemonic: {ex.Message}");
            }
        }

        return GetCurrentWalletPrivateKey(vault.isTestNet);
    }

    /// <summary>
    /// Gets the current wallet's active Public Key for co-signing
    /// </summary>
    public static PubKey GetCurrentWalletPubKey(bool isTestNet)
    {
        var wallet = WalletManager2.Instance.GetCurrentWallet();
        if (wallet == null) return null;

        var network = GetNetwork(isTestNet);

        if (!string.IsNullOrEmpty(wallet.seedPhrase))
        {
            int coinType = isTestNet ? 1 : 0;
            var mnemonic = new Mnemonic(wallet.seedPhrase, Wordlist.English);
            var masterKey = mnemonic.DeriveExtKey();
            var keyPath = new KeyPath($"m/84'/{coinType}'/0'/0/0");
            var key = masterKey.Derive(keyPath).PrivateKey;
            return key.PubKey;
        }
        else if (!string.IsNullOrEmpty(wallet.privateKey))
        {
            try
            {
                BitcoinSecret secret = null;
                try {
                    secret = new BitcoinSecret(wallet.privateKey, network);
                } catch {
                    secret = new BitcoinSecret(wallet.privateKey, isTestNet ? NBitcoin.Network.Main : NBitcoin.Network.TestNet);
                }
                return secret.PrivateKey.PubKey;
            }
            catch
            {
                var keyBytes = Encoders.Hex.DecodeData(wallet.privateKey);
                var key = new Key(keyBytes);
                return key.PubKey;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the main wallet's Extended Public Key (xpub / tpub), SLIP-0132 zpub / vpub, and compressed hex public key.
    /// </summary>
    public static (string xpub, string zpub, string hexPubKey) GetMainWalletExtendedPubKey(bool isTestNet)
    {
        var wallet = WalletManager2.Instance.GetCurrentWallet();
        if (wallet == null) return ("", "", "");

        var network = GetNetwork(isTestNet);
        string hexPubKey = "";
        var activePubKey = GetCurrentWalletPubKey(isTestNet);
        if (activePubKey != null) hexPubKey = activePubKey.ToHex();

        if (string.IsNullOrEmpty(wallet.seedPhrase))
        {
            // Seedless / imported private key has no BIP32 master xpub
            return (hexPubKey, hexPubKey, hexPubKey);
        }

        try
        {
            int coinType = isTestNet ? 1 : 0;
            var mnemonic = new Mnemonic(wallet.seedPhrase, Wordlist.English);
            var masterKey = mnemonic.DeriveExtKey();
            var accountKey = masterKey.Derive(new KeyPath($"m/84'/{coinType}'/0'"));
            string xpub = accountKey.Neuter().ToString(network);

            // Convert to SLIP-0132 zpub (Mainnet) or vpub (Testnet)
            string zpub = ConvertToSlip132(xpub, isTestNet);
            return (xpub, zpub, hexPubKey);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultisigCore] Failed to derive extended pubkey: {ex.Message}");
            return (hexPubKey, hexPubKey, hexPubKey);
        }
    }

    private static string ConvertToSlip132(string xpub, bool isTestNet)
    {
        try
        {
            byte[] data = Encoders.Base58Check.DecodeData(xpub);
            if (data.Length == 78)
            {
                // zpub (0x04b24746) for MainNet, vpub (0x045f1cf6) for TestNet
                byte[] version = isTestNet 
                    ? new byte[] { 0x04, 0x5F, 0x1C, 0xF6 } 
                    : new byte[] { 0x04, 0xB2, 0x47, 0x46 };
                Array.Copy(version, 0, data, 0, 4);
                return Encoders.Base58Check.EncodeData(data);
            }
        }
        catch { }
        return xpub;
    }

    /// <summary>
    /// Parses a Zpub (BIP84), vpub (Testnet), xpub/tpub, or raw 66-character hex public key.
    /// If an extended public key is provided, derives the child PubKey at path 0/index (default 0/0).
    /// </summary>
    public static (PubKey pubKey, string canonicalHex, string zpubString) ParseZpubOrPubKey(string input, bool isTestNet, uint index = 0)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Public key or Zpub cannot be empty.");

        string trimmed = input.Trim();
        var network = GetNetwork(isTestNet);

        // 1. Try parsing as SLIP-0132 Zpub / vpub or standard xpub / tpub
        if (trimmed.StartsWith("zpub", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("vpub", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("xpub", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("tpub", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                byte[] data = Encoders.Base58Check.DecodeData(trimmed);
                if (data.Length == 78)
                {
                    // Convert version bytes to standard xpub (mainnet) or tpub (testnet)
                    byte[] standardVersion = isTestNet
                        ? new byte[] { 0x04, 0x35, 0x87, 0xCF }  // tpub
                        : new byte[] { 0x04, 0x88, 0xB2, 0x1E }; // xpub
                    Array.Copy(standardVersion, 0, data, 0, 4);
                    string standardXpub = Encoders.Base58Check.EncodeData(data);

                    var extPubKey = ExtPubKey.Parse(standardXpub, network);
                    // Derive 0/index (first external receiving address key under BIP84 account)
                    PubKey derived = extPubKey.Derive(0).Derive(index).PubKey;
                    string zpubOut = ConvertToSlip132(trimmed, isTestNet);
                    return (derived, derived.ToHex(), zpubOut);
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid Zpub / Extended Key format: {ex.Message}");
            }
        }

        // 2. Fallback: Parse as raw 66-character compressed hex public key (02... or 03...)
        try
        {
            string cleanHex = trimmed.Replace(" ", "").Replace("\r", "").Replace("\n", "");
            var pk = new PubKey(cleanHex);
            return (pk, pk.ToHex(), "");
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid key format. Expected a BIP84 Zpub or compressed hex PubKey: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the private key of the current device to sign transactions
    /// </summary>
    public static Key GetCurrentWalletPrivateKey(bool isTestNet)
    {
        var wallet = WalletManager2.Instance.GetCurrentWallet();
        if (wallet == null) return null;

        var network = GetNetwork(isTestNet);

        if (!string.IsNullOrEmpty(wallet.seedPhrase))
        {
            int coinType = isTestNet ? 1 : 0;
            var mnemonic = new Mnemonic(wallet.seedPhrase, Wordlist.English);
            var masterKey = mnemonic.DeriveExtKey();
            var keyPath = new KeyPath($"m/84'/{coinType}'/0'/0/0");
            return masterKey.Derive(keyPath).PrivateKey;
        }
        else if (!string.IsNullOrEmpty(wallet.privateKey))
        {
            try
            {
                BitcoinSecret secret = null;
                try {
                    secret = new BitcoinSecret(wallet.privateKey, network);
                } catch {
                    secret = new BitcoinSecret(wallet.privateKey, isTestNet ? NBitcoin.Network.Main : NBitcoin.Network.TestNet);
                }
                return secret.PrivateKey;
            }
            catch
            {
                var keyBytes = Encoders.Hex.DecodeData(wallet.privateKey);
                return new Key(keyBytes);
            }
        }

        return null;
    }

    /// <summary>
    /// Computes the standard Bitcoin 4-byte (8-hex-character) master key fingerprint.
    /// </summary>
    public static string GetKeyFingerprint(PubKey pubKey)
    {
        if (pubKey == null) return "00000000";
        try
        {
            var hashBytes = pubKey.Hash.ToBytes();
            byte[] fp = new byte[4];
            Array.Copy(hashBytes, 0, fp, 0, 4);
            return Encoders.Hex.EncodeData(fp).ToUpperInvariant();
        }
        catch
        {
            return "00000000";
        }
    }

    /// <summary>
    /// Generates the standard Multisig PubKey Group Index export text.
    /// Contains only public keys and is safe to distribute among cosigners.
    /// </summary>
    public static string GenerateGroupIndexText(MultisigVaultConfig vault)
    {
        if (vault == null) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Multisig Pubkey Group Index");
        sb.AppendLine("# this file contains only public keys and is safe to");
        sb.AppendLine("# distribute among cosigners");
        sb.AppendLine("#");
        sb.AppendLine($"Name: {vault.vaultName}");
        string policyStr = (vault.requiredSignatures == vault.totalKeys)
            ? $"{vault.requiredSignatures} of {vault.totalKeys} (n-of-n)"
            : $"{vault.requiredSignatures} of {vault.totalKeys}";
        sb.AppendLine($"Policy: {policyStr}");
        string deriv = !string.IsNullOrEmpty(vault.derivationPath)
            ? vault.derivationPath 
            : (vault.isTestNet ? "m/48'/1'/0'/2'" : "m/48'/0'/0'/2'");
        sb.AppendLine($"Derivation: {deriv}");
        sb.AppendLine($"Format: {vault.GetAddressType()}");

        if (vault.publicKeysHex != null)
        {
            foreach (var pkHex in vault.publicKeysHex)
            {
                try
                {
                    var pk = new PubKey(pkHex);
                    string fp = GetKeyFingerprint(pk);
                    sb.AppendLine($"[{fp}: {pkHex}]");
                }
                catch
                {
                    sb.AppendLine($"[00000000: {pkHex}]");
                }
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Mathematically validates whether an input passphrase (12/24 words), WIF, or Hex private key
    /// produces the expected Public Key Hex.
    /// </summary>
    public static Key ValidateAndExtractKey(string input, string expectedPubKeyHex, bool isTestNet)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(expectedPubKeyHex))
            return null;

        input = input.Trim();
        expectedPubKeyHex = expectedPubKeyHex.Trim();
        var net = GetNetwork(isTestNet);

        // 1. Try 12 or 24-word Mnemonic
        var words = input.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 12 || words.Length == 24)
        {
            try
            {
                var mnemonic = new Mnemonic(string.Join(" ", words), Wordlist.English);
                var masterKey = mnemonic.DeriveExtKey();
                int coinType = isTestNet ? 1 : 0;

                // Try BIP84 (Segwit standard)
                var k84 = masterKey.Derive(new KeyPath($"m/84'/{coinType}'/0'/0/0")).PrivateKey;
                if (k84.PubKey.ToHex().Equals(expectedPubKeyHex, StringComparison.OrdinalIgnoreCase))
                    return k84;

                // Try BIP44
                var k44 = masterKey.Derive(new KeyPath($"m/44'/{coinType}'/0'/0/0")).PrivateKey;
                if (k44.PubKey.ToHex().Equals(expectedPubKeyHex, StringComparison.OrdinalIgnoreCase))
                    return k44;

                // Try BIP49
                var k49 = masterKey.Derive(new KeyPath($"m/49'/{coinType}'/0'/0/0")).PrivateKey;
                if (k49.PubKey.ToHex().Equals(expectedPubKeyHex, StringComparison.OrdinalIgnoreCase))
                    return k49;

                // Try BIP48 Multisig path (m/48'/coinType'/0'/2'/0/0)
                var k48 = masterKey.Derive(new KeyPath($"m/48'/{coinType}'/0'/2'/0/0")).PrivateKey;
                if (k48.PubKey.ToHex().Equals(expectedPubKeyHex, StringComparison.OrdinalIgnoreCase))
                    return k48;
            }
            catch { }
        }

        // 2. Try WIF private key
        try
        {
            BitcoinSecret secret = null;
            try { secret = new BitcoinSecret(input, net); }
            catch { secret = new BitcoinSecret(input, isTestNet ? Network.Main : Network.TestNet); }
            if (secret != null && secret.PrivateKey.PubKey.ToHex().Equals(expectedPubKeyHex, StringComparison.OrdinalIgnoreCase))
                return secret.PrivateKey;
        }
        catch { }

        // 3. Try Raw 64-char Hex private key
        try
        {
            if (input.Length == 64)
            {
                byte[] keyBytes = Encoders.Hex.DecodeData(input);
                var pk = new Key(keyBytes);
                if (pk.PubKey.ToHex().Equals(expectedPubKeyHex, StringComparison.OrdinalIgnoreCase))
                    return pk;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Executes a Multisig Transaction with strict mathematical quorum enforcement.
    /// Requires at least vault.requiredSignatures distinct co-signing keys.
    /// </summary>
    public static async System.Threading.Tasks.Task<string> SendMultisigTransaction(
        MultisigVaultConfig vault,
        List<Key> signingKeys,
        string toAddress,
        decimal amount,
        decimal fee)
    {
        if (vault == null) throw new ArgumentNullException(nameof(vault));
        if (string.IsNullOrWhiteSpace(toAddress)) throw new Exception("Recipient address is required.");
        if (amount <= 0) throw new Exception("Amount must be greater than zero.");
        if (fee <= 0) throw new Exception("Fee must be greater than zero.");

        // MATHEMATICAL QUORUM ENFORCEMENT:
        var uniqueKeys = new List<Key>();
        var seenPubKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (signingKeys != null)
        {
            foreach (var k in signingKeys)
            {
                if (k != null)
                {
                    string h = k.PubKey.ToHex();
                    if (!seenPubKeys.Contains(h))
                    {
                        seenPubKeys.Add(h);
                        uniqueKeys.Add(k);
                    }
                }
            }
        }

        if (uniqueKeys.Count < vault.requiredSignatures)
        {
            throw new Exception($"Policy violation: Vault requires at least {vault.requiredSignatures} distinct valid co-signer signatures. Provided unique keys: {uniqueKeys.Count}. Spending blocked!");
        }

        var network = GetNetwork(vault.isTestNet);
        var api = new BlockCypherApiCore();

        // 1. Fetch UTXOs for the multisig address
        var utxos = await api.GetUnspentOutputs(vault.multisigAddress, vault.isTestNet);
        if (utxos == null || utxos.Count == 0)
        {
            throw new Exception("No unspent coins (UTXOs) found on this multisig vault address.");
        }

        var redeemScriptBytes = Encoders.Hex.DecodeData(vault.redeemScriptHex);
        var redeemScript = new Script(redeemScriptBytes);

        List<ICoin> scriptCoins = new List<ICoin>();
        Money totalInput = Money.Zero;
        Money sendAmount = Money.Coins(amount);
        Money minerFee = Money.Coins(fee);

        foreach (var u in utxos)
        {
            var outpoint = new OutPoint(new uint256(u.txHash), (uint)u.outputIndex);
            var txOut = new TxOut(Money.Satoshis(u.value), BitcoinAddress.Create(vault.multisigAddress, network).ScriptPubKey);
            var baseCoin = new Coin(outpoint, txOut);
            
            var sc = new ScriptCoin(baseCoin, redeemScript);
            scriptCoins.Add(sc);
            totalInput += Money.Satoshis(u.value);
        }

        if (totalInput < sendAmount + minerFee)
        {
            throw new Exception($"Insufficient funds in vault. Needed: {(sendAmount + minerFee).ToDecimal(MoneyUnit.BTC):F8} BTC, Available: {totalInput.ToDecimal(MoneyUnit.BTC):F8} BTC");
        }

        Money change = totalInput - sendAmount - minerFee;
        var recipientAddr = BitcoinAddress.Create(toAddress, network);
        var vaultAddr = BitcoinAddress.Create(vault.multisigAddress, network);

        // 2. Build and sign transaction with all required keys
        TransactionBuilder builder = network.CreateTransactionBuilder();
        builder.AddCoins(scriptCoins);
        builder.AddKeys(uniqueKeys.ToArray());
        builder.Send(recipientAddr, sendAmount);

        if (change > Money.Satoshis(546))
        {
            builder.SetChange(vaultAddr);
        }
        else if (change > Money.Zero)
        {
            // Absorb dust into miner fee
            minerFee += change;
        }

        builder.SendFees(minerFee);

        Transaction signedTx = builder.BuildTransaction(sign: true);

        // 3. Strict mathematical cryptographic verification:
        bool verified = builder.Verify(signedTx);
        if (!verified)
        {
            throw new Exception($"Cryptographic verification failed! The transaction does not contain the required {vault.requiredSignatures} valid signatures for this multisig script.");
        }

        // 4. Broadcast to Bitcoin network
        string hex = signedTx.ToHex();
        Debug.Log($"[MultisigCore] Broadcasting verified multisig tx: {signedTx.GetHash()} ({hex.Length} hex chars)");
        string txId = await api.BroadcastTransaction(hex, vault.isTestNet);
        return txId;
    }

    // --- STORAGE (Supports Multiple Vaults) ---

    private static string GetVaultListPath(bool isTestNet)
    {
        string suffix = isTestNet ? "testnet" : "mainnet";
        return Path.Combine(Application.persistentDataPath, $"multisig_vaults_{suffix}.json");
    }

    private static string GetLegacySinglePath(bool isTestNet)
    {
        string suffix = isTestNet ? "testnet" : "mainnet";
        return Path.Combine(Application.persistentDataPath, $"multisig_vault_{suffix}.json");
    }

    public static List<MultisigVaultConfig> LoadAllVaults(bool isTestNet)
    {
        string path = GetVaultListPath(isTestNet);
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var wrapper = JsonUtility.FromJson<MultisigVaultList>(json);
                if (wrapper != null && wrapper.vaults != null)
                    return wrapper.vaults;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MultisigCore] Failed to load multiple vaults: {ex.Message}");
            }
        }

        // Migration check: If single vault config exists, import it into the list
        string legacyPath = GetLegacySinglePath(isTestNet);
        if (File.Exists(legacyPath))
        {
            try
            {
                string json = File.ReadAllText(legacyPath);
                var single = JsonUtility.FromJson<MultisigVaultConfig>(json);
                if (single != null && !string.IsNullOrEmpty(single.multisigAddress))
                {
                    if (string.IsNullOrEmpty(single.vaultId)) single.vaultId = System.Guid.NewGuid().ToString();
                    var list = new List<MultisigVaultConfig> { single };
                    SaveAllVaults(list, isTestNet);
                    return list;
                }
            }
            catch { }
        }

        return new List<MultisigVaultConfig>();
    }

    public static void SaveAllVaults(List<MultisigVaultConfig> vaults, bool isTestNet)
    {
        if (vaults == null) vaults = new List<MultisigVaultConfig>();
        string path = GetVaultListPath(isTestNet);
        var wrapper = new MultisigVaultList { vaults = vaults };
        string json = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(path, json);
        Debug.Log($"[MultisigCore] Saved {vaults.Count} vaults to: {path}");
    }

    public static void SaveVault(MultisigVaultConfig config)
    {
        if (config == null) return;
        if (string.IsNullOrEmpty(config.vaultId))
            config.vaultId = System.Guid.NewGuid().ToString();

        var list = LoadAllVaults(config.isTestNet);
        int idx = list.FindIndex(v => v.vaultId == config.vaultId || v.multisigAddress == config.multisigAddress);
        if (idx >= 0)
        {
            list[idx] = config;
        }
        else
        {
            list.Add(config);
        }
        SaveAllVaults(list, config.isTestNet);
    }

    public static void DeleteVault(string vaultId, bool isTestNet)
    {
        if (string.IsNullOrEmpty(vaultId)) return;
        var list = LoadAllVaults(isTestNet);
        list.RemoveAll(v => v.vaultId == vaultId);
        SaveAllVaults(list, isTestNet);
    }

    // Backward-compatibility wrappers
    public static void SaveVaultConfig(MultisigVaultConfig config) => SaveVault(config);

    public static MultisigVaultConfig LoadVaultConfig(bool isTestNet)
    {
        var list = LoadAllVaults(isTestNet);
        return (list != null && list.Count > 0) ? list[0] : null;
    }

    public static void DeleteVaultConfig(bool isTestNet)
    {
        try
        {
            string path = GetVaultListPath(isTestNet);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultisigCore] Failed to delete vault list: {ex.Message}");
        }

        try
        {
            string leg = GetLegacySinglePath(isTestNet);
            if (File.Exists(leg)) File.Delete(leg);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultisigCore] Failed to delete legacy vault: {ex.Message}");
        }
    }
}
