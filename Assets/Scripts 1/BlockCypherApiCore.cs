using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;

public class BlockCypherApiCore
{
    private static DateTime lastApiCall = DateTime.MinValue;
    private const int MinApiDelayMs = 1500; // 1.5s between calls — safe for BlockCypher free tier and AGENTS.md compliance

    private HttpClient CreateHttpClient(int timeoutSeconds = 30)
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return client;
    }
    
    /// <summary>
    /// Get all transaction history using Mempool.space API (no rate limits, supports both MainNet and TestNet)
    /// </summary>
    public async Task<List<TransactionDisplayData>> GetAllTransactions(string address, bool isTestNet = false)
    {
        await RateLimitDelay();

#if TESTNET_BUILD
        bool useTestNet = true;
#else
        bool useTestNet = isTestNet;
#endif
        string mempoolBase = useTestNet
            ? "https://mempool.space/testnet/api"
            : "https://mempool.space/api";

        string mempoolUrl = $"{mempoolBase}/address/{address}/txs";
        Debug.Log($"[Mempool] GET {mempoolUrl}");

        using (var client = CreateHttpClient(30))
        {
            string json = null;
            bool mempoolSuccess = false;

            try
            {
                var response = await client.GetAsync(mempoolUrl);
                var rawJson = await response.Content.ReadAsStringAsync();
                Debug.Log($"[Mempool] Response {(int)response.StatusCode} — length={rawJson?.Length ?? 0}");

                if (response.IsSuccessStatusCode)
                {
                    json = rawJson;
                    mempoolSuccess = true;
                }
                else
                {
                    Debug.LogWarning($"[Mempool] HTTP {(int)response.StatusCode} for {address}. Falling back to Blockstream...");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Mempool] Exception for {address}: {ex.Message}. Falling back to Blockstream...");
            }

            // Fallback to Blockstream.info if Mempool returned 429 Too Many Requests or failed
            if (!mempoolSuccess)
            {
                try
                {
                    string blockstreamBase = useTestNet
                        ? "https://blockstream.info/testnet/api"
                        : "https://blockstream.info/api";
                    string blockstreamUrl = $"{blockstreamBase}/address/{address}/txs";
                    Debug.Log($"[BlockstreamFallback] GET {blockstreamUrl}");

                    var bsResponse = await client.GetAsync(blockstreamUrl);
                    var bsJson = await bsResponse.Content.ReadAsStringAsync();
                    if (bsResponse.IsSuccessStatusCode)
                    {
                        json = bsJson;
                        Debug.Log($"[BlockstreamFallback] Success for {address} — length={json?.Length ?? 0}");
                    }
                    else
                    {
                        Debug.LogWarning($"[BlockstreamFallback] HTTP {(int)bsResponse.StatusCode} for {address}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BlockstreamFallback] Exception for {address}: {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(json) || json == "[]")
            {
                Debug.Log($"[GetAllTransactions] No transactions for {address}");
                return new List<TransactionDisplayData>();
            }

            try
            {
                var txArray = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MempoolTx>>(json);
                var history = new List<TransactionDisplayData>();

                if (txArray != null)
                {
                    foreach (var tx in txArray)
                    {
                        var txData = ParseMempoolTx(tx, address);
                        if (txData != null) history.Add(txData);
                    }
                }

                Debug.Log($"[GetAllTransactions] Parsed {history.Count} transactions for {address}");
                return history;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GetAllTransactions] Parse Error for {address}: {ex.Message}");
                return new List<TransactionDisplayData>();
            }
        }
    }

    /// <summary>
    /// Parse a Mempool.space transaction into TransactionDisplayData
    /// </summary>
    private TransactionDisplayData ParseMempoolTx(MempoolTx tx, string address)
    {
        if (tx == null) return null;

        // Calculate net satoshi change for this address
        long inputSum = 0;
        long outputSum = 0;

        if (tx.vin != null)
            foreach (var inp in tx.vin)
                if (inp.prevout != null && inp.prevout.scriptpubkey_address == address)
                    inputSum += inp.prevout.value;

        if (tx.vout != null)
            foreach (var outp in tx.vout)
                if (outp.scriptpubkey_address == address)
                    outputSum += outp.value;

        long net = outputSum - inputSum;
        if (net == 0) return null;

        var result = new TransactionDisplayData();
        result.txHash = tx.txid;

        // Date from block timestamp
        if (tx.status != null && tx.status.confirmed && tx.status.block_time > 0)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(tx.status.block_time).LocalDateTime;
            result.date = dt.ToString("yyyy-MM-dd HH:mm");
        }
        else
        {
            result.date = "Unconfirmed";
        }

        result.amount = (Math.Abs(net) / 100000000m).ToString("F8");
        result.fee = (tx.fee / 100000000m).ToString("F8");

        if (net > 0)
        {
            result.type = "Receive";
            // Sender = first input address
            result.otherAddress = tx.vin?[0]?.prevout?.scriptpubkey_address ?? "Unknown";
        }
        else
        {
            result.type = "Send";
            // Recipient = first output NOT belonging to us
            result.otherAddress = "Unknown";
            if (tx.vout != null)
                foreach (var outp in tx.vout)
                    if (outp.scriptpubkey_address != address && !string.IsNullOrEmpty(outp.scriptpubkey_address))
                    { result.otherAddress = outp.scriptpubkey_address; break; }
        }

        return result;
    }

    // --- Mempool.space data classes ---
    private class MempoolTx
    {
        public string txid;
        public List<MempoolInput>  vin;
        public List<MempoolOutput> vout;
        public MempoolStatus status;
        public long fee;
    }
    private class MempoolInput  { public MempoolPrevout prevout; }
    private class MempoolPrevout { public string scriptpubkey_address; public long value; }
    private class MempoolOutput  { public string scriptpubkey_address; public long value; }
    private class MempoolStatus  { public bool confirmed; public long block_time; }

    public async Task<decimal> GetBalance(string address, bool isTestNet = false)
    {
#if TESTNET_BUILD
        bool useTestNet = true;
#else
        bool useTestNet = isTestNet;
#endif

        if (!useTestNet)
        {
            // --- MAINNET TIER 1: Blockchain.info query (fastest, unblocked, direct satoshi text) ---
            try
            {
                using (var client = CreateHttpClient(6))
                {
                    string bcUrl = $"https://blockchain.info/q/addressbalance/{address}";
                    var resp = await client.GetAsync(bcUrl);
                    if (resp.IsSuccessStatusCode)
                    {
                        string str = await resp.Content.ReadAsStringAsync();
                        if (long.TryParse(str.Trim(), out long satoshis))
                        {
                            decimal btc = satoshis / 100000000m;
                            Debug.Log($"[GetBalance] Blockchain.info: {address} → {btc:F8} BTC");
                            return btc;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GetBalance] Blockchain.info failed for {address}: {ex.Message}");
            }

            // --- MAINNET TIER 2: BlockCypher query ---
            try
            {
                using (var client = CreateHttpClient(6))
                {
                    string cypherUrl = $"https://api.blockcypher.com/v1/btc/main/addrs/{address}/balance";
                    var resp = await client.GetAsync(cypherUrl);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        var token = Newtonsoft.Json.Linq.JObject.Parse(json);
                        if (token["final_balance"] != null && long.TryParse(token["final_balance"].ToString(), out long satoshis))
                        {
                            decimal btc = satoshis / 100000000m;
                            Debug.Log($"[GetBalance] BlockCypher: {address} → {btc:F8} BTC");
                            return btc;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GetBalance] BlockCypher failed for {address}: {ex.Message}");
            }
        }

        // --- TESTNET or Fallback: Mempool.space ---
        string baseUrl = useTestNet
            ? "https://mempool.space/testnet/api"
            : "https://mempool.space/api";
        string url = $"{baseUrl}/address/{address}";
        Debug.Log($"[GetBalance] GET {url}");

        using (var client = CreateHttpClient(8))
        {
            try
            {
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogWarning($"[GetBalance] {response.StatusCode} for {address}");
                    return 0;
                }
                var json = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(json)) return 0;

                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<MempoolAddressData>(json);
                if (data == null) return 0;

                // confirmed balance = funded - spent
                long satoshi = (data.chain_stats.funded_txo_sum - data.chain_stats.spent_txo_sum)
                             + (data.mempool_stats.funded_txo_sum - data.mempool_stats.spent_txo_sum);
                decimal btc = satoshi / 100000000m;
                Debug.Log($"[GetBalance] Mempool: {address} → {btc:F8}");
                return btc;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GetBalance] Error for {address}: {ex.Message}");
                return 0;
            }
        }
    }

    public async Task<int> GetTransactionCount(string address, bool isTestNet = false)
    {
#if TESTNET_BUILD
        bool useTestNet = true;
#else
        bool useTestNet = isTestNet;
#endif
        string baseUrl = useTestNet ? "https://mempool.space/testnet/api" : "https://mempool.space/api";
        string url = $"{baseUrl}/address/{address}";
        Debug.Log($"[GetTransactionCount] GET {url}");

        using (var client = CreateHttpClient(15))
        {
            try
            {
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogWarning($"[GetTransactionCount] {response.StatusCode} for {address}");
                    return 0;
                }
                var json = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(json)) return 0;

                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<MempoolAddressData>(json);
                if (data == null) return 0;

                int txs = data.chain_stats.tx_count + data.mempool_stats.tx_count;
                Debug.Log($"[GetTransactionCount] {address} → {txs} txs");
                return txs;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GetTransactionCount] Error for {address}: {ex.Message}");
                return 0;
            }
        }
    }

    public class MempoolAddressData
    {
        public MempoolAddrStats chain_stats   = new MempoolAddrStats();
        public MempoolAddrStats mempool_stats = new MempoolAddrStats();
    }
    public class MempoolAddrStats
    {
        public long funded_txo_sum;
        public long spent_txo_sum;
        public int tx_count;
    }

    // --- BATCH API SUPPORT ---

    /// <summary>
    /// Fetch balances for multiple addresses using Mempool.space in parallel (no rate limit)
    /// </summary>
    public async Task<Dictionary<string, decimal>> GetBatchBalances(List<string> addresses, bool isTestNet = false)
    {
        if (addresses == null || addresses.Count == 0) return new Dictionary<string, decimal>();

#if TESTNET_BUILD
        bool useTestNet = true;
#else
        bool useTestNet = isTestNet;
#endif
        string baseUrl = useTestNet ? "https://mempool.space/testnet/api" : "https://mempool.space/api";
        var result = new Dictionary<string, decimal>();

        using (var client = CreateHttpClient(30))
        {
            // Fetch all addresses in parallel
            var tasks = addresses.Select(async addr =>
            {
                try
                {
                    var resp = await client.GetAsync($"{baseUrl}/address/{addr}");
                    if (!resp.IsSuccessStatusCode) return (addr, 0m);
                    var json = await resp.Content.ReadAsStringAsync();
                    if (string.IsNullOrEmpty(json)) return (addr, 0m);
                    var data = Newtonsoft.Json.JsonConvert.DeserializeObject<MempoolAddressData>(json);
                    if (data == null) return (addr, 0m);
                    long sat = (data.chain_stats.funded_txo_sum - data.chain_stats.spent_txo_sum)
                             + (data.mempool_stats.funded_txo_sum - data.mempool_stats.spent_txo_sum);
                    return (addr, sat / 100000000m);
                }
                catch { return (addr, 0m); }
            });

            var results = await Task.WhenAll(tasks);
            foreach (var (addr, bal) in results)
                result[addr] = bal;
        }

        Debug.Log($"[GetBatchBalances] Scanned {addresses.Count} addresses via Mempool ({(useTestNet ? "testnet" : "mainnet")})");
        return result;
    }

    /// <summary>
    /// Fetch full transaction data and UTXOs for multiple addresses in a single API call
    /// </summary>
    public async Task<Dictionary<string, AddressFullData>> GetBatchFullData(List<string> addresses, bool isTestNet = false)
    {
        if (addresses == null || addresses.Count == 0) return new Dictionary<string, AddressFullData>();

        await RateLimitDelay();

#if TESTNET_BUILD
        bool useTestNet = true;
#else
        bool useTestNet = isTestNet;
#endif
        string network = useTestNet ? "test3" : "main";
        string batchStr = string.Join(";", addresses);
        // limit=50 to prevent huge payloads in batch
        string url = $"https://api.blockcypher.com/v1/btc/{network}/addrs/{batchStr}/full?limit=50";

        using (var client = CreateHttpClient(25)) // Large batches need more time
        {
            try
            {
                var response = await client.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogError($"Batch Full Data Error: {response.StatusCode}");
                    return new Dictionary<string, AddressFullData>();
                }

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

    /// <summary>
    /// دریافت تمام تراکنش‌های چند آدرس به صورت یکجا
    /// </summary>
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

        // مرتب‌سازی بر اساس تاریخ (جدیدترین اول)
        allTransactions.Sort((a, b) => string.Compare(b.date, a.date));
        return allTransactions;
    }
    
    // --- NEW: Real Transaction Support ---

    // 1. Get UTXOs (Unspent Transaction Outputs) — uses Mempool.space (no rate limit)
    private async Task<List<UTXO>> FetchAddressUtxos(HttpClient client, string address, bool useTestNet)
    {
        string mempoolBase = useTestNet ? "https://mempool.space/testnet/api" : "https://mempool.space/api";
        string blockstreamBase = useTestNet ? "https://blockstream.info/testnet/api" : "https://blockstream.info/api";

        // 1. Primary: Mempool.space
        try
        {
            string url = $"{mempoolBase}/address/{address}/utxo";
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(json) && json != "[]")
                {
                    var mempoolUtxos = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MempoolUtxo>>(json);
                    if (mempoolUtxos != null && mempoolUtxos.Count > 0)
                    {
                        var utxos = new List<UTXO>();
                        foreach (var u in mempoolUtxos)
                        {
                            if (u.status != null)
                            {
                                utxos.Add(new UTXO {
                                    txHash      = u.txid,
                                    outputIndex = u.vout,
                                    value       = u.value,
                                    scriptHex   = ""
                                });
                            }
                        }
                        return utxos;
                    }
                }
                return new List<UTXO>();
            }
            if ((int)response.StatusCode == 429)
            {
                Debug.LogWarning($"[FetchAddressUtxos] Mempool.space rate limited (429) for {address}, waiting before fallback...");
                await Task.Delay(250);
            }
            else
            {
                Debug.LogWarning($"[FetchAddressUtxos] Mempool.space returned HTTP {response.StatusCode} for {address}, trying Blockstream fallback...");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FetchAddressUtxos] Mempool.space exception for {address}: {ex.Message}, trying Blockstream fallback...");
        }

        // 2. Secondary Fallback: Blockstream.info Esplora API
        try
        {
            string url = $"{blockstreamBase}/address/{address}/utxo";
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(json) && json != "[]")
                {
                    var blockstreamUtxos = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MempoolUtxo>>(json);
                    if (blockstreamUtxos != null && blockstreamUtxos.Count > 0)
                    {
                        var utxos = new List<UTXO>();
                        foreach (var u in blockstreamUtxos)
                        {
                            if (u.status != null)
                            {
                                utxos.Add(new UTXO {
                                    txHash      = u.txid,
                                    outputIndex = u.vout,
                                    value       = u.value,
                                    scriptHex   = ""
                                });
                            }
                        }
                        Debug.Log($"[FetchAddressUtxos] Blockstream fallback found {utxos.Count} UTXOs for {address}");
                        return utxos;
                    }
                }
                return new List<UTXO>();
            }
            Debug.LogWarning($"[FetchAddressUtxos] Blockstream fallback HTTP {response.StatusCode} for {address}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FetchAddressUtxos] Blockstream fallback exception for {address}: {ex.Message}");
        }

        return new List<UTXO>();
    }

    public async Task<List<UTXO>> GetUnspentOutputs(string address, bool isTestNet = false)
    {
#if TESTNET_BUILD
        bool useTestNet = true;
#else
        bool useTestNet = isTestNet;
#endif
        using (var client = CreateHttpClient(30))
        {
            return await FetchAddressUtxos(client, address, useTestNet);
        }
    }

    public async Task<Dictionary<string, List<UTXO>>> GetBatchUnspentOutputs(List<string> addresses, bool isTestNet = false)
    {
        if (addresses == null || addresses.Count == 0) return new Dictionary<string, List<UTXO>>();

#if TESTNET_BUILD
        bool useTestNet = true;
#else
        bool useTestNet = isTestNet;
#endif
        var result = new Dictionary<string, List<UTXO>>();

        using (var client = CreateHttpClient(30))
        {
            // Limit concurrency to 4 to stay well within mempool.space / blockstream.info rate limits
            var semaphore = new System.Threading.SemaphoreSlim(4);

            var tasks = addresses.Select(async addr =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var utxos = await FetchAddressUtxos(client, addr, useTestNet);
                    await Task.Delay(40);
                    return (addr, utxos);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GetBatchUnspentOutputs] Error for {addr}: {ex.Message}");
                    return (addr, new List<UTXO>());
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            foreach (var (addr, utxos) in results)
            {
                result[addr] = utxos;
            }
        }

        Debug.Log($"[GetBatchUnspentOutputs] Completed batch UTXO scan for {addresses.Count} addresses.");
        return result;
    }

    private class MempoolUtxo
    {
        public string txid;
        public int    vout;
        public long   value;
        public MempoolUtxoStatus status;
    }
    private class MempoolUtxoStatus
    {
        public bool confirmed;
        public long block_height;
    }

    /// <summary>
    /// Fetches live recommended fee rates (in sat/vByte) from Mempool.space with fallback defaults.
    /// </summary>
    public async Task<MempoolRecommendedFees> GetRecommendedFeesAsync(bool isTestNet = false)
    {
#if TESTNET_BUILD
        bool useTestNet = true;
#else
        bool useTestNet = isTestNet;
#endif
        string url = useTestNet
            ? "https://mempool.space/testnet/api/v1/fees/recommended"
            : "https://mempool.space/api/v1/fees/recommended";

        try
        {
            using (var client = CreateHttpClient(8))
            {
                var resp = await client.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    var fees = Newtonsoft.Json.JsonConvert.DeserializeObject<MempoolRecommendedFees>(json);
                    if (fees != null && fees.minimumFee > 0)
                    {
                        Debug.Log($"[MempoolFees] Live fees: Fastest={fees.fastestFee}, HalfHour={fees.halfHourFee}, Hour={fees.hourFee}, Min={fees.minimumFee} sat/vB");
                        return fees;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MempoolFees] Failed to fetch live fees: {ex.Message}. Using fallback.");
        }

        // Safe Fallback rates
        return useTestNet
            ? new MempoolRecommendedFees { fastestFee = 1, halfHourFee = 1, hourFee = 1, economyFee = 1, minimumFee = 1 }
            : new MempoolRecommendedFees { fastestFee = 25, halfHourFee = 15, hourFee = 10, economyFee = 7, minimumFee = 3 };
    }

    public static string GetPreferredBroadcastApi()
    {
        return PlayerPrefs.GetString("Pref_Send_API", "mempool").ToLowerInvariant();
    }

    private static string FormatNodeError(string apiName, int statusCode, string body)
    {
        if (string.IsNullOrEmpty(body)) return $"{apiName} error ({statusCode})";
        string clean = body;
        if (clean.StartsWith("sendrawtransaction RPC error: "))
            clean = clean.Substring("sendrawtransaction RPC error: ".Length);
        return $"{apiName}: {clean}";
    }

    private async Task<(bool success, string txid, string err, bool isNodeRejection)> TryBroadcastMempool(string cleanHex, string localTxHash, bool useTestNet)
    {
        string mempoolBase = useTestNet ? "https://mempool.space/testnet/api" : "https://mempool.space/api";
        string mempoolUrl  = $"{mempoolBase}/tx";

        try
        {
            using (var client = CreateHttpClient(12))
            {
                var content = new System.Net.Http.StringContent(cleanHex, System.Text.Encoding.UTF8, "text/plain");
                var resp    = await client.PostAsync(mempoolUrl, content);
                var body    = (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"');

                Debug.Log($"[Broadcast] Mempool response: HTTP {(int)resp.StatusCode}, body={body}");

                if (resp.IsSuccessStatusCode && !string.IsNullOrEmpty(body))
                {
                    if (body.Length == 64) return (true, body, null, false);
                    if (!string.IsNullOrEmpty(localTxHash)) return (true, localTxHash, null, false);
                }

                if (!string.IsNullOrEmpty(body) && (body.Contains("already in mempool") || body.Contains("txn-already-in-mempool") || body.Contains("already known") || body.Contains("already in block")) && !string.IsNullOrEmpty(localTxHash))
                {
                    return (true, localTxHash, null, false);
                }

                string formattedError = FormatNodeError("Mempool", (int)resp.StatusCode, body);
                // Only HTTP 400 is an RPC consensus rejection (e.g. dust, min fee, already spent).
                // HTTP 429 (Too Many Requests), 403, 408, 5xx are transport/rate-limit issues that must failover.
                bool isNodeRejection = (int)resp.StatusCode == 400;
                return (false, null, formattedError, isNodeRejection);
            }
        }
        catch (Exception ex)
        {
            return (false, null, $"Mempool connection error: {ex.Message}", false);
        }
    }

    private async Task<(bool success, string txid, string err, bool isNodeRejection)> TryBroadcastBlockstream(string cleanHex, string localTxHash, bool useTestNet)
    {
        string blockstreamBase = useTestNet ? "https://blockstream.info/testnet/api" : "https://blockstream.info/api";
        string blockstreamUrl  = $"{blockstreamBase}/tx";
        try
        {
            using (var client = CreateHttpClient(12))
            {
                var content = new System.Net.Http.StringContent(cleanHex, System.Text.Encoding.UTF8, "text/plain");
                var resp    = await client.PostAsync(blockstreamUrl, content);
                var body    = (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"');

                Debug.Log($"[Broadcast] Blockstream response: HTTP {(int)resp.StatusCode}, body={body}");

                if (resp.IsSuccessStatusCode && !string.IsNullOrEmpty(body))
                {
                    if (body.Length == 64) return (true, body, null, false);
                    if (!string.IsNullOrEmpty(localTxHash)) return (true, localTxHash, null, false);
                }
                if (!string.IsNullOrEmpty(body) && (body.Contains("already in mempool") || body.Contains("txn-already-in-mempool") || body.Contains("already known") || body.Contains("already in block")) && !string.IsNullOrEmpty(localTxHash))
                {
                    return (true, localTxHash, null, false);
                }

                string formattedError = FormatNodeError("Blockstream", (int)resp.StatusCode, body);
                // Only HTTP 400 is an RPC consensus rejection. 429, 403, 5xx must failover.
                bool isNodeRejection = (int)resp.StatusCode == 400;
                return (false, null, formattedError, isNodeRejection);
            }
        }
        catch (Exception ex)
        {
            return (false, null, $"Blockstream connection error: {ex.Message}", false);
        }
    }

    private async Task<(bool success, string txid, string err, bool isNodeRejection)> TryBroadcastBlockCypher(string cleanHex, bool useTestNet)
    {
        string network     = useTestNet ? "test3" : "main";
        string blockUrl    = $"https://api.blockcypher.com/v1/btc/{network}/txs/push";
        var    txWrapper   = new { tx = cleanHex };
        var    jsonContent = Newtonsoft.Json.JsonConvert.SerializeObject(txWrapper);

        try
        {
            using (var client = CreateHttpClient(15))
            {
                var content  = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(blockUrl, content);
                var respJson = await response.Content.ReadAsStringAsync();

                Debug.Log($"[Broadcast] BlockCypher response: HTTP {(int)response.StatusCode}, body={respJson}");

                if (!response.IsSuccessStatusCode)
                    return (false, null, $"BlockCypher ({response.StatusCode}): {respJson}", (int)response.StatusCode >= 400 && (int)response.StatusCode < 500);

                var txObj = Newtonsoft.Json.JsonConvert.DeserializeObject<TxData>(respJson);
                if (txObj == null || string.IsNullOrEmpty(txObj.hash))
                    return (false, null, $"BlockCypher: no TXID in response", false);

                return (true, txObj.hash, null, false);
            }
        }
        catch (Exception ex)
        {
            return (false, null, $"BlockCypher connection error: {ex.Message}", false);
        }
    }

    // 2. Broadcast Signed Transaction
    public async Task<string> BroadcastTransaction(string hex, bool isTestNet = false)
    {
        await RateLimitDelay();
#if TESTNET_BUILD
        bool useTestNet = true;
#else
        bool useTestNet = isTestNet;
#endif

        string cleanHex = hex?.Trim().Replace(" ", "").Replace("\r", "").Replace("\n", "");
        if (string.IsNullOrEmpty(cleanHex))
            throw new ArgumentException("Transaction hex is empty.", nameof(hex));

        string localTxHash = "";
        try
        {
            var parsedTx = NBitcoin.Transaction.Parse(cleanHex, useTestNet ? NBitcoin.Network.TestNet : NBitcoin.Network.Main);
            localTxHash = parsedTx.GetHash().ToString();
            Debug.Log($"[Broadcast] Parsed TX hash: {localTxHash}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Broadcast] Could not pre-parse TX hash: {ex.Message}");
        }

        string pref = GetPreferredBroadcastApi();
        Debug.Log($"[Broadcast] Starting broadcast (network={(useTestNet ? "testnet" : "mainnet")}, preferredApi={pref}), hex length={cleanHex.Length}");

        string lastError = "All broadcasters failed";

        if (pref == "blockstream")
        {
            var (success, txid, err, isNodeRejection) = await TryBroadcastBlockstream(cleanHex, localTxHash, useTestNet);
            if (success) return txid;
            lastError = err;
            if (isNodeRejection) throw new Exception(err);

            var mp = await TryBroadcastMempool(cleanHex, localTxHash, useTestNet);
            if (mp.success) return mp.txid;
            if (mp.isNodeRejection) throw new Exception(mp.err);

            var bcy = await TryBroadcastBlockCypher(cleanHex, useTestNet);
            if (bcy.success) return bcy.txid;
            throw new Exception($"Transaction Failed: {lastError}. Fallback: {bcy.err}");
        }
        else if (pref == "blockcypher")
        {
            var (success, txid, err, isNodeRejection) = await TryBroadcastBlockCypher(cleanHex, useTestNet);
            if (success) return txid;
            lastError = err;
            if (isNodeRejection) throw new Exception(err);

            var mp = await TryBroadcastMempool(cleanHex, localTxHash, useTestNet);
            if (mp.success) return mp.txid;
            if (mp.isNodeRejection) throw new Exception(mp.err);

            throw new Exception($"Transaction Failed: {lastError}");
        }
        else
        {
            // Default: Mempool.space
            var (success, txid, err, isNodeRejection) = await TryBroadcastMempool(cleanHex, localTxHash, useTestNet);
            if (success) return txid;
            lastError = err;
            if (isNodeRejection)
            {
                throw new Exception(err);
            }

            var bs = await TryBroadcastBlockstream(cleanHex, localTxHash, useTestNet);
            if (bs.success) return bs.txid;
            if (bs.isNodeRejection) throw new Exception(bs.err);

            var bcy = await TryBroadcastBlockCypher(cleanHex, useTestNet);
            if (bcy.success) return bcy.txid;
            throw new Exception($"Transaction Failed: {lastError}. Fallback: {bcy.err}");
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

    /// <summary>
    /// Parse TxData to TransactionDisplayData for UI display
    /// </summary>
    private TransactionDisplayData ParseTxDataToDisplay(TxData tx, string address)
    {
        if (tx == null) return null;

        // Calculate net change amount for this address
        decimal inputSum = 0;
        decimal outputSum = 0;

        // Sum inputs that belong to us (money we spent)
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

        // Sum outputs that belong to us (money received or change returned)
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
        
        // If difference is zero (e.g., internal transfer), ignore it
        if (difference == 0) return null;

        TransactionDisplayData txData = new TransactionDisplayData();
        txData.txHash = tx.hash;
        txData.date = tx.confirmed.HasValue ? tx.confirmed.Value.ToString("yyyy-MM-dd HH:mm") : "Unconfirmed";
        
        // Convert satoshi to BTC
        decimal amountBTC = Math.Abs(difference) / 100000000m;
        txData.amount = amountBTC.ToString("F8");
        txData.fee = (tx.fees / 100000000m).ToString("F8");

        if (difference > 0)
        {
            txData.type = "Receive";
            // Find sender address (first input)
            if (tx.inputs != null && tx.inputs.Count > 0 && tx.inputs[0].addresses != null)
                txData.otherAddress = tx.inputs[0].addresses[0];
            else
                txData.otherAddress = "Unknown";
        }
        else
        {
            txData.type = "Send";
            // Find recipient address (first output that's not ours)
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

    // --- Data Classes ---

    public class TransactionDisplayData
    {
        public string type;
        public string amount;
        public string date;
        public string otherAddress;
        public string txHash;
        public string fee;
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
        public long fees;
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

    // New UTXO Classes
    public class UTXO 
    {
        public string txHash;
        public int outputIndex;
        public long value; // satoshis
        public string scriptHex;
    }

    public class AddressData
    {
        public string address;
        public long final_balance;
        public List<TxRef> txrefs;
        public List<TxRef> unconfirmed_txrefs;
    }


}