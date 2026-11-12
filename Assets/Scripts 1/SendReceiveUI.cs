using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using ZXing;
using ZXing.QrCode;
using ZXing.Common;
using NBitcoin;
using UnityEngine.Networking;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class SendReceiveUI : MonoBehaviour
{
    private InputField sendAddressInput;
    private InputField sendAmountInput;
    private InputField feeInput;
    private Text statusText;
    private Text balanceText;
    private Text usdValueText;
    private Text networkText;
    private GameObject receivePanel;
    private GameObject scanPanel;
    private RawImage cameraDisplay;
    private GameObject addressBookContainer;
    
    // UI Layout Configuration
    private const float REFERENCE_WIDTH = 1080f;
    private const float REFERENCE_HEIGHT = 1920f;
    
    private WebCamTexture webCamTexture;
    private bool isScanPanelVisible = false;
    private BitcoinTransactionService transactionService;
    private CancellationTokenSource cts;
    
    // Transaction loading panel
    private GameObject loadingPanel;
    private Text loadingText;
    private Button cancelButton;
    private bool transactionCancelled = false;
    
    private const string COINGECKO_API = "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd";
    private const string COINPAPRIKA_API = "https://api.coinpaprika.com/v1/tickers/btc-bitcoin";
    
    private double lastBtcPrice = 0;
    private GameObject addressBookOverlay;
    
    // ✅ Auto-refresh timer
    private const float AUTO_REFRESH_INTERVAL = 180f; // 3 minutes
    private Text balanceUsdText;
    
    // PIN verification for transaction
    private static bool pendingPINVerification = false;
    private static string pendingAddress = "";
    private static decimal pendingAmount = 0;
    private static decimal pendingFee = 0;

    void Start()
    {
        try {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<StandaloneInputModule>();
            }
            BuildUI();
        }
        catch (System.Exception ex) { Debug.LogError($"SendReceiveUI: UI Build Failed: {ex.Message}"); }

        try {
            transactionService = new BitcoinTransactionService();

            // ✅ Step 1: Show the last known balance instantly (no API delay)
            ShowCachedBalance();

            // ✅ Step 2: Ask the dashboard to do a refresh — avoids duplicate API calls
            // If dashboard is active, it fires OnBalanceChanged which updates this screen too
            // If dashboard is NOT loaded (standalone scene), do our own refresh as fallback
            if (ModernMobileWallet.Instance != null)
            {
                Debug.Log("[SendReceiveUI] Dashboard active — delegating refresh to ModernMobileWallet");
                ModernMobileWallet.Instance.ForceRefreshBalance();
            }
            else
            {
                Debug.Log("[SendReceiveUI] Dashboard not active — doing own refresh");
                RefreshWalletStatus();
            }

            // ✅ Step 3: Auto-refresh every 3 min — also delegates to dashboard
            InvokeRepeating("AutoRefreshBalance", AUTO_REFRESH_INTERVAL, AUTO_REFRESH_INTERVAL);
        }
        catch (System.Exception ex) { if (statusText != null) statusText.text = "Init Error: " + ex.Message; }
        
        if (pendingPINVerification && AuthManager.Instance != null && AuthManager.Instance.IsAuthenticated)
        {
            pendingPINVerification = false;
            StartCoroutine(ExecutePendingTransactionDelayed());
        }
    }

    void OnEnable()
    {
        // ✅ Subscribe to central balance event — keeps all screens in sync
        WalletManager2.OnBalanceChanged += OnCentralBalanceChanged;
    }

    void OnDisable()
    {
        WalletManager2.OnBalanceChanged -= OnCentralBalanceChanged;
    }

    void OnCentralBalanceChanged(double newBalance, string currency)
    {
        // Called by WalletManager2 whenever balance is refreshed anywhere
        if (balanceText != null)
            balanceText.text = $"{newBalance:F8} {currency}";
        StartCoroutine(UpdateUSDValue((float)newBalance, false));
        Debug.Log($"[SendReceiveUI] ✅ Balance synced from event: {newBalance:F8} {currency}");
    }

    // Coroutine wrapper to ensure UI is ready before executing
    System.Collections.IEnumerator ExecutePendingTransactionDelayed()
    {
        yield return null; // Wait one frame
        ExecutePendingTransaction();
    }

    void OnDestroy()
    {
        if (cts != null) cts.Cancel();
        StopCamera();
    }

    // ✅ NEW: Block Android back button during transaction
    void Update()
    {
        if (loadingPanel != null && loadingPanel.activeSelf)
        {
            // Block Android/Escape back button
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Debug.Log("[SendReceiveUI] Back button blocked during transaction");
                // Don't process the back button
                return;
            }
        }
    }

    void BuildUI()
    {
        GameObject canvasObj = new GameObject("Canvas");
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

        // THEME: BG
        GameObject bg = new GameObject("BG");
        bg.layer = 5;
        bg.transform.SetParent(canvas.transform, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = ThemeManager.Instance.GetBackgroundColor(); 

        if (isDesktop)
        {
            // --- DESKTOP TOP BAR (Height 150) ---
            GameObject topBar = MakePanel(canvas.transform, "TopBar", 0, 0, 1920, 150, ThemeManager.Instance.GetCardColor());
            RectTransform topBarRT = topBar.GetComponent<RectTransform>();
            topBarRT.anchorMin = new Vector2(0, 1);
            topBarRT.anchorMax = new Vector2(1, 1);
            topBarRT.pivot = new Vector2(0.5f, 1);
            topBarRT.anchoredPosition = Vector2.zero;
            topBarRT.sizeDelta = new Vector2(0, 150); 

            // Back Button
            MakeButton(topBar.transform, LocalizationManager.Instance.Get("GEN_BACK"), -800, 0, 200, 70, () => SceneManager.LoadScene("MainWallet"))
                .GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();

            // Title
#if TESTNET_BUILD
            Text titleTxt = MakeText(topBar.transform, LocalizationManager.Instance.Get("TX_TITLE_TN"), 0, 20, 36, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
#else
            Text titleTxt = MakeText(topBar.transform, LocalizationManager.Instance.Get("TX_TITLE"), 0, 20, 36, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
#endif
            titleTxt.rectTransform.sizeDelta = new Vector2(500, 50);
            titleTxt.alignment = TextAnchor.MiddleCenter;
            
            // Balance Display
            Color balColor = ThemeManager.Instance.IsDarkMode ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.5f, 0f);
#if TESTNET_BUILD
            balanceText = MakeText(topBar.transform, "0.00000000 tBTC", 650, 20, 36, balColor, FontStyle.Bold);
#else
            balanceText = MakeText(topBar.transform, "0.00000000 BTC", 650, 20, 36, balColor, FontStyle.Bold);
#endif
            balanceText.rectTransform.sizeDelta = new Vector2(500, 50);
            balanceText.alignment = TextAnchor.MiddleRight;
            
            balanceUsdText = MakeText(topBar.transform, "", 650, -30, 24, new Color(1f, 0.84f, 0f), FontStyle.Normal);
            balanceUsdText.rectTransform.sizeDelta = new Vector2(500, 40);
            balanceUsdText.alignment = TextAnchor.MiddleRight;
            
#if TESTNET_BUILD
            networkText = MakeText(topBar.transform, "TestNet", 0, -30, 20, new Color(0.2f, 0.6f, 1f), FontStyle.Normal);
#else
            networkText = MakeText(topBar.transform, "MainNet", 0, -30, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
#endif
            networkText.rectTransform.sizeDelta = new Vector2(400, 40);
            networkText.alignment = TextAnchor.MiddleCenter;

            // --- DESKTOP CONTENT (Left: Send form, Right: QR/Address book/Recent) ---
            float desktopY = -70; // Center Y of layout content

            // Left Column: Send Card
            GameObject sendCard = MakeCard(canvas.transform, -450, desktopY, 850, 800);
            float cardY = 340;

#if TESTNET_BUILD
            MakeText(sendCard.transform, LocalizationManager.Instance.Get("TX_SEND_CARD_TN"), 0, cardY, 36, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
#else
            MakeText(sendCard.transform, LocalizationManager.Instance.Get("TX_SEND_CARD"), 0, cardY, 36, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
#endif
            cardY -= 70;

            // 1. To Address
            MakeLabel(sendCard.transform, LocalizationManager.Instance.Get("TX_TO_ADDR"), -150, cardY);
            cardY -= 65;
            sendAddressInput = MakeInput(sendCard.transform, -100, cardY, 600, 75, LocalizationManager.Instance.Get("INPUT_PASTE_ADDR"));
            
            MakeButton(sendCard.transform, LocalizationManager.Instance.Get("BTN_PASTE"), 240, cardY, 85, 75, () => {
                sendAddressInput.text = GUIUtility.systemCopyBuffer;
            });
            MakeButtonWithIcon(sendCard.transform, "camera", 340, cardY, 85, 75, OnScanQR);
            cardY -= 110;

            // 2. Amount
#if TESTNET_BUILD
            MakeLabel(sendCard.transform, LocalizationManager.Instance.Get("TX_AMOUNT_TN"), -150, cardY);
#else
            MakeLabel(sendCard.transform, LocalizationManager.Instance.Get("TX_AMOUNT"), -150, cardY);
#endif
            cardY -= 65;
            sendAmountInput = MakeInput(sendCard.transform, 0, cardY, 800, 75, "0.00000000");
            sendAmountInput.contentType = InputField.ContentType.DecimalNumber;
            sendAmountInput.onValueChanged.AddListener(OnAmountChanged);

            usdValueText = MakeText(sendCard.transform, "≈ $0.00 USD", 0, cardY - 55, 24, new Color(1f, 0.84f, 0f), FontStyle.Italic);
            usdValueText.alignment = TextAnchor.MiddleLeft; 
            usdValueText.rectTransform.sizeDelta = new Vector2(800, 40);
            cardY -= 110;

            // 3. Percentage Buttons
            float btnW = 160;
            float btnGap = 15;
            float startX = -((btnW * 4) + (btnGap * 3)) / 2 + btnW / 2;
            
            MakeButtonOutline(sendCard.transform, "25%", startX, cardY, btnW, 60, () => OnPercentageClick(0.25f));
            MakeButtonOutline(sendCard.transform, "50%", startX + btnW + btnGap, cardY, btnW, 60, () => OnPercentageClick(0.50f));
            MakeButtonOutline(sendCard.transform, "75%", startX + (btnW + btnGap) * 2, cardY, btnW, 60, () => OnPercentageClick(0.75f));
            MakeButtonOutline(sendCard.transform, "MAX", startX + (btnW + btnGap) * 3, cardY, btnW, 60, () => OnPercentageClick(1.0f));
            cardY -= 95;

            // 4. Fee
            MakeLabel(sendCard.transform, LocalizationManager.Instance.Get("TX_FEE"), -150, cardY);
            cardY -= 65;
            feeInput = MakeInput(sendCard.transform, 0, cardY, 800, 75, "0.00001000");
            feeInput.text = PlayerPrefs.GetString("Send_FeePref", "0.00001"); 
            feeInput.contentType = InputField.ContentType.DecimalNumber;
            feeInput.onValueChanged.AddListener((val) => {
                PlayerPrefs.SetString("Send_FeePref", val);
                PlayerPrefs.Save();
            });
            cardY -= 110;

            // 5. Send Action
#if TESTNET_BUILD
            MakeButton(sendCard.transform, LocalizationManager.Instance.Get("TX_BTN_SEND_TN"), 0, cardY, 450, 90, OnSendPressed).GetComponent<Image>().color = new Color(0.15f, 0.5f, 0.85f);
#else
            MakeButton(sendCard.transform, LocalizationManager.Instance.Get("TX_BTN_SEND"), 0, cardY, 450, 90, OnSendPressed).GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.3f);
#endif

            // Right Column: Show QR, Address book, Recent Addresses
            float rightX = 450;
#if TESTNET_BUILD
            MakeButton(canvas.transform, LocalizationManager.Instance.Get("TX_BTN_SHOW_QR_TN"), rightX, desktopY + 310, 800, 100, OnShowReceive);
#else
            MakeButton(canvas.transform, LocalizationManager.Instance.Get("TX_BTN_SHOW_QR"), rightX, desktopY + 310, 800, 100, OnShowReceive);
#endif

            MakeButton(canvas.transform, LocalizationManager.Instance.Get("ADDR_BOOK_BTN"), rightX, desktopY + 180, 800, 100, ShowAddressBookOverlay)
                .GetComponent<Image>().color = new Color(0.55f, 0.35f, 0.15f);

            GameObject abCard = MakeCard(canvas.transform, rightX, desktopY - 140, 800, 480);
            MakeText(abCard.transform, LocalizationManager.Instance.Get("RECENT_ADDR_TITLE"), 0, 190, 32, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

            addressBookContainer = abCard;
            UpdateAddressBookUI();
        }
        else
        {
            // Mobile portrait layout (original)
            // --- TOP BAR (Taller to show balance) ---
            GameObject topBar = MakePanel(canvas.transform, "TopBar", 0, 0, REFERENCE_WIDTH, 300, ThemeManager.Instance.GetCardColor());
            RectTransform topBarRT = topBar.GetComponent<RectTransform>();
            topBarRT.anchorMin = new Vector2(0, 1);
            topBarRT.anchorMax = new Vector2(1, 1);
            topBarRT.pivot = new Vector2(0.5f, 1);
            topBarRT.anchoredPosition = Vector2.zero;
            topBarRT.sizeDelta = new Vector2(0, 300); 
            topBarRT.offsetMin = new Vector2(0, topBarRT.offsetMin.y);
            topBarRT.offsetMax = new Vector2(0, topBarRT.offsetMax.y);

            // Back Button
            MakeButton(topBar.transform, LocalizationManager.Instance.Get("GEN_BACK"), -400, 60, 200, 80, () => SceneManager.LoadScene("MainWallet"))
                .GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();

            // Title - network-aware
#if TESTNET_BUILD
            MakeText(topBar.transform, LocalizationManager.Instance.Get("TX_TITLE_TN"), 0, 80, 40, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
#else
            MakeText(topBar.transform, LocalizationManager.Instance.Get("TX_TITLE"), 0, 80, 40, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
#endif
            
            // Balance Display - Always visible, shows cached balance
            Color balColor = ThemeManager.Instance.IsDarkMode ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.5f, 0f);
#if TESTNET_BUILD
            balanceText = MakeText(topBar.transform, "0.00000000 tBTC", 0, 10, 42, balColor, FontStyle.Bold);
#else
            balanceText = MakeText(topBar.transform, "0.00000000 BTC", 0, 10, 42, balColor, FontStyle.Bold);
#endif
            balanceText.gameObject.SetActive(true);
            
            // USD equivalent below balance
            balanceUsdText = MakeText(topBar.transform, "", 0, -35, 28, new Color(1f, 0.84f, 0f), FontStyle.Normal);
            balanceUsdText.gameObject.SetActive(true);
            
            // Network label
#if TESTNET_BUILD
            networkText = MakeText(topBar.transform, "TestNet", 0, -75, 24, new Color(0.2f, 0.6f, 1f), FontStyle.Normal);
#else
            networkText = MakeText(topBar.transform, "MainNet", 0, -75, 24, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
#endif
            networkText.gameObject.SetActive(true);

            // --- CONTENT ---
            float startY = 100; 

            // Send Card - Taller and better positioned
            GameObject sendCard = MakeCard(canvas.transform, 0, startY, 1000, 1100);
            float cardY = 480;

            // Send Card header - network-aware
#if TESTNET_BUILD
            MakeText(sendCard.transform, LocalizationManager.Instance.Get("TX_SEND_CARD_TN"), 0, cardY, 40, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
#else
            MakeText(sendCard.transform, LocalizationManager.Instance.Get("TX_SEND_CARD"), 0, cardY, 40, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
#endif
            cardY -= 90;

            // 1. To Address
            MakeLabel(sendCard.transform, LocalizationManager.Instance.Get("TX_TO_ADDR"), -185, cardY);
            cardY -= 80;
            sendAddressInput = MakeInput(sendCard.transform, -120, cardY, 630, 90, LocalizationManager.Instance.Get("INPUT_PASTE_ADDR"));
            
            // Paste Button (Clipboard)
            MakeButton(sendCard.transform, LocalizationManager.Instance.Get("BTN_PASTE"), 270, cardY, 100, 90, () => {
                sendAddressInput.text = GUIUtility.systemCopyBuffer;
            });
            
            // Scan Button with Camera Icon
            MakeButtonWithIcon(sendCard.transform, "camera", 390, cardY, 100, 90, OnScanQR);
            cardY -= 140;

            // 2. Amount
#if TESTNET_BUILD
            MakeLabel(sendCard.transform, LocalizationManager.Instance.Get("TX_AMOUNT_TN"), -200, cardY);
#else
            MakeLabel(sendCard.transform, LocalizationManager.Instance.Get("TX_AMOUNT"), -200, cardY);
#endif
            cardY -= 80;
            sendAmountInput = MakeInput(sendCard.transform, 0, cardY, 900, 90, "0.00000000");
            sendAmountInput.contentType = InputField.ContentType.DecimalNumber;
            sendAmountInput.onValueChanged.AddListener(OnAmountChanged);
            cardY -= 70;
            
            usdValueText = MakeText(sendCard.transform, "≈ $0.00 USD", 0, cardY, 28, new Color(1f, 0.84f, 0f), FontStyle.Italic);
            usdValueText.alignment = TextAnchor.MiddleLeft; 
            usdValueText.rectTransform.sizeDelta = new Vector2(900, 50);
            cardY -= 80;

            // 3. Percentage Buttons
            float btnW = 200;
            float btnGap = 25;
            float startX = -((btnW * 4) + (btnGap * 3)) / 2 + btnW / 2;
            
            MakeButtonOutline(sendCard.transform, "25%", startX, cardY, btnW, 70, () => OnPercentageClick(0.25f));
            MakeButtonOutline(sendCard.transform, "50%", startX + btnW + btnGap, cardY, btnW, 70, () => OnPercentageClick(0.50f));
            MakeButtonOutline(sendCard.transform, "75%", startX + (btnW + btnGap) * 2, cardY, btnW, 70, () => OnPercentageClick(0.75f));
            MakeButtonOutline(sendCard.transform, "MAX", startX + (btnW + btnGap) * 3, cardY, btnW, 70, () => OnPercentageClick(1.0f));
            cardY -= 120;

            // 4. Fee
            MakeLabel(sendCard.transform, LocalizationManager.Instance.Get("TX_FEE"), -200, cardY);
            cardY -= 80;
            feeInput = MakeInput(sendCard.transform, 0, cardY, 900, 90, "0.00001000");
            feeInput.text = PlayerPrefs.GetString("Send_FeePref", "0.00001"); 
            feeInput.contentType = InputField.ContentType.DecimalNumber;
            feeInput.onValueChanged.AddListener((val) => {
                PlayerPrefs.SetString("Send_FeePref", val);
                PlayerPrefs.Save();
            });
            cardY -= 150;

            // 5. Send Action
#if TESTNET_BUILD
            MakeButton(sendCard.transform, LocalizationManager.Instance.Get("TX_BTN_SEND_TN"), 0, cardY, 500, 110, OnSendPressed).GetComponent<Image>().color = new Color(0.15f, 0.5f, 0.85f);
#else
            MakeButton(sendCard.transform, LocalizationManager.Instance.Get("TX_BTN_SEND"), 0, cardY, 500, 110, OnSendPressed).GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.3f);
#endif
            
            // Address Book Button
            MakeButton(canvas.transform, LocalizationManager.Instance.Get("ADDR_BOOK_BTN"), 0, -500, 900, 120, ShowAddressBookOverlay)
                .GetComponent<Image>().color = new Color(0.55f, 0.35f, 0.15f);
            
            // "Show My QR" Button
#if TESTNET_BUILD
            MakeButton(canvas.transform, LocalizationManager.Instance.Get("TX_BTN_SHOW_QR_TN"), 0, -650, 900, 120, OnShowReceive);
#else
            MakeButton(canvas.transform, LocalizationManager.Instance.Get("TX_BTN_SHOW_QR"), 0, -650, 900, 120, OnShowReceive);
#endif

            // Recent Addresses Card
            GameObject abCard = MakeCard(canvas.transform, 0, -950, 1000, 350);
            MakeText(abCard.transform, LocalizationManager.Instance.Get("RECENT_ADDR_TITLE"), 0, 120, 35, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            
            addressBookContainer = abCard;
            UpdateAddressBookUI();
        }

        statusText = MakeText(canvas.transform, LocalizationManager.Instance.Get("STATUS_READY"), 0, 160, 32, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        RectTransform statusRT = statusText.rectTransform;
        statusRT.anchorMin = new Vector2(0.5f, 1); statusRT.anchorMax = new Vector2(0.5f, 1);
        if (isDesktop)
            statusRT.anchoredPosition = new Vector2(0, -170); // Below compact TopBar
        else
            statusRT.anchoredPosition = new Vector2(0, -310); // Below original TopBar

        CreateReceivePanel(canvas.transform);
        CreateScanPanel(canvas.transform);
    }
    
    // --- HELPERS ---

    Font GetFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 16);
        return f;
    }
    
    GameObject MakePanel(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.layer = 5; 
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition3D = new Vector3(x, y, 0); // Z=0
        rt.sizeDelta = new Vector2(w, h);
        obj.AddComponent<Image>().color = color;
        return obj;
    }

    GameObject MakeCard(Transform parent, float x, float y, float w, float h)
    {
        return MakePanel(parent, "Card", x, y, w, h, ThemeManager.Instance.GetCardColor());
    }

    Text MakeText(Transform parent, string txt, float x, float y, int size, Color col, FontStyle style)
    {
        GameObject obj = new GameObject("Text");
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(950, 80);
        Text text = obj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt); text.fontSize = size; text.color = col; text.fontStyle = style; text.font = GetFont();
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false; // Fix: No block clicks
        return text;
    }

    void MakeLabel(Transform parent, string txt, float x, float y)
    {
        GameObject obj = new GameObject("Label");
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(500, 50);
        Text text = obj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt); text.fontSize = 28; text.color = ThemeManager.Instance.GetSecondaryTextColor();
        text.font = GetFont(); text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
    }

    InputField MakeInput(Transform parent, float x, float y, float w, float h, string placeholder)
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
        textRT.offsetMin = new Vector2(10, 0); textRT.offsetMax = new Vector2(-10, 0);
        Text text = textObj.AddComponent<Text>();
        text.font = GetFont(); text.fontSize = 32; text.color = ThemeManager.Instance.GetTextColor(); text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        input.textComponent = text;
        input.onValueChanged.AddListener((val) => AudioManager.Instance.PlayPinType());
        
        GameObject phObj = new GameObject("Placeholder");
        phObj.layer = 5; phObj.transform.SetParent(obj.transform, false);
        RectTransform phRT = phObj.AddComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(10, 0); phRT.offsetMax = new Vector2(-10, 0);
        Text ph = phObj.AddComponent<Text>();
        ph.text = LocalizationManager.CleanIcons(placeholder); ph.font = GetFont(); ph.fontSize = 32; ph.color = new Color(0.5f, 0.5f, 0.5f);
        ph.fontStyle = FontStyle.Italic; ph.alignment = TextAnchor.MiddleLeft;
        ph.raycastTarget = false;
        input.placeholder = ph;
        return input;
    }

    Button MakeButton(Transform parent, string txt, float x, float y, float w, float h, System.Action onClick)
    {
        GameObject obj = new GameObject("Button");
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(w, h);
        Image img = obj.AddComponent<Image>();
        img.color = ThemeManager.Instance.GetAccentColor();
        Button btn = obj.AddComponent<Button>();
        
        if (txt == LocalizationManager.Instance.Get("GEN_BACK"))
            btn.onClick.AddListener(() => AudioManager.Instance.PlayBack());
        else
            btn.onClick.AddListener(() => AudioManager.Instance.PlayClick());

        btn.onClick.AddListener(()=>onClick());
        
        if (!string.IsNullOrEmpty(txt)) {
            GameObject tObj = new GameObject("Text");
            tObj.layer = 5; tObj.transform.SetParent(obj.transform, false);
            RectTransform tRT = tObj.AddComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
            tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
            Text t = tObj.AddComponent<Text>();
            t.text = LocalizationManager.CleanIcons(txt); t.font = GetFont(); t.fontSize = 32; t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter; t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
        }
        return btn;
    }

    Button MakeButtonOutline(Transform parent, string txt, float x, float y, float w, float h, System.Action onClick)
    {
        GameObject obj = new GameObject("ButtonOutline");
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(w, h);
        obj.AddComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.2f, 0.2f, 0.3f) : new Color(0.9f, 0.9f, 0.95f);
        Button btn = obj.AddComponent<Button>();
        btn.onClick.AddListener(() => AudioManager.Instance.PlayCloseMenu());
        btn.onClick.AddListener(()=>onClick());
        
        GameObject tObj = new GameObject("Text");
        tObj.layer = 5; tObj.transform.SetParent(obj.transform, false);
        RectTransform tRT = tObj.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        Text t = tObj.AddComponent<Text>();
        t.text = txt; t.font = GetFont(); t.fontSize = 28; 
        t.color = ThemeManager.Instance.GetAccentColor();
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        return btn;
    }

    Button MakeButtonWithIcon(Transform parent, string iconName, float x, float y, float w, float h, System.Action onClick)
    {
        GameObject obj = new GameObject("ButtonWithIcon");
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(w, h);
        Image bgImg = obj.AddComponent<Image>();
        bgImg.color = ThemeManager.Instance.GetAccentColor();
        Button btn = obj.AddComponent<Button>();
        btn.onClick.AddListener(() => AudioManager.Instance.PlayClick());
        btn.onClick.AddListener(()=>onClick());
        
        // Load icon from Resources
        Sprite iconSprite = Resources.Load<Sprite>(iconName);
        if (iconSprite != null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.layer = 5;
            iconObj.transform.SetParent(obj.transform, false);
            RectTransform iconRT = iconObj.AddComponent<RectTransform>();
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.offsetMin = new Vector2(10, 10); // Padding
            iconRT.offsetMax = new Vector2(-10, -10); // Padding
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = iconSprite;
            iconImg.color = Color.white;
            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true; // Maintain aspect ratio
        }
        else
        {
            Debug.LogWarning($"Icon '{iconName}' not found in Resources folder!");
        }
        
        return btn;
    }

    // --- LOGIC ---

    // ✅ Show the best known balance instantly (no API delay)
    void ShowCachedBalance()
    {
        WalletData wallet = WalletManager2.Instance.GetCurrentWallet();
        if (wallet == null) return;
        
#if TESTNET_BUILD
        bool isTestNetUI = true;
#else
        bool isTestNetUI = (wallet != null && (wallet.isTestNet || (!string.IsNullOrEmpty(wallet.address) && wallet.address.StartsWith("tb1"))));
#endif
        string currency = isTestNetUI ? "tBTC" : "BTC";
        // ✅ Use in-memory value (UpdateBalance keeps this current, no disk I/O lag)
        double cached = isTestNetUI ? wallet.cachedTestnetBalance : (wallet.cachedBalance > 0 ? wallet.cachedBalance : wallet.balance);
        
        if (balanceText)
        {
            balanceText.text = $"{cached:F8} {currency}";
            balanceText.gameObject.SetActive(true);
            Debug.Log($"[SendReceiveUI] ShowCachedBalance: {cached:F8} {currency}");
        }
        
        if (networkText)
        {
#if TESTNET_BUILD
            networkText.text = LocalizationManager.Instance.Get("TESTNET");
            networkText.color = new Color(0.2f, 0.6f, 1f);
#else
            networkText.text = LocalizationManager.Instance.Get("MAINNET");
            networkText.color = ThemeManager.Instance.GetSecondaryTextColor();
#endif
            networkText.gameObject.SetActive(true);
        }
        
        // Always fetch price so USD value shows on startup too
        StartCoroutine(UpdateUSDValue((float)cached, false));
    }
    
    // ✅ Auto-refresh: always delegate to dashboard to avoid duplicate API calls
    void AutoRefreshBalance()
    {
        if (!WalletManager2.Instance.HasWallet()) return;

        if (ModernMobileWallet.Instance != null)
        {
            // Dashboard is active — let it do the one true refresh
            // The result fires OnBalanceChanged → OnCentralBalanceChanged updates our UI
            Debug.Log("[SendReceiveUI] AutoRefresh — delegating to ModernMobileWallet");
            ModernMobileWallet.Instance.ForceRefreshBalance();
        }
        else
        {
            // Standalone: do our own refresh only if no dashboard is running
            RefreshWalletStatus();
        }
    }

    // Fallback refresh — only runs when ModernMobileWallet is NOT in the scene
    async void RefreshWalletStatus()
    {
        // Skip if dashboard is already running a refresh
        if (ModernMobileWallet.IsRefreshingBalance)
        {
            Debug.Log("[SendReceiveUI] Skipping own refresh — dashboard already refreshing");
            return;
        }
        
        WalletData wallet = WalletManager2.Instance.GetCurrentWallet();
        if (wallet == null) return;

#if TESTNET_BUILD
        bool useTestNet = true;
#else
        bool useTestNet = false;
#endif

        Debug.Log($"[SendReceiveUI] Fallback refresh ({(useTestNet ? "TestNet" : "MainNet")})");
        try
        {
            decimal total = 0;
            if (!string.IsNullOrEmpty(wallet.seedPhrase))
            {
                var result = await transactionService.ScanAllAddresses(
                    wallet.seedPhrase, null, wallet.generatedAddresses, null, useTestNet);
                total = result.confirmedBalance;
            }
            else if (!string.IsNullOrEmpty(wallet.privateKey))
            {
                var result = await transactionService.ScanAllAddresses(
                    null, null, null, wallet.privateKey, useTestNet);
                total = result.confirmedBalance;
            }
            // UpdateBalance fires OnBalanceChanged → all screens update
            WalletManager2.Instance.UpdateBalance((double)total);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[SendReceiveUI] Fallback refresh error: {ex.Message}");
        }
    }

    void OnPercentageClick(float percent)
    {
        Debug.Log($"[SendReceiveUI] Percentage Click: {percent}");
        WalletData wallet = WalletManager2.Instance.GetCurrentWallet();
        
        if(wallet == null) {
            Debug.LogError("Wallet is null");
            return;
        }
        
        // ✅ Use ONLY the correct network balance field — no cross-network fallback
#if TESTNET_BUILD
        double bal = wallet.cachedTestnetBalance;
#else
        double bal = wallet.cachedBalance > 0 ? wallet.cachedBalance : wallet.balance;
#endif
        Debug.Log($"[SendReceiveUI] Balance for percentage: {bal}");
        
        double fee = 0.0000025; 
        if(decimal.TryParse(feeInput.text, out decimal f)) fee = (double)f;
        
        // Logic: Calculate percent, subtract fee only if MAX (approx 1.0)
        double amt = (bal * percent);
        if(percent > 0.99f) amt -= fee;
        
        if(amt < 0) amt = 0;
        
        sendAmountInput.text = amt.ToString("0.########");
        Debug.Log($"[SendReceiveUI] New Amount set: {sendAmountInput.text}");
        OnAmountChanged(sendAmountInput.text);
    }

    void OnAmountChanged(string val) {
        if(float.TryParse(val,out float a)) StartCoroutine(UpdateUSDValue(a,true));
    }

    IEnumerator UpdateUSDValue(float btc, bool isAmt) {
        string currencyCode = PlayerPrefs.GetString("SelectedCurrency", "USD").ToLower();
        string symbol = CurrencyHelper.GetSymbol(currencyCode.ToUpper());

#if TESTNET_BUILD
        // Fetch real tBTC price from CoinPaprika
        string tbtcUrl = "https://api.coinpaprika.com/v1/tickers/tbtc-bitcoin-testnet";
        using (UnityWebRequest tReq = UnityWebRequest.Get(tbtcUrl)) {
            yield return tReq.SendWebRequest();
            if (tReq.result == UnityWebRequest.Result.Success) {
                try {
                    double p = ParseCoinpaprikaPrice(tReq.downloadHandler.text, "USD");
                    if (p > 0) {
                        lastBtcPrice = p;
                        if (usdValueText) usdValueText.text = $"≈ {symbol}{(p*btc):F4} {currencyCode.ToUpper()}";
                        if (!isAmt && balanceUsdText) balanceUsdText.text = $"≈ {symbol}{(p*btc):F4} {currencyCode.ToUpper()}";
                    }
                } catch {}
            }
        }
#else
        bool gotPrice = false;

        // Tier 1: Blockchain.info ticker (fast, multi-currency, unblocked globally)
        string binfoUrl = "https://blockchain.info/ticker";
        using (UnityWebRequest req = UnityWebRequest.Get(binfoUrl)) {
            req.timeout = 8;
            req.SetRequestHeader("User-Agent", "CoinHouseWallet/1.0");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success) {
                try {
                    double p = ParseBlockchainInfoPrice(req.downloadHandler.text, currencyCode);
                    if (p > 0) {
                        gotPrice = true;
                        lastBtcPrice = p;
                        if(usdValueText) usdValueText.text = $"≈ {symbol}{(p*btc):F2} {currencyCode.ToUpper()}";
                        if(!isAmt && balanceUsdText) balanceUsdText.text = $"≈ {symbol}{(p*btc):F2} {currencyCode.ToUpper()}";
                    }
                } catch {}
            }
        }

        // Tier 2: Binance ticker (reliable fallback)
        if (!gotPrice) {
            string binanceUrl = "https://api.binance.com/api/v3/ticker/price?symbol=BTCUSDT";
            using (UnityWebRequest bReq = UnityWebRequest.Get(binanceUrl)) {
                bReq.timeout = 8;
                bReq.SetRequestHeader("User-Agent", "CoinHouseWallet/1.0");
                yield return bReq.SendWebRequest();

                if (bReq.result == UnityWebRequest.Result.Success) {
                    try {
                        double p = ParseBinancePrice(bReq.downloadHandler.text);
                        if (p > 0) {
                            gotPrice = true;
                            lastBtcPrice = p;
                            if(usdValueText) usdValueText.text = $"≈ {symbol}{(p*btc):F2} {currencyCode.ToUpper()}";
                            if(!isAmt && balanceUsdText) balanceUsdText.text = $"≈ {symbol}{(p*btc):F2} {currencyCode.ToUpper()}";
                        }
                    } catch {}
                }
            }
        }

        // Tier 3: CoinPaprika Fallback
        if (!gotPrice) {
            yield return StartCoroutine(FetchPriceFallback(btc, isAmt, currencyCode, symbol));
        }
#endif
    }

    IEnumerator FetchPriceFallback(float btc, bool isAmt, string currencyCode, string symbol)
    {
        // Mainnet-only fallback (CoinPaprika with User-Agent)
        string fallbackUrl = "https://api.coinpaprika.com/v1/tickers/btc-bitcoin";
        using(UnityWebRequest req = UnityWebRequest.Get(fallbackUrl))
        {
            req.timeout = 8;
            req.SetRequestHeader("User-Agent", "CoinHouseWallet/1.0");
            yield return req.SendWebRequest();
            if(req.result == UnityWebRequest.Result.Success)
            {
                try {
                    double p = ParseCoinpaprikaPrice(req.downloadHandler.text, "USD");
                    if (p > 0)
                    {
                        lastBtcPrice = p;
                        if(usdValueText) usdValueText.text = $"≈ {symbol}{(p*btc):F2} {currencyCode.ToUpper()}";
                        if(!isAmt && balanceUsdText) balanceUsdText.text = $"≈ {symbol}{(p*btc):F2} {currencyCode.ToUpper()}";
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

    double ParsePriceSimple(string json, string currencyCode) {
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

    // --- IMPROVED SEND FLOW ---
    
    private GameObject confirmationPanel;
    private GameObject resultPanel;
    private Text resultText;
    private Text resultHashText;
    private Text resultIcon;

    void OnSendPressed()
    {
        if(!decimal.TryParse(sendAmountInput.text, out decimal amt)) { ShowStatus(LocalizationManager.Instance.Get("ERR_INVALID_AMT"), Color.red); return; }
        if(string.IsNullOrEmpty(sendAddressInput.text)) { ShowStatus(LocalizationManager.Instance.Get("ERR_INVALID_ADDR"), Color.red); return; }
        if(amt <= 0) { ShowStatus(LocalizationManager.Instance.Get("ERR_AMOUNT_GT_ZERO"), Color.red); return; }
        
        // Get fee from input field
        decimal fee = 0.0000025m; // Default
        if(decimal.TryParse(feeInput.text, out decimal userFee) && userFee > 0)
            fee = userFee;
        
        // ✅ Pre-flight balance check — catch insufficient funds before queuing
        WalletData wCheck = WalletManager2.Instance.GetCurrentWallet();
        if (wCheck != null)
        {
#if TESTNET_BUILD
            // TestNet: use ONLY cachedTestnetBalance — never cross to mainnet cachedBalance
            decimal cachedBal = (decimal)wCheck.cachedTestnetBalance;
            string cur = "tBTC";
#else
            decimal cachedBal = (decimal)(wCheck.cachedBalance > 0 ? wCheck.cachedBalance : wCheck.balance);
            string cur = "BTC";
#endif
            if (cachedBal > 0 && amt + fee > cachedBal)
            {
                ShowStatus($"❌ Not enough funds.\nNeed {amt+fee:F8} {cur}, have {cachedBal:F8} {cur}", Color.red);
                // Force a balance refresh so user sees the real value
                RefreshWalletStatus();
                return;
            }
        }
        
        // Open Confirmation Panel
        if(confirmationPanel == null) BuildConfirmationPanel();
        UpdateConfirmationData(sendAddressInput.text, amt, fee);
        confirmationPanel.SetActive(true);
        confirmationPanel.transform.SetAsLastSibling();
    }
    
    void BuildConfirmationPanel()
    {
        GameObject canvas = GameObject.Find("Canvas");
        confirmationPanel = MakePanel(canvas.transform, "ConfirmPanel", 0, 0, REFERENCE_WIDTH, REFERENCE_HEIGHT, new Color(0,0,0,0.85f));
        
        // Card - Clean dark card matching screenshot
        GameObject card = MakePanel(confirmationPanel.transform, "Card", 0, 50, 950, 900, new Color(0.08f, 0.08f, 0.14f));
        
        // Title: CONFIRM TRANSACTION - network-aware
#if TESTNET_BUILD
        MakeText(card.transform, LocalizationManager.Instance.Get("TX_CONFIRM_TITLE_TN"), 0, 380, 44, new Color(0.2f, 0.6f, 1f), FontStyle.Bold);
#else
        MakeText(card.transform, LocalizationManager.Instance.Get("TX_CONFIRM_TITLE"), 0, 380, 44, Color.white, FontStyle.Bold);
#endif
        
        // Divider line below title
        GameObject divider1 = MakePanel(card.transform, "Divider1", 0, 330, 880, 2, new Color(0.3f, 0.3f, 0.4f));
        
        // Destination Address (full, white, left-aligned)
        GameObject addrObj = MakeText(card.transform, "...", 0, 260, 28, Color.white, FontStyle.Normal).gameObject;
        addrObj.name = "addr_val";
        Text addrText = addrObj.GetComponent<Text>();
        addrText.alignment = TextAnchor.MiddleLeft;
        addrText.rectTransform.sizeDelta = new Vector2(860, 80);
        addrText.horizontalOverflow = HorizontalWrapMode.Wrap;
        
        // Divider line below address
        GameObject divider2 = MakePanel(card.transform, "Divider2", 0, 200, 880, 2, new Color(0.3f, 0.3f, 0.4f));
        
        // --- Amount Row ---
        float rowY = 130;
        // BTC/BTCT amount (left) - network-aware initial label
#if TESTNET_BUILD
        GameObject amtBtcObj = MakeText(card.transform, "0.00000000 BTCT", -220, rowY, 32, Color.white, FontStyle.Normal).gameObject;
#else
        GameObject amtBtcObj = MakeText(card.transform, "0.00000000 BTC", -220, rowY, 32, Color.white, FontStyle.Normal).gameObject;
#endif
        amtBtcObj.name = "amt_btc_val";
        amtBtcObj.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
        amtBtcObj.GetComponent<RectTransform>().sizeDelta = new Vector2(450, 50);
        // ~ USD (right)
        GameObject amtUsdObj = MakeText(card.transform, "~ $0.00 USD", 220, rowY, 36, Color.white, FontStyle.Bold).gameObject;
        amtUsdObj.name = "amt_usd_val";
        amtUsdObj.GetComponent<Text>().alignment = TextAnchor.MiddleRight;
        amtUsdObj.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 50);
        // Label: (Sending Amount)
        var amtLabel = MakeText(card.transform, LocalizationManager.Instance.Get("TX_LBL_AMT"), -220, rowY - 38, 22, new Color(0.6f, 0.6f, 0.7f), FontStyle.Italic);
        amtLabel.alignment = TextAnchor.MiddleLeft;
        amtLabel.rectTransform.sizeDelta = new Vector2(450, 35);
        
        // Divider
        GameObject divider3 = MakePanel(card.transform, "Divider3", 0, 85, 880, 1, new Color(0.25f, 0.25f, 0.35f));
        
        // --- Fee Row ---
        float feeY = 40;
#if TESTNET_BUILD
        GameObject feeBtcObj = MakeText(card.transform, "0.00000000 BTCT", -220, feeY, 32, Color.white, FontStyle.Normal).gameObject;
#else
        GameObject feeBtcObj = MakeText(card.transform, "0.00000000 BTC", -220, feeY, 32, Color.white, FontStyle.Normal).gameObject;
#endif
        feeBtcObj.name = "fee_btc_val";
        feeBtcObj.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
        feeBtcObj.GetComponent<RectTransform>().sizeDelta = new Vector2(450, 50);
        
        GameObject feeUsdObj = MakeText(card.transform, "~ $0.00 USD", 220, feeY, 36, Color.white, FontStyle.Normal).gameObject;
        feeUsdObj.name = "fee_usd_val";
        feeUsdObj.GetComponent<Text>().alignment = TextAnchor.MiddleRight;
        feeUsdObj.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 50);
        // Label: (Transaction Fee)
        var feeLabel = MakeText(card.transform, LocalizationManager.Instance.Get("TX_LBL_FEE"), -220, feeY - 38, 22, new Color(0.6f, 0.6f, 0.7f), FontStyle.Italic);
        feeLabel.alignment = TextAnchor.MiddleLeft;
        feeLabel.rectTransform.sizeDelta = new Vector2(450, 35);
        
        // Divider
        GameObject divider4 = MakePanel(card.transform, "Divider4", 0, -5, 880, 1, new Color(0.25f, 0.25f, 0.35f));
        
        // --- Total Row (prominent) ---
        float totalY = -60;
#if TESTNET_BUILD
        GameObject totalBtcObj = MakeText(card.transform, "0.00000000 BTCT", -220, totalY, 32, Color.white, FontStyle.Bold).gameObject;
#else
        GameObject totalBtcObj = MakeText(card.transform, "0.00000000 BTC", -220, totalY, 32, Color.white, FontStyle.Bold).gameObject;
#endif
        totalBtcObj.name = "total_btc_val";
        totalBtcObj.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
        totalBtcObj.GetComponent<RectTransform>().sizeDelta = new Vector2(450, 50);
        
        GameObject totalUsdObj = MakeText(card.transform, "~ $0.00 USD", 220, totalY, 38, new Color(0.2f, 1f, 0.2f), FontStyle.Bold).gameObject;
        totalUsdObj.name = "total_usd_val";
        totalUsdObj.GetComponent<Text>().alignment = TextAnchor.MiddleRight;
        totalUsdObj.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 50);
        // Label: (Total Amount)
        var totalLabel = MakeText(card.transform, LocalizationManager.Instance.Get("TX_LBL_TOTAL"), -220, totalY - 38, 22, new Color(0.2f, 1f, 0.2f), FontStyle.Italic);
        totalLabel.alignment = TextAnchor.MiddleLeft;
        totalLabel.rectTransform.sizeDelta = new Vector2(450, 35);
        
        // Divider
        GameObject divider5 = MakePanel(card.transform, "Divider5", 0, -110, 880, 2, new Color(0.3f, 0.3f, 0.4f));
        
        // CONFIRM SEND Button - network-aware color
#if TESTNET_BUILD
        MakeButton(card.transform, LocalizationManager.Instance.Get("TX_BTN_CONFIRM_TN"), 0, -200, 500, 100, ProcessSend)
            .GetComponent<Image>().color = new Color(0.1f, 0.45f, 0.85f); // Blue for TestNet
#else
        MakeButton(card.transform, LocalizationManager.Instance.Get("TX_BTN_CONFIRM"), 0, -200, 500, 100, ProcessSend)
            .GetComponent<Image>().color = new Color(0.15f, 0.55f, 0.25f);
#endif
        
        // --- Cancel Button (Blue, smaller) ---
        Button cancelBtn = MakeButton(card.transform, LocalizationManager.Instance.Get("TX_BTN_CANCEL"), 0, -320, 300, 70, () => confirmationPanel.SetActive(false));
        cancelBtn.GetComponent<Image>().color = new Color(0.15f, 0.4f, 0.85f);
        Text cancelTxt = cancelBtn.GetComponentInChildren<Text>();
        if (cancelTxt != null)
        {
            cancelTxt.alignment = TextAnchor.MiddleCenter;
            cancelTxt.rectTransform.anchorMin = Vector2.zero;
            cancelTxt.rectTransform.anchorMax = Vector2.one;
            cancelTxt.rectTransform.offsetMin = Vector2.zero;
            cancelTxt.rectTransform.offsetMax = Vector2.zero;
        }
    }
    
    void UpdateConfirmationData(string addr, decimal amt, decimal fee)
    {
        string currency = PlayerPrefs.GetString("SelectedCurrency", "USD");
        string sym = CurrencyHelper.GetSymbol(currency);
        
        // Amount/Fee/Total rows - show BTCT in TestNet
#if TESTNET_BUILD
        string coinLabel = "BTCT";
#else
        string coinLabel = "BTC";
#endif
        // Set address (full address, no truncation)
        SetText(confirmationPanel, "addr_val", addr);
        
        // Amount row
        SetText(confirmationPanel, "amt_btc_val", $"{amt:F8} {coinLabel}");
        // Fee row
        SetText(confirmationPanel, "fee_btc_val", $"{fee:F8} {coinLabel}");
        // Total row
        SetText(confirmationPanel, "total_btc_val", $"{amt+fee:F8} {coinLabel}");
        
        // USD equivalents
        if (lastBtcPrice > 0)
        {
            SetText(confirmationPanel, "amt_usd_val", $"~ {sym}{((double)amt * lastBtcPrice):F2} {currency}");
            SetText(confirmationPanel, "fee_usd_val", $"~ {sym}{((double)fee * lastBtcPrice):F2} {currency}");
            SetText(confirmationPanel, "total_usd_val", $"~ {sym}{((double)(amt+fee) * lastBtcPrice):F2} {currency}");
        }
        else
        {
            SetText(confirmationPanel, "amt_usd_val", "");
            SetText(confirmationPanel, "fee_usd_val", "");
            SetText(confirmationPanel, "total_usd_val", "");
        }
    }
    
    void SetText(GameObject panel, string name, string val) {
        Transform t = panel.transform.Find("Card")?.Find(name);
        if(t) t.GetComponent<Text>().text = val;
    }

    async void ProcessSend()
    {
        // PIN verification gate - require PIN before broadcasting
        pendingAddress = sendAddressInput.text;
        if(!decimal.TryParse(sendAmountInput.text, out decimal parsedAmt)) return;
        pendingAmount = parsedAmt;
        pendingFee = 0.0000025m;
        if(decimal.TryParse(feeInput.text, out decimal parsedFee) && parsedFee > 0)
            pendingFee = parsedFee;
        
        // Save pending state and require PIN
        pendingPINVerification = true;
        PlayerPrefs.SetInt("PendingTxPIN", 1);
        PlayerPrefs.Save();
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
        SceneManager.LoadScene("AppLock");
    }
    
    async void ExecutePendingTransaction()
    {
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
        
        // Reset cancellation flag
        transactionCancelled = false;
        
        // Show Blocking Loader
        if(loadingPanel == null) BuildLoadingPanel();
        loadingPanel.SetActive(true);
        loadingPanel.transform.SetAsLastSibling();
        
        // Show cancel button
        if (cancelButton != null)
        {
            cancelButton.gameObject.SetActive(true);
        }
        
        // Wait 5 seconds with countdown and immediate cancel option
        float countdown = 5.0f;
        while (countdown > 0f)
        {
            if (transactionCancelled)
            {
                Debug.Log("❌ [SendReceiveUI] Transaction cancelled by user");
                if (loadingPanel != null) loadingPanel.SetActive(false);
                ShowResult(false, "", LocalizationManager.Instance.Get("TX_CANCELLED"));
                return;
            }

            int secs = Mathf.CeilToInt(countdown);
            if (loadingText != null)
            {
                loadingText.text = $"Preparing Transaction ({secs}s)...\nTap CANCEL to abort";
            }

            await System.Threading.Tasks.Task.Delay(100);
            countdown -= 0.1f;
        }

        if (transactionCancelled)
        {
            Debug.Log("❌ [SendReceiveUI] Transaction cancelled by user");
            if (loadingPanel != null) loadingPanel.SetActive(false);
            ShowResult(false, "", LocalizationManager.Instance.Get("TX_CANCELLED"));
            return;
        }

        // Hide cancel button after countdown expires
        if (cancelButton != null)
        {
            cancelButton.gameObject.SetActive(false);
        }

        if (loadingText != null)
        {
            loadingText.text = LocalizationManager.Instance.Get("TX_PROCESSING_BG");
        }
        
        decimal amt = pendingAmount;
        decimal fee = pendingFee;
        
        // Validate wallet exists
        if (!WalletManager2.Instance.HasWallet())
        {
            loadingPanel.SetActive(false);
            ShowResult(false, "", "No wallet found! Please create or restore a wallet first.");
            return;
        }
        
        WalletData w = WalletManager2.Instance.GetCurrentWallet();
        
        // Validate wallet has keys
        if (string.IsNullOrEmpty(w?.seedPhrase) && string.IsNullOrEmpty(w?.privateKey))
        {
            loadingPanel.SetActive(false);
            ShowResult(false, "", "Wallet keys not available! Please check your wallet.");
            return;
        }
        
        Debug.Log($"[Send] Wallet keys - Seed available: {!string.IsNullOrEmpty(w.seedPhrase)}, PrivKey available: {!string.IsNullOrEmpty(w.privateKey)}");
        
        string txAddress = pendingAddress;
        // ✅ FIX: keep seed and privateKey separate — do NOT merge into one variable
        // SendBitcoin accepts them as distinct parameters (seedPhrase vs privateKey)
        string seedToUse   = w.seedPhrase;   // may be empty for PK-import wallets

        // Determine network from build variant or wallet address
#if TESTNET_BUILD
        bool sendIsTestNet = true;
#else
        bool sendIsTestNet = (w != null && (w.isTestNet || (!string.IsNullOrEmpty(w.address) && w.address.StartsWith("tb1")) || (!string.IsNullOrEmpty(txAddress) && txAddress.StartsWith("tb1"))));
#endif
        string privKeyToUse = (sendIsTestNet && !string.IsNullOrEmpty(w.testnetPrivateKey)) ? w.testnetPrivateKey : w.privateKey;  // may be empty for seed wallets
        
        Debug.Log($"[Send] Network: {(sendIsTestNet ? "TestNet" : "MainNet")}, Address: {txAddress}, Amount: {amt}, Fee: {fee}");
        
        // Use BackgroundTransactionService
        BackgroundTransactionService.Instance.QueueTransaction(
            seedToUse,      // ✅ seed phrase (empty string if PK-import)
            sendIsTestNet,
            txAddress,
            amt,
            fee,
            (success, result) => {
                loadingPanel.SetActive(false);
                
                if (success)
                {
                    WalletManager2.Instance.AddTransaction("Send", txAddress, amt.ToString(), result);
                    UpdateAddressBookUI();
                    ShowResult(true, result, "");
                    Debug.Log($"✅ [SendReceiveUI] Transaction completed: {result}");

                    if(NotificationManager.Instance != null)
                    {
#if TESTNET_BUILD
                        NotificationManager.Instance.SendNotification("Transaction Sent!", $"Sent {amt} BTCT to {txAddress.Substring(0, 6)}...");
#else
                        NotificationManager.Instance.SendNotification("Transaction Sent!", $"Sent {amt} BTC to {txAddress.Substring(0, 6)}...");
#endif
                    }
                    
                    StartCoroutine(RefreshBalanceAfterDelay(3f));
                }
                else
                {
                    ShowResult(false, "", result);
                    Debug.LogError($"❌ [SendReceiveUI] Transaction failed: {result}");
                }
            },
            privKeyToUse    // ✅ private key (empty string if seed wallet)
        );
    }
    
    // ✅ NEW: Helper method to refresh balance after transaction
    IEnumerator RefreshBalanceAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        Debug.Log("🔄 Auto-refreshing balance after transaction...");
        RefreshWalletStatus(); // Existing method
    }
    
    void BuildLoadingPanel()
    {
        GameObject canvas = GameObject.Find("Canvas");
        loadingPanel = MakePanel(canvas.transform, "Loader", 0, 0, REFERENCE_WIDTH, REFERENCE_HEIGHT, new Color(0,0,0,0.92f)); 
        
        // Block all interaction on background
        Image blocker = loadingPanel.GetComponent<Image>();
        if (blocker == null) blocker = loadingPanel.AddComponent<Image>();
        blocker.color = new Color(0, 0, 0, 0.92f);
        blocker.raycastTarget = true;
        
        // --- MODAL CARD ---
        GameObject modalCard = MakePanel(loadingPanel.transform, "ModalCard", 0, 0, 850, 450, new Color(0.12f, 0.12f, 0.18f));
        
        // Spinner/hourglass icon
        MakeText(modalCard.transform, "⌛", 0, 150, 80, Color.yellow, FontStyle.Normal);
        
        // Processing text
        loadingText = MakeText(modalCard.transform, LocalizationManager.Instance.Get("TX_PROCESSING_LONG"), 0, 30, 32, Color.white, FontStyle.Bold);
        loadingText.alignment = TextAnchor.MiddleCenter;
        loadingText.rectTransform.sizeDelta = new Vector2(750, 120);
        
        // Cancel button
        cancelButton = MakeButton(modalCard.transform, LocalizationManager.Instance.Get("TX_BTN_CANCEL"), 0, -120, 350, 90, () => {
            transactionCancelled = true;
            Debug.Log("[SendReceiveUI] User clicked cancel button");
            if (loadingPanel != null) loadingPanel.SetActive(false);
            ShowResult(false, "", LocalizationManager.Instance.Get("TX_CANCELLED"));
        });
        cancelButton.gameObject.name = "CancelButton";
        cancelButton.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f);
        Text cbTxt = cancelButton.GetComponentInChildren<Text>();
        if (cbTxt != null)
        {
            cbTxt.alignment = TextAnchor.MiddleCenter;
            cbTxt.rectTransform.anchorMin = Vector2.zero;
            cbTxt.rectTransform.anchorMax = Vector2.one;
            cbTxt.rectTransform.offsetMin = Vector2.zero;
            cbTxt.rectTransform.offsetMax = Vector2.zero;
        }
        
        // Start hidden (shown during 5-second countdown window)
        cancelButton.gameObject.SetActive(false);
    }
    
    void ShowResult(bool success, string hash, string error)
    {
        if(resultPanel == null) BuildResultPanel();
        resultPanel.SetActive(true);
        resultPanel.transform.SetAsLastSibling();
        
        if(success) {
            resultText.text = LocalizationManager.Instance.Get("TX_SUCCESS");
            resultText.color = Color.green;
            resultIcon.color = Color.green;
            resultHashText.text = LocalizationManager.Instance.Get("TX_HASH_PREFIX") + hash;
        } else {
            resultText.text = LocalizationManager.Instance.Get("TX_FAILED");
            resultText.color = Color.red;
            resultIcon.color = Color.red;
            resultHashText.text = error;
        }
    }
    
    void BuildResultPanel()
    {
        GameObject canvas = GameObject.Find("Canvas");
        resultPanel = MakePanel(canvas.transform, "Result", 0, 0, REFERENCE_WIDTH, REFERENCE_HEIGHT, new Color(0,0,0,0.9f));
        
        // Taller card to hold wrapped hash text
        GameObject card = MakePanel(resultPanel.transform, "Card", 0, 0, 940, 820, ThemeManager.Instance.GetCardColor());
        
        GameObject iconObj = MakeText(card.transform, "✔", 0, 340, 80, Color.green, FontStyle.Bold).gameObject;
        resultIcon = iconObj.GetComponent<Text>();
        
        GameObject tObj = MakeText(card.transform, LocalizationManager.Instance.Get("TX_SUCCESS_TITLE"), 0, 240, 40, Color.green, FontStyle.Bold).gameObject;
        resultText = tObj.GetComponent<Text>();
        
        // Hash label above the hash value
        MakeText(card.transform, LocalizationManager.Instance.Get("TX_HASH_LABEL"), 0, 160, 26,
            ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        
        // Hash value - wraps into 2-3 lines
        GameObject hObj = new GameObject("HashText");
        hObj.layer = 5;
        hObj.transform.SetParent(card.transform, false);
        RectTransform hRT = hObj.AddComponent<RectTransform>();
        hRT.anchoredPosition = new Vector2(0, 40);
        hRT.sizeDelta = new Vector2(860, 160); // Wide + tall enough for 3 lines
        Text hText = hObj.AddComponent<Text>();
        hText.font = GetFont();
        hText.fontSize = 22;
        hText.color = ThemeManager.Instance.GetSecondaryTextColor();
        hText.alignment = TextAnchor.UpperCenter;
        hText.horizontalOverflow = HorizontalWrapMode.Wrap;   // wrap long hash
        hText.verticalOverflow = VerticalWrapMode.Overflow;
        hText.raycastTarget = false;
        resultHashText = hText;
        
        MakeButton(card.transform, LocalizationManager.Instance.Get("TX_DONE"), 0, -280, 400, 100, () => resultPanel.SetActive(false));
    }

    void ShowStatus(string m, Color c) { if(statusText){ statusText.text=m; statusText.color=c; CancelInvoke("ClearStatus"); Invoke("ClearStatus",5f); }}
    void ClearStatus(){ if(statusText) statusText.text=""; }
    
    // --- RECEIVE & SCAN logic kept similar but condensed ---
    // ✅ Returns the correct receive address based on build variant
    string GetReceiveAddress(WalletData w)
    {
        if (w == null) return string.Empty;
#if TESTNET_BUILD
        bool isTestNet = true;
#else
        bool isTestNet = (w.isTestNet || (!string.IsNullOrEmpty(w.address) && w.address.StartsWith("tb1")));
#endif
        if (isTestNet)
        {
            // Use testnetAddress (tb1...) if available, otherwise derive it
            if (!string.IsNullOrEmpty(w.testnetAddress))
                return w.testnetAddress;
            if (!string.IsNullOrEmpty(w.address) && w.address.StartsWith("tb1"))
                return w.address;
            // Fallback: ensure testnet address is derived
            WalletManager2.Instance.EnsureTestnetAddress();
            w = WalletManager2.Instance.GetCurrentWallet();
            return !string.IsNullOrEmpty(w?.testnetAddress) ? w.testnetAddress : w?.address ?? string.Empty;
        }
        return w.address;
    }

    void OnShowReceive()
    {
        receivePanel.SetActive(true);
        WalletData w = WalletManager2.Instance.GetCurrentWallet();
        if (w != null)
        {
            string receiveAddr = GetReceiveAddress(w);
            // Update address text in receive panel
            Transform card = receivePanel.transform.Find("Card");
            if (card != null)
            {
                Transform addrTxt = card.Find("ReceiveAddress");
                if (addrTxt != null) addrTxt.GetComponent<Text>().text = receiveAddr;
                Transform copyBtn = card.Find("CopyBtn");
                if (copyBtn != null)
                {
                    Button b = copyBtn.GetComponent<Button>();
                    if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(() => GUIUtility.systemCopyBuffer = receiveAddr); }
                }
            }
            GenerateQRCode(receiveAddr);
        }
    }

    void CreateReceivePanel(Transform p)
    {
        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif
        float w = isDesktop ? 1920 : REFERENCE_WIDTH;
        float h = isDesktop ? 1080 : REFERENCE_HEIGHT;
        receivePanel = MakePanel(p, "Rec", 0, 0, w, h, new Color(0,0,0,0.95f));
        receivePanel.SetActive(false);
        GameObject c = MakePanel(receivePanel.transform, "Card", 0, 0, 900, 800, ThemeManager.Instance.GetCardColor());

        // Title
        MakeText(c.transform, LocalizationManager.Instance.Get("RECEIVE_TITLE"), 0, 350, 40, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // Network label  
#if TESTNET_BUILD
        MakeText(c.transform, "TestNet (tb1...)", 0, 295, 24, new Color(0.2f, 0.6f, 1f), FontStyle.Italic);
#else
        MakeText(c.transform, "MainNet (bc1...)", 0, 295, 24, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Italic);
#endif

        WalletData walletData = WalletManager2.Instance.GetCurrentWallet();
        string displayAddr = GetReceiveAddress(walletData);
        Text addrText = MakeText(c.transform, displayAddr != null ? displayAddr : "...", 0, 240, 22, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
        addrText.gameObject.name = "ReceiveAddress";
        addrText.horizontalOverflow = HorizontalWrapMode.Wrap;
        addrText.rectTransform.sizeDelta = new Vector2(820, 80);

        // Copy Button - named so we can update its listener
        Button copyBtn = MakeButton(c.transform, LocalizationManager.Instance.Get("BTN_COPY_ADDRESS"), 0, 150, 300, 80, () => {
            GUIUtility.systemCopyBuffer = GetReceiveAddress(WalletManager2.Instance.GetCurrentWallet());
        });
        copyBtn.gameObject.name = "CopyBtn";

        // QR Code
        GameObject q = new GameObject("QR");
        q.transform.SetParent(c.transform, false);
        q.AddComponent<RectTransform>().anchoredPosition3D = new Vector3(0, -100, 0);
        q.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 400);
        q.AddComponent<Image>();

        MakeButton(c.transform, LocalizationManager.Instance.Get("GEN_CLOSE"), 0, -350, 300, 80, () => receivePanel.SetActive(false));
    }
    void GenerateQRCode(string t){ 
        try{ 
            var w=new QRCodeWriter(); var m=w.encode(t,BarcodeFormat.QR_CODE,256,256);
            int W=m.Width, H=m.Height; var T=new Texture2D(W,H); 
            var P=new Color32[W*H]; for(int y=0;y<H;y++)for(int x=0;x<W;x++)P[y*W+x]=m[x,y]?Color.black:Color.white;
            T.SetPixels32(P); T.Apply();
            receivePanel.transform.Find("Card").Find("QR").GetComponent<Image>().sprite=Sprite.Create(T,new Rect(0,0,W,H),new Vector2(0.5f,0.5f));
        }catch{}
    }
    
    void StopCamera() 
    { 
        if(webCamTexture != null) webCamTexture.Stop(); 
    }

    void OnScanQR()
    {
        #if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            ShowStatus(LocalizationManager.Instance.Get("ERR_CAMERA_PERM"), Color.yellow);
            return;
        }
        #endif

        if (WebCamTexture.devices.Length == 0)
        {
            ShowStatus(LocalizationManager.Instance.Get("ERR_NO_CAMERA"), Color.red);
            return;
        }

        scanPanel.SetActive(true);
        isScanPanelVisible = true;
        
        webCamTexture = new WebCamTexture();
        cameraDisplay.texture = webCamTexture;
        webCamTexture.Play();
        
        // Wait briefly for camera to initialize properties
        StartCoroutine(ScanRoutine());
    }
    void CreateScanPanel(Transform p){
        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif
        float w = isDesktop ? 1920 : REFERENCE_WIDTH;
        float h = isDesktop ? 1080 : REFERENCE_HEIGHT;
        scanPanel = MakePanel(p,"Scan",0,0,w,h,Color.black); scanPanel.SetActive(false);
        GameObject c=MakePanel(scanPanel.transform,"Card",0,0,900,800, ThemeManager.Instance.GetCardColor());
        GameObject cam=new GameObject("Cam"); cam.transform.SetParent(c.transform,false); 
        cameraDisplay=cam.AddComponent<RawImage>(); cam.GetComponent<RectTransform>().sizeDelta=new Vector2(500,500);
        MakeButton(c.transform, LocalizationManager.Instance.Get("TX_BTN_CANCEL"),0,-350,300,80,()=>{isScanPanelVisible=false; scanPanel.SetActive(false); StopCamera();});
    }
    IEnumerator ScanRoutine(){
        var r=new ZXing.QrCode.QRCodeReader();
        
        // --- ROTATION FIX START ---
        // Wait until we have valid dimensions
        while(isScanPanelVisible && webCamTexture.isPlaying && webCamTexture.width < 100) 
            yield return null;

        if(isScanPanelVisible && webCamTexture.isPlaying)
        {
            int angle = webCamTexture.videoRotationAngle;
            bool mirrored = webCamTexture.videoVerticallyMirrored;
            
            RectTransform rt = cameraDisplay.rectTransform;
            
            // Allow rotation
            rt.localEulerAngles = new Vector3(0, 0, -angle);
            
            // Handle mirroring and aspect ratio
            float scaleY = mirrored ? -1f : 1f;
            rt.localScale = new Vector3(1f, scaleY, 1f);
            
            // Adjust size to fit container if rotated 90/270
            // (Optional refinement, but basic rotation is key request)
        }
        // --- ROTATION FIX END ---

        while(isScanPanelVisible&&webCamTexture.isPlaying){
            try{ var res=r.decode(new BinaryBitmap(new HybridBinarizer(new Color32LuminanceSource(webCamTexture.GetPixels32(),webCamTexture.width,webCamTexture.height))));
                 if(res!=null){ sendAddressInput.text=res.Text; isScanPanelVisible=false; scanPanel.SetActive(false); StopCamera(); }
            }catch{} yield return new WaitForSeconds(0.5f);
        }
    }
    
    void ShowAddressBookOverlay()
    {
        if (addressBookOverlay != null) { Destroy(addressBookOverlay); }
        
        GameObject canvas = GameObject.Find("Canvas");
        addressBookOverlay = MakePanel(canvas.transform, "AddrBookOverlay", 0, 0, REFERENCE_WIDTH, REFERENCE_HEIGHT, new Color(0, 0, 0, 0.9f));
        
        GameObject card = MakePanel(addressBookOverlay.transform, "Card", 0, 50, 950, 1400, ThemeManager.Instance.GetCardColor());
        
        MakeText(card.transform, LocalizationManager.Instance.Get("ADDR_BOOK_TITLE"), 0, 600, 42, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        
        // List saved addresses
        var entries = AddressBookManager.Instance.GetEntries();
        float y = 450;
        
        if (entries.Count == 0)
        {
            MakeText(card.transform, LocalizationManager.Instance.Get("ADDR_BOOK_EMPTY"), 0, y, 28, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Italic);
        }
        else
        {
            for (int i = 0; i < entries.Count && i < 6; i++)
            {
                var entry = entries[i];
                string displayAddr = entry.address.Length > 18 ? entry.address.Substring(0, 18) + "..." : entry.address;
                string displayName = string.IsNullOrEmpty(entry.name) ? "Unnamed" : entry.name;
                
                // Name
                MakeText(card.transform, displayName, -250, y, 28, ThemeManager.Instance.GetTextColor(), FontStyle.Bold).GetComponent<RectTransform>().sizeDelta = new Vector2(400, 40);
                // Address
                MakeText(card.transform, displayAddr, -250, y - 30, 22, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal).GetComponent<RectTransform>().sizeDelta = new Vector2(400, 30);
                
                // Use button
                string capturedAddr = entry.address;
                MakeButton(card.transform, LocalizationManager.Instance.Get("BTN_USE"), 280, y - 10, 120, 60, () => {
                    sendAddressInput.text = capturedAddr;
                    Destroy(addressBookOverlay);
                }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
                
                // Delete button
                MakeButton(card.transform, "✕", 380, y - 10, 60, 60, () => {
                    AddressBookManager.Instance.DeleteEntry(capturedAddr);
                    Destroy(addressBookOverlay);
                    ShowAddressBookOverlay(); // Refresh
                }).GetComponent<Image>().color = ThemeManager.Instance.GetErrorColor();
                
                y -= 100;
            }
        }
        
        // --- Add New Section ---
        y -= 40;
        MakeText(card.transform, LocalizationManager.Instance.Get("ADDR_BOOK_ADD"), 0, y, 32, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        y -= 70;
        
        InputField nameInput = MakeInput(card.transform, 0, y, 850, 80, LocalizationManager.Instance.Get("ADDR_BOOK_NAME"));
        y -= 100;
        InputField addrInput = MakeInput(card.transform, 0, y, 850, 80, LocalizationManager.Instance.Get("ADDR_BOOK_ADDR"));
        
        // Pre-fill with current send address if available
        if (!string.IsNullOrEmpty(sendAddressInput.text))
        {
            addrInput.text = sendAddressInput.text;
        }
        
        y -= 100;
        MakeButton(card.transform, LocalizationManager.Instance.Get("ADDR_BOOK_ADD"), 0, y, 400, 90, () => {
            if (!string.IsNullOrEmpty(addrInput.text))
            {
                AddressBookManager.Instance.AddEntry(nameInput.text, addrInput.text);
                ShowStatus(LocalizationManager.Instance.Get("ADDR_BOOK_SAVED"), new Color(1.0f, 0.6f, 0.0f));
                Destroy(addressBookOverlay);
                ShowAddressBookOverlay(); // Refresh
            }
        }).GetComponent<Image>().color = ThemeManager.Instance.GetSuccessColor();
        
        // Close
        MakeButton(card.transform, LocalizationManager.Instance.Get("GEN_CLOSE"), 0, -620, 400, 90, () => {
            Destroy(addressBookOverlay);
        }).GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.4f);
        
        addressBookOverlay.transform.SetAsLastSibling();
    }

    void UpdateAddressBookUI(){
        foreach(Transform t in addressBookContainer.transform) if(t.name.Contains("Row")) Destroy(t.gameObject);
        WalletData w=WalletManager2.Instance.GetCurrentWallet(); if(w==null||w.transactions==null)return;
        HashSet<string> seen=new HashSet<string>();
        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif
        float y = isDesktop ? 90 : 70;
        float spacing = isDesktop ? 70 : 60;
        int maxItems = isDesktop ? 4 : 3;
        int c=0;
        for(int i=w.transactions.Count-1;i>=0;i--){
            var tx=w.transactions[i];
            if(tx.type=="Send"&&!seen.Contains(tx.address)){
                seen.Add(tx.address);
                GameObject r=new GameObject("Row"); r.layer=5; r.transform.SetParent(addressBookContainer.transform,false);
                r.AddComponent<RectTransform>().anchoredPosition3D=new Vector3(0,y,0);
                if (isDesktop)
                {
                    MakeText(r.transform,tx.address.Length>30?tx.address.Substring(0,24)+"..."+tx.address.Substring(tx.address.Length-6):tx.address,-100,0,26,ThemeManager.Instance.GetAccentColor(),FontStyle.Normal).GetComponent<RectTransform>().sizeDelta=new Vector2(600,60);
                    MakeButton(r.transform, LocalizationManager.Instance.Get("BTN_USE"),280,0,120,50,()=>sendAddressInput.text=tx.address);
                }
                else
                {
                    MakeText(r.transform,tx.address.Length>20?tx.address.Substring(0,20):tx.address,-100,0,26,ThemeManager.Instance.GetAccentColor(),FontStyle.Normal).GetComponent<RectTransform>().sizeDelta=new Vector2(600,60);
                    MakeButton(r.transform, LocalizationManager.Instance.Get("BTN_USE"),350,0,120,50,()=>sendAddressInput.text=tx.address);
                }
                y-=spacing; c++; if(c>=maxItems)break;
            }
        }
    }
}
