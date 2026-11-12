using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using ZXing;
using ZXing.QrCode;
using ZXing.Common;

public class ModernMobileWallet : MonoBehaviour
{
    public static ModernMobileWallet Instance { get; private set; } // Singleton instance
    private Text totalBalanceText;
    private Text cryptoBalanceText; // Small BTC underneath
    private Text testnetBalanceText; // Small tBTC below USD
    private Text addressDisplayText;
    private Image netIconImage; // Added Reference
#if TESTNET_BUILD
    private bool isTestNet = true;
#else
    private bool isTestNet = false;
#endif
    private bool autoRefresh = false;
    private bool _isDestroyed = false;
    private bool _isRefreshingBalance = false; // Prevent concurrent refresh operations
    public static bool IsRefreshingBalance => Instance?._isRefreshingBalance ?? false; // Public check for other classes
    // ── Notification guard ─────────────────────────────────────────────────
    // Tracks the balance at the time of the LAST payment notification.
    // A new notification is only sent when the balance increases by more than
    // DUST_THRESHOLD AND at least NOTIF_COOLDOWN_MINUTES have elapsed.
    private double _lastNotifiedBalance = -1.0;          // -1 = not yet initialised
    private System.DateTime _lastNotificationTime = System.DateTime.MinValue;
    private const double DUST_THRESHOLD_BTC      = 0.00000546; // 546 sat – Bitcoin dust limit
    private const double NOTIF_COOLDOWN_MINUTES  = 5.0;         // minimum gap between notifications
    private Button miningButton; // Reference for dynamic updates
    private GameObject keysOverlay; // First-time keys backup overlay
    private const string MEMPOOL_TESTNET = "https://mempool.space/testnet/api/address/";

#if TESTNET_BUILD
    private Coroutine faucetTimerCoroutine;
#endif

    // UI Config
    private const float REFERENCE_WIDTH = 1080f;
    private const float REFERENCE_HEIGHT = 1920f;

    // API
    private const string COINGECKO_API = "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd";
    private const string COINPAPRIKA_API = "https://api.coinpaprika.com/v1/tickers/btc-bitcoin"; // MainNet

    private GameObject mainCanvas; // Track main canvas for rebuilding
    private bool isOptionsOpen = false;

    void Start()
    {
        Instance = this; // Set singleton instance
        // Subscribe to language change
        LocalizationManager.Instance.OnLanguageChanged += RefreshUI;

        // 1. Auth Check
        if(AuthManager.Instance == null || !AuthManager.Instance.IsAuthenticated)
        {
             SceneManager.LoadScene("AppLock");
             return;
        }
    
        // 2. Build UI
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
        
        // Auto-Redirect if no wallet
        if (!WalletManager2.Instance.HasWallet())
        {
            SceneManager.LoadScene(1); // Go to Create Wallet
            return;
        }

        // 3. Start Native Android Foreground Service for background notifications
        try {
            BackgroundServiceManager.Instance.StartForegroundService();
        } catch (System.Exception e) {
            Debug.LogError("Foreground Service Error: " + e.Message);
        }
        
        BuildUI();
        
        // Check for pending TimeLock after PIN verification
        if (PlayerPrefs.GetString("PendingTL", "") == "1")
        {
            BuildTimeLockOverlay();
            StartCoroutine(ExecutePendingTimeLockDelayed());
        }
        else if (PlayerPrefs.GetString("PendingTLW", "") == "1")
        {
            BuildWithdrawTimeLockOverlay();
            StartCoroutine(ExecutePendingWithdrawTimeLockDelayed());
        }
        
        // 3. Load Logic
        LoadSavedWallet();
        autoRefresh = true;
        CancelInvoke("AutoRefreshBalance"); // prevent duplicate if Start() runs again
        InvokeRepeating("AutoRefreshBalance", 2f, 120f);
        // Fetch price immediately on startup so the ticker shows a real value right away
        StartCoroutine(FetchPriceOnly());
#if TESTNET_BUILD
        StartCoroutine(FetchTestnetBalance()); // fetch tBTC balance for testnet
#else
        StartCoroutine(FetchMainnetBalance()); // fetch BTC balance for mainnet
#endif
        
        // 4. Initialize Auto-Lock Manager
        var _ = AppAutoLockManager.Instance;
        
        // 5. Return to settings if coming back from sub-page
        if (PlayerPrefs.GetInt("ReturnToSettings", 0) == 1)
        {
            PlayerPrefs.SetInt("ReturnToSettings", 0);
            PlayerPrefs.Save();
            OnShowOptions();
        }
        
        // 6. Play welcome sound when entering main wallet
        AudioManager.Instance.PlayWelcomeBack();
    }

    void OnDestroy()
    {
        _isDestroyed = true;
        if (Instance == this) Instance = null;
        if (LocalizationManager.Instance != null)
            LocalizationManager.Instance.OnLanguageChanged -= RefreshUI;
        // ✅ Unsubscribe from balance event
        WalletManager2.OnBalanceChanged -= OnCentralBalanceChanged;
        ScreenSecurityManager.DisableProtection();
    }

    void OnEnable()
    {
        // ✅ Subscribe to central balance event — all screens stay in sync
        WalletManager2.OnBalanceChanged += OnCentralBalanceChanged;
    }

    void OnDisable()
    {
        WalletManager2.OnBalanceChanged -= OnCentralBalanceChanged;
    }

    void OnCentralBalanceChanged(double newBalance, string currency)
    {
        // Called whenever any screen calls WalletManager2.UpdateBalance()
        if (_isDestroyed || cryptoBalanceText == null) return;
        cryptoBalanceText.text = $"{newBalance:F8} {currency}";
        Debug.Log($"[ModernMobileWallet] ✅ Balance synced via event: {newBalance:F8} {currency}");
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && autoRefresh && !_isDestroyed && WalletManager2.Instance.HasWallet())
        {
            // Only show cached balance if not already refreshing (prevents double UI updates)
            if (!_isRefreshingBalance)
            {
                ShowCachedBalanceNow();
                OnRefreshBalance();
            }
            else
            {
                Debug.Log("[OnApplicationFocus] Refresh already in progress, skipping...");
            }
        }
    }

    void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            // App going to background — schedule price alert notifications
            if (PlayerPrefs.GetInt("PriceAlertEnabled", 0) == 1 && lastBtcPrice > 0 && NotificationManager.Instance != null)
            {
                string currency = PlayerPrefs.GetString("SelectedCurrency", "USD").ToUpper();
                string symbol = CurrencyHelper.GetSymbol(currency);
                NotificationManager.Instance.ScheduleBackgroundPriceAlerts(lastBtcPrice, symbol, currency);
#if TESTNET_BUILD
                Debug.Log($"[ModernMobileWallet] App paused — scheduled background price alerts (TBTC: {symbol}{lastBtcPrice:N6})");
#else
                Debug.Log($"[ModernMobileWallet] App paused — scheduled background price alerts (BTC: {symbol}{lastBtcPrice:N0})");
#endif
            }
        }
        else
        {
            // App returning to foreground — cancel scheduled, check live instead
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.CancelScheduledPriceAlerts();
                Debug.Log("[ModernMobileWallet] App resumed — cancelled scheduled alerts, checking live");
            }
        }
    }

    void RefreshUI()
    {
        // Rebuild UI without scene reload
        if (mainCanvas != null) Destroy(mainCanvas);
        BuildUI();
        LoadSavedWallet();
        
        // Restore Options Menu if it was open
        if (isOptionsOpen)
        {
            OnShowOptions();
            // Restore page if needed, OnShowOptions defaults to 0
            if (currentOptionsPage > 0)
            {
                 // Small hack: if page 1, we reload it. 
                 // Since OnShowOptions builds page 0, we can just call ReloadOptionsMenu() which uses currentOptionsPage
                 ReloadOptionsMenu(); 
            }
        }
    }

    void ShowCachedBalanceNow()
    {
        if (!WalletManager2.Instance.HasWallet()) { Debug.Log("[ShowCachedBalanceNow] No wallet"); return; }
        WalletData d = WalletManager2.Instance.GetCurrentWallet();
        if (cryptoBalanceText == null) { Debug.LogWarning("[ShowCachedBalanceNow] cryptoBalanceText is NULL!"); return; }
#if TESTNET_BUILD
        double cached = d.cachedTestnetBalance;
        string suffix = "tBTC";
        Debug.Log($"[ShowCachedBalanceNow] TestNet cachedTestnetBalance={cached:F8} tBTC");
#else
        double cached = d.cachedBalance;
        double mainBalance = d.balance;
        string suffix = "BTC";
        Debug.Log($"[ShowCachedBalanceNow] MainNet cachedBalance={cached:F8}, balance={mainBalance:F8} BTC");
#endif
        cryptoBalanceText.text = $"{cached:F8} {suffix}";
        Debug.Log($"[ShowCachedBalanceNow] UI updated to: {cached:F8} {suffix}");
    }

    void LoadSavedWallet()
    {
        if (WalletManager2.Instance.HasWallet())
        {
            WalletData data = WalletManager2.Instance.GetCurrentWallet();
            ShowCachedBalanceNow(); // ✅ show cache instantly before API call
            // Always MainNet, ignore data.isTestNet
            string addr = !string.IsNullOrEmpty(data.address) ? data.address : "No Address Generated";
            if(addressDisplayText) 
            {
                 string type = LocalizationManager.Instance.Get("MAIN_WALLET");
                 string net = LocalizationManager.Instance.Get("MAINNET");
#if TESTNET_BUILD
                 string netText = "Testnet Wallet - Testnet3";
#else
                 string netText = $"{type} • {net}";
#endif
                 
                 // Show parent address in orange above current address
                 // Use the permanent parentAddress field (set once during wallet creation)
                 string parentAddr = GetParentAddress(data);
                 
                 if (!string.IsNullOrEmpty(parentAddr) && parentAddr != addr)
                 {
                     string truncParent = parentAddr.Length > 20 ? parentAddr.Substring(0, 10) + "..." + parentAddr.Substring(parentAddr.Length - 8) : parentAddr;
                     addressDisplayText.text = $"<size=24><color=#FF8C00>Parent Address: {truncParent}</color></size>\n{addr}\n<size=24><color=#888888>{netText}</color></size>";
                 }
                 else
                 {
                     addressDisplayText.text = $"{addr}\n<size=24><color=#888888>{netText}</color></size>";
                 }
            }
            
            // Update Icon Dynamically
            if (netIconImage != null)
            {
#if TESTNET_BUILD
                Sprite s = Resources.Load<Sprite>("Bitcointa"); // TestNet icon
#else
                Sprite s = Resources.Load<Sprite>("y"); // MainNet icon
#endif
                if (s != null) netIconImage.sprite = s;
#if TESTNET_BUILD
                else netIconImage.color = new Color(0.2f, 0.6f, 1f); // TestNet blue
#else
                else netIconImage.color = new Color(1f, 0.6f, 0f); // MainNet orange
#endif
            }

            OnRefreshBalance();
            UpdateMiningButtonState();
            
            // Show keys backup overlay on first open (seed-phrase wallets only)
            if (!data.hasShownKeys && !data.isPrivateKeyImport && !string.IsNullOrEmpty(data.seedPhrase))
            {
                BuildFirstTimeKeysOverlay(data);
            }
        }
    }

    string GetParentAddress(WalletData data)
    {
        if (!string.IsNullOrEmpty(data.parentAddress))
            return data.parentAddress;
            
        if (data.generatedAddresses != null && data.generatedAddresses.Count > 0)
        {
            string origAddr = data.generatedAddresses[0].address;
            int lowestIdx = data.generatedAddresses[0].index;
            foreach (var ga in data.generatedAddresses)
            {
                if (ga.index < lowestIdx) { lowestIdx = ga.index; origAddr = ga.address; }
            }
            return origAddr;
        }
        
        return data.address;
    }

    void UpdateMiningButtonState()
    {
        if(miningButton == null) return;
        
        miningButton.interactable = true; // Always MainNet = always enabled
        
        // Visuals
        Color targetColor = Color.white;
        Color targetTextColor = ThemeManager.Instance.GetTextColor();
        
        foreach(var img in miningButton.GetComponentsInChildren<Image>()) 
        {
            // Keep Circle BG color distinct if needed, or just gray everything out
            if(img.gameObject.name == "Circle")
                 img.color = ThemeManager.Instance.IsDarkMode ? new Color(0.2f, 0.2f, 0.25f) : new Color(0.95f, 0.95f, 0.97f); // MainNet style
            else if (img.gameObject != miningButton.gameObject) // Icon/etc
                 img.color = targetColor;
        }

        foreach(var txt in miningButton.GetComponentsInChildren<Text>()) 
            txt.color = targetTextColor;
    }

    void BuildUI()
    {
        // Canvas
        mainCanvas = new GameObject("Canvas");
        GameObject canvasObj = mainCanvas;
        canvasObj.layer = 5;
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        
        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        if (isDesktop)
        {
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
        }
        else
        {
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            scaler.matchWidthOrHeight = 0.5f;
        }
        
        canvasObj.AddComponent<GraphicRaycaster>();

        // THEME: Background - FULL SCREEN
        GameObject bg = new GameObject("BG");
        bg.layer = 5;
        bg.transform.SetParent(canvas.transform, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = ThemeManager.Instance.GetBackgroundColor(); 
        // Force BG to be clean white/dark depending on theme, maybe slight gray for modern feel
        if(!ThemeManager.Instance.IsDarkMode) bgImg.color = new Color(0.98f, 0.98f, 0.98f); 

        BuildDashboard(canvas.transform, isDesktop);
    }

    void BuildDashboard(Transform dashboardPanel, bool isDesktop)
    {
        // --- HEADER SECTION ---
        // Brand Title (Satolite) with Satellite Logo badge
        GameObject brandBadgeObj = new GameObject("SatoliteBadge");
        brandBadgeObj.layer = 5;
        brandBadgeObj.transform.SetParent(dashboardPanel, false);
        RectTransform brandBadgeRT = brandBadgeObj.AddComponent<RectTransform>();
        brandBadgeRT.anchorMin = new Vector2(0.5f, 1); brandBadgeRT.anchorMax = new Vector2(0.5f, 1); brandBadgeRT.pivot = new Vector2(0.5f, 1);
        if (isDesktop)
            brandBadgeRT.anchoredPosition = new Vector2(-450, -40);
        else
            brandBadgeRT.anchoredPosition = new Vector2(0, -30);
        brandBadgeRT.sizeDelta = isDesktop ? new Vector2(580, 70) : new Vector2(600, 70);
        Image brandBadgeImg = brandBadgeObj.AddComponent<Image>();
        brandBadgeImg.color = Color.black; // Clean black background matching satolite_logo_bw

        // Centered Content Container for Logo + Title
        GameObject brandContentObj = new GameObject("BrandContent");
        brandContentObj.layer = 5;
        brandContentObj.transform.SetParent(brandBadgeObj.transform, false);
        RectTransform brandContentRT = brandContentObj.AddComponent<RectTransform>();
        brandContentRT.anchorMin = new Vector2(0.5f, 0.5f);
        brandContentRT.anchorMax = new Vector2(0.5f, 0.5f);
        brandContentRT.pivot = new Vector2(0.5f, 0.5f);
        brandContentRT.anchoredPosition = Vector2.zero;
        brandContentRT.sizeDelta = isDesktop ? new Vector2(530, 50) : new Vector2(550, 50);

        // Satellite Logo Icon (Black & White Satolite Logo, maintaining exact 452x348 aspect ratio)
        GameObject satLogoObj = new GameObject("SatelliteLogo");
        satLogoObj.layer = 5;
        satLogoObj.transform.SetParent(brandContentObj.transform, false);
        RectTransform satLogoRT = satLogoObj.AddComponent<RectTransform>();
        satLogoRT.anchorMin = new Vector2(0, 0.5f);
        satLogoRT.anchorMax = new Vector2(0, 0.5f);
        satLogoRT.pivot = new Vector2(0, 0.5f);
        satLogoRT.anchoredPosition = Vector2.zero;
        satLogoRT.sizeDelta = new Vector2(52, 40); // 452x348 aspect ratio fits cleanly without distortion
        Image satLogoImg = satLogoObj.AddComponent<Image>();
        Sprite satSprite = GetBrandLogoSprite();
        if (satSprite != null)
        {
            satLogoImg.sprite = satSprite;
            satLogoImg.color = Color.white;
            satLogoImg.preserveAspect = true;
        }
        else
        {
            satLogoImg.color = Color.clear;
        }

        GameObject brandTitleObj = new GameObject("BrandTitle");
        brandTitleObj.layer = 5;
        brandTitleObj.transform.SetParent(brandContentObj.transform, false);
        RectTransform brandRT = brandTitleObj.AddComponent<RectTransform>();
        brandRT.anchorMin = Vector2.zero;
        brandRT.anchorMax = Vector2.one;
        brandRT.offsetMin = new Vector2(64, 0);
        brandRT.offsetMax = Vector2.zero;
        Text brandTxt = brandTitleObj.AddComponent<Text>();
        brandTxt.text = "Satolite Wallet - Bitcoin Wallet";
        brandTxt.font = GetFont();
        brandTxt.fontSize = isDesktop ? 22 : 23;
        brandTxt.resizeTextForBestFit = true;
        brandTxt.resizeTextMinSize = 16;
        brandTxt.resizeTextMaxSize = 24;
        brandTxt.fontStyle = FontStyle.Bold;
        brandTxt.color = Color.white;
        brandTxt.alignment = TextAnchor.MiddleLeft;

#if UNITY_STANDALONE || UNITY_EDITOR
        if (isDesktop)
        {
            // --- WINDOW CONTROL BUTTONS (Minimize / Close) ---
            float winBtnY = -20f;
            float winBtnSize = 40f;

            // Close (X) button
            GameObject closeBtnObj = new GameObject("WinCloseBtn");
            closeBtnObj.layer = 5;
            closeBtnObj.transform.SetParent(dashboardPanel, false);
            RectTransform closeRT = closeBtnObj.AddComponent<RectTransform>();
            closeRT.anchorMin = new Vector2(1, 1); closeRT.anchorMax = new Vector2(1, 1); closeRT.pivot = new Vector2(1, 1);
            closeRT.anchoredPosition = new Vector2(-10, winBtnY);
            closeRT.sizeDelta = new Vector2(winBtnSize, winBtnSize);
            Image closeBtnImg = closeBtnObj.AddComponent<Image>();
            closeBtnImg.color = new Color(0.85f, 0.2f, 0.2f);
            Button closeBtn = closeBtnObj.AddComponent<Button>();
            closeBtn.onClick.AddListener(() => { Application.Quit(); });
            Text closeTxt = new GameObject("X").AddComponent<Text>();
            closeTxt.transform.SetParent(closeBtnObj.transform, false);
            closeTxt.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            closeTxt.GetComponent<RectTransform>().anchorMax = Vector2.one;
            closeTxt.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            closeTxt.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            closeTxt.text = "✕"; closeTxt.fontSize = 22; closeTxt.color = Color.white;
            closeTxt.font = GetFont(); closeTxt.alignment = TextAnchor.MiddleCenter;

            // Minimize (-) button
            GameObject minBtnObj = new GameObject("WinMinBtn");
            minBtnObj.layer = 5;
            minBtnObj.transform.SetParent(dashboardPanel, false);
            RectTransform minRT = minBtnObj.AddComponent<RectTransform>();
            minRT.anchorMin = new Vector2(1, 1); minRT.anchorMax = new Vector2(1, 1); minRT.pivot = new Vector2(1, 1);
            minRT.anchoredPosition = new Vector2(-10 - winBtnSize - 6, winBtnY);
            minRT.sizeDelta = new Vector2(winBtnSize, winBtnSize);
            Image minBtnImg = minBtnObj.AddComponent<Image>();
            minBtnImg.color = new Color(0.4f, 0.4f, 0.45f);
            Button minBtn = minBtnObj.AddComponent<Button>();
            minBtn.onClick.AddListener(() => {
                try {
                    var psi = new System.Diagnostics.ProcessStartInfo("powershell",
                        "-Command \"Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class WinAPI { [DllImport(\\\"user32.dll\\\")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow); [DllImport(\\\"user32.dll\\\")] public static extern IntPtr GetForegroundWindow(); }'; [WinAPI]::ShowWindow([WinAPI]::GetForegroundWindow(), 6)\"");
                    psi.CreateNoWindow = true; psi.UseShellExecute = false;
                    System.Diagnostics.Process.Start(psi);
                } catch (System.Exception e) { Debug.LogWarning("Minimize failed: " + e.Message); }
            });
            Text minTxt = new GameObject("Dash").AddComponent<Text>();
            minTxt.transform.SetParent(minBtnObj.transform, false);
            minTxt.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            minTxt.GetComponent<RectTransform>().anchorMax = Vector2.one;
            minTxt.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            minTxt.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            minTxt.text = "—"; minTxt.fontSize = 22; minTxt.color = Color.white;
            minTxt.font = GetFont(); minTxt.alignment = TextAnchor.MiddleCenter;
        }
#endif

        // Light mode: use dark text + shadow for visibility on white background
        Color headerTextColor = ThemeManager.Instance.IsDarkMode ? Color.white : new Color(0.1f, 0.1f, 0.1f);

        // 1. App Subtitle — "Bitcoin" for MainNet, "Bitcoin Tap" for TestNet
#if TESTNET_BUILD
        string appSubtitleStr = "Bitcoin Tap";
#else
        string appSubtitleStr = "Bitcoin";
#endif
        GameObject subtitleObj = MakeText(dashboardPanel, appSubtitleStr, 0, -125, 48, headerTextColor, FontStyle.Bold).gameObject;
        subtitleObj.name = "AppSubtitle";
        RectTransform subRT = subtitleObj.GetComponent<RectTransform>();
        subRT.anchorMin = new Vector2(0.5f, 1); subRT.anchorMax = new Vector2(0.5f, 1); subRT.pivot = new Vector2(0.5f, 1);
        if (isDesktop)
            subRT.anchoredPosition = new Vector2(-450, -130);
        else
            subRT.anchoredPosition = new Vector2(0, -125);
        subRT.sizeDelta = new Vector2(900, 60);
        if (!ThemeManager.Instance.IsDarkMode)
            subtitleObj.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.3f);

        // 3. "Home" - small section label, below subtitle
        GameObject homeObj = MakeText(dashboardPanel, LocalizationManager.Instance.Get("HOME_TITLE"), 0, -195, 22, headerTextColor, FontStyle.Italic).gameObject;
        homeObj.name = "HomeLabel";
        RectTransform homeRT = homeObj.GetComponent<RectTransform>();
        homeRT.anchorMin = new Vector2(0.5f, 1); homeRT.anchorMax = new Vector2(0.5f, 1); homeRT.pivot = new Vector2(0.5f, 1);
        if (isDesktop)
            homeRT.anchoredPosition = new Vector2(-450, -200);
        else
            homeRT.anchoredPosition = new Vector2(0, -195);
        homeRT.sizeDelta = new Vector2(900, 32);
        if (!ThemeManager.Instance.IsDarkMode)
            homeObj.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.3f);

#if TESTNET_BUILD
        // Lion icon (ok.png) — tap to open daily faucet
        GameObject okImgObj = new GameObject("OkImg");
        okImgObj.layer = 5;
        okImgObj.transform.SetParent(dashboardPanel, false);
        RectTransform okRT = okImgObj.AddComponent<RectTransform>();
        okRT.anchorMin = new Vector2(0.5f, 1); okRT.anchorMax = new Vector2(0.5f, 1); okRT.pivot = new Vector2(0.5f, 1);
        if (isDesktop)
            okRT.anchoredPosition = new Vector2(-450, -285);
        else
            okRT.anchoredPosition = new Vector2(0, -285);
        okRT.sizeDelta = new Vector2(90, 83);
        Image okImg = okImgObj.AddComponent<Image>();
        // Light mode: use faucetlogoshadow.png — it has a built-in shadow, so it stays
        // readable on the bright background. Dark mode keeps the regular ok.png icon.
        Sprite okSprite = ThemeManager.Instance.IsDarkMode
            ? Resources.Load<Sprite>("ok")
            : (Resources.Load<Sprite>("faucetlogoshadow") ?? Resources.Load<Sprite>("ok"));
        if (okSprite != null) okImg.sprite = okSprite;
        else okImg.color = new Color(0.2f, 0.6f, 1f);
        Button okBtn = okImgObj.AddComponent<Button>();
        okBtn.targetGraphic = okImg;
        okBtn.onClick.AddListener(() => { AudioManager.Instance.PlayBitcoinTap(); BuildDailyFaucetOverlay(); });
        // No extra Shadow component in light mode — the sprite already contains its own shadow.
        // Fallback: only if the shadowed sprite is missing, add a runtime shadow for visibility.
        if (!ThemeManager.Instance.IsDarkMode && Resources.Load<Sprite>("faucetlogoshadow") == null)
            okImgObj.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.4f);
#endif
        
        // --- BALANCE CARD SECTION ---
        GameObject cardObj;
        if (isDesktop)
            cardObj = MakePanel(dashboardPanel, "BalanceCard", -450, -190, 950, 550, ThemeManager.Instance.GetCardColor());
        else
            cardObj = MakePanel(dashboardPanel, "BalanceCard", 0, 420, 950, 550, ThemeManager.Instance.GetCardColor());
        
        // 1. Bitcoin Icon (Top Left inside Card)
        GameObject iconObj = new GameObject("BitIcon");
        iconObj.transform.SetParent(cardObj.transform, false);
        RectTransform iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchoredPosition = new Vector2(-335, 180);
        iconRT.sizeDelta = new Vector2(100, 100);
        
        Image iconImg = iconObj.AddComponent<Image>();
        this.netIconImage = iconImg; // Capture reference for updates
        
#if TESTNET_BUILD
        string iconName = "Bitcointa";
#else
        string iconName = "y";
#endif
        Sprite iconSprite = Resources.Load<Sprite>(iconName);
        if (iconSprite != null) iconImg.sprite = iconSprite;
#if TESTNET_BUILD
        else iconImg.color = new Color(0.2f, 0.6f, 1f);
#else
        else iconImg.color = new Color(1f, 0.6f, 0f); // MainNet orange
#endif
        
        // 2. Balance Texts
        // BTC - Show cached balance immediately
#if TESTNET_BUILD
        string curSuffix = "BTCT";
        string cachedBtc = "0.00000000 BTCT";
        double cachedVal = 0;
        if (WalletManager2.Instance.HasWallet())
        {
            WalletData cachedData = WalletManager2.Instance.GetCurrentWallet();
            cachedVal = cachedData.cachedTestnetBalance;
            if (cachedData.cachedTestnetBalance > 0)
                cachedBtc = $"{cachedData.cachedTestnetBalance:F8} BTCT";
        }
        Debug.Log($"[BuildDashboard] Initial TestNet display: cachedTestnetBalance={cachedVal:F8}, text='{cachedBtc}'");
#else
        string curSuffix = "BTC";
        string cachedBtc = "0.00000000 BTC";
        double cachedVal = 0;
        double mainBal = 0;
        if (WalletManager2.Instance.HasWallet())
        {
            WalletData cachedData = WalletManager2.Instance.GetCurrentWallet();
            cachedVal = cachedData.cachedBalance;
            mainBal = cachedData.balance;
            // Show balance even if 0, so user sees their actual balance
            cachedBtc = $"{cachedData.cachedBalance:F8} BTC";
        }
        Debug.Log($"[BuildDashboard] Initial MainNet display: cachedBalance={cachedVal:F8}, balance={mainBal:F8}, text='{cachedBtc}'");
#endif
        cryptoBalanceText = MakeText(cardObj.transform, cachedBtc, 50, 180, 52, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        cryptoBalanceText.alignment = TextAnchor.MiddleLeft;
        cryptoBalanceText.rectTransform.sizeDelta = new Vector2(650, 80);
        // USD
        totalBalanceText = MakeText(cardObj.transform, "≈ $0.00 USD", 50, 110, 36, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        totalBalanceText.alignment = TextAnchor.MiddleLeft;
        totalBalanceText.rectTransform.sizeDelta = new Vector2(650, 60);
        
        // 3. Address Section (Middle-Bottom of Card)
        MakeText(cardObj.transform, LocalizationManager.Instance.Get("BALANCE_MAIN"), -280, 20, 28, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal)
            .alignment = TextAnchor.MiddleLeft;

        // Address Value
        addressDisplayText = MakeText(cardObj.transform, LocalizationManager.Instance.Get("GEN_LOADING"), 0, -30, 32, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        addressDisplayText.alignment = TextAnchor.MiddleCenter;
        
        // 4. Address Actions (Row at bottom of card)
        float btnY = -180;
            
        // Copy Address Button (centered, no New Address button)
        MakeButtonSmall(cardObj.transform, "📋 " + LocalizationManager.Instance.Get("BTN_COPY"), 0, btnY, 500, 80, () => {
             if(WalletManager2.Instance.HasWallet()) {
                GUIUtility.systemCopyBuffer = WalletManager2.Instance.GetCurrentWallet().address;
                AudioManager.Instance.PlayCopy(); // Copy sound
                ShowStatus(LocalizationManager.Instance.Get("GEN_COPIED"), ThemeManager.Instance.GetSuccessColor());
            }
        }).GetComponent<Image>().color = ThemeManager.Instance.GetButtonColor();
        
        // --- ACTION GRID (Center) ---
        if (isDesktop)
        {
            // Desktop Actions horizontally aligned in upper right
            float gridCenterY = 320;
            float startX = 150;
            float spacingX = 170;
            
            MakeActionCircle(dashboardPanel, LocalizationManager.Instance.Get("BTN_SEND"), "↑", startX, gridCenterY, () => SceneManager.LoadScene(7));
            MakeActionCircle(dashboardPanel, LocalizationManager.Instance.Get("BTN_RECEIVE"), "↓", startX + spacingX, gridCenterY, OnShowReceive);
            MakeActionCircle(dashboardPanel, LocalizationManager.Instance.Get("BTN_MINING_NEW"), "⛏️", startX + spacingX * 2, gridCenterY, () => {
                AudioManager.Instance.PlayMining();
                SceneManager.LoadScene(5);
            });
            MakeActionCircle(dashboardPanel, LocalizationManager.Instance.Get("BTN_OPTIONS"), "⚙️", startX + spacingX * 3, gridCenterY, OnShowOptions);
            
            UpdateMiningButtonState(); // Init state

            // Desktop Footer stacked vertically in lower right
            float footerX = 405;
            float footerY = 50;
            float btnSpacing = 110;
            
            GameObject histBtn = MakeButton(dashboardPanel, LocalizationManager.Instance.Get("BTN_HISTORY"), footerX, footerY, 700, 90, () => SceneManager.LoadScene(3));
            histBtn.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();
            if (!ThemeManager.Instance.IsDarkMode)
            {
                Text histBtnText = histBtn.GetComponentInChildren<Text>();
                if (histBtnText != null) histBtnText.color = Color.black;
            }

#if TESTNET_BUILD
            string explorerUrl = "https://blockchair.com/bitcoin/testnet?utm_referrer=https%3A%2F%2Fwww.google.com%2F";
#else
            string explorerUrl = "https://mempool.space/";
#endif
            MakeButton(dashboardPanel, LocalizationManager.Instance.Get("BTN_EXPLORER"), footerX, footerY - btnSpacing, 700, 90,
                () => Application.OpenURL(explorerUrl))
                .GetComponent<Image>().color = new Color(0.45f, 0.1f, 0.75f); // Purple

            MakeButton(dashboardPanel, LocalizationManager.Instance.Get("BTN_SUPPORT"), footerX, footerY - (btnSpacing * 2), 700, 90, OnDonate)
                .GetComponent<Image>().color = new Color(0.8f, 0.4f, 0.6f); 
        }
        else
        {
            // Mobile portrait layout (original)
            float gridCenterY = -50; // Moved down for better mobile spacing
            float spacingX = 250;
            float spacingY = 240;
            
            // Row 1
            MakeActionCircle(dashboardPanel, LocalizationManager.Instance.Get("BTN_SEND"), "↑", -spacingX/2 - 20, gridCenterY + 50, () => SceneManager.LoadScene(7));
            MakeActionCircle(dashboardPanel, LocalizationManager.Instance.Get("BTN_RECEIVE"), "↓", spacingX/2 + 20, gridCenterY + 50, OnShowReceive);
            
            // Row 2
            MakeActionCircle(dashboardPanel, LocalizationManager.Instance.Get("BTN_MINING_NEW"), "⛏️", -spacingX/2 - 20, gridCenterY - spacingY + 50, () => {
                AudioManager.Instance.PlayMining(); // Mining button sound
                SceneManager.LoadScene(5);
            });
            MakeActionCircle(dashboardPanel, LocalizationManager.Instance.Get("BTN_OPTIONS"), "⚙️", spacingX/2 + 20, gridCenterY - spacingY + 50, OnShowOptions);
            
            UpdateMiningButtonState(); // Init state

            // --- FOOTER BUTTONS ---
            // Positioned lower on screen to avoid overlap with action grid
            float footerY = -600;
            float btnSpacing = 100;
            
            GameObject histBtn = MakeButton(dashboardPanel, LocalizationManager.Instance.Get("BTN_HISTORY"), 0, footerY, 900, 85, () => SceneManager.LoadScene(3));
            histBtn.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();
            // Fix: In light mode, button text must be dark (MakeButton defaults to white)
            if (!ThemeManager.Instance.IsDarkMode)
            {
                Text histBtnText = histBtn.GetComponentInChildren<Text>();
                if (histBtnText != null) histBtnText.color = Color.black;
            }

            // --- BLOCK EXPLORER BUTTON (Purple, between History and Donate) ---
#if TESTNET_BUILD
            string explorerUrl = "https://blockchair.com/bitcoin/testnet?utm_referrer=https%3A%2F%2Fwww.google.com%2F";
#else
            string explorerUrl = "https://mempool.space/";
#endif
            MakeButton(dashboardPanel, LocalizationManager.Instance.Get("BTN_EXPLORER"), 0, footerY - btnSpacing, 900, 80,
                () => Application.OpenURL(explorerUrl))
                .GetComponent<Image>().color = new Color(0.45f, 0.1f, 0.75f); // Purple

            MakeButton(dashboardPanel, LocalizationManager.Instance.Get("BTN_SUPPORT"), 0, footerY - (btnSpacing * 2), 900, 80, OnDonate)
                .GetComponent<Image>().color = new Color(0.8f, 0.4f, 0.6f); 
        } 

        // --- MARKET TICKER ---
        // Fixed on the right side on Desktop, centered at bottom on Mobile
        float tickerX = isDesktop ? 405f : 0f;
        float tickerY = isDesktop ? -280f : -880f;
        float tickerW = isDesktop ? 700f : 950f;
        BuildMarketTicker(dashboardPanel, tickerX, tickerY, tickerW);

        // --- FOOTER VERSION SERIAL (Very small at bottom of screen) ---
        GameObject footerSerialObj = new GameObject("FooterVersionSerial");
        footerSerialObj.layer = 5;
        footerSerialObj.transform.SetParent(dashboardPanel, false);
        RectTransform fsRT = footerSerialObj.AddComponent<RectTransform>();
        fsRT.anchorMin = new Vector2(0.5f, 0f);
        fsRT.anchorMax = new Vector2(0.5f, 0f);
        fsRT.pivot = new Vector2(0.5f, 0f);
        fsRT.anchoredPosition = new Vector2(0f, 8f);
        fsRT.sizeDelta = new Vector2(isDesktop ? 700f : 950f, 24f);
        Text fsTxt = footerSerialObj.AddComponent<Text>();
        fsTxt.text = SatoliteSerialService.GetVersionSerialString();
        fsTxt.font = GetFont();
        fsTxt.fontSize = isDesktop ? 15 : 17;
        fsTxt.color = new Color(0.55f, 0.58f, 0.65f, 0.65f);
        fsTxt.alignment = TextAnchor.MiddleCenter;
        fsTxt.raycastTarget = false;
    }
    
    // --- OPTIONS MENU ---
    private GameObject optionsOverlay;
    private GameObject changePasswordDialog;
    private int currentOptionsPage = 0; // 0 = Page 1, 1 = Page 2

    // Data for new settings
    private string[] availableCurrencies = new string[] { "USD", "EUR", "JPY", "RUB", "GBP", "CNY", "AUD", "CAD", "THB", "IDR", "TRY", "MXN", "BRL" };
    private string[] availableLanguages = new string[] { "English", "Chinese", "Japanese", "Spanish", "Russian", "Hindi", "Thai", "French", "Swahili", "Persian" };

    // Completely Rewritten Options Menu with all features
    void OnShowOptions()
    {
        isOptionsOpen = true; // Track state
        // 1. Overlay
        GameObject canvasObj = GameObject.Find("Canvas");
        optionsOverlay = new GameObject("OptionsOverlay");
        optionsOverlay.layer = 5;
        optionsOverlay.transform.SetParent(canvasObj.transform, false);
        
        RectTransform rt = optionsOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        optionsOverlay.AddComponent<Image>().color = new Color(0,0,0, 0.9f);
        optionsOverlay.AddComponent<Button>().onClick.AddListener(()=> { optionsOverlay.SetActive(false); });

        // 2. Panel (Center, tall enough for all items)
        GameObject panel = new GameObject("MenuPanel");
        panel.layer = 5; panel.transform.SetParent(optionsOverlay.transform, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(900, 1500);
        
        Image pImg = panel.AddComponent<Image>();
        pImg.color = ThemeManager.Instance.GetBackgroundColor();

        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif
        if (isDesktop)
        {
            panel.transform.localScale = new Vector3(0.65f, 0.65f, 1f);
        }
        
        // 3. Render Page Content based on current page
        if (currentOptionsPage == 0)
        {
            BuildOptionsPage1(panel.transform);
        }
        else
        {
            BuildOptionsPage2(panel.transform);
        }
    }

    void BuildOptionsPage1(Transform panel)
    {
        float startY = 590;
        float gap = 118;
        int row = 0;
        
        // Define standard button color based on theme
        Color standardBtnColor = ThemeManager.Instance.IsDarkMode ? ThemeManager.Instance.GetCardColor() : new Color(0.8f, 0.8f, 0.8f);

        // Title
        MakeText(panel.transform, LocalizationManager.Instance.Get("MENU_TITLE") + " (1/2)", 0, startY + 80, 48, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        
        // --- Wallet Management ---
        MakeText(panel.transform, LocalizationManager.Instance.Get("MENU_WALLET"), -280, startY - (gap * row) + 30, 28, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        
        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_CREATE"), 0, startY - (gap * row), 800, 100, () => {
            optionsOverlay.SetActive(false);
            SceneManager.LoadScene("CreateWallet");
        }).GetComponent<Image>().color = new Color(1f, 0.5f, 0f); // Orange
        row++;
        
        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_RECOVER"), 0, startY - (gap * row), 800, 100, () => {
            optionsOverlay.SetActive(false);
            SceneManager.LoadScene("RecoverWallet");
        }).GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.8f);
        row++;

        // TIME LOCK BTC BUTTON (Purple)
        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_TIMELOCK"), 0, startY - (gap * row), 800, 100, () => {
            optionsOverlay.SetActive(false);
            Destroy(optionsOverlay);
            BuildTimeLockOverlay();
        }).GetComponent<Image>().color = new Color(0.5f, 0.0f, 0.8f); // Purple
        row++;

        // WITHDRAW TIMELOCK BUTTON (Deep magenta) — redeem a previously locked tx
        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_TIMELOCK_WITHDRAW"), 0, startY - (gap * row), 800, 100, () => {
            optionsOverlay.SetActive(false);
            Destroy(optionsOverlay);
            BuildWithdrawTimeLockOverlay();
        }).GetComponent<Image>().color = new Color(0.75f, 0.15f, 0.55f); // Magenta
        row++;

        // 🛡️ MULTISIG VAULT BUTTON (Deep Blue)
        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_MULTISIG"), 0, startY - (gap * row), 800, 100, () => {
            optionsOverlay.SetActive(false);
            Destroy(optionsOverlay);
            OpenMultisigVault();
        }).GetComponent<Image>().color = new Color(0.12f, 0.42f, 0.75f);
        row++;

        // 🔑 EMERGENCY PASSPHRASE REVEAL (Amber / Orange) - One-time option
        if (!HasEmergencyRevealBeenUsed())
        {
            MakeButton(panel.transform, "🔑 " + (LocalizationManager.Instance?.Get("MENU_EMERGENCY_REVEAL") ?? "Emergency Passphrase Reveal"), 0, startY - (gap * row), 800, 100, () => {
                ShowEmergencyRevealConfirmDialog();
            }).GetComponent<Image>().color = new Color(0.9f, 0.45f, 0.05f);
            row++;
        }

        // Backup Wallet and Import Wallet File removed

        // --- Address ---
        MakeText(panel.transform, LocalizationManager.Instance.Get("ADDR_SECTION"), -280, startY - (gap * row) + 30, 28, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);

        // ✅ ADDRESS BOOK
        MakeButton(panel.transform, LocalizationManager.Instance.Get("ADDR_BOOK_BTN"), 0, startY - (gap * row), 800, 100, () => {
            optionsOverlay.SetActive(false);
            Destroy(optionsOverlay);
            ShowAddressBookOverlay();
        }).GetComponent<Image>().color = new Color(0.55f, 0.35f, 0.15f); // Brown
        row++;

        // --- Security ---
        MakeText(panel.transform, LocalizationManager.Instance.Get("MENU_SECURITY"), -280, startY - (gap * row) + 30, 28, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        
        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_PIN"), 0, startY - (gap * row), 800, 100, () => {
            optionsOverlay.SetActive(false);
            BuildChangePasswordDialog();
        }).GetComponent<Image>().color = new Color(0.45f, 0.45f, 0.5f); // Gray
        row++;

        // --- App Settings ---
        MakeText(panel.transform, LocalizationManager.Instance.Get("MENU_APP"), -280, startY - (gap * row) + 30, 28, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);

        string themeText = ThemeManager.Instance.IsDarkMode ? LocalizationManager.Instance.Get("MENU_THEME_LIGHT") : LocalizationManager.Instance.Get("MENU_THEME_DARK");
        MakeButton(panel.transform, themeText, 0, startY - (gap * row), 800, 100, () => {
            ThemeManager.Instance.ToggleTheme();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }).GetComponent<Image>().color = standardBtnColor;
        row++;
        
        // --- NEXT PAGE BUTTON ---
        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_NEXT"), 0, startY - (gap * row) - 20, 800, 100, () => {
            currentOptionsPage = 1;
            ReloadOptionsMenu();
        }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
        row++;

        // ✅ CLOSE BUTTON
        MakeButtonOutline(panel.transform, LocalizationManager.Instance.Get("MENU_CLOSE"), 0, -650, 400, 100, () => {
             isOptionsOpen = false;
             optionsOverlay.SetActive(false);
             Destroy(optionsOverlay);
        });
        MakeText(panel.transform, SatoliteSerialService.GetVersionSerialString(), 0, -735, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
    }

    void BuildOptionsPage2(Transform panel)
    {
        float startY = 600;
        float gap = 125;
        int row = 0;
        
        Color standardBtnColor = ThemeManager.Instance.IsDarkMode ? ThemeManager.Instance.GetCardColor() : new Color(0.8f, 0.8f, 0.8f);

        // Title
        MakeText(panel.transform, LocalizationManager.Instance.Get("MENU_TITLE") + " (2/2)", 0, startY + 80, 48, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // --- General ---
        MakeText(panel.transform, LocalizationManager.Instance.Get("MENU_GENERAL"), -280, startY - (gap * row) + 30, 28, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);

        // 1. Sound Toggle
        bool isSoundOn = PlayerPrefs.GetInt("SoundEnabled", 1) == 1;
        string soundText = isSoundOn ? LocalizationManager.Instance.Get("MENU_SOUND_ON") : LocalizationManager.Instance.Get("MENU_SOUND_OFF");
        Color soundColor = isSoundOn ? new Color(0.2f, 0.8f, 0.2f) : standardBtnColor;
        
        MakeButton(panel.transform, soundText, 0, startY - (gap * row), 800, 100, () => {
            int newState = isSoundOn ? 0 : 1;
            PlayerPrefs.SetInt("SoundEnabled", newState);
            PlayerPrefs.Save();
            ReloadOptionsMenu();
        }).GetComponent<Image>().color = soundColor;
        row++;

        // 2. Currency Selector (Dropdown)
        string currentCurrency = PlayerPrefs.GetString("SelectedCurrency", "USD");
        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_CURRENCY") + $": {currentCurrency} 💰", 0, startY - (gap * row), 800, 100, () => {
            ShowCurrencySelector();
        }).GetComponent<Image>().color = standardBtnColor;
        row++;

        // 3. Language Selector — darker grey than the other option buttons (both themes)
        string currentLanguage = PlayerPrefs.GetString("SelectedLanguage", "English");
        Color langBtnColor = ThemeManager.Instance.IsDarkMode
            ? new Color(0.11f, 0.11f, 0.14f)
            : new Color(0.35f, 0.35f, 0.38f);
        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_LANGUAGE") + $": {currentLanguage} 🌐", 0, startY - (gap * row), 800, 100, () => {
            ShowLanguageSelector();
        }).GetComponent<Image>().color = langBtnColor;
        row++;

        // 4. Broadcast API Selector
        string currentApi = PlayerPrefs.GetString("Pref_Send_API", "mempool").ToLowerInvariant();
        string apiDisplayName = "Mempool.space";
        if (currentApi == "blockstream") apiDisplayName = "Blockstream.info";
        else if (currentApi == "blockcypher") apiDisplayName = "BlockCypher";

        string apiLabel = LocalizationManager.Instance?.Get("MENU_BROADCAST_API") ?? "Broadcast API";
        Color apiBtnColor = new Color(0.18f, 0.52f, 0.45f);
        MakeButton(panel.transform, $"🌐 {apiLabel}: {apiDisplayName}", 0, startY - (gap * row), 800, 100, () => {
            ShowBroadcastApiSelector();
        }).GetComponent<Image>().color = apiBtnColor;
        row++;

        // --- Community ---
        MakeText(panel.transform, LocalizationManager.Instance.Get("MENU_COMMUNITY"), -280, startY - (gap * row) + 30, 28, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        
        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_JOIN"), 0, startY - (gap * row), 800, 100, () => {
            Application.OpenURL("https://satolite.org/");
        }).GetComponent<Image>().color = new Color(0.3f, 0.6f, 1f);
        row++;

        // --- Notifications (Moved from Page 1 if needed, or kept there. Let's keep basics here) ---
        // Just for balance, maybe add something else or leave gap. 
        // Let's put Danger Zone here as it's better hidden on Page 2
        
        MakeText(panel.transform, LocalizationManager.Instance.Get("MENU_DANGER"), -280, startY - (gap * row) + 30, 28, ThemeManager.Instance.GetErrorColor(), FontStyle.Bold);

        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_DELETE"), 0, startY - (gap * row), 800, 100, OnDeleteWallet)
             .GetComponent<Image>().color = ThemeManager.Instance.GetErrorColor();
        row++;

        // --- Price Alerts ---
        MakeText(panel.transform, LocalizationManager.Instance.Get("ALERTS_SECTION"), -280, startY - (gap * row) + 30, 28, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);

        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_PRICE_ALERT"), 0, startY - (gap * row), 800, 100, () => {
            ShowPriceAlertOverlay();
        }).GetComponent<Image>().color = new Color(0.9f, 0.6f, 0.1f);
        row++;

        // --- PREV PAGE BUTTON ---
        MakeButton(panel.transform, LocalizationManager.Instance.Get("MENU_PREV"), 0, startY - (gap * row) - 20, 800, 100, () => {
            currentOptionsPage = 0;
            ReloadOptionsMenu();
        }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
        row++;

        // ✅ CLOSE BUTTON
        MakeButtonOutline(panel.transform, LocalizationManager.Instance.Get("MENU_CLOSE"), 0, -650, 400, 100, () => {
             isOptionsOpen = false;
             optionsOverlay.SetActive(false);
             Destroy(optionsOverlay);
        });
        MakeText(panel.transform, SatoliteSerialService.GetVersionSerialString(), 0, -735, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
    }

    void ReloadOptionsMenu()
    {
        if(optionsOverlay != null) 
        {
            optionsOverlay.SetActive(false);
            Destroy(optionsOverlay);
        }
        OnShowOptions();
        if(optionsOverlay != null)
        {
             optionsOverlay.SetActive(true);
             optionsOverlay.transform.SetAsLastSibling();
        }
    }
    // --- FILE PERMISSION POPUP ---
    void CheckFilePermissionAndRun(System.Action onPermissionGranted)
    {
        // If already has permission, run immediately
        if (WalletFileService.HasStoragePermission())
        {
            onPermissionGranted?.Invoke();
            return;
        }

        // Show permission dialog
        GameObject canvas = GameObject.Find("Canvas");
        GameObject permDialog = MakePanel(canvas.transform, "PermDialog", 0, 0, REFERENCE_WIDTH, REFERENCE_HEIGHT, new Color(0,0,0,0.85f));

        GameObject card = MakePanel(permDialog.transform, "Card", 0, 50, 850, 550, new Color(0.12f, 0.12f, 0.18f));

        MakeText(card.transform, "🔐 " + LocalizationManager.Instance.Get("PERM_TITLE"), 0, 200, 40, Color.white, FontStyle.Bold);
        
        Text msgText = MakeText(card.transform, LocalizationManager.Instance.Get("PERM_MSG"), 0, 60, 28, new Color(0.8f, 0.8f, 0.8f), FontStyle.Normal);
        msgText.rectTransform.sizeDelta = new Vector2(750, 180);
        msgText.alignment = TextAnchor.MiddleCenter;

        // Grant button
        MakeButton(card.transform, "✅ " + LocalizationManager.Instance.Get("PERM_GRANT"), 0, -100, 600, 100, () => {
            WalletFileService.RequestStoragePermission();
            Destroy(permDialog);
            // Small delay to let permission dialog finish
            StartCoroutine(DelayedAction(0.5f, () => {
                if (WalletFileService.HasStoragePermission())
                {
                    onPermissionGranted?.Invoke();
                }
                else
                {
                    ShowStatus("❌ " + LocalizationManager.Instance.Get("PERM_DENIED"), Color.red);
                }
            }));
        }).GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.3f);

        // Cancel button
        MakeButton(card.transform, "❌ " + LocalizationManager.Instance.Get("BTN_CANCEL"), 0, -210, 400, 80, () => {
            Destroy(permDialog);
        }).GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f);

        permDialog.transform.SetAsLastSibling();
    }

    System.Collections.IEnumerator DelayedAction(float delay, System.Action action)
    {
        yield return new WaitForSeconds(delay);
        action?.Invoke();
    }

    void CycleCurrency(string current)
    {
        int index = System.Array.IndexOf(availableCurrencies, current);
        if(index == -1) index = 0;
        index = (index + 1) % availableCurrencies.Length;
        PlayerPrefs.SetString("SelectedCurrency", availableCurrencies[index]);
        PlayerPrefs.Save();
        
        ShowStatus(LocalizationManager.Instance.Get("CURRENCY_SET") + $" {availableCurrencies[index]}", ThemeManager.Instance.GetSuccessColor());
        
        // Force refresh to update price immediately
        if(WalletManager2.Instance.HasWallet())
        {
             // We can just call UpdateUSDValue directly with current cached balance if we don't want full scan
             // But OnRefreshBalance is safer as it handles everything
             OnRefreshBalance();
        }
    }

    void CycleLanguage(string current)
    {
        int index = System.Array.IndexOf(availableLanguages, current);
        if(index == -1) index = 0;
        index = (index + 1) % availableLanguages.Length;
        LocalizationManager.Instance.SetLanguage(availableLanguages[index]);
        
        ShowStatus(LocalizationManager.Instance.Get("LANGUAGE_SET") + $": {availableLanguages[index]}", ThemeManager.Instance.GetSuccessColor());
    }

    // --- CURRENCY SELECTOR DROPDOWN ---
    private GameObject currencySelectorOverlay;

    void ShowCurrencySelector()
    {
        // Close options menu first
        if (optionsOverlay != null)
        {
            optionsOverlay.SetActive(false);
            Destroy(optionsOverlay);
        }

        GameObject canvasObj = GameObject.Find("Canvas");
        currencySelectorOverlay = new GameObject("CurrencySelectorOverlay");
        currencySelectorOverlay.layer = 5;
        currencySelectorOverlay.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = currencySelectorOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        currencySelectorOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.9f);

        GameObject panel = new GameObject("CurrencyPanel");
        panel.layer = 5; panel.transform.SetParent(currencySelectorOverlay.transform, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(800, 1600);
        panel.AddComponent<Image>().color = ThemeManager.Instance.GetBackgroundColor();

        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif
        if (isDesktop)
        {
            panel.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
        }

        string currentCurrency = PlayerPrefs.GetString("SelectedCurrency", "USD");

        MakeText(panel.transform, LocalizationManager.Instance.Get("MENU_CURRENCY"), 0, 720, 42, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        float y = 600;
        float gap = 100;

        for (int i = 0; i < availableCurrencies.Length; i++)
        {
            string currency = availableCurrencies[i];
            string symbol = CurrencyHelper.GetSymbol(currency);
            bool isActive = (currency == currentCurrency);
            Color btnColor = isActive ? ThemeManager.Instance.GetAccentColor() :
                (ThemeManager.Instance.IsDarkMode ? new Color(0.2f, 0.2f, 0.3f) : new Color(0.7f, 0.7f, 0.75f));
            string label = isActive ? $"✓ {symbol} {currency}" : $"{symbol} {currency}";

            MakeButton(panel.transform, label, 0, y - (gap * i), 700, 80, () => {
                PlayerPrefs.SetString("SelectedCurrency", currency);
                PlayerPrefs.Save();
                Destroy(currencySelectorOverlay);
                ShowStatus(LocalizationManager.Instance.Get("CURRENCY_SET") + $" {currency}", ThemeManager.Instance.GetSuccessColor());
                // Force refresh to update price
                if(WalletManager2.Instance.HasWallet()) OnRefreshBalance();
            }).GetComponent<Image>().color = btnColor;
        }

        // Close button
        MakeButtonOutline(panel.transform, LocalizationManager.Instance.Get("MENU_CLOSE"), 0, -720, 400, 90, () => {
            Destroy(currencySelectorOverlay);
            // Reopen options menu
            OnShowOptions();
        });

        currencySelectorOverlay.transform.SetAsLastSibling();
    }

    // --- LANGUAGE SELECTOR DROPDOWN ---
    private GameObject languageSelectorOverlay;

    void ShowLanguageSelector()
    {
        // Close options menu first
        if (optionsOverlay != null)
        {
            optionsOverlay.SetActive(false);
            Destroy(optionsOverlay);
        }

        GameObject canvasObj = GameObject.Find("Canvas");
        languageSelectorOverlay = new GameObject("LanguageSelectorOverlay");
        languageSelectorOverlay.layer = 5;
        languageSelectorOverlay.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = languageSelectorOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        languageSelectorOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.9f);

        GameObject panel = new GameObject("LangPanel");
        panel.layer = 5; panel.transform.SetParent(languageSelectorOverlay.transform, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(800, 1500);
        panel.AddComponent<Image>().color = ThemeManager.Instance.GetBackgroundColor();

        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif
        if (isDesktop)
        {
            panel.transform.localScale = new Vector3(0.65f, 0.65f, 1f);
        }

        string currentLang = PlayerPrefs.GetString("SelectedLanguage", "English");

        MakeText(panel.transform, LocalizationManager.Instance.Get("LANG_SELECT_TITLE"), 0, 650, 42, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        float y = 520;
        float gap = 110;

        for (int i = 0; i < availableLanguages.Length; i++)
        {
            string lang = availableLanguages[i];
            bool isActive = (lang == currentLang);
            Color btnColor = isActive ? ThemeManager.Instance.GetAccentColor() : 
                (ThemeManager.Instance.IsDarkMode ? new Color(0.2f, 0.2f, 0.3f) : new Color(0.7f, 0.7f, 0.75f));
            string label = isActive ? $"✓ {lang}" : lang;

            MakeButton(panel.transform, label, 0, y - (gap * i), 700, 90, () => {
                if (lang == currentLang)
                {
                    Destroy(languageSelectorOverlay);
                    OnShowOptions();
                    return;
                }
                BuildChangeLanguageConfirmDialog(lang);
            }).GetComponent<Image>().color = btnColor;
        }

        // Close button
        MakeButtonOutline(panel.transform, LocalizationManager.Instance.Get("MENU_CLOSE"), 0, -650, 400, 90, () => {
            Destroy(languageSelectorOverlay);
            // Reopen options menu
            OnShowOptions();
        });

        languageSelectorOverlay.transform.SetAsLastSibling();
    }

    private GameObject changeLanguageConfirmDialog;

    void BuildChangeLanguageConfirmDialog(string targetLang)
    {
        if (changeLanguageConfirmDialog != null) Destroy(changeLanguageConfirmDialog);

        GameObject canvasObj = GameObject.Find("Canvas");
        if (canvasObj == null && mainCanvas != null) canvasObj = mainCanvas;

        changeLanguageConfirmDialog = new GameObject("ChangeLanguageConfirmDialog");
        changeLanguageConfirmDialog.layer = 5;
        changeLanguageConfirmDialog.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = changeLanguageConfirmDialog.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        changeLanguageConfirmDialog.AddComponent<Image>().color = new Color(0, 0, 0, 0.9f);

        GameObject panel = new GameObject("ConfirmPanel");
        panel.layer = 5;
        panel.transform.SetParent(changeLanguageConfirmDialog.transform, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(850, 620);
        panel.AddComponent<Image>().color = ThemeManager.Instance.GetBackgroundColor();

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif
        if (isDesktop)
        {
            panel.transform.localScale = new Vector3(0.65f, 0.65f, 1f);
        }

        // Icon
        MakeText(panel.transform, "🌐", 0, 210, 64, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);

        // Title
        MakeText(panel.transform, LocalizationManager.Instance.Get("CHANGE_LANG_TITLE"), 0, 130, 42, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // Message
        string confirmMsg = LocalizationManager.Instance.GetFormat("CHANGE_LANG_CONFIRM", targetLang);
        Text msgText = MakeText(panel.transform, confirmMsg, 0, 10, 26, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        msgText.rectTransform.sizeDelta = new Vector2(750, 200);
        msgText.alignment = TextAnchor.MiddleCenter;

        // Confirm Button
        MakeButton(panel.transform, LocalizationManager.Instance.Get("BTN_CONFIRM"), -180, -180, 320, 100, () => {
            if (changeLanguageConfirmDialog != null) Destroy(changeLanguageConfirmDialog);
            if (languageSelectorOverlay != null) Destroy(languageSelectorOverlay);
            if (optionsOverlay != null) { Destroy(optionsOverlay); isOptionsOpen = false; }
            
            LocalizationManager.Instance.SetLanguage(targetLang);
            PlayerPrefs.SetInt("ReturnToSettings", 1);
            PlayerPrefs.Save();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();

        // Cancel Button
        MakeButton(panel.transform, LocalizationManager.Instance.Get("BTN_CANCEL"), 180, -180, 320, 100, () => {
            if (changeLanguageConfirmDialog != null) Destroy(changeLanguageConfirmDialog);
            changeLanguageConfirmDialog = null;
        }).GetComponent<Image>().color = new Color(0.35f, 0.35f, 0.4f);

        changeLanguageConfirmDialog.transform.SetAsLastSibling();
    }

    // --- BROADCAST API SELECTOR ---
    private GameObject broadcastApiSelectorOverlay;

    private string GetBroadcastApiDisplay()
    {
        string pref = PlayerPrefs.GetString("Pref_Send_API", "mempool").ToLowerInvariant();
        if (pref == "blockstream") return "Blockstream.info";
        if (pref == "blockcypher") return "BlockCypher";
        return "Mempool.space";
    }

    void ShowBroadcastApiSelector()
    {
        if (optionsOverlay != null)
        {
            optionsOverlay.SetActive(false);
            Destroy(optionsOverlay);
        }

        GameObject canvasObj = GameObject.Find("Canvas");
        broadcastApiSelectorOverlay = new GameObject("BroadcastApiSelectorOverlay");
        broadcastApiSelectorOverlay.layer = 5;
        broadcastApiSelectorOverlay.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = broadcastApiSelectorOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        broadcastApiSelectorOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.9f);

        GameObject panel = new GameObject("ApiPanel");
        panel.layer = 5; panel.transform.SetParent(broadcastApiSelectorOverlay.transform, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(850, 1000);
        panel.AddComponent<Image>().color = ThemeManager.Instance.GetBackgroundColor();

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif
        if (isDesktop)
        {
            panel.transform.localScale = new Vector3(0.65f, 0.65f, 1f);
        }

        string currentApi = PlayerPrefs.GetString("Pref_Send_API", "mempool").ToLowerInvariant();

        string title = LocalizationManager.Instance?.Get("API_SELECT_TITLE") ?? "Select Broadcast API";
        string subtitle = LocalizationManager.Instance?.Get("API_SELECT_SUBTITLE") ?? "Choose the API provider used to broadcast transactions and query network status:";

        MakeText(panel.transform, title, 0, 360, 42, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        Text subText = MakeText(panel.transform, subtitle, 0, 280, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        subText.rectTransform.sizeDelta = new Vector2(750, 80);
        subText.horizontalOverflow = HorizontalWrapMode.Wrap;

        (string id, string name)[] apiOptions = new (string, string)[]
        {
            ("mempool", "Mempool.space (Default / Recommended)"),
            ("blockstream", "Blockstream.info"),
            ("blockcypher", "BlockCypher")
        };

        float startY = 140;
        float gap = 130;

        for (int i = 0; i < apiOptions.Length; i++)
        {
            string apiId = apiOptions[i].id;
            string apiTitle = apiOptions[i].name;
            bool isActive = (currentApi == apiId);

            Color btnColor = isActive ? ThemeManager.Instance.GetAccentColor() :
                (ThemeManager.Instance.IsDarkMode ? new Color(0.2f, 0.2f, 0.26f) : new Color(0.75f, 0.75f, 0.8f));

            string label = isActive ? $"✓ {apiTitle}" : apiTitle;

            MakeButton(panel.transform, label, 0, startY - (gap * i), 760, 105, () => {
                PlayerPrefs.SetString("Pref_Send_API", apiId);
                PlayerPrefs.Save();
                AudioManager.Instance?.PlayClick();
                Destroy(broadcastApiSelectorOverlay);
                currentOptionsPage = 1;
                OnShowOptions();
            }).GetComponent<Image>().color = btnColor;
        }

        // Close button
        MakeButtonOutline(panel.transform, LocalizationManager.Instance.Get("MENU_CLOSE"), 0, -380, 400, 90, () => {
            Destroy(broadcastApiSelectorOverlay);
            currentOptionsPage = 1;
            OnShowOptions();
        });

        broadcastApiSelectorOverlay.transform.SetAsLastSibling();
    }

    // --- PRICE ALERTS ---
    private GameObject priceAlertOverlay;
    private double lastBtcPrice = 0;

    void ShowPriceAlertOverlay()
    {
        if (optionsOverlay != null)
        {
            optionsOverlay.SetActive(false);
            Destroy(optionsOverlay);
        }

        GameObject canvasObj = GameObject.Find("Canvas");
        priceAlertOverlay = new GameObject("PriceAlertOverlay");
        priceAlertOverlay.layer = 5;
        priceAlertOverlay.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = priceAlertOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        priceAlertOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.9f);

        // Card panel
        GameObject card = MakePanel(priceAlertOverlay.transform, "Card", 0, 50, 900, 700, ThemeManager.Instance.GetCardColor());
        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif
        if (isDesktop)
        {
            card.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
        }

        // Title - PRICE ALERTS
        MakeText(card.transform, LocalizationManager.Instance.Get("ALERT_TITLE"), 0, 280, 48, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // Enable/Disable Toggle - Clear ON/OFF button
        bool alertsEnabled = PlayerPrefs.GetInt("PriceAlertEnabled", 0) == 1;
        string toggleText = alertsEnabled ? LocalizationManager.Instance.Get("ALERT_ON") : LocalizationManager.Instance.Get("ALERT_OFF");
        Color toggleColor = alertsEnabled ? new Color(0.15f, 0.7f, 0.3f) : new Color(0.55f, 0.2f, 0.2f);

        MakeButton(card.transform, toggleText, 0, 170, 700, 90, () => {
            int newState = alertsEnabled ? 0 : 1;
            PlayerPrefs.SetInt("PriceAlertEnabled", newState);
            PlayerPrefs.Save();
            Destroy(priceAlertOverlay);
            ShowPriceAlertOverlay();
        }).GetComponent<Image>().color = toggleColor;

        // Status indicator text
        string statusLabel = alertsEnabled ? LocalizationManager.Instance.Get("ALERT_STATUS_ON") : LocalizationManager.Instance.Get("ALERT_STATUS_OFF");
        Color statusColor = alertsEnabled ? ThemeManager.Instance.GetSuccessColor() : new Color(0.6f, 0.4f, 0.4f);
        MakeText(card.transform, statusLabel, 0, 110, 26, statusColor, FontStyle.Italic);

        // Alert Cycle Label
        MakeText(card.transform, LocalizationManager.Instance.Get("ALERT_CYCLE"), 0, 60, 36, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // Current alert cycle value
        string currentCycle = PlayerPrefs.GetString("PriceAlertCycle", "1hr");

        // Cycle buttons: 15m, 30m, 1hr, 24hr, 7 days
        string[] cycles = { "15m", "30m", "1hr", "24hr", "7 days" };
        float btnW = 140;
        float totalW = (btnW * cycles.Length) + (12 * (cycles.Length - 1));
        float startX = -totalW / 2 + btnW / 2;

        for (int i = 0; i < cycles.Length; i++)
        {
            string cycle = cycles[i];
            bool isActive = (currentCycle == cycle);
            Color bc = isActive ? ThemeManager.Instance.GetAccentColor() : (ThemeManager.Instance.IsDarkMode ? new Color(0.2f, 0.2f, 0.3f) : new Color(0.85f, 0.85f, 0.9f));

            MakeButton(card.transform, cycle, startX + (btnW + 12) * i, -40, btnW, 70, () => {
                PlayerPrefs.SetString("PriceAlertCycle", cycle);
                PlayerPrefs.Save();
                Destroy(priceAlertOverlay);
                ShowPriceAlertOverlay();
            }).GetComponent<Image>().color = bc;
        }



        // Close Menu Button
        MakeButton(card.transform, LocalizationManager.Instance.Get("MENU_CLOSE"), 0, -250, 400, 100, () => {
            ShowStatus(LocalizationManager.Instance.Get("ALERT_SAVED"), ThemeManager.Instance.GetSuccessColor());
            Destroy(priceAlertOverlay);
        }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();

        priceAlertOverlay.transform.SetAsLastSibling();
    }

    void CheckPriceAlert(double currentPrice)
    {
        if (PlayerPrefs.GetInt("PriceAlertEnabled", 0) != 1) return;
        if (currentPrice <= 0) return;

        // Get cycle interval in seconds
        string cycle = PlayerPrefs.GetString("PriceAlertCycle", "1hr");
        float intervalSeconds = GetCycleSeconds(cycle);

        // Use long timestamps for precision (float loses ~256s at 2026 Unix timestamps)
        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long lastAlertTime = 0;
        string lastAlertStr = PlayerPrefs.GetString("PriceAlertTimestampStr", "");
        if (!string.IsNullOrEmpty(lastAlertStr))
            long.TryParse(lastAlertStr, out lastAlertTime);
        
        // Migrate from old float storage if needed
        if (lastAlertTime == 0)
        {
            float oldTimestamp = PlayerPrefs.GetFloat("LastAlertTimestamp", 0f);
            if (oldTimestamp > 0) lastAlertTime = (long)oldTimestamp;
        }
        
        float lastPrice = PlayerPrefs.GetFloat("LastAlertPrice", 0f);

        if (lastPrice <= 0)
        {
            // First time — save baseline, no notification yet
            PlayerPrefs.SetFloat("LastAlertPrice", (float)currentPrice);
            PlayerPrefs.SetString("PriceAlertTimestampStr", now.ToString());
            PlayerPrefs.Save();
            Debug.Log($"[PriceAlert] Baseline set: ${currentPrice:N0}");
            return;
        }

        long elapsed = now - lastAlertTime;
        
        if (elapsed < (long)intervalSeconds)
        {
            // Not enough time has passed for this cycle
            return;
        }

        // Time to send notification! Calculate change
        double changePercent = ((currentPrice - lastPrice) / lastPrice) * 100.0;
        
        string direction = changePercent >= 0 ? "📈" : "📉";
        string changeStr = changePercent >= 0 ? $"+{changePercent:F1}%" : $"{changePercent:F1}%";
        
        string title = changePercent >= 0 ? 
            LocalizationManager.Instance.Get("ALERT_PRICE_UP") : 
            LocalizationManager.Instance.Get("ALERT_PRICE_DOWN");
        
        string currencyCode = PlayerPrefs.GetString("SelectedCurrency", "USD").ToUpper();
        string symbol = CurrencyHelper.GetSymbol(currencyCode);
#if TESTNET_BUILD
        string msg = $"{direction} TBTC: {symbol}{currentPrice:N6} ({changeStr})";
#else
        string msg = $"{direction} BTC: {symbol}{currentPrice:N0} ({changeStr})";
#endif

        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.SendNotification(title, msg);
            Debug.Log($"[PriceAlert] ✅ Notification sent: {title} - {msg}");
        }
        else
        {
            Debug.LogWarning("[PriceAlert] NotificationManager.Instance is null!");
        }

        // Update baseline for next cycle
        PlayerPrefs.SetFloat("LastAlertPrice", (float)currentPrice);
        PlayerPrefs.SetString("PriceAlertTimestampStr", now.ToString());
        PlayerPrefs.Save();
    }

    float GetCycleSeconds(string cycle)
    {
        switch (cycle)
        {
            case "15m":    return 15f * 60f;
            case "30m":    return 30f * 60f;
            case "1hr":    return 60f * 60f;
            case "24hr":   return 24f * 60f * 60f;
            case "7 days": return 7f * 24f * 60f * 60f;
            default:       return 60f * 60f;
        }
    }

    // ── Testnet (tBTC) balance fetch ─────────────────────────────
    System.Collections.IEnumerator FetchTestnetBalance()
    {
        if (!WalletManager2.Instance.HasWallet()) yield break;
        if (_isRefreshingBalance) { Debug.Log("[FetchTestnetBalance] Skipping - OnRefreshBalance already running"); yield break; }
        WalletManager2.Instance.EnsureTestnetAddress();
        string tAddr = WalletManager2.Instance.GetTestnetAddress();
        Debug.Log($"[FetchTestnetBalance] Testnet address: {tAddr}");
        if (string.IsNullOrEmpty(tAddr)) yield break;

        string url = MEMPOOL_TESTNET + tAddr;
        Debug.Log($"[FetchTestnetBalance] Fetching from: {url}");
        using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
        {
            req.timeout = 15;
            yield return req.SendWebRequest();
            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                try
                {
                    var data = JsonUtility.FromJson<MempoolAddrResp>(req.downloadHandler.text);
                    if (data != null && data.chain_stats != null)
                    {
                        long satoshi = (data.chain_stats.funded_txo_sum - data.chain_stats.spent_txo_sum);
                        if (data.mempool_stats != null)
                        {
                            satoshi += (data.mempool_stats.funded_txo_sum - data.mempool_stats.spent_txo_sum);
                        }
                        double tbtc = satoshi / 100000000.0;
                        Debug.Log($"[FetchTestnetBalance] Balance fetched: {tbtc:F8} tBTC (satoshi: {satoshi})");
                        // ✅ Use UpdateBalance so OnBalanceChanged event fires and ALL screens sync
                        // Do NOT set UI text directly here — OnCentralBalanceChanged handles it
                        WalletManager2.Instance.UpdateBalance(tbtc);
                        Debug.Log($"[FetchTestnetBalance] UpdateBalance called: {tbtc:F8} tBTC");
                    }
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogWarning("[FetchTestnetBalance] parse error: " + e.Message);
                }
            }
            else
            {
                Debug.LogWarning($"[FetchTestnetBalance] Failed: {req.result}, {req.error}");
            }
        }
    }

    // ── Mainnet (BTC) balance fetch ─────────────────────────────
    System.Collections.IEnumerator FetchMainnetBalance()
    {
        if (!WalletManager2.Instance.HasWallet()) yield break;
        if (_isRefreshingBalance) { Debug.Log("[FetchMainnetBalance] Skipping - OnRefreshBalance already running"); yield break; }
        string mainAddr = WalletManager2.Instance.GetAddress();
        Debug.Log($"[FetchMainnetBalance] Mainnet address: {mainAddr}");
        if (string.IsNullOrEmpty(mainAddr)) yield break;

        // Tier 1: Blockchain.info q/addressbalance (fastest, direct satoshi text)
        string binfoUrl = "https://blockchain.info/q/addressbalance/" + mainAddr;
        Debug.Log($"[FetchMainnetBalance] Fetching from: {binfoUrl}");
        using (var req = UnityEngine.Networking.UnityWebRequest.Get(binfoUrl))
        {
            req.timeout = 10;
            req.SetRequestHeader("User-Agent", "CoinHouseWallet/1.0");
            yield return req.SendWebRequest();
            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                if (long.TryParse(req.downloadHandler.text.Trim(), out long satoshi))
                {
                    float btc = satoshi / 100000000f;
                    Debug.Log($"[FetchMainnetBalance] Balance fetched (blockchain.info): {btc:F8} BTC (satoshi: {satoshi})");
                    WalletManager2.Instance.UpdateBalance(btc);
                    yield break;
                }
            }
            else
            {
                Debug.LogWarning($"[FetchMainnetBalance] Blockchain.info failed: {req.result}, {req.error}");
            }
        }

        // Tier 2: BlockCypher API
        string bcUrl = "https://api.blockcypher.com/v1/btc/main/addrs/" + mainAddr + "/balance";
        Debug.Log($"[FetchMainnetBalance] Fetching fallback from: {bcUrl}");
        using (var req = UnityEngine.Networking.UnityWebRequest.Get(bcUrl))
        {
            req.timeout = 10;
            req.SetRequestHeader("User-Agent", "CoinHouseWallet/1.0");
            yield return req.SendWebRequest();
            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                try
                {
                    var bcResp = JsonUtility.FromJson<BlockCypherBalanceResp>(req.downloadHandler.text);
                    if (bcResp != null)
                    {
                        float btc = bcResp.final_balance / 100000000f;
                        Debug.Log($"[FetchMainnetBalance] Balance fetched (blockcypher): {btc:F8} BTC");
                        WalletManager2.Instance.UpdateBalance(btc);
                        yield break;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[FetchMainnetBalance] BlockCypher parse error: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[FetchMainnetBalance] BlockCypher failed: {req.result}, {req.error}");
            }
        }
    }

    [System.Serializable]
    private class BlockCypherBalanceResp
    {
        public long final_balance;
    }

    [System.Serializable]
    private class MempoolAddrResp
    {
        public MempoolStats chain_stats;
        public MempoolStats mempool_stats;
        [System.Serializable]
        public class MempoolStats { public long funded_txo_sum; public long spent_txo_sum; }
    }

    // --- ADDRESS BOOK OVERLAY ---
    private GameObject addressBookOverlayPanel;
    private GameObject deleteConfirmDialog;

    void ShowAddressBookOverlay()
    {
        if (addressBookOverlayPanel != null) Destroy(addressBookOverlayPanel);

        GameObject canvasObj = GameObject.Find("Canvas");
        addressBookOverlayPanel = new GameObject("AddressBookOverlay");
        addressBookOverlayPanel.layer = 5;
        addressBookOverlayPanel.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = addressBookOverlayPanel.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        addressBookOverlayPanel.AddComponent<Image>().color = new Color(0, 0, 0, 0.92f);

        // Card
        GameObject card = MakePanel(addressBookOverlayPanel.transform, "Card", 0, 50, 950, 1600, ThemeManager.Instance.GetCardColor());
        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif
        if (isDesktop)
        {
            card.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
        }

        // Title
        MakeText(card.transform, "📒 " + LocalizationManager.Instance.Get("ADDR_BOOK_TITLE"), 0, 720, 48, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // Get entries
        var entries = AddressBookManager.Instance.GetEntries();

        float bottomY = 600; // Track bottom of entries for add form

        if (entries.Count == 0)
        {
            MakeText(card.transform, LocalizationManager.Instance.Get("ADDR_BOOK_EMPTY"), 0, 300, 32, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            bottomY = 100;
        }
        else
        {
            float itemY = 580;
            float itemH = 160;
            int maxVisible = 4;
            int count = Mathf.Min(entries.Count, maxVisible);

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                float currentY = itemY - (itemH * i);

                // Entry background
                Color entryBg = ThemeManager.Instance.IsDarkMode ? new Color(0.15f, 0.15f, 0.2f) : new Color(0.92f, 0.92f, 0.95f);
                GameObject entryPanel = MakePanel(card.transform, "Entry_" + i, 0, currentY, 880, 140, entryBg);

                // Name
                Text nameText = MakeText(entryPanel.transform, entry.name, -120, 30, 30, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
                nameText.alignment = TextAnchor.MiddleLeft;
                nameText.rectTransform.sizeDelta = new Vector2(480, 45);

                // Address (truncated)
                string truncAddr = entry.address.Length > 20 
                    ? entry.address.Substring(0, 10) + "..." + entry.address.Substring(entry.address.Length - 8) 
                    : entry.address;
                Text addrText = MakeText(entryPanel.transform, truncAddr, -120, -20, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
                addrText.alignment = TextAnchor.MiddleLeft;
                addrText.rectTransform.sizeDelta = new Vector2(480, 35);

                // ✅ Copy button with 📋 icon
                string fullAddr = entry.address;
                MakeButtonSmall(entryPanel.transform, "📋 " + LocalizationManager.Instance.Get("GEN_COPY"), 290, 20, 160, 55, () => {
                    GUIUtility.systemCopyBuffer = fullAddr;
                    ShowStatus(LocalizationManager.Instance.Get("GEN_COPIED"), ThemeManager.Instance.GetSuccessColor());
                }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();

                // ✅ Delete button with ✕ icon - shows confirmation dialog
                string delAddr = entry.address;
                string delName = entry.name;
                MakeButtonSmall(entryPanel.transform, "✕ " + LocalizationManager.Instance.Get("BTN_DELETE"), 290, -40, 160, 55, () => {
                    ShowDeleteConfirmation(delAddr, delName);
                }).GetComponent<Image>().color = ThemeManager.Instance.GetErrorColor();
            }

            if (entries.Count > maxVisible)
            {
                MakeText(card.transform, $"+ {entries.Count - maxVisible} more...", 0, itemY - (itemH * maxVisible) + 50, 26, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            }

            bottomY = itemY - (itemH * count) - 30;
        }

        // --- ADD NEW ADDRESS SECTION ---
        float addY = Mathf.Min(bottomY, -50);
        
        // Separator line
        GameObject sep = MakePanel(card.transform, "Sep", 0, addY + 40, 850, 3, ThemeManager.Instance.GetSecondaryTextColor());
        sep.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.5f, 0.5f);

        MakeText(card.transform, "➕ " + LocalizationManager.Instance.Get("ADDR_BOOK_ADD"), 0, addY - 10, 34, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);

        // Name input
        MakeText(card.transform, LocalizationManager.Instance.Get("ADDR_BOOK_NAME_LABEL"), -300, addY - 60, 24, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        InputField nameInput = MakeInputField(card.transform, 50, addY - 60, 600, 70, LocalizationManager.Instance.Get("ADDR_BOOK_NAME"));

        // Address input
        MakeText(card.transform, LocalizationManager.Instance.Get("ADDR_BOOK_ADDR_LABEL"), -280, addY - 150, 24, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        InputField addrInput = MakeInputField(card.transform, 50, addY - 150, 600, 70, LocalizationManager.Instance.Get("ADDR_BOOK_ADDR"));

        // Paste button next to address input
        MakeButtonSmall(card.transform, "📋", 390, addY - 150, 70, 70, () => {
            addrInput.text = GUIUtility.systemCopyBuffer;
        }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();

        // Add button
        MakeButton(card.transform, "➕ " + LocalizationManager.Instance.Get("ADDR_BOOK_ADD_BTN"), 0, addY - 250, 500, 90, () => {
            string newName = nameInput.text.Trim();
            string newAddr = addrInput.text.Trim();
            
            if (string.IsNullOrEmpty(newAddr))
            {
                ShowStatus(LocalizationManager.Instance.Get("ADDR_BOOK_NO_ADDR"), ThemeManager.Instance.GetErrorColor());
                return;
            }
            if (string.IsNullOrEmpty(newName)) newName = "Unnamed";
            
            AddressBookManager.Instance.AddEntry(newName, newAddr);
            ShowStatus(LocalizationManager.Instance.Get("ADDR_BOOK_SAVED"), new Color(1.0f, 0.6f, 0.0f));
            Destroy(addressBookOverlayPanel);
            ShowAddressBookOverlay(); // Refresh
        }).GetComponent<Image>().color = ThemeManager.Instance.GetSuccessColor();

        // Close button
        MakeButton(card.transform, LocalizationManager.Instance.Get("GEN_CLOSE"), 0, addY - 370, 400, 90, () => {
            Destroy(addressBookOverlayPanel);
        }).GetComponent<Image>().color = new Color(0.35f, 0.35f, 0.4f);

        addressBookOverlayPanel.transform.SetAsLastSibling();
    }

    // ✅ Delete Confirmation Dialog
    void ShowDeleteConfirmation(string address, string name)
    {
        if (deleteConfirmDialog != null) Destroy(deleteConfirmDialog);

        GameObject canvasObj = GameObject.Find("Canvas");
        deleteConfirmDialog = new GameObject("DeleteConfirmDialog");
        deleteConfirmDialog.layer = 5;
        deleteConfirmDialog.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = deleteConfirmDialog.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        deleteConfirmDialog.AddComponent<Image>().color = new Color(0, 0, 0, 0.85f);

        // Dialog card
        GameObject dialogCard = MakePanel(deleteConfirmDialog.transform, "DialogCard", 0, 50, 800, 450, ThemeManager.Instance.GetBackgroundColor());

        // Warning icon
        MakeText(dialogCard.transform, "⚠️", 0, 150, 60, Color.yellow, FontStyle.Normal);

        // Message
        string displayName = string.IsNullOrEmpty(name) ? "this address" : $"\"{name}\"";
        Text msgText = MakeText(dialogCard.transform, LocalizationManager.Instance.Get("ADDR_DELETE_CONFIRM") + $"\n{displayName}?", 0, 50, 30, ThemeManager.Instance.GetTextColor(), FontStyle.Normal);
        msgText.rectTransform.sizeDelta = new Vector2(700, 100);

        // Yes button
        MakeButton(dialogCard.transform, "✅ " + LocalizationManager.Instance.Get("DELETE_YES"), -180, -80, 300, 90, () => {
            AddressBookManager.Instance.DeleteEntry(address);
            Destroy(deleteConfirmDialog);
            deleteConfirmDialog = null;
            Destroy(addressBookOverlayPanel);
            ShowAddressBookOverlay(); // Refresh
            ShowStatus(LocalizationManager.Instance.Get("ADDR_BOOK_DELETED"), ThemeManager.Instance.GetSuccessColor());
        }).GetComponent<Image>().color = ThemeManager.Instance.GetErrorColor();

        // No button
        MakeButton(dialogCard.transform, "❌ " + LocalizationManager.Instance.Get("DELETE_NO"), 180, -80, 300, 90, () => {
            Destroy(deleteConfirmDialog);
            deleteConfirmDialog = null;
        }).GetComponent<Image>().color = new Color(0.35f, 0.35f, 0.4f);

        deleteConfirmDialog.transform.SetAsLastSibling();
    }

    // ✅ Helper: Create InputField for Address Book
    InputField MakeInputField(Transform parent, float x, float y, float w, float h, string placeholder)
    {
        GameObject obj = new GameObject("Input");
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(w, h);
        obj.AddComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.1f, 0.1f, 0.15f) : new Color(0.9f, 0.9f, 0.9f);
        InputField input = obj.AddComponent<InputField>();
        
        GameObject textObj = new GameObject("Text");
        textObj.layer = 5; textObj.transform.SetParent(obj.transform, false);
        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(15, 0); textRT.offsetMax = new Vector2(-15, 0);
        Text text = textObj.AddComponent<Text>();
        text.font = GetFont(); text.fontSize = 28; text.color = ThemeManager.Instance.GetTextColor();
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        input.textComponent = text;
        input.onValueChanged.AddListener((val) => AudioManager.Instance.PlayPinType());
        
        GameObject phObj = new GameObject("Placeholder");
        phObj.layer = 5; phObj.transform.SetParent(obj.transform, false);
        RectTransform phRT = phObj.AddComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(15, 0); phRT.offsetMax = new Vector2(-15, 0);
        Text ph = phObj.AddComponent<Text>();
        ph.text = placeholder; ph.font = GetFont(); ph.fontSize = 28;
        ph.color = new Color(0.5f, 0.5f, 0.5f); ph.fontStyle = FontStyle.Italic;
        ph.alignment = TextAnchor.MiddleLeft;
        ph.raycastTarget = false;
        input.placeholder = ph;
        
        return input;
    }

    // --- MARKET TICKER ---
    private Text tickerMainnetText;
    
    void BuildMarketTicker(Transform parent, float x, float y, float w)
    {
        // Reduced height for mobile screens (100 instead of 150)
        GameObject panel = MakePanel(parent, "MarketTicker", x, y, w, 100, ThemeManager.Instance.GetCardColor());
        panel.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.1f, 0.1f, 0.12f, 0.95f) : new Color(0.95f, 0.95f, 0.98f, 0.95f);
        
#if TESTNET_BUILD
        tickerMainnetText = MakeText(panel.transform, "BTCT: $ ...", 0, 0, 38, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
#else
        tickerMainnetText = MakeText(panel.transform, "BTC: $ ...", 0, 0, 38, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
#endif
        tickerMainnetText.alignment = TextAnchor.MiddleCenter;
    }

    // --- RECEIVE OVERLAY ---
    private GameObject receiveOverlay;

    void OnShowReceive()
    {
        BuildReceiveOverlay();
        receiveOverlay.SetActive(true);
        receiveOverlay.transform.SetAsLastSibling();
    }

    void BuildReceiveOverlay()
    {
        if (receiveOverlay != null) Destroy(receiveOverlay);

        GameObject canvasObj = GameObject.Find("Canvas");
        receiveOverlay = new GameObject("ReceiveOverlay");
        receiveOverlay.layer = 5;
        receiveOverlay.transform.SetParent(canvasObj.transform, false);
        
        RectTransform rt = receiveOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin=Vector2.zero; rt.offsetMax=Vector2.zero;
        
        Image img = receiveOverlay.AddComponent<Image>();
        img.color = new Color(0,0,0, 0.9f);
        
        // Panel sized dynamically: 900x1000 or scaled
        GameObject card = MakePanel(receiveOverlay.transform, "Card", 0, 0, 900, 1100, ThemeManager.Instance.GetCardColor());
        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif
        if (isDesktop)
        {
            card.transform.localScale = new Vector3(0.85f, 0.85f, 1f);
        }
        
        MakeText(card.transform, LocalizationManager.Instance.Get("RECEIVE_TITLE"), 0, 400, 48, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        
        GameObject qrObj = new GameObject("QR");
        qrObj.layer = 5;
        qrObj.transform.SetParent(card.transform, false);
        RectTransform qrRT = qrObj.AddComponent<RectTransform>();
        qrRT.anchoredPosition = new Vector2(0, 50);
        qrRT.sizeDelta = new Vector2(500, 500);
        qrObj.AddComponent<Image>().color = Color.white; 
        
        string addr = "No Address";
        if (WalletManager2.Instance.HasWallet())
        {
#if TESTNET_BUILD
            WalletManager2.Instance.EnsureTestnetAddress();
            addr = WalletManager2.Instance.GetTestnetAddress();
#else
            var curW = WalletManager2.Instance.GetCurrentWallet();
            if (curW != null && (curW.isTestNet || (!string.IsNullOrEmpty(curW.address) && curW.address.StartsWith("tb1"))))
            {
                WalletManager2.Instance.EnsureTestnetAddress();
                addr = WalletManager2.Instance.GetTestnetAddress();
            }
            else
            {
                addr = curW?.address ?? "No Address";
            }
#endif
        }

        Text addrText = MakeText(card.transform, addr, 0, -250, 32, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
        
        MakeButton(card.transform, LocalizationManager.Instance.Get("BTN_COPY_ADDRESS"), 0, -350, 400, 100, () => {
             GUIUtility.systemCopyBuffer = addr;
             ShowStatus(LocalizationManager.Instance.Get("GEN_COPIED"), ThemeManager.Instance.GetSuccessColor());
        });

        // ✅ CLOSE BUTTON - Moved down to allow space
        MakeButtonOutline(card.transform, LocalizationManager.Instance.Get("MENU_CLOSE"), 0, -500, 400, 100, () => Destroy(receiveOverlay));

        if (addr != "No Address")
        {
            GenerateQRCode(addr);
        }
    }
    
    void GenerateQRCode(string text)
    {
        try {
            if(receiveOverlay == null) return;
            Transform card = receiveOverlay.transform.Find("Card");
            if(card == null) return;
            Transform qr = card.Find("QR");
            if(qr == null) return;
            
            var writer = new QRCodeWriter();
            var bitMatrix = writer.encode(text, BarcodeFormat.QR_CODE, 256, 256);
            int w = bitMatrix.Width;
            int h = bitMatrix.Height;
            Texture2D tex = new Texture2D(w, h);
            Color32[] pixels = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    pixels[y * w + x] = bitMatrix[x, y] ? Color.black : Color.white;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            qr.GetComponent<Image>().sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        } catch { }
    }

    void OnGenerateNewWallet() { SceneManager.LoadScene(1); }
    
    // --- DONATE OVERLAY ---
    private GameObject donateOverlay;
    
    void OnDonate()
    {
        if(donateOverlay == null) BuildDonateOverlay();
        donateOverlay.SetActive(true);
        donateOverlay.transform.SetAsLastSibling();
    }
    
    void BuildDonateOverlay()
    {
        GameObject canvasObj = GameObject.Find("Canvas");
        donateOverlay = new GameObject("DonateOverlay");
        donateOverlay.layer = 5;
        donateOverlay.transform.SetParent(canvasObj.transform, false);
        
        RectTransform rt = donateOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin=Vector2.zero; rt.offsetMax=Vector2.zero;
        
        Image img = donateOverlay.AddComponent<Image>();
        img.color = new Color(0,0,0, 0.9f);
        
        // Panel sized to accommodate Contact Support and Dev Notice
        GameObject card = MakePanel(donateOverlay.transform, "Card", 0, 0, 920, 1040, ThemeManager.Instance.GetCardColor());
        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif
        if (isDesktop)
        {
            card.transform.localScale = new Vector3(0.72f, 0.72f, 1f);
        }
        
        // 1. Title
        MakeText(card.transform, LocalizationManager.Instance.Get("CONTACT_SUPPORT_TITLE"), 0, 435, 40, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // 2. Contact Message (with clickable coinvader link)
        string curLang = LocalizationManager.Instance.GetCurrentLanguage();
        string contactMsg = LocalizationManager.Instance.Get("CONTACT_SUPPORT_MSG");
        if (curLang != "English")
        {
            string enMsg = LocalizationManager.Instance.GetForLanguage("English", "CONTACT_SUPPORT_MSG");
            contactMsg += "\n<size=19><color=#99B0D0>" + enMsg + "</color></size>";
        }

        Text contactText = MakeText(card.transform, contactMsg, 0, 345, 21, new Color(0.85f, 0.9f, 1f), FontStyle.Normal);
        contactText.rectTransform.sizeDelta = new Vector2(850, 105);
        contactText.horizontalOverflow = HorizontalWrapMode.Wrap;
        contactText.alignment = TextAnchor.MiddleCenter;

        // Allow clicking directly on contact text to open Bitcointalk profile
        Button textClickBtn = contactText.gameObject.AddComponent<Button>();
        textClickBtn.onClick.AddListener(() => {
            Application.OpenURL("https://bitcointalk.org/index.php?action=profile;u=3404014");
        });

        // 3. Action Buttons for coinvader Profile and Email
        GameObject b1 = MakeButton(card.transform, "🔗 coinvader (bitcointalk.org)", -215, 250, 410, 68, () => {
            Application.OpenURL("https://bitcointalk.org/index.php?action=profile;u=3404014");
        });
        b1.GetComponent<Image>().color = new Color(0.18f, 0.48f, 0.85f);
        Text b1Txt = b1.GetComponentInChildren<Text>();
        if (b1Txt != null)
        {
            b1Txt.fontSize = 20;
            b1Txt.resizeTextForBestFit = true;
            b1Txt.resizeTextMinSize = 13;
            b1Txt.resizeTextMaxSize = 22;
        }

        GameObject b2 = MakeButton(card.transform, "✉️ coinvaderwho@protonmail.com", 215, 250, 410, 68, () => {
            GUIUtility.systemCopyBuffer = "coinvaderwho@protonmail.com";
            Application.OpenURL("mailto:coinvaderwho@protonmail.com");
            ShowStatus("Email copied: coinvaderwho@protonmail.com", ThemeManager.Instance.GetSuccessColor());
        });
        b2.GetComponent<Image>().color = new Color(0.2f, 0.55f, 0.4f);
        Text b2Txt = b2.GetComponentInChildren<Text>();
        if (b2Txt != null)
        {
            b2Txt.fontSize = 18;
            b2Txt.resizeTextForBestFit = true;
            b2Txt.resizeTextMinSize = 12;
            b2Txt.resizeTextMaxSize = 20;
        }

        // 4. Support Dev message* Section
        string devTitle = LocalizationManager.Instance.Get("SUPPORT_DEV_TITLE");
        if (string.IsNullOrEmpty(devTitle) || devTitle == "SUPPORT_DEV_TITLE") devTitle = "Support Dev message*";
        MakeText(card.transform, devTitle, 0, 175, 26, new Color(1f, 0.75f, 0.2f), FontStyle.Bold);

        string devMsgBody = LocalizationManager.Instance.Get("SUPPORT_DEV_BODY");
        if (string.IsNullOrEmpty(devMsgBody) || devMsgBody == "SUPPORT_DEV_BODY" || devMsgBody == "SUPPORT_DEV_MSG")
        {
            devMsgBody = LocalizationManager.Instance.Get("SUPPORT_DEV_MSG");
            if (string.IsNullOrEmpty(devMsgBody) || devMsgBody == "SUPPORT_DEV_MSG" || devMsgBody == "SUPPORT_DEV_BODY")
            {
                devMsgBody = "If you are having problems sending or receiving your BTC. You can open your account using other Bitcoin wallets like Electrum, Trust Wallet and Exodus. All you need is your 12-word passphrase to open your account.";
            }
        }
        Text devMsgText = MakeText(card.transform, devMsgBody, 0, 85, 21, new Color(0.9f, 0.9f, 0.95f), FontStyle.Normal);
        devMsgText.rectTransform.sizeDelta = new Vector2(850, 120);
        devMsgText.horizontalOverflow = HorizontalWrapMode.Wrap;
        devMsgText.verticalOverflow = VerticalWrapMode.Overflow;
        devMsgText.alignment = TextAnchor.MiddleCenter;

        // 5. Donate / Support BTC Section
        MakeText(card.transform, LocalizationManager.Instance.Get("DONATE_TITLE"), 0, -15, 26, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

#if TESTNET_BUILD
        string devAddr = "tb1qh33ew5gqek462d3dl5kgjpvghrsshq7dvssm03";
        Text addrText = MakeText(card.transform, devAddr + "\n<size=20>(Please send testnet3 only to this address)</size>", 0, -80, 22, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
#else
        string devAddr = "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh";
        Text addrText = MakeText(card.transform, devAddr, 0, -80, 22, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
#endif
        addrText.horizontalOverflow = HorizontalWrapMode.Wrap;
        addrText.rectTransform.sizeDelta = new Vector2(840, 60);
        addrText.alignment = TextAnchor.MiddleCenter;
        
        MakeButton(card.transform, LocalizationManager.Instance.Get("BTN_COPY_ADDRESS"), 0, -165, 460, 75, () => {
             GUIUtility.systemCopyBuffer = devAddr;
             ShowStatus(LocalizationManager.Instance.Get("DONATE_COPIED"), ThemeManager.Instance.GetSuccessColor());
        });

        // 6. Close Button
        MakeButtonOutline(card.transform, LocalizationManager.Instance.Get("MENU_CLOSE"), 0, -270, 380, 80, () => donateOverlay.SetActive(false));
    }

    // --- ADDRESS TYPE SELECTION ---
    private GameObject addressTypeOverlay;

    void OnGenerateNewAddress()
    {
        if(addressTypeOverlay == null) BuildAddressTypeOverlay();
        addressTypeOverlay.SetActive(true);
        addressTypeOverlay.transform.SetAsLastSibling();
    }

    void BuildAddressTypeOverlay()
    {
        GameObject canvasObj = GameObject.Find("Canvas");
        addressTypeOverlay = new GameObject("AddressTypeOverlay");
        addressTypeOverlay.layer = 5;
        addressTypeOverlay.transform.SetParent(canvasObj.transform, false);
        
        RectTransform rt = addressTypeOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin=Vector2.zero; rt.offsetMax=Vector2.zero;
        
        Image img = addressTypeOverlay.AddComponent<Image>();
        img.color = new Color(0,0,0, 0.9f);
        
        GameObject card = MakePanel(addressTypeOverlay.transform, "Card", 0, 0, 900, 850, ThemeManager.Instance.GetCardColor());
        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif
        if (isDesktop)
        {
            card.transform.localScale = new Vector3(0.95f, 0.95f, 1f);
        }
        
        MakeText(card.transform, LocalizationManager.Instance.Get("ADDR_TYPE_TITLE"), 0, 300, 48, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
        MakeText(card.transform, LocalizationManager.Instance.Get("ADDR_TYPE_MESSAGE"), 0, 230, 28, new Color(0.8f, 0.8f, 0.8f), FontStyle.Normal);

        float y = 100;
        
        // SegWit Button - with proper text sizing for multi-line
        GameObject segBtn = MakeButton(card.transform, LocalizationManager.Instance.Get("ADDR_TYPE_SEGWIT"), 0, y, 800, 160, () => {
            GenerateAddress(NBitcoin.ScriptPubKeyType.Segwit);
            addressTypeOverlay.SetActive(false);
        });
        segBtn.GetComponent<Image>().color = new Color(0.2f, 0.6f, 1f);
        // Fix text to fill button properly
        Text segText = segBtn.GetComponentInChildren<Text>();
        if (segText != null)
        {
            segText.rectTransform.anchorMin = Vector2.zero;
            segText.rectTransform.anchorMax = Vector2.one;
            segText.rectTransform.offsetMin = new Vector2(10, 10);
            segText.rectTransform.offsetMax = new Vector2(-10, -10);
        }
        
        y -= 200;

        // Legacy Button - with proper text sizing for multi-line
        GameObject legBtn = MakeButton(card.transform, LocalizationManager.Instance.Get("ADDR_TYPE_LEGACY"), 0, y, 800, 160, () => {
            GenerateAddress(NBitcoin.ScriptPubKeyType.Legacy);
            addressTypeOverlay.SetActive(false);
        });
        legBtn.GetComponent<Image>().color = new Color(1f, 0.5f, 0.2f);
        Text legText = legBtn.GetComponentInChildren<Text>();
        if (legText != null)
        {
            legText.rectTransform.anchorMin = Vector2.zero;
            legText.rectTransform.anchorMax = Vector2.one;
            legText.rectTransform.offsetMin = new Vector2(10, 10);
            legText.rectTransform.offsetMax = new Vector2(-10, -10);
        }

        MakeButton(card.transform, LocalizationManager.Instance.Get("BTN_CANCEL"), 0, -350, 400, 100, () => addressTypeOverlay.SetActive(false))
            .GetComponent<Image>().color = new Color(0.3f, 0.4f, 0.6f); 
    }

    void GenerateAddress(NBitcoin.ScriptPubKeyType type)
    {
        try
        {
            WalletData wallet = WalletManager2.Instance.GetCurrentWallet();
            if (wallet == null)
            {
                ShowStatus(LocalizationManager.Instance.Get("ERR_NO_WALLET_LOADED"), Color.red);
                return;
            }
            
            if (string.IsNullOrEmpty(wallet.seedPhrase))
            {
                ShowStatus(LocalizationManager.Instance.Get("ERR_CANNOT_GEN_IMPORTED"), Color.red);
                return;
            }
            
            int nextIndex = WalletManager2.Instance.GetNextAddressIndex();
            NBitcoin.Network net = NBitcoin.Network.Main; 
            BitcoinWalletCore core = new BitcoinWalletCore(wallet.seedPhrase, net);
            string newAddr = core.GetAddress(nextIndex, type); 
            
            if (string.IsNullOrEmpty(newAddr))
            {
                ShowStatus(LocalizationManager.Instance.Get("ERR_FAILED_GEN_ADDR"), Color.red);
                return;
            }
            
            WalletManager2.Instance.AddGeneratedAddress(newAddr, nextIndex);
            WalletManager2.Instance.UpdateMainAddress(newAddr);
            
            // Refresh UI Consistently - Show parent address in orange above new address
            string typeName = type == NBitcoin.ScriptPubKeyType.Segwit ? "SegWit" : "Legacy";
            
            // Get the permanent parent address (set once during wallet creation)
            string parentAddr = GetParentAddress(wallet);
            string truncParent = parentAddr.Length > 20 ? parentAddr.Substring(0, 10) + "..." + parentAddr.Substring(parentAddr.Length - 8) : parentAddr;
#if TESTNET_BUILD
            string netColor = "#3399FF";
            string netName = "TestNet";
#else
            string netColor = "#FF8C00";
            string netName = "MainNet";
#endif
            string newText = $"<size=24><color={netColor}>Parent Address: {truncParent}</color></size>\n{newAddr}\n<size=24><color=#888888>{typeName} • {netName}</color></size>";
            if(addressDisplayText) addressDisplayText.text = newText;
            
            ShowStatus(LocalizationManager.Instance.Get("ADDR_GENERATED") + $" ({typeName})", ThemeManager.Instance.GetSuccessColor());
            Debug.Log($"[ModernMobileWallet] Generated {typeName} address: {newAddr} (index {nextIndex})");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ModernMobileWallet] GenerateAddress Error: {ex.Message}");
            ShowStatus($"Error: {ex.Message}", Color.red);
        }
    }

    void OnDeleteWallet()
    {
        // Show warning confirmation dialog before deleting
        BuildDeleteWalletWarning();
    }

    private GameObject deleteWalletWarningDialog;

    void BuildDeleteWalletWarning()
    {
        GameObject canvasObj = GameObject.Find("Canvas");
        if (deleteWalletWarningDialog != null) Destroy(deleteWalletWarningDialog);

        deleteWalletWarningDialog = new GameObject("DeleteWalletWarning");
        deleteWalletWarningDialog.layer = 5;
        deleteWalletWarningDialog.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = deleteWalletWarningDialog.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        deleteWalletWarningDialog.AddComponent<Image>().color = new Color(0, 0, 0, 0.9f);

        // Panel
        GameObject panel = new GameObject("WarningPanel");
        panel.layer = 5;
        panel.transform.SetParent(deleteWalletWarningDialog.transform, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(850, 700);
        panel.AddComponent<Image>().color = ThemeManager.Instance.GetBackgroundColor();

        // Warning Icon
        MakeText(panel.transform, "⚠️", 0, 260, 70, Color.yellow, FontStyle.Normal);

        // Warning Title
        MakeText(panel.transform, LocalizationManager.Instance.Get("DELETE_CONFIRM_TITLE"), 0, 180, 44, ThemeManager.Instance.GetErrorColor(), FontStyle.Bold);

        // Warning Message
        Text msgText = MakeText(panel.transform, LocalizationManager.Instance.Get("DELETE_CONFIRM_MSG"), 0, 30, 28, ThemeManager.Instance.GetTextColor(), FontStyle.Normal);
        msgText.rectTransform.sizeDelta = new Vector2(750, 250);

        // Yes Button (Red - Danger)
        MakeButton(panel.transform, LocalizationManager.Instance.Get("DELETE_YES"), -180, -150, 320, 110, () => {
            Destroy(deleteWalletWarningDialog);
            deleteWalletWarningDialog = null;
            WalletManager2.Instance.DeleteWallet();
            SceneManager.LoadScene(1);
        }).GetComponent<Image>().color = ThemeManager.Instance.GetErrorColor();

        // No Button (Gray - Cancel)
        MakeButton(panel.transform, LocalizationManager.Instance.Get("DELETE_NO"), 180, -150, 320, 110, () => {
            Destroy(deleteWalletWarningDialog);
            deleteWalletWarningDialog = null;
        }).GetComponent<Image>().color = new Color(0.35f, 0.35f, 0.4f);

        deleteWalletWarningDialog.transform.SetAsLastSibling();
    }

    void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnSwitchNetwork()
    {
        WalletManager2.Instance.ToggleNetwork();
        LoadSavedWallet(); 
    }

    void ShowStatus(string message, Color color)
    {
         GameObject canvasObj = GameObject.Find("Canvas");
         if(!canvasObj) return;

         GameObject toast = MakePanel(canvasObj.transform, "Toast", 0, -650, 850, 140, color);
         Text t = MakeText(toast.transform, message, 0, 0, 26, Color.white, FontStyle.Bold);
         t.rectTransform.sizeDelta = new Vector2(810, 120);
         t.horizontalOverflow = HorizontalWrapMode.Wrap;
         t.verticalOverflow = VerticalWrapMode.Overflow;
         Destroy(toast, 4.0f);
    }
    


    // --- CHANGE PASSWORD CONFIRMATION DIALOG ---
    void BuildChangePasswordDialog()
    {
        GameObject canvasObj = GameObject.Find("Canvas");
        if(changePasswordDialog != null) Destroy(changePasswordDialog);

        changePasswordDialog = new GameObject("ChangePasswordDialog");
        changePasswordDialog.layer = 5;
        changePasswordDialog.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = changePasswordDialog.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        changePasswordDialog.AddComponent<Image>().color = new Color(0, 0, 0, 0.9f);

        // Panel
        GameObject panel = new GameObject("DialogPanel");
        panel.layer = 5;
        panel.transform.SetParent(changePasswordDialog.transform, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(850, 650);
        panel.AddComponent<Image>().color = ThemeManager.Instance.GetBackgroundColor();

        // Warning Icon + Title
        MakeText(panel.transform, LocalizationManager.Instance.Get("CHANGE_PIN_TITLE"), 0, 220, 44, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // Message
        Text msgText = MakeText(panel.transform, LocalizationManager.Instance.Get("CHANGE_PIN_MESSAGE"), 0, 60, 30, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        msgText.rectTransform.sizeDelta = new Vector2(750, 200);

        // Confirm Button
        MakeButton(panel.transform, LocalizationManager.Instance.Get("BTN_CONFIRM"), 0, -120, 700, 110, () => {
            Destroy(changePasswordDialog);
            changePasswordDialog = null;
            AuthManager.Instance.ResetAuth();
            SceneManager.LoadScene(0); // AppLock is Scene 0
        }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();

        // Cancel Button
        Color cancelColor = ThemeManager.Instance.IsDarkMode ? ThemeManager.Instance.GetCardColor() : new Color(0.75f, 0.75f, 0.75f);
        MakeButton(panel.transform, LocalizationManager.Instance.Get("BTN_CANCEL"), 0, -250, 700, 110, () => {
            Destroy(changePasswordDialog);
            changePasswordDialog = null;
        }).GetComponent<Image>().color = cancelColor;

        changePasswordDialog.transform.SetAsLastSibling();
    }

    // --- ONE-TIME EMERGENCY PASSPHRASE REVEAL CONFIRMATION ---
    private GameObject emergencyRevealConfirmDialog;

    private bool HasEmergencyRevealBeenUsed()
    {
        WalletData currentW = WalletManager2.Instance != null ? WalletManager2.Instance.GetCurrentWallet() : null;
        if (currentW == null) return false;
        string addr = !string.IsNullOrEmpty(currentW.address) ? currentW.address : "default";
        return PlayerPrefs.GetInt("EmergencyRevealUsed_" + addr, 0) == 1;
    }

    private void MarkEmergencyRevealAsUsed()
    {
        WalletData currentW = WalletManager2.Instance != null ? WalletManager2.Instance.GetCurrentWallet() : null;
        string addr = currentW != null && !string.IsNullOrEmpty(currentW.address) ? currentW.address : "default";
        PlayerPrefs.SetInt("EmergencyRevealUsed_" + addr, 1);
        PlayerPrefs.Save();
    }

    void ShowEmergencyRevealConfirmDialog()
    {
        if (emergencyRevealConfirmDialog != null) Destroy(emergencyRevealConfirmDialog);

        GameObject canvasObj = GameObject.Find("Canvas");
        if (canvasObj == null && mainCanvas != null) canvasObj = mainCanvas;

        emergencyRevealConfirmDialog = new GameObject("EmergencyRevealConfirmDialog");
        emergencyRevealConfirmDialog.layer = 5;
        emergencyRevealConfirmDialog.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = emergencyRevealConfirmDialog.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        emergencyRevealConfirmDialog.AddComponent<Image>().color = new Color(0, 0, 0, 0.9f);

        GameObject panel = new GameObject("ConfirmPanel");
        panel.layer = 5;
        panel.transform.SetParent(emergencyRevealConfirmDialog.transform, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(850, 750);
        panel.AddComponent<Image>().color = ThemeManager.Instance.GetBackgroundColor();

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif
        if (isDesktop)
        {
            panel.transform.localScale = new Vector3(0.65f, 0.65f, 1f);
        }

        // Icon
        MakeText(panel.transform, "⚠️", 0, 260, 64, new Color(0.95f, 0.55f, 0.1f), FontStyle.Normal);

        // Title
        string title = LocalizationManager.Instance?.Get("EMERGENCY_REVEAL_CONFIRM_TITLE") ?? "One-Time Passphrase Reveal";
        MakeText(panel.transform, title, 0, 180, 36, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // Message
        string confirmMsg = LocalizationManager.Instance?.Get("EMERGENCY_REVEAL_CONFIRM_MSG") 
            ?? "You have a one time passphrase reveal option. If you agree to exercise this option, the button will be deleted after passphrase is revealed. Do you want to reveal your 12-word passphrase?";
        Text msgText = MakeText(panel.transform, confirmMsg, 0, 20, 26, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        msgText.rectTransform.sizeDelta = new Vector2(760, 250);
        msgText.alignment = TextAnchor.MiddleCenter;
        msgText.horizontalOverflow = HorizontalWrapMode.Wrap;

        // Confirm Button (Orange / Amber)
        MakeButton(panel.transform, LocalizationManager.Instance.Get("BTN_CONFIRM"), -180, -230, 320, 100, () => {
            if (emergencyRevealConfirmDialog != null) Destroy(emergencyRevealConfirmDialog);
            emergencyRevealConfirmDialog = null;

            if (optionsOverlay != null)
            {
                optionsOverlay.SetActive(false);
                Destroy(optionsOverlay);
                isOptionsOpen = false;
            }

            MarkEmergencyRevealAsUsed();

            WalletData currentW = WalletManager2.Instance.GetCurrentWallet();
            if (currentW != null)
            {
                BuildFirstTimeKeysOverlay(currentW);
            }
        }).GetComponent<Image>().color = new Color(0.9f, 0.45f, 0.05f);

        // Cancel Button (Gray)
        MakeButton(panel.transform, LocalizationManager.Instance.Get("BTN_CANCEL"), 180, -230, 320, 100, () => {
            if (emergencyRevealConfirmDialog != null) Destroy(emergencyRevealConfirmDialog);
            emergencyRevealConfirmDialog = null;
        }).GetComponent<Image>().color = new Color(0.35f, 0.35f, 0.4f);

        emergencyRevealConfirmDialog.transform.SetAsLastSibling();
    }

    // --- FIRST-TIME KEYS BACKUP OVERLAY ---
    void BuildFirstTimeKeysOverlay(WalletData data)
    {
        ScreenSecurityManager.EnableProtection();
        try
        {
            // Re-derive keys from the stored seed phrase
            NBitcoin.Network net = NBitcoin.Network.Main;
            BitcoinWalletCore core = new BitcoinWalletCore(data.seedPhrase, net);
            
            string seedPhrase = core.GetSeedPhrase();
            string wifKey = core.GetSegWitPrivateKey(0);
            string hexKey = core.GetSegWitPrivateKeyHex(0);
            
            // Split seed phrase into words
            string[] words = seedPhrase.Split(' ');

            // Build overlay
            GameObject canvasObj = GameObject.Find("Canvas");
            keysOverlay = new GameObject("KeysOverlay");
            keysOverlay.layer = 5;
            keysOverlay.transform.SetParent(canvasObj.transform, false);
            
            RectTransform rt = keysOverlay.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            keysOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.95f);

            // Scrollable content panel - Theme-aware
            GameObject panel = new GameObject("KeysPanel");
            panel.layer = 5;
            panel.transform.SetParent(keysOverlay.transform, false);
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(950, 1700);
            panel.AddComponent<Image>().color = ThemeManager.Instance.GetBackgroundColor();

            bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
            isDesktop = true;
#endif
            if (isDesktop)
            {
                panel.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
            }

            float y = 750;

            // Colors from theme
            Color titleColor = ThemeManager.Instance.GetTextColor();
            Color subtitleColor = ThemeManager.Instance.GetSecondaryTextColor();
            Color labelColor = ThemeManager.Instance.GetTextColor();
            Color keyTextColor = ThemeManager.Instance.GetTextColor();
            Color boxColor = ThemeManager.Instance.IsDarkMode ? new Color(0.2f, 0.2f, 0.25f) : new Color(0.85f, 0.85f, 0.85f);
            Color copyBtnColor = ThemeManager.Instance.GetAccentColor();
            Color cellColor = ThemeManager.Instance.IsDarkMode ? new Color(0.12f, 0.12f, 0.16f) : new Color(0.93f, 0.93f, 0.96f);

            // Warning Title
            MakeText(panel.transform, LocalizationManager.Instance.Get("BACKUP_KEYS_TITLE"), 0, y, 48, titleColor, FontStyle.Bold);
            y -= 80;
            MakeText(panel.transform, LocalizationManager.Instance.Get("BACKUP_KEYS_MESSAGE"), 0, y, 26, subtitleColor, FontStyle.Normal);
            y -= 120;

            // --- 12 Seed Words ---
            MakeText(panel.transform, LocalizationManager.Instance.Get("BACKUP_SEED_WORDS"), -250, y, 34, labelColor, FontStyle.Bold);
            string capturedSeed = seedPhrase;
            MakeButtonSmall(panel.transform, LocalizationManager.Instance.Get("BTN_COPY"), 350, y, 150, 60, () => {
                GUIUtility.systemCopyBuffer = capturedSeed;
                ShowStatus(LocalizationManager.Instance.Get("SEED_COPIED"), ThemeManager.Instance.GetSuccessColor());
            }).GetComponent<Image>().color = copyBtnColor;
            y -= 50;

            // Seed words 2-column grid
            {
                float gridW = 870f;
                float cellW = 410f;
                float cellH = 50f;
                float gapX = 20f;
                float gapY = 8f;
                int half = (words.Length + 1) / 2; // 6 rows
                float gridH = half * cellH + (half - 1) * gapY + 20;
                
                GameObject seedBox = MakePanel(panel.transform, "SeedBox", 0, y - gridH / 2, gridW, gridH, boxColor);
                
                float col0X = -(cellW / 2 + gapX / 2);
                float col1X = (cellW / 2 + gapX / 2);
                float startCellY = gridH / 2 - 10;
                
                Color numColor = new Color(0.5f, 0.5f, 0.6f);
                Color wordColor = new Color(1f, 0.75f, 0.2f);
                
                for (int i = 0; i < words.Length; i++)
                {
                    int col = i < half ? 0 : 1;
                    int row = i < half ? i : i - half;
                    
                    float cx = col == 0 ? col0X : col1X;
                    float cy = startCellY - row * (cellH + gapY) - cellH / 2;
                    
                    // Cell background
                    GameObject cell = MakePanel(seedBox.transform, $"W{i}", cx, cy, cellW, cellH, cellColor);
                    
                    // Number (left side)
                    Text numT = MakeText(cell.transform, $"{i + 1}.", 0, 0, 22, numColor, FontStyle.Normal);
                    numT.alignment = TextAnchor.MiddleLeft;
                    numT.rectTransform.anchorMin = new Vector2(0, 0);
                    numT.rectTransform.anchorMax = new Vector2(0, 1);
                    numT.rectTransform.pivot = new Vector2(0, 0.5f);
                    numT.rectTransform.anchoredPosition = new Vector2(12, 0);
                    numT.rectTransform.sizeDelta = new Vector2(50, 0);
                    
                    // Word (next to number)
                    Text wordT = MakeText(cell.transform, words[i], 0, 0, 26, wordColor, FontStyle.Bold);
                    wordT.alignment = TextAnchor.MiddleLeft;
                    wordT.rectTransform.anchorMin = new Vector2(0, 0);
                    wordT.rectTransform.anchorMax = new Vector2(1, 1);
                    wordT.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    wordT.rectTransform.offsetMin = new Vector2(55, 2);
                    wordT.rectTransform.offsetMax = new Vector2(-8, -2);
                }
                
                y -= (gridH + 30);
            }

            // --- Private Key (WIF) ---
            MakeText(panel.transform, LocalizationManager.Instance.Get("BACKUP_WIF_KEY"), -220, y, 34, labelColor, FontStyle.Bold);
            string capturedWif = wifKey;
            MakeButtonSmall(panel.transform, LocalizationManager.Instance.Get("BTN_COPY"), 350, y, 150, 60, () => {
                GUIUtility.systemCopyBuffer = capturedWif;
                ShowStatus(LocalizationManager.Instance.Get("WIF_COPIED"), ThemeManager.Instance.GetSuccessColor());
            }).GetComponent<Image>().color = copyBtnColor;
            y -= 60;

            GameObject wifBox = MakePanel(panel.transform, "WifBox", 0, y - 30, 850, 120, boxColor);
            Text wifText = MakeText(wifBox.transform, wifKey, 0, 0, 24, keyTextColor, FontStyle.Bold);
            wifText.alignment = TextAnchor.MiddleLeft;
            wifText.rectTransform.anchorMin = Vector2.zero;
            wifText.rectTransform.anchorMax = Vector2.one;
            wifText.rectTransform.offsetMin = new Vector2(20, 5);
            wifText.rectTransform.offsetMax = new Vector2(-20, -5);
            y -= 150;

            // --- Private Key (Hex) ---
            MakeText(panel.transform, LocalizationManager.Instance.Get("BACKUP_HEX_KEY"), -220, y, 34, labelColor, FontStyle.Bold);
            string capturedHex = hexKey;
            MakeButtonSmall(panel.transform, LocalizationManager.Instance.Get("BTN_COPY"), 350, y, 150, 60, () => {
                GUIUtility.systemCopyBuffer = capturedHex;
                ShowStatus(LocalizationManager.Instance.Get("HEX_COPIED"), ThemeManager.Instance.GetSuccessColor());
            }).GetComponent<Image>().color = copyBtnColor;
            y -= 60;

            GameObject hexBox = MakePanel(panel.transform, "HexBox", 0, y - 30, 850, 120, boxColor);
            Text hexText = MakeText(hexBox.transform, hexKey, 0, 0, 24, keyTextColor, FontStyle.Bold);
            hexText.alignment = TextAnchor.MiddleLeft;
            hexText.rectTransform.anchorMin = Vector2.zero;
            hexText.rectTransform.anchorMax = Vector2.one;
            hexText.rectTransform.offsetMin = new Vector2(20, 5);
            hexText.rectTransform.offsetMax = new Vector2(-20, -5);
            y -= 180;

            // --- Dismiss Button ---
            MakeButton(panel.transform, LocalizationManager.Instance.Get("BACKUP_CONFIRM"), 0, y, 800, 120, () => {
                data.hasShownKeys = true;
                // Save the flag to file
                try {
                    string savePath = Application.persistentDataPath + "/wallet_mainnet.json";
                    string json = JsonUtility.ToJson(data, true);
                    System.IO.File.WriteAllText(savePath, json);
                } catch (System.Exception ex) { Debug.LogError("Save hasShownKeys error: " + ex.Message); }
                
                ScreenSecurityManager.DisableProtection();
                Destroy(keysOverlay);
                keysOverlay = null;
            }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();

            keysOverlay.transform.SetAsLastSibling();
            Debug.Log("First-time keys backup overlay displayed.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to build keys overlay: {ex.Message}");
        }
    }

    void AutoRefreshBalance()
    {
        if(autoRefresh && WalletManager2.Instance.HasWallet()) OnRefreshBalance();
    }

    // Public method to force balance refresh (can be called from UI button)
    public void ForceRefreshBalance()
    {
        Debug.Log("[ForceRefreshBalance] Manual refresh triggered");
        _isRefreshingBalance = false; // Reset flag to allow refresh
        ShowCachedBalanceNow(); // Show current cached value first
        OnRefreshBalance(); // Then start fresh refresh
    }

    async void OnRefreshBalance()
    {
        if (_isDestroyed || this == null) return;
        if(!WalletManager2.Instance.HasWallet()) return;
        if(_isRefreshingBalance) { Debug.Log("[OnRefreshBalance] Already refreshing, skipping..."); return; }
        
        _isRefreshingBalance = true;
        Debug.Log("[OnRefreshBalance] Starting balance refresh...");
        
        try
        {
            WalletData data = WalletManager2.Instance.GetCurrentWallet();
#if TESTNET_BUILD
            double oldBalance = data.cachedTestnetBalance;
            Debug.Log($"[OnRefreshBalance] Starting - cachedTestnetBalance={oldBalance:F8} BTCT, hasCompletedInitialScan={data.hasCompletedInitialScan}");
#else
            double oldBalance = data.cachedBalance;
            double mainBal = data.balance;
            Debug.Log($"[OnRefreshBalance] Starting - cachedBalance={oldBalance:F8}, balance={mainBal:F8} BTC, hasCompletedInitialScan={data.hasCompletedInitialScan}");
#endif
            bool wasInitialScan = data.hasCompletedInitialScan;

            // ✅ FIX: Start from cached value so UI never shows 0 during scan
            decimal total = (decimal)oldBalance;
            int successfulApiCalls = 0; // Track how many addresses responded OK
            Debug.Log($"[OnRefreshBalance] Starting scan with total={total:F8}");
            
            if (data.isPrivateKeyImport || string.IsNullOrEmpty(data.seedPhrase))
            {
                // Private key import: scan single address
                BlockCypherApiCore api = new BlockCypherApiCore();
#if TESTNET_BUILD
                // ✅ FIX: For TestNet import, use testnet address (tb1...) not mainnet address
                WalletManager2.Instance.EnsureTestnetAddress();
                string scanAddr = WalletManager2.Instance.GetTestnetAddress();
                if (string.IsNullOrEmpty(scanAddr)) scanAddr = data.address; // fallback
                try {
                    decimal directBalance = await api.GetBalance(scanAddr, true);
                    total = directBalance;
                    successfulApiCalls++;
                    Debug.Log($"[OnRefreshBalance] TestNet PK import, addr={scanAddr}, bal={directBalance:F8}");
                } catch (System.Exception ex) {
                    Debug.LogWarning($"[OnRefreshBalance] PK import balance fetch failed: {ex.Message}");
                }
#else
                if (string.IsNullOrEmpty(data.address)) return;
                try {
                    decimal directBalance = await api.GetBalance(data.address, false);
                    total = directBalance;
                    successfulApiCalls++;
                    Debug.Log($"[OnRefreshBalance] MainNet PK import, addr={data.address}, bal={directBalance:F8}");
                } catch (System.Exception ex) {
                    Debug.LogWarning($"[OnRefreshBalance] PK import balance fetch failed: {ex.Message}");
                }
#endif
            }
            else
            {
                // Seed phrase wallet: Smart scan with Gap Limit
                BlockCypherApiCore api = new BlockCypherApiCore();
                NBitcoin.Mnemonic mnemonic = new NBitcoin.Mnemonic(data.seedPhrase, NBitcoin.Wordlist.English);
                NBitcoin.ExtKey masterKey = mnemonic.DeriveExtKey();
                decimal scanTotal = 0;
#if TESTNET_BUILD
                NBitcoin.Network network = NBitcoin.Network.TestNet;
                int coinType = 1;
                bool isTestNetScan = true;

                // ✅ FIX: Scan testnetAddress (tb1...) first — this is the primary TestNet address
                WalletManager2.Instance.EnsureTestnetAddress();
                string tbAddr = WalletManager2.Instance.GetTestnetAddress();
                if (!string.IsNullOrEmpty(tbAddr))
                {
                    try {
                        decimal tbBal = await api.GetBalance(tbAddr, true);
                        if (_isDestroyed || this == null) return;
                        successfulApiCalls++;
                        if (tbBal > 0) { scanTotal += tbBal; }
                        Debug.Log($"[OnRefreshBalance] TestNet main addr {tbAddr} → {tbBal:F8} BTCT");
                    } catch (System.Exception ex) {
                        Debug.LogWarning($"[OnRefreshBalance] TestNet main addr failed: {ex.Message}");
                    }
                }
#else
                NBitcoin.Network network = NBitcoin.Network.Main;
                int coinType = 0;
                bool isTestNetScan = false;

                // ✅ FIX: Scan primary MainNet address (bc1q...) FIRST
                string mainAddr = data.address;
                if (!string.IsNullOrEmpty(mainAddr))
                {
                    try {
                        decimal primaryBal = await api.GetBalance(mainAddr, false);
                        if (_isDestroyed || this == null) return;
                        successfulApiCalls++;
                        if (primaryBal > 0) { scanTotal += primaryBal; }
                        Debug.Log($"[OnRefreshBalance] MainNet main addr {mainAddr} → {primaryBal:F8} BTC");
                    } catch (System.Exception ex) {
                        Debug.LogWarning($"[OnRefreshBalance] MainNet main addr failed: {ex.Message}");
                    }
                }
#endif
                
                var addressConfigs = new[]
                {
                    new { Name = "SegWit", Path = $"m/84'/{coinType}'/0'/0", Type = NBitcoin.ScriptPubKeyType.Segwit },
                    new { Name = "Legacy", Path = $"m/44'/{coinType}'/0'/0", Type = NBitcoin.ScriptPubKeyType.Legacy },
                    new { Name = "P2SH-SegWit", Path = $"m/49'/{coinType}'/0'/0", Type = NBitcoin.ScriptPubKeyType.SegwitP2SH }
                };

                const int GAP_LIMIT = 5;
                const int MAX_ADDRESSES = 30;


                foreach (var config in addressConfigs)
                {
                    int consecutiveEmpty = 0;
                    int index = 0;
                    
                    while (consecutiveEmpty < GAP_LIMIT && index < MAX_ADDRESSES)
                    {
                        NBitcoin.KeyPath keyPath = new NBitcoin.KeyPath($"{config.Path}/{index}");
                        string addrStr = masterKey.Derive(keyPath).PrivateKey.PubKey
                            .GetAddress(config.Type, network).ToString();

#if TESTNET_BUILD
                        // ✅ CRITICAL FIX: Skip if this derived address == tbAddr (already counted above)
                        // This was causing the balance to be DOUBLED in TestNet
                        if (addrStr == tbAddr)
                        {
                            index++;
                            continue;
                        }
#else
                        // ✅ Skip if this derived address == mainAddr (already counted above)
                        if (addrStr == mainAddr)
                        {
                            index++;
                            continue;
                        }
#endif
                        
                        try
                        {
                            decimal balance = await api.GetBalance(addrStr, isTestNetScan);
                            if (_isDestroyed || this == null) return;
                            successfulApiCalls++;
                            
                            if (balance > 0)
                            {
                                scanTotal += balance;
                                consecutiveEmpty = 0;
                                decimal partialTotal = scanTotal;
#if TESTNET_BUILD
                                if (cryptoBalanceText != null) cryptoBalanceText.text = $"{partialTotal:F8} tBTC";
#else
                                if (cryptoBalanceText != null) cryptoBalanceText.text = $"{partialTotal:F8} BTC";
#endif
                                Debug.Log($"{config.Name} [{index}]: {addrStr} - {balance:F8}");
                            }
                            else
                            {
                                consecutiveEmpty++;
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"Error checking {addrStr}: {ex.Message}");
                            consecutiveEmpty++;
                        }
                        
                        await System.Threading.Tasks.Task.Delay(300);
                        index++;
                    }
                    
                    Debug.Log($"{config.Name} scan complete: {index} addresses, total={scanTotal:F8}");
                }
                
#if !TESTNET_BUILD
                // Generated addresses are MainNet only — skip for TestNet
                if (data.generatedAddresses != null)
                {
                    foreach (var genAddr in data.generatedAddresses)
                    {
                        if (genAddr.address == mainAddr) continue;
                        try
                        {
                            decimal balance = await api.GetBalance(genAddr.address, isTestNetScan);
                            successfulApiCalls++;
                            if (balance > 0)
                            {
                                scanTotal += balance;
                                Debug.Log($"Generated [{genAddr.index}]: {genAddr.address} - {balance:F8} BTC");
                            }
                            await System.Threading.Tasks.Task.Delay(300);
                        }
                        catch { }
                    }
                }
#endif
                
                // ✅ FIX: Only use scan result if we got at least one successful API response
                if (successfulApiCalls > 0)
                    total = scanTotal;
                // else: keep 'total = oldBalance' (cached value) – don't wipe balance on API failure
                
                WalletManager2.Instance.SyncTransactionHistory();
            }
            
            if (_isDestroyed || this == null) return;

            // ✅ FIX: Only update stored balance if scan actually succeeded
            if (successfulApiCalls > 0)
            {
                WalletManager2.Instance.UpdateBalance((double)total);
                Debug.Log($"[OnRefreshBalance] Balance updated to: {total:F8} (successfulApiCalls={successfulApiCalls})");
            }
            else
            {
                Debug.LogWarning($"[OnRefreshBalance] No successful API calls, keeping old balance. Displaying: {total:F8}");
            }

#if TESTNET_BUILD
            string cur = "tBTC";
            // UpdateBalance (called below) already sets cachedTestnetBalance — no need to set it twice
#else
            string cur = "BTC";
#endif
            // ✅ Only update balance if scan succeeded — avoids wiping valid balance on API timeout
            if (cryptoBalanceText != null)
            {
                cryptoBalanceText.text = $"{total:F8} {cur}";
                Debug.Log($"[OnRefreshBalance] UI: {total:F8} {cur}");
            }

            // ── Payment notification guard ─────────────────────────────────
            // Only notify when:
            //   1. scan actually returned valid data (successfulApiCalls > 0)
            //   2. hasCompletedInitialScan was already true (not the very first run)
            //   3. balance increased by at least DUST_THRESHOLD vs LAST NOTIFIED balance
            //   4. cooldown period has elapsed since the last notification
            if (successfulApiCalls > 0 && wasInitialScan)
            {
                // Initialise tracking balance on first successful scan
                if (_lastNotifiedBalance < 0)
                    _lastNotifiedBalance = (double)total;

                double diff = (double)total - _lastNotifiedBalance;
                bool cooldownOk = (System.DateTime.UtcNow - _lastNotificationTime).TotalMinutes >= NOTIF_COOLDOWN_MINUTES;

                if (diff > DUST_THRESHOLD_BTC && cooldownOk)
                {
#if TESTNET_BUILD
                    string notifCur = "BTCT";
#else
                    string notifCur = "BTC";
#endif
                    NotificationManager.Instance.SendNotification(
                        LocalizationManager.Instance.Get("NOTIFICATION_PAYMENT_RECEIVED"),
                        LocalizationManager.Instance.Get("NOTIFICATION_PAYMENT_AMOUNT") + $" {diff:F8} {notifCur}"
                    );
                    _lastNotifiedBalance = (double)total;   // update watermark
                    _lastNotificationTime = System.DateTime.UtcNow;
                    Debug.Log($"[PaymentNotif] Sent: +{diff:F8} {notifCur} (total={total:F8})");
                }
                else if (diff > DUST_THRESHOLD_BTC)
                {
#if TESTNET_BUILD
                    Debug.Log($"[PaymentNotif] Suppressed (cooldown): +{diff:F8} BTCT");
#else
                    Debug.Log($"[PaymentNotif] Suppressed (cooldown): +{diff:F8} BTC");
#endif
                }
            }
            
            StartCoroutine(UpdateUSDValue((float)total));
        }
        catch (System.Exception ex) 
        { 
            Debug.LogError($"[OnRefreshBalance] Error: {ex.Message}");
        }
        finally
        {
            _isRefreshingBalance = false;
            Debug.Log("[OnRefreshBalance] Refresh completed.");
        }
    }


    // --- UPDATED PRICE FETCHING ---

    IEnumerator UpdateUSDValue(float currentWalletBtc)
    {
        string currencyCode = PlayerPrefs.GetString("SelectedCurrency", "USD").ToLower();
        string symbol = CurrencyHelper.GetSymbol(currencyCode.ToUpper());
#if TESTNET_BUILD
        if (currentWalletBtc <= 0 && WalletManager2.Instance.HasWallet())
            currentWalletBtc = (float)WalletManager2.Instance.GetCurrentWallet().cachedTestnetBalance;

        // Fetch real tBTC price from CoinPaprika
        string tbtcUrl = "https://api.coinpaprika.com/v1/tickers/tbtc-bitcoin-testnet";
        using (UnityWebRequest tReq = UnityWebRequest.Get(tbtcUrl))
        {
            yield return tReq.SendWebRequest();
            if (tReq.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    double price = ParseCoinpaprikaPrice(tReq.downloadHandler.text, "USD");
                    if (price > 0)
                    {
                        lastBtcPrice = price;
                        if (tickerMainnetText != null)
                            tickerMainnetText.text = $"tBTC: {symbol}{price:F4}";
                        if (totalBalanceText != null)
                            totalBalanceText.text = $"≈ {symbol}{(currentWalletBtc * price):F4} {currencyCode.ToUpper()}";
                        CheckPriceAlert(price);
                    }
                }
                catch { }
            }
        }
        yield break;
#else
        if (currentWalletBtc <= 0 && WalletManager2.Instance.HasWallet())
            currentWalletBtc = (float)WalletManager2.Instance.GetCurrentWallet().cachedBalance;

        // Tier 1: Blockchain.info ticker (fast, multi-currency, unblocked globally)
        string binfoUrl = "https://blockchain.info/ticker";
        using (UnityWebRequest req = UnityWebRequest.Get(binfoUrl))
        {
            req.timeout = 8;
            req.SetRequestHeader("User-Agent", "CoinHouseWallet/1.0");
            yield return req.SendWebRequest();

            bool gotPrice = false;
            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    double price = ParseBlockchainInfoPrice(req.downloadHandler.text, currencyCode);
                    if (price > 0)
                    {
                        gotPrice = true;
                        lastBtcPrice = price;
                        if (tickerMainnetText != null)
                            tickerMainnetText.text = $"BTC: {symbol}{price:N0}";
                        if (totalBalanceText != null)
                            totalBalanceText.text = $"≈ {symbol}{(currentWalletBtc * price):N2} {currencyCode.ToUpper()}";
                        CheckPriceAlert(price);
                    }
                }
                catch { }
            }

            // Tier 2: Binance Ticker (reliable fallback)
            if (!gotPrice)
            {
                string binanceUrl = "https://api.binance.com/api/v3/ticker/price?symbol=BTCUSDT";
                using (UnityWebRequest bReq = UnityWebRequest.Get(binanceUrl))
                {
                    bReq.timeout = 8;
                    bReq.SetRequestHeader("User-Agent", "CoinHouseWallet/1.0");
                    yield return bReq.SendWebRequest();

                    if (bReq.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            double price = ParseBinancePrice(bReq.downloadHandler.text);
                            if (price > 0)
                            {
                                gotPrice = true;
                                lastBtcPrice = price;
                                if (tickerMainnetText != null)
                                    tickerMainnetText.text = $"BTC: {symbol}{price:N0}";
                                if (totalBalanceText != null)
                                    totalBalanceText.text = $"≈ {symbol}{(currentWalletBtc * price):N2} {currencyCode.ToUpper()}";
                                CheckPriceAlert(price);
                            }
                        }
                        catch { }
                    }
                }
            }

            // Tier 3: CoinPaprika Fallback
            if (!gotPrice)
                yield return StartCoroutine(FetchPriceFallback(currentWalletBtc, currencyCode, symbol));
        }
#endif
    }

    IEnumerator FetchPriceFallback(float currentWalletBtc, string currencyCode, string symbol)
    {
        // Mainnet-only fallback (CoinPaprika with User-Agent)
        string fallbackUrl = "https://api.coinpaprika.com/v1/tickers/btc-bitcoin";
        using (UnityWebRequest req = UnityWebRequest.Get(fallbackUrl))
        {
            req.timeout = 8;
            req.SetRequestHeader("User-Agent", "CoinHouseWallet/1.0");
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                try {
                    double price = ParseCoinpaprikaPrice(req.downloadHandler.text, "USD");
                    if (price > 0)
                    {
                        lastBtcPrice = price;
                        if (tickerMainnetText)
                            tickerMainnetText.text = $"BTC: {symbol}{price:N0}";
                        if (totalBalanceText)
                            totalBalanceText.text = $"≈ {symbol}{(currentWalletBtc * price):N2} {currencyCode.ToUpper()}";
                        CheckPriceAlert(price);
                    }
                } catch {}
            }
        }
    }

    double ParseBlockchainInfoPrice(string json, string currencyCode)
    {
        try {
            string codeUpper = currencyCode.ToUpper();
            int codeIdx = json.IndexOf("\"" + codeUpper + "\"");
            if (codeIdx < 0) {
                codeIdx = json.IndexOf("\"USD\"");
            }
            if (codeIdx >= 0) {
                int lastIdx = json.IndexOf("\"last\":", codeIdx);
                if (lastIdx > codeIdx) {
                    int start = lastIdx + 7;
                    int end = json.IndexOf(",", start);
                    if (end < 0) end = json.IndexOf("}", start);
                    if (end > start) {
                        string valStr = json.Substring(start, end - start).Trim();
                        if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
                            return result;
                    }
                }
            }
        } catch { }
        return 0;
    }

    double ParseBinancePrice(string json)
    {
        try {
            int pIdx = json.IndexOf("\"price\":\"");
            if (pIdx >= 0) {
                int start = pIdx + 9;
                int end = json.IndexOf("\"", start);
                if (end > start) {
                    string valStr = json.Substring(start, end - start).Trim();
                    if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
                        return result;
                }
            }
        } catch { }
        return 0;
    }

    /// Fetches only the current BTC/TBTC price and updates the ticker — no balance scan needed.
    IEnumerator FetchPriceOnly()
    {
        yield return new WaitForSeconds(1.5f); // Small delay so UI is ready
        StartCoroutine(UpdateUSDValue(0f)); // Pass 0 — only updates ticker, not total balance text
    }

    double ParsePriceSimple(string json, string currencyCode)
    {
        try {
            string search = "\"" + currencyCode + "\":";
            int idx = json.IndexOf(search);
            if(idx > 0) {
                int start = idx + search.Length;
                int end = json.IndexOf("}", start);
                if(end == -1) end = json.IndexOf(",", start);
                if(end > start) {
                    string valStr = json.Substring(start, end - start).Trim();
                    if(double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result)) {
                        return result;
                    }
                }
            }
        } catch {}
        return 0;
    }

    double ParseCoinpaprikaPrice(string json, string currencyCode)
    {
        try {
            // Find "currencyCode":{"price":
            string search = "\"" + currencyCode + "\":{\"price\":";
            int idx = json.IndexOf(search);
            if(idx > 0) {
                int start = idx + search.Length;
                int end = json.IndexOf(",", start);
                if(end > start) {
                    string valStr = json.Substring(start, end - start).Trim();
                    if(double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result)) {
                        return result;
                    }
                }
            }
        } catch {}
        return 0;
    }

    double ParseOhlcvPrice(string json)
    {
        try {
            // OHLCV response: [{"close":0.073,...}]
            string search = "\"close\":";
            int idx = json.IndexOf(search);
            if (idx > 0) {
                int start = idx + search.Length;
                int end = json.IndexOf(",", start);
                if (end == -1) end = json.IndexOf("}", start);
                if (end > start) {
                    string valStr = json.Substring(start, end - start).Trim();
                    if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result)) {
                        return result;
                    }
                }
            }
        } catch {}
        return 0;
    }

    // --- HELPER FACTORY (Updated for New Design) ---
    
    Button MakeActionCircle(Transform parent, string label, string icon, float x, float y, System.Action onClick)
    {
        GameObject obj = new GameObject("Btn_" + label);
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(180, 220); 
        
        GameObject circle = new GameObject("Circle");
        circle.layer = 5; circle.transform.SetParent(obj.transform, false);
        RectTransform crt = circle.AddComponent<RectTransform>();
        crt.anchoredPosition = new Vector2(0, 20); 
        crt.sizeDelta = new Vector2(160, 160);
        Image cImg = circle.AddComponent<Image>();
        cImg.color = ThemeManager.Instance.IsDarkMode ? new Color(0.2f, 0.2f, 0.25f) : new Color(0.95f, 0.95f, 0.97f);
        
        Button btn = circle.AddComponent<Button>();
        btn.onClick.AddListener(() => AudioManager.Instance.PlayClick()); // Sound
        btn.onClick.AddListener(() => onClick());
        
        GameObject iObj = new GameObject("Icon");
        iObj.transform.SetParent(circle.transform, false);
        Text iTxt = iObj.AddComponent<Text>();
        iTxt.text = icon;
        iTxt.font = GetEmojiFont();
        iTxt.fontSize = 70;
        iTxt.alignment = TextAnchor.MiddleCenter;
        iTxt.color = ThemeManager.Instance.GetTextColor();
        
        RectTransform irt = iObj.GetComponent<RectTransform>();
        irt.sizeDelta = new Vector2(160, 160);
        
        if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
        {
            if (icon != "↑" && icon != "↓")
            {
                irt.anchoredPosition = new Vector2(0, -18);
            }
        }
        
        GameObject lObj = new GameObject("Label");
        lObj.transform.SetParent(obj.transform, false);
        RectTransform lrt = lObj.AddComponent<RectTransform>();
        lrt.anchoredPosition = new Vector2(0, -90);
        lrt.sizeDelta = new Vector2(250, 50); // Increased width to fit text
        Text lTxt = lObj.AddComponent<Text>();
        lTxt.text = label;
        lTxt.font = GetFont();
        lTxt.fontSize = 28;
        lTxt.alignment = TextAnchor.MiddleCenter;
        lTxt.horizontalOverflow = HorizontalWrapMode.Overflow; // Prevent breaking into two lines
        lTxt.color = ThemeManager.Instance.GetTextColor();
        
        return btn;
    }
    
    Font GetFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 24);
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Roboto", 24);
        return f;
    }

    Font GetEmojiFont()
    {
        Font f = null;
        if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
        {
            f = Font.CreateDynamicFontFromOSFont("Segoe UI Emoji", 24);
        }
        if (f == null) f = GetFont();
        return f;
    }

    GameObject MakePanel(Transform parent, string name, float x, float y, float w, float h, Color color) {
        GameObject obj = new GameObject(name);
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(w, h);
        obj.AddComponent<Image>().color = color;
        return obj;
    }
    
    Text MakeText(Transform parent, string txt, float x, float y, int size, Color col, FontStyle style) {
        GameObject obj = new GameObject("Text");
        obj.layer = 5; obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(1000, 80);
        Text text = obj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt); text.fontSize = size; text.color = col; text.fontStyle = style; 
        text.font = GetFont();
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false; 
        return text;
    }
    
    GameObject MakeButton(Transform parent, string txt, float x, float y, float w, float h, System.Action onClick) {
        GameObject obj = new GameObject("Button");
        obj.layer = 5; obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(w, h);
        Image img = obj.AddComponent<Image>();
        img.color = ThemeManager.Instance.GetAccentColor();
        Button btn = obj.AddComponent<Button>();
        btn.onClick.AddListener(() => AudioManager.Instance.PlayClick()); // Sound
        btn.onClick.AddListener(()=>onClick());
        GameObject tObj = new GameObject("Text");
        tObj.layer = 5; tObj.transform.SetParent(obj.transform, false);
        RectTransform tRT = tObj.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = new Vector2(5, 5); tRT.offsetMax = new Vector2(-5, -5);
        Text t = tObj.AddComponent<Text>();
        t.text = LocalizationManager.CleanIcons(txt); t.fontSize = 32; t.color = Color.white;
        t.font = GetFont();
        t.alignment = TextAnchor.MiddleCenter; t.fontStyle = FontStyle.Bold;
        return obj;
    }
    
    void MakeButtonOutline(Transform parent, string txt, float x, float y, float w, float h, System.Action onClick) {
        GameObject obj = new GameObject("Button");
        obj.layer = 5; obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(w, h);
        Image img = obj.AddComponent<Image>();
        img.color = Color.clear; // Transparent
        Button btn = obj.AddComponent<Button>();
        btn.onClick.AddListener(() => AudioManager.Instance.PlayCloseMenu()); // Close menu sound
        btn.onClick.AddListener(()=>onClick());
        
        GameObject tObj = new GameObject("Text");
        tObj.layer = 5; tObj.transform.SetParent(obj.transform, false);
        RectTransform tRT = tObj.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        Text t = tObj.AddComponent<Text>();
        t.text = LocalizationManager.CleanIcons(txt); t.fontSize = 32; 
        t.color = ThemeManager.Instance.GetAccentColor();
        t.font = GetFont();
        t.alignment = TextAnchor.MiddleCenter; t.fontStyle = FontStyle.Bold;
    }

    GameObject MakeButtonSmall(Transform parent, string txt, float x, float y, float w, float h, System.Action onClick) {
         GameObject obj = new GameObject("BtnSmall");
        obj.layer = 5; obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(w, h);
        Image img = obj.AddComponent<Image>();
        img.color = new Color(0.9f, 0.9f, 0.95f);
        if(ThemeManager.Instance.IsDarkMode) img.color = new Color(0.3f,0.3f,0.3f);
        
        Button btn = obj.AddComponent<Button>();
        btn.onClick.AddListener(() => AudioManager.Instance.PlayClick()); // Sound
        btn.onClick.AddListener(()=>onClick());
        
        Text t = MakeText(obj.transform, txt, 0, 0, 24, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        // Ensure text fills button area and is perfectly centered
        t.rectTransform.anchorMin = Vector2.zero;
        t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        t.rectTransform.sizeDelta = Vector2.zero;
        t.rectTransform.offsetMin = new Vector2(4, 2);
        t.rectTransform.offsetMax = new Vector2(-4, -2);
        t.rectTransform.anchoredPosition = Vector2.zero;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        return obj;
    }

    InputField MakeInput(Transform parent, string placeholder, float x, float y, float w, float h)
    {
        GameObject obj = new GameObject("InputField");
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        
        Image img = obj.AddComponent<Image>();
        img.color = ThemeManager.Instance.IsDarkMode ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.9f, 0.9f, 0.9f);
        
        InputField input = obj.AddComponent<InputField>();
        
        GameObject textObj = new GameObject("Text");
        textObj.layer = 5;
        textObj.transform.SetParent(obj.transform, false);
        RectTransform trt = textObj.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(15, 5); trt.offsetMax = new Vector2(-15, -5);
        Text text = textObj.AddComponent<Text>();
        text.font = GetFont();
        text.fontSize = 28;
        text.color = ThemeManager.Instance.GetTextColor();
        text.alignment = TextAnchor.MiddleLeft;
        
        GameObject phObj = new GameObject("Placeholder");
        phObj.layer = 5;
        phObj.transform.SetParent(obj.transform, false);
        RectTransform prt = phObj.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(15, 5); prt.offsetMax = new Vector2(-15, -5);
        Text phText = phObj.AddComponent<Text>();
        phText.font = GetFont();
        phText.fontSize = 28;
        phText.color = ThemeManager.Instance.GetSecondaryTextColor();
        phText.text = LocalizationManager.CleanIcons(placeholder);
        phText.alignment = TextAnchor.MiddleLeft;
        
        input.textComponent = text;
        input.placeholder = phText;
        
        return input;
    }
    
    // --- DAILY TESTNET FAUCET ---
#if TESTNET_BUILD
    private const string DAILY_FAUCET_URL      = "http://89.106.206.25:2500/api/claim";
    private const string FAUCET_LAST_CLAIM_KEY = "DailyFaucetLastClaim";
    private const long   FAUCET_CLAIM_AMOUNT   = 1000000; // 0.01 BTCT in satoshis
    private GameObject   dailyFaucetOverlay;

    private long GetFaucetCooldownRemaining(out bool isFirst)
    {
        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string storedClaim = PlayerPrefs.GetString(FAUCET_LAST_CLAIM_KEY, "");
        if (!string.IsNullOrEmpty(storedClaim) && long.TryParse(storedClaim, out long lastClaim) && lastClaim > 0)
        {
            isFirst = false;
            long elapsed = now - lastClaim;
            return 86400 - elapsed; // 24 hours
        }
        else
        {
            isFirst = true;
            string firstOpen = PlayerPrefs.GetString("FAUCET_FIRST_OPEN_KEY", "");
            long firstOpenSec = 0;
            if (string.IsNullOrEmpty(firstOpen) || !long.TryParse(firstOpen, out firstOpenSec))
            {
                firstOpenSec = now;
                PlayerPrefs.SetString("FAUCET_FIRST_OPEN_KEY", firstOpenSec.ToString());
                PlayerPrefs.Save();
            }
            long elapsed = now - firstOpenSec;
            return 7200 - elapsed; // 2 hours
        }
    }
    private bool isWalletActiveStatus = false;
    private bool isCheckingActiveStatus = true;

    private async System.Threading.Tasks.Task<bool> IsWalletActive(string mainnetAddr, string testnetAddr)
    {
        // Condition C: Wallet Age (24 hours)
        string createKey = "WalletCreatedTime_" + mainnetAddr;
        if (PlayerPrefs.HasKey(createKey))
        {
            string storedEpoch = PlayerPrefs.GetString(createKey, "0");
            long createdEpoch = 0;
            if (long.TryParse(storedEpoch, out createdEpoch))
            {
                long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (now - createdEpoch >= 86400) // 24 hours
                {
                    Debug.Log($"[DailyFaucet] Wallet active due to age (created: {createdEpoch}, elapsed: {now - createdEpoch}s)");
                    return true;
                }
            }
        }

        // Condition B: Mining claims (at least 1 claim completed)
        int miningClaims = PlayerPrefs.GetInt("MiningClaimsCount_" + testnetAddr, 0);
        if (miningClaims >= 1)
        {
            Debug.Log($"[DailyFaucet] Wallet active due to mining claims ({miningClaims})");
            return true;
        }

        // Condition A: Blockchain transactions (at least 1 tx on mainnet or testnet)
        BlockCypherApiCore api = new BlockCypherApiCore();
        
        // Check TestNet transaction count
        if (!string.IsNullOrEmpty(testnetAddr))
        {
            int testnetTx = await api.GetTransactionCount(testnetAddr, true);
            if (testnetTx > 0)
            {
                Debug.Log($"[DailyFaucet] Wallet active due to TestNet transactions ({testnetTx})");
                return true;
            }
        }

        // Check MainNet transaction count
        if (!string.IsNullOrEmpty(mainnetAddr))
        {
            int mainnetTx = await api.GetTransactionCount(mainnetAddr, false);
            if (mainnetTx > 0)
            {
                Debug.Log($"[DailyFaucet] Wallet active due to MainNet transactions ({mainnetTx})");
                return true;
            }
        }

        Debug.Log("[DailyFaucet] Wallet is INACTIVE");
        return false;
    }

    System.Collections.IEnumerator CheckWalletVerification(string mainnetAddr, string testnetAddr, System.Action onComplete)
    {
        isCheckingActiveStatus = true;
        isWalletActiveStatus = false;

        var task = IsWalletActive(mainnetAddr, testnetAddr);

        while (!task.IsCompleted)
        {
            yield return null;
        }

        try
        {
            isWalletActiveStatus = task.Result;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[DailyFaucet] Error in IsWalletActive: {ex.Message}");
            isWalletActiveStatus = false;
        }
        isCheckingActiveStatus = false;
        onComplete?.Invoke();
    }

    void BuildDailyFaucetOverlay()
    {
        if (dailyFaucetOverlay != null) Destroy(dailyFaucetOverlay);
        if (faucetTimerCoroutine != null) StopCoroutine(faucetTimerCoroutine);

        GameObject canvasObj = GameObject.Find("Canvas");
        dailyFaucetOverlay = new GameObject("DailyFaucetOverlay");
        dailyFaucetOverlay.layer = 5;
        dailyFaucetOverlay.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = dailyFaucetOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        dailyFaucetOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.92f);

        GameObject card = MakePanel(dailyFaucetOverlay.transform, "Card", 0, 50, 900, 800, ThemeManager.Instance.GetCardColor());
        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif
        if (isDesktop)
        {
            card.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
        }

        MakeText(card.transform, "🚰 " + LocalizationManager.Instance.Get("FAUCET_TITLE"), 0, 320, 38, new Color(0.1f, 0.8f, 0.4f), FontStyle.Bold);

        // Testnet address display
        string tAddr = WalletManager2.Instance.HasWallet() ? WalletManager2.Instance.GetTestnetAddress() : "";
        string mainAddr = WalletManager2.Instance.HasWallet() ? WalletManager2.Instance.GetCurrentWallet().address : "";
        if (string.IsNullOrEmpty(tAddr) && WalletManager2.Instance.HasWallet())
        {
            WalletManager2.Instance.EnsureTestnetAddress();
            tAddr = WalletManager2.Instance.GetTestnetAddress();
        }
        string truncAddr = (!string.IsNullOrEmpty(tAddr) && tAddr.Length > 20)
            ? tAddr.Substring(0, 10) + "..." + tAddr.Substring(tAddr.Length - 8)
            : tAddr;
        Text addrLbl = MakeText(card.transform, "Address: " + truncAddr, 0, 260, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        addrLbl.rectTransform.sizeDelta = new Vector2(820, 40);

        Text statusTxt = MakeText(card.transform, LocalizationManager.Instance.Get("FAUCET_CHECKING_STATUS"), 0, 200, 28, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        statusTxt.rectTransform.sizeDelta = new Vector2(820, 50);

        Text detailsTxt = MakeText(card.transform, "", 0, 60, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        detailsTxt.rectTransform.sizeDelta = new Vector2(820, 200);
        detailsTxt.alignment = TextAnchor.MiddleCenter;

        // Claim button (updated via coroutine)
        GameObject claimBtnObj = MakeButton(card.transform, "", 0, -130, 700, 100, () => {
            if (isCheckingActiveStatus || !isWalletActiveStatus) return;
            if (GetFaucetCooldownRemaining(out _) > 0) return;

            StartCoroutine(ClaimDailyFaucet(tAddr, statusTxt));
        });

        Button claimBtn = claimBtnObj.GetComponent<Button>();
        Image claimBtnImg = claimBtnObj.GetComponent<Image>();
        Text claimBtnTxt = claimBtnObj.GetComponentInChildren<Text>();
        // The button also displays the "next claim in hh:mm:ss" countdown — let long
        // localized strings shrink instead of clipping.
        claimBtnTxt.resizeTextForBestFit = true;
        claimBtnTxt.resizeTextMinSize = 16;
        claimBtnTxt.resizeTextMaxSize = 32;

        MakeButtonOutline(card.transform, LocalizationManager.Instance.Get("MENU_CLOSE"), 0, -260, 400, 80, () => {
            if (faucetTimerCoroutine != null) { StopCoroutine(faucetTimerCoroutine); faucetTimerCoroutine = null; }
            if (dailyFaucetOverlay != null) Destroy(dailyFaucetOverlay);
        });

        dailyFaucetOverlay.transform.SetAsLastSibling();

        // Start active state verification, and trigger the timer update coroutine
        StartCoroutine(CheckWalletVerification(mainAddr, tAddr, () => {
            // Callback when verification completes
        }));

        faucetTimerCoroutine = StartCoroutine(UpdateFaucetTimer(claimBtn, claimBtnTxt, claimBtnImg, statusTxt, detailsTxt));
    }

    System.Collections.IEnumerator UpdateFaucetTimer(Button claimBtn, Text claimBtnTxt, Image claimBtnImg, Text statusTxt, Text detailsTxt)
    {
        // Theme-aware status colors: the bright variants are unreadable on the light card
        bool darkTheme = ThemeManager.Instance.IsDarkMode;
        Color infoCol = darkTheme ? new Color(0.5f, 0.8f, 1f)  : new Color(0.1f, 0.4f, 0.8f);
        Color goodCol = darkTheme ? new Color(0.1f, 0.9f, 0.4f) : new Color(0.05f, 0.55f, 0.25f);
        Color warnCol = darkTheme ? new Color(1f, 0.7f, 0.2f)  : new Color(0.8f, 0.5f, 0f);
        Color errCol  = darkTheme ? new Color(1f, 0.3f, 0.3f)  : new Color(0.8f, 0.15f, 0.15f);

        while (dailyFaucetOverlay != null)
        {
            if (isCheckingActiveStatus)
            {
                statusTxt.text = LocalizationManager.Instance.Get("FAUCET_CHECKING_STATUS");
                statusTxt.color = infoCol;
                detailsTxt.text = "";

                if (claimBtnTxt != null)
                {
                    claimBtnTxt.text = LocalizationManager.Instance.Get("FAUCET_BTN_CLAIM_NOW");
                    claimBtnTxt.color = new Color(0.6f, 0.6f, 0.6f);
                }
                if (claimBtnImg != null)
                {
                    claimBtnImg.color = new Color(0.25f, 0.25f, 0.25f);
                }
                if (claimBtn != null)
                {
                    claimBtn.interactable = false;
                }
            }
            else if (!isWalletActiveStatus)
            {
                statusTxt.text = LocalizationManager.Instance.Get("FAUCET_INACTIVE_WARNING");
                statusTxt.color = errCol;
                detailsTxt.text = LocalizationManager.Instance.Get("FAUCET_ACTIVATE_STEPS");

                if (claimBtnTxt != null)
                {
                    claimBtnTxt.text = LocalizationManager.Instance.Get("FAUCET_BTN_CLAIM_NOW");
                    claimBtnTxt.color = new Color(0.6f, 0.6f, 0.6f);
                }
                if (claimBtnImg != null)
                {
                    claimBtnImg.color = new Color(0.25f, 0.25f, 0.25f);
                }
                if (claimBtn != null)
                {
                    claimBtn.interactable = false;
                }
            }
            else
            {
                long remaining = GetFaucetCooldownRemaining(out _);
                bool canClaim = remaining <= 0;

                detailsTxt.text = LocalizationManager.Instance.Get("FAUCET_WAIT_INFO");
                detailsTxt.color = goodCol;

                if (canClaim)
                {
                    statusTxt.text = LocalizationManager.Instance.Get("FAUCET_STATUS_LABEL") + " "
                        + LocalizationManager.Instance.Get("FAUCET_STATUS_READY");
                    statusTxt.color = goodCol;
                    if (claimBtnTxt != null)
                    {
                        claimBtnTxt.text = LocalizationManager.Instance.Get("FAUCET_BTN_CLAIM_NOW");
                        claimBtnTxt.color = Color.white;
                    }
                    if (claimBtnImg != null)
                    {
                        claimBtnImg.color = new Color(0.1f, 0.65f, 0.3f);
                    }
                    if (claimBtn != null)
                    {
                        claimBtn.interactable = true;
                    }
                }
                else
                {
                    long hrs  = remaining / 3600;
                    long mins = (remaining % 3600) / 60;
                    long secs = remaining % 60;
                    string timeStr = $"{hrs:D2}:{mins:D2}:{secs:D2}";
                    statusTxt.text = LocalizationManager.Instance.Get("FAUCET_STATUS_LABEL") + " "
                        + LocalizationManager.Instance.GetFormat("FAUCET_STATUS_COUNTDOWN", timeStr);
                    statusTxt.color = warnCol;
                    if (claimBtnTxt != null)
                    {
                        claimBtnTxt.text = LocalizationManager.Instance.Get("FAUCET_BTN_CLAIM_NOW");
                        claimBtnTxt.color = new Color(0.6f, 0.6f, 0.6f);
                    }
                    if (claimBtnImg != null)
                    {
                        claimBtnImg.color = new Color(0.25f, 0.25f, 0.25f);
                    }
                    if (claimBtn != null)
                    {
                        claimBtn.interactable = false;
                    }
                }
            }
            yield return new WaitForSeconds(1f);
        }
    }

    System.Collections.IEnumerator ClaimDailyFaucet(string address, Text statusLabel)
    {
        if (string.IsNullOrEmpty(address))
        {
            if (statusLabel != null) { statusLabel.text = LocalizationManager.Instance.Get("FAUCET_NO_TESTNET_ADDR"); statusLabel.color = Color.red; }
            yield break;
        }

        if (statusLabel != null) { statusLabel.text = LocalizationManager.Instance.Get("FAUCET_CLAIMING"); statusLabel.color = ThemeManager.Instance.IsDarkMode ? new Color(0.5f, 0.8f, 1f) : new Color(0.1f, 0.4f, 0.8f); }

        string json = $"{{\"address\":\"{address}\",\"amount\":{FAUCET_CLAIM_AMOUNT}}}";
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);

        using (var req = UnityWebRequest.Put(DAILY_FAUCET_URL, bodyRaw))
        {
            req.method = "POST";
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 20;
            yield return req.SendWebRequest();

            bool success = req.result == UnityWebRequest.Result.Success
                           && (req.responseCode == 200 || req.responseCode == 201);

            if (success)
            {
                long nowSec = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                PlayerPrefs.SetString(FAUCET_LAST_CLAIM_KEY, nowSec.ToString());
                PlayerPrefs.Save();

                if (statusLabel != null) { statusLabel.text = LocalizationManager.Instance.Get("FAUCET_SUCCESS"); statusLabel.color = ThemeManager.Instance.IsDarkMode ? new Color(0.1f, 0.9f, 0.4f) : new Color(0.05f, 0.55f, 0.25f); }
                ShowStatus(LocalizationManager.Instance.Get("FAUCET_SUCCESS"), ThemeManager.Instance.GetSuccessColor());
                Debug.Log($"[DailyFaucet] Claimed 0.01 BTCT → {address}");

                // Refresh balance after short delay
                yield return new WaitForSeconds(3f);
                StartCoroutine(FetchTestnetBalance());
            }
            else
            {
                string err = req.downloadHandler != null ? req.downloadHandler.text : req.error;
                if (statusLabel != null) { statusLabel.text = LocalizationManager.Instance.Get("FAUCET_ERROR") + "\n" + err; statusLabel.color = Color.red; }
                Debug.LogWarning($"[DailyFaucet] Failed: {req.responseCode} {err}");
            }
        }
    }
#endif

    // --- TIMELOCK HISTORY ---
    [System.Serializable]
    private class TimeLockRecord
    {
        public string date;          // human-readable date
        public string lockType;      // "Absolute" or "Relative"
        public string toAddress;
        public string amount;
        public string lockValue;     // minutes or blocks
        public string txId;
        public string redeemScript;
        public string p2shAddress;
        public int    outputIndex;
        public string network;       // "TestNet" or "MainNet"
    }

    [System.Serializable]
    private class TimeLockRecordList { public List<TimeLockRecord> records = new List<TimeLockRecord>(); }

    private const string TL_HISTORY_KEY = "TimeLockHistory";

    void SaveTimeLockRecord(TimeLockRecord rec)
    {
        var list = LoadTimeLockRecords();
        list.Insert(0, rec); // newest first
        if (list.Count > 50) list.RemoveRange(50, list.Count - 50); // cap at 50
        var wrapper = new TimeLockRecordList(); wrapper.records = list;
        PlayerPrefs.SetString(TL_HISTORY_KEY, JsonUtility.ToJson(wrapper));
        PlayerPrefs.Save();
    }

    List<TimeLockRecord> LoadTimeLockRecords()
    {
        string json = PlayerPrefs.GetString(TL_HISTORY_KEY, "");
        if (string.IsNullOrEmpty(json)) return new List<TimeLockRecord>();
        try { return JsonUtility.FromJson<TimeLockRecordList>(json)?.records ?? new List<TimeLockRecord>(); }
        catch { return new List<TimeLockRecord>(); }
    }

    // --- BIP-0065 TIME LOCK FEATURE ---
    private GameObject timeLockOverlay;
    private GameObject timeLockCard;
    
    void BuildTimeLockOverlay()
    {
        if (timeLockOverlay != null) Destroy(timeLockOverlay);

        GameObject canvasObj = GameObject.Find("Canvas");
        timeLockOverlay = new GameObject("TimeLockOverlay");
        timeLockOverlay.layer = 5;
        timeLockOverlay.transform.SetParent(canvasObj.transform, false);
        
        RectTransform rt = timeLockOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        
        Image img = timeLockOverlay.AddComponent<Image>();
        img.color = new Color(0,0,0, 0.95f);
        
        // Show onboarding first if never shown before
        if (PlayerPrefs.GetInt("TL_OnboardingShown", 0) == 0)
        {
            BuildTimeLockOnboardingOverlay(() => {
                PlayerPrefs.SetInt("TL_OnboardingShown", 1);
                PlayerPrefs.Save();
                BuildTimeLockCard(true);
            });
        }
        else
        {
            BuildTimeLockCard(true);
        }
    }

    void BuildTimeLockOnboardingOverlay(System.Action onComplete)
    {
        GameObject canvasObj = GameObject.Find("Canvas");
        GameObject onboardingOverlay = new GameObject("TLOnboardingOverlay");
        onboardingOverlay.layer = 5;
        onboardingOverlay.transform.SetParent(canvasObj.transform, false);

        RectTransform bgRt = onboardingOverlay.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
        onboardingOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.96f);
        onboardingOverlay.transform.SetAsLastSibling();

        GameObject card = null;
        int currentSlide = 0;
        int totalSlides = 3;

        void RenderSlide()
        {
            if (card != null) Destroy(card);
            card = MakePanel(onboardingOverlay.transform, "OnboardingCard", 0, 0, 950, 1500, ThemeManager.Instance.GetCardColor());
            bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
            isDesktop = true;
#endif
            if (isDesktop)
            {
                card.transform.localScale = new Vector3(0.65f, 0.65f, 1f);
            }

            string lang = LocalizationManager.Instance.GetCurrentLanguage();
            bool isFA = (lang == "Persian" || lang == "Farsi");

            string titleText = LocalizationManager.Instance.Get($"TL_ONBOARD_TITLE_{currentSlide + 1}");
            string descText = LocalizationManager.Instance.Get($"TL_ONBOARD_DESC_{currentSlide + 1}");
            string pageText = string.Format(LocalizationManager.Instance.Get("TL_ONBOARD_PAGE"), currentSlide + 1, totalSlides);
            string nextLabel = currentSlide == totalSlides - 1 ? LocalizationManager.Instance.Get("TL_ONBOARD_DONE") : LocalizationManager.Instance.Get("TL_ONBOARD_NEXT");
            string prevLabel = LocalizationManager.Instance.Get("TL_ONBOARD_PREV");

            MakeText(card.transform, titleText, 0, 550, 42, new Color(0.6f, 0.2f, 0.9f), FontStyle.Bold);
            
            Text dTxt = MakeText(card.transform, descText, 0, 0, 28, ThemeManager.Instance.GetTextColor(), FontStyle.Normal);
            dTxt.alignment = isFA ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            RectTransform dRt = dTxt.GetComponent<RectTransform>();
            dRt.sizeDelta = new Vector2(820, 800);
            dRt.anchoredPosition = new Vector2(0, 50);

            MakeText(card.transform, pageText, 0, -450, 24, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);

            GameObject nextBtn = MakeButton(card.transform, nextLabel, currentSlide > 0 ? 220 : 0, -580, currentSlide > 0 ? 380 : 800, 100, () => {
                AudioManager.Instance.PlayClick();
                if (currentSlide < totalSlides - 1)
                {
                    currentSlide++;
                    RenderSlide();
                }
                else
                {
                    Destroy(onboardingOverlay);
                    onComplete?.Invoke();
                }
            });
            nextBtn.GetComponent<Image>().color = new Color(0.5f, 0.0f, 0.8f);

            if (currentSlide > 0)
            {
                MakeButtonOutline(card.transform, prevLabel, -220, -580, 380, 100, () => {
                    AudioManager.Instance.PlayClick();
                    currentSlide--;
                    RenderSlide();
                });
            }
        }

        RenderSlide();
    }
    
    void BuildTimeLockCard(bool isAbsoluteLock)
    {
        if (timeLockCard != null) Destroy(timeLockCard);

        // ── card sized to content — ✅ taller to fit fee field ──
        timeLockCard = MakePanel(timeLockOverlay.transform, "Card", 0, 0, 960, 1800, ThemeManager.Instance.GetCardColor());
        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif
        if (isDesktop)
        {
            timeLockCard.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
        }

        // layout: top of card = +760, step down for each row
        float y = 720f;
        float gap  = 18f;  // extra gap between sections

        // ── header ────────────────────────────────────────────────────────
        MakeText(timeLockCard.transform, LocalizationManager.Instance.Get("TL_TITLE"), 0, y, 46,
            new Color(0.6f, 0.2f, 0.9f), FontStyle.Bold);
        MakeButton(timeLockCard.transform, "❓", 410, y, 68, 68, () => {
            BuildTimeLockOnboardingOverlay(null);
        }).GetComponent<Image>().color = new Color(0.18f, 0.28f, 0.48f);
        y -= 58f;

        // ── tabs ──────────────────────────────────────────────────────────
        MakeButton(timeLockCard.transform, LocalizationManager.Instance.Get("TL_TAB_ABS"), -220, y, 430, 76, () => BuildTimeLockCard(true))
            .GetComponent<Image>().color = isAbsoluteLock ? new Color(0.6f, 0.2f, 0.9f) : new Color(0.28f, 0.28f, 0.32f);
        MakeButton(timeLockCard.transform, LocalizationManager.Instance.Get("TL_TAB_REL"),  220, y, 430, 76, () => BuildTimeLockCard(false))
            .GetComponent<Image>().color = !isAbsoluteLock ? new Color(0.6f, 0.2f, 0.9f) : new Color(0.28f, 0.28f, 0.32f);
        y -= 76f + gap;

        // ── description ───────────────────────────────────────────────────
        string descKey = isAbsoluteLock ? "TL_DESC" : "TL_DESC_REL";
        Text descTxt = MakeText(timeLockCard.transform, LocalizationManager.Instance.Get(descKey), 0, y, 22,
            ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        descTxt.rectTransform.sizeDelta = new Vector2(900, 56);
        y -= 64f + gap;

        // ── recipient address ─────────────────────────────────────────────
        Text addrLbl = MakeText(timeLockCard.transform, LocalizationManager.Instance.Get("TL_ADDR_LBL"), 0, y, 26,
            ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        addrLbl.alignment = TextAnchor.MiddleLeft;
        addrLbl.rectTransform.sizeDelta = new Vector2(880, 40);
        y -= 70f;
        InputField addrInput = MakeInput(timeLockCard.transform, "", 0, y, 880, 88);
        y -= 88f + 6f;

        // ✅ FIX: Get correct network address — no silent fallback to wrong-network address
        string myAddress = "";
        bool addrValid = false;
        if (WalletManager2.Instance.HasWallet())
        {
            var w = WalletManager2.Instance.GetCurrentWallet();
#if TESTNET_BUILD
            myAddress = WalletManager2.Instance.GetTestnetAddress();
            addrValid = !string.IsNullOrEmpty(myAddress) && myAddress.StartsWith("tb1");
            if (!addrValid) myAddress = ""; // Don't pre-fill with wrong-network address
#else
            myAddress = !string.IsNullOrEmpty(w.address) ? w.address : "";
            addrValid = !string.IsNullOrEmpty(myAddress);
#endif
        }
        addrInput.text = myAddress;

        MakeButton(timeLockCard.transform, LocalizationManager.Instance.Get("TL_BTN_USE_MY_ADDR"), 0, y, 500, 68, () => {
            if (!string.IsNullOrEmpty(myAddress)) addrInput.text = myAddress;
        }).GetComponent<Image>().color = new Color(0.18f, 0.38f, 0.58f);
        y -= 68f + gap * 1.5f;

        // ── amount ────────────────────────────────────────────────────────
        Text amtLbl = MakeText(timeLockCard.transform, LocalizationManager.Instance.Get("TL_AMT_LBL"), 0, y, 26,
            ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        amtLbl.alignment = TextAnchor.MiddleLeft;
        amtLbl.rectTransform.sizeDelta = new Vector2(880, 40);
        y -= 70f;
        InputField amtInput = MakeInput(timeLockCard.transform, "0.001", 0, y, 880, 88);
        amtInput.contentType = InputField.ContentType.DecimalNumber;
        y -= 88f + gap * 1.5f;

        // ── lock time ─────────────────────────────────────────────────────
        string timeKey = isAbsoluteLock ? "TL_TIME_LBL" : "TL_TIME_LBL_REL";
        Text timeLbl = MakeText(timeLockCard.transform, LocalizationManager.Instance.Get(timeKey), 0, y, 26,
            ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        timeLbl.alignment = TextAnchor.MiddleLeft;
        timeLbl.rectTransform.sizeDelta = new Vector2(880, 40);
        y -= 70f;
        InputField timeInput = MakeInput(timeLockCard.transform, isAbsoluteLock ? "60" : "10", 0, y, 880, 88);
        timeInput.contentType = InputField.ContentType.IntegerNumber;
        y -= 88f + 4f;

        string hint = isAbsoluteLock ? "⏱ Minutes from now" : "⛓ Blocks to wait (~10 min each)";
        MakeText(timeLockCard.transform, hint, 0, y, 22, new Color(0.4f, 0.65f, 1f), FontStyle.Italic);
        y -= 34f + gap;

        // ── fee ──────────────────────────────────────────────────────────
#if TESTNET_BUILD
        string defaultFee = PlayerPrefs.GetString("TL_FeePref", "0.00001");
        string feeHintStr = "⚡ Fee (BTCT) — 0.00001 recommended for TestNet";
#else
        string defaultFee = PlayerPrefs.GetString("TL_FeePref", "0.00005");
        string feeHintStr = "⚡ Fee (BTC) — 0.00005 recommended for MainNet";
#endif
        Text feeLbl = MakeText(timeLockCard.transform, "Network Fee (BTC)", 0, y, 26,
            ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        feeLbl.alignment = TextAnchor.MiddleLeft;
        feeLbl.rectTransform.sizeDelta = new Vector2(880, 40);
        y -= 70f;
        InputField feeInput = MakeInput(timeLockCard.transform, defaultFee, 0, y, 880, 88);
        feeInput.contentType = InputField.ContentType.DecimalNumber;
        feeInput.onValueChanged.AddListener((val) => {
            PlayerPrefs.SetString("TL_FeePref", val);
            PlayerPrefs.Save();
        });
        y -= 88f + 4f;
        MakeText(timeLockCard.transform, feeHintStr, 0, y, 20, new Color(0.5f, 0.8f, 0.5f), FontStyle.Italic);
        y -= 32f + gap;

        // ── error label ───────────────────────────────────────────────────
        Text errLabel = MakeText(timeLockCard.transform, "", 0, y, 24, new Color(1f, 0.3f, 0.3f), FontStyle.Bold);
        errLabel.rectTransform.sizeDelta = new Vector2(900, 44);
        y -= 50f + gap;

        // ── what is timelock button ───────────────────────────────────────
        MakeButton(timeLockCard.transform, LocalizationManager.Instance.Get("TL_WHAT_IS"), 0, y, 880, 104, () => {
            BuildTimeLockOnboardingOverlay(null);
        }).GetComponent<Image>().color = new Color(0.8f, 0.15f, 0.15f);
        y -= 104f + gap;

        // ── lock button ───────────────────────────────────────────────────
        MakeButton(timeLockCard.transform, LocalizationManager.Instance.Get("TL_BTN_LOCK"), 0, y, 880, 104, () => {
            errLabel.text = "";
            if (string.IsNullOrWhiteSpace(addrInput.text))  { errLabel.text = LocalizationManager.Instance.Get("TL_ERR_NO_ADDR"); return; }
            if (string.IsNullOrWhiteSpace(amtInput.text))   { errLabel.text = LocalizationManager.Instance.Get("TL_ERR_NO_AMT"); return; }
            if (string.IsNullOrWhiteSpace(timeInput.text))  { errLabel.text = LocalizationManager.Instance.Get("TL_ERR_NO_TIME"); return; }
            // ✅ FIX: Validate fee input
            string feeStr = string.IsNullOrWhiteSpace(feeInput.text) ? defaultFee : feeInput.text.Trim();
            if (!decimal.TryParse(feeStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal feeVal) || feeVal <= 0)
            {
                errLabel.text = LocalizationManager.Instance.Get("TL_ERR_INVALID_FEE"); return;
            }
            // ✅ FIX: Pass fee to ExecuteTimeLock
            ExecuteTimeLock(addrInput.text.Trim(), amtInput.text.Trim(), timeInput.text.Trim(), isAbsoluteLock, feeStr);
        }).GetComponent<Image>().color = new Color(0.5f, 0.0f, 0.8f);
        y -= 104f + gap;

        // ── history + close ───────────────────────────────────────────────
        MakeButton(timeLockCard.transform, LocalizationManager.Instance.Get("TL_BTN_HISTORY"), -220, y, 420, 76, () => BuildTimeLockHistoryOverlay())
            .GetComponent<Image>().color = new Color(0.18f, 0.28f, 0.48f);
        MakeButtonOutline(timeLockCard.transform, LocalizationManager.Instance.Get("MENU_CLOSE"), 220, y, 420, 76, () => Destroy(timeLockOverlay));
    }

    // Pending TimeLock state for PIN verification
    private static string pendingTL_Addr;
    private static string pendingTL_Amt;
    private static string pendingTL_Time;
    private static bool pendingTL_IsAbsolute;
    private static string pendingTL_Fee; // ✅ FIX: store fee separately

    void ExecuteTimeLock(string addr, string amtStr, string timeStr, bool isAbsoluteLock, string feeStr)
    {
        // Save pending state
        pendingTL_Addr = addr;
        pendingTL_Amt = amtStr;
        pendingTL_Time = timeStr;
        pendingTL_IsAbsolute = isAbsoluteLock;
        pendingTL_Fee = feeStr; // ✅ FIX: save fee
        
        // Mark as pending transaction for PIN
        PlayerPrefs.SetInt("PendingTxPIN", 1);
        PlayerPrefs.SetString("PendingTL", "1");
        PlayerPrefs.Save();
        
        // Go to PIN verification
        SceneManager.LoadScene("AppLock");
    }

    async void ExecutePendingTimeLock()
    {
        // Wait a moment for UI to be fully initialized
        await System.Threading.Tasks.Task.Delay(100);
        
        // Clear pending flags
        PlayerPrefs.DeleteKey("PendingTL");
        PlayerPrefs.Save();
        
        string addr = pendingTL_Addr;
        string amtStr = pendingTL_Amt;
        string timeStr = pendingTL_Time;
        bool isAbsoluteLock = pendingTL_IsAbsolute;
        
        // Determine network from build flag
#if TESTNET_BUILD
        bool useTestNet = true;
#else
        bool useTestNet = false;
#endif

        Debug.Log($"[TimeLock] Executing pending timelock: addr={addr}, amt={amtStr}, time={timeStr}, network={(useTestNet ? "TestNet" : "MainNet")}");

        // ── Status label inside the card ──────────────────────────────────
        // We render a floating status text so the user sees progress
        Text statusLbl = null;
        if (timeLockCard != null)
        {
            statusLbl = MakeText(timeLockCard.transform, "⚙️ Starting...", 0, -490, 24,
                                 new Color(0.5f, 0.8f, 1f), FontStyle.Italic);
            statusLbl.rectTransform.sizeDelta = new Vector2(880, 40);
        }

        System.Action<string, Color> setStatus = (msg, col) => {
            if (statusLbl != null) { statusLbl.text = msg; statusLbl.color = col; }
            ShowStatus(msg, col);
        };

        try
        {
            if (!decimal.TryParse(amtStr, System.Globalization.NumberStyles.Any,
                                  System.Globalization.CultureInfo.InvariantCulture, out decimal amount)
                || amount <= 0)
            {
                setStatus("❌ Invalid amount! Use dot as decimal separator (e.g. 0.001)", ThemeManager.Instance.GetErrorColor());
                return;
            }

            if (!int.TryParse(timeStr, out int timeVal) || timeVal <= 0)
            {
                setStatus("❌ Invalid time/block value!", ThemeManager.Instance.GetErrorColor());
                return;
            }

            var bts  = new BitcoinTransactionService();
            
            // Validate wallet exists
            if (!WalletManager2.Instance.HasWallet())
            {
                setStatus("❌ No wallet found! Please create or restore a wallet first.", ThemeManager.Instance.GetErrorColor());
                return;
            }
            
            string seed    = WalletManager2.Instance.GetCurrentWallet().seedPhrase;
            string privKey = WalletManager2.Instance.GetCurrentWallet().privateKey;
            
            Debug.Log($"[TimeLock] Wallet keys - Seed available: {!string.IsNullOrEmpty(seed)}, PrivKey available: {!string.IsNullOrEmpty(privKey)}");
            if (!string.IsNullOrEmpty(seed))
                Debug.Log($"[TimeLock] Seed: {seed.Substring(0, System.Math.Min(20, seed.Length))}...");
            if (!string.IsNullOrEmpty(privKey))
                Debug.Log($"[TimeLock] PrivKey: {privKey.Substring(0, System.Math.Min(20, privKey.Length))}...");
            
            // Validate we have keys
            if (string.IsNullOrEmpty(seed) && string.IsNullOrEmpty(privKey))
            {
                setStatus("❌ Wallet keys not available! Please check your wallet.", ThemeManager.Instance.GetErrorColor());
                return;
            }

            setStatus("⚙️ Generating timelock script...", new Color(0.5f, 0.5f, 0.8f));

            // Get generated addresses for scanning UTXOs beyond index 5
            var walletData = WalletManager2.Instance.GetCurrentWallet();
            var genAddresses = walletData?.generatedAddresses;

            // ✅ FIX: Parse fee from pending state — use network-aware default if invalid
            decimal timeLockFee = 0.00005m; // Default: 5000 sat (safe for MainNet)
#if TESTNET_BUILD
            timeLockFee = 0.00001m; // TestNet: lower fee OK
#endif
            if (!string.IsNullOrEmpty(pendingTL_Fee) &&
                decimal.TryParse(pendingTL_Fee, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out decimal parsedFee)
                && parsedFee > 0)
            {
                timeLockFee = parsedFee;
            }
            Debug.Log($"[TimeLock] Using fee: {timeLockFee:F8} ({(useTestNet ? "TestNet" : "MainNet")})");

            var result = isAbsoluteLock
                ? await bts.CreateTimeLockTransaction(seed, privKey, useTestNet, addr, amount, timeVal, timeLockFee,
                    msg => { setStatus("⚙️ " + msg, new Color(0.5f, 0.7f, 1f)); Debug.Log("[TimeLock] " + msg); },
                    genAddresses)
                : await bts.CreateRelativeTimeLockTransaction(seed, privKey, useTestNet, addr, amount, timeVal, timeLockFee,
                    msg => { setStatus("⚙️ " + msg, new Color(0.5f, 0.7f, 1f)); Debug.Log("[TimeLock] " + msg); },
                    genAddresses);

            setStatus("✅ " + LocalizationManager.Instance.Get("TL_SUCCESS"), ThemeManager.Instance.GetSuccessColor());

            // Save to history
            SaveTimeLockRecord(new TimeLockRecord {
                date         = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                lockType     = isAbsoluteLock ? "Absolute" : "Relative",
                toAddress    = addr,
                amount       = amtStr,
                lockValue    = timeStr + (isAbsoluteLock ? " min" : " blocks"),
                txId         = result.TxId,
                redeemScript = result.RedeemScriptHex,
                p2shAddress  = result.P2shAddress,
                outputIndex  = result.OutputIndex,
                network      = useTestNet ? "TestNet" : "MainNet"
            });

            ShowTimeLockResultOverlay(result);
        }
        catch (System.Exception ex)
        {
            string errMsg = GetFriendlyTimeLockError(ex.Message);
            Debug.LogError("[TimeLock] " + ex.Message);
            setStatus(errMsg, ThemeManager.Instance.GetErrorColor());
        }
    }

    // Coroutine wrapper to wait for UI to be built before executing
    System.Collections.IEnumerator ExecutePendingTimeLockDelayed()
    {
        yield return null; // Wait one frame for UI to initialize
        ExecutePendingTimeLock();
    }

    void ShowTimeLockResultOverlay(BitcoinTransactionService.TimeLockResult result)
    {
        if (timeLockOverlay != null) Destroy(timeLockOverlay);

        GameObject canvasObj = GameObject.Find("Canvas");
        timeLockOverlay = new GameObject("TimeLockResultOverlay");
        timeLockOverlay.layer = 5;
        timeLockOverlay.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = timeLockOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        timeLockOverlay.AddComponent<Image>().color = new Color(0,0,0, 0.95f);

        GameObject card = MakePanel(timeLockOverlay.transform, "Card", 0, 0, 950, 1400, ThemeManager.Instance.GetCardColor());
        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif
        if (isDesktop)
        {
            card.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
        }

        MakeText(card.transform, LocalizationManager.Instance.Get("TL_RES_TITLE"), 0, 620, 42, ThemeManager.Instance.GetSuccessColor(), FontStyle.Bold);
        MakeText(card.transform, LocalizationManager.Instance.Get("TL_RES_SAVE_INFO"), 0, 550, 28, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);

        // 1. Redeem Script
        MakeText(card.transform, LocalizationManager.Instance.Get("TL_REDEEM_LBL"), -350, 480, 24, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        Text scriptText = MakeText(card.transform, result.RedeemScriptHex, 0, 420, 22, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
        scriptText.rectTransform.sizeDelta = new Vector2(850, 80);
        scriptText.horizontalOverflow = HorizontalWrapMode.Wrap;
        scriptText.verticalOverflow = VerticalWrapMode.Overflow;

        MakeButton(card.transform, LocalizationManager.Instance.Get("TL_BTN_COPY_SCRIPT"), 280, 480, 250, 60, () => {
            GUIUtility.systemCopyBuffer = result.RedeemScriptHex;
            ShowStatus(LocalizationManager.Instance.Get("TL_SCRIPT_COPIED"), ThemeManager.Instance.GetSuccessColor());
        }).GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.3f);

        // 2. Transaction ID
        MakeText(card.transform, LocalizationManager.Instance.Get("TX_HASH_LABEL"), -350, 320, 24, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        Text txidText = MakeText(card.transform, result.TxId, 0, 260, 22, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
        txidText.rectTransform.sizeDelta = new Vector2(850, 60);
        txidText.horizontalOverflow = HorizontalWrapMode.Wrap;
        txidText.verticalOverflow = VerticalWrapMode.Overflow;

        MakeButton(card.transform, LocalizationManager.Instance.Get("TL_BTN_COPY_TXID"), 280, 320, 250, 60, () => {
            GUIUtility.systemCopyBuffer = result.TxId;
            ShowStatus(LocalizationManager.Instance.Get("TL_TXID_COPIED"), ThemeManager.Instance.GetSuccessColor());
        }).GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.3f);

        // 3. Output Index
        MakeText(card.transform, LocalizationManager.Instance.Get("TL_VOUT_LBL"), -300, 160, 24, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        string voutStr = result.OutputIndex.ToString();
        MakeText(card.transform, voutStr, 0, 100, 28, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);

        MakeButton(card.transform, LocalizationManager.Instance.Get("TL_BTN_COPY_VOUT"), 280, 160, 250, 60, () => {
            GUIUtility.systemCopyBuffer = voutStr;
            ShowStatus(LocalizationManager.Instance.Get("TL_VOUT_COPIED"), ThemeManager.Instance.GetSuccessColor());
        }).GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.3f);

        // 4. Locked Amount
        MakeText(card.transform, LocalizationManager.Instance.Get("TL_AMT_LBL"), -320, -20, 24, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
#if TESTNET_BUILD
        string amountStr = $"{result.Amount:F8} BTCT";
#else
        string amountStr = $"{result.Amount:F8} BTC";
#endif
        MakeText(card.transform, amountStr, 0, -80, 28, new Color(1f, 0.8f, 0.2f), FontStyle.Bold);

        MakeButton(card.transform, LocalizationManager.Instance.Get("TL_BTN_COPY_AMOUNT"), 280, -20, 250, 60, () => {
            GUIUtility.systemCopyBuffer = result.Amount.ToString("F8");
            ShowStatus(LocalizationManager.Instance.Get("TL_AMT_COPIED"), ThemeManager.Instance.GetSuccessColor());
        }).GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.3f);

        // 5. P2SH Address (info only)
        MakeText(card.transform, LocalizationManager.Instance.Get("TL_P2SH_ADDR_LBL"), -250, -200, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        Text p2shText = MakeText(card.transform, result.P2shAddress, 0, -260, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        p2shText.rectTransform.sizeDelta = new Vector2(850, 50);
        p2shText.horizontalOverflow = HorizontalWrapMode.Wrap;

        // Copy All button
        MakeButton(card.transform, LocalizationManager.Instance.Get("TL_COPY_ALL"), 0, -380, 700, 90, () => {
#if TESTNET_BUILD
            string cur = "BTCT";
#else
            string cur = "BTC";
#endif
            string allInfo = $"Redeem Script: {result.RedeemScriptHex}\n" +
                             $"TX ID: {result.TxId}\n" +
                             $"Output Index: {result.OutputIndex}\n" +
                             $"Locked Amount: {result.Amount:F8} {cur}\n" +
                             $"P2SH Address: {result.P2shAddress}\n" +
                             $"Recipient Address: {result.ToAddress}\n" +
                             $"Lock Type: {result.LockType}\n" +
                             $"Lock Value: {result.LockValue}";
            GUIUtility.systemCopyBuffer = allInfo;
            ShowStatus(LocalizationManager.Instance.Get("TL_ALL_COPIED"), ThemeManager.Instance.GetSuccessColor());
        }).GetComponent<Image>().color = new Color(0.4f, 0.2f, 0.8f);

        MakeButtonOutline(card.transform, LocalizationManager.Instance.Get("MENU_CLOSE"), 0, -520, 400, 100, () => Destroy(timeLockOverlay));
    }

    // --- MULTISIG VAULT CONTROLLER ---
    private GameObject multisigVaultObj;
    void OpenMultisigVault()
    {
        var existingVault = FindAnyObjectByType<MultisigVaultUI>();
        if (existingVault != null) Destroy(existingVault.gameObject);
        if (multisigVaultObj != null) Destroy(multisigVaultObj);
        multisigVaultObj = new GameObject("MultisigVaultController");
        multisigVaultObj.AddComponent<MultisigVaultUI>();
    }

    // --- REVEAL PUBLIC KEY MODAL ---
    private GameObject pubKeyModalObj;

    void ShowRevealPubKeyModal()
    {
        if (pubKeyModalObj != null) Destroy(pubKeyModalObj);

        GameObject canvasObj = GameObject.Find("Canvas");
        if (canvasObj == null) return;

        bool isNetTest = false;
#if TESTNET_BUILD
        isNetTest = true;
#endif

        var (xpub, zpub, hexPubKey) = MultisigCore.GetMainWalletExtendedPubKey(isNetTest);

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        pubKeyModalObj = new GameObject("RevealPubKeyModal");
        pubKeyModalObj.layer = 5;
        pubKeyModalObj.transform.SetParent(canvasObj.transform, false);

        RectTransform mRT = pubKeyModalObj.AddComponent<RectTransform>();
        mRT.anchorMin = Vector2.zero; mRT.anchorMax = Vector2.one;
        mRT.offsetMin = Vector2.zero; mRT.offsetMax = Vector2.zero;

        Image bg = pubKeyModalObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.88f);

        // Card container
        GameObject card = MakePanel(pubKeyModalObj.transform, "Card", 0, 0, isDesktop ? 850 : 980, isDesktop ? 900 : 1380, ThemeManager.Instance.GetCardColor());
        RectTransform cardRT = card.GetComponent<RectTransform>();
        cardRT.anchoredPosition = Vector2.zero;

        float topY = isDesktop ? 380f : 600f;

        // Title
        MakeText(card.transform, "🔑 Public Key Details", 0, topY, isDesktop ? 32 : 36, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        topY -= isDesktop ? 45f : 60f;

        MakeText(card.transform, "Safe for multisig co-signing or watch-only (Private key stays safe)", 0, topY, isDesktop ? 20 : 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        topY -= isDesktop ? 65f : 85f;

        // QR Code
        GameObject qrObj = new GameObject("QRImage");
        qrObj.transform.SetParent(card.transform, false);
        RectTransform qrRT = qrObj.AddComponent<RectTransform>();
        float qrSize = isDesktop ? 180f : 240f;
        qrRT.anchoredPosition = new Vector2(0, topY - (qrSize / 2f));
        qrRT.sizeDelta = new Vector2(qrSize, qrSize);
        Image qrImg = qrObj.AddComponent<Image>();
        GenerateQRCodeForPubKey(qrImg, string.IsNullOrEmpty(zpub) ? hexPubKey : zpub);
        topY -= (qrSize + (isDesktop ? 35f : 55f));

        // Section 1: Compressed Public Key (Hex - 66 chars)
        MakeText(card.transform, LocalizationManager.Instance.Get("KEY_PUBKEY_COMPRESSED"), 0, topY, isDesktop ? 20 : 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        topY -= isDesktop ? 35f : 45f;

        Text hexTxt = MakeText(card.transform, hexPubKey, 0, topY, isDesktop ? 17 : 19, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
        hexTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
        topY -= isDesktop ? 45f : 60f;

        MakeButton(card.transform, LocalizationManager.Instance.Get("KEY_COPY_HEX"), 0, topY, isDesktop ? 280 : 360, isDesktop ? 55 : 70, () => {
            GUIUtility.systemCopyBuffer = hexPubKey;
            AudioManager.Instance?.PlayCopy();
        });
        topY -= isDesktop ? 75f : 95f;

        // Section 2: Extended Public Key (BIP84 zpub / vpub)
        string extLabel = isNetTest ? "Extended Public Key (BIP84 vpub / tpub):" : "Extended Public Key (BIP84 zpub):";
        MakeText(card.transform, extLabel, 0, topY, isDesktop ? 20 : 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        topY -= isDesktop ? 35f : 45f;

        string displayExt = string.IsNullOrEmpty(zpub) ? xpub : zpub;
        Text zpubTxt = MakeText(card.transform, displayExt, 0, topY, isDesktop ? 16 : 18, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
        zpubTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
        topY -= isDesktop ? 45f : 60f;

        MakeButton(card.transform, LocalizationManager.Instance.Get("KEY_COPY_EXT"), 0, topY, isDesktop ? 280 : 360, isDesktop ? 55 : 70, () => {
            GUIUtility.systemCopyBuffer = displayExt;
            AudioManager.Instance?.PlayCopy();
        });
        topY -= isDesktop ? 85f : 115f;

        // Close Button
        MakeButton(card.transform, LocalizationManager.Instance.Get("GEN_CLOSE"), 0, topY, isDesktop ? 260 : 360, isDesktop ? 60 : 80, () => {
            if (pubKeyModalObj != null) Destroy(pubKeyModalObj);
        }).GetComponent<Image>().color = new Color(0.35f, 0.4f, 0.5f);
    }

    private void GenerateQRCodeForPubKey(Image targetImg, string text)
    {
        if (targetImg == null || string.IsNullOrEmpty(text)) return;
        try
        {
            var writer = new QRCodeWriter();
            var matrix = writer.encode(text, BarcodeFormat.QR_CODE, 256, 256);
            int w = matrix.Width; int h = matrix.Height;
            var tex = new Texture2D(w, h);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = matrix[x, y] ? Color.black : Color.white;
            tex.SetPixels32(pixels);
            tex.Apply();
            targetImg.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[ModernMobileWallet] QR error: " + ex.Message);
        }
    }

    // --- WITHDRAW (REDEEM) TIMELOCK FEATURE ---
    void BuildWithdrawTimeLockOverlay() => BuildWithdrawTimeLockOverlay(null);
    void BuildWithdrawTimeLockOverlay(TimeLockRecord prefill)
    {
        if (timeLockOverlay != null) Destroy(timeLockOverlay);
        if (tlHistoryOverlay != null) Destroy(tlHistoryOverlay);
        GameObject canvasObj = GameObject.Find("Canvas");
        timeLockOverlay = new GameObject("WithdrawTimeLockOverlay");
        timeLockOverlay.layer = 5;
        timeLockOverlay.transform.SetParent(canvasObj.transform, false);
        RectTransform rt = timeLockOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        timeLockOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.95f);

        BuildWithdrawTimeLockCard(true, prefill);
    }

    void BuildWithdrawTimeLockCard(bool isAbsoluteLock) => BuildWithdrawTimeLockCard(isAbsoluteLock, null);
    void BuildWithdrawTimeLockCard(bool isAbsoluteLock, TimeLockRecord prefill)
    {
        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif

        float panelW = isDesktop ? 1600f : 960f;
        float panelH = isDesktop ? 1100f : 1900f;

        if (timeLockCard != null) Destroy(timeLockCard);
        timeLockCard = MakePanel(timeLockOverlay.transform, "WithdrawCard", 0, 0, panelW, panelH, ThemeManager.Instance.GetCardColor());
        
        if (isDesktop)
        {
            timeLockCard.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
        }

        float y   = isDesktop ? 420f : 820f;
        float gap = 20f;

        // ── Title ────────────────────────────────────────────────────────
        MakeText(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_TITLE"), 0, y, 46,
            new Color(0.9f, 0.3f, 0.7f), FontStyle.Bold);
        y -= 48f;

        // ── Subtitle (Network) ───────────────────────────────────────────
        #if TESTNET_BUILD
        MakeText(timeLockCard.transform, "Testnet", 0, y, 22, new Color(0.2f, 0.65f, 1f), FontStyle.Bold);
        #else
        MakeText(timeLockCard.transform, "Mainnet", 0, y, 22, new Color(1f, 0.6f, 0.1f), FontStyle.Bold);
        #endif
        y -= 42f;

        // ── Type Tabs (Absolute vs Relative) ─────────────────────────────
        MakeButton(timeLockCard.transform, "⏰ " + LocalizationManager.Instance.Get("TL_TAB_ABS"), -220, y, 430, 72,
            () => BuildWithdrawTimeLockCard(true))
            .GetComponent<Image>().color = isAbsoluteLock ? new Color(0.75f, 0.15f, 0.55f) : new Color(0.22f, 0.22f, 0.28f);
        MakeButton(timeLockCard.transform, "⛓ " + LocalizationManager.Instance.Get("TL_TAB_REL"), 220, y, 430, 72,
            () => BuildWithdrawTimeLockCard(false))
            .GetComponent<Image>().color = !isAbsoluteLock ? new Color(0.75f, 0.15f, 0.55f) : new Color(0.22f, 0.22f, 0.28f);
        y -= 82f + gap;

        // ── Load Prefilled Record (if any) ───────────────────────────────
        TimeLockRecord lastRec = prefill; // removed auto-fill for new TimeLock withdrawal panel so it's empty as requested

        // ── Inputs Setup ─────────────────────────────────────────────────
#if TESTNET_BUILD
        string defFee = PlayerPrefs.GetString("TLW_FeePref", "0.00001");
#else
        string defFee = PlayerPrefs.GetString("TLW_FeePref", "0.00005");
#endif
        InputField scriptInput, txidInput, amtInput, voutInput, toInput, wifInput, feeInput;

        if (isDesktop)
        {
            float leftX = -380f; float rightX = 380f; float currentY = y;
            scriptInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_SCRIPT"), leftX, currentY, 700, 88);
            scriptInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            if (lastRec != null) scriptInput.text = lastRec.redeemScript ?? "";
            
            txidInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_TXID"), leftX, currentY - 110f, 700, 88);
            txidInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            if (lastRec != null) txidInput.text = lastRec.txId ?? "";

            wifInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_WIF"), leftX, currentY - 220f, 700, 88);
            wifInput.contentType = InputField.ContentType.Password;
            wifInput.GetComponent<Image>().color = new Color(0.14f, 0.12f, 0.06f);

            amtInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_AMT"), rightX - 110f, currentY, 460, 88);
            amtInput.contentType = InputField.ContentType.DecimalNumber;
            amtInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            if (lastRec != null) amtInput.text = lastRec.amount ?? "";

            voutInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_VOUT"), rightX + 240f, currentY, 200, 88);
            voutInput.contentType = InputField.ContentType.IntegerNumber;
            voutInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            if (lastRec != null) voutInput.text = lastRec.outputIndex.ToString();

            toInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_TO"), rightX - 110f, currentY - 110f, 460, 88);
            toInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);

            MakeButton(timeLockCard.transform, LocalizationManager.Instance.Get("TL_BTN_MINE"), rightX + 240f, currentY - 110f, 200, 88, () => {
                if (!WalletManager2.Instance.HasWallet()) return;
#if TESTNET_BUILD
                WalletManager2.Instance.EnsureTestnetAddress();
                string tAddr = WalletManager2.Instance.GetTestnetAddress();
                if (!string.IsNullOrEmpty(tAddr) && tAddr.StartsWith("tb1")) toInput.text = tAddr;
#else
                toInput.text = WalletManager2.Instance.GetCurrentWallet()?.address ?? "";
#endif
            }).GetComponent<Image>().color = new Color(0.18f, 0.42f, 0.72f);

            feeInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_FEE"), rightX, currentY - 220f, 700, 88);
            feeInput.contentType = InputField.ContentType.DecimalNumber;
            feeInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            feeInput.text = defFee;
            feeInput.onValueChanged.AddListener((val) => { PlayerPrefs.SetString("TLW_FeePref", val); PlayerPrefs.Save(); });
            y = currentY - 330f;
        }
        else
        {
            scriptInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_SCRIPT"), 0, y, 880, 88);
            scriptInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            if (lastRec != null) scriptInput.text = lastRec.redeemScript ?? "";
            y -= 88f + gap;

            txidInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_TXID"), 0, y, 880, 88);
            txidInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            if (lastRec != null) txidInput.text = lastRec.txId ?? "";
            y -= 88f + gap;

            amtInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_AMT"), -120, y, 620, 88);
            amtInput.contentType = InputField.ContentType.DecimalNumber;
            amtInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            if (lastRec != null) amtInput.text = lastRec.amount ?? "";

            voutInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_VOUT"), 320, y, 220, 88);
            voutInput.contentType = InputField.ContentType.IntegerNumber;
            voutInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            if (lastRec != null) voutInput.text = lastRec.outputIndex.ToString();
            y -= 88f + gap;

            toInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_TO"), -120, y, 620, 88);
            toInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);

            MakeButton(timeLockCard.transform, LocalizationManager.Instance.Get("TL_BTN_MINE"), 320, y, 220, 88, () => {
                if (!WalletManager2.Instance.HasWallet()) return;
#if TESTNET_BUILD
                WalletManager2.Instance.EnsureTestnetAddress();
                string tAddr = WalletManager2.Instance.GetTestnetAddress();
                if (!string.IsNullOrEmpty(tAddr) && tAddr.StartsWith("tb1")) toInput.text = tAddr;
#else
                toInput.text = WalletManager2.Instance.GetCurrentWallet()?.address ?? "";
#endif
            }).GetComponent<Image>().color = new Color(0.18f, 0.42f, 0.72f);
            y -= 88f + gap;

            wifInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_WIF"), 0, y, 880, 88);
            wifInput.contentType = InputField.ContentType.Password;
            wifInput.GetComponent<Image>().color = new Color(0.14f, 0.12f, 0.06f);
            y -= 88f + gap;

            feeInput = MakeInput(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_PH_FEE"), 0, y, 880, 88);
            feeInput.contentType = InputField.ContentType.DecimalNumber;
            feeInput.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            feeInput.text = defFee;
            feeInput.onValueChanged.AddListener((val) => { PlayerPrefs.SetString("TLW_FeePref", val); PlayerPrefs.Save(); });
            y -= 88f + gap;
        }

        // ── Error Label ──────────────────────────────────────────────────
        Text errLabel = MakeText(timeLockCard.transform, "", 0, y, 24, new Color(1f, 0.3f, 0.3f), FontStyle.Bold);
        errLabel.rectTransform.sizeDelta = new Vector2(880, 50);
        y -= 50f;

        // ── Redeem Button (REDEEM FUNDS) ──────────────────────────────────
        MakeButton(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_BTN_REDEEM"), 0, y, isDesktop ? 1460 : 880, 110, () => {
            errLabel.text = "";
            if (string.IsNullOrWhiteSpace(scriptInput.text)) { errLabel.text = LocalizationManager.Instance.Get("TLW_ERR_NO_SCRIPT"); return; }
            if (string.IsNullOrWhiteSpace(txidInput.text))   { errLabel.text = LocalizationManager.Instance.Get("TLW_ERR_NO_TXID"); return; }
            if (string.IsNullOrWhiteSpace(amtInput.text))    { errLabel.text = LocalizationManager.Instance.Get("TL_ERR_NO_AMT"); return; }
            if (string.IsNullOrWhiteSpace(toInput.text))     { errLabel.text = LocalizationManager.Instance.Get("TL_ERR_NO_ADDR"); return; }
            string vout   = string.IsNullOrWhiteSpace(voutInput.text) ? "0" : voutInput.text.Trim();
            string fee    = string.IsNullOrWhiteSpace(feeInput.text)  ? defFee : feeInput.text.Trim();
            string manKey = wifInput != null ? wifInput.text.Trim() : "";
            ExecuteWithdrawTimeLock(
                scriptInput.text.Trim(), txidInput.text.Trim(), vout,
                amtInput.text.Trim(), toInput.text.Trim(), fee, isAbsoluteLock, manKey);
        }).GetComponent<Image>().color = new Color(0.75f, 0.15f, 0.55f);
        y -= 110f + gap;

        // ── Footer ───────────────────────────────────────────────────────
        MakeText(timeLockCard.transform, LocalizationManager.Instance.Get("TLW_TIP_HISTORY"), 0, y, 20,
            ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Italic).rectTransform.sizeDelta = new Vector2(isDesktop ? 1460 : 880, 36);
        y -= 44f + gap;

        MakeButton(timeLockCard.transform, LocalizationManager.Instance.Get("TL_BTN_HISTORY"), -220, y, 420, 88,
            () => BuildTimeLockHistoryOverlay())
            .GetComponent<Image>().color = new Color(0.18f, 0.28f, 0.48f);
        MakeButtonOutline(timeLockCard.transform, LocalizationManager.Instance.Get("MENU_CLOSE"),
            220, y, 420, 88, () => Destroy(timeLockOverlay));
    }

    // Pending Withdraw TimeLock state for PIN verification
    private static string pendingTLW_Script;
    private static string pendingTLW_Txid;
    private static string pendingTLW_Vout;
    private static string pendingTLW_Amt;
    private static string pendingTLW_ToAddr;
    private static string pendingTLW_Fee;
    private static bool   pendingTLW_IsAbsolute;
    private static string pendingTLW_ManualKey; // ✅ Optional manual WIF key

    void ExecuteWithdrawTimeLock(
        string scriptHex, string txid, string voutStr,
        string amtStr, string toAddr, string feeStr, bool isAbsoluteLock,
        string manualWifKey = "")
    {
        if (string.IsNullOrEmpty(scriptHex) || string.IsNullOrEmpty(txid) ||
            string.IsNullOrEmpty(amtStr) || string.IsNullOrEmpty(toAddr) ||
            string.IsNullOrEmpty(voutStr) || string.IsNullOrEmpty(feeStr))
        {
            ShowStatus(LocalizationManager.Instance.Get("TL_ERR_FILL"), ThemeManager.Instance.GetErrorColor());
            return;
        }

        pendingTLW_Script     = scriptHex;
        pendingTLW_Txid       = txid;
        pendingTLW_Vout       = voutStr;
        pendingTLW_Amt        = amtStr;
        pendingTLW_ToAddr     = toAddr;
        pendingTLW_Fee        = feeStr;
        pendingTLW_IsAbsolute = isAbsoluteLock;
        pendingTLW_ManualKey  = manualWifKey; // ✅ store manual key
        
        // Save to PlayerPrefs so we survive the PIN scene transition
        PlayerPrefs.SetString("TLW_Script", scriptHex);
        PlayerPrefs.SetString("TLW_Txid", txid);
        PlayerPrefs.SetString("TLW_Vout", voutStr);
        PlayerPrefs.SetString("TLW_Amt", amtStr);
        PlayerPrefs.SetString("TLW_ToAddr", toAddr);
        PlayerPrefs.SetString("TLW_Fee", feeStr);
        PlayerPrefs.SetInt("TLW_IsAbsolute", isAbsoluteLock ? 1 : 0);
        PlayerPrefs.SetString("TLW_ManualKey", manualWifKey);
        
        PlayerPrefs.SetInt("PendingTxPIN", 1);
        PlayerPrefs.SetString("PendingTLW", "1");
        PlayerPrefs.Save();
        SceneManager.LoadScene("AppLock");
    }

    async void ExecutePendingWithdrawTimeLock()
    {
        // Clear pending flags
        PlayerPrefs.DeleteKey("PendingTLW");
        PlayerPrefs.Save();
        
        // Restore from PlayerPrefs if static variables were cleared (e.g., scene reload)
        string scriptHex = pendingTLW_Script ?? PlayerPrefs.GetString("TLW_Script", "");
        string txid = pendingTLW_Txid ?? PlayerPrefs.GetString("TLW_Txid", "");
        string voutStr = pendingTLW_Vout ?? PlayerPrefs.GetString("TLW_Vout", "");
        string amtStr = pendingTLW_Amt ?? PlayerPrefs.GetString("TLW_Amt", "");
        string toAddr = pendingTLW_ToAddr ?? PlayerPrefs.GetString("TLW_ToAddr", "");
        string feeStr = pendingTLW_Fee ?? PlayerPrefs.GetString("TLW_Fee", "");
        bool isAbsoluteLock = pendingTLW_Script != null ? pendingTLW_IsAbsolute : (PlayerPrefs.GetInt("TLW_IsAbsolute", 0) == 1);
        pendingTLW_ManualKey = pendingTLW_ManualKey ?? PlayerPrefs.GetString("TLW_ManualKey", "");

        // Clean up PlayerPrefs
        PlayerPrefs.DeleteKey("TLW_Script");
        PlayerPrefs.DeleteKey("TLW_Txid");
        PlayerPrefs.DeleteKey("TLW_Vout");
        PlayerPrefs.DeleteKey("TLW_Amt");
        PlayerPrefs.DeleteKey("TLW_ToAddr");
        PlayerPrefs.DeleteKey("TLW_Fee");
        PlayerPrefs.DeleteKey("TLW_IsAbsolute");
        PlayerPrefs.DeleteKey("TLW_ManualKey");
        PlayerPrefs.Save();

        try
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int vout = int.Parse(voutStr);
            decimal amount = decimal.Parse(amtStr, inv);
            decimal fee = decimal.Parse(feeStr, inv);

            ShowStatus(LocalizationManager.Instance.Get("TLW_BROADCASTING"), ThemeManager.Instance.GetAccentColor());

            // Validate wallet exists
            if (!WalletManager2.Instance.HasWallet())
            {
                ShowStatus(LocalizationManager.Instance.Get("TL_ERR_NO_WALLET"), ThemeManager.Instance.GetErrorColor());
                return;
            }
            
            WalletData w = WalletManager2.Instance.GetCurrentWallet();
            string seed = w?.seedPhrase;
            string privKey = w?.privateKey;
            
            // Validate we have keys
            if (string.IsNullOrEmpty(seed) && string.IsNullOrEmpty(privKey))
            {
                ShowStatus(LocalizationManager.Instance.Get("TL_ERR_NO_KEYS"), ThemeManager.Instance.GetErrorColor());
                return;
            }
            
#if TESTNET_BUILD
            bool useTestNet = true;
#else
            bool useTestNet = false;
#endif

            BitcoinTransactionService bts = new BitcoinTransactionService();

            // ✅ If manual WIF key provided, use it as privateKey; otherwise use wallet's key
            string effectiveSeed   = string.IsNullOrEmpty(pendingTLW_ManualKey) ? seed : null;
            string effectivePrivKey = !string.IsNullOrEmpty(pendingTLW_ManualKey)
                ? pendingTLW_ManualKey
                : privKey;

            if (!string.IsNullOrEmpty(pendingTLW_ManualKey))
                Debug.Log("[Redeem] Using manually provided WIF key");

            string txId = await bts.RedeemTimeLockTransaction(
                effectiveSeed, effectivePrivKey, useTestNet,
                scriptHex.Trim(), txid.Trim(), vout,
                amount, toAddr.Trim(), fee, isAbsoluteLock,
                msg => Debug.Log("[Redeem] " + msg));

            ShowStatus(LocalizationManager.Instance.Get("TLW_SUCCESS") + " " + txId, ThemeManager.Instance.GetSuccessColor());
            Destroy(timeLockOverlay);
        }
        catch (System.Exception ex)
        {
            string errMsg = GetFriendlyTimeLockError(ex.Message);
            Debug.LogError("[Redeem] " + ex.Message);
            ShowStatus(errMsg, ThemeManager.Instance.GetErrorColor());
        }
    }

    string GetFriendlyTimeLockError(string rawError)
    {
        bool isPersian = LocalizationManager.Instance.GetCurrentLanguage() == "Persian";
        
        if (rawError.Contains("non-final") || rawError.Contains("not final"))
        {
            return isPersian 
                ? "❌ دارایی‌ها هنوز قفل هستند. زمان قفل آنها هنوز به پایان نرسیده است." 
                : "❌ Funds are still locked. The lock time has not expired yet.";
        }
        if (rawError.Contains("non-BIP68-sequence") || rawError.Contains("sequence") || rawError.Contains("CSV"))
        {
            return isPersian
                ? "❌ قفل بلاک‌های نسبی هنوز منقضی نشده است. لطفا منتظر بمانید تا تعداد بیشتری بلاک استخراج شود."
                : "❌ Relative block lock sequence not met yet. Please wait for more blocks to be mined.";
        }
        if (rawError.Contains("bad-txns-inputs-spent") || rawError.Contains("already spent") || rawError.Contains("Missing input") || rawError.Contains("spent"))
        {
            return isPersian
                ? "❌ این تراکنش قفل‌شده قبلاً برداشت یا خرج شده است."
                : "❌ This locked transaction has already been withdrawn or spent.";
        }
        if (rawError.Contains("Insufficient funds") || rawError.Contains("Need"))
        {
            return isPersian
                ? "❌ موجودی کافی برای پرداخت این تراکنش و کارمزد شبکه وجود ندارد."
                : "❌ Insufficient balance to cover the transaction amount and fee.";
        }
        if (rawError.Contains("Could not derive a private key") || rawError.Contains("signing failed"))
        {
            return isPersian
                ? "❌ کیف پول یا کلید خصوصی وارد شده نامعتبر است. امضای تراکنش ناموفق بود."
                : "❌ Incorrect wallet or private key. Transaction signing failed.";
        }
        if (rawError.Contains("Invalid Bitcoin address"))
        {
            return isPersian
                ? "❌ آدرس وارد شده برای این شبکه معتبر نیست. آدرس مقصد را بررسی کنید."
                : "❌ Invalid address for this network. Please check the recipient address.";
        }
        if (rawError.Contains("No Unspent Outputs"))
        {
            return isPersian
                ? "❌ هیچ تراکنش تأیید شده‌ای یافت نشد. لطفاً منتظر تأیید تراکنش‌های خود بمانید."
                : "❌ No confirmed outputs found. Please wait for your transaction to confirm.";
        }
        
        return isPersian ? "❌ خطا: " + rawError : "❌ Error: " + rawError;
    }

    // Coroutine wrapper to wait for UI to be built before executing
    System.Collections.IEnumerator ExecutePendingWithdrawTimeLockDelayed()
    {
        yield return null; // Wait one frame for UI to initialize
        ExecutePendingWithdrawTimeLock();
    }

    // --- TIMELOCK HISTORY OVERLAY ---
    private GameObject tlHistoryOverlay;

    void BuildTimeLockHistoryOverlay()
    {
        if (tlHistoryOverlay != null) Destroy(tlHistoryOverlay);

        GameObject canvasObj = GameObject.Find("Canvas");
        tlHistoryOverlay = new GameObject("TLHistoryOverlay");
        tlHistoryOverlay.layer = 5;
        tlHistoryOverlay.transform.SetParent(canvasObj.transform, false);

        RectTransform bgRt = tlHistoryOverlay.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
        tlHistoryOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.96f);
        tlHistoryOverlay.transform.SetAsLastSibling();

        // Let's create a container Panel!
        GameObject container = new GameObject("HistoryContainer");
        container.layer = 5;
        container.transform.SetParent(tlHistoryOverlay.transform, false);
        RectTransform ctrRT = container.AddComponent<RectTransform>();
        ctrRT.anchorMin = new Vector2(0.5f, 0.5f);
        ctrRT.anchorMax = new Vector2(0.5f, 0.5f);
        ctrRT.anchoredPosition = Vector2.zero;
        ctrRT.sizeDelta = new Vector2(1080, 1920); // standard mobile aspect canvas size!
        
        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif
        if (isDesktop)
        {
            container.transform.localScale = new Vector3(0.52f, 0.52f, 1f);
        }

        // Header
        MakeText(container.transform, LocalizationManager.Instance.Get("TL_HISTORY_TITLE"), 0, 850, 44, new Color(0.6f, 0.2f, 0.9f), FontStyle.Bold);
        MakeButtonOutline(container.transform, "✕ " + LocalizationManager.Instance.Get("GEN_CLOSE"), 0, -780, 400, 90, () => Destroy(tlHistoryOverlay));

        var records = LoadTimeLockRecords();

        if (records.Count == 0)
        {
            MakeText(container.transform, LocalizationManager.Instance.Get("TL_NO_RECORDS"), 0, 0, 34, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Italic);
            return;
        }

        // Scroll View Setup (Anchored below header, above close button)
        GameObject scrollView = new GameObject("ScrollView");
        scrollView.layer = 5;
        scrollView.transform.SetParent(container.transform, false);
        
        Image scrollImg = scrollView.AddComponent<Image>();
        scrollImg.color = new Color(0, 0, 0, 0); 

        RectTransform scrollRT = scrollView.GetComponent<RectTransform>();
        scrollRT.anchorMin = Vector2.zero; // Bottom Left
        scrollRT.anchorMax = Vector2.one;  // Top Right
        scrollRT.offsetMin = new Vector2(65, 280); // Bottom margin (above close button)
        scrollRT.offsetMax = new Vector2(-65, -180); // Top margin (below Title)
        
        ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();
        scrollRect.horizontal = false; scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 40f;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        
        GameObject viewport = new GameObject("Viewport");
        viewport.layer = 5; viewport.transform.SetParent(scrollView.transform, false);
        RectTransform viewportRT = viewport.AddComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero; viewportRT.anchorMax = Vector2.one; 
        viewportRT.sizeDelta = Vector2.zero;
        viewport.AddComponent<RectMask2D>();
        viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0);

        GameObject content = new GameObject("Content");
        content.layer = 5; content.transform.SetParent(viewport.transform, false);
        RectTransform contentRT = content.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1); contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1); contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(950, 0);

        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.padding = new RectOffset(0, 0, 20, 20);
        vlg.spacing = 15; // smaller gap
        vlg.childControlWidth = false; 
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize; 
        
        scrollRect.content = contentRT;
        scrollRect.viewport = viewportRT;

        float cardW = 900f;
        float cardH = 150f;

        for (int i = 0; i < records.Count; i++)
        {
            var rec = records[i];

            GameObject card = MakePanel(content.transform, "TLCard_" + i, 0, 0, cardW, cardH,
                new Color(0.12f, 0.10f, 0.20f, 1f));
            LayoutElement le = card.AddComponent<LayoutElement>();
            le.minHeight = cardH;
            le.preferredHeight = cardH;

            // Left color bar by lock type
            GameObject bar = new GameObject("Bar"); bar.layer = 5;
            bar.transform.SetParent(card.transform, false);
            RectTransform barRt = bar.AddComponent<RectTransform>();
            barRt.anchoredPosition = new Vector2(-cardW / 2 + 4, 0);
            barRt.sizeDelta = new Vector2(8, cardH);
            bar.AddComponent<Image>().color = rec.lockType == "Absolute"
                ? new Color(0.6f, 0.2f, 0.9f) : new Color(0.2f, 0.6f, 0.9f);

            // Row 1 — date + lock type badge (smaller font, y = 40)
            string badge = rec.lockType == "Absolute" ? "⏰ Absolute" : "⛓ Relative";
            Color badgeCol = rec.lockType == "Absolute" ? new Color(0.7f, 0.3f, 1f) : new Color(0.3f, 0.7f, 1f);
            MakeText(card.transform, rec.date ?? "—", -280, 40, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            MakeText(card.transform, badge, 280, 40, 20, badgeCol, FontStyle.Bold).alignment = TextAnchor.MiddleRight;

            // Row 2 — amount + network (y = 0)
            string cur = rec.network == "TestNet" ? "BTCT" : "BTC";
            MakeText(card.transform, $"{rec.amount} {cur}", -180, 0, 26, new Color(1f, 0.85f, 0.2f), FontStyle.Bold)
                .alignment = TextAnchor.MiddleLeft;
            Color netCol = rec.network == "TestNet" ? new Color(0.2f, 0.6f, 1f) : new Color(1f, 0.6f, 0.1f);
            MakeText(card.transform, rec.network ?? "—", 280, 0, 20, netCol, FontStyle.Bold).alignment = TextAnchor.MiddleRight;

            // Row 3 — lock value + TXID truncated (y = -40)
            string txShort = (!string.IsNullOrEmpty(rec.txId) && rec.txId.Length > 16)
                ? rec.txId.Substring(0, 8) + "…" + rec.txId.Substring(rec.txId.Length - 6)
                : (rec.txId ?? "—");
            MakeText(card.transform, "🔒 " + rec.lockValue, -230, -40, 20, ThemeManager.Instance.GetTextColor(), FontStyle.Normal)
                .alignment = TextAnchor.MiddleLeft;
            MakeText(card.transform, "TX: " + txShort, 100, -40, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal)
                .alignment = TextAnchor.MiddleLeft;

            // Tap whole card to see detail
            var capturedRec = rec;
            Button cardBtn = card.AddComponent<Button>();
            cardBtn.transition = Selectable.Transition.None;
            cardBtn.onClick.AddListener(() => { AudioManager.Instance.PlayClick(); BuildTimeLockDetailOverlay(capturedRec); });
        }
    }

    void BuildTimeLockDetailOverlay(TimeLockRecord rec)
    {
        GameObject canvasObj = GameObject.Find("Canvas");
        GameObject detailOverlay = new GameObject("TLDetailOverlay");
        detailOverlay.layer = 5;
        detailOverlay.transform.SetParent(canvasObj.transform, false);

        RectTransform bgRt = detailOverlay.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
        detailOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.97f);
        detailOverlay.transform.SetAsLastSibling();

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        float panelW = isDesktop ? 1500f : 950f;
        float panelH = isDesktop ? 900f : 1500f;

        GameObject card = MakePanel(detailOverlay.transform, "DetailCard", 0, 20, panelW, panelH, ThemeManager.Instance.GetCardColor());
        
        if (isDesktop)
        {
            card.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
        }

        string cur = rec.network == "TestNet" ? "BTCT" : "BTC";
        Color typeCol = rec.lockType == "Absolute" ? new Color(0.7f, 0.3f, 1f) : new Color(0.3f, 0.7f, 1f);

        float y = isDesktop ? 350f : 670f;
        MakeText(card.transform, LocalizationManager.Instance.Get("TL_DETAIL_TITLE"), 0, y, 40, new Color(0.6f, 0.2f, 0.9f), FontStyle.Bold);

        y -= 80;
        MakeText(card.transform, $"{rec.date ?? "—"}  •  {rec.lockType} Lock  •  {rec.network}", 0, y, 22,
            ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);

        y -= 80;

        void Row(string label, string value, Color valColor, float rowX, float rowY, float width, System.Action onCopy = null)
        {
            Text lbl = MakeText(card.transform, label, rowX, rowY, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            lbl.alignment = TextAnchor.MiddleLeft;
            lbl.rectTransform.sizeDelta = new Vector2(width, 50);

            Text valTxt = MakeText(card.transform, value, onCopy != null ? rowX - 30 : rowX, rowY - 35, 26, valColor, FontStyle.Bold);
            valTxt.rectTransform.sizeDelta = new Vector2(onCopy != null ? width - 60 : width, 100);
            valTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
            valTxt.verticalOverflow   = VerticalWrapMode.Overflow;
            valTxt.alignment = TextAnchor.MiddleLeft;
            if (onCopy != null)
            {
                MakeButton(card.transform, "📋", rowX + (width / 2f) - 30f, rowY - 35, 80, 60, () => { onCopy(); AudioManager.Instance.PlayCopy(); })
                    .GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.6f);
            }
        }

        if (isDesktop)
        {
            float leftX = -320f;
            float rightX = 380f;
            float curY = y;
            
            // Left Column
            Row("Amount:", $"{rec.amount} {cur}", new Color(1f, 0.85f, 0.2f), leftX, curY, 700);
            curY -= 100;
            Row("Lock:", rec.lockValue ?? "—", typeCol, leftX, curY, 700);
            curY -= 100;
            Row("vout:", rec.outputIndex.ToString(), ThemeManager.Instance.GetTextColor(), leftX, curY, 700);
            curY -= 100;
            Row("To Address:", rec.toAddress ?? "—", ThemeManager.Instance.GetAccentColor(), leftX, curY, 700,
                () => { GUIUtility.systemCopyBuffer = rec.toAddress; ShowStatus(LocalizationManager.Instance.Get("GEN_COPIED_TICK"), ThemeManager.Instance.GetSuccessColor()); });
            curY -= 120;
            Row("P2SH Address:", rec.p2shAddress ?? "—", new Color(0.7f, 0.9f, 1f), leftX, curY, 700,
                () => { GUIUtility.systemCopyBuffer = rec.p2shAddress; ShowStatus(LocalizationManager.Instance.Get("TL_P2SH_COPIED"), ThemeManager.Instance.GetSuccessColor()); });

            // Right Column
            curY = y;
            Row("TX ID:", rec.txId ?? "—", new Color(0.5f, 0.9f, 0.5f), rightX, curY, 700,
                () => { GUIUtility.systemCopyBuffer = rec.txId; ShowStatus(LocalizationManager.Instance.Get("TL_TXID_COPIED"), ThemeManager.Instance.GetSuccessColor()); });
            curY -= 140;
            Row("Redeem Script:", rec.redeemScript ?? "—", new Color(0.8f, 0.6f, 1f), rightX, curY, 700,
                () => { GUIUtility.systemCopyBuffer = rec.redeemScript; ShowStatus(LocalizationManager.Instance.Get("TL_SCRIPT_COPIED"), ThemeManager.Instance.GetSuccessColor()); });
            
            y = -350f;
        }
        else
        {
            Row("Amount:", $"{rec.amount} {cur}", new Color(1f, 0.85f, 0.2f), 0, y, 840);
            y -= 90;  Row("Lock:", rec.lockValue ?? "—", typeCol, 0, y, 840);
            y -= 100; Row("To Address:", rec.toAddress ?? "—", ThemeManager.Instance.GetAccentColor(), 0, y, 840,
                          () => { GUIUtility.systemCopyBuffer = rec.toAddress; ShowStatus(LocalizationManager.Instance.Get("GEN_COPIED_TICK"), ThemeManager.Instance.GetSuccessColor()); });
            y -= 120; Row("P2SH Address:", rec.p2shAddress ?? "—", new Color(0.7f, 0.9f, 1f), 0, y, 840,
                          () => { GUIUtility.systemCopyBuffer = rec.p2shAddress; ShowStatus(LocalizationManager.Instance.Get("TL_P2SH_COPIED"), ThemeManager.Instance.GetSuccessColor()); });
            y -= 120; Row("TX ID:", rec.txId ?? "—", new Color(0.5f, 0.9f, 0.5f), 0, y, 840,
                          () => { GUIUtility.systemCopyBuffer = rec.txId; ShowStatus(LocalizationManager.Instance.Get("TL_TXID_COPIED"), ThemeManager.Instance.GetSuccessColor()); });
            y -= 120; Row("vout:", rec.outputIndex.ToString(), ThemeManager.Instance.GetTextColor(), 0, y, 840);
            y -= 100; Row("Redeem Script:", rec.redeemScript ?? "—", new Color(0.8f, 0.6f, 1f), 0, y, 840,
                          () => { GUIUtility.systemCopyBuffer = rec.redeemScript; ShowStatus(LocalizationManager.Instance.Get("TL_SCRIPT_COPIED"), ThemeManager.Instance.GetSuccessColor()); });
            y -= 130;
        }

        System.Action copyAllAction = () => {
            string all = $"Date: {rec.date}\nType: {rec.lockType} Lock\nNetwork: {rec.network}\n" +
                         $"Amount: {rec.amount} {cur}\nLock: {rec.lockValue}\n" +
                         $"To: {rec.toAddress}\nP2SH: {rec.p2shAddress}\n" +
                         $"TX ID: {rec.txId}\nvout: {rec.outputIndex}\n" +
                         $"Redeem Script: {rec.redeemScript}";
            GUIUtility.systemCopyBuffer = all;
            ShowStatus(LocalizationManager.Instance.Get("TL_ALL_COPIED"), ThemeManager.Instance.GetSuccessColor());
        };

        System.Action checkTxAction = () => {
            if (!string.IsNullOrEmpty(rec.txId) && rec.txId != "—")
            {
                string txUrl = rec.network == "TestNet"
                    ? $"https://mempool.space/testnet/tx/{rec.txId}"
                    : $"https://mempool.space/tx/{rec.txId}";
                Application.OpenURL(txUrl);
            }
            else
            {
                ShowStatus(LocalizationManager.Instance.Get("TL_NO_TXID"), ThemeManager.Instance.GetErrorColor());
            }
        };

        System.Action redeemAction = () => {
            Destroy(detailOverlay);
            BuildWithdrawTimeLockOverlay(rec);
        };

        if (isDesktop)
        {
            float btnW = 340f;
            MakeButton(card.transform, LocalizationManager.Instance.Get("TL_COPY_ALL"), -540f, y, btnW, 80, copyAllAction)
                .GetComponent<Image>().color = new Color(0.4f, 0.2f, 0.8f);
            
            GameObject checkTxBtn = MakeButton(card.transform, LocalizationManager.Instance.Get("TL_BTN_CHECK_TX"), -180f, y, btnW, 80, checkTxAction);
            checkTxBtn.GetComponent<Image>().color = new Color(0.1f, 0.5f, 0.8f);
            
            MakeButton(card.transform, LocalizationManager.Instance.Get("TL_BTN_REDEEM_LOCK"), 180f, y, btnW, 80, redeemAction)
                .GetComponent<Image>().color = new Color(0.75f, 0.15f, 0.55f);
            
            MakeButtonOutline(card.transform, LocalizationManager.Instance.Get("GEN_BACK"), 540f, y, 200f, 80, () => Destroy(detailOverlay));
        }
        else
        {
            MakeButton(card.transform, LocalizationManager.Instance.Get("TL_COPY_ALL"), 0, y, 700, 90, copyAllAction)
                .GetComponent<Image>().color = new Color(0.4f, 0.2f, 0.8f);
            y -= 110;
            
            GameObject checkTxBtn = MakeButton(card.transform, LocalizationManager.Instance.Get("TL_BTN_CHECK_TX"), 0, y, 700, 90, checkTxAction);
            checkTxBtn.GetComponent<Image>().color = new Color(0.1f, 0.5f, 0.8f);
            y -= 110;
            
            MakeButton(card.transform, LocalizationManager.Instance.Get("TL_BTN_REDEEM_LOCK"), 0, y, 700, 100, redeemAction)
                .GetComponent<Image>().color = new Color(0.75f, 0.15f, 0.55f);
            y -= 110;
            
            MakeButtonOutline(card.transform, LocalizationManager.Instance.Get("GEN_BACK"), 0, y, 400, 90, () => Destroy(detailOverlay));
        }
    }

    // Duplicate OnDestroy removed (handled at line 133)
    /*
        if (keysOverlay != null)
        {
            ScreenSecurityManager.DisableProtection();
        }
    */

    public class CoinGeckoResponse { public BitcoinPrice bitcoin; }
    [System.Serializable]
    public class BitcoinPrice { public float usd; }
    [System.Serializable]
    public class PaprikaTicker { public PaprikaQuotes quotes; }
    [System.Serializable]
    public class PaprikaQuotes { public PaprikaUSD USD; }
    [System.Serializable]
    public class PaprikaUSD { public float price; }

    // ==========================================
    // BRAND LOGO LOADER (ROBUST MULTI-FALLBACK)
    // ==========================================
    public static Sprite GetBrandLogoSprite()
    {
        Sprite s = Resources.Load<Sprite>("satolite_logo_bw")
                ?? Resources.Load<Sprite>("satolite_logo");
        if (s != null) return s;

        Sprite[] subSprites = Resources.LoadAll<Sprite>("satolite_logo_bw");
        if (subSprites != null && subSprites.Length > 0 && subSprites[0] != null) return subSprites[0];

        Texture2D tex = Resources.Load<Texture2D>("satolite_logo_bw")
                     ?? Resources.Load<Texture2D>("satolite_logo");
        if (tex != null)
        {
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        try
        {
            string[] paths = new string[]
            {
                System.IO.Path.Combine(Application.dataPath, "Resources", "satolite_logo_bw.png"),
                System.IO.Path.Combine(Application.dataPath, "Resources", "satolite_logo.png")
            };
            foreach (string p in paths)
            {
                if (System.IO.File.Exists(p))
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(p);
                    Texture2D diskTex = new Texture2D(2, 2);
                    if (diskTex.LoadImage(bytes))
                    {
                        return Sprite.Create(diskTex, new Rect(0, 0, diskTex.width, diskTex.height), new Vector2(0.5f, 0.5f));
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[BrandLogo] Disk fallback error: " + ex.Message);
        }

        return null;
    }
}