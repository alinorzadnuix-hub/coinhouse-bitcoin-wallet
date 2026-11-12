using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

public class MiningUI : MonoBehaviour
{
    private Text hashrateText, sharesText, timeText, hashDisplayText;
    private Text logText, statusText, cpuPercentText, threadCountText;
    private Text testnetAddrLabel, toggleButtonText, tbtcBalanceText;
    private Text countdownText; // Live faucet countdown banner
    private InputField addressInput;
    private Slider threadSlider, cpuSlider;
    private Button toggleButton;
    private GameObject configContent, logContent;
    private bool configExpanded = true, logExpanded = true;
    private Text cfgArrow, logArrow;

    private bool isMining = false;
    private int threadCount = 1, cpuPercent = 50;
    private long totalHashes = 0, sessionHashes = 0;
    private double hashrate = 0;
    private volatile string lastHashHex = "";
    private Stopwatch sessionTimer;
    private CancellationTokenSource cts;

    private const string FAUCET_URL   = "http://89.106.206.25:2500/api/claim"; // Correct endpoint
    private const string DOWNLOAD_URL = "http://89.106.206.25:2500";
    private const string MEMPOOL_TESTNET = "https://mempool.space/testnet/api/address/";
    private const long   FAUCET_AMOUNT = 14000000; // satoshis (0.14 tBTC)
    private float faucetTimer = 0f, faucetInterval = 60f;
    private float faucetBackoff = 0f;   // extra wait after error
    private int   consecutiveFails = 0; // stop after 3 in a row
    private bool  claimInProgress  = false;
    private string testnetAddress = "";
    private string walletAddress  = "";
    private string mainnetAddress = ""; // For TESTNET_BUILD: derived mainnet address from same seed

    private const float RW = 1080f, RH = 1920f;

    void Start()
    {
        threadCount = 1; cpuPercent = 50;
        if (WalletManager2.Instance.HasWallet())
        {
            walletAddress = WalletManager2.Instance.GetCurrentWallet().address;
            Debug.Log($"[MiningUI] Main wallet address: {walletAddress}");
            WalletManager2.Instance.EnsureTestnetAddress();
            testnetAddress = WalletManager2.Instance.GetTestnetAddress();
            Debug.Log($"[MiningUI] Testnet address initialized: {testnetAddress}");

#if TESTNET_BUILD
            // Derive the MainNet (BIP84 SegWit) address from the same seed for display
            try
            {
                var w = WalletManager2.Instance.GetCurrentWallet();
                if (w != null && !string.IsNullOrEmpty(w.seedPhrase))
                {
                    var mn = new NBitcoin.Mnemonic(w.seedPhrase, NBitcoin.Wordlist.English);
                    var mk = mn.DeriveExtKey();
                    var kp = new NBitcoin.KeyPath("m/84'/0'/0'/0/0"); // mainnet coinType=0
                    var pk = mk.Derive(kp).PrivateKey;
                    mainnetAddress = pk.PubKey.GetAddress(NBitcoin.ScriptPubKeyType.Segwit, NBitcoin.Network.Main).ToString();
                }
                else if (w != null && !string.IsNullOrEmpty(w.privateKey))
                {
                    NBitcoin.Key pk;
                    try
                    {
                        NBitcoin.BitcoinSecret s;
                        try { s = new NBitcoin.BitcoinSecret(w.privateKey, NBitcoin.Network.TestNet); }
                        catch { s = new NBitcoin.BitcoinSecret(w.privateKey, NBitcoin.Network.Main); }
                        pk = s.PrivateKey;
                    }
                    catch
                    {
                        var bytes = NBitcoin.DataEncoders.Encoders.Hex.DecodeData(w.privateKey);
                        pk = new NBitcoin.Key(bytes);
                    }
                    mainnetAddress = pk.PubKey.GetAddress(NBitcoin.ScriptPubKeyType.Segwit, NBitcoin.Network.Main).ToString();
                }
            }
            catch (System.Exception ex) { Debug.LogWarning($"[MiningUI] Mainnet derive error: {ex.Message}"); }
#endif
        }
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
        BuildUI();
        InvokeRepeating("UpdateStats", 1f, 1f);
        StartCoroutine(FetchTestnetBalance());
    }

    void OnDestroy() { StopMining(); WalletManager2.OnBalanceChanged -= OnCentralBalanceChanged; }

    void OnEnable()  { WalletManager2.OnBalanceChanged += OnCentralBalanceChanged; }
    void OnDisable() { WalletManager2.OnBalanceChanged -= OnCentralBalanceChanged; }

    void OnCentralBalanceChanged(double newBalance, string currency)
    {
#if TESTNET_BUILD
        // The yellow mining balance is always the TestNet amount → always label it tBTC
        if (tbtcBalanceText != null)
            tbtcBalanceText.text = $"{newBalance:F8} tBTC";
#else
        // MainNet build: central balance events carry the BTC balance —
        // never overwrite the yellow tBTC mining display with it.
        // (FetchTestnetBalance updates tbtcBalanceText directly.)
#endif
    }

    void Update()
    {
        if (!isMining)
        {
            if (countdownText != null) countdownText.text = LocalizationManager.Instance.Get("MINE_BANNER");
            return;
        }

        // Count down backoff separately
        if (faucetBackoff > 0f) { faucetBackoff -= Time.deltaTime; return; }

        // Stop auto-claiming after 3 consecutive failures
        if (consecutiveFails >= 3) return;

        // Claim is already in flight – do not queue another
        if (claimInProgress) return;

        faucetTimer += Time.deltaTime;

        // Update live countdown in the banner
        float remaining = faucetInterval - faucetTimer;
        if (remaining < 0) remaining = 0;
        if (countdownText != null)
            countdownText.text = LocalizationManager.Instance.GetFormat("MINE_NEXT_CLAIM", Mathf.CeilToInt(remaining));

        if (faucetTimer >= faucetInterval)
        {
            faucetTimer = 0f;
            // Send notification that a claim is being made
            if (NotificationManager.Instance != null)
                NotificationManager.Instance.SendNotification("TestNet Faucet", "Claiming BTCT from faucet...");
            StartCoroutine(ClaimFaucet());
        }
    }

    // 60 seconds wait between claims
    float CalcInterval() => 60f; // Fixed 60 seconds

    // ─── MINING ──────────────────────────────────────────────────
    void ToggleMining() { if (isMining) StopMining(); else StartMining(); UpdateUIState(); }

    void StartMining()
    {
        if (isMining) return;
        isMining = true; sessionHashes = 0;
        
        Application.runInBackground = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        
        sessionTimer = Stopwatch.StartNew();
        cts = new CancellationTokenSource();
        faucetInterval    = CalcInterval();
        faucetTimer       = 0f;   // Always start from 0 — first claim after exactly 60s
        faucetBackoff     = 0f;
        consecutiveFails  = 0;
        claimInProgress   = false;
        statusText.text = LocalizationManager.Instance.GetFormat("MINE_STATUS_MINING", threadCount);
        statusText.color = ThemeManager.Instance.GetSuccessColor();
        sharesText.text = LocalizationManager.Instance.Get("MINE_STATUS_MINING_SHORT");
        sharesText.color = ThemeManager.Instance.GetSuccessColor();
        Log($"Started {threadCount} thread(s) @ {cpuPercent}% CPU");
        Log($"Faucet claim every {faucetInterval:F0}s → {Tr(testnetAddress, 14)}");
        if (NotificationManager.Instance) NotificationManager.Instance.SendNotification("Bitcoin Miner", "Mining Active");
        for (int i = 0; i < threadCount; i++) { int id = i, p = cpuPercent; Task.Run(() => MineLoop(cts.Token, id, p), cts.Token); }
    }

    void StopMining()
    {
        if (!isMining) return;
        isMining = false; cts?.Cancel(); sessionTimer?.Stop();
        
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
        
        if (NotificationManager.Instance) NotificationManager.Instance.SendNotification("Bitcoin Miner", "Miner Stopped");
        Log("Miner stopped.");
    }

    void MineLoop(CancellationToken tok, int id, int pct)
    {
        using (var sha = SHA256.Create())
        {
            byte[] buf = new byte[80];
            new System.Random(id).NextBytes(buf);
            int sl = pct < 100 ? System.Math.Max(1, (int)((100 - pct) * 0.1f)) : 0, ctr = 0;
            while (!tok.IsCancellationRequested)
            {
                byte[] h = sha.ComputeHash(sha.ComputeHash(buf));
                buf[79]++; if (buf[79] == 0) { buf[78]++; if (buf[78] == 0) buf[77]++; }
                long n = Interlocked.Increment(ref totalHashes);
                Interlocked.Increment(ref sessionHashes);
                if (n % 1000 == 0)
                {
                    var sb = new System.Text.StringBuilder(64);
                    foreach (byte b in h) sb.Append(b.ToString("x2"));
                    lastHashHex = sb.ToString();
                }
                if (sl > 0 && ++ctr >= 100) { ctr = 0; Thread.Sleep(sl); }
            }
        }
    }

    void UpdateStats()
    {
        if (!isMining || sessionTimer == null) return;
        double el = sessionTimer.Elapsed.TotalSeconds;
        if (el <= 0) return;
        hashrate = sessionHashes / el;
        string r;
        if      (hashrate >= 1e9) r = $"{hashrate/1e9:F2} GH/s";
        else if (hashrate >= 1e6) r = $"{hashrate/1e6:F2} MH/s";
        else if (hashrate >= 1e3) r = $"{hashrate/1e3:F2} kH/s";
        else                      r = $"{hashrate:F0} H/s";
        hashrateText.text = r;
        if (hashDisplayText && lastHashHex.Length > 0) hashDisplayText.text = lastHashHex;
        var t = sessionTimer.Elapsed;
        timeText.text = $"{t.Hours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
    }

    IEnumerator ClaimFaucet()
    {
        // Guard: don't overlap requests
        if (claimInProgress) yield break;
        if (string.IsNullOrEmpty(testnetAddress))
        {
            Log("⚠ No testnet address – skipping claim");
            yield break;
        }

        claimInProgress = true;
        Log($"📡 Claiming {FAUCET_AMOUNT} sat → {Tr(testnetAddress, 14)}...");

        // /api/claim respects the amount if sent in body
        string json = $"{{\"address\":\"{testnetAddress}\",\"amount\":{FAUCET_AMOUNT}}}";
        using (var req = new UnityWebRequest(FAUCET_URL, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 20;
            yield return req.SendWebRequest();

            // ── Connection failure ────────────────────────────────
            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                consecutiveFails++;
                faucetBackoff = 300f; // 5 min back-off
                Log($"❌ Cannot reach server ({consecutiveFails}/3). Waiting 5 min...");
                if (consecutiveFails >= 3)
                {
                    Log("⛔ 3 failures in a row – auto-claim paused. Restart mining to retry.");
                    ShowServerError("Faucet server unreachable after 3 attempts.\nAuto-claiming is paused.\nRestart mining to try again.");
                }
                claimInProgress = false;
                yield break;
            }

            // ── HTTP response received ────────────────────────────
            string body = req.downloadHandler.text;
            long   code = req.responseCode;

            if (req.result == UnityWebRequest.Result.Success || code == 200)
            {
                try
                {
                    var resp = JsonUtility.FromJson<FaucetResp>(body);
                    if (resp != null && resp.success)
                    {
                        consecutiveFails = 0; // reset on success
                        float btc = resp.amount / 100000000f;
                        Log($"✅ +{btc:F8} tBTC  TX:{Tr(resp.txHash, 8)}...");
                        WalletManager2.Instance?.AddTransaction(
                            "Received", "Testnet Faucet",
                            btc.ToString("0.00000000"), resp.txHash);
                        
                        // Increment persistent mining claim counter
                        int currentMiningClaims = PlayerPrefs.GetInt("MiningClaimsCount_" + testnetAddress, 0);
                        PlayerPrefs.SetInt("MiningClaimsCount_" + testnetAddress, currentMiningClaims + 1);
                        PlayerPrefs.Save();

                        // After success, wait the full interval again
                        faucetTimer = 0f;
                    }
                    else
                    {
                        consecutiveFails++;
                        string errMsg = resp?.error ?? "unknown";
                        Log($"⚠ Faucet error ({code}): {errMsg}");

                        if (code == 500)
                        {
                            // 500 = server-side tx failure (UTXO unconfirmed etc.)
                            // Wait 5 min before retrying
                            faucetBackoff = 300f;
                            Log("⏳ Waiting 5 min for UTXOs to confirm...");
                        }
                        else
                        {
                            faucetBackoff = 60f; // other errors: wait 1 min
                        }

                        if (consecutiveFails >= 3)
                        {
                            Log("⛔ 3 failures – auto-claim paused. Restart mining to retry.");
                            ShowServerError($"Faucet failed 3 times.\nLast error: {errMsg}\n\nAuto-claiming paused.\nRestart mining to retry.");
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Log($"⚠ Parse error: {e.Message} | Raw: {Tr(body, 60)}");
                }
            }
            else
            {
                consecutiveFails++;
                // Parse the JSON body - server returns {success:false, error:"..."} on 500
                string serverErr = req.error;
                try
                {
                    var errResp = JsonUtility.FromJson<FaucetResp>(body);
                    if (errResp != null && !string.IsNullOrEmpty(errResp.error))
                        serverErr = errResp.error;
                }
                catch { }

                Log($"❌ HTTP {code}: {serverErr}");
                faucetBackoff = code == 500 ? 300f : 60f;
                Log(code == 500 ? "⏳ 5 min wait (UTXO not confirmed yet)..." : "⏳ 1 min wait before retry...");
                if (consecutiveFails >= 3)
                    ShowServerError($"Faucet failed 3 times.\nError: {serverErr}\n\nAuto-claim paused.\nRestart mining to retry.");
            }
        }

        claimInProgress = false;
    }

    [System.Serializable] class MempoolAddrResp { public MempoolChainStats chain_stats; public MempoolChainStats mempool_stats; }
    [System.Serializable] class MempoolChainStats { public long funded_txo_sum; public long spent_txo_sum; }

    System.Collections.IEnumerator FetchTestnetBalance()
    {
        while (true) // Loop forever with delay
        {
            // Try to get testnet address if empty
            if (string.IsNullOrEmpty(testnetAddress))
            {
                if (WalletManager2.Instance.HasWallet())
                {
                    WalletManager2.Instance.EnsureTestnetAddress();
                    testnetAddress = WalletManager2.Instance.GetTestnetAddress();
                    Debug.Log($"[MiningUI] Retrying testnet address lookup: {testnetAddress}");
                }
            }

            if (!string.IsNullOrEmpty(testnetAddress))
            {
                string url = MEMPOOL_TESTNET + testnetAddress;
                Debug.Log($"[MiningUI] Fetching testnet balance from: {url}");
                using (var req = UnityWebRequest.Get(url))
                {
                    req.timeout = 15;
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            var data = JsonUtility.FromJson<MempoolAddrResp>(req.downloadHandler.text);
                            Debug.Log($"[MiningUI] Mempool response: funded={data?.chain_stats?.funded_txo_sum}, spent={data?.chain_stats?.spent_txo_sum}");
                            if (data != null && data.chain_stats != null)
                            {
                                long satoshi = data.chain_stats.funded_txo_sum - data.chain_stats.spent_txo_sum;
                                if (data.mempool_stats != null)
                                    satoshi += data.mempool_stats.funded_txo_sum - data.mempool_stats.spent_txo_sum;
                                double tbtc = satoshi / 100000000.0;
#if TESTNET_BUILD
                                // ✅ Use UpdateBalance so ALL screens sync via OnBalanceChanged event
                                if (WalletManager2.Instance != null && WalletManager2.Instance.HasWallet())
                                    WalletManager2.Instance.UpdateBalance(tbtc);
                                else if (tbtcBalanceText != null)
                                    tbtcBalanceText.text = $"{tbtc:F8} tBTC";
#else
                                // MainNet build: NEVER call UpdateBalance here — it would overwrite
                                // the real BTC balance (fetched from the API) with the testnet amount.
                                // Only update the local yellow tBTC display on this screen.
                                if (tbtcBalanceText != null)
                                    tbtcBalanceText.text = $"{tbtc:F8} tBTC";
#endif
                                Debug.Log($"[MiningUI] Balance updated: {tbtc:F8} tBTC");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"[MiningUI] Error parsing balance: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[MiningUI] Failed to fetch balance: {req.result}, {req.error}");
                    }
                }
            }
            else
            {
                Debug.LogWarning("[MiningUI] Testnet address is empty, cannot fetch balance");
            }

            yield return new WaitForSeconds(10f); // Retry every 10 seconds
        }
    }

    // Show a brief red error banner on screen
    void ShowServerError(string message)
    {
        // Find canvas
        var canvasObj = GameObject.Find("Canvas");
        if (canvasObj == null) return;

        // Remove existing error banner if any
        var old = canvasObj.transform.Find("ErrBanner");
        if (old != null) Destroy(old.gameObject);

        var banner = new GameObject("ErrBanner");
        banner.transform.SetParent(canvasObj.transform, false);
        var r = banner.AddComponent<RectTransform>();
        r.anchorMin = new Vector2(0.5f, 0f);
        r.anchorMax = new Vector2(0.5f, 0f);
        r.anchoredPosition = new Vector2(0, 150);
        r.sizeDelta = new Vector2(950, 200);
        banner.AddComponent<Image>().color = new Color(0.8f, 0.1f, 0.1f, 0.95f);

        var txt = new GameObject("Txt"); txt.transform.SetParent(banner.transform, false);
        var tR = txt.AddComponent<RectTransform>();
        tR.anchoredPosition = Vector2.zero; tR.sizeDelta = new Vector2(920, 190);
        var tx = txt.AddComponent<Text>();
        tx.text = LocalizationManager.Instance.Get("MINE_FAUCET_ERR_TITLE") + message;
        tx.fontSize = 24; tx.color = Color.white;
        tx.alignment = TextAnchor.MiddleCenter;
        tx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tx.horizontalOverflow = HorizontalWrapMode.Wrap;

        // Close button
        var closeGo = new GameObject("CloseBtn"); closeGo.transform.SetParent(banner.transform, false);
        var cR = closeGo.AddComponent<RectTransform>(); cR.anchoredPosition = new Vector2(420, 80); cR.sizeDelta = new Vector2(60, 60);
        closeGo.AddComponent<Image>().color = new Color(1f, 0.3f, 0.3f);
        var cb = closeGo.AddComponent<Button>(); cb.onClick.AddListener(() => Destroy(banner));
        var cT = new GameObject("X"); cT.transform.SetParent(closeGo.transform, false);
        var cTR = cT.AddComponent<RectTransform>(); cTR.sizeDelta = new Vector2(60, 60);
        var cTx = cT.AddComponent<Text>(); cTx.text = "✕"; cTx.fontSize = 28; cTx.color = Color.white;
        cTx.alignment = TextAnchor.MiddleCenter; cTx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        banner.transform.SetAsLastSibling();

        // Auto-dismiss after 8 seconds
        StartCoroutine(AutoDismiss(banner, 8f));
    }

    IEnumerator AutoDismiss(GameObject go, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (go != null) Destroy(go);
    }

    void Log(string msg)
    {
        if (!logText) return;
        logText.text = $"> {msg}\n" + logText.text;
        if (logText.text.Length > 900) logText.text = logText.text.Substring(0, 900);
    }

    string Tr(string s, int n) { if (string.IsNullOrEmpty(s)) return "(none)"; return s.Length <= n ? s : s.Substring(0, n); }

    void GoBack()  { SceneManager.LoadScene("MainWallet"); }
    void OpenDL()  { Application.OpenURL(DOWNLOAD_URL); }

    void ToggleConfig()
    {
        configExpanded = !configExpanded;
        configContent.SetActive(configExpanded);
        cfgArrow.text = configExpanded ? "▼" : "▶";
    }

    void ToggleLog()
    {
        logExpanded = !logExpanded;
        logContent.SetActive(logExpanded);
        logArrow.text = logExpanded ? "▼" : "▶";
    }

    void UpdateUIState()
    {
        if (isMining)
        {
            toggleButtonText.text = LocalizationManager.Instance.Get("MINE_BTN_STOP");
            toggleButton.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f);
            addressInput.interactable = false;
        }
        else
        {
            toggleButtonText.text = LocalizationManager.Instance.Get("MINE_BTN_START");
            toggleButton.GetComponent<Image>().color = new Color(1f, 0.5f, 0f);
            addressInput.interactable = true;
            hashrateText.text = "0 H/s"; hashrate = 0;
            statusText.text = LocalizationManager.Instance.Get("MINE_STATUS_STOPPED");
            statusText.color = ThemeManager.Instance.GetSecondaryTextColor();
            timeText.text = "00:00:00";
            if (hashDisplayText) hashDisplayText.text = "---";
            lastHashHex = "";
            sharesText.text = LocalizationManager.Instance.Get("MINE_STATUS_STOPPED_SHORT");
            sharesText.color = Color.gray;
        }
    }

    // ─── BUILD UI ────────────────────────────────────────────────
    void BuildUI()
    {
        // Canvas
        var cvObj = new GameObject("Canvas");
        var cv = cvObj.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        var sc = cvObj.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        
        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        if (isDesktop)
        {
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
        }
        else
        {
            sc.referenceResolution = new Vector2(RW, RH);
            sc.matchWidthOrHeight = 0.5f;
        }
        cvObj.AddComponent<GraphicRaycaster>();
        var root = cv.transform;

        // Background
        if (isDesktop)
        {
            P(root, "BG", 0, 0, 1920, 1080, ThemeManager.Instance.GetBackgroundColor());
        }
        else
        {
            P(root, "BG", 0, 0, RW, RH, ThemeManager.Instance.GetBackgroundColor());
        }

        if (isDesktop)
        {
            // ── Top bar (Height 150) ───────────────────────────────────
            var hdr = P(root, "TopBar", 0, 0, 1920, 150, ThemeManager.Instance.GetCardColor());
            var hR = hdr.GetComponent<RectTransform>();
            hR.anchorMin = hR.anchorMax = new Vector2(0.5f, 1f);
            hR.anchoredPosition = new Vector2(0, -75);
            B(hdr.transform, "←", -800, 0, 110, 80, GoBack);
            T(hdr.transform, LocalizationManager.Instance.Get("MINE_TITLE"), 0, 0, 36, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

            // ── Window control buttons (Minimize / Close) ──
            var closeGo = P(hdr.transform, "WinClose", 0, 0, 45, 45, new Color(0.85f, 0.2f, 0.2f));
            var closeGoRT = closeGo.GetComponent<RectTransform>();
            closeGoRT.anchorMin = new Vector2(1, 0.5f); closeGoRT.anchorMax = new Vector2(1, 0.5f); closeGoRT.pivot = new Vector2(1, 0.5f);
            closeGoRT.anchoredPosition = new Vector2(-15, 0);
            var closeBtnM = closeGo.AddComponent<Button>();
            closeBtnM.onClick.AddListener(() => { Application.Quit(); });
            var closeTxtM = T(closeGo.transform, "✕", 0, 0, 22, Color.white, FontStyle.Bold);

            var minGo = P(hdr.transform, "WinMin", 0, 0, 45, 45, new Color(0.4f, 0.4f, 0.45f));
            var minGoRT = minGo.GetComponent<RectTransform>();
            minGoRT.anchorMin = new Vector2(1, 0.5f); minGoRT.anchorMax = new Vector2(1, 0.5f); minGoRT.pivot = new Vector2(1, 0.5f);
            minGoRT.anchoredPosition = new Vector2(-66, 0);
            var minBtnM = minGo.AddComponent<Button>();
            minBtnM.onClick.AddListener(() => {
                try {
                    var psi = new System.Diagnostics.ProcessStartInfo("powershell",
                        "-Command \"Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class WinAPI { [DllImport(\\\"user32.dll\\\")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow); [DllImport(\\\"user32.dll\\\")] public static extern IntPtr GetForegroundWindow(); }'; [WinAPI]::ShowWindow([WinAPI]::GetForegroundWindow(), 6)\"");
                    psi.CreateNoWindow = true; psi.UseShellExecute = false;
                    System.Diagnostics.Process.Start(psi);
                } catch (System.Exception e) { Debug.LogWarning("Minimize failed: " + e.Message); }
            });
            var minTxtM = T(minGo.transform, "—", 0, 0, 22, Color.white, FontStyle.Bold);

            float leftX = -450;
            float rightX = 450;

            // ── Status card ───────────────────────────────────
            var sC = Card(root, leftX, 240, 850, 300);
            T(sC.transform, LocalizationManager.Instance.Get("MINE_CARD_TITLE"), 0, 120, 36, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            T(sC.transform, LocalizationManager.Instance.Get("MINE_ALGO"), 0, 80, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            hashrateText = T(sC.transform, "0 H/s", 0, 25, 48, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            hashrateText.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 60);
            hashrateText.resizeTextForBestFit = true; hashrateText.resizeTextMinSize = 24; hashrateText.resizeTextMaxSize = 48;
            statusText = T(sC.transform, LocalizationManager.Instance.Get("MINE_STATUS_READY"), 0, -20, 24, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            timeText   = T(sC.transform, "00:00:00", 0, -50, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            sharesText = T(sC.transform, LocalizationManager.Instance.Get("MINE_STATUS_READY_SHORT"), 0, -80, 22, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            T(sC.transform, LocalizationManager.Instance.Get("MINE_DIFFICULTY_SIMULATED"), 0, -105, 18, new Color(0.5f, 0.5f, 0.5f), FontStyle.Italic);
            T(sC.transform, LocalizationManager.Instance.Get("MINE_CURRENT_HASH"), 0, -125, 18, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
            hashDisplayText = T(sC.transform, "---", 0, -142, 16, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
            hashDisplayText.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 28);
            hashDisplayText.alignment = TextAnchor.MiddleCenter;

            // ── Controls: Start button (orange) / Download button (blue) ──
            toggleButton = B(root, "Start Mining BTC for BTCT", leftX, 50, 850, 70, ToggleMining);
            toggleButton.GetComponent<Image>().color = new Color(1f, 0.5f, 0f);
            toggleButtonText = toggleButton.GetComponentInChildren<Text>();
            toggleButtonText.fontSize = 24;
            toggleButtonText.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 60);
            toggleButtonText.verticalOverflow = VerticalWrapMode.Overflow;

            var dl = B(root, "Download Bitcoin Tap Wallet", leftX, -40, 850, 70, OpenDL);
            dl.GetComponent<Image>().color = new Color(0.15f, 0.47f, 0.85f);
            
            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(dl.transform, false);
            var contentRT = contentObj.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0.5f, 0.5f);
            contentRT.anchorMax = new Vector2(0.5f, 0.5f);
            contentRT.pivot = new Vector2(0.5f, 0.5f);
            contentRT.anchoredPosition = Vector2.zero;

            var lg = contentObj.AddComponent<HorizontalLayoutGroup>();
            lg.childAlignment = TextAnchor.MiddleCenter;
            lg.spacing = 15;
            lg.childControlWidth = true;
            lg.childControlHeight = true;
            lg.childForceExpandWidth = false;
            lg.childForceExpandHeight = false;

            var csf = contentObj.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var iconObj = new GameObject("BlueIcon");
            var iRT = iconObj.AddComponent<RectTransform>();
            var iImg = iconObj.AddComponent<Image>();
            iImg.sprite = Resources.Load<Sprite>("Bitcointa");
            iImg.preserveAspect = true;
            var le = iconObj.AddComponent<LayoutElement>();
            le.preferredWidth = 45;
            le.preferredHeight = 45;
            iconObj.transform.SetParent(contentObj.transform, false);

            var dlTxt = dl.GetComponentInChildren<Text>();
            dlTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            dlTxt.transform.SetParent(contentObj.transform, false);

            // ── Log section header ──
            var logHdrGo = P(root, "LogHdr", leftX, -120, 850, 50, ThemeManager.Instance.GetCardColor());
            logArrow = T(logHdrGo.transform, "▼", -390, 0, 24, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            T(logHdrGo.transform, LocalizationManager.Instance.Get("MINE_LOG"), 10, 0, 24, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            var logHB = logHdrGo.AddComponent<Button>();
            logHB.targetGraphic = logHdrGo.GetComponent<Image>();
            logHB.onClick.AddListener(() => { ToggleLog(); AudioManager.Instance.PlayClick(); });

            // ── Log body ──
            logContent = Card(root, leftX, -250, 850, 190);
            logText = T(logContent.transform, LocalizationManager.Instance.Get("MINE_LOG_READY"), 0, 0, 18, new Color(0.6f, 0.3f, 0.9f), FontStyle.Normal);
            logText.alignment = TextAnchor.UpperLeft;
            logText.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 170);

            // ── Config section header ──
            var cfgHdrGo = P(root, "CfgHdr", rightX, 355, 850, 50, ThemeManager.Instance.GetCardColor());
            cfgArrow = T(cfgHdrGo.transform, "▼", -390, 0, 24, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            T(cfgHdrGo.transform, LocalizationManager.Instance.Get("MINE_CONFIG"), 10, 0, 24, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            var cfgHB = cfgHdrGo.AddComponent<Button>();
            cfgHB.targetGraphic = cfgHdrGo.GetComponent<Image>();
            cfgHB.onClick.AddListener(() => { ToggleConfig(); AudioManager.Instance.PlayClick(); });

            // ── Config body ──
            configContent = Card(root, rightX, 100, 850, 440);
            var cc = configContent.transform;
#if TESTNET_BUILD
            string topAddrDisplay = !string.IsNullOrEmpty(mainnetAddress) ? mainnetAddress : walletAddress;
            T(cc, "Mainnet Wallet Address (BTC)", -190, 190, 22, new Color(1f, 0.6f, 0.1f), FontStyle.Normal);
#else
            string topAddrDisplay = walletAddress;
            T(cc, LocalizationManager.Instance.Get("MINE_ADDR"), -240, 190, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
#endif
            addressInput = I(cc, 0, 135, 760, 60, topAddrDisplay);
            addressInput.readOnly = true;

            var copyBtnObj = new GameObject("CopyBtn");
            copyBtnObj.transform.SetParent(addressInput.transform, false);
            var copyBtnRt = copyBtnObj.AddComponent<RectTransform>();
            copyBtnRt.anchorMin = Vector2.zero; copyBtnRt.anchorMax = Vector2.one;
            copyBtnRt.offsetMin = Vector2.zero; copyBtnRt.offsetMax = Vector2.zero;
            copyBtnObj.AddComponent<Image>().color = new Color(0,0,0,0);
            var copyBtn = copyBtnObj.AddComponent<Button>();
            copyBtn.onClick.AddListener(() => {
                GUIUtility.systemCopyBuffer = topAddrDisplay;
                AudioManager.Instance.PlayClick();
                Log(LocalizationManager.Instance.Get("MINE_MAIN_COPIED"));
            });

            T(cc, LocalizationManager.Instance.Get("MINE_THREADS"), -250, 70, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            threadCountText = T(cc, threadCount.ToString(), 350, 70, 28, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            threadSlider = S(cc, 0, 15, 760, 45);
            threadSlider.minValue = 1; threadSlider.maxValue = System.Math.Max(System.Environment.ProcessorCount, 2);
            threadSlider.value = threadCount;
            threadSlider.onValueChanged.AddListener(v => { threadCount = (int)v; threadCountText.text = v.ToString(); if (isMining) { StopMining(); StartMining(); } });

            T(cc, LocalizationManager.Instance.Get("MINE_CPU_USAGE"), -250, -50, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            cpuPercentText = T(cc, $"{cpuPercent}%", 350, -50, 28, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            cpuSlider = S(cc, 0, -105, 760, 45);
            cpuSlider.minValue = 10; cpuSlider.maxValue = 100; cpuSlider.value = cpuPercent; cpuSlider.wholeNumbers = true;
            cpuSlider.onValueChanged.AddListener(v =>
            {
                cpuPercent = (int)v;
                cpuPercentText.text = $"{cpuPercent}%";
                cpuPercentText.color = cpuPercent >= 90 ? new Color(1f, 0.2f, 0.2f) : cpuPercent >= 70 ? new Color(1f, 0.5f, 0f) : ThemeManager.Instance.GetAccentColor();
                faucetInterval = CalcInterval();
                if (isMining) { StopMining(); StartMining(); }
            });
            T(cc, "⚠️ 100% = Maximum stress (app will lag)", 0, -150, 18, new Color(0.7f, 0.4f, 0.1f), FontStyle.Italic);

#if TESTNET_BUILD
            T(cc, "TestNet Wallet Address (BTCT)", -210, -180, 22, new Color(0.2f, 0.6f, 1f), FontStyle.Normal);
            testnetAddrLabel = T(cc, string.IsNullOrEmpty(testnetAddress) ? "(No wallet)" : testnetAddress, 0, -210, 18, new Color(0.2f, 0.6f, 1f), FontStyle.Normal);
#else
            T(cc, "Bitcoin Tap Wallet address", -210, -180, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            testnetAddrLabel = T(cc, string.IsNullOrEmpty(testnetAddress) ? "(No wallet)" : testnetAddress, 0, -210, 18, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
#endif
            testnetAddrLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(760, 30);

            var tapAddrBtnGo = new GameObject("TapAddrCopy");
            tapAddrBtnGo.transform.SetParent(cc, false);
            var tapAddrRt = tapAddrBtnGo.AddComponent<RectTransform>();
            tapAddrRt.anchoredPosition = new Vector2(0, -210);
            tapAddrRt.sizeDelta = new Vector2(760, 30);
            tapAddrBtnGo.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var tapAddrBtn = tapAddrBtnGo.AddComponent<Button>();
            tapAddrBtn.onClick.AddListener(() => {
                if (!string.IsNullOrEmpty(testnetAddress))
                {
                    GUIUtility.systemCopyBuffer = testnetAddress;
                    AudioManager.Instance.PlayCopy();
#if TESTNET_BUILD
                    Log("✅ TestNet address copied!");
#else
                    Log("✅ Bitcoin Tap address copied!");
#endif
                }
            });

            // ── Balance display ──
            double cachedBal = 0;
            bool hasWallet = WalletManager2.Instance != null && WalletManager2.Instance.HasWallet();
            if (hasWallet)
                cachedBal = WalletManager2.Instance.GetCurrentWallet().cachedTestnetBalance;

            string balDisplay = $"{cachedBal:F8} tBTC";
            // Yellow in dark mode; darker amber in light mode so it stays readable
            tbtcBalanceText = T(root, balDisplay, rightX, -165, 24,
                ThemeManager.Instance.IsDarkMode ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.72f, 0.5f, 0.05f), FontStyle.Bold);

            // ── Bottom Banner ──
            var bottomBanner = P(root, "BottomBanner", 0, 0, 1920, 100, new Color(0.21f, 0.33f, 0.46f));
            var bbRt = bottomBanner.GetComponent<RectTransform>();
            bbRt.anchorMin = new Vector2(0.5f, 0);
            bbRt.anchorMax = new Vector2(0.5f, 0);
            bbRt.anchoredPosition = new Vector2(0, 50);
            countdownText = T(bottomBanner.transform, LocalizationManager.Instance.Get("MINE_BANNER"), 0, 0, 30, Color.white, FontStyle.Normal);
            countdownText.raycastTarget = false;
        }
        else
        {
            // Mobile portrait layout (original)
            // ── Top bar ──────────────────────────────────────────────
            var hdr = P(root, "TopBar", 0, 0, RW, 180, ThemeManager.Instance.GetCardColor());
            var hR = hdr.GetComponent<RectTransform>();
            hR.anchorMin = hR.anchorMax = new Vector2(0.5f, 1f);
            hR.anchoredPosition = new Vector2(0, -90);
            B(hdr.transform, "←", -420, 0, 110, 90, GoBack);
            T(hdr.transform, LocalizationManager.Instance.Get("MINE_TITLE"), 0, 0, 46, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

            // ── Status card  y=550 ───────────────────────────────────
            var sC = Card(root, 0, 550, 950, 380);
            T(sC.transform, LocalizationManager.Instance.Get("MINE_CARD_TITLE"), 0, 170, 50, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            T(sC.transform, LocalizationManager.Instance.Get("MINE_ALGO"), 0, 115, 26, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            hashrateText = T(sC.transform, "0 H/s", 0, 45, 68, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            hashrateText.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 84);
            hashrateText.resizeTextForBestFit = true; hashrateText.resizeTextMinSize = 30; hashrateText.resizeTextMaxSize = 68;
            statusText = T(sC.transform, LocalizationManager.Instance.Get("MINE_STATUS_READY"), 0, -5, 30, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            timeText   = T(sC.transform, "00:00:00", 0, -45, 26, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            sharesText = T(sC.transform, LocalizationManager.Instance.Get("MINE_STATUS_READY_SHORT"), 0, -82, 26, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            T(sC.transform, LocalizationManager.Instance.Get("MINE_DIFFICULTY_SIMULATED"), 0, -113, 22, new Color(0.5f, 0.5f, 0.5f), FontStyle.Italic);
            T(sC.transform, LocalizationManager.Instance.Get("MINE_CURRENT_HASH"), 0, -148, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
            hashDisplayText = T(sC.transform, "---", 0, -172, 17, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
            hashDisplayText.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 36);
            hashDisplayText.alignment = TextAnchor.MiddleCenter;

            // ── Config section header  y=100 ─────────────────────────
            var cfgHdrGo = P(root, "CfgHdr", 0, 100, 970, 65, ThemeManager.Instance.GetCardColor());
            cfgArrow = T(cfgHdrGo.transform, "▼", -420, 0, 30, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            T(cfgHdrGo.transform, LocalizationManager.Instance.Get("MINE_CONFIG"), 30, 0, 32, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            var cfgHB = cfgHdrGo.AddComponent<Button>();
            cfgHB.targetGraphic = cfgHdrGo.GetComponent<Image>();
            cfgHB.onClick.AddListener(() => { ToggleConfig(); AudioManager.Instance.PlayClick(); });

            // ── Config body  y=-90 ───────────────────────────────────
            configContent = Card(root, 0, -90, 970, 570);
            var cc = configContent.transform;
#if TESTNET_BUILD
            // In TestNet build: top field shows the MAINNET address (bc1...)
            string topAddrDisplay = !string.IsNullOrEmpty(mainnetAddress) ? mainnetAddress : walletAddress;
            T(cc, "Mainnet Wallet Address (BTC)", -195, 255, 26, new Color(1f, 0.6f, 0.1f), FontStyle.Normal);
#else
            string topAddrDisplay = walletAddress;
            T(cc, LocalizationManager.Instance.Get("MINE_ADDR"), -250, 255, 26, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
#endif
            addressInput = I(cc, 0, 185, 860, 75, topAddrDisplay);
            addressInput.readOnly = true;

            // Add transparent copy button over input
            var copyBtnObj = new GameObject("CopyBtn");
            copyBtnObj.transform.SetParent(addressInput.transform, false);
            var copyBtnRt = copyBtnObj.AddComponent<RectTransform>();
            copyBtnRt.anchorMin = Vector2.zero; copyBtnRt.anchorMax = Vector2.one;
            copyBtnRt.offsetMin = Vector2.zero; copyBtnRt.offsetMax = Vector2.zero;
            copyBtnObj.AddComponent<Image>().color = new Color(0,0,0,0);
            var copyBtn = copyBtnObj.AddComponent<Button>();
            copyBtn.onClick.AddListener(() => {
                GUIUtility.systemCopyBuffer = topAddrDisplay;
                AudioManager.Instance.PlayClick();
                Log(LocalizationManager.Instance.Get("MINE_MAIN_COPIED"));
            });

            T(cc, LocalizationManager.Instance.Get("MINE_THREADS"), -260, 105, 26, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            threadCountText = T(cc, threadCount.ToString(), 390, 105, 34, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            threadSlider = S(cc, 0, 35, 860, 55);
            threadSlider.minValue = 1; threadSlider.maxValue = System.Math.Max(System.Environment.ProcessorCount, 2);
            threadSlider.value = threadCount;
            threadSlider.onValueChanged.AddListener(v => { threadCount = (int)v; threadCountText.text = v.ToString(); if (isMining) { StopMining(); StartMining(); } });

            T(cc, LocalizationManager.Instance.Get("MINE_CPU_USAGE"), -260, -55, 26, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            cpuPercentText = T(cc, $"{cpuPercent}%", 390, -55, 34, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            cpuSlider = S(cc, 0, -125, 860, 55);
            cpuSlider.minValue = 10; cpuSlider.maxValue = 100; cpuSlider.value = cpuPercent; cpuSlider.wholeNumbers = true;
            cpuSlider.onValueChanged.AddListener(v =>
            {
                cpuPercent = (int)v;
                cpuPercentText.text = $"{cpuPercent}%";
                cpuPercentText.color = cpuPercent >= 90 ? new Color(1f, 0.2f, 0.2f) : cpuPercent >= 70 ? new Color(1f, 0.5f, 0f) : ThemeManager.Instance.GetAccentColor();
                faucetInterval = CalcInterval();
                if (isMining) { StopMining(); StartMining(); }
            });
            T(cc, "⚠️ 100% = Maximum stress (app will lag)", 0, -185, 20, new Color(0.7f, 0.4f, 0.1f), FontStyle.Italic);

#if TESTNET_BUILD
            // TestNet mode: Show clear TestNet address label and value
            T(cc, "TestNet Wallet Address (BTCT)", -220, -225, 26, new Color(0.2f, 0.6f, 1f), FontStyle.Normal);
            testnetAddrLabel = T(cc, string.IsNullOrEmpty(testnetAddress) ? "(No wallet)" : testnetAddress, 0, -263, 21, new Color(0.2f, 0.6f, 1f), FontStyle.Normal);
#else
            // MainNet mode: Show Bitcoin Tap address
            T(cc, "Bitcoin Tap Wallet address", -220, -225, 26, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            testnetAddrLabel = T(cc, string.IsNullOrEmpty(testnetAddress) ? "(No wallet)" : testnetAddress, 0, -263, 21, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
#endif
            testnetAddrLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(870, 38);

            // Tap to copy Bitcoin Tap/TestNet address
            var tapAddrBtnGo = new GameObject("TapAddrCopy");
            tapAddrBtnGo.transform.SetParent(cc, false);
            var tapAddrRt = tapAddrBtnGo.AddComponent<RectTransform>();
            tapAddrRt.anchoredPosition = new Vector2(0, -263);
            tapAddrRt.sizeDelta = new Vector2(870, 40);
            tapAddrBtnGo.AddComponent<Image>().color = new Color(0, 0, 0, 0); // invisible
            var tapAddrBtn = tapAddrBtnGo.AddComponent<Button>();
            tapAddrBtn.onClick.AddListener(() => {
                if (!string.IsNullOrEmpty(testnetAddress))
                {
                    GUIUtility.systemCopyBuffer = testnetAddress;
                    AudioManager.Instance.PlayCopy();
#if TESTNET_BUILD
                    Log("✅ TestNet address copied!");
#else
                    Log("✅ Bitcoin Tap address copied!");
#endif
                }
            });

            // ── Balance display y=-370 ────────────────────────────
            double cachedBal = 0;
            bool hasWallet = WalletManager2.Instance != null && WalletManager2.Instance.HasWallet();
            if (hasWallet)
                cachedBal = WalletManager2.Instance.GetCurrentWallet().cachedTestnetBalance;

            string balDisplay = $"{cachedBal:F8} tBTC";
            // Yellow in dark mode; darker amber in light mode so it stays readable
            tbtcBalanceText = T(root, balDisplay, 0, -370, 26,
                ThemeManager.Instance.IsDarkMode ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.72f, 0.5f, 0.05f), FontStyle.Bold);
            
            toggleButton = B(root, "Start Mining BTC for BTCT", 0, -430, 920, 100, ToggleMining);
            toggleButton.GetComponent<Image>().color = new Color(1f, 0.5f, 0f);
            toggleButtonText = toggleButton.GetComponentInChildren<Text>();
            toggleButtonText.fontSize = 26;
            toggleButtonText.GetComponent<RectTransform>().sizeDelta = new Vector2(890, 90);
            toggleButtonText.verticalOverflow = VerticalWrapMode.Overflow;

            // ── Download button (blue)  y=-550 ───────────────────────
            var dl = B(root, "Download Bitcoin Tap Wallet", 0, -550, 920, 80, OpenDL);
            dl.GetComponent<Image>().color = new Color(0.15f, 0.47f, 0.85f);

            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(dl.transform, false);
            var contentRT = contentObj.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0.5f, 0.5f);
            contentRT.anchorMax = new Vector2(0.5f, 0.5f);
            contentRT.pivot = new Vector2(0.5f, 0.5f);
            contentRT.anchoredPosition = Vector2.zero;

            var lg = contentObj.AddComponent<HorizontalLayoutGroup>();
            lg.childAlignment = TextAnchor.MiddleCenter;
            lg.spacing = 15;
            lg.childControlWidth = true;
            lg.childControlHeight = true;
            lg.childForceExpandWidth = false;
            lg.childForceExpandHeight = false;

            var csf = contentObj.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var iconObj = new GameObject("BlueIcon");
            var iRT = iconObj.AddComponent<RectTransform>();
            var iImg = iconObj.AddComponent<Image>();
            iImg.sprite = Resources.Load<Sprite>("Bitcointa");
            iImg.preserveAspect = true;
            var le = iconObj.AddComponent<LayoutElement>();
            le.preferredWidth = 50;
            le.preferredHeight = 50;
            iconObj.transform.SetParent(contentObj.transform, false);

            var dlTxt = dl.GetComponentInChildren<Text>();
            dlTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            dlTxt.transform.SetParent(contentObj.transform, false);

            // ── Log section header  y=-660 ────────────────────────────
            var logHdrGo = P(root, "LogHdr", 0, -660, 970, 60, ThemeManager.Instance.GetCardColor());
            logArrow = T(logHdrGo.transform, "▼", -420, 0, 28, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            T(logHdrGo.transform, LocalizationManager.Instance.Get("MINE_LOG"), 10, 0, 30, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            var logHB = logHdrGo.AddComponent<Button>();
            logHB.targetGraphic = logHdrGo.GetComponent<Image>();
            logHB.onClick.AddListener(() => { ToggleLog(); AudioManager.Instance.PlayClick(); });

            // ── Log body  y=-820 ─────────────────────────────────────
            logContent = Card(root, 0, -820, 970, 260);
            logText = T(logContent.transform, LocalizationManager.Instance.Get("MINE_LOG_READY"), 0, 0, 22, new Color(0.6f, 0.3f, 0.9f), FontStyle.Normal);
            logText.alignment = TextAnchor.UpperLeft;
            logText.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 240);

            // ── Bottom Banner ─────────────────────────────────────
            var bottomBanner = P(root, "BottomBanner", 0, 0, RW, 120, new Color(0.21f, 0.33f, 0.46f));
            var bbRt = bottomBanner.GetComponent<RectTransform>();
            bbRt.anchorMin = new Vector2(0.5f, 0);
            bbRt.anchorMax = new Vector2(0.5f, 0);
            bbRt.anchoredPosition = new Vector2(0, 60);
            countdownText = T(bottomBanner.transform, LocalizationManager.Instance.Get("MINE_BANNER"), 0, 0, 36, Color.white, FontStyle.Normal);
            countdownText.raycastTarget = false;
        }
    }

    // ─── FACTORY ─────────────────────────────────────────────────
    GameObject P(Transform p, string n, float x, float y, float w, float h, Color c)
    {
        var o = new GameObject(n); o.transform.SetParent(p, false);
        var r = o.AddComponent<RectTransform>();
        r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
        o.AddComponent<Image>().color = c; return o;
    }

    GameObject Card(Transform p, float x, float y, float w, float h) => P(p, "Card", x, y, w, h, ThemeManager.Instance.GetCardColor());

    Text T(Transform p, string txt, float x, float y, int s, Color c, FontStyle fs)
    {
        var o = new GameObject("T"); o.transform.SetParent(p, false);
        var r = o.AddComponent<RectTransform>();
        r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(860, 58);
        var tx = o.AddComponent<Text>();
        tx.text = txt; tx.fontSize = s; tx.color = c; tx.fontStyle = fs;
        tx.alignment = TextAnchor.MiddleCenter;
        tx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tx.horizontalOverflow = HorizontalWrapMode.Wrap;
        tx.raycastTarget = false;
        return tx;
    }

    Button B(Transform p, string lbl, float x, float y, float w, float h, UnityEngine.Events.UnityAction a)
    {
        var o = P(p, "Btn", x, y, w, h, ThemeManager.Instance.GetAccentColor());
        var b = o.AddComponent<Button>();
        if (lbl == LocalizationManager.Instance.Get("GEN_BACK"))
            b.onClick.AddListener(() => AudioManager.Instance.PlayBack());
        else if (lbl.Contains("Mining") || lbl.Contains("Stop") || lbl.Contains("Start"))
            b.onClick.AddListener(() => AudioManager.Instance.PlayMining());
        else
            b.onClick.AddListener(() => AudioManager.Instance.PlayClick());
        b.onClick.AddListener(a);
        var tx = T(o.transform, lbl, 0, 0, 30, Color.white, FontStyle.Bold);
        tx.GetComponent<RectTransform>().sizeDelta = new Vector2(w - 20, h - 10);
        tx.verticalOverflow = VerticalWrapMode.Overflow;
        tx.raycastTarget = false;
        return b;
    }

    InputField I(Transform p, float x, float y, float w, float h, string def)
    {
        var o = P(p, "Inp", x, y, w, h, ThemeManager.Instance.IsDarkMode ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.9f, 0.9f, 0.9f));
        var inp = o.AddComponent<InputField>();
        var tx = T(o.transform, "", 0, 0, 26, ThemeManager.Instance.GetTextColor(), FontStyle.Normal);
        tx.GetComponent<RectTransform>().sizeDelta = new Vector2(w - 40, h - 10);
        tx.raycastTarget = true;
        inp.textComponent = tx; inp.text = def;
        inp.onValueChanged.AddListener(_ => AudioManager.Instance.PlayPinType());
        return inp;
    }

    Slider S(Transform p, float x, float y, float w, float h)
    {
        var o = P(p, "Sld", x, y, w, h, Color.clear);
        var bg = P(o.transform, "BG", 0, 0, w, 10, Color.gray);
        var fa = new GameObject("FillArea"); fa.transform.SetParent(o.transform, false);
        fa.AddComponent<RectTransform>().sizeDelta = new Vector2(w, 10);
        var fill = P(fa.transform, "Fill", 0, 0, 0, 0, ThemeManager.Instance.GetAccentColor());
        var sl = o.AddComponent<Slider>();
        sl.targetGraphic = bg.GetComponent<Image>();
        sl.fillRect = fill.GetComponent<RectTransform>();
        sl.direction = Slider.Direction.LeftToRight;
        var ha = new GameObject("HandleArea"); ha.transform.SetParent(o.transform, false);
        ha.AddComponent<RectTransform>().sizeDelta = new Vector2(w, 20);
        var handle = P(ha.transform, "Handle", 0, 0, 40, 40, Color.white);
        sl.handleRect = handle.GetComponent<RectTransform>();
        return sl;
    }

    [System.Serializable]
    class FaucetResp { public bool success; public string txHash; public long amount; public string error; }
}
