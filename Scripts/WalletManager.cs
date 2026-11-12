using UnityEngine;
using NBitcoin;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnityBitcoinWallet
{
    public class WalletManager : MonoBehaviour
    {
        private BitcoinWallet wallet;
        private BlockCypherApi api;

        void Start()
        {
            api = new BlockCypherApi();
        }

        public void GenerateNewWallet(bool isMainnet = true)
        {
            NBitcoin.Network network = isMainnet ? NBitcoin.Network.Main : NBitcoin.Network.TestNet;
            wallet = new BitcoinWallet(network);
            Debug.Log("Wallet Created!");
        }

        public void RecoverWallet(string seedPhrase, bool isMainnet = true)
        {
            NBitcoin.Network network = isMainnet ? NBitcoin.Network.Main : NBitcoin.Network.TestNet;
            wallet = new BitcoinWallet(seedPhrase, network);
            Debug.Log("Wallet Recovered!");
        }

        public string GetAddress(int index = 0)
        {
            if (wallet == null)
            {
                Debug.LogError("Wallet not initialized!");
                return null;
            }
            return wallet.GetAddress(index);
        }

        public string GetPrivateKey(int index = 0)
        {
            if (wallet == null)
            {
                Debug.LogError("Wallet not initialized!");
                return null;
            }
            return wallet.GetPrivateKey(index).ToString();
        }

        public string GetSeedPhrase()
        {
            if (wallet == null)
            {
                Debug.LogError("Wallet not initialized!");
                return null;
            }
            return wallet.GetSeedPhrase();
        }

        public async Task<decimal> GetBalance(string address)
        {
            var addresses = new List<string> { address };
            return await api.GetWalletBalanceAsync(addresses);
        }
    }
}
