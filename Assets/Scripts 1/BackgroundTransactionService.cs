using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Background service that persists and completes transactions even if app is minimized/closed.
/// Implements singleton pattern with DontDestroyOnLoad for persistence.
/// </summary>
public class BackgroundTransactionService : MonoBehaviour
{
    private static BackgroundTransactionService instance;
    public static BackgroundTransactionService Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("BackgroundTransactionService");
                instance = go.AddComponent<BackgroundTransactionService>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    [Serializable]
    private class PendingTransaction
    {
        public string id;
        public string seedPhrase;
        public string privateKey; // Added privateKey support
        public bool isTestNet;
        public string toAddress;
        public string amount; // Stored as string to avoid decimal serialization issues
        public string fee;
        public int attemptCount;
        public long timestamp;
        public string status; // "pending", "broadcasting", "completed", "failed"
        public string errorMessage;
        public string txHash;
    }

    [Serializable]
    private class TransactionQueue
    {
        public List<PendingTransaction> transactions = new List<PendingTransaction>();
    }

    private const string QUEUE_KEY = "BTX_Queue";
    private const int MAX_RETRIES = 3;
    private TransactionQueue queue = new TransactionQueue();
    private bool isProcessing = false;
    private BitcoinTransactionService txService;
    private Dictionary<string, Action<bool, string>> callbacks = new Dictionary<string, Action<bool, string>>();

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        
        txService = new BitcoinTransactionService();
        LoadState();
        
        // Resume processing any pending transactions
        if (queue.transactions.Count > 0)
        {
            Debug.Log($"[BackgroundTxService] Resuming {queue.transactions.Count} pending transactions");
            StartCoroutine(ProcessQueueCoroutine());
        }
    }

    /// <summary>
    /// Queue a new transaction for background processing
    /// </summary>
    public void QueueTransaction(
        string seedPhrase, 
        bool isTestNet, 
        string toAddress, 
        decimal amount, 
        decimal fee, 
        Action<bool, string> onComplete,
        string privateKey = null) // Added privateKey parameter
    {
        string txId = Guid.NewGuid().ToString();
        
        PendingTransaction tx = new PendingTransaction
        {
            id = txId,
            seedPhrase = seedPhrase,
            privateKey = privateKey, // Store private key
            isTestNet = isTestNet,
            toAddress = toAddress,
            amount = amount.ToString(),
            fee = fee.ToString(),
            attemptCount = 0,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            status = "pending"
        };

        queue.transactions.Add(tx);
        callbacks[txId] = onComplete;
        SaveState();

        Debug.Log($"[BackgroundTxService] Queued transaction {txId}: {amount} to {toAddress}");

        // Start processing if not already running
        if (!isProcessing)
        {
            StartCoroutine(ProcessQueueCoroutine());
        }
    }

    private IEnumerator ProcessQueueCoroutine()
    {
        isProcessing = true;

        while (queue.transactions.Count > 0)
        {
            PendingTransaction tx = queue.transactions[0];

            // Skip if already completed or failed with max retries
            if (tx.status == "completed" || (tx.status == "failed" && tx.attemptCount >= MAX_RETRIES))
            {
                queue.transactions.RemoveAt(0);
                SaveState();
                continue;
            }

            Debug.Log($"[BackgroundTxService] Processing transaction {tx.id} (attempt {tx.attemptCount + 1}/{MAX_RETRIES})");

            tx.status = "broadcasting";
            tx.attemptCount++;
            SaveState();

            // Process transaction asynchronously
            Task<(bool success, string result)> processTask = ProcessTransactionAsync(tx);
            
            // Wait for completion
            while (!processTask.IsCompleted)
            {
                yield return null;
            }

            var (success, result) = processTask.Result;

            if (success)
            {
                tx.status = "completed";
                tx.txHash = result;
                Debug.Log($"✅ [BackgroundTxService] Transaction {tx.id} completed: {result}");

                if (callbacks.ContainsKey(tx.id))
                {
                    callbacks[tx.id]?.Invoke(true, result);
                    callbacks.Remove(tx.id);
                }

                queue.transactions.RemoveAt(0);
                SaveState();
            }
            else
            {
                tx.errorMessage = result;

                // ✅ FIX: Detect PERMANENT failures — do NOT retry these
                bool isPermanent =
                    result.Contains("Insufficient funds") ||
                    result.Contains("No Unspent Outputs") ||
                    result.Contains("Invalid Bitcoin address") ||
                    result.Contains("signing failed") ||
                    result.Contains("does not match") ||
                    result.Contains("dust") ||
                    result.Contains("Amount must be greater");

                if (isPermanent || tx.attemptCount >= MAX_RETRIES)
                {
                    tx.status = "failed";
                    string reason = isPermanent ? "permanent error (no retry)" : $"after {MAX_RETRIES} attempts";
                    Debug.LogError($"❌ [BackgroundTxService] Transaction {tx.id} failed ({reason}): {result}");

                    if (callbacks.ContainsKey(tx.id))
                    {
                        callbacks[tx.id]?.Invoke(false, result);
                        callbacks.Remove(tx.id);
                    }

                    queue.transactions.RemoveAt(0);
                    SaveState();
                }
                else
                {
                    tx.status = "pending";
                    SaveState();

                    // Exponential backoff: 5s, 15s, 45s — only for transient network errors
                    float delay = 5f * Mathf.Pow(3f, tx.attemptCount - 1);
                    Debug.LogWarning($"⚠️ [BackgroundTxService] Transaction {tx.id} failed (attempt {tx.attemptCount}), retrying in {delay}s: {result}");
                    yield return new WaitForSeconds(delay);
                }
            }
        }

        isProcessing = false;
        Debug.Log("[BackgroundTxService] Queue processing completed");
    }

    private async Task<(bool success, string result)> ProcessTransactionAsync(PendingTransaction tx)
    {
        try
        {
            decimal amount = decimal.Parse(tx.amount);
            decimal fee = decimal.Parse(tx.fee);

            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60)); // 60 second timeout

            // Get generated addresses from wallet for UTXO scanning
            List<GeneratedAddress> genAddresses = null;
            WalletData wallet = WalletManager2.Instance.GetCurrentWallet();
            if (wallet != null) genAddresses = wallet.generatedAddresses;

            string txHash = await txService.SendBitcoin(
                tx.seedPhrase,
                tx.isTestNet,
                tx.toAddress,
                amount,
                fee,
                null, // No status callback in background
                cts.Token,
                genAddresses,
                tx.privateKey // Pass private key to service
            );

            return (true, txHash);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private void SaveState()
    {
        try
        {
            string json = JsonUtility.ToJson(queue);
            PlayerPrefs.SetString(QUEUE_KEY, json);
            PlayerPrefs.Save();
            Debug.Log($"[BackgroundTxService] State saved: {queue.transactions.Count} transactions");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BackgroundTxService] Failed to save state: {ex.Message}");
        }
    }

    private void LoadState()
    {
        try
        {
            if (PlayerPrefs.HasKey(QUEUE_KEY))
            {
                string json = PlayerPrefs.GetString(QUEUE_KEY);
                queue = JsonUtility.FromJson<TransactionQueue>(json);
                
                if (queue == null)
                {
                    queue = new TransactionQueue();
                }

                Debug.Log($"[BackgroundTxService] State loaded: {queue.transactions.Count} transactions");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BackgroundTxService] Failed to load state: {ex.Message}");
            queue = new TransactionQueue();
        }
    }

    /// <summary>
    /// Get current queue status for debugging
    /// </summary>
    public string GetQueueStatus()
    {
        if (queue.transactions.Count == 0)
        {
            return "Queue empty";
        }

        string status = $"Queue: {queue.transactions.Count} transactions\n";
        foreach (var tx in queue.transactions)
        {
            status += $"- {tx.id}: {tx.status} (attempt {tx.attemptCount})\n";
        }
        return status;
    }

    /// <summary>
    /// Clear all failed transactions from queue
    /// </summary>
    public void ClearFailedTransactions()
    {
        queue.transactions.RemoveAll(tx => tx.status == "failed");
        SaveState();
        Debug.Log("[BackgroundTxService] Cleared failed transactions");
    }

    void OnApplicationQuit()
    {
        SaveState();
        Debug.Log("[BackgroundTxService] Application quitting, state saved");
    }
}
