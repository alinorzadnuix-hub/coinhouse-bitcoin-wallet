using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.Collections;
using System.IO;
using NBitcoin;

public class FinalWalletUI : MonoBehaviour
{
    private BitcoinWalletCore wallet;
    
    private Text statusText;
    private GameObject backupOverlay; // Full-screen backup keys overlay
    private string generatedSeedPhrase = "";
    private string generatedPrivateKeyWIF = "";
    private string generatedPrivateKeyHex = "";

    void Start()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
        
        BuildUI();
    }

    void BuildUI()
    {
        GameObject canvasObj = new GameObject("Canvas");
        canvasObj.layer = 5;
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        
        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
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
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
        }
        
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        // Background
        GameObject bg = new GameObject("BG");
        bg.layer = 5;
        bg.transform.SetParent(canvas.transform, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;
        bg.AddComponent<Image>().color = ThemeManager.Instance.GetBackgroundColor();

        // Title Position (Comfortably below top of screen, completely clear of card and buttons)
        float titleY = isDesktop ? 390f : 720f;
        
        // Title — TestNet build shows the Bitcoin Tap Wallet branding
#if TESTNET_BUILD
        string createTitle = "Satolite Wallet - Bitcoin Wallet";
#else
        string createTitle = LocalizationManager.Instance.Get("CREATE_TITLE");
#endif
        MakeText(canvas.transform, createTitle, 0, titleY, isDesktop ? 44 : 54, ThemeManager.Instance.GetTextColor());

        // Card Panel dimensions for the 2 action buttons
        float cardW = isDesktop ? 680f : 940f;
        float cardH = isDesktop ? 300f : 370f;
        float cardY = isDesktop ? 60f : 300f;

        // Card panel directly on canvas
        GameObject panel = MakeCard(canvas.transform, 0, cardY, cardW, cardH);

        // Layout buttons cleanly inside the card panel
        float btnW = isDesktop ? 580f : 860f;
        float btnH1 = isDesktop ? 80f : 95f;   // Generate button
        float btnH2 = isDesktop ? 75f : 85f;   // Recover button
        float gap = isDesktop ? 22f : 28f;

        float totalH = btnH1 + gap + btnH2;
        float btn1Y = (totalH / 2f) - (btnH1 / 2f);
        float btn2Y = btn1Y - (btnH1 / 2f) - gap - (btnH2 / 2f);

        // 1. Generate Button
        MakeButton(panel.transform, LocalizationManager.Instance.Get("CREATE_BTN_GEN"), 0, btn1Y, btnW, btnH1, GenerateWallet);

        // 2. Recovery Button
        MakeButtonWithColor(panel.transform, "🔑 " + LocalizationManager.Instance.Get("CREATE_BTN_REC"), 0, btn2Y, btnW, btnH2, 
            new Color(0.2f, 0.5f, 0.9f),
            () => {
                SceneManager.LoadScene("RecoverWallet");
            });

        // Status text - positioned at bottom of screen
        statusText = MakeText(canvas.transform, "", 0, isDesktop ? -400 : -850, 30, Color.yellow);

        // Back Button
        if (isDesktop)
        {
            MakeButton(canvas.transform, LocalizationManager.Instance.Get("GEN_BACK"), -800, 380, 200, 70, () => {
                GoBackToMain();
            });
        }
        else
        {
            MakeButton(canvas.transform, LocalizationManager.Instance.Get("GEN_BACK"), -400, 850, 280, 100, () => {
                GoBackToMain();
            });
        }
    }

    // ===== BACKUP YOUR KEYS OVERLAY (shown after wallet generation) =====
    void BuildBackupOverlay(string seedPhrase, string wifKey, string hexKey)
    {
        ScreenSecurityManager.EnableProtection();
        GameObject canvas = GameObject.Find("Canvas");
        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif
        
        // Full-screen dark overlay
        backupOverlay = new GameObject("BackupOverlay");
        backupOverlay.layer = 5;
        backupOverlay.transform.SetParent(canvas.transform, false);
        RectTransform overlayRT = backupOverlay.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;
        backupOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.95f);
        
        // --- Card Background (Centered, No Scroll) ---
        GameObject card = new GameObject("Card");
        card.layer = 5;
        card.transform.SetParent(backupOverlay.transform, false);
        RectTransform cardRT = card.AddComponent<RectTransform>();
        cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot = new Vector2(0.5f, 0.5f);
        cardRT.anchoredPosition = Vector2.zero;
        if (isDesktop)
            cardRT.sizeDelta = new Vector2(1000, 950);
        else
            cardRT.sizeDelta = new Vector2(940, 1450);
        card.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.14f);
        
        // Close Button (X) in top right of card
        if (isDesktop)
        {
            MakeButtonWithColor(card.transform, "✕", 450, 420, 70, 70, 
                new Color(0.3f, 0.3f, 0.35f, 0.8f), () => {
                    ScreenSecurityManager.DisableProtection();
                    Destroy(backupOverlay);
                    SceneManager.LoadScene("MainWallet");
                });
        }
        else
        {
            MakeButtonWithColor(card.transform, "✕", 420, 680, 80, 80, 
                new Color(0.3f, 0.3f, 0.35f, 0.8f), () => {
                    ScreenSecurityManager.DisableProtection();
                    Destroy(backupOverlay);
                    SceneManager.LoadScene("MainWallet");
                });
        }

        float y = isDesktop ? 390 : 620;
        
        // ===== TITLE SECTION =====
        Text titleText = MakeText(card.transform, LocalizationManager.Instance.Get("BACKUP_KEYS_TITLE"), 0, y, isDesktop ? 34 : 42, Color.white);
        titleText.fontStyle = FontStyle.Bold;
        y -= isDesktop ? 50 : 60;
        
        Text subtitleText = MakeText(card.transform, LocalizationManager.Instance.Get("BACKUP_KEYS_MESSAGE"), 0, y, isDesktop ? 22 : 26, new Color(0.7f, 0.7f, 0.75f));
        subtitleText.rectTransform.sizeDelta = new Vector2(850, isDesktop ? 50 : 80);
        y -= isDesktop ? 60 : 80;
        
        // ===== 12 SEED WORDS SECTION =====
        Text seedTitle = MakeText(card.transform, LocalizationManager.Instance.Get("BACKUP_SEED_WORDS"), -200, y, isDesktop ? 26 : 32, Color.white);
        seedTitle.fontStyle = FontStyle.Bold;
        seedTitle.alignment = TextAnchor.MiddleLeft;
        seedTitle.rectTransform.sizeDelta = new Vector2(500, 50);
        
        string capturedSeed = seedPhrase;
        MakeButtonWithColor(card.transform, "📋 " + LocalizationManager.Instance.Get("GEN_COPY"), 340, y, 180, isDesktop ? 50 : 60,
            new Color(0.2f, 0.45f, 0.9f), () => {
                GUIUtility.systemCopyBuffer = capturedSeed;
                ShowCopyFeedback(LocalizationManager.Instance.Get("SEED_COPIED"));
            });
        y -= isDesktop ? 50 : 60;
        
        string[] words = seedPhrase.Split(' ');
        GameObject gridBorder = new GameObject("GridBorder");
        gridBorder.layer = 5;
        gridBorder.transform.SetParent(card.transform, false);
        RectTransform gridBorderRT = gridBorder.AddComponent<RectTransform>();
        if (isDesktop)
        {
            gridBorderRT.anchoredPosition = new Vector2(0, y - 110);
            gridBorderRT.sizeDelta = new Vector2(880, 220);
        }
        else
        {
            gridBorderRT.anchoredPosition = new Vector2(0, y - 180);
            gridBorderRT.sizeDelta = new Vector2(880, 370);
        }
        gridBorder.AddComponent<Image>().color = new Color(0.6f, 0.5f, 0.2f, 0.4f);
        
        GameObject gridInner = new GameObject("GridInner");
        gridInner.layer = 5;
        gridInner.transform.SetParent(gridBorder.transform, false);
        RectTransform gridInnerRT = gridInner.AddComponent<RectTransform>();
        gridInnerRT.anchorMin = Vector2.zero;
        gridInnerRT.anchorMax = Vector2.one;
        gridInnerRT.offsetMin = new Vector2(3, 3);
        gridInnerRT.offsetMax = new Vector2(-3, -3);
        gridInner.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.16f);
        
        PopulateSeedGrid(gridInner, words);
        y -= isDesktop ? 240 : 410;
        
        // ===== PRIVATE KEY (WIF) SECTION =====
        Text wifTitle = MakeText(card.transform, LocalizationManager.Instance.Get("BACKUP_WIF_KEY"), -200, y, isDesktop ? 26 : 32, Color.white);
        wifTitle.fontStyle = FontStyle.Bold;
        wifTitle.alignment = TextAnchor.MiddleLeft;
        wifTitle.rectTransform.sizeDelta = new Vector2(500, 50);
        
        string capturedWIF = wifKey;
        MakeButtonWithColor(card.transform, "📋 " + LocalizationManager.Instance.Get("GEN_COPY"), 340, y, 180, isDesktop ? 50 : 60,
            new Color(0.2f, 0.45f, 0.9f), () => {
                GUIUtility.systemCopyBuffer = capturedWIF;
                ShowCopyFeedback(LocalizationManager.Instance.Get("WIF_COPIED"));
            });
        y -= isDesktop ? 50 : 60;
        
        GameObject wifBox = new GameObject("WIFBox");
        wifBox.layer = 5;
        wifBox.transform.SetParent(card.transform, false);
        RectTransform wifBoxRT = wifBox.AddComponent<RectTransform>();
        wifBoxRT.anchoredPosition = new Vector2(0, y - (isDesktop ? 15 : 25));
        wifBoxRT.sizeDelta = new Vector2(880, isDesktop ? 60 : 80);
        wifBox.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
        
        Text wifText = MakeText(wifBox.transform, wifKey, 0, 0, isDesktop ? 18 : 22, new Color(0.7f, 0.85f, 1f));
        wifText.alignment = TextAnchor.MiddleLeft;
        wifText.rectTransform.anchorMin = Vector2.zero;
        wifText.rectTransform.anchorMax = Vector2.one;
        wifText.rectTransform.offsetMin = new Vector2(15, 5);
        wifText.rectTransform.offsetMax = new Vector2(-15, -5);
        wifText.rectTransform.sizeDelta = Vector2.zero;
        wifText.horizontalOverflow = HorizontalWrapMode.Wrap;
        
        y -= isDesktop ? 90 : 110;
        
        // ===== PRIVATE KEY (HEX) SECTION =====
        Text hexTitle = MakeText(card.transform, LocalizationManager.Instance.Get("BACKUP_HEX_KEY"), -200, y, isDesktop ? 26 : 32, Color.white);
        hexTitle.fontStyle = FontStyle.Bold;
        hexTitle.alignment = TextAnchor.MiddleLeft;
        hexTitle.rectTransform.sizeDelta = new Vector2(500, 50);
        
        string capturedHex = hexKey;
        MakeButtonWithColor(card.transform, "📋 " + LocalizationManager.Instance.Get("GEN_COPY"), 340, y, 180, isDesktop ? 50 : 60,
            new Color(0.2f, 0.45f, 0.9f), () => {
                GUIUtility.systemCopyBuffer = capturedHex;
                ShowCopyFeedback(LocalizationManager.Instance.Get("HEX_COPIED"));
            });
        y -= isDesktop ? 50 : 60;
        
        GameObject hexBox = new GameObject("HexBox");
        hexBox.layer = 5;
        hexBox.transform.SetParent(card.transform, false);
        RectTransform hexBoxRT = hexBox.AddComponent<RectTransform>();
        hexBoxRT.anchoredPosition = new Vector2(0, y - (isDesktop ? 15 : 25));
        hexBoxRT.sizeDelta = new Vector2(880, isDesktop ? 60 : 80);
        hexBox.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
        
        Text hexText = MakeText(hexBox.transform, hexKey, 0, 0, isDesktop ? 18 : 22, new Color(0.7f, 0.85f, 1f));
        hexText.alignment = TextAnchor.MiddleLeft;
        hexText.rectTransform.anchorMin = Vector2.zero;
        hexText.rectTransform.anchorMax = Vector2.one;
        hexText.rectTransform.offsetMin = new Vector2(15, 5);
        hexText.rectTransform.offsetMax = new Vector2(-15, -5);
        hexText.rectTransform.sizeDelta = Vector2.zero;
        hexText.horizontalOverflow = HorizontalWrapMode.Wrap;
        
        y -= isDesktop ? 110 : 130;
        
        // ===== "I HAVE SAVED MY KEYS" BUTTON =====
        MakeButtonWithColor(card.transform, LocalizationManager.Instance.Get("BACKUP_CONFIRM"), 0, y, isDesktop ? 600 : 750, isDesktop ? 90 : 110,
            new Color(0.15f, 0.4f, 0.85f), () => {
                ScreenSecurityManager.DisableProtection();
                if (WalletManager2.Instance.HasWallet())
                {
                    WalletData data = WalletManager2.Instance.GetCurrentWallet();
                    data.hasShownKeys = true;
                    try {
                        string json = JsonUtility.ToJson(data, true);
                        string savePath = Application.persistentDataPath + "/wallet_mainnet.json";
                        File.WriteAllText(savePath, json);
                    } catch (System.Exception ex) { Debug.LogWarning("Save flag error: " + ex.Message); }
                }
                SceneManager.LoadScene("MainWallet");
            });
        
        backupOverlay.transform.SetAsLastSibling();
    }
    
    // Copy feedback helper
    private Text copyFeedbackText;
    
    void ShowCopyFeedback(string message)
    {
        if (copyFeedbackText == null && backupOverlay != null)
        {
            copyFeedbackText = MakeText(backupOverlay.transform, "", 0, -900, 28, Color.green);
            copyFeedbackText.fontStyle = FontStyle.Bold;
        }
        if (copyFeedbackText != null)
        {
            copyFeedbackText.text = "✅ " + message;
            copyFeedbackText.color = Color.green;
            CancelInvoke("ClearCopyFeedback");
            Invoke("ClearCopyFeedback", 2f);
        }
    }
    
    void ClearCopyFeedback()
    {
        if (copyFeedbackText != null) copyFeedbackText.text = "";
    }

    // ===== HELPER METHODS =====

    Text MakeText(Transform parent, string txt, float x, float y, int size, Color col, float width = 1000f, float height = 80f)
    {
        GameObject obj = new GameObject("Text");
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(width, height);
        
        Text text = obj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt);
        text.fontSize = size;
        text.color = col;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        
        return text;
    }

    GameObject MakeCard(Transform parent, float x, float y, float w, float h)
    {
        GameObject card = new GameObject("Card");
        card.layer = 5;
        card.transform.SetParent(parent, false);
        RectTransform rt = card.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        Image img = card.AddComponent<Image>();
        img.color = ThemeManager.Instance.GetCardColor();
        return card;
    }

    void MakeButton(Transform parent, string txt, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
    {
        MakeButtonWithColor(parent, txt, x, y, w, h, ThemeManager.Instance.GetAccentColor(), action);
    }
    
    Button MakeButtonWithColor(Transform parent, string txt, float x, float y, float w, float h, Color color, UnityEngine.Events.UnityAction action)
    {
        GameObject obj = new GameObject("Btn_" + txt);
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        
        Image img = obj.AddComponent<Image>();
        img.color = color;
        
        Button btn = obj.AddComponent<Button>();
        
        if (txt == LocalizationManager.Instance.Get("GEN_BACK"))
            btn.onClick.AddListener(() => AudioManager.Instance.PlayBack());
        else if (txt == LocalizationManager.Instance.Get("MODAL_CLOSE"))
            btn.onClick.AddListener(() => AudioManager.Instance.PlayCloseMenu());
        else if (txt != null && txt.Contains(LocalizationManager.Instance.Get("GEN_COPY")))
            btn.onClick.AddListener(() => AudioManager.Instance.PlayCopy());
        else
            btn.onClick.AddListener(() => AudioManager.Instance.PlayClick());

        btn.onClick.AddListener(action);
        
        GameObject txtObj = new GameObject("Text");
        txtObj.layer = 5;
        txtObj.transform.SetParent(obj.transform, false);
        
        RectTransform txtRT = txtObj.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
        
        Text text = txtObj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt);
        text.fontSize = 38;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.fontStyle = FontStyle.Bold;
        
        return btn;
    }

    void GenerateWallet()
    {
        statusText.text = LocalizationManager.Instance.Get("GEN_LOADING");
        statusText.color = Color.yellow;
        
        try
        {
            Debug.Log("=== Starting Wallet Generation ===");
            
#if TESTNET_BUILD
            NBitcoin.Network network = NBitcoin.Network.TestNet;
            Debug.Log("Network: TestNet");
#else
            NBitcoin.Network network = NBitcoin.Network.Main;
            Debug.Log("Network: MainNet");
#endif
            
            wallet = new BitcoinWalletCore(network);
            Debug.Log("Wallet created");
            
            // Use SegWit address (Bech32 format - bc1...)
            string address = wallet.GetSegWitAddress(0);
            Debug.Log($"SegWit Address: {address}");
            
            // Use BIP84 (SegWit) private keys to match the SegWit address
            string privateKey = wallet.GetSegWitPrivateKey(0);
            Debug.Log($"Private Key (WIF): {privateKey}");
            
            string privateKeyHex = wallet.GetSegWitPrivateKeyHex(0);
            Debug.Log($"Private Key (Hex): {privateKeyHex}");
            
            string seedPhrase = wallet.GetSeedPhrase();
            Debug.Log($"Seed Phrase: {seedPhrase}");
            
            string[] words = seedPhrase.Split(' ');
            Debug.Log($"Word count: {words.Length}");
            
            // Save wallet with SegWit address as main address
#if TESTNET_BUILD
            bool isGenTestNet = true;
#else
            bool isGenTestNet = false;
#endif
            WalletManager2.Instance.SaveWallet(seedPhrase, isGenTestNet, address, privateKey);
            
            // Store generated keys
            generatedSeedPhrase = seedPhrase;
            generatedPrivateKeyWIF = privateKey;
            generatedPrivateKeyHex = privateKeyHex;
            
            statusText.text = LocalizationManager.Instance.Get("GEN_SUCCESS");
            statusText.color = Color.green;
            
            Debug.Log("=== SegWit Wallet Generated and Saved Successfully ===");
            
            // ✅ Show full-screen BACKUP YOUR KEYS overlay
            BuildBackupOverlay(seedPhrase, privateKey, privateKeyHex);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ERROR: {ex.Message}");
            Debug.LogError($"Stack: {ex.StackTrace}");
            
            statusText.text = $"❌ {LocalizationManager.Instance.Get("GEN_ERROR")}: {ex.Message}";
            statusText.color = Color.red;
        }
    }

    void GoBackToMain()
    {
        Debug.Log("Back button clicked! Going to MainWallet");
        try
        {
            SceneManager.LoadScene("MainWallet");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Scene load error: {ex.Message}");
        }
    }

    // ✅ Populate a container with seed words in a proper 2-column UI grid
    void PopulateSeedGrid(GameObject container, string[] words)
    {
        // Clear existing children
        for (int c = container.transform.childCount - 1; c >= 0; c--)
        {
            Destroy(container.transform.GetChild(c).gameObject);
        }

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        RectTransform containerRT = container.GetComponent<RectTransform>();
        float containerH = containerRT.rect.height;
        if (containerH <= 0) containerH = isDesktop ? 220 : 380; // fallback

        int half = (words.Length + 1) / 2; // 6 for 12 words
        float cellW = isDesktop ? 380f : 400f;
        float cellH = isDesktop ? 30f : 52f;
        float gapX = 20f;
        float gapY = isDesktop ? 4f : 8f;
        float startY = containerH / 2 - (isDesktop ? 10 : 20);

        Color numColor = new Color(0.5f, 0.5f, 0.6f);
        Color wordColor = new Color(1f, 0.75f, 0.2f); // Gold/Orange
        Color cellBgColor = new Color(0.12f, 0.12f, 0.18f);

        float col0X = -(cellW / 2 + gapX / 2);
        float col1X = (cellW / 2 + gapX / 2);

        for (int i = 0; i < words.Length; i++)
        {
            int col = i < half ? 0 : 1;
            int row = i < half ? i : i - half;

            float xPos = col == 0 ? col0X : col1X;
            float yPos = startY - row * (cellH + gapY) - cellH / 2;

            // Cell background
            GameObject cell = new GameObject($"Cell_{i}");
            cell.layer = 5;
            cell.transform.SetParent(container.transform, false);
            RectTransform cellRT = cell.AddComponent<RectTransform>();
            cellRT.anchoredPosition = new Vector2(xPos, yPos);
            cellRT.sizeDelta = new Vector2(cellW, cellH);
            Image cellImg = cell.AddComponent<Image>();
            cellImg.color = cellBgColor;

            // Number text (left side)
            Text numText = MakeText(cell.transform, $"{i + 1}.", 0, 0, isDesktop ? 16 : 22, numColor);
            numText.alignment = TextAnchor.MiddleLeft;
            numText.rectTransform.anchorMin = new Vector2(0, 0);
            numText.rectTransform.anchorMax = new Vector2(0, 1);
            numText.rectTransform.pivot = new Vector2(0, 0.5f);
            numText.rectTransform.anchoredPosition = new Vector2(12, 0);
            numText.rectTransform.sizeDelta = new Vector2(45, 0);

            // Word text (right of number)
            Text wordText = MakeText(cell.transform, words[i], 0, 0, isDesktop ? 20 : 26, wordColor);
            wordText.alignment = TextAnchor.MiddleLeft;
            wordText.fontStyle = FontStyle.Bold;
            wordText.rectTransform.anchorMin = new Vector2(0, 0);
            wordText.rectTransform.anchorMax = new Vector2(1, 1);
            wordText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            wordText.rectTransform.offsetMin = new Vector2(55, 2);
            wordText.rectTransform.offsetMax = new Vector2(-8, -2);
        }
    }

    void GoToMain()
    {
        SceneManager.LoadScene("MainWallet");
    }

    void OnDestroy()
    {
        ScreenSecurityManager.DisableProtection();
    }
}
