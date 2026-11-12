using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.Collections;
using System.IO;
using NBitcoin;

public class RecoverWalletUI : MonoBehaviour
{
    private BitcoinWalletCore wallet;
    private InputField[] seedInputs = new InputField[12];
    private InputField hexKeyInput;
    private InputField wifKeyInput;
#if TESTNET_BUILD
    private bool isTestNet = true;
#else
    private bool isTestNet = false;
#endif
    
    // Recovery mode: 0 = Seed Phrase, 1 = Private Key (Hex), 2 = WIF Key
    private int recoverMode = 0;
    
    private Button seedPhraseBtn;
    private Button hexKeyBtn;
    private Button wifKeyBtn;
    private Text statusText;
    private GameObject seedPhraseContainer;
    private GameObject hexKeyContainer;
    private GameObject wifKeyContainer;

    void Start()
    {
        ScreenSecurityManager.EnableProtection();
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
        
        BuildUI();
        ShowWarningModal();
    }

    void BuildUI()
    {
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
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
        }
        
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject bg = new GameObject("BG");
        bg.layer = 5;
        bg.transform.SetParent(canvas.transform, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;
        bg.AddComponent<Image>().color = ThemeManager.Instance.GetBackgroundColor();

        float y = isDesktop ? 420 : 700;

        // Title
        MakeText(canvas.transform, LocalizationManager.Instance.Get("REC_TITLE"), isDesktop ? -400 : 0, y, isDesktop ? 50 : 70, ThemeManager.Instance.GetTextColor());
        if (!isDesktop)
        {
            y -= 160;
            y -= 20; // Slight adjustment
        }
        else
        {
            y = 280;
        }

        // Mode Selection Buttons (3 tabs: Seed Phrase / Hex Key / WIF Key)
        float tabWidth = isDesktop ? 280 : 300;
        float tabSpacing = 20;
        float totalWidth = tabWidth * 3 + tabSpacing * 2;
        float startX = -totalWidth / 2 + tabWidth / 2;
        
        seedPhraseBtn = MakeButtonReturn(canvas.transform, LocalizationManager.Instance.Get("REC_TAB_SEED"), startX, y, tabWidth, isDesktop ? 80 : 100,
            recoverMode == 0 ? new Color(0.3f, 0.7f, 0.9f) : ThemeManager.Instance.GetCardColor(),
            () => { recoverMode = 0; UpdateModeButtons(); });
        
        hexKeyBtn = MakeButtonReturn(canvas.transform, LocalizationManager.Instance.Get("REC_TAB_HEX"), startX + tabWidth + tabSpacing, y, tabWidth, isDesktop ? 80 : 100,
            recoverMode == 1 ? new Color(0.3f, 0.7f, 0.9f) : ThemeManager.Instance.GetCardColor(),
            () => { recoverMode = 1; UpdateModeButtons(); });
            
        wifKeyBtn = MakeButtonReturn(canvas.transform, LocalizationManager.Instance.Get("REC_TAB_WIF"), startX + (tabWidth + tabSpacing) * 2, y, tabWidth, isDesktop ? 80 : 100,
            recoverMode == 2 ? new Color(0.3f, 0.7f, 0.9f) : ThemeManager.Instance.GetCardColor(),
            () => { recoverMode = 2; UpdateModeButtons(); });
        
        if (!isDesktop)
            y -= 180;

        // ===== Seed Phrase Container =====
        seedPhraseContainer = new GameObject("SeedPhraseContainer");
        seedPhraseContainer.layer = 5;
        seedPhraseContainer.transform.SetParent(canvas.transform, false);
        
        MakeLabel(seedPhraseContainer.transform, LocalizationManager.Instance.Get("REC_LBL_SEED"), 0, isDesktop ? 180 : y, isDesktop ? 30 : 40);
        float seedY = isDesktop ? 80 : (y - 100);

        for (int i = 0; i < 12; i++)
        {
            float x, yPos;
            if (isDesktop)
            {
                // 4 columns, 3 rows
                int col = i % 4;
                int row = i / 4;
                x = -525 + (col * 350); // -525, -175, 175, 525
                yPos = seedY - (row * 100);
            }
            else
            {
                int col = i % 2;
                int row = i / 2;
                x = col == 0 ? -230 : 270;
                yPos = seedY - (row * 110);
            }
            
            GameObject numLabel = new GameObject($"Num{i}");
            numLabel.layer = 5;
            numLabel.transform.SetParent(canvas.transform, false);
            RectTransform numRT = numLabel.AddComponent<RectTransform>();
            if (isDesktop)
                numRT.anchoredPosition = new Vector2(x - 140, yPos);
            else
                numRT.anchoredPosition = new Vector2(x - 230, yPos);
            numRT.sizeDelta = new Vector2(60, 80);
            numLabel.transform.SetParent(seedPhraseContainer.transform, false);
            Text numText = numLabel.AddComponent<Text>();
            numText.text = $"{i + 1:00}";
            numText.fontSize = isDesktop ? 30 : 38;
            numText.color = ThemeManager.Instance.GetAccentColor();
            numText.font = GetFont();
            numText.alignment = TextAnchor.MiddleCenter;
            numText.fontStyle = FontStyle.Bold;
            
            seedInputs[i] = MakeInput(seedPhraseContainer.transform, x, yPos, isDesktop ? 220 : 380, isDesktop ? 70 : 80);
        }
        
        // ===== Hex Key Container =====
        hexKeyContainer = new GameObject("HexKeyContainer");
        hexKeyContainer.layer = 5;
        hexKeyContainer.transform.SetParent(canvas.transform, false);
        
        MakeLabel(hexKeyContainer.transform, LocalizationManager.Instance.Get("REC_LBL_HEX"), 0, isDesktop ? 180 : y, isDesktop ? 30 : 40);
        float pkY = isDesktop ? 100 : (y - 80);
        MakeLabel(hexKeyContainer.transform, LocalizationManager.Instance.Get("REC_HINT_HEX"), 0, pkY, isDesktop ? 22 : 28);
        pkY -= isDesktop ? 120 : 100;
        
        hexKeyInput = MakeInput(hexKeyContainer.transform, 0, pkY, isDesktop ? 1000 : 900, isDesktop ? 150 : 100);
        hexKeyInput.lineType = InputField.LineType.MultiLineNewline;
        
        // ===== WIF Key Container =====
        wifKeyContainer = new GameObject("WifKeyContainer");
        wifKeyContainer.layer = 5;
        wifKeyContainer.transform.SetParent(canvas.transform, false);
        
        MakeLabel(wifKeyContainer.transform, LocalizationManager.Instance.Get("REC_LBL_WIF"), 0, isDesktop ? 180 : y, isDesktop ? 30 : 40);
        float wifY = isDesktop ? 100 : (y - 80);
#if TESTNET_BUILD
        MakeLabel(wifKeyContainer.transform, LocalizationManager.Instance.Get("REC_HINT_WIF_TESTNET"), 0, wifY, isDesktop ? 22 : 28);
#else
        MakeLabel(wifKeyContainer.transform, LocalizationManager.Instance.Get("REC_HINT_WIF"), 0, wifY, isDesktop ? 22 : 28);
#endif
        wifY -= isDesktop ? 120 : 100;
        
        wifKeyInput = MakeInput(wifKeyContainer.transform, 0, wifY, isDesktop ? 1000 : 900, isDesktop ? 150 : 100);
        wifKeyInput.lineType = InputField.LineType.MultiLineNewline;
        
        float recoverY = isDesktop ? -220 : (y - 850);
        
        UpdateModeButtons();

        // Recover Button
        MakeButton(canvas.transform, LocalizationManager.Instance.Get("REC_BTN_RECOVER"), 0, recoverY, isDesktop ? 600 : 900, isDesktop ? 90 : 120, RecoverWallet)
            .GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.3f);
        
        statusText = MakeText(canvas.transform, "", 0, recoverY - (isDesktop ? 110 : 180), 35, Color.yellow);
        
        // Back Button
        if (isDesktop)
        {
            MakeButton(canvas.transform, LocalizationManager.Instance.Get("GEN_BACK"), -800, 420, 200, 70, () => {
                SceneManager.LoadScene("MainWallet");
            }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
        }
        else
        {
            MakeButton(canvas.transform, LocalizationManager.Instance.Get("GEN_BACK"), -400, 850, 280, 100, () => {
                SceneManager.LoadScene("MainWallet");
            }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
        }
    }

    // --- HELPER METHODS ---

    Font GetFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return f;
    }

    Text MakeText(Transform parent, string txt, float x, float y, int size, Color col)
    {
        GameObject obj = new GameObject("Text");
        obj.layer = 5; obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(1000, 80);
        Text text = obj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt); text.fontSize = size; text.color = col;
        text.font = GetFont();
        text.alignment = TextAnchor.MiddleCenter;
        return text;
    }

    void MakeLabel(Transform parent, string txt, float x, float y, int size)
    {
        GameObject obj = new GameObject("Label");
        obj.layer = 5; obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(1000, 60);
        Text text = obj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt); text.fontSize = size;
        text.color = ThemeManager.Instance.GetSecondaryTextColor();
        text.font = GetFont();
        text.alignment = TextAnchor.MiddleCenter;
        text.fontStyle = FontStyle.Bold;
    }

    InputField MakeInput(Transform parent, float x, float y, float w, float h)
    {
        GameObject obj = new GameObject("Input");
        obj.layer = 5; obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        
        Image img = obj.AddComponent<Image>();
        img.color = ThemeManager.Instance.GetCardColor();
        
        InputField input = obj.AddComponent<InputField>();
        
        GameObject txtObj = new GameObject("Text");
        txtObj.layer = 5; txtObj.transform.SetParent(obj.transform, false);
        RectTransform txtRT = txtObj.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(15, 5); txtRT.offsetMax = new Vector2(-15, -5);
        
        Text txt = txtObj.AddComponent<Text>();
        txt.font = GetFont();
        txt.fontSize = 32;
        txt.color = ThemeManager.Instance.GetTextColor();
        txt.alignment = TextAnchor.MiddleCenter;
        
        input.textComponent = txt;
        input.onValueChanged.AddListener((val) => AudioManager.Instance.PlayPinType());
        return input;
    }

    Button MakeButton(Transform parent, string txt, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
    {
        GameObject obj = new GameObject("Btn");
        obj.layer = 5; obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        Image img = obj.AddComponent<Image>();
        img.color = ThemeManager.Instance.GetAccentColor();
        Button btn = obj.AddComponent<Button>();
        
        if (txt == LocalizationManager.Instance.Get("GEN_BACK"))
            btn.onClick.AddListener(() => AudioManager.Instance.PlayBack());
        else
            btn.onClick.AddListener(() => AudioManager.Instance.PlayClick());
            
        btn.onClick.AddListener(action);
        
        GameObject txtObj = new GameObject("Text");
        txtObj.layer = 5; txtObj.transform.SetParent(obj.transform, false);
        RectTransform txtRT = txtObj.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
        Text text = txtObj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt); text.fontSize = 45; text.color = Color.white;
        text.font = GetFont();
        text.alignment = TextAnchor.MiddleCenter;
        text.fontStyle = FontStyle.Bold;
        
        return btn;
    }
    
    Button MakeButtonReturn(Transform parent, string txt, float x, float y, float w, float h, Color color, UnityEngine.Events.UnityAction action)
    {
        GameObject obj = new GameObject("Btn");
        obj.layer = 5; obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        Image img = obj.AddComponent<Image>();
        img.color = color;
        Button btn = obj.AddComponent<Button>();
        btn.onClick.AddListener(() => AudioManager.Instance.PlayClick());
        btn.onClick.AddListener(action);
        
        GameObject txtObj = new GameObject("Text");
        txtObj.layer = 5; txtObj.transform.SetParent(obj.transform, false);
        RectTransform txtRT = txtObj.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
        Text text = txtObj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt); text.fontSize = 38; text.color = Color.white;
        text.font = GetFont();
        text.alignment = TextAnchor.MiddleCenter;
        text.fontStyle = FontStyle.Bold;
        
        return btn;
    }
    
    void UpdateModeButtons()
    {
        Color activeCol = new Color(0.3f, 0.7f, 0.9f);
        Color inactiveCol = ThemeManager.Instance.GetCardColor();
        
        if (seedPhraseBtn != null)
            seedPhraseBtn.GetComponent<Image>().color = recoverMode == 0 ? activeCol : inactiveCol;
        if (hexKeyBtn != null)
            hexKeyBtn.GetComponent<Image>().color = recoverMode == 1 ? activeCol : inactiveCol;
        if (wifKeyBtn != null)
            wifKeyBtn.GetComponent<Image>().color = recoverMode == 2 ? activeCol : inactiveCol;
            
        // Toggle visibility of containers
        if (seedPhraseContainer != null)
            seedPhraseContainer.SetActive(recoverMode == 0);
        if (hexKeyContainer != null)
            hexKeyContainer.SetActive(recoverMode == 1);
        if (wifKeyContainer != null)
            wifKeyContainer.SetActive(recoverMode == 2);
    }

    void RecoverWallet()
    {
        statusText.text = LocalizationManager.Instance.Get("GEN_CHECKING");
        statusText.color = Color.yellow;
        
        try
        {
#if TESTNET_BUILD
            NBitcoin.Network network = NBitcoin.Network.TestNet;
#else
            NBitcoin.Network network = NBitcoin.Network.Main;
#endif
            string address = "";
            string privateKey = "";
            string seedPhrase = "";
            
            if (recoverMode == 0)
            {
                // === Seed Phrase Recovery Mode ===
                bool hasEmpty = false;
                
                for (int i = 0; i < 12; i++)
                {
                    string word = seedInputs[i].text.Trim().ToLower();
                    if (string.IsNullOrEmpty(word))
                    {
                        hasEmpty = true;
                        seedInputs[i].GetComponent<Image>().color = new Color(1f, 0.5f, 0.5f);
                    }
                    else
                    {
                        seedInputs[i].GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();
                    }
                    seedPhrase += word + " ";
                }
                
                if (hasEmpty)
                {
                    statusText.text = LocalizationManager.Instance.Get("REC_ERR_SEED_EMPTY");
                    statusText.color = Color.red;
                    return;
                }
                
                seedPhrase = seedPhrase.Trim();
                wallet = new BitcoinWalletCore(seedPhrase, network);
                
                address = wallet.GetSegWitAddress(0);
                privateKey = wallet.GetSegWitPrivateKey(0);
            }
            else if (recoverMode == 1)
            {
                // === Private Key (Hex) Recovery Mode ===
                string privateKeyHex = hexKeyInput.text.Trim();
                
                if (string.IsNullOrEmpty(privateKeyHex))
                {
                    statusText.text = LocalizationManager.Instance.Get("REC_ERR_PK_EMPTY");
                    statusText.color = Color.red;
                    hexKeyInput.GetComponent<Image>().color = new Color(1f, 0.5f, 0.5f);
                    return;
                }
                
                // Remove spaces, newlines
                privateKeyHex = privateKeyHex.Replace(" ", "").Replace("\n", "").Replace("\r", "");
                
                // Validate: must be exactly 64 hex characters
                if (privateKeyHex.Length != 64)
                {
                    statusText.text = LocalizationManager.Instance.Get("REC_ERR_HEX_LEN");
                    statusText.color = Color.red;
                    hexKeyInput.GetComponent<Image>().color = new Color(1f, 0.5f, 0.5f);
                    return;
                }
                
                if (!System.Text.RegularExpressions.Regex.IsMatch(privateKeyHex, "^[0-9a-fA-F]+$"))
                {
                    statusText.text = LocalizationManager.Instance.Get("REC_ERR_HEX_FMT");
                    statusText.color = Color.red;
                    hexKeyInput.GetComponent<Image>().color = new Color(1f, 0.5f, 0.5f);
                    return;
                }
                
                hexKeyInput.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();
                
                // Create wallet from hex private key
                wallet = new BitcoinWalletCore(privateKeyHex, network, true);
                
                address = wallet.GetSegWitAddress(0);
                privateKey = wallet.GetSegWitPrivateKey(0);
                seedPhrase = ""; // No seed phrase for private key import
            }
            else if (recoverMode == 2)
            {
                // === WIF Key Recovery Mode ===
                string wifKey = wifKeyInput.text.Trim();
                
                if (string.IsNullOrEmpty(wifKey))
                {
                    statusText.text = LocalizationManager.Instance.Get("REC_ERR_WIF_EMPTY");
                    statusText.color = Color.red;
                    wifKeyInput.GetComponent<Image>().color = new Color(1f, 0.5f, 0.5f);
                    return;
                }
                
                // Remove spaces, newlines
                wifKey = wifKey.Replace(" ", "").Replace("\n", "").Replace("\r", "");
                
                // Validate WIF format: 51-52 characters, starts with K, L, or 5
                // Base58 character set: 123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz
                if (wifKey.Length < 51 || wifKey.Length > 52)
                {
                    statusText.text = LocalizationManager.Instance.Get("REC_ERR_WIF_LEN");
                    statusText.color = Color.red;
                    wifKeyInput.GetComponent<Image>().color = new Color(1f, 0.5f, 0.5f);
                    return;
                }
                
#if TESTNET_BUILD
                if (wifKey[0] != 'c' && wifKey[0] != '9')
                {
                    statusText.text = LocalizationManager.Instance.Get("REC_ERR_WIF_START_TESTNET");
                    statusText.color = Color.red;
                    wifKeyInput.GetComponent<Image>().color = new Color(1f, 0.5f, 0.5f);
                    return;
                }
#else
                if (wifKey[0] != 'K' && wifKey[0] != 'L' && wifKey[0] != '5')
                {
                    statusText.text = LocalizationManager.Instance.Get("REC_ERR_WIF_START");
                    statusText.color = Color.red;
                    wifKeyInput.GetComponent<Image>().color = new Color(1f, 0.5f, 0.5f);
                    return;
                }
#endif
                
                // Validate Base58 characters
                string base58Chars = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
                foreach (char c in wifKey)
                {
                    if (base58Chars.IndexOf(c) < 0)
                    {
                        statusText.text = LocalizationManager.Instance.Get("REC_ERR_WIF_FMT");
                        statusText.color = Color.red;
                        wifKeyInput.GetComponent<Image>().color = new Color(1f, 0.5f, 0.5f);
                        return;
                    }
                }
                
                wifKeyInput.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();
                
                // Create wallet from WIF key (isHex = false means WIF format)
                wallet = new BitcoinWalletCore(wifKey, network, false);
                
                address = wallet.GetSegWitAddress(0);
                privateKey = wallet.GetSegWitPrivateKey(0);
                seedPhrase = ""; // No seed phrase for WIF import
            }
            
            // Save wallet (seedPhrase will be empty string for private key/WIF imports)
            WalletManager2.Instance.SaveWallet(seedPhrase, isTestNet, address, privateKey);
            
            statusText.text = LocalizationManager.Instance.Get("REC_SUCCESS");
            statusText.color = Color.green;
            
            Debug.Log("✅ Wallet Recovered and Saved!");
            Invoke("GoToMain", 1.5f);
        }
        catch (System.Exception ex)
        {
            statusText.color = Color.red;
            if (ex.Message.Contains("not in the wordlist")) statusText.text = LocalizationManager.Instance.Get("REC_ERR_INVALID_WORD");
            else if (ex.Message.Contains("entropy")) statusText.text = LocalizationManager.Instance.Get("REC_ERR_SEED_EMPTY"); // Use general seed error or specific
            else if (ex.Message.Contains("private key")) statusText.text = LocalizationManager.Instance.Get("REC_ERR_PK_EMPTY"); // Generic
            else if (ex.Message.Contains("checksum")) statusText.text = LocalizationManager.Instance.Get("REC_ERR_WIF_FMT"); // Checksum similar to format
            else statusText.text = $"❌ {LocalizationManager.Instance.Get("GEN_ERROR")}: {ex.Message.Substring(0, System.Math.Min(50, ex.Message.Length))}";
            Debug.LogError($"❌ Recovery Error: {ex.Message}");
        }
    }
    
    void GoToMain()
    {
        SceneManager.LoadScene("MainWallet");
    }

    void ShowWarningModal()
    {
        GameObject canvas = GameObject.Find("Canvas");
        if (canvas == null) return;

        // Full-screen dark overlay
        GameObject warningOverlay = new GameObject("RecoverWarningOverlay");
        warningOverlay.layer = 5;
        warningOverlay.transform.SetParent(canvas.transform, false);
        RectTransform overlayRT = warningOverlay.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;
        
        // Use a semi-transparent dark blue/black for premium feel
        Image overlayImg = warningOverlay.AddComponent<Image>();
        overlayImg.color = new Color(0.05f, 0.05f, 0.1f, 0.98f);
        
        // Modal Card
        GameObject card = new GameObject("WarningCard");
        card.layer = 5;
        card.transform.SetParent(warningOverlay.transform, false);
        RectTransform cardRT = card.AddComponent<RectTransform>();
        cardRT.sizeDelta = new Vector2(950, 700);
        cardRT.anchoredPosition = new Vector2(0, 50);
        Image cardImg = card.AddComponent<Image>();
        cardImg.color = ThemeManager.Instance.GetCardColor();
        
        // Add a subtle border/glow
        GameObject border = new GameObject("Border");
        border.transform.SetParent(card.transform, false);
        RectTransform borderRT = border.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-2,-2); borderRT.offsetMax = new Vector2(2,2);
        Image borderImg = border.AddComponent<Image>();
        borderImg.color = new Color(0.6f, 0.5f, 0.2f, 0.3f); // Subtle gold
        border.transform.SetAsFirstSibling();

        // Warning Icon or Symbol
        Text icon = MakeText(card.transform, "⚠️", 0, 220, 100, Color.yellow);
        
        // Warning Text
        Text warningText = MakeText(card.transform, LocalizationManager.Instance.Get("RECOVER_WARNING"), 0, 30, 38, Color.white);
        warningText.fontStyle = FontStyle.Bold;
        warningText.rectTransform.sizeDelta = new Vector2(850, 400);
        warningText.lineSpacing = 1.2f;

        // Buttons Container
        GameObject btnContainer = new GameObject("Buttons");
        btnContainer.transform.SetParent(card.transform, false);
        RectTransform btnRT = btnContainer.AddComponent<RectTransform>();
        btnRT.anchoredPosition = new Vector2(0, -250);
        btnRT.sizeDelta = new Vector2(900, 120);

        // Confirm Button
        Button confirmBtn = MakeButton(btnContainer.transform, LocalizationManager.Instance.Get("BTN_CONFIRM"), -220, 0, 400, 110, () => {
            AudioManager.Instance.PlayClick();
            Destroy(warningOverlay);
        });
        confirmBtn.GetComponent<Image>().color = new Color(0.15f, 0.5f, 0.3f); // Deep emerald green
        
        // Cancel Button
        Button cancelBtn = MakeButton(btnContainer.transform, LocalizationManager.Instance.Get("BTN_CANCEL"), 220, 0, 400, 110, () => {
            AudioManager.Instance.PlayBack();
            SceneManager.LoadScene("MainWallet");
        });
        cancelBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f); // Muted red
    }

    void OnDestroy()
    {
        ScreenSecurityManager.DisableProtection();
    }
}
