using NBitcoin;
// Remove implicit usage if it causes issues, but normally fine.

public struct WalletKeys
{
    public string address;
    public string privateKey;
}

public class BitcoinWalletCore
{
    private readonly NBitcoin.Network network;
    private readonly ExtKey masterKey;
    private readonly Mnemonic mnemonic;
    private readonly Key importedPrivateKey; // For wallets created from private key
    private readonly bool isImportedFromPrivateKey;

    // Constructor for NEW wallet
    public BitcoinWalletCore(NBitcoin.Network net = null)
    {
        network = net ?? NBitcoin.Network.Main;
        mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        masterKey = mnemonic.DeriveExtKey();
        isImportedFromPrivateKey = false;
    }

    // Constructor for IMPORTED wallet from SEED PHRASE
    public BitcoinWalletCore(string mnemonicPhrase, NBitcoin.Network net = null)
    {
        network = net ?? NBitcoin.Network.Main;
        mnemonic = new Mnemonic(mnemonicPhrase, Wordlist.English);
        masterKey = mnemonic.DeriveExtKey();
        isImportedFromPrivateKey = false;
    }
    
    // Constructor for IMPORTED wallet from PRIVATE KEY (WIF or Hex)
    public BitcoinWalletCore(string privateKeyString, NBitcoin.Network net, bool isHex)
    {
        network = net ?? NBitcoin.Network.Main;
        isImportedFromPrivateKey = true;
        
        try
        {
            if (isHex)
            {
                // Import from hexadecimal private key
                importedPrivateKey = new Key(NBitcoin.DataEncoders.Encoders.Hex.DecodeData(privateKeyString));
            }
            else
            {
                // Import from WIF format
                BitcoinSecret secret = new BitcoinSecret(privateKeyString, network);
                importedPrivateKey = secret.PrivateKey;
            }
        }
        catch (System.Exception ex)
        {
            throw new System.Exception($"Invalid private key format: {ex.Message}");
        }
    }
    
    // --- HELPER METHODS FOR UI ---

    public static string GenerateMnemonic()
    {
        return new Mnemonic(Wordlist.English, WordCount.Twelve).ToString();
    }

    public static WalletKeys GenerateKeysFromSeed(string seedPhrase, bool isTestNet)
    {
        NBitcoin.Network net = isTestNet ? NBitcoin.Network.TestNet : NBitcoin.Network.Main;
        Mnemonic mn = new Mnemonic(seedPhrase, Wordlist.English);
        ExtKey mk = mn.DeriveExtKey();
        
        int coinType = isTestNet ? 1 : 0;
        var keyPath = new KeyPath($"m/44'/{coinType}'/0'/0/0");
        var key = mk.Derive(keyPath).PrivateKey;
        
        return new WalletKeys 
        {
            address = key.PubKey.GetAddress(ScriptPubKeyType.Legacy, net).ToString(),
            privateKey = key.GetBitcoinSecret(net).ToString()
        };
    }

    // --- INSTANCE METHODS ---

    public string GetSeedPhrase()
    {
        return mnemonic.ToString();
    }

    public string GetAddress(int index = 0)
    {
        // Default to Legacy if not specified, or use GetLegacyAddress
        return GetLegacyAddress(index);
    }

    public string GetAddress(int index, ScriptPubKeyType type)
    {
        if (type == ScriptPubKeyType.Segwit) return GetSegWitAddress(index);
        return GetLegacyAddress(index);
    }

    public string GetPrivateKey(int index = 0)
    {
        // If imported from private key, ignore index and return the imported key
        if (isImportedFromPrivateKey)
        {
            return importedPrivateKey.GetBitcoinSecret(network).ToString();
        }
        
        int coinType = (network == NBitcoin.Network.TestNet) ? 1 : 0;
        var keyPath = new KeyPath($"m/44'/{coinType}'/0'/0/{index}");
        return masterKey.Derive(keyPath).PrivateKey.GetBitcoinSecret(network).ToString();
    }
    
    // Get private key in hexadecimal format
    public string GetPrivateKeyHex(int index = 0)
    {
        // If imported from private key, return the imported key in hex
        if (isImportedFromPrivateKey)
        {
            return NBitcoin.DataEncoders.Encoders.Hex.EncodeData(importedPrivateKey.ToBytes());
        }
        
        int coinType = (network == NBitcoin.Network.TestNet) ? 1 : 0;
        var keyPath = new KeyPath($"m/44'/{coinType}'/0'/0/{index}");
        var privateKey = masterKey.Derive(keyPath).PrivateKey;
        return NBitcoin.DataEncoders.Encoders.Hex.EncodeData(privateKey.ToBytes());
    }
    
    // Get SegWit Address (Bech32 format - start with bc1 or tb1)
    public string GetSegWitAddress(int index = 0)
    {
        // If imported from private key, ignore index and return the single address
        if (isImportedFromPrivateKey)
        {
            return importedPrivateKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, network).ToString();
        }
        
        // BIP84 path for SegWit: m/84'/coinType'/0'/0/index
        int coinType = (network == NBitcoin.Network.TestNet) ? 1 : 0;
        var keyPath = new KeyPath($"m/84'/{coinType}'/0'/0/{index}");
        var privateKey = masterKey.Derive(keyPath).PrivateKey;
        return privateKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, network).ToString();
    }
    
    // Get SegWit private key in WIF format (BIP84 path to match SegWit addresses)
    public string GetSegWitPrivateKey(int index = 0)
    {
        if (isImportedFromPrivateKey)
        {
            return importedPrivateKey.GetBitcoinSecret(network).ToString();
        }
        
        int coinType = (network == NBitcoin.Network.TestNet) ? 1 : 0;
        var keyPath = new KeyPath($"m/84'/{coinType}'/0'/0/{index}");
        return masterKey.Derive(keyPath).PrivateKey.GetBitcoinSecret(network).ToString();
    }
    
    // Get SegWit private key in hexadecimal format (BIP84 path to match SegWit addresses)
    public string GetSegWitPrivateKeyHex(int index = 0)
    {
        if (isImportedFromPrivateKey)
        {
            return NBitcoin.DataEncoders.Encoders.Hex.EncodeData(importedPrivateKey.ToBytes());
        }
        
        int coinType = (network == NBitcoin.Network.TestNet) ? 1 : 0;
        var keyPath = new KeyPath($"m/84'/{coinType}'/0'/0/{index}");
        var privateKey = masterKey.Derive(keyPath).PrivateKey;
        return NBitcoin.DataEncoders.Encoders.Hex.EncodeData(privateKey.ToBytes());
    }
    
    // Get Legacy Address (P2PKH format - start with 1 or m/n)
    public string GetLegacyAddress(int index = 0)
    {
        // If imported from private key, ignore index and return the single address
        if (isImportedFromPrivateKey)
        {
            return importedPrivateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, network).ToString();
        }
        
        // BIP44 path for Legacy: m/44'/coinType'/0'/0/index
        int coinType = (network == NBitcoin.Network.TestNet) ? 1 : 0;
        var keyPath = new KeyPath($"m/44'/{coinType}'/0'/0/{index}");
        var privateKey = masterKey.Derive(keyPath).PrivateKey;
        return privateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, network).ToString();
    }
}
