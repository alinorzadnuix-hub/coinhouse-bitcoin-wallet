using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using UnityEngine;
using NBitcoinNetwork = NBitcoin.Network;

namespace BitcoinSDK.Core
{
    /// <summary>
    /// Main Bitcoin Wallet SDK for Unity
    /// Supports MainNet and TestNet
    /// </summary>
    public class BitcoinWalletSDK
    {
        private BitcoinSDK.API.BlockchainAPI blockchainAPI;
        private NBitcoinNetwork currentNetwork;
        
        public enum NetworkType { MainNet, TestNet }
        
        /// <summary>
        /// Initialize SDK with network type
        /// </summary>
        public BitcoinWalletSDK(NetworkType networkType = NetworkType.MainNet)
        {
            currentNetwork = networkType == NetworkType.MainNet ? NBitcoinNetwork.Main : NBitcoinNetwork.TestNet;
            blockchainAPI = new BitcoinSDK.API.BlockchainAPI();
        }

        #region Wallet Creation

        /// <summary>
        /// Generate new wallet with 12-word seed phrase
        /// </summary>
        public Models.WalletInfo CreateNewWallet()
        {
            Mnemonic mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
            ExtKey masterKey = mnemonic.DeriveExtKey();
            
            // Generate default SegWit address (BIP84)
            KeyPath keyPath = new KeyPath("m/84'/0'/0'/0/0");
            BitcoinAddress address = masterKey.Derive(keyPath).PrivateKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, currentNetwork);
            
            return new Models.WalletInfo
            {
                seedPhrase = mnemonic.ToString(),
                address = address.ToString(),
                network = currentNetwork == NBitcoinNetwork.Main ? "MainNet" : "TestNet",
                createdAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Restore wallet from seed phrase
        /// </summary>
        public Models.WalletInfo RestoreWalletFromSeed(string seedPhrase)
        {
            try
            {
                Mnemonic mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
                ExtKey masterKey = mnemonic.DeriveExtKey();
                
                KeyPath keyPath = new KeyPath("m/84'/0'/0'/0/0");
                BitcoinAddress address = masterKey.Derive(keyPath).PrivateKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, currentNetwork);
                
                return new Models.WalletInfo
                {
                    seedPhrase = seedPhrase,
                    address = address.ToString(),
                    network = currentNetwork == NBitcoinNetwork.Main ? "MainNet" : "TestNet",
                    createdAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Invalid seed phrase: {ex.Message}");
            }
        }

        /// <summary>
        /// Import wallet from private key (WIF or Hex format)
        /// </summary>
        public Models.WalletInfo ImportFromPrivateKey(string privateKey)
        {
            try
            {
                BitcoinSecret secret = new BitcoinSecret(privateKey, currentNetwork);
                Key key = secret.PrivateKey;
                
                BitcoinAddress address = key.PubKey.GetAddress(ScriptPubKeyType.Segwit, currentNetwork);
                
                return new Models.WalletInfo
                {
                    privateKey = privateKey,
                    address = address.ToString(),
                    network = currentNetwork == NBitcoinNetwork.Main ? "MainNet" : "TestNet",
                    createdAt = DateTime.UtcNow
                };
            }
            catch
            {
                // Try hex format
                try
                {
                    Key key = new Key(Utils.HexHelper.HexToBytes(privateKey));
                    BitcoinAddress address = key.PubKey.GetAddress(ScriptPubKeyType.Segwit, currentNetwork);
                    
                    return new Models.WalletInfo
                    {
                        privateKey = privateKey,
                        address = address.ToString(),
                        network = currentNetwork == NBitcoinNetwork.Main ? "MainNet" : "TestNet",
                        createdAt = DateTime.UtcNow
                    };
                }
                catch (Exception ex)
                {
                    throw new Exception($"Invalid private key format: {ex.Message}");
                }
            }
        }

        #endregion

        #region Address Generation

        /// <summary>
        /// Generate address from seed phrase with custom derivation path
        /// </summary>
        public string GenerateAddress(string seedPhrase, string derivationPath, ScriptPubKeyType addressType = ScriptPubKeyType.Segwit)
        {
            Mnemonic mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
            ExtKey masterKey = mnemonic.DeriveExtKey();
            KeyPath keyPath = new KeyPath(derivationPath);
            return masterKey.Derive(keyPath).PrivateKey.PubKey.GetAddress(addressType, currentNetwork).ToString();
        }

        /// <summary>
        /// Generate multiple addresses (Legacy, SegWit, P2SH-SegWit)
        /// </summary>
        public Models.AddressSet GenerateAddressSet(string seedPhrase, int index = 0)
        {
            Mnemonic mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
            ExtKey masterKey = mnemonic.DeriveExtKey();
            
            return new Models.AddressSet
            {
                legacy = GenerateAddress(seedPhrase, $"m/44'/0'/0'/0/{index}", ScriptPubKeyType.Legacy),
                segwit = GenerateAddress(seedPhrase, $"m/84'/0'/0'/0/{index}", ScriptPubKeyType.Segwit),
                p2shSegwit = GenerateAddress(seedPhrase, $"m/49'/0'/0'/0/{index}", ScriptPubKeyType.SegwitP2SH)
            };
        }

        #endregion

        #region Balance & Transactions

        /// <summary>
        /// Get balance for single address
        /// </summary>
        public async Task<decimal> GetBalance(string address)
        {
            bool isTestNet = currentNetwork == NBitcoinNetwork.TestNet;
            return await blockchainAPI.GetBalance(address, isTestNet);
        }

        /// <summary>
        /// Get balance for multiple addresses
        /// </summary>
        public async Task<Dictionary<string, decimal>> GetBatchBalances(List<string> addresses)
        {
            bool isTestNet = currentNetwork == NBitcoinNetwork.TestNet;
            return await blockchainAPI.GetBatchBalances(addresses, isTestNet);
        }

        /// <summary>
        /// Scan all derived addresses and return total balance
        /// </summary>
        public async Task<Models.ScanResult> ScanWalletBalance(string seedPhrase, Action<string, int, int> progressCallback = null)
        {
            BitcoinTransactionService txService = new BitcoinTransactionService();
            bool isTestNet = currentNetwork == NBitcoinNetwork.TestNet;
            return await txService.ScanAllAddresses(seedPhrase, progressCallback, null, null);
        }

        /// <summary>
        /// Get transaction history for address
        /// </summary>
        public async Task<List<Models.Transaction>> GetTransactionHistory(string address)
        {
            bool isTestNet = currentNetwork == NBitcoinNetwork.TestNet;
            var txs = await blockchainAPI.GetAllTransactions(address, isTestNet);
            
            List<Models.Transaction> result = new List<Models.Transaction>();
            foreach (var tx in txs)
            {
                result.Add(new Models.Transaction
                {
                    txHash = tx.txHash,
                    type = tx.type,
                    amount = decimal.Parse(tx.amount),
                    date = tx.date,
                    otherAddress = tx.otherAddress
                });
            }
            return result;
        }

        /// <summary>
        /// Get transaction history for multiple addresses
        /// </summary>
        public async Task<List<Models.Transaction>> GetBatchTransactionHistory(List<string> addresses)
        {
            bool isTestNet = currentNetwork == NBitcoinNetwork.TestNet;
            var txs = await blockchainAPI.GetBatchTransactions(addresses, isTestNet);
            
            List<Models.Transaction> result = new List<Models.Transaction>();
            foreach (var tx in txs)
            {
                result.Add(new Models.Transaction
                {
                    txHash = tx.txHash,
                    type = tx.type,
                    amount = decimal.Parse(tx.amount),
                    date = tx.date,
                    otherAddress = tx.otherAddress
                });
            }
            return result;
        }

        #endregion

        #region Send Transaction

        /// <summary>
        /// Send Bitcoin transaction
        /// </summary>
        public async Task<string> SendBitcoin(
            string seedPhrase,
            string toAddress,
            decimal amount,
            decimal fee,
            Action<string> statusCallback = null,
            CancellationToken cancellationToken = default)
        {
            BitcoinTransactionService txService = new BitcoinTransactionService();
            bool isTestNet = currentNetwork == NBitcoinNetwork.TestNet;
            
            return await txService.SendBitcoin(
                seedPhrase,
                isTestNet,
                toAddress,
                amount,
                fee,
                statusCallback,
                cancellationToken,
                null,
                null
            );
        }

        /// <summary>
        /// Send Bitcoin from imported private key
        /// </summary>
        public async Task<string> SendBitcoinFromPrivateKey(
            string privateKey,
            string toAddress,
            decimal amount,
            decimal fee,
            Action<string> statusCallback = null,
            CancellationToken cancellationToken = default)
        {
            BitcoinTransactionService txService = new BitcoinTransactionService();
            bool isTestNet = currentNetwork == NBitcoinNetwork.TestNet;
            
            return await txService.SendBitcoin(
                null,
                isTestNet,
                toAddress,
                amount,
                fee,
                statusCallback,
                cancellationToken,
                null,
                privateKey
            );
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validate Bitcoin address
        /// </summary>
        public bool ValidateAddress(string address)
        {
            try
            {
                BitcoinAddress.Create(address, currentNetwork);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validate seed phrase
        /// </summary>
        public bool ValidateSeedPhrase(string seedPhrase)
        {
            try
            {
                new Mnemonic(seedPhrase, Wordlist.English);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Convert BTC to Satoshi
        /// </summary>
        public static long BTCToSatoshi(decimal btc)
        {
            return (long)(btc * 100000000m);
        }

        /// <summary>
        /// Convert Satoshi to BTC
        /// </summary>
        public static decimal SatoshiToBTC(long satoshi)
        {
            return satoshi / 100000000m;
        }

        /// <summary>
        /// Get current network type
        /// </summary>
        public string GetNetworkType()
        {
            return currentNetwork == NBitcoinNetwork.Main ? "MainNet" : "TestNet";
        }

        #endregion
    }
}
