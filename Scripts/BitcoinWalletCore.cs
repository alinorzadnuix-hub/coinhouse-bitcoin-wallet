using NBitcoin;

public class BitcoinWalletCore
{
    private readonly NBitcoin.Network network;
    private readonly ExtKey masterKey;
    private readonly Mnemonic mnemonic;

    public BitcoinWalletCore(NBitcoin.Network net = null)
    {
        network = net ?? NBitcoin.Network.Main;
        mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        masterKey = mnemonic.DeriveExtKey();
    }

    public BitcoinWalletCore(string mnemonicPhrase, NBitcoin.Network net = null)
    {
        network = net ?? NBitcoin.Network.Main;
        mnemonic = new Mnemonic(mnemonicPhrase, Wordlist.English);
        masterKey = mnemonic.DeriveExtKey();
    }

    public string GetSeedPhrase()
    {
        return mnemonic.ToString();
    }

    public string GetAddress(int index = 0)
    {
        var keyPath = new KeyPath($"m/44'/0'/0'/0/{index}");
        var privateKey = masterKey.Derive(keyPath).PrivateKey;
        return privateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, network).ToString();
    }

    public string GetPrivateKey(int index = 0)
    {
        var keyPath = new KeyPath($"m/44'/0'/0'/0/{index}");
        return masterKey.Derive(keyPath).PrivateKey.GetBitcoinSecret(network).ToString();
    }
}
