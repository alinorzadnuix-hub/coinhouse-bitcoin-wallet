using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace BitcoinSDK.API
{
    /// <summary>
    /// Blockchain API wrapper using BlockCypher
    /// </summary>
    public class BlockchainAPI
    {
        private static DateTime lastApiCall = DateTime.MinValue;
        private const int MinApiDelayMs = 1500;

        public async Task<decimal> GetBalance(string address, bool isTestNet = false)
        {
            await RateLimitDelay();
            
            string network = isTestNet ? "test3" : "main";
            string url = $"https://api.blockcypher.com/v1/btc/{network}/addrs/{address}/balance";
            
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                try
                {
                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode) return 0;
                    
                    var json = await response.Content.ReadAsStringAsync();
                    var data = Newtonsoft.Json.JsonConvert.DeserializeObject<BalanceData>(json);
                    return data != null ? data.final_balance / 100000000m : 0;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Balance fetch error: {ex.Message}");
                    return 0;
                }
            }
        }

        public async Task<Dictionary<string, decimal>> GetBatchBalances(List<string> addresses, bool isTestNet = false)
        {
            if (addresses == null || addresses.Count == 0) return new Dictionary<string, decimal>();
            
            await RateLimitDelay();
            
            string network = isTestNet ? "test3" : "main";
            string batchStr = string.Join(";", addresses);
            string url = $"https://api.blockcypher.com/v1/btc/{network}/addrs/{batchStr}/balance";
            
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                try
                {
                    var response = await client.GetAsync(url);
                    var json = await response.Content.ReadAsStringAsync();
                    
                    if (!response.IsSuccessStatusCode) return new Dictionary<string, decimal>();

                    var dataList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BalanceData>>(json);
                    var result = new Dictionary<string, decimal>();
                    
                    if (dataList != null)
                    {
                        foreach (var data in dataList)
                        {
                            if (!string.IsNullOrEmpty(data.address))
                            {
                                result[data.address] = data.final_balance / 100000000m;
                            }
                        }
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Batch Balance Exception: {ex.Message}");
                    return new Dictionary<string, decimal>();
                }
            }
        }

        public async Task<List<TransactionDisplayData>> GetAllTransactions(string address, bool isTestNet = false)
        {
            await RateLimitDelay();
            
            string network = isTestNet ? "test3" : "main";
            string url = $"https://api.blockcypher.com/v1/btc/{network}/addrs/{address}/full?limit=50";
            
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                try
                {
                    var response = await client.GetAsync(url);
                    var json = await response.Content.ReadAsStringAsync();
                    
                    if (!response.IsSuccessStatusCode) return new List<TransactionDisplayData>();
                    if (string.IsNullOrEmpty(json)) return new List<TransactionDisplayData>();
                    
                    var data = Newtonsoft.Json.JsonConvert.DeserializeObject<AddressFullData>(json);
                    List<TransactionDisplayData> history = new List<TransactionDisplayData>();
                    
                    if (data.txs != null)
                    {
                        foreach (var tx in data.txs)
                        {
                            var txData = ParseTxDataToDisplay(tx, address);
                            if (txData != null) history.Add(txData);
                        }
                    }
                    
                    return history;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"History Error: {ex.Message}");
                    return new List<TransactionDisplayData>();
                }
            }
        }

        public async Task<Dictionary<string, AddressFullData>> GetBatchFullData(List<string> addresses, bool isTestNet = false)
        {
            if (addresses == null || addresses.Count == 0) return new Dictionary<string, AddressFullData>();

            await RateLimitDelay();

            string network = isTestNet ? "test3" : "main";
            string batchStr = string.Join(";", addresses);
            string url = $"https://api.blockcypher.com/v1/btc/{network}/addrs/{batchStr}/full?limit=50";

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(25);
                try
                {
                    var response = await client.GetAsync(url);
                    var json = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode) return new Dictionary<string, AddressFullData>();

                    var dataList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<AddressFullData>>(json);
                    var result = new Dictionary<string, AddressFullData>();

                    if (dataList != null)
                    {
                        foreach (var data in dataList)
                        {
                            if (!string.IsNullOrEmpty(data.address))
                            {
                                result[data.address] = data;
                            }
                        }
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Batch Full Data Exception: {ex.Message}");
                    return new Dictionary<string, AddressFullData>();
                }
            }
        }

        public async Task<List<TransactionDisplayData>> GetBatchTransactions(List<string> addresses, bool isTestNet = false)
        {
            if (addresses == null || addresses.Count == 0) return new List<TransactionDisplayData>();

            var batchData = await GetBatchFullData(addresses, isTestNet);
            var allTransactions = new List<TransactionDisplayData>();
            var seenTxHashes = new HashSet<string>();

            foreach (var kvp in batchData)
            {
                string address = kvp.Key;
                var data = kvp.Value;

                if (data.txs != null)
                {
                    foreach (var tx in data.txs)
                    {
                        if (!seenTxHashes.Contains(tx.hash))
                        {
                            var txData = ParseTxDataToDisplay(tx, address);
                            if (txData != null)
                            {
                                allTransactions.Add(txData);
                                seenTxHashes.Add(tx.hash);
                            }
                        }
                    }
                }
            }

            allTransactions.Sort((a, b) => string.Compare(b.date, a.date));
            return allTransactions;
        }

        public async Task<string> BroadcastTransaction(string hex, bool isTestNet = false)
        {
            await RateLimitDelay();
            string network = isTestNet ? "test3" : "main";
            string url = $"https://api.blockcypher.com/v1/btc/{network}/txs/push";

            var txWrapper = new { tx = hex };
            var jsonContent = Newtonsoft.Json.JsonConvert.SerializeObject(txWrapper);
            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            using (var client = new HttpClient())
            {
                try
                {
                    var response = await client.PostAsync(url, content);
                    var respJson = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Broadcast Failed ({response.StatusCode}): {respJson}");
                    }

                    var txObj = Newtonsoft.Json.JsonConvert.DeserializeObject<TxData>(respJson);
                    return txObj.hash;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Broadcast Error: {ex.Message}");
                    throw;
                }
            }
        }

        private async Task RateLimitDelay()
        {
            var timeSinceLastCall = (DateTime.Now - lastApiCall).TotalMilliseconds;
            if (timeSinceLastCall < MinApiDelayMs)
            {
                await Task.Delay((int)(MinApiDelayMs - timeSinceLastCall));
            }
            lastApiCall = DateTime.Now;
        }

        private TransactionDisplayData ParseTxDataToDisplay(TxData tx, string address)
        {
            if (tx == null) return null;

            decimal inputSum = 0;
            decimal outputSum = 0;

            if (tx.inputs != null)
            {
                foreach (var inp in tx.inputs)
                {
                    if (inp.addresses != null && inp.addresses.Contains(address))
                    {
                        inputSum += inp.output_value;
                    }
                }
            }

            if (tx.outputs != null)
            {
                foreach (var outp in tx.outputs)
                {
                    if (outp.addresses != null && outp.addresses.Contains(address))
                    {
                        outputSum += outp.value;
                    }
                }
            }

            decimal difference = outputSum - inputSum;
            if (difference == 0) return null;

            TransactionDisplayData txData = new TransactionDisplayData();
            txData.txHash = tx.hash;
            txData.date = tx.confirmed.HasValue ? tx.confirmed.Value.ToString("yyyy-MM-dd HH:mm") : "Unconfirmed";
            
            decimal amountBTC = Math.Abs(difference) / 100000000m;
            txData.amount = amountBTC.ToString("F8");

            if (difference > 0)
            {
                txData.type = "Receive";
                if (tx.inputs != null && tx.inputs.Count > 0 && tx.inputs[0].addresses != null)
                    txData.otherAddress = tx.inputs[0].addresses[0];
                else
                    txData.otherAddress = "Unknown";
            }
            else
            {
                txData.type = "Send";
                txData.otherAddress = "Multiple/Unknown";
                if (tx.outputs != null)
                {
                    foreach(var outp in tx.outputs)
                    {
                        if (outp.addresses != null && !outp.addresses.Contains(address))
                        {
                            txData.otherAddress = outp.addresses[0];
                            break;
                        }
                    }
                }
            }

            return txData;
        }

        #region Data Classes

        public class TransactionDisplayData
        {
            public string type;
            public string amount;
            public string date;
            public string otherAddress;
            public string txHash;
        }

        [Serializable]
        public class BalanceData
        {
            public string address;
            public long final_balance;
            public List<TxRef> txrefs;
        }

        [Serializable]
        public class AddressFullData
        {
            public string address;
            public List<TxData> txs;
            public List<TxRef> txrefs;
        }

        [Serializable]
        public class TxRef
        {
            public string tx_hash;
            public int block_height;
            public int tx_input_n;
            public int tx_output_n;
            public long value;
            public bool spent;
            public string confirmed;
            public string script;
        }

        [Serializable]
        public class TxData
        {
            public string hash;
            public DateTime? confirmed;
            public DateTime? received;
            public List<TxInput> inputs;
            public List<TxOutput> outputs;
        }

        [Serializable]
        public class TxInput
        {
            public long output_value;
            public List<string> addresses;
        }

        [Serializable]
        public class TxOutput
        {
            public long value;
            public List<string> addresses;
        }

        #endregion
    }
}
