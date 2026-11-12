using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

public class WalletManager2 : MonoBehaviour
{
    private static WalletManager2 instance;
    public static WalletManager2 Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject obj = new GameObject("WalletManager");
                instance = obj.AddComponent<WalletManager2>();
                DontDestroyOnLoad(obj);
            }
            return instance;
        }
    }

    private WalletData currentWallet;
    private string savePath;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            // ✅ PHASE 4: Separate save files for each network
            // savePath will be set when wallet is loaded/created
            LoadWallet();
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    // ✅ Get network-specific save path based on Build Variant
    private string GetSavePath(bool isTestNetParam)
    {
#if UNITY_EDITOR
        // In Editor mode, use a single shared wallet file so switching variants doesn't require re-import
        return Application.persistentDataPath + "/wallet_editor.json";
#else
        // In builds, use separate files per network
        if (isTestNetParam)
            return Application.persistentDataPath + "/wallet_testnet.json";
        else
            return Application.persistentDataPath + "/wallet_mainnet.json";
#endif
    }

    // ─────────────────────────────────────────────────
    // TESTNET ADDRESS SUPPORT
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the Testnet address derived from the SAME key as the mainnet wallet.
    /// The Testnet address is computed once and then cached in WalletData.
    public string GetAddress()
    {
#if TESTNET_BUILD
        return GetTestnetAddress();
#else
        if (currentWallet != null && (currentWallet.isTestNet || (!string.IsNullOrEmpty(currentWallet.address) && currentWallet.address.StartsWith("tb1"))))
            return GetTestnetAddress();
        return currentWallet?.address ?? string.Empty;
#endif
    }

    public string GetTestnetAddress()
    {
        if (currentWallet == null) return string.Empty;

        // If cached value exists AND it is already a SegWit (tb1) address, return it
        if (!string.IsNullOrEmpty(currentWallet.testnetAddress) &&
            currentWallet.testnetAddress.StartsWith("tb1"))
            return currentWallet.testnetAddress;

        // If currentWallet.address is ALREADY a testnet SegWit address (tb1), use it!
        if (!string.IsNullOrEmpty(currentWallet.address) &&
            currentWallet.address.StartsWith("tb1"))
        {
            currentWallet.testnetAddress = currentWallet.address;
            if (!string.IsNullOrEmpty(currentWallet.privateKey))
                currentWallet.testnetPrivateKey = currentWallet.privateKey;
            return currentWallet.testnetAddress;
        }

        // Otherwise re-derive (upgrades old Legacy m/n cache to SegWit tb1)
        currentWallet.testnetAddress   = string.Empty;
        currentWallet.testnetPrivateKey = string.Empty;
        EnsureTestnetAddress();
        return currentWallet.testnetAddress ?? string.Empty;
    }

    /// <summary>
    /// Derives the Testnet Native SegWit address (BIP84, tb1...) from the same
    /// seed phrase or private key as the mainnet wallet, then caches the result.
    /// </summary>
    public void EnsureTestnetAddress()
    {
        if (currentWallet == null) return;

        // If currentWallet.address is already a testnet SegWit address (tb1), use it directly
        if (!string.IsNullOrEmpty(currentWallet.address) && currentWallet.address.StartsWith("tb1"))
        {
            currentWallet.testnetAddress = currentWallet.address;
            if (!string.IsNullOrEmpty(currentWallet.privateKey))
                currentWallet.testnetPrivateKey = currentWallet.privateKey;
            return;
        }

        try
        {
            string testnetAddr = string.Empty;
            string testnetPK   = string.Empty;

            if (!string.IsNullOrEmpty(currentWallet.seedPhrase))
            {
                // ── BIP84 SegWit path: m/84'/1'/0'/0/0 (coin_type=1 for testnet) ──
                var mnemonic  = new NBitcoin.Mnemonic(currentWallet.seedPhrase, NBitcoin.Wordlist.English);
                var masterKey = mnemonic.DeriveExtKey();
                var keyPath   = new NBitcoin.KeyPath("m/84'/1'/0'/0/0");
                var privKey   = masterKey.Derive(keyPath).PrivateKey;

                testnetAddr = privKey.PubKey
                    .GetAddress(NBitcoin.ScriptPubKeyType.Segwit, NBitcoin.Network.TestNet)
                    .ToString(); // → tb1...
                testnetPK = privKey.GetBitcoinSecret(NBitcoin.Network.TestNet).ToString();
            }
            else if (!string.IsNullOrEmpty(currentWallet.privateKey))
            {
                // Import path: same raw private key → SegWit on testnet
                try
                {
                    NBitcoin.Key privKey;
                    try
                    {
                        // Try WIF (mainnet or testnet)
                        NBitcoin.BitcoinSecret secret = null;
                        try {
                            secret = new NBitcoin.BitcoinSecret(currentWallet.privateKey, NBitcoin.Network.Main);
                        } catch {
                            secret = new NBitcoin.BitcoinSecret(currentWallet.privateKey, NBitcoin.Network.TestNet);
                        }
                        privKey = secret.PrivateKey;
                    }
                    catch
                    {
                        // Fallback: hex-encoded raw key
                        var keyBytes = NBitcoin.DataEncoders.Encoders.Hex.DecodeData(currentWallet.privateKey);
                        privKey = new NBitcoin.Key(keyBytes);
                    }

                    testnetAddr = privKey.PubKey
                        .GetAddress(NBitcoin.ScriptPubKeyType.Segwit, NBitcoin.Network.TestNet)
                        .ToString(); // → tb1...
                    testnetPK = privKey.GetBitcoinSecret(NBitcoin.Network.TestNet).ToString();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[WalletManager2] Cannot derive testnet SegWit address: {ex.Message}");
                    return;
                }
            }

            if (!string.IsNullOrEmpty(testnetAddr))
            {
                currentWallet.testnetAddress    = testnetAddr;
                currentWallet.testnetPrivateKey = testnetPK;

                // Persist — ✅ FIX: use correct network path, not always MainNet
                try
                {
                    if (string.IsNullOrEmpty(savePath))
                    {
#if TESTNET_BUILD
                        savePath = GetSavePath(true);  // TestNet build → testnet file
#else
                        savePath = GetSavePath(false); // MainNet build → mainnet file
#endif
                    }

                    string json = JsonUtility.ToJson(currentWallet, true);
                    File.WriteAllText(savePath, json);
                    Debug.Log($"[WalletManager2] Testnet SegWit address: {testnetAddr}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[WalletManager2] Could not save testnet address: {ex.Message}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WalletManager2] EnsureTestnetAddress error: {ex.Message}");
        }
    }

    public void SaveWallet(string seedPhrase, bool isTestNet, string address, string privateKey)
    {
        // Allow empty seedPhrase for private key imports, but address is required
        if (string.IsNullOrEmpty(address))
        {
            Debug.LogError("Cannot save wallet: No address provided");
            return;
        }
        
        bool isPKImport = string.IsNullOrEmpty(seedPhrase);
        
        // Record creation time for verification if not present
        string createKey = "WalletCreatedTime_" + address;
        if (!PlayerPrefs.HasKey(createKey))
        {
            long nowSec = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            PlayerPrefs.SetString(createKey, nowSec.ToString());
            PlayerPrefs.Save();
            Debug.Log($"[WalletManager2] Recorded wallet creation time for {address}: {nowSec}");
        }
        
        // ✅ Use network-specific save path based on variant
#if TESTNET_BUILD
        isTestNet = true;
#endif
        if (!string.IsNullOrEmpty(address) && address.StartsWith("tb1"))
            isTestNet = true;

        savePath = GetSavePath(isTestNet); 
        
        currentWallet = new WalletData
        {
            seedPhrase = seedPhrase ?? "",
#if TESTNET_BUILD
            isTestNet = true,
#else
            isTestNet = isTestNet,
#endif
            address = address,
            privateKey = privateKey,
            isPrivateKeyImport = isPKImport,
            parentAddress = address, // Store the very first address as parent (never changes)
            currentAddressIndex = 1, // MainNet default index
            generatedAddresses = new System.Collections.Generic.List<GeneratedAddress>()
        };

        if (address.StartsWith("tb1"))
        {
            currentWallet.testnetAddress = address;
            currentWallet.testnetPrivateKey = privateKey;
        }

        if (currentWallet.transactions == null)
        {
            currentWallet.transactions = new System.Collections.Generic.List<TransactionHistory>();
        }

        try
        {
            string json = JsonUtility.ToJson(currentWallet, true);
            File.WriteAllText(savePath, json);
            Debug.Log($"✅ Wallet saved to: {savePath} (PK Import: {isPKImport})");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to save wallet: {ex.Message}");
        }
    }

    public void LoadWallet()
    {
        // ✅ Try to find wallet on current build network first
#if TESTNET_BUILD
        bool defaultTestNet = true;
#else
        bool defaultTestNet = false;
#endif
        string path = GetSavePath(defaultTestNet);
        
        try
        {
            // Fallback: If in TestNet build but no wallet_testnet.json exists, check if mainnet wallet exists
            if (!File.Exists(path) && defaultTestNet)
            {
                string fallbackPath = GetSavePath(false);
                if (File.Exists(fallbackPath))
                {
                    path = fallbackPath;
                    Debug.Log($"[WalletManager2] TestNet file not found, loading base wallet from {path}");
                }
            }

            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                if (!string.IsNullOrEmpty(json))
                {
                    WalletData wallet = JsonUtility.FromJson<WalletData>(json);
                    // Accept wallet if it has a seed phrase OR a private key (PK import)
                    if (wallet != null && (!string.IsNullOrEmpty(wallet.seedPhrase) || !string.IsNullOrEmpty(wallet.privateKey)))
                    {
#if TESTNET_BUILD
                        wallet.isTestNet = true; 
#else
                        if (wallet.address != null && wallet.address.StartsWith("tb1"))
                            wallet.isTestNet = true;
#endif
                        currentWallet = wallet;
                        savePath = GetSavePath(defaultTestNet);
                        Debug.Log($"✅ Wallet loaded from: {path} (PK Import: {wallet.isPrivateKeyImport})");
                        EnsureTestnetAddress();
                        return;
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to load wallet from {path}: {ex.Message}");
        }
        
        Debug.Log("No saved wallet found");
        currentWallet = null;
    }

    public WalletData GetCurrentWallet()
    {
        return currentWallet;
    }

    public bool HasWallet()
    {
        // Return true if wallet has seed phrase OR private key (for PK imports)
        return currentWallet != null && 
               (!string.IsNullOrEmpty(currentWallet.seedPhrase) || !string.IsNullOrEmpty(currentWallet.privateKey));
    }

    public void ToggleNetwork()
    {
        Debug.Log("Network Toggle Removed (MainNet Only)");
    }
    // ✅ Single balance changed event — all UI screens subscribe to this
    // This ensures every screen shows the SAME value at all times
    public static System.Action<double, string> OnBalanceChanged;

    public void UpdateBalance(double newBalance)
    {
        if (currentWallet == null) return;
        currentWallet.balance      = newBalance;
        currentWallet.cachedBalance = newBalance;
        currentWallet.lastBalanceUpdate = System.DateTime.Now.ToString("HH:mm:ss");

#if TESTNET_BUILD
        currentWallet.cachedTestnetBalance = newBalance;
        string currency = "tBTC";
#else
        string currency = "BTC";
#endif
        currentWallet.hasCompletedInitialScan = true;
        savePath = GetSavePath(currentWallet.isTestNet);

        try {
            string json = JsonUtility.ToJson(currentWallet, true);
            File.WriteAllText(savePath, json);
            Debug.Log($"[WalletManager2] ✅ Balance saved: {newBalance:F8} {currency}");
        } catch (System.Exception ex) { Debug.LogError("Save Error: " + ex.Message); }

        // 🔔 Notify ALL subscribers (Dashboard, SendReceiveUI, MiningUI, etc.)
        OnBalanceChanged?.Invoke(newBalance, currency);
    }

    /// <summary>
    /// Get the next available address index for generation
    /// </summary>
    public int GetNextAddressIndex()
    {
        if (currentWallet == null) return 0;
        currentWallet.currentAddressIndex++;
        return currentWallet.currentAddressIndex;
    }

    /// <summary>
    /// Add a newly generated address to the wallet and save to file
    /// </summary>
    public void AddGeneratedAddress(string address, int index)
    {
        if (currentWallet == null) return;
        
        if (currentWallet.generatedAddresses == null)
            currentWallet.generatedAddresses = new List<GeneratedAddress>();
        
        GeneratedAddress genAddr = new GeneratedAddress
        {
            address = address,
            index = index,
            dateGenerated = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        };
        
        currentWallet.generatedAddresses.Add(genAddr);
        
        // Save to file
        try {
            savePath = GetSavePath(currentWallet.isTestNet);
            string json = JsonUtility.ToJson(currentWallet, true);
            File.WriteAllText(savePath, json);
            Debug.Log($"Address saved: {address} (index {index}), total: {currentWallet.generatedAddresses.Count}");
        } catch (System.Exception ex) { Debug.LogError("Save Address Error: " + ex.Message); }
    }

    /// <summary>
    /// Update the main wallet address
    /// </summary>
    public void UpdateMainAddress(string newAddress)
    {
        if (currentWallet == null) return;
        currentWallet.address = newAddress;
        
        // Save to file
        try {
            savePath = GetSavePath(currentWallet.isTestNet);
            string json = JsonUtility.ToJson(currentWallet, true);
            File.WriteAllText(savePath, json);
            Debug.Log($"Main address updated to: {newAddress}");
        } catch (System.Exception ex) { Debug.LogError("Save Error: " + ex.Message); }
    }

    public async void SyncTransactionHistory()
    {
        if (currentWallet == null) return;

        // ✅ FIX: Use correct network based on wallet setting
        bool isTestNet = currentWallet.isTestNet;
#if TESTNET_BUILD
        isTestNet = true;
#endif
        var network  = isTestNet ? NBitcoin.Network.TestNet : NBitcoin.Network.Main;
        int coinType = isTestNet ? 1 : 0;

        Debug.Log($"Syncing Transaction History ({(isTestNet ? "TestNet" : "MainNet")}) for all addresses...");
        BitcoinTransactionService service = new BitcoinTransactionService();
        try
        {
            var allHistory = new List<TransactionHistory>();
            var scannedAddresses = new HashSet<string>();

            // 1. Scan main address — must use tb1... (TestNet) or bc1... (MainNet)
#if TESTNET_BUILD
            EnsureTestnetAddress();
            string mainScanAddr = currentWallet.testnetAddress;
            // ✅ CRITICAL FIX: Never fall back to mainnet bc1 address on testnet API!
            // If testnetAddress is empty, skip rather than mixing networks
            if (string.IsNullOrEmpty(mainScanAddr))
            {
                Debug.LogWarning("[SyncHistory] No testnet address available — skipping main address scan");
                mainScanAddr = null;
            }
#else
            string mainScanAddr = currentWallet.address;
#endif
            if (!string.IsNullOrEmpty(mainScanAddr))
                await ScanAddressHistory(service, mainScanAddr, allHistory, scannedAddresses, isTestNet);

            // 2. Scan derived addresses (Legacy BIP44 + SegWit BIP84, first 5 each)
            if (!string.IsNullOrEmpty(currentWallet.seedPhrase))
            {
                try
                {
                    var mnemonic  = new NBitcoin.Mnemonic(currentWallet.seedPhrase, NBitcoin.Wordlist.English);
                    var masterKey = mnemonic.DeriveExtKey();

                    // Legacy addresses (BIP44) - correct coinType
                    for (int i = 0; i < 5; i++)
                    {
                        var keyPath = new NBitcoin.KeyPath($"m/44'/{coinType}'/0'/0/{i}");
                        var key  = masterKey.Derive(keyPath).PrivateKey;
                        string addr = key.PubKey.GetAddress(NBitcoin.ScriptPubKeyType.Legacy, network).ToString();
                        int count = await ScanAddressHistory(service, addr, allHistory, scannedAddresses, isTestNet);
                        if (count == 0) break;
                    }

                    // SegWit addresses (BIP84) - correct coinType
                    for (int i = 0; i < 5; i++)
                    {
                        var keyPath = new NBitcoin.KeyPath($"m/84'/{coinType}'/0'/0/{i}");
                        var key  = masterKey.Derive(keyPath).PrivateKey;
                        string addr = key.PubKey.GetAddress(NBitcoin.ScriptPubKeyType.Segwit, network).ToString();
                        int count = await ScanAddressHistory(service, addr, allHistory, scannedAddresses, isTestNet);
                        if (count == 0) break;
                    }
                }
                catch (System.Exception ex) { Debug.LogWarning($"Derived address scan error: {ex.Message}"); }
            }

            // 3. Scan user-generated addresses
            if (currentWallet.generatedAddresses != null)
            {
                foreach (var genAddr in currentWallet.generatedAddresses)
                    await ScanAddressHistory(service, genAddr.address, allHistory, scannedAddresses, isTestNet);
            }

            if (allHistory.Count > 0)
            {
                currentWallet.transactions = allHistory;
                try {
                    string json = JsonUtility.ToJson(currentWallet, true);
                    File.WriteAllText(savePath, json);
                    Debug.Log($"Synced {allHistory.Count} transactions from {scannedAddresses.Count} addresses.");
                } catch {}
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Sync History Failed: " + ex.Message);
        }
    }
    
    private async System.Threading.Tasks.Task<int> ScanAddressHistory(
        BitcoinTransactionService service, string address,
        List<TransactionHistory> allHistory, HashSet<string> scanned,
        bool isTestNet = false)  // ✅ FIX: network flag
    {
        if (string.IsNullOrEmpty(address) || scanned.Contains(address)) return 0;
        scanned.Add(address);

        try
        {
            // ✅ FIX: pass isTestNet to GetTransactionHistory
            var history = await service.GetTransactionHistory(address, isTestNet);
            if (history != null && history.Count > 0)
            {
                foreach (var tx in history)
                {
                    bool isDuplicate = false;
                    foreach (var existing in allHistory)
                    {
                        if (existing.txHash == tx.txHash) { isDuplicate = true; break; }
                    }
                    if (!isDuplicate) allHistory.Add(tx);
                }
                await System.Threading.Tasks.Task.Delay(500);
                return history.Count;
            }
            await System.Threading.Tasks.Task.Delay(350);
        }
        catch (System.Exception ex) { Debug.LogWarning($"History scan error for {address}: {ex.Message}"); }
        return 0;
    }

    public void AddTransaction(string type, string address, string amount, string txHash = "")
    {
        if (currentWallet == null)
        {
            Debug.LogWarning("Cannot add transaction: No wallet loaded");
            return;
        }
        
        if (currentWallet.transactions == null)
        {
            currentWallet.transactions = new System.Collections.Generic.List<TransactionHistory>();
        }
        
        try
        {
            TransactionHistory tx = new TransactionHistory
            {
                type = type,
                address = address,
                amount = amount,
                date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                txHash = txHash
            };

            currentWallet.transactions.Add(tx);
            string json = JsonUtility.ToJson(currentWallet, true);
            File.WriteAllText(savePath, json);
            Debug.Log($"Transaction added: {type} {amount} BTC");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to add transaction: {ex.Message}");
        }
    }

    public void DeleteWallet()
    {
        try
        {
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
                Debug.Log("Wallet deleted");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to delete wallet: {ex.Message}");
        }
        finally
        {
            currentWallet = null;
        }
    }

    public void SwitchNetwork(bool toTestNet)
    {
        // DISABLED - This App is MainNet ONLY
        Debug.LogWarning("[WalletManager2] SwitchNetwork is disabled. This app is MainNet only.");
    }
}
