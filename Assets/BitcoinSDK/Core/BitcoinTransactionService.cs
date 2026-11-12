using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using UnityEngine;

namespace BitcoinSDK.Core
{
    /// <summary>
    /// Service for handling Bitcoin transactions and address scanning
    /// Supports both MainNet and TestNet
    /// </summary>
    public class BitcoinTransactionService
    {
        private BitcoinSDK.API.BlockchainAPI apiCore;

        public BitcoinTransactionService()
        {
            apiCore = new BitcoinSDK.API.BlockchainAPI();
        }

        public async Task<Models.ScanResult> ScanAllAddresses(
            string seedPhrase, 
            Action<string, int, int> progressCallback = null,
            List<GeneratedAddress> generatedAddresses = null,
            string privateKey = null)
        {
            decimal confirmedBalance = 0;
            decimal unconfirmedBalance = 0;

            try
            {
                List<string> addressesToScan = new List<string>();
                
                if (!string.IsNullOrEmpty(seedPhrase))
                {
                    Mnemonic mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
                    ExtKey masterKey = mnemonic.DeriveExtKey();
                    NBitcoin.Network network = NBitcoin.Network.Main;

                    var relevantPaths = new List<AddressConfig>
                    {
                        new AddressConfig { Name = "Legacy", Path = "m/44'/0'/0'/0", ScriptType = ScriptPubKeyType.Legacy },
                        new AddressConfig { Name = "SegWit", Path = "m/84'/0'/0'/0", ScriptType = ScriptPubKeyType.Segwit },
                        new AddressConfig { Name = "P2SH-SegWit", Path = "m/49'/0'/0'/0", ScriptType = ScriptPubKeyType.SegwitP2SH },
                        new AddressConfig { Name = "Legacy Change", Path = "m/44'/0'/0'/1", ScriptType = ScriptPubKeyType.Legacy },
                        new AddressConfig { Name = "SegWit Change", Path = "m/84'/0'/0'/1", ScriptType = ScriptPubKeyType.Segwit },
                        new AddressConfig { Name = "P2SH Change", Path = "m/49'/0'/0'/1", ScriptType = ScriptPubKeyType.SegwitP2SH }
                    };

                    foreach (var config in relevantPaths)
                    {
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
                    BitcoinSecret secret = new BitcoinSecret(privateKey, NBitcoin.Network.Main);
                    var pKey = secret.PrivateKey;
                    addressesToScan.Add(pKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, NBitcoin.Network.Main).ToString());
                    addressesToScan.Add(pKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, NBitcoin.Network.Main).ToString());
                    addressesToScan.Add(pKey.PubKey.GetAddress(ScriptPubKeyType.SegwitP2SH, NBitcoin.Network.Main).ToString());
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
                    progressCallback?.Invoke("Fetching Balances (Batch Mode)", 1, 1);
                    var balances = await apiCore.GetBatchBalances(addressesToScan, false);
                    
                    foreach (var bal in balances.Values)
                    {
                        confirmedBalance += bal;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"ScanAllAddresses Error: {ex.Message}");
                throw;
            }

            return new Models.ScanResult
            {
                confirmedBalance = confirmedBalance,
                unconfirmedBalance = unconfirmedBalance
            };
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

                if (string.IsNullOrEmpty(seedPhrase) && string.IsNullOrEmpty(privateKey))
                    throw new Exception("Seed phrase OR Private Key is required");
                if (string.IsNullOrEmpty(toAddress))
                    throw new Exception("Recipient address is required");
                if (amount <= 0)
                    throw new Exception("Amount must be greater than 0");
                if (fee <= 0)
                    throw new Exception("Fee must be greater than 0");

                cancellationToken.ThrowIfCancellationRequested();

                statusCallback?.Invoke("Deriving keys...");
                
                NBitcoin.Network network = (isTestNet) ? NBitcoin.Network.TestNet : NBitcoin.Network.Main;
                int coinType = isTestNet ? 1 : 0;
                
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
                        for(int i=0; i<5; i++) {
                            Key pk = masterKey.Derive(new KeyPath($"{p.path}/{i}")).PrivateKey;
                            string addr = pk.PubKey.GetAddress(p.type, network).ToString();
                            addrToKeyMap[addr] = pk;
                            addrToTypeMap[addr] = p.type;
                        }
                    }

                    if (generatedAddresses != null) {
                        foreach(var ga in generatedAddresses) {
                            if (ga.index >= 5)
                            {
                                ScriptPubKeyType[] types = { ScriptPubKeyType.Segwit, ScriptPubKeyType.Legacy, ScriptPubKeyType.SegwitP2SH };
                                string[] basePaths = { $"m/84'/{coinType}'/0'/0", $"m/44'/{coinType}'/0'/0", $"m/49'/{coinType}'/0'/0" };
                                
                                for (int t = 0; t < types.Length; t++)
                                {
                                    Key pk = masterKey.Derive(new KeyPath($"{basePaths[t]}/{ga.index}")).PrivateKey;
                                    string addr = pk.PubKey.GetAddress(types[t], network).ToString();
                                    addrToKeyMap[addr] = pk;
                                    addrToTypeMap[addr] = types[t];
                                }
                            }
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(privateKey))
                {
                    BitcoinSecret secret = new BitcoinSecret(privateKey, network);
                    Key pk = secret.PrivateKey;
                    ScriptPubKeyType[] types = { ScriptPubKeyType.Legacy, ScriptPubKeyType.Segwit, ScriptPubKeyType.SegwitP2SH };
                    foreach(var t in types) {
                        string addr = pk.PubKey.GetAddress(t, network).ToString();
                        addrToKeyMap[addr] = pk;
                        addrToTypeMap[addr] = t;
                    }
                }

                statusCallback?.Invoke("Scanning addresses for funds (Batch Mode)...");
                var addresses = new List<string>(addrToKeyMap.Keys);
                var batchFullData = await apiCore.GetBatchFullData(addresses, isTestNet);

                List<ICoin> allCoins = new List<ICoin>();
                List<Key> allKeys = new List<Key>();

                foreach (var addr in batchFullData.Keys)
                {
                    var data = batchFullData[addr];
                    if (data.txrefs != null)
                    {
                        foreach (var txref in data.txrefs)
                        {
                            if (txref.spent == false)
                            {
                                Key pk = addrToKeyMap[addr];
                                ScriptPubKeyType type = addrToTypeMap[addr];
                                Script script = pk.PubKey.GetAddress(type, network).ScriptPubKey;
                                
                                allCoins.Add(new Coin(new uint256(txref.tx_hash), (uint)txref.tx_output_n, new Money(txref.value), script));
                                if (!allKeys.Contains(pk)) allKeys.Add(pk);
                            }
                        }
                    }
                }

                if (allCoins.Count == 0)
                {
                    throw new Exception("No Unspent Outputs (UTXOs) found on any address. Do you have confirmed funds?");
                }
                
                statusCallback?.Invoke("Building and signing transaction...");
                
                BitcoinAddress changeAddress = allKeys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
                
                Money sendAmount = Money.Coins(amount);
                Money minerFee = Money.Coins(fee);
                
                BitcoinAddress recipientAddr = BitcoinAddress.Create(toAddress, network);
                
                TransactionBuilder builder = network.CreateTransactionBuilder();
                Transaction signedTx = builder
                    .AddCoins(allCoins)
                    .AddKeys(allKeys.ToArray())
                    .Send(recipientAddr, sendAmount)
                    .SetChange(changeAddress)
                    .SendFees(minerFee)
                    .BuildTransaction(true);
                
                if (!builder.Verify(signedTx))
                {
                    throw new Exception("Transaction signing failed! Check keys and UTXOs.");
                }
                
                string hex = signedTx.ToHex();
                
                cancellationToken.ThrowIfCancellationRequested();
                
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

        private struct AddressConfig
        {
            public string Name;
            public string Path;
            public ScriptPubKeyType ScriptType;
        }
    }

    public class GeneratedAddress
    {
        public string address;
        public int index;
    }
}
