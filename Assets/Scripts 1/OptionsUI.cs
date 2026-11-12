using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using ZXing;
using ZXing.QrCode;

public class OptionsUI : MonoBehaviour
{
    // UI
    private const float REFERENCE_WIDTH = 1080f;
    private const float REFERENCE_HEIGHT = 1920f;
    
    private Text feedbackText;
    private Text broadcastApiBtnText;

    void Start()
    {
        BuildUI();
    }

    void BuildUI()
    {
        // Ensure EventSystem exists
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        GameObject canvasObj = new GameObject("Canvas");
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

        // THEME: BG
        if (isDesktop)
        {
            MakePanel(canvas.transform, "BG", 0, 0, 1920, 1080, ThemeManager.Instance.GetBackgroundColor());
        }
        else
        {
            MakePanel(canvas.transform, "BG", 0, 0, REFERENCE_WIDTH, REFERENCE_HEIGHT, ThemeManager.Instance.GetBackgroundColor());
        }

        // Top Bar
        float topBarH = isDesktop ? 140f : 200f;
        float safeTopPad = isDesktop ? 20f : 75f;
        float topBarCenterY = -(safeTopPad + (topBarH - safeTopPad) / 2f);

        GameObject topBar = MakePanel(canvas.transform, "TopBar", 0, 0, isDesktop ? 1920 : REFERENCE_WIDTH, topBarH, ThemeManager.Instance.GetCardColor());
        RectTransform topBarRT = topBar.GetComponent<RectTransform>();
        topBarRT.anchorMin = new Vector2(0, 1);
        topBarRT.anchorMax = new Vector2(1, 1);
        topBarRT.pivot = new Vector2(0.5f, 1);
        topBarRT.anchoredPosition = Vector2.zero;
        topBarRT.sizeDelta = new Vector2(0, topBarH);

        if (isDesktop)
        {
            MakeButton(topBar.transform, LocalizationManager.Instance.Get("GEN_BACK"), -800, topBarCenterY, 200, 70, () => SceneManager.LoadScene("MainWallet"))
                .GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
        }
        else
        {
            MakeButton(topBar.transform, LocalizationManager.Instance.Get("GEN_BACK"), -400, topBarCenterY, 200, 80, () => SceneManager.LoadScene("MainWallet"))
                .GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
        }
        
        MakeText(topBar.transform, LocalizationManager.Instance.Get("MENU_TITLE"), 0, topBarCenterY, isDesktop ? 36 : 40, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // Content
        float y = isDesktop ? 260f : 490f;
        float spacing = isDesktop ? 95f : 115f;

        // 0. Address Log (New)
        MakeButton(canvas.transform, LocalizationManager.Instance.Get("MENU_ADDR_LOG"), 0, y, isDesktop ? 600 : 800, isDesktop ? 95 : 115, () => SceneManager.LoadScene("Address Log"))
             .GetComponent<Image>().color = new Color(1f, 0.8f, 0.2f); // Gold/Yellowish
        y -= spacing;

        // 0.5 Multisig Vault
        MakeButton(canvas.transform, LocalizationManager.Instance.Get("MENU_MULTISIG"), 0, y, isDesktop ? 600 : 800, isDesktop ? 95 : 115, () => {
            var existingVault = FindAnyObjectByType<MultisigVaultUI>();
            if (existingVault != null) Destroy(existingVault.gameObject);
            GameObject vault = new GameObject("MultisigVaultController");
            vault.AddComponent<MultisigVaultUI>();
        }).GetComponent<Image>().color = new Color(0.12f, 0.42f, 0.75f);
        y -= spacing;
        // 0.6 Emergency Passphrase Reveal - One-time option
        if (!HasEmergencyRevealBeenUsed())
        {
            MakeButton(canvas.transform, "🔑 " + (LocalizationManager.Instance?.Get("MENU_EMERGENCY_REVEAL") ?? "Emergency Passphrase Reveal"), 0, y, isDesktop ? 600 : 800, isDesktop ? 95 : 115, ShowEmergencyRevealConfirmDialog)
                .GetComponent<Image>().color = new Color(0.9f, 0.45f, 0.05f);
            y -= spacing;
        }

        // 1. Change Theme
        string themeBtnText = ThemeManager.Instance.IsDarkMode ? LocalizationManager.Instance.Get("MENU_THEME_LIGHT") : LocalizationManager.Instance.Get("MENU_THEME_DARK");
        MakeButton(canvas.transform, themeBtnText, 0, y, isDesktop ? 600 : 800, isDesktop ? 95 : 115, OnToggleTheme);
        y -= spacing;

        // 1.5 Broadcast API Preference (Mempool / Blockstream / BlockCypher)
        Button apiBtn = MakeButton(canvas.transform, GetBroadcastApiDisplay(), 0, y, isDesktop ? 600 : 800, isDesktop ? 95 : 115, OnToggleBroadcastApi);
        apiBtn.GetComponent<Image>().color = new Color(0.18f, 0.52f, 0.45f);
        broadcastApiBtnText = apiBtn.GetComponentInChildren<Text>();
        y -= spacing;

        // 2. Change Password
        MakeButton(canvas.transform, LocalizationManager.Instance.Get("MENU_PIN"), 0, y, isDesktop ? 600 : 800, isDesktop ? 95 : 115, OnResetPin)
            .GetComponent<Image>().color = new Color(0.3f, 0.4f, 0.5f);
        y -= spacing;

        // 3. Create New Wallet
        MakeButton(canvas.transform, LocalizationManager.Instance.Get("MENU_CREATE"), 0, y, isDesktop ? 600 : 800, isDesktop ? 95 : 115, OnCreateNewWallet)
             .GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.3f);
        y -= spacing;
        
        float feedbackY = y - 40f;
        feedbackText = MakeText(canvas.transform, "", 0, feedbackY, 32, ThemeManager.Instance.GetAccentColor(), FontStyle.Italic);

        // App Version & Serial display
        float serialY = isDesktop ? -480f : -820f;
        MakeText(canvas.transform, SatoliteSerialService.GetVersionSerialString(), 0, serialY, isDesktop ? 18 : 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
    }
    
    void OnToggleTheme()
    {
        ThemeManager.Instance.ToggleTheme();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); // Reload to apply
    }

    private string GetBroadcastApiDisplay()
    {
        string pref = PlayerPrefs.GetString("Pref_Send_API", "mempool").ToLowerInvariant();
        if (pref == "blockstream") return "Broadcast API: Blockstream.info";
        if (pref == "blockcypher") return "Broadcast API: BlockCypher";
        return "Broadcast API: Mempool.space (Default)";
    }

    private void OnToggleBroadcastApi()
    {
        string pref = PlayerPrefs.GetString("Pref_Send_API", "mempool").ToLowerInvariant();
        string next = "mempool";
        if (pref == "mempool") next = "blockstream";
        else if (pref == "blockstream") next = "blockcypher";
        else next = "mempool";

        PlayerPrefs.SetString("Pref_Send_API", next);
        PlayerPrefs.Save();
        AudioManager.Instance?.PlayClick();

        if (broadcastApiBtnText != null)
        {
            broadcastApiBtnText.text = GetBroadcastApiDisplay();
        }
        if (feedbackText != null)
        {
            feedbackText.text = $"Send API set to {next.ToUpper()}";
            feedbackText.color = ThemeManager.Instance.GetAccentColor();
            CancelInvoke("ClearFeedback");
            Invoke("ClearFeedback", 2.5f);
        }
    }
    
    void OnCreateNewWallet()
    {
        SceneManager.LoadScene(2);
    }
    
    // Kept for compatibility if referenced, but unused in new UI
    void OnExport()
    {
        WalletFileService.ExportWallet();
        feedbackText.text = LocalizationManager.Instance.Get("GEN_EXPORTED");
        feedbackText.color = Color.green;
        Invoke("ClearFeedback", 3f);
    }
    
    void OnResetPin()
    {
        AuthManager.Instance.ResetAuth();
        SceneManager.LoadScene("MainWallet");
    }
    
    void ClearFeedback() { feedbackText.text =""; }

    private GameObject pubKeyModalObj;

    void ShowRevealPubKeyModal()
    {
        if (pubKeyModalObj != null) Destroy(pubKeyModalObj);

        GameObject canvasObj = GameObject.Find("Canvas");
        if (canvasObj == null) return;

        bool isTestNet = false;
#if TESTNET_BUILD
        isTestNet = true;
#endif

        var (xpub, zpub, hexPubKey) = MultisigCore.GetMainWalletExtendedPubKey(isTestNet);

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

        // Card container (compact, displaying only compressed public key)
        GameObject card = MakePanel(pubKeyModalObj.transform, "Card", 0, 0, isDesktop ? 850 : 980, isDesktop ? 680 : 860, ThemeManager.Instance.GetCardColor());
        RectTransform cardRT = card.GetComponent<RectTransform>();
        cardRT.anchoredPosition = Vector2.zero;

        float topY = isDesktop ? 260f : 340f;

        // Title
        MakeText(card.transform, LocalizationManager.Instance.Get("KEY_PUBKEY_TITLE"), 0, topY, isDesktop ? 32 : 36, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        topY -= isDesktop ? 45f : 60f;

        MakeText(card.transform, LocalizationManager.Instance.Get("KEY_PUBKEY_SUB"), 0, topY, isDesktop ? 20 : 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        topY -= isDesktop ? 65f : 85f;

        // QR Code (encodes hex compressed public key)
        GameObject qrObj = new GameObject("QRImage");
        qrObj.transform.SetParent(card.transform, false);
        RectTransform qrRT = qrObj.AddComponent<RectTransform>();
        float qrSize = isDesktop ? 180f : 240f;
        qrRT.anchoredPosition = new Vector2(0, topY - (qrSize / 2f));
        qrRT.sizeDelta = new Vector2(qrSize, qrSize);
        Image qrImg = qrObj.AddComponent<Image>();
        GenerateQRCodeForImage(qrImg, hexPubKey);
        topY -= (qrSize + (isDesktop ? 35f : 55f));

        // Section: Compressed Public Key (Hex - 66 chars)
        MakeText(card.transform, LocalizationManager.Instance.Get("KEY_PUBKEY_COMPRESSED"), 0, topY, isDesktop ? 20 : 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        topY -= isDesktop ? 35f : 45f;

        Text hexTxt = MakeText(card.transform, hexPubKey, 0, topY, isDesktop ? 17 : 19, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
        hexTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
        topY -= isDesktop ? 45f : 60f;

        MakeButton(card.transform, LocalizationManager.Instance.Get("KEY_COPY_HEX"), 0, topY, isDesktop ? 280 : 360, isDesktop ? 55 : 70, () => {
            GUIUtility.systemCopyBuffer = hexPubKey;
            if (feedbackText != null) { feedbackText.text = LocalizationManager.Instance.Get("HEX_PUBKEY_COPIED"); feedbackText.color = Color.green; Invoke("ClearFeedback", 2.5f); }
        });
        topY -= isDesktop ? 75f : 95f;

        // Close Button
        MakeButton(card.transform, LocalizationManager.Instance.Get("GEN_CLOSE"), 0, topY, isDesktop ? 260 : 360, isDesktop ? 60 : 80, () => {
            if (pubKeyModalObj != null) Destroy(pubKeyModalObj);
        }).GetComponent<Image>().color = new Color(0.35f, 0.4f, 0.5f);
    }

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
        if (canvasObj == null) return;

        emergencyRevealConfirmDialog = new GameObject("EmergencyRevealConfirmDialog");
        emergencyRevealConfirmDialog.layer = 5;
        emergencyRevealConfirmDialog.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = emergencyRevealConfirmDialog.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        emergencyRevealConfirmDialog.AddComponent<Image>().color = new Color(0, 0, 0, 0.9f);

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        GameObject panel = MakePanel(emergencyRevealConfirmDialog.transform, "ConfirmPanel", 0, 0, isDesktop ? 750 : 850, isDesktop ? 550 : 680, ThemeManager.Instance.GetBackgroundColor());

        // Icon
        MakeText(panel.transform, "⚠️", 0, isDesktop ? 180 : 230, 56, new Color(0.95f, 0.55f, 0.1f), FontStyle.Normal);

        // Title
        string title = LocalizationManager.Instance?.Get("EMERGENCY_REVEAL_CONFIRM_TITLE") ?? "One-Time Passphrase Reveal";
        MakeText(panel.transform, title, 0, isDesktop ? 110 : 150, isDesktop ? 30 : 34, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // Message
        string confirmMsg = LocalizationManager.Instance?.Get("EMERGENCY_REVEAL_CONFIRM_MSG") 
            ?? "You have a one time passphrase reveal option. If you agree to exercise this option, the button will be deleted after passphrase is revealed. Do you want to reveal your 12-word passphrase?";
        Text msgText = MakeText(panel.transform, confirmMsg, 0, isDesktop ? -10 : 10, isDesktop ? 20 : 24, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        msgText.rectTransform.sizeDelta = new Vector2(isDesktop ? 680 : 760, 220);
        msgText.alignment = TextAnchor.MiddleCenter;
        msgText.horizontalOverflow = HorizontalWrapMode.Wrap;

        float btnY = isDesktop ? -180 : -220;
        // Confirm Button
        MakeButton(panel.transform, LocalizationManager.Instance.Get("BTN_CONFIRM"), -160, btnY, 280, isDesktop ? 70 : 90, () => {
            if (emergencyRevealConfirmDialog != null) Destroy(emergencyRevealConfirmDialog);
            emergencyRevealConfirmDialog = null;
            MarkEmergencyRevealAsUsed();
            ShowEmergencyPassphraseModal();
        }).GetComponent<Image>().color = new Color(0.9f, 0.45f, 0.05f);

        // Cancel Button
        MakeButton(panel.transform, LocalizationManager.Instance.Get("BTN_CANCEL"), 160, btnY, 280, isDesktop ? 70 : 90, () => {
            if (emergencyRevealConfirmDialog != null) Destroy(emergencyRevealConfirmDialog);
            emergencyRevealConfirmDialog = null;
        }).GetComponent<Image>().color = new Color(0.35f, 0.35f, 0.4f);

        emergencyRevealConfirmDialog.transform.SetAsLastSibling();
    }

    private GameObject emergencyModalObj;

    void ShowEmergencyPassphraseModal()
    {
        ScreenSecurityManager.EnableProtection();
        if (emergencyModalObj != null) Destroy(emergencyModalObj);

        GameObject canvasObj = GameObject.Find("Canvas");
        if (canvasObj == null) return;

        WalletData currentWallet = WalletManager2.Instance != null ? WalletManager2.Instance.GetCurrentWallet() : null;
        string phrase = currentWallet != null ? currentWallet.seedPhrase : "";
        if (string.IsNullOrEmpty(phrase) && currentWallet != null)
        {
            phrase = !string.IsNullOrEmpty(currentWallet.privateKey) ? "Private Key: " + currentWallet.privateKey : "No wallet phrase found.";
        }

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        emergencyModalObj = new GameObject("EmergencyPassphraseModal");
        emergencyModalObj.layer = 5;
        emergencyModalObj.transform.SetParent(canvasObj.transform, false);

        RectTransform mRT = emergencyModalObj.AddComponent<RectTransform>();
        mRT.anchorMin = Vector2.zero; mRT.anchorMax = Vector2.one;
        mRT.offsetMin = Vector2.zero; mRT.offsetMax = Vector2.zero;

        Image bg = emergencyModalObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.92f);

        GameObject card = MakePanel(emergencyModalObj.transform, "Card", 0, 0, isDesktop ? 850 : 960, isDesktop ? 600 : 750, ThemeManager.Instance.GetCardColor());
        RectTransform cardRT = card.GetComponent<RectTransform>();
        cardRT.anchoredPosition = Vector2.zero;

        float topY = isDesktop ? 230f : 290f;

        // Title
        MakeText(card.transform, "🛡️ Emergency Passphrase Reveal", 0, topY, isDesktop ? 30 : 34, new Color(1f, 0.55f, 0.1f), FontStyle.Bold);
        topY -= isDesktop ? 45f : 60f;

        // Subtitle warning
        MakeText(card.transform, "Save these words securely before updating or reinstalling your app to prevent fund loss.", 0, topY, isDesktop ? 18 : 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        topY -= isDesktop ? 65f : 85f;

        // Phrase box
        GameObject box = MakePanel(card.transform, "Box", 0, topY, isDesktop ? 750 : 860, isDesktop ? 140 : 180, ThemeManager.Instance.IsDarkMode ? new Color(0.12f, 0.14f, 0.20f) : new Color(0.92f, 0.94f, 0.98f));
        Text wordsTxt = MakeText(box.transform, phrase, 0, 0, isDesktop ? 22 : 26, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        wordsTxt.alignment = TextAnchor.MiddleCenter;
        wordsTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
        wordsTxt.rectTransform.sizeDelta = new Vector2(isDesktop ? 720 : 820, isDesktop ? 130 : 170);
        topY -= isDesktop ? 110f : 140f;

        // Copy button
        MakeButton(card.transform, "📋 Copy 12-Word Phrase", 0, topY, isDesktop ? 340 : 420, isDesktop ? 60 : 75, () => {
            if (!string.IsNullOrEmpty(phrase))
            {
                GUIUtility.systemCopyBuffer = phrase;
                if (feedbackText != null)
                {
                    feedbackText.text = "✅ 12-Word Phrase Copied to Clipboard!";
                    feedbackText.color = Color.green;
                    Invoke("ClearFeedback", 3f);
                }
            }
        }).GetComponent<Image>().color = new Color(0.9f, 0.45f, 0.05f);
        topY -= isDesktop ? 75f : 95f;

        // Close button
        MakeButton(card.transform, LocalizationManager.Instance.Get("GEN_CLOSE"), 0, topY, isDesktop ? 260 : 320, isDesktop ? 55 : 70, () => {
            ScreenSecurityManager.DisableProtection();
            if (emergencyModalObj != null) Destroy(emergencyModalObj);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }).GetComponent<Image>().color = new Color(0.35f, 0.4f, 0.5f);
    }

    private void GenerateQRCodeForImage(Image targetImg, string text)
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
            Debug.LogWarning("[OptionsUI] QR error: " + ex.Message);
        }
    }

    // --- HELPERS ---

    Font GetFont() {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(950, 80);
        Text text = obj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt); text.fontSize = size; text.color = col; text.fontStyle = style; text.font = GetFont();
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false; 
        return text;
    }

    Button MakeButton(Transform parent, string txt, float x, float y, float w, float h, System.Action onClick) {
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
        if(!string.IsNullOrEmpty(txt)){
            GameObject tObj = new GameObject("Text");
            tObj.layer = 5; tObj.transform.SetParent(obj.transform, false);
            RectTransform tRT = tObj.AddComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
            tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
            Text t = tObj.AddComponent<Text>();
            t.text = LocalizationManager.CleanIcons(txt); t.fontSize = 32; t.color = Color.white; t.font = GetFont();
            t.alignment = TextAnchor.MiddleCenter; t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
        }
        return btn;
    }

    void OnDestroy()
    {
        ScreenSecurityManager.DisableProtection();
    }
}
