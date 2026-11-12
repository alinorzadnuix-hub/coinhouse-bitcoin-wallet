using NBitcoin;
using System.Threading.Tasks;

namespace UnityBitcoinWallet
{
    public class BitcoinWallet
    {
        private readonly NBitcoin.Network _network;
        private readonly ExtKey _masterKey;
        private readonly Mnemonic _mnemonic;

        public BitcoinWallet(NBitcoin.Network network = null)
        {
            _network = network ?? NBitcoin.Network.Main;
            _mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
            _masterKey = _mnemonic.DeriveExtKey();
        }

        public BitcoinWallet(string mnemonicPhrase, NBitcoin.Network network = null)
        {
            _network = network ?? NBitcoin.Network.Main;
            _mnemonic = new Mnemonic(mnemonicPhrase, Wordlist.English);
            _masterKey = _mnemonic.DeriveExtKey();
        }

        public string GetSeedPhrase()
        {
            return _mnemonic.ToString();
        }

        public string GetAddress(int index = 0)
        {
            var keyPath = new KeyPath($"m/44'/0'/0'/0/{index}");
            var privateKey = _masterKey.Derive(keyPath).PrivateKey;
            return privateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, _network).ToString();
        }

        public BitcoinSecret GetPrivateKey(int index = 0)
        {
            var keyPath = new KeyPath($"m/44'/0'/0'/0/{index}");
            return _masterKey.Derive(keyPath).PrivateKey.GetBitcoinSecret(_network);
        }

        public Transaction BuildTransaction(string destinationAddress, Money amount, int index = 0)
        {
            var privateKey = GetPrivateKey(index);
            var address = BitcoinAddress.Create(destinationAddress, _network);
            var txBuilder = _network.CreateTransactionBuilder();
            var tx = txBuilder
                .AddKeys(privateKey)
                .Send(address, amount)
                .SetChange(privateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, _network))
                .SendFees(Money.Coins(0.0001m))
                .BuildTransaction(sign: true);
            return tx;
        }
    }
}
