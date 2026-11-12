using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using UnityEngine;

/// <summary>
/// Service for handling Bitcoin transactions and address scanning
/// </summary>
public class BitcoinTransactionService
{
    private BlockCypherApiCore apiCore;

    public BitcoinTransactionService()
    {
        apiCore = new BlockCypherApiCore();
    }

    /// <summary>
    /// Scan all addresses derived from seed phrase and return total balance.
    /// Now includes Legacy, SegWit, Nested SegWit (P2SH), and change addresses.
    /// </summary>
    public async Task<ScanResult> ScanAllAddresses(
        string seedPhrase,
        Action<string, int, int> progressCallback = null,
        List<GeneratedAddress> generatedAddresses = null,
        string privateKey = null,
        bool isTestNet = false)  // ✅ FIX: accept isTestNet parameter
    {
        decimal confirmedBalance = 0;
        decimal unconfirmedBalance = 0;

        try
        {
            List<string> addressesToScan = new List<string>();

            // ✅ FIX: Determine network at runtime, not just compile-time
#if TESTNET_BUILD
            NBitcoin.Network network = NBitcoin.Network.TestNet;
            int coinType = 1;
            bool scanTestNet = true;
#else
            NBitcoin.Network network = isTestNet ? NBitcoin.Network.TestNet : NBitcoin.Network.Main;
            int coinType = isTestNet ? 1 : 0;
            bool scanTestNet = isTestNet;
#endif

            if (!string.IsNullOrEmpty(seedPhrase))
            {
                Mnemonic mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
                ExtKey masterKey = mnemonic.DeriveExtKey();

                var relevantPaths = new List<AddressConfig>
                {
                    new AddressConfig { Name = "Legacy",       Path = $"m/44'/{coinType}'/0'/0", ScriptType = ScriptPubKeyType.Legacy },
                    new AddressConfig { Name = "SegWit",       Path = $"m/84'/{coinType}'/0'/0", ScriptType = ScriptPubKeyType.Segwit },
                    new AddressConfig { Name = "P2SH-SegWit",  Path = $"m/49'/{coinType}'/0'/0", ScriptType = ScriptPubKeyType.SegwitP2SH },
                    new AddressConfig { Name = "Legacy Change", Path = $"m/44'/{coinType}'/0'/1", ScriptType = ScriptPubKeyType.Legacy },
                    new AddressConfig { Name = "SegWit Change", Path = $"m/84'/{coinType}'/0'/1", ScriptType = ScriptPubKeyType.Segwit },
                    new AddressConfig { Name = "P2SH Change",   Path = $"m/49'/{coinType}'/0'/1", ScriptType = ScriptPubKeyType.SegwitP2SH }
                };

                foreach (var config in relevantPaths)
                {
                    // ✅ FIX: Scan 20 addresses per path (was 5) to cover all possible UTXOs
                    for (int i = 0; i < 5; i++)
                    {
                        KeyPath keyPath = new KeyPath($"{config.Path}/{i}");
                        BitcoinAddress address = masterKey.Derive(keyPath).PrivateKey.PubKey.GetAddress(config.ScriptType, network);
                        addressesToScan.Add(address.ToString());
                    }
                }
            }
            else if (!string.IsNullOrEmpty(privateKey))
            {
                // For private key imports, derive addresses on the correct network
                try
                {
                    var secret = new BitcoinSecret(privateKey, network);
                    var pKey = secret.PrivateKey;
                    addressesToScan.Add(pKey.PubKey.GetAddress(ScriptPubKeyType.Segwit,    network).ToString());
                    addressesToScan.Add(pKey.PubKey.GetAddress(ScriptPubKeyType.Legacy,    network).ToString());
                    addressesToScan.Add(pKey.PubKey.GetAddress(ScriptPubKeyType.SegwitP2SH, network).ToString());
                }
                catch
                {
                    // Fallback: try parsing as hex raw key
                    var keyBytes = NBitcoin.DataEncoders.Encoders.Hex.DecodeData(privateKey);
                    var pKey = new Key(keyBytes);
                    addressesToScan.Add(pKey.PubKey.GetAddress(ScriptPubKeyType.Segwit,    network).ToString());
                    addressesToScan.Add(pKey.PubKey.GetAddress(ScriptPubKeyType.Legacy,    network).ToString());
                    addressesToScan.Add(pKey.PubKey.GetAddress(ScriptPubKeyType.SegwitP2SH, network).ToString());
                }
            }

            if (generatedAddresses != null)
            {
                foreach (var ga in generatedAddresses)
                {
                    if (!addressesToScan.Contains(ga.address))
                        addressesToScan.Add(ga.address);
                }
            }

            if (addressesToScan.Count > 0)
            {
                progressCallback?.Invoke("Scanning wallet...", 0, 100);
                
                // Call parallel batch UTXO API
                var batchUtxos = await apiCore.GetBatchUnspentOutputs(addressesToScan, scanTestNet);
                
                progressCallback?.Invoke("Calculating balance...", 50, 100);
                
                foreach (var kvp in batchUtxos)
                {
                    string addr = kvp.Key;
                    List<BlockCypherApiCore.UTXO> utxos = kvp.Value;
                    if (utxos != null && utxos.Count > 0)
                    {
                        decimal addrBal = 0;
                        foreach (var u in utxos)
                        {
                            addrBal += (decimal)u.value / 100000000m;
                        }
                        if (addrBal > 0)
                        {
                            confirmedBalance += addrBal;
                            Debug.Log($"[ScanAll] ✅ {addr}: {addrBal:F8}");
                        }
                    }
                }
                
                progressCallback?.Invoke("Sync complete", 100, 100);
                Debug.Log($"[ScanAll] Done. Total: {confirmedBalance:F8} from {addressesToScan.Count} addresses");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"ScanAllAddresses Error: {ex.Message}");
            throw;
        }

        return new ScanResult
        {
            confirmedBalance = confirmedBalance,
            unconfirmedBalance = unconfirmedBalance
        };
    }

    /// <summary>
    /// Send Bitcoin transaction.
    /// Scans Legacy, SegWit, P2SH-SegWit, and change addresses for UTXOs.
    /// </summary>
    private static string CleanBitcoinAddress(string addr)
    {
        if (string.IsNullOrEmpty(addr)) return "";
        string clean = addr.Trim();
        if (clean.StartsWith("bitcoin:", System.StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring("bitcoin:".Length);
        int qIdx = clean.IndexOf('?');
        if (qIdx >= 0)
            clean = clean.Substring(0, qIdx);
        return clean.Trim();
    }

    public async Task<string> SendBitcoin(
        string seedPhrase,
        bool isTestNet,
        string toAddress,
        decimal amount,
        decimal fee,
        Action<string> statusCallback = null,
        CancellationToken cancellationToken = default,
        List<GeneratedAddress> generatedAddresses = null,
        string privateKey = null)
    {
        try
        {
            statusCallback?.Invoke("Preparing transaction...");

            // Validate inputs
            if (string.IsNullOrEmpty(seedPhrase) && string.IsNullOrEmpty(privateKey))
                throw new Exception("Wallet keys not found. Please create or restore a wallet first.");
            string cleanToAddress = CleanBitcoinAddress(toAddress);
            if (string.IsNullOrEmpty(cleanToAddress))
                throw new Exception("Recipient address is required");
            if (amount <= 0)
                throw new Exception("Amount must be greater than 0");
            if (fee <= 0)
                throw new Exception("Fee must be greater than 0");

            cancellationToken.ThrowIfCancellationRequested();

            statusCallback?.Invoke("Deriving keys...");
            
            NBitcoin.Network network = (isTestNet) ? NBitcoin.Network.TestNet : NBitcoin.Network.Main;
            int coinType = isTestNet ? 1 : 0;
            
            Debug.Log($"[SendBitcoin] Network: {(isTestNet ? "TestNet" : "MainNet")}, CoinType: {coinType}");
            
            // Collect all potential addresses and keys
            Dictionary<string, Key> addrToKeyMap = new Dictionary<string, Key>();
            Dictionary<string, ScriptPubKeyType> addrToTypeMap = new Dictionary<string, ScriptPubKeyType>();

            if (!string.IsNullOrEmpty(seedPhrase))
            {
                Mnemonic mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
                ExtKey masterKey = mnemonic.DeriveExtKey();
                
                var paths = new (string path, ScriptPubKeyType type)[] {
                    ($"m/44'/{coinType}'/0'/0", ScriptPubKeyType.Legacy),
                    ($"m/84'/{coinType}'/0'/0", ScriptPubKeyType.Segwit),
                    ($"m/49'/{coinType}'/0'/0", ScriptPubKeyType.SegwitP2SH),
                    ($"m/44'/{coinType}'/0'/1", ScriptPubKeyType.Legacy),
                    ($"m/84'/{coinType}'/0'/1", ScriptPubKeyType.Segwit),
                    ($"m/49'/{coinType}'/0'/1", ScriptPubKeyType.SegwitP2SH)
                };

                foreach(var p in paths) {
                    // ✅ FIX: Scan 20 addresses per path (was 5)
                    for(int i=0; i<5; i++) {
                        Key pk = masterKey.Derive(new KeyPath($"{p.path}/{i}")).PrivateKey;
                        string addr = pk.PubKey.GetAddress(p.type, network).ToString();
                        addrToKeyMap[addr] = pk;
                        addrToTypeMap[addr] = p.type;
                    }
                }

                if (generatedAddresses != null && generatedAddresses.Count > 0) {
                    Debug.Log($"[SendBitcoin] Processing {generatedAddresses.Count} generated addresses:");
                    foreach(var ga in generatedAddresses) {
                        Debug.Log($"[SendBitcoin]   GeneratedAddress: {ga.address} (index {ga.index})");
                        // Deriving a specific index for a specific type
                        // We need to know which type it is. In our wallet, generated addresses are usually SegWit or Legacy.
                        // For simplicity, we can derive both and check which one matches, 
                        // OR we can trust the map since we already derived the first 5 of each.
                        // If index >= 5, we derive it here:
                        if (ga.index >= 5)
                        {
                            Debug.Log($"[SendBitcoin]   Deriving keys for index {ga.index} (>= 5)");
                            // Derive SegWit, Legacy, and P2SH for this index to be safe
                            ScriptPubKeyType[] types = { ScriptPubKeyType.Segwit, ScriptPubKeyType.Legacy, ScriptPubKeyType.SegwitP2SH };
                            string[] basePaths = { $"m/84'/{coinType}'/0'/0", $"m/44'/{coinType}'/0'/0", $"m/49'/{coinType}'/0'/0" };
                            
                            for (int t = 0; t < types.Length; t++)
                            {
                                Key pk = masterKey.Derive(new KeyPath($"{basePaths[t]}/{ga.index}")).PrivateKey;
                                string addr = pk.PubKey.GetAddress(types[t], network).ToString();
                                addrToKeyMap[addr] = pk;
                                addrToTypeMap[addr] = types[t];
                                Debug.Log($"[SendBitcoin]     Derived {types[t]}: {addr}");
                            }
                        }
                        else
                        {
                            Debug.Log($"[SendBitcoin]   Index {ga.index} < 5, already derived");
                        }
                    }
                }
            }
            else if (!string.IsNullOrEmpty(privateKey))
            {
                Key pk = null;
                try
                {
                    BitcoinSecret secret = null;
                    try { secret = new BitcoinSecret(privateKey, network); }
                    catch { secret = new BitcoinSecret(privateKey, isTestNet ? Network.Main : Network.TestNet); }
                    if (secret != null) pk = secret.PrivateKey;
                }
                catch { }

                if (pk == null && privateKey.Trim().Length == 64)
                {
                    try
                    {
                        byte[] keyBytes = NBitcoin.DataEncoders.Encoders.Hex.DecodeData(privateKey.Trim());
                        pk = new Key(keyBytes);
                    }
                    catch { }
                }

                if (pk == null)
                    throw new Exception("Invalid private key format. Must be WIF or 64-char hex.");

                ScriptPubKeyType[] types = { ScriptPubKeyType.Segwit, ScriptPubKeyType.Legacy, ScriptPubKeyType.SegwitP2SH };
                foreach(var t in types) {
                    string addr = pk.PubKey.GetAddress(t, network).ToString();
                    addrToKeyMap[addr] = pk;
                    addrToTypeMap[addr] = t;
                }
            }

            statusCallback?.Invoke("Scanning addresses for funds in parallel...");
            var addresses = new List<string>(addrToKeyMap.Keys);
            
            Debug.Log($"[SendBitcoin] Will scan {addresses.Count} addresses for UTXOs:");
            foreach (var addr in addresses)
            {
                Debug.Log($"[SendBitcoin]   {addr} ({addrToTypeMap[addr]})");
            }

            var availableCoins = new List<(ICoin coin, Key key)>();

            // ✅ Parallelized batch UTXO scanning
            var batchUtxos = await apiCore.GetBatchUnspentOutputs(addresses, isTestNet);

            foreach (var addr in addresses)
            {
                if (batchUtxos.TryGetValue(addr, out var utxos) && utxos != null && utxos.Count > 0)
                {
                    Key pk = addrToKeyMap[addr];
                    ScriptPubKeyType type = addrToTypeMap[addr];
                    Script scriptPubKey = pk.PubKey.GetAddress(type, network).ScriptPubKey;

                    Debug.Log($"[SendBitcoin] Address {addr} ({type}) has {utxos.Count} UTXOs");

                    foreach (var utxo in utxos)
                    {
                        // ✅ CRITICAL FIX: Use ScriptCoin for P2SH-SegWit so NBitcoin can sign correctly
                        // Plain Coin for Legacy/SegWit, ScriptCoin for P2SH-P2WPKH
                        OutPoint outPoint = new OutPoint(new uint256(utxo.txHash), (uint)utxo.outputIndex);
                        TxOut txOut = new TxOut(new Money(utxo.value), scriptPubKey);
                        Coin baseCoin = new Coin(outPoint, txOut);

                        ICoin finalCoin;
                        if (type == ScriptPubKeyType.SegwitP2SH)
                        {
                            // P2SH-P2WPKH: inner redeem script = OP_0 <pubkey-hash>
                            Script redeemScript = pk.PubKey.WitHash.ScriptPubKey;
                            finalCoin = new ScriptCoin(baseCoin, redeemScript);
                            Debug.Log($"[SendBitcoin]   P2SH-SegWit ScriptCoin created for: {addr}");
                        }
                        else
                        {
                            finalCoin = baseCoin;
                        }

                        availableCoins.Add((finalCoin, pk));
                        Debug.Log($"[SendBitcoin]   UTXO: {utxo.value} sat, TX: {utxo.txHash.Substring(0,16)}...");
                    }
                }
            }

            if (availableCoins.Count == 0)
            {
                throw new Exception("No Unspent Outputs (UTXOs) found on any address. Do you have confirmed funds?");
            }

            // Sort coins by amount descending (largest first)
            availableCoins.Sort((a, b) => b.coin.Amount.CompareTo(a.coin.Amount));

            // Log summary of available UTXOs
            Debug.Log($"[SendBitcoin] === UTXO POOL ===");
            Debug.Log($"[SendBitcoin] Total available UTXOs: {availableCoins.Count}");

            // Build & Sign Transaction
            statusCallback?.Invoke("Selecting coins and building transaction...");

            const long DUST_LIMIT = 546L; // satoshis — standard dust threshold

            Money sendAmount = Money.Coins(amount);
            Money minerFee   = Money.Coins(fee);

            // Minimum relay fee floor (at least 1200 sats / 2 sat/vB for safe relay)
            long minRelayFeeSat = Math.Max(1200L, (long)(200 * 2.0));
            if (minerFee.Satoshi < minRelayFeeSat)
            {
                minerFee = Money.Satoshis(minRelayFeeSat);
            }

            List<ICoin> selectedCoins = new List<ICoin>();
            List<Key> selectedKeys = new List<Key>();
            Money selectedSum = Money.Zero;

            for (int i = 0; i < availableCoins.Count; i++)
            {
                var item = availableCoins[i];
                selectedCoins.Add(item.coin);
                if (!selectedKeys.Contains(item.key)) selectedKeys.Add(item.key);
                selectedSum += (Money)item.coin.Amount;

                // Dynamically ensure fee meets minimum relay rate: 2 sat/vB
                int estimatedVBytes = (selectedCoins.Count * 68) + (2 * 31) + 11;
                long requiredFeeSat = Math.Max(1200L, (long)(estimatedVBytes * 2.0));
                if (minerFee.Satoshi < requiredFeeSat)
                {
                    minerFee = Money.Satoshis(requiredFeeSat);
                }

                if (selectedSum >= sendAmount + minerFee)
                {
                    break;
                }
            }

            if (selectedSum < sendAmount + minerFee)
            {
                throw new Exception($"Insufficient funds. Need {(sendAmount + minerFee).ToDecimal(MoneyUnit.BTC):F8} BTC (including network fee), but available balance is {selectedSum.ToDecimal(MoneyUnit.BTC):F8} BTC");
            }

            // Calculate change; absorb dust into fee
            Money change = selectedSum - sendAmount - minerFee;
            if (change > Money.Zero && change.Satoshi < DUST_LIMIT)
            {
                long dustSat = change.Satoshi;
                minerFee += change;
                change = Money.Zero;
                statusCallback?.Invoke($"Note: {dustSat} sat change absorbed into fee (below dust limit).");
            }

            BitcoinAddress recipientAddr = BitcoinAddress.Create(cleanToAddress, network);
            BitcoinAddress changeAddress = selectedKeys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, network);

            // Build transaction manually to have full control over outputs
            Transaction tx = network.CreateTransaction();
            foreach (var coin in selectedCoins)
            {
                var input = tx.Inputs.Add(coin.Outpoint);
                input.Sequence = Sequence.Final;
            }

            // Recipient output
            tx.Outputs.Add(sendAmount, recipientAddr);

            // Change output only if above dust limit
            if (change.Satoshi >= DUST_LIMIT)
                tx.Outputs.Add(change, changeAddress);

            Debug.Log($"[SendBitcoin] Spending {selectedCoins.Count} coins with {selectedKeys.Count} keys, sending to {toAddress}");

            // Sign all inputs using the selected coin + key list
            TransactionBuilder sigBuilder = network.CreateTransactionBuilder();
            Transaction signedTx = sigBuilder
                .AddCoins(selectedCoins)
                .AddKeys(selectedKeys.ToArray())
                .SignTransaction(tx);

                        // Final verification
            TransactionBuilder verifyBuilder = network.CreateTransactionBuilder();
            verifyBuilder.AddCoins(selectedCoins).AddKeys(selectedKeys.ToArray());
            bool isValid = verifyBuilder.Verify(signedTx);
            if (!isValid)
            {
                Debug.LogError($"[SendBitcoin] Verification failed! Coins: {selectedCoins.Count}, Keys: {selectedKeys.Count}");
                foreach (var coin in selectedCoins)
                {
                    Debug.LogError($"  Coin: {coin.Outpoint} - Script: {coin.TxOut.ScriptPubKey}");
                }
                throw new Exception("Transaction signing failed! Keys don't match UTXOs. Please check your wallet has the correct keys for these funds.");
            }
            
            string hex = signedTx.ToHex();
            
            cancellationToken.ThrowIfCancellationRequested();
            
            // Broadcast
            statusCallback?.Invoke("Broadcasting to network...");
            string txId = await apiCore.BroadcastTransaction(hex, isTestNet);
            
            return txId;
        }
        catch (OperationCanceledException)
        {
            Debug.Log("Transaction cancelled by user");
            throw new Exception("Transaction cancelled");
        }
        catch (Exception ex)
        {
            Debug.LogError($"SendBitcoin Error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Result of a timelock transaction creation
    /// </summary>
    public class TimeLockResult
    {
        public string RedeemScriptHex;
        public string TxId;
        public string P2shAddress;
        public decimal Amount;
        public int OutputIndex; // Usually 0 for the first output
        public string LockType;  // "Absolute" or "Relative"
        public string LockValue; // minutes or blocks
        public string ToAddress; // recipient address
    }

    /// <summary>
    /// Creates a BIP-0065 CLTV Time-Locked transaction
    /// </summary>
    public async Task<TimeLockResult> CreateTimeLockTransaction(
        string seedPhrase, string privateKey, bool isTestNet,
        string toAddress, decimal amount, int lockMinutes, decimal fee,
        Action<string> statusCallback = null,
        List<GeneratedAddress> generatedAddresses = null)
    {
        NBitcoin.Network network = isTestNet ? NBitcoin.Network.TestNet : NBitcoin.Network.Main;

        // Validate keys first
        if (string.IsNullOrEmpty(seedPhrase) && string.IsNullOrEmpty(privateKey))
            throw new Exception("Wallet keys not found. Please create or restore a wallet first.");

        // Parse recipient address and extract key hash bytes (supports Legacy, SegWit bech32, P2SH)
        byte[] keyIdBytes = ExtractKeyHashFromAddress(toAddress, network);

        // Calculate lock time (Unix timestamp)
        uint lockTime = (uint)DateTimeOffset.UtcNow.AddMinutes(lockMinutes).ToUnixTimeSeconds();

        Script redeemScript = new Script(
            Op.GetPushOp(lockTime),
            OpcodeType.OP_CHECKLOCKTIMEVERIFY,
            OpcodeType.OP_DROP,
            OpcodeType.OP_DUP,
            OpcodeType.OP_HASH160,
            Op.GetPushOp(keyIdBytes),
            OpcodeType.OP_EQUALVERIFY,
            OpcodeType.OP_CHECKSIG
        );

        // Generate P2SH address for this script
        BitcoinAddress p2sh = redeemScript.Hash.GetAddress(network);

        statusCallback?.Invoke($"Sending to TimeLock P2SH: {p2sh.ToString()}");

        // Send funds to this P2SH address (pass generatedAddresses for UTXO scanning)
        string txId = await SendBitcoin(seedPhrase, isTestNet, p2sh.ToString(), amount, fee, statusCallback, default, generatedAddresses, privateKey);

        // Return all the information needed for redemption
        return new TimeLockResult
        {
            RedeemScriptHex = redeemScript.ToHex(),
            TxId = txId,
            P2shAddress = p2sh.ToString(),
            Amount = amount,
            OutputIndex = 0, // Typically the first output in the funding tx
            LockType = "Absolute",
            LockValue = lockMinutes + " min",
            ToAddress = toAddress
        };
    }

    /// <summary>
    /// Creates a BIP-0112 CSV Relative Time-Locked transaction
    /// </summary>
    public async Task<TimeLockResult> CreateRelativeTimeLockTransaction(
        string seedPhrase, string privateKey, bool isTestNet,
        string toAddress, decimal amount, int lockBlocks, decimal fee,
        Action<string> statusCallback = null,
        List<GeneratedAddress> generatedAddresses = null)
    {
        NBitcoin.Network network = isTestNet ? NBitcoin.Network.TestNet : NBitcoin.Network.Main;

        // Validate keys first
        if (string.IsNullOrEmpty(seedPhrase) && string.IsNullOrEmpty(privateKey))
            throw new Exception("Wallet keys not found. Please create or restore a wallet first.");

        // Parse recipient address and extract key hash bytes (supports Legacy, SegWit bech32, P2SH)
        byte[] keyIdBytes = ExtractKeyHashFromAddress(toAddress, network);

        // The lock value is simply the number of blocks (from 1 to 65535, per BIP 68)
        if (lockBlocks < 1 || lockBlocks > 65535)
            throw new Exception("Lock blocks must be between 1 and 65535");

        uint sequenceBlocks = (uint)lockBlocks;

        Script redeemScript = new Script(
            Op.GetPushOp(sequenceBlocks),
            OpcodeType.OP_CHECKSEQUENCEVERIFY,
            OpcodeType.OP_DROP,
            OpcodeType.OP_DUP,
            OpcodeType.OP_HASH160,
            Op.GetPushOp(keyIdBytes),
            OpcodeType.OP_EQUALVERIFY,
            OpcodeType.OP_CHECKSIG
        );

        // Generate P2SH address for this script
        BitcoinAddress p2sh = redeemScript.Hash.GetAddress(network);

        statusCallback?.Invoke($"Sending to Relative TimeLock P2SH: {p2sh.ToString()}");

        // Send funds to this P2SH address (pass generatedAddresses for UTXO scanning)
        string txId = await SendBitcoin(seedPhrase, isTestNet, p2sh.ToString(), amount, fee, statusCallback, default, generatedAddresses, privateKey);

        // Return all the information needed for redemption
        return new TimeLockResult
        {
            RedeemScriptHex = redeemScript.ToHex(),
            TxId = txId,
            P2shAddress = p2sh.ToString(),
            Amount = amount,
            OutputIndex = 0, // Typically the first output in the funding tx
            LockType = "Relative",
            LockValue = lockBlocks + " blocks",
            ToAddress = toAddress
        };
    }

    /// <summary>
    /// Redeems (spends) a previously time-locked output.
    /// Caller provides the redeem script hex (returned when creating the lock),
    /// the funding TXID and output index, the funding amount (in BTC),
    /// and an absolute/relative flag matching the original lock.
    /// </summary>
    public async Task<string> RedeemTimeLockTransaction(
        string seedPhrase, string privateKey, bool isTestNet,
        string redeemScriptHex, string fundingTxId, int outputIndex,
        decimal fundingAmountBtc, string toAddress, decimal fee,
        bool isAbsoluteLock, Action<string> statusCallback = null)
    {
        NBitcoin.Network network = isTestNet ? NBitcoin.Network.TestNet : NBitcoin.Network.Main;

        // Validate keys first
        if (string.IsNullOrEmpty(seedPhrase) && string.IsNullOrEmpty(privateKey))
            throw new Exception("Wallet keys not found. Please create or restore a wallet first.");

        if (string.IsNullOrEmpty(redeemScriptHex)) throw new Exception("Redeem script is required.");
        if (string.IsNullOrEmpty(fundingTxId))     throw new Exception("Funding TX ID is required.");
        if (string.IsNullOrEmpty(toAddress))       throw new Exception("Recipient address is required.");
        if (fundingAmountBtc <= 0)                 throw new Exception("Funding amount must be > 0.");
        if (fee < 0)                               throw new Exception("Fee cannot be negative.");
        if (fundingAmountBtc <= fee)               throw new Exception("Amount must be greater than fee.");

        // 1. Parse the redeem script
        Script redeemScript = new Script(NBitcoin.DataEncoders.Encoders.Hex.DecodeData(redeemScriptHex));
        var ops = redeemScript.ToOps();
        var opList = new List<Op>(ops);
        if (opList.Count < 8)
            throw new Exception("Redeem script does not look like a valid timelock script.");

        // 2. Extract lock value (first push) and target keyHash (push after OP_HASH160)
        long lockValue;
        if (opList[0].PushData != null && opList[0].PushData.Length > 0)
        {
            byte[] b = opList[0].PushData;
            long val = 0;
            for (int i = b.Length - 1; i >= 0; i--) val = (val << 8) | b[i];
            lockValue = val;
            Debug.Log($"[Redeem] Lock value extracted: {lockValue}");
        }
        else
        {
            throw new Exception("Cannot parse lock value from redeem script.");
        }

        // Dynamically find the 20-byte keyHash by scanning for OP_HASH160 (0xa9)
        byte[] targetKeyHash = null;
        for (int i = 0; i < opList.Count - 1; i++)
        {
            if (opList[i].Code == OpcodeType.OP_HASH160 && opList[i + 1].PushData?.Length == 20)
            {
                targetKeyHash = opList[i + 1].PushData;
                Debug.Log($"[Redeem] Found keyHash at op index {i + 1}: {BitConverter.ToString(targetKeyHash).Replace("-", "")}");
                break;
            }
        }
        if (targetKeyHash == null || targetKeyHash.Length != 20)
            throw new Exception("Cannot parse pubkey hash from redeem script. Ensure the Redeem Script is correct and unmodified.");

        // Self-Healing: Query transaction output P2SH address from Mempool API and verify
        statusCallback?.Invoke("Verifying target output with blockchain...");
        TxApiInfo apiInfo = null;
        try
        {
            apiInfo = await FetchTxInfoFromApi(fundingTxId, outputIndex, isTestNet);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Redeem] Failed to fetch TX from API: {ex.Message}");
        }

        byte[] targetScriptHash = redeemScript.Hash.ToBytes();
        if (apiInfo != null && !string.IsNullOrEmpty(apiInfo.scriptHashHex))
        {
            byte[] apiHash = NBitcoin.DataEncoders.Encoders.Hex.DecodeData(apiInfo.scriptHashHex);
            if (!BytesEqual(targetScriptHash, apiHash))
            {
                statusCallback?.Invoke("Redeem script mismatch detected. Auto-correcting locktime...");
                bool foundIsAbsolute = false;
                long correctedLock = await Task.Run(() => BruteForceCorrectLockTime(apiHash, targetKeyHash, apiInfo.blockTime, out foundIsAbsolute));

                if (correctedLock != -1)
                {
                    isAbsoluteLock = foundIsAbsolute;
                    if (isAbsoluteLock)
                    {
                        redeemScript = new Script(
                            Op.GetPushOp(correctedLock),
                            OpcodeType.OP_CHECKLOCKTIMEVERIFY,
                            OpcodeType.OP_DROP,
                            OpcodeType.OP_DUP,
                            OpcodeType.OP_HASH160,
                            Op.GetPushOp(targetKeyHash),
                            OpcodeType.OP_EQUALVERIFY,
                            OpcodeType.OP_CHECKSIG
                        );
                    }
                    else
                    {
                        redeemScript = new Script(
                            Op.GetPushOp(correctedLock),
                            OpcodeType.OP_CHECKSEQUENCEVERIFY,
                            OpcodeType.OP_DROP,
                            OpcodeType.OP_DUP,
                            OpcodeType.OP_HASH160,
                            Op.GetPushOp(targetKeyHash),
                            OpcodeType.OP_EQUALVERIFY,
                            OpcodeType.OP_CHECKSIG
                        );
                    }
                    targetScriptHash = redeemScript.Hash.ToBytes();
                    lockValue = correctedLock;
                    statusCallback?.Invoke($"Auto-corrected! Locktime found: {correctedLock}. Rebuilt script: {redeemScript.ToHex()}");
                }
                else
                {
                    statusCallback?.Invoke("Auto-correction failed. Locktime not found. Proceeding anyway...");
                }
            }
        }

        // 3. Resolve the matching private key
        Key spendKey = null;

        // Try user's manual WIF private key first (could also be a mnemonic)
        if (!string.IsNullOrEmpty(privateKey))
        {
            string trimmedKey = privateKey.Trim();
            // Check if user entered a mnemonic instead of WIF key in the field
            if (trimmedKey.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length >= 12)
            {
                try
                {
                    var mn = new Mnemonic(trimmedKey, Wordlist.English);
                    var mk = mn.DeriveExtKey();
                    var pathRoots = new[] {
                        "m/84'/1'/0'/0",
                        "m/84'/1'/0'/1",
                        "m/84'/0'/0'/0",
                        "m/84'/0'/0'/1",
                        "m/44'/1'/0'/0",
                        "m/44'/1'/0'/1",
                        "m/44'/0'/0'/0",
                        "m/44'/0'/0'/1",
                        "m/49'/1'/0'/0",
                        "m/49'/1'/0'/1",
                        "m/49'/0'/0'/0",
                        "m/49'/0'/0'/1"
                    };
                    foreach (var root in pathRoots)
                    {
                        for (int i = 0; i < 100 && spendKey == null; i++)
                        {
                            var k = mk.Derive(new KeyPath($"{root}/{i}")).PrivateKey;
                            if (BytesEqual(k.PubKey.Hash.ToBytes(), targetKeyHash))
                            {
                                spendKey = k;
                                Debug.Log($"[Redeem] Found spend key in manual mnemonic at path {root}/{i}");
                            }
                        }
                        if (spendKey != null) break;
                    }
                }
                catch { /* ignore */ }
            }
            else
            {
                // Try standard private key parsers
                Key parsedKey = null;
                try { parsedKey = new BitcoinSecret(trimmedKey, network).PrivateKey; }
                catch
                {
                    try { parsedKey = new BitcoinSecret(trimmedKey, NBitcoin.Network.Main).PrivateKey; }
                    catch
                    {
                        try { parsedKey = new BitcoinSecret(trimmedKey, NBitcoin.Network.TestNet).PrivateKey; }
                        catch
                        {
                            try
                            {
                                var bytes = NBitcoin.DataEncoders.Encoders.Hex.DecodeData(trimmedKey);
                                parsedKey = new Key(bytes);
                            }
                            catch { /* ignore */ }
                        }
                    }
                }

                if (parsedKey != null)
                {
                    if (BytesEqual(parsedKey.PubKey.Hash.ToBytes(), targetKeyHash))
                    {
                        spendKey = parsedKey;
                        Debug.Log("[Redeem] Provided private key matches targetKeyHash.");
                    }
                    else
                    {
                        Debug.LogWarning("[Redeem] Provided private key does NOT match targetKeyHash. Ignoring it to try other options.");
                    }
                }
            }
        }

        // Try wallet seed phrase next if key not resolved yet
        if (spendKey == null && !string.IsNullOrEmpty(seedPhrase))
        {
            try
            {
                var mn = new Mnemonic(seedPhrase, Wordlist.English);
                var mk = mn.DeriveExtKey();
                var pathRoots = new[] {
                    "m/84'/1'/0'/0",
                    "m/84'/1'/0'/1",
                    "m/84'/0'/0'/0",
                    "m/84'/0'/0'/1",
                    "m/44'/1'/0'/0",
                    "m/44'/1'/0'/1",
                    "m/44'/0'/0'/0",
                    "m/44'/0'/0'/1",
                    "m/49'/1'/0'/0",
                    "m/49'/1'/0'/1",
                    "m/49'/0'/0'/0",
                    "m/49'/0'/0'/1"
                };
                foreach (var root in pathRoots)
                {
                    for (int i = 0; i < 100 && spendKey == null; i++)
                    {
                        var k = mk.Derive(new KeyPath($"{root}/{i}")).PrivateKey;
                        if (BytesEqual(k.PubKey.Hash.ToBytes(), targetKeyHash))
                        {
                            spendKey = k;
                            Debug.Log($"[Redeem] Found spend key in wallet seed at path {root}/{i}");
                        }
                    }
                    if (spendKey != null) break;
                }
            }
            catch { /* ignore */ }
        }

        if (spendKey == null)
            throw new Exception("Could not derive a private key matching this lock. Make sure you are using the same wallet that created the lock, or enter the WIF private key manually.");

        // 4. Build spending transaction (P2SH-CLTV)
        // Our app's CreateTimeLockTransaction always wraps in P2SH.
        // The 'Script' field in the UI is the REDEEM SCRIPT, not the raw scriptPubKey.
        // Therefore we always use P2SH mode: scriptPubKey = OP_HASH160 <hash> OP_EQUAL
        Script p2shScriptPubKey = redeemScript.Hash.ScriptPubKey;

        Money fundingMoney = Money.Coins(fundingAmountBtc);
        Money sendMoney    = Money.Coins(fundingAmountBtc - fee);

        Debug.Log($"[Redeem] P2SH mode — scriptPubKey={p2shScriptPubKey}");
        Debug.Log($"[Redeem] Building TX: {fundingTxId}:{outputIndex} amount={fundingAmountBtc:F8} fee={fee:F8} to={toAddress}");

        Transaction tx = network.CreateTransaction();
        tx.Version = 2u; // BIP68 requires version 2

        OutPoint prevOut = new OutPoint(uint256.Parse(fundingTxId), outputIndex);
        TxIn input = new TxIn(prevOut);
        
        if (isAbsoluteLock)
        {
            tx.LockTime = new LockTime((uint)lockValue);
            input.Sequence = new Sequence(0xFFFFFFFEu); // Enable absolute locktime
        }
        else
        {
            input.Sequence = new Sequence((uint)lockValue); // Enable relative locktime
        }
        tx.Inputs.Add(input);

        BitcoinAddress recipient = BitcoinAddress.Create(toAddress, network);
        tx.Outputs.Add(new TxOut(sendMoney, recipient.ScriptPubKey));

        // Sign: P2SH legacy sighash (BIP16)
        TxOut spentOutput = new TxOut(fundingMoney, p2shScriptPubKey);
        uint256 sigHash = tx.GetSignatureHash(
            redeemScript, 0, SigHash.All, spentOutput, HashVersion.Original);
        var ecdsaSig = spendKey.Sign(sigHash);
        var txSig    = new TransactionSignature(ecdsaSig, SigHash.All);

        // scriptSig for P2SH-CLTV: <sig> <pubkey> <redeemScript>
        tx.Inputs[0].ScriptSig = new Script(
            Op.GetPushOp(txSig.ToBytes()),
            Op.GetPushOp(spendKey.PubKey.ToBytes()),
            Op.GetPushOp(redeemScript.ToBytes())
        );
        Debug.Log($"[Redeem] scriptSig built: sig+pubkey+redeemScript");

        string txHex = tx.ToHex();
        Debug.Log($"[Redeem] TX hex len={txHex.Length} hash={tx.GetHash()}");
        statusCallback?.Invoke($"Broadcasting ({tx.GetHash().ToString().Substring(0, 8)}...)...");
        string txId = await apiCore.BroadcastTransaction(txHex, isTestNet);
        Debug.Log($"[Redeem] ✅ Broadcast OK: {txId}");
        return txId;
    }

    public class TxApiInfo
    {
        public string scriptHashHex;
        public long blockTime;
    }

    private async Task<TxApiInfo> FetchTxInfoFromApi(string txid, int outputIndex, bool isTestNet)
    {
        string baseUrl = isTestNet ? "https://mempool.space/testnet/api" : "https://mempool.space/api";
        string url = $"{baseUrl}/tx/{txid}";
        using (var client = new System.Net.Http.HttpClient())
        {
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.Timeout = TimeSpan.FromSeconds(15);
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var txObj = Newtonsoft.Json.Linq.JObject.Parse(json);
                
                var info = new TxApiInfo();
                var statusObj = txObj["status"];
                if (statusObj != null && statusObj["confirmed"] != null && (bool)statusObj["confirmed"])
                {
                    info.blockTime = (long?)statusObj["block_time"] ?? 0;
                }
                
                var vouts = txObj["vout"] as Newtonsoft.Json.Linq.JArray;
                if (vouts != null && outputIndex < vouts.Count)
                {
                    var voutObj = vouts[outputIndex];
                    string scriptpubkey = voutObj["scriptpubkey"]?.ToString();
                    if (!string.IsNullOrEmpty(scriptpubkey) && scriptpubkey.StartsWith("a914") && scriptpubkey.EndsWith("87"))
                    {
                        info.scriptHashHex = scriptpubkey.Substring(4, 40);
                    }
                }
                return info;
            }
        }
        return null;
    }

    private long BruteForceCorrectLockTime(byte[] targetScriptHash, byte[] targetKeyHash, long hintBlockTime, out bool outIsAbsolute)
    {
        // Try Absolute Lock (OP_CLTV)
        // 1. Search Block Heights (1 to 2,500,000)
        byte[] bhScript = new byte[31];
        bhScript[0] = 0x03; // push 3 bytes
        bhScript[4] = 0xb1; // OP_CLTV
        bhScript[5] = 0x75; // OP_DROP
        bhScript[6] = 0x76; // OP_DUP
        bhScript[7] = 0xa9; // OP_HASH160
        bhScript[8] = 0x14; // push 20
        Array.Copy(targetKeyHash, 0, bhScript, 9, 20);
        bhScript[29] = 0x88; // OP_EQUALVERIFY
        bhScript[30] = 0xac; // OP_CHECKSIG

        for (int h = 1; h <= 2500000; h++)
        {
            bhScript[1] = (byte)(h & 0xff);
            bhScript[2] = (byte)((h >> 8) & 0xff);
            bhScript[3] = (byte)((h >> 16) & 0xff);

            byte[] computed = NBitcoin.Crypto.Hashes.Hash160(bhScript).ToBytes();
            if (BytesEqual(computed, targetScriptHash))
            {
                outIsAbsolute = true;
                return h;
            }
        }

        // 2. Search Timestamps (Hint-Guided)
        byte[] tsScript = new byte[32];
        tsScript[0] = 0x04; // push 4 bytes
        tsScript[5] = 0xb1; // OP_CLTV
        tsScript[6] = 0x75; // OP_DROP
        tsScript[7] = 0x76; // OP_DUP
        tsScript[8] = 0xa9; // OP_HASH160
        tsScript[9] = 0x14; // push 20
        Array.Copy(targetKeyHash, 0, tsScript, 10, 20);
        tsScript[30] = 0x88; // OP_EQUALVERIFY
        tsScript[31] = 0xac; // OP_CHECKSIG

        long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long start = nowSec - 86400 * 2; // 2 days ago
        long end = nowSec + 86400 * 30;  // 30 days from now
        if (hintBlockTime > 1600000000)
        {
            start = hintBlockTime - 86400; // 1 day before confirmation
            end = hintBlockTime + 86400 * 30; // up to 30 days after confirmation (fast & safe)
        }

        for (long t = start; t <= end; t++)
        {
            tsScript[1] = (byte)(t & 0xff);
            tsScript[2] = (byte)((t >> 8) & 0xff);
            tsScript[3] = (byte)((t >> 16) & 0xff);
            tsScript[4] = (byte)((t >> 24) & 0xff);

            byte[] computed = NBitcoin.Crypto.Hashes.Hash160(tsScript).ToBytes();
            if (BytesEqual(computed, targetScriptHash))
            {
                outIsAbsolute = true;
                return t;
            }
        }

        // Try Relative Lock (OP_CSV)
        // 3. Search Relative Sequences (1 to 65,535)
        for (int seq = 1; seq <= 65535; seq++)
        {
            byte[] seqBytes;
            if (seq <= 16)
            {
                byte op = (byte)(0x50 + seq);
                seqBytes = new byte[28];
                seqBytes[0] = op;
                seqBytes[1] = 0xb2; // OP_CSV
                seqBytes[2] = 0x75; // OP_DROP
                seqBytes[3] = 0x76; // OP_DUP
                seqBytes[4] = 0xa9; // OP_HASH160
                seqBytes[5] = 0x14; // push 20
                Array.Copy(targetKeyHash, 0, seqBytes, 6, 20);
                seqBytes[26] = 0x88; // OP_EQUALVERIFY
                seqBytes[27] = 0xac; // OP_CHECKSIG
            }
            else if (seq <= 255)
            {
                seqBytes = new byte[29];
                seqBytes[0] = 0x01;
                seqBytes[1] = (byte)seq;
                seqBytes[2] = 0xb2; // OP_CSV
                seqBytes[3] = 0x75; // OP_DROP
                seqBytes[4] = 0x76; // OP_DUP
                seqBytes[5] = 0xa9; // OP_HASH160
                seqBytes[6] = 0x14; // push 20
                Array.Copy(targetKeyHash, 0, seqBytes, 7, 20);
                seqBytes[27] = 0x88; // OP_EQUALVERIFY
                seqBytes[28] = 0xac; // OP_CHECKSIG
            }
            else
            {
                seqBytes = new byte[30];
                seqBytes[0] = 0x02;
                seqBytes[1] = (byte)(seq & 0xff);
                seqBytes[2] = (byte)((seq >> 8) & 0xff);
                seqBytes[3] = 0xb2; // OP_CSV
                seqBytes[4] = 0x75; // OP_DROP
                seqBytes[5] = 0x76; // OP_DUP
                seqBytes[6] = 0xa9; // OP_HASH160
                seqBytes[7] = 0x14; // push 20
                Array.Copy(targetKeyHash, 0, seqBytes, 8, 20);
                seqBytes[28] = 0x88; // OP_EQUALVERIFY
                seqBytes[29] = 0xac; // OP_CHECKSIG
            }

            byte[] computed = NBitcoin.Crypto.Hashes.Hash160(seqBytes).ToBytes();
            if (BytesEqual(computed, targetScriptHash))
            {
                outIsAbsolute = false;
                return seq;
            }
        }

        outIsAbsolute = true;
        return -1;
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>
    /// Extracts the 20-byte key hash from any Bitcoin address type:
    /// Legacy (P2PKH, starts with 1/m), SegWit bech32 (P2WPKH, starts with bc1/tb1), P2SH (starts with 3/2)
    /// </summary>
    private byte[] ExtractKeyHashFromAddress(string addressStr, NBitcoin.Network network)
    {
        try
        {
            BitcoinAddress addr = BitcoinAddress.Create(addressStr, network);
            
            // P2PKH (Legacy: 1... / m... on testnet)
            if (addr is BitcoinPubKeyAddress pkAddr)
                return pkAddr.Hash.ToBytes();
            
            // P2WPKH (Native SegWit bech32: bc1q... / tb1q...)
            if (addr is BitcoinWitPubKeyAddress wpkAddr)
                return wpkAddr.Hash.ToBytes();
            
            // P2SH (3... / 2... on testnet) — extract script hash
            if (addr is BitcoinScriptAddress scriptAddr)
                return scriptAddr.Hash.ToBytes();

            throw new Exception($"Unsupported address type: {addr.GetType().Name}. Use Legacy (1...), SegWit (bc1q...), or P2SH (3...) address.");
        }
        catch (Exception ex) when (!(ex.Message.StartsWith("Unsupported")))
        {
            throw new Exception($"Invalid Bitcoin address '{addressStr}' for {(network == NBitcoin.Network.Main ? "Mainnet" : "Testnet")}: {ex.Message}");
        }
    }

    /// <summary>
    /// Get transaction history for an address
    /// </summary>
    public async Task<List<TransactionHistory>> GetTransactionHistory(string address, bool isTestNet = false)
    {
        var history = new List<TransactionHistory>();

        try
        {
            // ✅ FIX: Pass isTestNet so history is fetched from the correct network
            var txs = await apiCore.GetAllTransactions(address, isTestNet);

            foreach (var tx in txs)
            {
                history.Add(new TransactionHistory
                {
                    type = tx.type,
                    address = tx.otherAddress,
                    amount = tx.amount,
                    date = tx.date,
                    txHash = tx.txHash
                });
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"GetTransactionHistory Error: {ex.Message}");
        }

        return history;
    }
}

/// <summary>
/// Result of address scanning
/// </summary>
public class ScanResult
{
    public decimal confirmedBalance;
    public decimal unconfirmedBalance;
}
