using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ZXing;
using ZXing.QrCode;
using NBitcoin;
using Key = NBitcoin.Key;

public class MultisigVaultUI : MonoBehaviour
{
    private const float REFERENCE_WIDTH = 1080f;
    private const float REFERENCE_HEIGHT = 1920f;

    // UI Navigation States
    private enum VaultViewState { Landing, Config, KeyInput, Import, Dashboard }
    private VaultViewState currentState = VaultViewState.Landing;

    private GameObject mainCanvas;
    private Transform contentRoot;

    // Active configuration state
    private string currentVaultName = "Vault 1";
    private int requiredSignatures = 2; // M
    private int totalKeys = 2;          // N
    private MultisigAddressType selectedAddressType = MultisigAddressType.P2WSH;
    private bool useDedicatedMnemonic = true;
    private string generatedVaultMnemonic = "";
    private string generatedVaultPubKeyHex = "";
    private string generatedVaultZpub = "";

    // Views
    private GameObject landingViewObj;
    private GameObject configViewObj;
    private GameObject keyInputViewObj;
    private GameObject importViewObj;
    private GameObject dashboardViewObj;

    // Config references
    private Text requiredSigText;
    private Text totalKeysText;
    private Text quorumSummaryBadge;
    private Text keyInputSubtitleText;
    private GameObject p2wshCard;
    private GameObject p2shP2wshCard;
    private GameObject p2shCard;
    private Text p2wshCheckmark;
    private Text p2shP2wshCheckmark;
    private Text p2shCheckmark;
    private InputField vaultNameInputField;

    // Policy Selection (Quorum vs Coordinator)
    private enum VaultPolicyMode { Quorum, Coordinator }
    private VaultPolicyMode selectedPolicyMode = VaultPolicyMode.Quorum;
    private readonly Color ORANGE_ACCENT = new Color(1.0f, 0.55f, 0.0f); // #FF8C00 Vibrant Orange
    private Image quorumTabImg;
    private Text quorumTabTxt;
    private Image coordinatorTabImg;
    private Text coordinatorTabTxt;
    private InputField coordinatorScriptInput;
    private Text coordinatorErrorText;

    // Preset buttons (Support n-of-n and m-of-n)
    private Image preset2of2Img;
    private Image preset3of3Img;
    private Image preset2of3Img;
    private Image preset3of5Img;
    private Text preset2of2Txt;
    private Text preset3of3Txt;
    private Text preset2of3Txt;
    private Text preset3of5Txt;

    // Stepper images for alpha dimming
    private Image btnMMinusImg;
    private Image btnMPlusImg;
    private Image btnNMinusImg;
    private Image btnNPlusImg;

    // Key inputs
    private List<InputField> keyInputFields = new List<InputField>();
    private Text keyInputErrorText;
    private string generatedKey2Mnemonic = "";

    // Import inputs
    private InputField importNameInput;
    private InputField importRedeemScriptInput;
    private Text importErrorText;

    // Dashboard references & verification states
    private Text dashboardTitleText;
    private Text vaultAddressText;
    private Text vaultBalanceText;
    private Text vaultStatusText;
    private Image vaultQrImage;

    private bool isKey2Verified = false;
    private NBitcoin.Key verifiedKey2 = null;
    private Button sendButtonRef = null;
    private Image sendButtonImgRef = null;
    private Text sendButtonTxtRef = null;
    private Text key2ValidationStatusText = null;
    private InputField key2InputField = null;

    // Modals
    private GameObject sendModalObj;
    private GameObject receiveModalObj;
    private GameObject groupIndexModalObj;
    private GameObject pinModalObj;
    private GameObject revealModalObj;
    private GameObject deleteModalObj;
    private GameObject manualConfigModalObj;
    private Text vaultSerialText;

    // Data
    private List<MultisigVaultConfig> allVaults = new List<MultisigVaultConfig>();
    private MultisigVaultConfig activeVault;
    private bool isTestNet = false;

    private string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    private string LF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : string.Format(key, args);

    void OnEnable()
    {
        if (LocalizationManager.Instance != null)
            LocalizationManager.Instance.OnLanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        if (LocalizationManager.Instance != null)
            LocalizationManager.Instance.OnLanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        if (mainCanvas != null && mainCanvas.activeSelf)
        {
            BuildUI();
        }
    }

    void Awake()
    {
#if TESTNET_BUILD
        isTestNet = true;
#else
        isTestNet = false;
#endif
    }

    void Start()
    {
        EnsureEventSystem();
        RefreshVaultsList();
        currentState = VaultViewState.Landing;
        BuildUI();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (currentState == VaultViewState.Landing)
            {
                if (mainCanvas != null) Destroy(mainCanvas);
                Destroy(gameObject);
            }
            else
            {
                bool isDesk = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
                isDesk = true;
#elif UNITY_EDITOR
                isDesk = (Screen.width > Screen.height);
#endif
                currentState = VaultViewState.Landing;
                BuildLandingView(contentRoot, isDesk);
                UpdateViewVisibility();
            }
        }
    }

    private void OnDestroy()
    {
        ScreenSecurityManager.DisableProtection();
        if (mainCanvas != null)
        {
            Destroy(mainCanvas);
        }
    }

    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private void RefreshVaultsList()
    {
        allVaults = MultisigCore.LoadAllVaults(isTestNet);
        if (allVaults == null) allVaults = new List<MultisigVaultConfig>();
    }

    public void BuildUI()
    {
        EnsureEventSystem();

        if (mainCanvas != null) Destroy(mainCanvas);

        // Purge any previously orphaned or duplicate MultisigCanvas objects in the scene
        var strayCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var c in strayCanvases)
        {
            if (c != null && c.gameObject.name == "MultisigCanvas" && c.gameObject != mainCanvas)
            {
                Destroy(c.gameObject);
            }
        }

        mainCanvas = new GameObject("MultisigCanvas");
        mainCanvas.layer = 5;
        mainCanvas.transform.SetParent(this.transform, false);
        Canvas canvas = mainCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // Unconditionally above background and options canvases
        canvas.overrideSorting = true;

        CanvasScaler scaler = mainCanvas.AddComponent<CanvasScaler>();
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

        var raycaster = mainCanvas.AddComponent<GraphicRaycaster>();
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        // Background
        GameObject bg = new GameObject("BG");
        bg.layer = 5;
        bg.transform.SetParent(mainCanvas.transform, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = ThemeManager.Instance.GetBackgroundColor();

        // Main Container Card
        GameObject card = new GameObject("MainContainer");
        card.layer = 5;
        card.transform.SetParent(mainCanvas.transform, false);
        RectTransform cardRT = card.AddComponent<RectTransform>();
        cardRT.anchorMin = Vector2.zero; cardRT.anchorMax = Vector2.one;
        cardRT.offsetMin = Vector2.zero; cardRT.offsetMax = Vector2.zero;
        contentRoot = card.transform;

        // Build all views
        BuildLandingView(contentRoot, isDesktop);
        BuildConfigView(contentRoot, isDesktop);
        BuildKeyInputView(contentRoot, isDesktop);
        BuildImportView(contentRoot, isDesktop);
        BuildDashboardView(contentRoot, isDesktop);

        UpdateViewVisibility();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. LANDING VIEW (Vaults List + Create/Import Actions)
    // ─────────────────────────────────────────────────────────────────────────────
    private void BuildLandingView(Transform parent, bool isDesktop)
    {
        if (landingViewObj != null)
        {
            landingViewObj.SetActive(false);
            Destroy(landingViewObj);
        }
        landingViewObj = CreateViewContainer(parent, "LandingView");
        var (header, content, scrollRect) = CreateScrollableScreen(landingViewObj.transform, isDesktop, L("MS_VAULTS_TITLE"), () => {
            if (mainCanvas != null) Destroy(mainCanvas);
            Destroy(gameObject);
        });

        float y = -20f;

        // Info Banner Card
        float infoH = 120f;
        GameObject infoCard = CreateCard(content, 0, y, isDesktop ? 860f : 920f, infoH);
        infoCard.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.12f, 0.16f, 0.24f) : new Color(0.92f, 0.95f, 1f);
        string netName = isTestNet ? "TestNet" : "MainNet";
        Text infoText = CreateText(infoCard.transform, LF("MS_VAULTS_BANNER", netName), 0, 0, 24, ThemeManager.Instance.GetTextColor(), FontStyle.Normal);
        infoText.alignment = TextAnchor.MiddleCenter;
        y -= (infoH + 25f);

        // Primary Action Buttons Row
        float btnW = isDesktop ? 415f : 445f;
        float btnH = isDesktop ? 85f : 95f;

        GameObject createBtn = CreateButton(content, L("MS_BTN_CREATE"), -(btnW / 2f + 12f), y, btnW, btnH, () => {
            currentVaultName = $"Vault {allVaults.Count + 1}";
            requiredSignatures = 2;
            totalKeys = 2;
            selectedAddressType = MultisigAddressType.P2WSH;
            useDedicatedMnemonic = true;
            selectedPolicyMode = VaultPolicyMode.Quorum;
            GenerateNewVaultKey();
            currentState = VaultViewState.Config;
            BuildConfigView(contentRoot, isDesktop);
            UpdateViewVisibility();
            if (vaultNameInputField != null) vaultNameInputField.text = currentVaultName;
            UpdateQuorumUI();
            UpdateTypeCheckmarks();
        });
        createBtn.GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
        var cTxt = createBtn.GetComponentInChildren<Text>();
        cTxt.color = Color.white; cTxt.fontStyle = FontStyle.Bold; cTxt.fontSize = 28;

        GameObject importBtn = CreateButton(content, L("MS_BTN_IMPORT"), (btnW / 2f + 12f), y, btnW, btnH, () => {
            currentState = VaultViewState.Import;
            UpdateViewVisibility();
        });
        importBtn.GetComponent<Image>().color = new Color(0.18f, 0.52f, 0.36f);
        var iTxt = importBtn.GetComponentInChildren<Text>();
        iTxt.color = Color.white; iTxt.fontStyle = FontStyle.Bold; iTxt.fontSize = 28;

        y -= (btnH + 35f);

        // Section Title: Your Vaults
        Text sectionTitle = CreateText(content, L("MS_ACTIVE_VAULTS"), 0, y, 24, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        sectionTitle.alignment = TextAnchor.MiddleLeft;
        y -= 45f;

        if (allVaults.Count == 0)
        {
            float emptyH = 180f;
            GameObject emptyCard = CreateCard(content, 0, y, isDesktop ? 860f : 920f, emptyH);
            Text emptyTxt = CreateText(emptyCard.transform, L("MS_NO_VAULTS"), 0, 0, 26, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            emptyTxt.alignment = TextAnchor.MiddleCenter;
            emptyTxt.rectTransform.sizeDelta = new Vector2(800f, 130f);
            y -= (emptyH + 30f);
        }
        else
        {
            for (int i = 0; i < allVaults.Count; i++)
            {
                var v = allVaults[i];
                float vCardH = 160f;
                GameObject vCard = CreateCard(content, 0, y, isDesktop ? 860f : 920f, vCardH);
                vCard.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();

                // Vault Name
                string vPrefix = v.isCoordinator ? "👁️ " : "🛡️ ";
                Text vName = CreateText(vCard.transform, vPrefix + v.vaultName, 0, 40f, 28, v.isCoordinator ? ORANGE_ACCENT : ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
                vName.alignment = TextAnchor.MiddleLeft;
                vName.rectTransform.sizeDelta = new Vector2(500f, 50f);
                vName.rectTransform.anchoredPosition = new Vector2(-180f, 40f);

                // Quorum & Type Badge
                string coordBadge = v.isCoordinator ? " • 👁️ Watch-Only (Coordinator)" : "";
                string badgeStr = $"{v.requiredSignatures}-of-{v.totalKeys} {(MultisigAddressType)v.addressTypeInt}{coordBadge}";
                Text vBadge = CreateText(vCard.transform, badgeStr, 0, 0, 22, v.isCoordinator ? ORANGE_ACCENT : ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
                vBadge.alignment = TextAnchor.MiddleLeft;
                vBadge.rectTransform.sizeDelta = new Vector2(500f, 40f);
                vBadge.rectTransform.anchoredPosition = new Vector2(-180f, 0);

                // Truncated Address
                string shortAddr = (v.multisigAddress.Length > 24)
                    ? v.multisigAddress.Substring(0, 12) + "..." + v.multisigAddress.Substring(v.multisigAddress.Length - 10)
                    : v.multisigAddress;
                Text vAddr = CreateText(vCard.transform, shortAddr, 0, -40f, 20, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
                vAddr.alignment = TextAnchor.MiddleLeft;
                vAddr.rectTransform.sizeDelta = new Vector2(500f, 40f);
                vAddr.rectTransform.anchoredPosition = new Vector2(-180f, -40f);

                // Open Button
                var capturedVault = v;
                GameObject openBtn = CreateButton(vCard.transform, L("MS_BTN_OPEN"), 270f, 0, 160f, 65f, () => {
                    activeVault = capturedVault;
                    currentState = VaultViewState.Dashboard;
                    BuildDashboardView(contentRoot, isDesktop);
                    UpdateViewVisibility();
                });
                openBtn.GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
                var obTxt = openBtn.GetComponentInChildren<Text>();
                obTxt.color = Color.white; obTxt.fontSize = 24; obTxt.fontStyle = FontStyle.Bold;

                // Delete Button
                GameObject delBtn = CreateButton(vCard.transform, "✕", 400f, 40f, 50f, 50f, () => {
                    ShowDeleteConfirmationModal(capturedVault, isDesktop);
                });
                delBtn.GetComponent<Image>().color = Color.clear;
                var dbTxt = delBtn.GetComponentInChildren<Text>();
                dbTxt.color = new Color(0.85f, 0.25f, 0.25f); dbTxt.fontSize = 28; dbTxt.fontStyle = FontStyle.Bold;

                y -= (vCardH + 20f);
            }
        }

        content.GetComponent<RectTransform>().sizeDelta = new Vector2(isDesktop ? 900f : 960f, Mathf.Abs(y) + 60f);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. CONFIG VIEW (Step 1: Quorum Stepper, Presets, Name, Address Type)
    // ─────────────────────────────────────────────────────────────────────────────
    private void BuildConfigView(Transform parent, bool isDesktop)
    {
        if (configViewObj != null)
        {
            configViewObj.SetActive(false);
            Destroy(configViewObj);
        }
        configViewObj = CreateViewContainer(parent, "ConfigView");
        var (header, content, scrollRect) = CreateScrollableScreen(configViewObj.transform, isDesktop, L("MS_CREATE_TITLE"), () => {
            currentState = VaultViewState.Landing;
            UpdateViewVisibility();
        });

        float y = -20f;
        float cardW = isDesktop ? 860f : 920f;

        // 1. Vault Name Card
        float nameCardH = 130f;
        GameObject nameCard = CreateCard(content, 0, y, cardW, nameCardH);
        Text nameLabel = CreateText(nameCard.transform, L("MS_VAULT_NAME"), 0, 30f, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        nameLabel.alignment = TextAnchor.MiddleLeft;

        vaultNameInputField = CreateInputField(nameCard.transform, 0, -20f, isDesktop ? 820f : 870f, 65f, L("MS_VAULT_NAME_PH"));
        vaultNameInputField.text = currentVaultName;
        vaultNameInputField.onValueChanged.AddListener((val) => currentVaultName = val);
        y -= (nameCardH + 20f);

        // 2. Policy Selection Tabs (Quorum Policy vs Coordinator Policy)
        float tabRowH = 65f;
        float tabW = (cardW - 16f) / 2f;

        // Tab 1: Quorum Policy (Signatures Required)
        GameObject qTab = CreateButton(content, L("MS_QUORUM_TAB"), -(tabW / 2f + 8f), y, tabW, tabRowH, () => {
            if (selectedPolicyMode != VaultPolicyMode.Quorum)
            {
                AudioManager.Instance?.PlayClick();
                selectedPolicyMode = VaultPolicyMode.Quorum;
                BuildConfigView(parent, isDesktop);
                UpdateViewVisibility();
            }
        });
        quorumTabImg = qTab.GetComponent<Image>();
        quorumTabTxt = qTab.GetComponentInChildren<Text>();

        // Tab 2: Coordinator Policy (Watch-Only) - Vibrant Orange (#FF8C00)
        GameObject cTab = CreateButton(content, L("MS_COORDINATOR_TAB"), (tabW / 2f + 8f), y, tabW, tabRowH, () => {
            if (selectedPolicyMode != VaultPolicyMode.Coordinator)
            {
                AudioManager.Instance?.PlayClick();
                selectedPolicyMode = VaultPolicyMode.Coordinator;
                BuildConfigView(parent, isDesktop);
                UpdateViewVisibility();
            }
        });
        coordinatorTabImg = cTab.GetComponent<Image>();
        coordinatorTabTxt = cTab.GetComponentInChildren<Text>();

        if (selectedPolicyMode == VaultPolicyMode.Quorum)
        {
            quorumTabImg.color = ThemeManager.Instance.GetAccentColor();
            quorumTabTxt.color = Color.white;
            quorumTabTxt.fontStyle = FontStyle.Bold;
            quorumTabTxt.fontSize = isDesktop ? 20 : 22;

            coordinatorTabImg.color = ThemeManager.Instance.IsDarkMode ? new Color(0.20f, 0.25f, 0.35f) : new Color(0.88f, 0.91f, 0.96f);
            coordinatorTabTxt.color = ORANGE_ACCENT;
            coordinatorTabTxt.fontStyle = FontStyle.Bold;
            coordinatorTabTxt.fontSize = isDesktop ? 19 : 21;
        }
        else
        {
            quorumTabImg.color = ThemeManager.Instance.IsDarkMode ? new Color(0.20f, 0.25f, 0.35f) : new Color(0.88f, 0.91f, 0.96f);
            quorumTabTxt.color = ThemeManager.Instance.GetSecondaryTextColor();
            quorumTabTxt.fontStyle = FontStyle.Normal;
            quorumTabTxt.fontSize = isDesktop ? 19 : 21;

            coordinatorTabImg.color = ORANGE_ACCENT;
            coordinatorTabTxt.color = Color.white;
            coordinatorTabTxt.fontStyle = FontStyle.Bold;
            coordinatorTabTxt.fontSize = isDesktop ? 20 : 22;
        }

        y -= (tabRowH + 25f);

        if (selectedPolicyMode == VaultPolicyMode.Quorum)
        {
            // ── Quorum Policy (Signatures Required) ──
            float quorumCardH = 580f;
            GameObject quorumCard = CreateCard(content, 0, y, cardW, quorumCardH);
            quorumCard.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();

            Text qTitle = CreateText(quorumCard.transform, L("MS_QUORUM_TITLE"), 0, 245f, 26, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            qTitle.alignment = TextAnchor.MiddleLeft;
            qTitle.rectTransform.anchoredPosition = new Vector2(0, 245f);
            qTitle.rectTransform.sizeDelta = new Vector2(cardW - 60f, 40f);

            Text qSub = CreateText(quorumCard.transform, L("MS_QUORUM_SUB"), 0, 212f, 18, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            qSub.alignment = TextAnchor.MiddleLeft;
            qSub.rectTransform.anchoredPosition = new Vector2(0, 212f);
            qSub.rectTransform.sizeDelta = new Vector2(cardW - 60f, 30f);

            // Presets: Row 1 (n-of-n consensus)
            float pillW = (cardW - 80f) / 2f;
            float pillH = 48f;
            float pillY1 = 158f;

            GameObject p1 = CreatePresetButton(quorumCard.transform, "2 of 2 (n-of-n)", -(pillW / 2f + 10f), pillY1, pillW, pillH, () => SetQuorumPreset(2, 2));
            preset2of2Img = p1.GetComponent<Image>();
            preset2of2Txt = p1.GetComponentInChildren<Text>();

            GameObject p2 = CreatePresetButton(quorumCard.transform, "3 of 3 (n-of-n)", (pillW / 2f + 10f), pillY1, pillW, pillH, () => SetQuorumPreset(3, 3));
            preset3of3Img = p2.GetComponent<Image>();
            preset3of3Txt = p2.GetComponentInChildren<Text>();

            // Presets: Row 2 (m-of-n threshold)
            float pillY2 = 100f;

            GameObject p3 = CreatePresetButton(quorumCard.transform, "2 of 3 (m-of-n)", -(pillW / 2f + 10f), pillY2, pillW, pillH, () => SetQuorumPreset(2, 3));
            preset2of3Img = p3.GetComponent<Image>();
            preset2of3Txt = p3.GetComponentInChildren<Text>();

            GameObject p4 = CreatePresetButton(quorumCard.transform, "3 of 5 (m-of-n)", (pillW / 2f + 10f), pillY2, pillW, pillH, () => SetQuorumPreset(3, 5));
            preset3of5Img = p4.GetComponent<Image>();
            preset3of5Txt = p4.GetComponentInChildren<Text>();

            // Layout math for Steppers M & N
            float labelW = isDesktop ? 460f : 500f;
            float labelX = -(cardW / 2f) + 30f + (labelW / 2f);

            float stepValW = 70f;
            float stepBtnW = 65f;
            float stepH = 56f;
            float stepValX = (cardW / 2f) - 30f - 110f; // Center of 3-element stepper pill
            float stepMinusX = stepValX - 78f;
            float stepPlusX = stepValX + 78f;

            // Row 1: Required Signatures (M)
            float rowMY = 25f;
            Text mLabel = CreateText(quorumCard.transform, L("MS_REQ_SIGS"), 0, rowMY + 13f, 23, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            mLabel.alignment = TextAnchor.MiddleLeft;
            mLabel.rectTransform.anchoredPosition = new Vector2(labelX, rowMY + 13f);
            mLabel.rectTransform.sizeDelta = new Vector2(labelW, 32f);

            Text mSub = CreateText(quorumCard.transform, L("MS_REQ_SIGS_SUB"), 0, rowMY - 16f, 17, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            mSub.alignment = TextAnchor.MiddleLeft;
            mSub.rectTransform.anchoredPosition = new Vector2(labelX, rowMY - 16f);
            mSub.rectTransform.sizeDelta = new Vector2(labelW, 26f);

            // Stepper M controls: [-] [val] [+]
            GameObject btnMM = CreatePillButton(quorumCard.transform, "—", stepMinusX, rowMY, stepBtnW, stepH, () => ChangeRequiredSigs(-1));
            btnMMinusImg = btnMM.GetComponent<Image>();

            GameObject valMBox = CreateCard(quorumCard.transform, stepValX, rowMY, stepValW, stepH);
            valMBox.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.12f, 0.15f, 0.22f) : new Color(0.92f, 0.94f, 0.98f);
            requiredSigText = CreateText(valMBox.transform, requiredSignatures.ToString(), 0, 0, 32, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            requiredSigText.alignment = TextAnchor.MiddleCenter;
            requiredSigText.rectTransform.sizeDelta = new Vector2(stepValW, stepH);

            GameObject btnMP = CreatePillButton(quorumCard.transform, "+", stepPlusX, rowMY, stepBtnW, stepH, () => ChangeRequiredSigs(1));
            btnMPlusImg = btnMP.GetComponent<Image>();

            // Row 2: Total Co-signers (N)
            float rowNY = -65f;
            Text nLabel = CreateText(quorumCard.transform, L("MS_TOTAL_KEYS"), 0, rowNY + 13f, 23, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            nLabel.alignment = TextAnchor.MiddleLeft;
            nLabel.rectTransform.anchoredPosition = new Vector2(labelX, rowNY + 13f);
            nLabel.rectTransform.sizeDelta = new Vector2(labelW, 32f);

            Text nSub = CreateText(quorumCard.transform, L("MS_TOTAL_KEYS_SUB"), 0, rowNY - 16f, 17, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            nSub.alignment = TextAnchor.MiddleLeft;
            nSub.rectTransform.anchoredPosition = new Vector2(labelX, rowNY - 16f);
            nSub.rectTransform.sizeDelta = new Vector2(labelW, 26f);

            // Stepper N controls: [-] [val] [+]
            GameObject btnNM = CreatePillButton(quorumCard.transform, "—", stepMinusX, rowNY, stepBtnW, stepH, () => ChangeTotalKeys(-1));
            btnNMinusImg = btnNM.GetComponent<Image>();

            GameObject valNBox = CreateCard(quorumCard.transform, stepValX, rowNY, stepValW, stepH);
            valNBox.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.12f, 0.15f, 0.22f) : new Color(0.92f, 0.94f, 0.98f);
            totalKeysText = CreateText(valNBox.transform, totalKeys.ToString(), 0, 0, 32, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            totalKeysText.alignment = TextAnchor.MiddleCenter;
            totalKeysText.rectTransform.sizeDelta = new Vector2(stepValW, stepH);

            GameObject btnNP = CreatePillButton(quorumCard.transform, "+", stepPlusX, rowNY, stepBtnW, stepH, () => ChangeTotalKeys(1));
            btnNPlusImg = btnNP.GetComponent<Image>();

            // Quorum Summary Badge (At bottom of card)
            GameObject badgeCard = CreateCard(quorumCard.transform, 0, -155f, cardW - 60f, 54f);
            badgeCard.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.12f, 0.16f, 0.25f) : new Color(0.90f, 0.94f, 1f);
            quorumSummaryBadge = CreateText(badgeCard.transform, "", 0, 0, 19, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
            quorumSummaryBadge.alignment = TextAnchor.MiddleCenter;
            quorumSummaryBadge.rectTransform.sizeDelta = new Vector2(cardW - 80f, 50f);

            UpdateQuorumUI();
            y -= (quorumCardH + 30f);

            // 3. Address Type Cards Section
            Text typeTitle = CreateText(content, L("MS_SELECT_STD"), 0, y, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
            typeTitle.alignment = TextAnchor.MiddleLeft;
            y -= 45f;

            p2wshCard = CreateTypeSelectionCard(content, "⚡ Native SegWit (P2WSH)", "Recommended • Lowest network fees • bc1q...", MultisigAddressType.P2WSH, ref p2wshCheckmark, y);
            y -= (88f + 16f);

            p2shP2wshCard = CreateTypeSelectionCard(content, "🔄 Nested SegWit (P2SH-P2WSH)", "High compatibility with older Bitcoin wallets • 3...", MultisigAddressType.P2SH_P2WSH, ref p2shP2wshCheckmark, y);
            y -= (88f + 16f);

            p2shCard = CreateTypeSelectionCard(content, "🏛️ Legacy MultiSig (P2SH)", "Original Bitcoin multisig standard • 3...", MultisigAddressType.P2SH, ref p2shCheckmark, y);
            y -= (88f + 35f);

            UpdateTypeCheckmarks();

            // 4. Continue Button
            float nextBtnH = 95f;
            GameObject nextBtn = CreateButton(content, L("MS_BTN_CONTINUE"), 0, y, isDesktop ? 600f : 880f, nextBtnH, () => {
                currentState = VaultViewState.KeyInput;
                UpdateViewVisibility();
                PopulateKeyInputs();
            });
            nextBtn.GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
            var nbTxt = nextBtn.GetComponentInChildren<Text>();
            nbTxt.color = Color.white; nbTxt.fontSize = 30; nbTxt.fontStyle = FontStyle.Bold;

            y -= (nextBtnH + 40f);
        }
        else
        {
            // ── Coordinator Policy (Watch-Only) Section ──
            // Notice: 12-Word Mnemonic generation is strictly disabled/hidden here
            useDedicatedMnemonic = false;
            generatedVaultMnemonic = "";

            if (requiredSignatures >= totalKeys)
            {
                totalKeys = 3;
                requiredSignatures = 2;
            }

            float coordCardH = 580f;
            GameObject coordCard = CreateCard(content, 0, y, cardW, coordCardH);
            coordCard.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();

            // Title: 👁️ Coordinator Policy (Watch-Only) in Orange #FF8C00
            Text cTitle = CreateText(coordCard.transform, L("MS_COORDINATOR_TITLE"), 0, 245f, 26, ORANGE_ACCENT, FontStyle.Bold);
            cTitle.alignment = TextAnchor.MiddleLeft;
            cTitle.rectTransform.anchoredPosition = new Vector2(0, 245f);
            cTitle.rectTransform.sizeDelta = new Vector2(cardW - 60f, 40f);

            // Subtitle: Threshold Quorum policy • No private keys stored on device
            Text cSub = CreateText(coordCard.transform, "Watch-Only Quorum Threshold Policy • No private keys stored on device", 0, 212f, 18, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            cSub.alignment = TextAnchor.MiddleLeft;
            cSub.rectTransform.anchoredPosition = new Vector2(0, 212f);
            cSub.rectTransform.sizeDelta = new Vector2(cardW - 60f, 30f);

            // Presets: Threshold Presets Only (Explicitly excluding n-of-n consensus options!)
            float pillW = (cardW - 80f) / 2f;
            float pillH = 48f;
            float pillY = 158f;

            GameObject p3 = CreatePresetButton(coordCard.transform, "2 of 3 (m-of-n)", -(pillW / 2f + 10f), pillY, pillW, pillH, () => SetQuorumPreset(2, 3));
            preset2of3Img = p3.GetComponent<Image>();
            preset2of3Txt = p3.GetComponentInChildren<Text>();

            GameObject p4 = CreatePresetButton(coordCard.transform, "3 of 5 (m-of-n)", (pillW / 2f + 10f), pillY, pillW, pillH, () => SetQuorumPreset(3, 5));
            preset3of5Img = p4.GetComponent<Image>();
            preset3of5Txt = p4.GetComponentInChildren<Text>();

            // Layout math for Steppers M & N
            float labelW = isDesktop ? 460f : 500f;
            float labelX = -(cardW / 2f) + 30f + (labelW / 2f);

            float stepValW = 70f;
            float stepBtnW = 65f;
            float stepH = 56f;
            float stepValX = (cardW / 2f) - 30f - 110f;
            float stepMinusX = stepValX - 78f;
            float stepPlusX = stepValX + 78f;

            // Row 1: Required Signatures (M)
            float rowMY = 25f;
            Text mLabel = CreateText(coordCard.transform, L("MS_REQ_SIGS"), 0, rowMY + 13f, 23, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            mLabel.alignment = TextAnchor.MiddleLeft;
            mLabel.rectTransform.anchoredPosition = new Vector2(labelX, rowMY + 13f);
            mLabel.rectTransform.sizeDelta = new Vector2(labelW, 32f);

            Text mSub = CreateText(coordCard.transform, "Number of co-signers required to authorize a transaction", 0, rowMY - 16f, 17, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            mSub.alignment = TextAnchor.MiddleLeft;
            mSub.rectTransform.anchoredPosition = new Vector2(labelX, rowMY - 16f);
            mSub.rectTransform.sizeDelta = new Vector2(labelW, 26f);

            // Stepper M controls: [-] [val] [+]
            GameObject btnMM = CreatePillButton(coordCard.transform, "—", stepMinusX, rowMY, stepBtnW, stepH, () => ChangeRequiredSigs(-1));
            btnMMinusImg = btnMM.GetComponent<Image>();

            GameObject valMBox = CreateCard(coordCard.transform, stepValX, rowMY, stepValW, stepH);
            valMBox.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.12f, 0.15f, 0.22f) : new Color(0.92f, 0.94f, 0.98f);
            requiredSigText = CreateText(valMBox.transform, requiredSignatures.ToString(), 0, 0, 32, ORANGE_ACCENT, FontStyle.Bold);
            requiredSigText.alignment = TextAnchor.MiddleCenter;
            requiredSigText.rectTransform.sizeDelta = new Vector2(stepValW, stepH);

            GameObject btnMP = CreatePillButton(coordCard.transform, "+", stepPlusX, rowMY, stepBtnW, stepH, () => ChangeRequiredSigs(1));
            btnMPlusImg = btnMP.GetComponent<Image>();

            // Row 2: Total Co-signers (N)
            float rowNY = -65f;
            Text nLabel = CreateText(coordCard.transform, L("MS_TOTAL_KEYS"), 0, rowNY + 13f, 23, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            nLabel.alignment = TextAnchor.MiddleLeft;
            nLabel.rectTransform.anchoredPosition = new Vector2(labelX, rowNY + 13f);
            nLabel.rectTransform.sizeDelta = new Vector2(labelW, 32f);

            Text nSub = CreateText(coordCard.transform, "Total number of participating co-signer public keys", 0, rowNY - 16f, 17, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
            nSub.alignment = TextAnchor.MiddleLeft;
            nSub.rectTransform.anchoredPosition = new Vector2(labelX, rowNY - 16f);
            nSub.rectTransform.sizeDelta = new Vector2(labelW, 26f);

            // Stepper N controls: [-] [val] [+]
            GameObject btnNM = CreatePillButton(coordCard.transform, "—", stepMinusX, rowNY, stepBtnW, stepH, () => ChangeTotalKeys(-1));
            btnNMinusImg = btnNM.GetComponent<Image>();

            GameObject valNBox = CreateCard(coordCard.transform, stepValX, rowNY, stepValW, stepH);
            valNBox.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.12f, 0.15f, 0.22f) : new Color(0.92f, 0.94f, 0.98f);
            totalKeysText = CreateText(valNBox.transform, totalKeys.ToString(), 0, 0, 32, ORANGE_ACCENT, FontStyle.Bold);
            totalKeysText.alignment = TextAnchor.MiddleCenter;
            totalKeysText.rectTransform.sizeDelta = new Vector2(stepValW, stepH);

            GameObject btnNP = CreatePillButton(coordCard.transform, "+", stepPlusX, rowNY, stepBtnW, stepH, () => ChangeTotalKeys(1));
            btnNPlusImg = btnNP.GetComponent<Image>();

            // Quorum Summary Badge (At bottom of card)
            GameObject badgeCard = CreateCard(coordCard.transform, 0, -155f, cardW - 60f, 54f);
            badgeCard.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.25f, 0.18f, 0.12f) : new Color(1.0f, 0.94f, 0.88f);
            quorumSummaryBadge = CreateText(badgeCard.transform, "", 0, 0, 19, ORANGE_ACCENT, FontStyle.Bold);
            quorumSummaryBadge.alignment = TextAnchor.MiddleCenter;
            quorumSummaryBadge.rectTransform.sizeDelta = new Vector2(cardW - 80f, 50f);

            UpdateQuorumUI();
            y -= (coordCardH + 30f);

            // 3. Address Type Cards Section
            Text typeTitle = CreateText(content, L("MS_SELECT_STD"), 0, y, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
            typeTitle.alignment = TextAnchor.MiddleLeft;
            y -= 45f;

            p2wshCard = CreateTypeSelectionCard(content, "⚡ Native SegWit (P2WSH)", "Recommended • Lowest network fees • bc1q...", MultisigAddressType.P2WSH, ref p2wshCheckmark, y);
            y -= (88f + 16f);

            p2shP2wshCard = CreateTypeSelectionCard(content, "🔄 Nested SegWit (P2SH-P2WSH)", "High compatibility with older Bitcoin wallets • 3...", MultisigAddressType.P2SH_P2WSH, ref p2shP2wshCheckmark, y);
            y -= (88f + 16f);

            p2shCard = CreateTypeSelectionCard(content, "🏛️ Legacy MultiSig (P2SH)", "Original Bitcoin multisig standard • 3...", MultisigAddressType.P2SH, ref p2shCheckmark, y);
            y -= (88f + 35f);

            UpdateTypeCheckmarks();

            // 4. Continue Button (Orange #FF8C00)
            float nextBtnH = 95f;
            GameObject nextBtn = CreateButton(content, L("MS_BTN_CONTINUE"), 0, y, isDesktop ? 600f : 880f, nextBtnH, () => {
                currentState = VaultViewState.KeyInput;
                UpdateViewVisibility();
                PopulateKeyInputs();
            });
            nextBtn.GetComponent<Image>().color = ORANGE_ACCENT;
            var nbTxt = nextBtn.GetComponentInChildren<Text>();
            nbTxt.color = Color.white; nbTxt.fontSize = 30; nbTxt.fontStyle = FontStyle.Bold;

            y -= (nextBtnH + 20f);

            // 5. Secondary Option: Import via raw redeem script hex
            GameObject importScriptBtn = CreateButton(content, "Or Import via Raw Redeem Script Hex", 0, y, isDesktop ? 600f : 880f, 65f, () => {
                currentState = VaultViewState.Import;
                UpdateViewVisibility();
            });
            importScriptBtn.GetComponent<Image>().color = new Color(0.28f, 0.32f, 0.42f);
            var isbTxt = importScriptBtn.GetComponentInChildren<Text>();
            isbTxt.color = Color.white; isbTxt.fontSize = 22; isbTxt.fontStyle = FontStyle.Normal;

            y -= 85f;
        }

        content.GetComponent<RectTransform>().sizeDelta = new Vector2(isDesktop ? 900f : 960f, Mathf.Abs(y) + 60f);
    }

    private void OnCreateCoordinatorVault()
    {
        try
        {
            AudioManager.Instance?.PlayClick();
            string rawInput = (coordinatorScriptInput != null) ? coordinatorScriptInput.text.Trim() : "";
            if (string.IsNullOrEmpty(rawInput))
            {
                throw new Exception("Please paste the Redeem Script or Output Descriptor.");
            }

            int m = 2;
            int n = 2;
            List<string> keysHex = new List<string>();
            string derivedAddr = "";
            string redeemScriptHex = "";

            string cleanHex = rawInput.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            bool isHex = System.Text.RegularExpressions.Regex.IsMatch(cleanHex, @"\A\b[0-9a-fA-F]+\b\Z");

            if (isHex && cleanHex.Length >= 10)
            {
                var parsed = MultisigCore.ParseRedeemScript(cleanHex, selectedAddressType, isTestNet);
                m = parsed.m;
                n = parsed.n;
                redeemScriptHex = cleanHex;
                derivedAddr = parsed.address;
                foreach (var pk in parsed.pubKeys)
                {
                    keysHex.Add(pk.ToHex());
                }
            }
            else
            {
                throw new Exception("Invalid script format. Please provide a valid Hex Redeem Script.");
            }

            bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
            isDesktop = true;
#elif UNITY_EDITOR
            isDesktop = (Screen.width > Screen.height);
#endif

            activeVault = new MultisigVaultConfig
            {
                vaultId = System.Guid.NewGuid().ToString(),
                vaultName = string.IsNullOrEmpty(currentVaultName) ? $"Coordinator Vault {allVaults.Count + 1}" : currentVaultName,
                requiredSignatures = m,
                totalKeys = n,
                addressTypeInt = (int)selectedAddressType,
                isTestNet = isTestNet,
                publicKeysHex = keysHex,
                redeemScriptHex = redeemScriptHex,
                multisigAddress = derivedAddr,
                myPublicKeyHex = (keysHex.Count > 0) ? keysHex[0] : "",
                vaultMnemonic = "", // Strict: NO 12-word mnemonic stored for watch-only coordinator!
                key2Mnemonic = "",
                derivationPath = isTestNet ? "m/48'/1'/0'/2'" : "m/48'/0'/0'/2'",
                vaultSerial = SatoliteSerialService.GenerateSerialNumber(),
                isCoordinator = true,
                descriptor = rawInput
            };

            MultisigCore.SaveVault(activeVault);
            RefreshVaultsList();

            // Item 10: Copy BIP 84 Zpub key to clipboard after multisig creation
            try
            {
                var mainWalletKey = MultisigCore.GetMainWalletExtendedPubKey(isTestNet);
                if (!string.IsNullOrEmpty(mainWalletKey.zpub))
                {
                    GUIUtility.systemCopyBuffer = mainWalletKey.zpub;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MultisigVaultUI] Failed to copy Zpub: " + ex.Message);
            }

            currentState = VaultViewState.Dashboard;
            BuildDashboardView(contentRoot, isDesktop);
            UpdateViewVisibility();
            PopulateDashboard();
        }
        catch (Exception ex)
        {
            if (coordinatorErrorText != null)
            {
                coordinatorErrorText.text = "Error: " + ex.Message;
                coordinatorErrorText.color = Color.red;
            }
        }
    }

    private GameObject CreateTypeSelectionCard(Transform parent, string title, string subtitle, MultisigAddressType type, ref Text checkmarkRef, float y)
    {
        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif
        float cardH = 88f;
        GameObject card = CreateCard(parent, 0, y, isDesktop ? 860f : 920f, cardH);
        card.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();

        Button btn = card.AddComponent<Button>();
        btn.targetGraphic = card.GetComponent<Image>();
        btn.onClick.AddListener(() => {
            AudioManager.Instance?.PlayClick();
            selectedAddressType = type;
            UpdateTypeCheckmarks();
        });

        Text t = CreateText(card.transform, title, 0, 16f, 24, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        t.alignment = TextAnchor.MiddleLeft;
        t.rectTransform.anchoredPosition = new Vector2(-60f, 16f);
        t.rectTransform.sizeDelta = new Vector2(680f, 35f);

        Text s = CreateText(card.transform, subtitle, 0, -18f, 18, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        s.alignment = TextAnchor.MiddleLeft;
        s.rectTransform.anchoredPosition = new Vector2(-60f, -18f);
        s.rectTransform.sizeDelta = new Vector2(680f, 30f);

        // Circular Radio Badge
        GameObject radioBg = CreateCard(card.transform, 380f, 0, 52f, 52f);
        radioBg.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.14f, 0.18f, 0.28f) : new Color(0.88f, 0.92f, 0.98f);

        Text chk = CreateText(radioBg.transform, "✓", 0, 0, 30, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
        chk.alignment = TextAnchor.MiddleCenter;
        chk.rectTransform.sizeDelta = new Vector2(52f, 52f);
        checkmarkRef = chk;

        return card;
    }

    private void UpdateTypeCheckmarks()
    {
        bool p2wshSel = (selectedAddressType == MultisigAddressType.P2WSH);
        bool p2shP2wshSel = (selectedAddressType == MultisigAddressType.P2SH_P2WSH);
        bool p2shSel = (selectedAddressType == MultisigAddressType.P2SH);

        Color selCardColor = ThemeManager.Instance.IsDarkMode ? new Color(0.14f, 0.20f, 0.32f) : new Color(0.90f, 0.94f, 1f);
        Color defCardColor = ThemeManager.Instance.GetCardColor();

        if (p2wshCheckmark != null) p2wshCheckmark.gameObject.SetActive(p2wshSel);
        if (p2shP2wshCheckmark != null) p2shP2wshCheckmark.gameObject.SetActive(p2shP2wshSel);
        if (p2shCheckmark != null) p2shCheckmark.gameObject.SetActive(p2shSel);

        if (p2wshCard != null) p2wshCard.GetComponent<Image>().color = p2wshSel ? selCardColor : defCardColor;
        if (p2shP2wshCard != null) p2shP2wshCard.GetComponent<Image>().color = p2shP2wshSel ? selCardColor : defCardColor;
        if (p2shCard != null) p2shCard.GetComponent<Image>().color = p2shSel ? selCardColor : defCardColor;
    }

    private void SetQuorumPreset(int m, int n)
    {
        AudioManager.Instance?.PlayClick();
        requiredSignatures = m;
        totalKeys = n;
        UpdateQuorumUI();
    }

    private void ChangeRequiredSigs(int delta)
    {
        AudioManager.Instance?.PlayClick();
        if (selectedPolicyMode == VaultPolicyMode.Coordinator)
        {
            if (delta > 0)
            {
                if (requiredSignatures < totalKeys - 1)
                {
                    requiredSignatures++;
                }
                else if (totalKeys < 7)
                {
                    totalKeys++;
                    requiredSignatures++;
                }
            }
            else if (delta < 0)
            {
                if (requiredSignatures > 1)
                    requiredSignatures--;
            }
        }
        else
        {
            if (delta > 0)
            {
                if (requiredSignatures < totalKeys)
                {
                    requiredSignatures++;
                }
                else if (totalKeys < 7)
                {
                    totalKeys++;
                    requiredSignatures++;
                }
            }
            else if (delta < 0)
            {
                if (requiredSignatures > 1)
                    requiredSignatures--;
            }
        }

        UpdateQuorumUI();
    }

    private void ChangeTotalKeys(int delta)
    {
        AudioManager.Instance?.PlayClick();
        if (selectedPolicyMode == VaultPolicyMode.Coordinator)
        {
            if (delta > 0)
            {
                if (totalKeys < 7) totalKeys++;
            }
            else if (delta < 0)
            {
                if (totalKeys > 3)
                {
                    totalKeys--;
                    if (requiredSignatures >= totalKeys)
                        requiredSignatures = totalKeys - 1;
                }
            }
        }
        else
        {
            if (delta > 0)
            {
                if (totalKeys < 7) totalKeys++;
            }
            else if (delta < 0)
            {
                if (totalKeys > 2)
                {
                    totalKeys--;
                    if (requiredSignatures > totalKeys)
                        requiredSignatures = totalKeys;
                }
            }
        }

        UpdateQuorumUI();
    }

    private void UpdateQuorumUI()
    {
        if (requiredSigText != null) requiredSigText.text = requiredSignatures.ToString();
        if (totalKeysText != null) totalKeysText.text = totalKeys.ToString();

        // Update Presets styling
        bool is2of2 = (requiredSignatures == 2 && totalKeys == 2);
        bool is3of3 = (requiredSignatures == 3 && totalKeys == 3);
        bool is2of3 = (requiredSignatures == 2 && totalKeys == 3);
        bool is3of5 = (requiredSignatures == 3 && totalKeys == 5);

        Color activePillBg = (selectedPolicyMode == VaultPolicyMode.Coordinator) ? ORANGE_ACCENT : ThemeManager.Instance.GetAccentColor();
        Color defaultPillBg = ThemeManager.Instance.IsDarkMode ? new Color(0.20f, 0.25f, 0.35f) : new Color(0.88f, 0.91f, 0.96f);

        if (preset2of2Img != null)
        {
            preset2of2Img.color = is2of2 ? activePillBg : defaultPillBg;
            if (preset2of2Txt != null) preset2of2Txt.color = is2of2 ? Color.white : ThemeManager.Instance.GetTextColor();
        }
        if (preset3of3Img != null)
        {
            preset3of3Img.color = is3of3 ? activePillBg : defaultPillBg;
            if (preset3of3Txt != null) preset3of3Txt.color = is3of3 ? Color.white : ThemeManager.Instance.GetTextColor();
        }
        if (preset2of3Img != null)
        {
            preset2of3Img.color = is2of3 ? activePillBg : defaultPillBg;
            if (preset2of3Txt != null) preset2of3Txt.color = is2of3 ? Color.white : ThemeManager.Instance.GetTextColor();
        }
        if (preset3of5Img != null)
        {
            preset3of5Img.color = is3of5 ? activePillBg : defaultPillBg;
            if (preset3of5Txt != null) preset3of5Txt.color = is3of5 ? Color.white : ThemeManager.Instance.GetTextColor();
        }

        // Stepper button alphas
        bool isCoord = (selectedPolicyMode == VaultPolicyMode.Coordinator);
        if (btnMMinusImg != null) btnMMinusImg.color = new Color(btnMMinusImg.color.r, btnMMinusImg.color.g, btnMMinusImg.color.b, (requiredSignatures <= 1) ? 0.35f : 1f);
        if (btnNMinusImg != null) btnNMinusImg.color = new Color(btnNMinusImg.color.r, btnNMinusImg.color.g, btnNMinusImg.color.b, (isCoord ? totalKeys <= 3 : totalKeys <= 2) ? 0.35f : 1f);
        if (btnNPlusImg != null) btnNPlusImg.color = new Color(btnNPlusImg.color.r, btnNPlusImg.color.g, btnNPlusImg.color.b, (totalKeys >= 7) ? 0.35f : 1f);

        // Quorum Summary Badge text
        if (quorumSummaryBadge != null)
        {
            if (isCoord)
            {
                quorumSummaryBadge.text = $"👁️ {requiredSignatures} of {totalKeys} Watch-Only Vault • Any {requiredSignatures} of {totalKeys} keys sign • No private keys on device";
            }
            else if (requiredSignatures == totalKeys)
                quorumSummaryBadge.text = $"🔒 {requiredSignatures} of {totalKeys} (n-of-n) Vault • Every key must sign (Full Consensus)";
            else if (requiredSignatures == 2 && totalKeys == 3)
                quorumSummaryBadge.text = $"🛡️ 2 of 3 (m-of-n) Vault • Any 2 keys can spend (1 key can be lost safely)";
            else if (requiredSignatures == 3 && totalKeys == 5)
                quorumSummaryBadge.text = $"🏛️ 3 of 5 (m-of-n) Vault • Any 3 keys can spend (Tolerates loss of up to 2 keys)";
            else
                quorumSummaryBadge.text = $"💡 {requiredSignatures} of {totalKeys} (m-of-n) Vault • Any {requiredSignatures} out of {totalKeys} keys can spend ({totalKeys - requiredSignatures} backup key(s))";
        }
    }

    private void GenerateNewVaultKey()
    {
        var (mnem, pubKey, _, zpub) = MultisigCore.GenerateVaultKeypair(isTestNet);
        generatedVaultMnemonic = mnem;
        generatedVaultPubKeyHex = pubKey.ToHex();
        generatedVaultZpub = zpub;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. KEY INPUT VIEW (Step 2: Keys Setup with Dedicated 12-words & Co-signers)
    // ─────────────────────────────────────────────────────────────────────────────
    private void BuildKeyInputView(Transform parent, bool isDesktop)
    {
        if (keyInputViewObj != null)
        {
            keyInputViewObj.SetActive(false);
            Destroy(keyInputViewObj);
        }
        keyInputViewObj = CreateViewContainer(parent, "KeyInputView");
        var (header, content, scrollRect) = CreateScrollableScreen(keyInputViewObj.transform, isDesktop, L("MS_CREATE_TITLE"), () => {
            ScreenSecurityManager.DisableProtection();
            currentState = VaultViewState.Config;
            UpdateViewVisibility();
        });

        keyInputSubtitleText = CreateText(content, $"Configure keys for {requiredSignatures}-of-{totalKeys} vault", 0, -20f, 24, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        keyInputSubtitleText.alignment = TextAnchor.MiddleCenter;

        keyInputErrorText = CreateText(content, "", 0, -60f, 22, Color.red, FontStyle.Normal);
        keyInputErrorText.alignment = TextAnchor.MiddleCenter;
    }

    private void PopulateKeyInputs()
    {
        if (keyInputViewObj == null) return;
        Transform content = keyInputViewObj.GetComponentInChildren<ScrollRect>().content;

        bool isWatchOnly = (selectedPolicyMode == VaultPolicyMode.Coordinator);

        if (keyInputSubtitleText != null)
            keyInputSubtitleText.text = isWatchOnly
                ? $"Enter public keys for all {totalKeys} co-signers ({requiredSignatures}-of-{totalKeys} Watch-Only)"
                : $"Enter public keys for {requiredSignatures}-of-{totalKeys} vault";

        if (keyInputErrorText != null) keyInputErrorText.text = "";

        // Remove old dynamic row elements
        foreach (var f in keyInputFields)
        {
            if (f != null && f.transform.parent != null)
                Destroy(f.transform.parent.gameObject);
        }
        keyInputFields.Clear();

        var oldMCard = content.Find("MnemonicBoxCard");
        if (oldMCard != null) Destroy(oldMCard.gameObject);
        var oldBtn1 = content.Find("Btn_⚡ Auto-Generate Test Keys (For TestNet)");
        if (oldBtn1 != null) Destroy(oldBtn1.gameObject);
        var oldBtn2 = content.Find("Btn_✓ Create Multisig Vault");
        if (oldBtn2 != null) Destroy(oldBtn2.gameObject);

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        float y = -100f;

        if (isWatchOnly)
        {
            // Watch-Only: NO 12-word mnemonic generated. Enter public keys for all co-signers!
            useDedicatedMnemonic = false;
            generatedVaultMnemonic = "";
            generatedVaultPubKeyHex = "";

            for (int i = 0; i < totalKeys; i++)
            {
                int keyNum = i + 1;
                CreateKeyRow(content, keyNum, $"Key #{keyNum} (Co-signer Zpub / PubKey):", "", false, ref y);
            }
        }
        else
        {
            // Signer / Quorum mode: Dedicated 12-Word Passphrase for Key #1
            ScreenSecurityManager.EnableProtection();

            if (string.IsNullOrEmpty(generatedVaultMnemonic))
                GenerateNewVaultKey();

            float mCardH = 220f;
            GameObject mCard = CreateCard(content, 0, y, isDesktop ? 860f : 920f, mCardH);
            mCard.name = "MnemonicBoxCard";
            mCard.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.12f, 0.17f, 0.26f) : new Color(0.92f, 0.96f, 1f);

            Text mTitle = CreateText(mCard.transform, L("MS_KEY1_TITLE"), 0, 70f, 22, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            mTitle.alignment = TextAnchor.MiddleCenter;

            Text mWords = CreateText(mCard.transform, generatedVaultMnemonic, 0, 15f, 22, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
            mWords.alignment = TextAnchor.MiddleCenter;
            mWords.horizontalOverflow = HorizontalWrapMode.Wrap;
            mWords.rectTransform.sizeDelta = new Vector2(850f, 60f);

            // Copy Words Button
            CreateButton(mCard.transform, L("MS_BTN_COPY_12"), -150f, -60f, 240f, 55f, () => {
                GUIUtility.systemCopyBuffer = generatedVaultMnemonic;
                if (keyInputErrorText != null) { keyInputErrorText.text = L("MS_COPIED_12"); keyInputErrorText.color = ORANGE_ACCENT; }
                AudioManager.Instance?.PlayCopy();
            });

            // New Words Button
            CreateButton(mCard.transform, L("MS_BTN_NEW_WORDS"), 150f, -60f, 240f, 55f, () => {
                GenerateNewVaultKey();
                PopulateKeyInputs();
            });

            y -= (mCardH + 25f);

            // Input row for Key #1 (ReadOnly, displaying Zpub or Hex)
            string key1Display = !string.IsNullOrEmpty(generatedVaultZpub) ? generatedVaultZpub : generatedVaultPubKeyHex;
            string key1Label = isTestNet ? "Key #1 (Your vpub / PubKey):" : "Key #1 (Your Zpub / PubKey):";
            CreateKeyRow(content, 1, key1Label, key1Display, true, ref y);

            // ── KEY #2 to KEY #N (Co-signers) ──
            for (int i = 1; i < totalKeys; i++)
            {
                int keyNum = i + 1;
                CreateKeyRow(content, keyNum, $"Key #{keyNum} (Co-signer Zpub / PubKey):", "", false, ref y);
            }
        }

        // TestNet Helper Button
#if TESTNET_BUILD
        float mockBtnH = 75f;
        GameObject mockBtn = CreateButton(content, L("MS_BTN_AUTOGEN"), 0, y, isDesktop ? 600f : 880f, mockBtnH, OnGenerateMockKeys);
        mockBtn.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.75f, 0.85f);
        var mbTxt = mockBtn.GetComponentInChildren<Text>();
        mbTxt.color = Color.white; mbTxt.fontSize = 26; mbTxt.fontStyle = FontStyle.Bold;
        y -= (mockBtnH + 20f);
#endif

        // Create Vault Action Button
        float createBtnH = 95f;
        GameObject createVaultBtn = CreateButton(content, isWatchOnly ? "✓ Create Watch-Only Vault" : L("MS_BTN_CREATE_CONFIRM"), 0, y, isDesktop ? 600f : 880f, createBtnH, OnCreateVault);
        createVaultBtn.GetComponent<Image>().color = isWatchOnly ? ORANGE_ACCENT : ThemeManager.Instance.GetAccentColor();
        var cvTxt = createVaultBtn.GetComponentInChildren<Text>();
        cvTxt.color = Color.white; cvTxt.fontSize = 32; cvTxt.fontStyle = FontStyle.Bold;

        y -= (createBtnH + 40f);
        content.GetComponent<RectTransform>().sizeDelta = new Vector2(isDesktop ? 900f : 960f, Mathf.Abs(y) + 60f);
    }

    private GameObject CreateKeyRow(Transform content, int index, string labelStr, string defaultVal, bool isReadOnly, ref float y)
    {
        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        float cardW = isDesktop ? 860f : 920f;
        float rowH = 130f;
        GameObject row = CreateCard(content, 0, y, cardW, rowH);

        // Label
        Text lbl = CreateText(row.transform, labelStr, 0, 32f, 22, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        lbl.alignment = TextAnchor.MiddleLeft;

        float pasteW = isDesktop ? 120f : 130f;
        float gap = 14f;
        float totalInnerW = cardW - 60f;

        float copyW = 120f;
        float inputW = isReadOnly ? (totalInnerW - copyW - gap) : (totalInnerW - pasteW - gap);
        float inputX = ((-totalInnerW / 2f) + (inputW / 2f));

        // Input field
        InputField input = CreateInputField(row.transform, inputX, -22f, inputW, 65f, "Enter Zpub (BIP84) or PubKey Hex...");
        keyInputFields.Add(input);

        if (!string.IsNullOrEmpty(defaultVal))
            input.text = defaultVal;

        if (isReadOnly)
        {
            input.interactable = false;
            float copyX = (totalInnerW / 2f) - (copyW / 2f);
            CreateButton(row.transform, L("GEN_COPY"), copyX, -22f, copyW, 65f, () => {
                GUIUtility.systemCopyBuffer = defaultVal;
                if (keyInputErrorText != null) { keyInputErrorText.text = "Key #1 Copied!"; keyInputErrorText.color = ORANGE_ACCENT; }
                AudioManager.Instance?.PlayCopy();
            });
        }
        else
        {
            // Paste Button strictly inside card boundaries
            float pasteX = (totalInnerW / 2f) - (pasteW / 2f);
            CreateButton(row.transform, L("MS_BTN_PASTE"), pasteX, -22f, pasteW, 65f, () => {
                input.text = GUIUtility.systemCopyBuffer;
                AudioManager.Instance?.PlayClick();
            });
        }

        y -= (rowH + 20f);
        return row;
    }

    private void OnGenerateMockKeys()
    {
        AudioManager.Instance?.PlayClick();
        if (keyInputFields.Count > 1)
        {
            var (mnem2, pubKey2, _, zpub2) = MultisigCore.GenerateVaultKeypair(isTestNet);
            generatedKey2Mnemonic = mnem2;
            keyInputFields[1].text = !string.IsNullOrEmpty(zpub2) ? zpub2 : pubKey2.ToHex();
        }
        for (int i = 2; i < keyInputFields.Count; i++)
        {
            var (_, pubKeyR, _, zpubR) = MultisigCore.GenerateVaultKeypair(isTestNet);
            keyInputFields[i].text = !string.IsNullOrEmpty(zpubR) ? zpubR : pubKeyR.ToHex();
        }
        if (keyInputErrorText != null)
        {
            keyInputErrorText.text = L("MS_DEMO_KEYS_GENERATED");
            keyInputErrorText.color = ORANGE_ACCENT;
        }
    }

    private void OnCreateVault()
    {
        try
        {
            AudioManager.Instance?.PlayClick();
            List<NBitcoin.PubKey> pubKeys = new List<NBitcoin.PubKey>();
            List<string> keysHex = new List<string>();

            for (int i = 0; i < keyInputFields.Count; i++)
            {
                string raw = keyInputFields[i].text.Trim();
                if (string.IsNullOrEmpty(raw))
                    throw new Exception($"Key #{i + 1} is empty!");

                var (pk, hex, _) = MultisigCore.ParseZpubOrPubKey(raw, isTestNet);
                pubKeys.Add(pk);
                keysHex.Add(hex);
            }

            var result = MultisigCore.GenerateMultisigAddress(requiredSignatures, pubKeys, selectedAddressType, isTestNet);

            bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
            isDesktop = true;
#elif UNITY_EDITOR
            isDesktop = (Screen.width > Screen.height);
#endif

            bool isWatchOnly = (selectedPolicyMode == VaultPolicyMode.Coordinator);

            activeVault = new MultisigVaultConfig
            {
                vaultId = System.Guid.NewGuid().ToString(),
                vaultName = string.IsNullOrEmpty(currentVaultName) ? $"{requiredSignatures}-of-{totalKeys} Vault" : currentVaultName,
                requiredSignatures = requiredSignatures,
                totalKeys = totalKeys,
                addressTypeInt = (int)selectedAddressType,
                isTestNet = isTestNet,
                publicKeysHex = keysHex,
                redeemScriptHex = result.redeemScriptHex,
                multisigAddress = result.address,
                myPublicKeyHex = keysHex[0],
                vaultMnemonic = (!isWatchOnly && useDedicatedMnemonic) ? generatedVaultMnemonic : "",
                key2Mnemonic = isWatchOnly ? "" : generatedKey2Mnemonic,
                derivationPath = isTestNet ? "m/48'/1'/0'/2'" : "m/48'/0'/0'/2'",
                vaultSerial = SatoliteSerialService.GenerateSerialNumber(),
                isCoordinator = isWatchOnly
            };

            MultisigCore.SaveVault(activeVault);
            RefreshVaultsList();

            // Item 10: Copy BIP 84 Zpub key to clipboard after multisig creation
            try
            {
                var mainWalletKey = MultisigCore.GetMainWalletExtendedPubKey(isTestNet);
                if (!string.IsNullOrEmpty(mainWalletKey.zpub))
                {
                    GUIUtility.systemCopyBuffer = mainWalletKey.zpub;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MultisigVaultUI] Failed to copy Zpub: " + ex.Message);
            }

            ScreenSecurityManager.DisableProtection();

            currentState = VaultViewState.Dashboard;
            BuildDashboardView(contentRoot, isDesktop);
            UpdateViewVisibility();
            PopulateDashboard();
        }
        catch (Exception ex)
        {
            if (keyInputErrorText != null)
            {
                keyInputErrorText.text = "Error: " + ex.Message;
                keyInputErrorText.color = Color.red;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4. IMPORT VIEW (Import via Redeem Script / Backup)
    // ─────────────────────────────────────────────────────────────────────────────
    private void BuildImportView(Transform parent, bool isDesktop)
    {
        if (importViewObj != null)
        {
            importViewObj.SetActive(false);
            Destroy(importViewObj);
        }
        importViewObj = CreateViewContainer(parent, "ImportView");
        var (header, content, scrollRect) = CreateScrollableScreen(importViewObj.transform, isDesktop, L("MS_IMPORT_TITLE"), () => {
            currentState = VaultViewState.Landing;
            UpdateViewVisibility();
        });

        float y = -20f;

        // Vault Name Card
        float nameCardH = 130f;
        GameObject nameCard = CreateCard(content, 0, y, isDesktop ? 860f : 920f, nameCardH);
        Text nameLbl = CreateText(nameCard.transform, L("MS_VAULT_NAME"), 0, 30f, 22, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        nameLbl.alignment = TextAnchor.MiddleLeft;

        importNameInput = CreateInputField(nameCard.transform, 0, -20f, isDesktop ? 820f : 870f, 65f, "e.g. Imported Savings Vault");
        importNameInput.text = L("MS_IMPORTED_VAULT_NAME");
        y -= (nameCardH + 25f);

        // Redeem Script Card
        float scriptCardH = 160f;
        GameObject scriptCard = CreateCard(content, 0, y, isDesktop ? 860f : 920f, scriptCardH);
        Text scriptLbl = CreateText(scriptCard.transform, L("MS_PASTE_REDEEM"), 0, 45f, 22, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        scriptLbl.alignment = TextAnchor.MiddleLeft;

        float impTotalInnerW = (isDesktop ? 860f : 920f) - 60f;
        float impPasteW = 120f;
        float impInputW = impTotalInnerW - impPasteW - 14f;
        float impInputX = (-impTotalInnerW / 2f) + (impInputW / 2f);
        float impPasteX = (impTotalInnerW / 2f) - (impPasteW / 2f);

        importRedeemScriptInput = CreateInputField(scriptCard.transform, impInputX, -15f, impInputW, 70f, "522102...52ae");
        CreateButton(scriptCard.transform, L("MS_BTN_PASTE"), impPasteX, -15f, impPasteW, 70f, () => {
            importRedeemScriptInput.text = GUIUtility.systemCopyBuffer;
            AudioManager.Instance?.PlayClick();
        });
        y -= (scriptCardH + 20f);

        // Error message text
        importErrorText = CreateText(content, "", 0, y, 22, Color.red, FontStyle.Normal);
        importErrorText.alignment = TextAnchor.MiddleCenter;
        y -= 45f;

        // Import Button
        float importBtnH = 90f;
        GameObject importBtn = CreateButton(content, L("MS_BTN_IMPORT_CONFIRM"), 0, y, isDesktop ? 600f : 880f, importBtnH, OnExecuteImport);
        importBtn.GetComponent<Image>().color = new Color(0.18f, 0.55f, 0.35f);
        var ibTxt = importBtn.GetComponentInChildren<Text>();
        ibTxt.color = Color.white; ibTxt.fontSize = 30; ibTxt.fontStyle = FontStyle.Bold;

        y -= (importBtnH + 40f);
        content.GetComponent<RectTransform>().sizeDelta = new Vector2(isDesktop ? 900f : 960f, Mathf.Abs(y) + 60f);
    }

    private void OnExecuteImport()
    {
        try
        {
            AudioManager.Instance?.PlayClick();
            string hex = (importRedeemScriptInput != null) ? importRedeemScriptInput.text.Trim() : "";
            if (string.IsNullOrEmpty(hex)) throw new Exception("Please paste the Redeem Script Hex.");

            var (m, n, pubKeys, addr) = MultisigCore.ParseRedeemScript(hex, MultisigAddressType.P2WSH, isTestNet);

            List<string> keysHex = new List<string>();
            foreach (var pk in pubKeys) keysHex.Add(pk.ToHex());

            string vName = (importNameInput != null && !string.IsNullOrEmpty(importNameInput.text))
                ? importNameInput.text.Trim()
                : $"{m}-of-{n} Imported Vault";

            activeVault = new MultisigVaultConfig
            {
                vaultId = System.Guid.NewGuid().ToString(),
                vaultName = vName,
                requiredSignatures = m,
                totalKeys = n,
                addressTypeInt = (int)MultisigAddressType.P2WSH,
                isTestNet = isTestNet,
                publicKeysHex = keysHex,
                redeemScriptHex = hex,
                multisigAddress = addr,
                myPublicKeyHex = keysHex.Count > 0 ? keysHex[0] : "",
                vaultMnemonic = ""
            };

            MultisigCore.SaveVault(activeVault);
            RefreshVaultsList();

            currentState = VaultViewState.Dashboard;
            UpdateViewVisibility();
            PopulateDashboard();
        }
        catch (Exception ex)
        {
            if (importErrorText != null)
            {
                importErrorText.text = "Import Error: " + ex.Message;
                importErrorText.color = Color.red;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 5. DASHBOARD VIEW (Active Vault Details & Balance)
    // ─────────────────────────────────────────────────────────────────────────────
    private void BuildDashboardView(Transform parent, bool isDesktop)
    {
        if (dashboardViewObj != null)
        {
            dashboardViewObj.SetActive(false);
            Destroy(dashboardViewObj);
        }
        dashboardViewObj = CreateViewContainer(parent, "DashboardView");
        var (header, content, scrollRect) = CreateScrollableScreen(dashboardViewObj.transform, isDesktop, L("MS_DETAILS_TITLE"), () => {
            currentState = VaultViewState.Landing;
            BuildLandingView(contentRoot, isDesktop);
            UpdateViewVisibility();
        });

        float y = -20f;
        float cardW = isDesktop ? 860f : 920f;

        // Ensure active vault has a Satolite Serial
        if (activeVault != null && string.IsNullOrEmpty(activeVault.vaultSerial))
        {
            activeVault.vaultSerial = SatoliteSerialService.GenerateSerialNumber();
            MultisigCore.SaveVault(activeVault);
        }

        // 1. Vault Title / Name Card (Centered cleanly without overlapping header)
        float titleCardH = 145f;
        GameObject titleCard = CreateCard(content, 0, y, cardW, titleCardH);
        string vName = activeVault != null ? activeVault.vaultName : "Vault 1";
        bool isCoord = (activeVault != null && activeVault.isCoordinator);
        string vIcon = isCoord ? "👁️" : "🛡️";
        dashboardTitleText = CreateText(titleCard.transform, $"{vIcon} {vName}", 0, 32f, 30, isCoord ? ORANGE_ACCENT : ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        dashboardTitleText.alignment = TextAnchor.MiddleCenter;
        dashboardTitleText.rectTransform.anchoredPosition = new Vector2(0, 32f);
        dashboardTitleText.rectTransform.sizeDelta = new Vector2(cardW - 40f, 38f);

        string net = isTestNet ? "TestNet" : "MainNet";
        string pType;
        if (isCoord && activeVault != null)
        {
            pType = $"Watch-Only Coordinator ({(MultisigAddressType)activeVault.addressTypeInt}) ({net})";
        }
        else if (activeVault != null)
        {
            string consensus = (activeVault.requiredSignatures == activeVault.totalKeys) ? "n-of-n" : "m-of-n";
            pType = $"{activeVault.requiredSignatures}-of-{activeVault.totalKeys} ({consensus}) {(MultisigAddressType)activeVault.addressTypeInt} ({net})";
        }
        else
        {
            pType = $"2-of-3 (m-of-n) NativeSegwit ({net})";
        }
        vaultStatusText = CreateText(titleCard.transform, pType, 0, -5f, 20, isCoord ? ORANGE_ACCENT : ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        vaultStatusText.alignment = TextAnchor.MiddleCenter;
        vaultStatusText.rectTransform.anchoredPosition = new Vector2(0, -5f);
        vaultStatusText.rectTransform.sizeDelta = new Vector2(cardW - 40f, 28f);

        // Satolite Serial display
        string serialStr = activeVault != null ? activeVault.vaultSerial : "SN000000000000";
        vaultSerialText = CreateText(titleCard.transform, $"Satolite Serial: {serialStr}", -50f, -44f, 18, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
        vaultSerialText.alignment = TextAnchor.MiddleCenter;
        vaultSerialText.rectTransform.anchoredPosition = new Vector2(-50f, -44f);
        vaultSerialText.rectTransform.sizeDelta = new Vector2(cardW - 180f, 26f);

        CreateButton(titleCard.transform, L("BTN_COPY"), (cardW / 2f) - 65f, -44f, 95f, 34f, () => {
            if (activeVault != null && !string.IsNullOrEmpty(activeVault.vaultSerial))
            {
                GUIUtility.systemCopyBuffer = activeVault.vaultSerial;
                AudioManager.Instance?.PlayCopy();
            }
        });

        y -= (titleCardH + 25f);

        // 2. Balance Card (With Receive and Send Buttons right below Refresh Balance)
        float balCardH = 270f;
        GameObject balCard = CreateCard(content, 0, y, cardW, balCardH);
        vaultBalanceText = CreateText(balCard.transform, "Balance: 0.00000000 BTC", 0, 82f, 30, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        vaultBalanceText.alignment = TextAnchor.MiddleCenter;

        CreateButton(balCard.transform, L("MS_BTN_REFRESH_BAL"), 0, 22f, 300f, 50f, FetchVaultBalance);

        // Receive & Send Buttons Row (Directly below Refresh Balance with clean 24px gap)
        float actionBtnW = (cardW - 60f - 20f) / 2f;
        float actionBtnH = 68f;
        float actionBtnY = -62f;

        // Receive Button
        GameObject recBtn = CreateButton(balCard.transform, L("MS_BTN_RECEIVE"), -(actionBtnW / 2f + 10f), actionBtnY, actionBtnW, actionBtnH, ShowReceiveModal);
        recBtn.GetComponent<Image>().color = new Color(0.18f, 0.55f, 0.36f);
        var rTxt = recBtn.GetComponentInChildren<Text>();
        rTxt.color = Color.white; rTxt.fontStyle = FontStyle.Bold; rTxt.fontSize = 26;

        // Send Button (Starts locked until Key #2 verified)
        GameObject sndBtn = CreateButton(balCard.transform, L("MS_BTN_SEND_LOCKED"), (actionBtnW / 2f + 10f), actionBtnY, actionBtnW, actionBtnH, OnSendButtonClicked);
        sendButtonRef = sndBtn.GetComponent<Button>();
        sendButtonImgRef = sndBtn.GetComponent<Image>();
        sendButtonTxtRef = sndBtn.GetComponentInChildren<Text>();
        sendButtonTxtRef.fontStyle = FontStyle.Bold; sendButtonTxtRef.fontSize = 25;
        UpdateSendButtonVisualState();

        y -= (balCardH + 25f);

        // ── Actions Row Under Balance (Copy Redeem Script & View 12-Word Phrase) ──
        float backupRowH = 70f;
        if (isCoord)
        {
            // Coordinator Policy (Watch-Only):
            // 1. ONLY show "Copy Redeem Script"
            // 2. "View 12-Word Phrase" is COMPLETELY HIDDEN
            float bW = cardW;
            GameObject cRedeemBtn = CreateButton(content, L("MS_BTN_COPY_REDEEM"), 0, y, bW, backupRowH, () => {
                if (activeVault != null && !string.IsNullOrEmpty(activeVault.redeemScriptHex))
                {
                    GUIUtility.systemCopyBuffer = activeVault.redeemScriptHex;
                    AudioManager.Instance?.PlayCopy();
                }
            });
            cRedeemBtn.GetComponent<Image>().color = ORANGE_ACCENT;
            var crTxt = cRedeemBtn.GetComponentInChildren<Text>();
            crTxt.color = Color.white; crTxt.fontStyle = FontStyle.Bold; crTxt.fontSize = 25;
            y -= (backupRowH + 25f);
        }
        else
        {
            // Quorum Policy (Signer):
            // Show BOTH "Copy Redeem Script" and "View 12-Word Phrase"
            float bW = isDesktop ? 415f : 445f;

            // Button 1: Copy Redeem Script
            GameObject cRedeemBtn = CreateButton(content, L("MS_BTN_COPY_REDEEM"), -(bW / 2f + 10f), y, bW, backupRowH, () => {
                if (activeVault != null && !string.IsNullOrEmpty(activeVault.redeemScriptHex))
                {
                    GUIUtility.systemCopyBuffer = activeVault.redeemScriptHex;
                    AudioManager.Instance?.PlayCopy();
                }
            });
            cRedeemBtn.GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
            var crTxt = cRedeemBtn.GetComponentInChildren<Text>();
            crTxt.color = Color.white; crTxt.fontStyle = FontStyle.Bold; crTxt.fontSize = 24;

            // Button 2: View 12-Word Phrase (PIN Security Gate)
            GameObject vWordsBtn = CreateButton(content, L("MS_BTN_VIEW_12WORDS"), (bW / 2f + 10f), y, bW, backupRowH, () => {
                ShowPinPromptModal(() => {
                    ShowRevealPassphraseModal("Vault 12-Word Phrase", activeVault != null ? activeVault.vaultMnemonic : "");
                }, "Enter Wallet PIN");
                AudioManager.Instance?.PlayClick();
            });
            vWordsBtn.GetComponent<Image>().color = new Color(0.20f, 0.45f, 0.65f);
            var vwTxt = vWordsBtn.GetComponentInChildren<Text>();
            vwTxt.color = Color.white; vwTxt.fontStyle = FontStyle.Bold; vwTxt.fontSize = 24;

            y -= (backupRowH + 25f);
        }

        // 3. Deposit Address Card (Compact)
        float addrCardH = 150f;
        GameObject addrCard = CreateCard(content, 0, y, cardW, addrCardH);
        Text dLbl = CreateText(addrCard.transform, L("MS_DEPOSIT_ADDR_LBL"), 0, 42f, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        dLbl.alignment = TextAnchor.MiddleCenter;

        string addrStr = activeVault != null ? activeVault.multisigAddress : "bc1q...";
        vaultAddressText = CreateText(addrCard.transform, addrStr, 0, 5f, 22, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
        vaultAddressText.alignment = TextAnchor.MiddleCenter;
        vaultAddressText.horizontalOverflow = HorizontalWrapMode.Wrap;
        vaultAddressText.rectTransform.sizeDelta = new Vector2(cardW - 60f, 40f);

        CreateButton(addrCard.transform, L("MS_BTN_COPY_ADDR"), 0, -42f, 220f, 44f, () => {
            if (activeVault != null) GUIUtility.systemCopyBuffer = activeVault.multisigAddress;
            AudioManager.Instance?.PlayCopy();
        });
        y -= (addrCardH + 25f);

        // 4. Key #1 Card (This Device)
        string pk1Hex = (activeVault != null && activeVault.publicKeysHex != null && activeVault.publicKeysHex.Count > 0) ? activeVault.publicKeysHex[0] : "";
        float key1CardH = isCoord ? 130f : 175f;
        GameObject key1Card = CreateCard(content, 0, y, cardW, key1CardH);
        string k1TitleStr = isCoord ? "👁️ Coordinator Key #1 (Watch-Only Device)" : L("MS_KEY1_DEVICE_LBL");
        Text k1Title = CreateText(key1Card.transform, k1TitleStr, 0, isCoord ? 30f : 52f, 22, isCoord ? ORANGE_ACCENT : ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        k1Title.alignment = TextAnchor.MiddleLeft;
        k1Title.rectTransform.anchoredPosition = new Vector2(-150f, isCoord ? 30f : 52f);

        // Hex PubKey Text + Dedicated Copy Button
        Text k1HexTxt = CreateText(key1Card.transform, pk1Hex, -60f, isCoord ? -20f : 15f, 18, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        k1HexTxt.alignment = TextAnchor.MiddleLeft;
        k1HexTxt.rectTransform.anchoredPosition = new Vector2(-60f, isCoord ? -20f : 15f);
        k1HexTxt.rectTransform.sizeDelta = new Vector2(cardW - 170f, 30f);

        CreateButton(key1Card.transform, L("BTN_COPY"), (cardW / 2f) - 65f, isCoord ? -20f : 15f, 95f, 40f, () => {
            if (!string.IsNullOrEmpty(pk1Hex))
            {
                GUIUtility.systemCopyBuffer = pk1Hex;
                AudioManager.Instance?.PlayCopy();
            }
        });

        if (!isCoord)
        {
            float kBtnW = isDesktop ? 380f : 410f;
            float kBtnH = 50f;
            float kBtnY = -42f;

            // Reveal Passphrase (Requires PIN)
            CreateButton(key1Card.transform, L("MS_BTN_REVEAL_PASS"), -(kBtnW / 2f + 10f), kBtnY, kBtnW, kBtnH, () => {
                ShowPinPromptModal(() => {
                    ShowRevealPassphraseModal("Key #1 Passphrase", activeVault != null ? activeVault.vaultMnemonic : "");
                }, "Enter PIN to Reveal Key #1");
                AudioManager.Instance?.PlayClick();
            });

            // Copy Passphrase (Requires PIN)
            CreateButton(key1Card.transform, L("MS_BTN_COPY_PASS"), (kBtnW / 2f + 10f), kBtnY, kBtnW, kBtnH, () => {
                ShowPinPromptModal(() => {
                    if (activeVault != null && !string.IsNullOrEmpty(activeVault.vaultMnemonic))
                    {
                        GUIUtility.systemCopyBuffer = activeVault.vaultMnemonic;
                        AudioManager.Instance?.PlayCopy();
                    }
                }, "Enter PIN to Copy Key #1");
            });
        }
        y -= (key1CardH + 20f);

        // 5. Key #2 Card (Co-signer)
        string pk2Hex = (activeVault != null && activeVault.publicKeysHex != null && activeVault.publicKeysHex.Count > 1) ? activeVault.publicKeysHex[1] : "";
        float key2CardH = isCoord ? 130f : 310f;
        GameObject key2Card = CreateCard(content, 0, y, cardW, key2CardH);

        Text k2Title = CreateText(key2Card.transform, L("MS_KEY2_COSIGNER_LBL"), 0, isCoord ? 30f : 120f, 22, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        k2Title.alignment = TextAnchor.MiddleLeft;
        k2Title.rectTransform.anchoredPosition = new Vector2(-150f, isCoord ? 30f : 120f);

        // Hex PubKey Text + Dedicated Copy Button
        Text k2HexTxt = CreateText(key2Card.transform, pk2Hex, -60f, isCoord ? -20f : 85f, 18, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        k2HexTxt.alignment = TextAnchor.MiddleLeft;
        k2HexTxt.rectTransform.anchoredPosition = new Vector2(-60f, isCoord ? -20f : 85f);
        k2HexTxt.rectTransform.sizeDelta = new Vector2(cardW - 170f, 30f);

        CreateButton(key2Card.transform, L("BTN_COPY"), (cardW / 2f) - 65f, isCoord ? -20f : 85f, 95f, 40f, () => {
            if (!string.IsNullOrEmpty(pk2Hex))
            {
                GUIUtility.systemCopyBuffer = pk2Hex;
                AudioManager.Instance?.PlayCopy();
            }
        });

        if (!isCoord)
        {
            float kBtnW = isDesktop ? 380f : 410f;
            float kBtnH = 50f;

            // Key #2 Reveal & Copy Row (Protected with PIN)
            CreateButton(key2Card.transform, L("MS_BTN_REVEAL_PASS"), -(kBtnW / 2f + 10f), 35f, kBtnW, kBtnH, () => {
                ShowPinPromptModal(() => {
                    ShowRevealPassphraseModal("Key #2 Passphrase", activeVault != null ? activeVault.key2Mnemonic : "");
                }, "Enter PIN to Reveal Key #2");
                AudioManager.Instance?.PlayClick();
            });

            CreateButton(key2Card.transform, L("MS_BTN_COPY_PASS"), (kBtnW / 2f + 10f), 35f, kBtnW, kBtnH, () => {
                ShowPinPromptModal(() => {
                    if (activeVault != null && !string.IsNullOrEmpty(activeVault.key2Mnemonic))
                    {
                        GUIUtility.systemCopyBuffer = activeVault.key2Mnemonic;
                        AudioManager.Instance?.PlayCopy();
                    }
                }, "Enter PIN to Copy Key #2");
            });

            // Key #2 Input Field (Enter Passphrase)
            Text inputLbl = CreateText(key2Card.transform, L("MS_KEY2_INPUT_LBL"), 0, -20f, 20, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
            inputLbl.alignment = TextAnchor.MiddleLeft;
            inputLbl.rectTransform.anchoredPosition = new Vector2(-100f, -20f);

            key2InputField = CreateInputField(key2Card.transform, -65f, -65f, cardW - 210f, 58f, "Enter 12 words or WIF / Hex private key...");
            CreateButton(key2Card.transform, L("MS_BTN_PASTE"), (cardW / 2f) - 95f, -65f, 110f, 58f, () => {
                if (key2InputField != null)
                {
                    key2InputField.text = GUIUtility.systemCopyBuffer;
                    OnKey2InputChanged(key2InputField.text);
                }
                AudioManager.Instance?.PlayClick();
            });

            key2InputField.onValueChanged.AddListener((val) => OnKey2InputChanged(val));

            // Realtime Verification Status
            key2ValidationStatusText = CreateText(key2Card.transform, L("MS_KEY2_STATUS_REQ"), 0, -118f, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
            key2ValidationStatusText.alignment = TextAnchor.MiddleCenter;
            key2ValidationStatusText.rectTransform.sizeDelta = new Vector2(cardW - 40f, 35f);
        }

        y -= (key2CardH + 20f);

        // 6. Back to Vaults List Button
        CreateButton(content, L("MS_BTN_BACK_LIST"), 0, y, isDesktop ? 450f : 600f, 70f, () => {
            currentState = VaultViewState.Landing;
            BuildLandingView(contentRoot, isDesktop);
            UpdateViewVisibility();
        }).GetComponent<Image>().color = new Color(0.35f, 0.4f, 0.5f);
        y -= 90f;

        // 7. Multisig PubKey Group Index Button (Placed directly under Back to Vaults List)
        float actionRowBtnW = (cardW - 60f - 20f) / 2f;
        GameObject groupIdxBtn = CreateButton(content, L("MS_BTN_GROUP_INDEX"), -(actionRowBtnW / 2f + 10f), y, actionRowBtnW, 75f, ShowPubKeyGroupIndexModal);
        groupIdxBtn.GetComponent<Image>().color = new Color(0.18f, 0.45f, 0.65f);
        var giTxt = groupIdxBtn.GetComponentInChildren<Text>();
        giTxt.color = Color.white; giTxt.fontStyle = FontStyle.Bold; giTxt.fontSize = 21;

        // 8. Manual Derivation Config Button
        GameObject manualCfgBtn = CreateButton(content, L("MS_BTN_DERIV_CONFIG"), (actionRowBtnW / 2f + 10f), y, actionRowBtnW, 75f, ShowManualConfigModal);
        manualCfgBtn.GetComponent<Image>().color = new Color(0.35f, 0.35f, 0.48f);
        var mcTxt = manualCfgBtn.GetComponentInChildren<Text>();
        mcTxt.color = Color.white; mcTxt.fontStyle = FontStyle.Bold; mcTxt.fontSize = 21;
        y -= 115f;

        content.GetComponent<RectTransform>().sizeDelta = new Vector2(isDesktop ? 900f : 960f, Mathf.Abs(y) + 80f);
    }

    private void OnKey2InputChanged(string val)
    {
        if (activeVault == null || activeVault.publicKeysHex.Count < 2) return;

        string expectedHex = activeVault.publicKeysHex[1];
        var key = MultisigCore.ValidateAndExtractKey(val, expectedHex, isTestNet);

        if (key != null)
        {
            isKey2Verified = true;
            verifiedKey2 = key;
            if (key2ValidationStatusText != null)
            {
                key2ValidationStatusText.text = L("MS_KEY2_STATUS_VERIFIED");
                key2ValidationStatusText.color = new Color(0.2f, 0.85f, 0.4f);
            }
        }
        else if (string.IsNullOrWhiteSpace(val))
        {
            isKey2Verified = false;
            verifiedKey2 = null;
            if (key2ValidationStatusText != null)
            {
                key2ValidationStatusText.text = L("MS_KEY2_STATUS_REQ");
                key2ValidationStatusText.color = ThemeManager.Instance.GetSecondaryTextColor();
            }
        }
        else
        {
            isKey2Verified = false;
            verifiedKey2 = null;
            if (key2ValidationStatusText != null)
            {
                key2ValidationStatusText.text = L("MS_KEY2_STATUS_INVALID");
                key2ValidationStatusText.color = new Color(0.9f, 0.3f, 0.3f);
            }
        }

        UpdateSendButtonVisualState();
    }

    private void UpdateSendButtonVisualState()
    {
        if (sendButtonRef == null || sendButtonImgRef == null || sendButtonTxtRef == null) return;

        if (activeVault != null && activeVault.isCoordinator)
        {
            sendButtonImgRef.color = ThemeManager.Instance.IsDarkMode ? new Color(0.22f, 0.25f, 0.32f) : new Color(0.85f, 0.88f, 0.92f);
            sendButtonTxtRef.text = L("MS_WATCH_ONLY_SEND_DISABLED");
            sendButtonTxtRef.color = ThemeManager.Instance.IsDarkMode ? new Color(0.65f, 0.70f, 0.78f) : new Color(0.45f, 0.50f, 0.58f);
            return;
        }

        if (isKey2Verified && verifiedKey2 != null)
        {
            sendButtonImgRef.color = ThemeManager.Instance.GetAccentColor();
            sendButtonTxtRef.text = L("MS_BTN_SEND");
            sendButtonTxtRef.color = Color.white;
        }
        else
        {
            sendButtonImgRef.color = ThemeManager.Instance.IsDarkMode ? new Color(0.25f, 0.30f, 0.40f) : new Color(0.80f, 0.84f, 0.90f);
            sendButtonTxtRef.text = L("MS_BTN_SEND_LOCKED");
            sendButtonTxtRef.color = ThemeManager.Instance.IsDarkMode ? new Color(0.7f, 0.75f, 0.85f) : new Color(0.4f, 0.45f, 0.55f);
        }
    }

    private void OnSendButtonClicked()
    {
        if (activeVault != null && activeVault.isCoordinator)
        {
            AudioManager.Instance?.PlayClick();
            if (vaultBalanceText != null)
            {
                vaultBalanceText.text = "👁️ Coordinator Vault: Watch-Only mode. Cannot sign or spend.";
            }
            return;
        }

        if (isKey2Verified && verifiedKey2 != null)
        {
            ShowSendModal();
        }
        else
        {
            AudioManager.Instance?.PlayClick();
            if (vaultBalanceText != null)
            {
                vaultBalanceText.text = L("MS_SEC_GATE_KEY2");
            }
        }
    }

    private void ShowReceiveModal()
    {
        if (receiveModalObj != null) Destroy(receiveModalObj);
        if (activeVault == null) return;

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        receiveModalObj = new GameObject("ReceiveModal");
        receiveModalObj.layer = 5;
        receiveModalObj.transform.SetParent(mainCanvas.transform, false);

        RectTransform mRT = receiveModalObj.AddComponent<RectTransform>();
        mRT.anchorMin = Vector2.zero; mRT.anchorMax = Vector2.one;
        mRT.offsetMin = Vector2.zero; mRT.offsetMax = Vector2.zero;

        Image bg = receiveModalObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.88f);

        float cardW = isDesktop ? 800f : 920f;
        float cardH = isDesktop ? 750f : 880f;
        GameObject card = CreateCard(receiveModalObj.transform, 0, 0, cardW, cardH);
        card.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();

        float y = (cardH / 2f) - 50f;

        Text title = CreateText(card.transform, "📥 Deposit to Multisig Vault", 0, y, 32, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        title.alignment = TextAnchor.MiddleCenter;
        y -= 50f;

        string netName = isTestNet ? "TestNet" : "MainNet";
        Text sub = CreateText(card.transform, $"Send Bitcoin to this address. Requires {activeVault.requiredSignatures} keys to spend ({netName})", 0, y, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        sub.alignment = TextAnchor.MiddleCenter;
        y -= 60f;

        // QR Code
        float qrSize = isDesktop ? 220f : 270f;
        GameObject qrObj = new GameObject("ModalQR");
        qrObj.transform.SetParent(card.transform, false);
        RectTransform qrRT = qrObj.AddComponent<RectTransform>();
        qrRT.anchoredPosition = new Vector2(0, y - (qrSize / 2f));
        qrRT.sizeDelta = new Vector2(qrSize, qrSize);
        Image qrImg = qrObj.AddComponent<Image>();
        GenerateQRCodeForImage(qrImg, activeVault.multisigAddress);
        y -= (qrSize + 40f);

        // Address Display
        Text addrTxt = CreateText(card.transform, activeVault.multisigAddress, 0, y, 22, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
        addrTxt.alignment = TextAnchor.MiddleCenter;
        addrTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
        addrTxt.rectTransform.sizeDelta = new Vector2(cardW - 80f, 60f);
        y -= 75f;

        // Copy and Close Buttons
        float btnW = isDesktop ? 260f : 320f;
        float btnH = 65f;

        CreateButton(card.transform, L("MS_BTN_COPY_ADDR"), -(btnW / 2f + 15f), y, btnW, btnH, () => {
            GUIUtility.systemCopyBuffer = activeVault.multisigAddress;
            AudioManager.Instance?.PlayCopy();
        });

        CreateButton(card.transform, L("MENU_CLOSE"), (btnW / 2f + 15f), y, btnW, btnH, () => {
            if (receiveModalObj != null) Destroy(receiveModalObj);
        }).GetComponent<Image>().color = new Color(0.35f, 0.4f, 0.5f);
    }

    private void ShowSendModal()
    {
        if (sendModalObj != null) Destroy(sendModalObj);
        if (activeVault == null) return;
        if (!isKey2Verified || verifiedKey2 == null)
        {
            if (vaultBalanceText != null)
                vaultBalanceText.text = L("MS_SEC_GATE_VERIFY");
            return;
        }

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        sendModalObj = new GameObject("SendModal");
        sendModalObj.layer = 5;
        sendModalObj.transform.SetParent(mainCanvas.transform, false);

        RectTransform mRT = sendModalObj.AddComponent<RectTransform>();
        mRT.anchorMin = Vector2.zero; mRT.anchorMax = Vector2.one;
        mRT.offsetMin = Vector2.zero; mRT.offsetMax = Vector2.zero;

        Image bg = sendModalObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.90f);

        float cardW = isDesktop ? 860f : 960f;
        float cardH = isDesktop ? 860f : 1060f;
        GameObject card = CreateCard(sendModalObj.transform, 0, 0, cardW, cardH);
        card.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();

        float y = (cardH / 2f) - 45f;

        Text title = CreateText(card.transform, L("MS_SEND_TITLE"), 0, y, 32, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        title.alignment = TextAnchor.MiddleCenter;
        y -= 42f;

        string quorumInfo = $"Signing with Key #1 (Device) + Key #2 (Verified) • {activeVault.requiredSignatures} of {activeVault.totalKeys} Signatures";
        Text sub = CreateText(card.transform, quorumInfo, 0, y, 19, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
        sub.alignment = TextAnchor.MiddleCenter;
        y -= 48f;

        // Recipient Address Input
        Text recLbl = CreateText(card.transform, L("MS_RECIPIENT_ADDR_LBL"), 0, y, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        recLbl.alignment = TextAnchor.MiddleLeft;
        recLbl.rectTransform.anchoredPosition = new Vector2(-120f, y);
        y -= 40f;

        InputField toAddrInput = CreateInputField(card.transform, -65f, y, cardW - 200f, 60f, "Enter bc1... or tb1... address");
        CreateButton(card.transform, L("MS_BTN_PASTE"), (cardW / 2f) - 90f, y, 110f, 60f, () => {
            toAddrInput.text = GUIUtility.systemCopyBuffer;
            AudioManager.Instance?.PlayClick();
        });
        y -= 68f;

        // Amount Input
        Text amtLbl = CreateText(card.transform, isTestNet ? "Amount (tBTC):" : "Amount (BTC):", 0, y, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        amtLbl.alignment = TextAnchor.MiddleLeft;
        amtLbl.rectTransform.anchoredPosition = new Vector2(-120f, y);
        y -= 40f;

        InputField amtInput = CreateInputField(card.transform, -65f, y, cardW - 200f, 60f, "0.00000000");
        amtInput.contentType = InputField.ContentType.DecimalNumber;
        CreateButton(card.transform, "MAX", (cardW / 2f) - 90f, y, 110f, 60f, () => {
            amtInput.text = "0.00050000";
            AudioManager.Instance?.PlayClick();
        });
        y -= 68f;

        // --- FEE CALCULATION SECTION (Auto / Manual) ---
        Text feeTitle = CreateText(card.transform, L("MS_FEE_SEL_LBL"), 0, y, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Bold);
        feeTitle.alignment = TextAnchor.MiddleLeft;
        feeTitle.rectTransform.anchoredPosition = new Vector2(-120f, y);
        y -= 40f;

        float tabW = (cardW - 100f) / 2f;
        int currentSatPerVb = 1;
        const int estimatedTxVBytes = 280;

        InputField feeInput = null;
        Text feeRateInfo = null;
        Image autoTabImg = null;
        Image manualTabImg = null;
        Text autoTabTxt = null;
        Text manualTabTxt = null;
        GameObject autoOptionsRow = null;
        GameObject manualInputRow = null;
        MempoolRecommendedFees cachedFees = null;

        void RecalculateBtcFee(int satPerVb)
        {
            currentSatPerVb = Math.Max(1, satPerVb);
            decimal btcFee = (decimal)(currentSatPerVb * estimatedTxVBytes) / 100000000m;
            if (feeInput != null) feeInput.text = btcFee.ToString("F8");
            if (feeRateInfo != null) feeRateInfo.text = $"Rate: {currentSatPerVb} sat/vB • Est. size: ~{estimatedTxVBytes} vB ({btcFee:F8} BTC)";
        }

        // Auto vs Manual Mode Buttons Row
        GameObject autoTabBtn = CreateButton(card.transform, L("MS_FEE_AUTO"), -(tabW / 2f + 10f), y, tabW, 50f, () => {
            if (autoTabImg != null) autoTabImg.color = ThemeManager.Instance.GetAccentColor();
            if (autoTabTxt != null) autoTabTxt.color = Color.white;
            if (manualTabImg != null) manualTabImg.color = ThemeManager.Instance.IsDarkMode ? new Color(0.2f, 0.25f, 0.35f) : new Color(0.85f, 0.88f, 0.94f);
            if (manualTabTxt != null) manualTabTxt.color = ThemeManager.Instance.GetTextColor();
            if (autoOptionsRow != null) autoOptionsRow.SetActive(true);
            if (manualInputRow != null) manualInputRow.SetActive(false);
            int rate = cachedFees != null ? Math.Max(1, cachedFees.minimumFee) : (isTestNet ? 1 : 3);
            RecalculateBtcFee(rate);
            AudioManager.Instance?.PlayClick();
        });
        autoTabImg = autoTabBtn.GetComponent<Image>();
        autoTabTxt = autoTabBtn.GetComponentInChildren<Text>();
        autoTabTxt.fontSize = 20; autoTabTxt.fontStyle = FontStyle.Bold;

        GameObject manualTabBtn = CreateButton(card.transform, L("MS_FEE_MANUAL"), (tabW / 2f + 10f), y, tabW, 50f, () => {
            if (manualTabImg != null) manualTabImg.color = ThemeManager.Instance.GetAccentColor();
            if (manualTabTxt != null) manualTabTxt.color = Color.white;
            if (autoTabImg != null) autoTabImg.color = ThemeManager.Instance.IsDarkMode ? new Color(0.2f, 0.25f, 0.35f) : new Color(0.85f, 0.88f, 0.94f);
            if (autoTabTxt != null) autoTabTxt.color = ThemeManager.Instance.GetTextColor();
            if (autoOptionsRow != null) autoOptionsRow.SetActive(false);
            if (manualInputRow != null) manualInputRow.SetActive(true);
            AudioManager.Instance?.PlayClick();
        });
        manualTabImg = manualTabBtn.GetComponent<Image>();
        manualTabTxt = manualTabBtn.GetComponentInChildren<Text>();
        manualTabTxt.fontSize = 20; manualTabTxt.fontStyle = FontStyle.Bold;
        y -= 58f;

        // Sub-options row for Auto Mode: [🟢 Lowest Fee] vs [🟡 Average Fee]
        autoOptionsRow = new GameObject("AutoOptionsRow");
        autoOptionsRow.transform.SetParent(card.transform, false);
        RectTransform aRT = autoOptionsRow.AddComponent<RectTransform>();
        aRT.anchoredPosition = new Vector2(0, y);
        aRT.sizeDelta = new Vector2(cardW - 80f, 50f);

        float subW = (cardW - 100f) / 2f;
        CreateButton(autoOptionsRow.transform, L("MS_FEE_LOWEST"), -(subW / 2f + 10f), 0, subW, 48f, () => {
            int rate = cachedFees != null ? Math.Max(1, cachedFees.minimumFee) : (isTestNet ? 1 : 3);
            RecalculateBtcFee(rate);
            AudioManager.Instance?.PlayClick();
        }).GetComponent<Image>().color = new Color(0.18f, 0.55f, 0.36f);

        CreateButton(autoOptionsRow.transform, L("MS_FEE_AVG"), (subW / 2f + 10f), 0, subW, 48f, () => {
            int rate = cachedFees != null ? Math.Max(1, cachedFees.halfHourFee) : (isTestNet ? 1 : 12);
            RecalculateBtcFee(rate);
            AudioManager.Instance?.PlayClick();
        }).GetComponent<Image>().color = new Color(0.75f, 0.55f, 0.18f);

        // Sub-options row for Manual Mode: Input for custom sat/vByte
        manualInputRow = new GameObject("ManualInputRow");
        manualInputRow.transform.SetParent(card.transform, false);
        RectTransform mInpRT = manualInputRow.AddComponent<RectTransform>();
        mInpRT.anchoredPosition = new Vector2(0, y);
        mInpRT.sizeDelta = new Vector2(cardW - 80f, 50f);
        manualInputRow.SetActive(false);

        Text satLbl = CreateText(manualInputRow.transform, "Custom sat/vB:", -160f, 0, 20, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        satLbl.alignment = TextAnchor.MiddleLeft;
        satLbl.rectTransform.anchoredPosition = new Vector2(-160f, 0);

        InputField manualSatInput = CreateInputField(manualInputRow.transform, 100f, 0, 220f, 48f, "e.g. 5");
        manualSatInput.contentType = InputField.ContentType.IntegerNumber;
        manualSatInput.text = "5";
        manualSatInput.onValueChanged.AddListener((val) => {
            if (int.TryParse(val, out int s)) RecalculateBtcFee(s);
        });

        y -= 55f;

        // Rate summary display
        feeRateInfo = CreateText(card.transform, "Fetching live Mempool fee rates...", 0, y, 18, ThemeManager.Instance.GetAccentColor(), FontStyle.Normal);
        feeRateInfo.alignment = TextAnchor.MiddleCenter;
        feeRateInfo.rectTransform.anchoredPosition = new Vector2(0, y);
        feeRateInfo.rectTransform.sizeDelta = new Vector2(cardW - 80f, 30f);
        y -= 40f;

        // Direct BTC Miner Fee Field
        feeInput = CreateInputField(card.transform, 0, y, cardW - 80f, 58f, "0.00001000");
        feeInput.text = "0.00001000";
        feeInput.contentType = InputField.ContentType.DecimalNumber;
        y -= 60f;

        // Fetch live fees asynchronously
        FetchLiveFeeRecommendations((fees) => {
            cachedFees = fees;
            int defRate = Math.Max(1, fees.minimumFee);
            RecalculateBtcFee(defRate);
        });

        // Status Text
        Text sendStatusText = CreateText(card.transform, "", 0, y, 20, Color.yellow, FontStyle.Normal);
        sendStatusText.alignment = TextAnchor.MiddleCenter;
        sendStatusText.rectTransform.sizeDelta = new Vector2(cardW - 80f, 45f);
        y -= 60f;

        // Action Buttons: Broadcast, Cancel
        float btnW = isDesktop ? 300f : 340f;
        float btnH = 72f;

        GameObject broadcastBtn = CreateButton(card.transform, L("MS_BTN_BROADCAST"), -(btnW / 2f + 15f), y, btnW, btnH, async () => {
            try
            {
                string toAddr = toAddrInput.text.Trim();
                if (string.IsNullOrEmpty(toAddr)) throw new Exception("Please enter recipient address.");
                if (!decimal.TryParse(amtInput.text, out decimal amt) || amt <= 0) throw new Exception("Please enter a valid amount.");
                if (!decimal.TryParse(feeInput.text, out decimal fee) || fee <= 0) fee = 0.00001000m;

                sendStatusText.text = L("MS_BUILDING_TX");
                sendStatusText.color = Color.yellow;

                Key key1 = MultisigCore.GetVaultPrivateKey(activeVault);
                if (key1 == null) throw new Exception("Failed to load Key #1 for this device.");

                var signingKeys = new List<Key> { key1, verifiedKey2 };
                string txId = await MultisigCore.SendMultisigTransaction(activeVault, signingKeys, toAddr, amt, fee);

                sendStatusText.text = $"✅ Transaction Broadcasted! TXID:\n{txId}";
                sendStatusText.color = Color.green;
                GUIUtility.systemCopyBuffer = txId;
                AudioManager.Instance?.PlayUnlock();
                FetchVaultBalance();
            }
            catch (Exception ex)
            {
                sendStatusText.text = "Error: " + ex.Message;
                sendStatusText.color = Color.red;
            }
        });
        broadcastBtn.GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
        var bTxt = broadcastBtn.GetComponentInChildren<Text>();
        bTxt.color = Color.white; bTxt.fontSize = 24; bTxt.fontStyle = FontStyle.Bold;

        CreateButton(card.transform, L("DELETE_NO"), (btnW / 2f + 15f), y, btnW, btnH, () => {
            if (sendModalObj != null) Destroy(sendModalObj);
        }).GetComponent<Image>().color = new Color(0.35f, 0.4f, 0.5f);
    }

    private async void FetchLiveFeeRecommendations(Action<MempoolRecommendedFees> onReceived)
    {
        try
        {
            var api = new BlockCypherApiCore();
            var fees = await api.GetRecommendedFeesAsync(isTestNet);
            onReceived?.Invoke(fees);
        }
        catch {}
    }

    private void ShowPinPromptModal(Action onPinVerified, string title = "Enter Wallet PIN")
    {
        if (pinModalObj != null) Destroy(pinModalObj);

        // If wallet PIN is not enabled in AuthManager, directly execute action
        if (AuthManager.Instance == null || !AuthManager.Instance.IsEncrypted)
        {
            onPinVerified?.Invoke();
            return;
        }

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        pinModalObj = new GameObject("PinModal");
        pinModalObj.layer = 5;
        pinModalObj.transform.SetParent(mainCanvas.transform, false);

        RectTransform mRT = pinModalObj.AddComponent<RectTransform>();
        mRT.anchorMin = Vector2.zero; mRT.anchorMax = Vector2.one;
        mRT.offsetMin = Vector2.zero; mRT.offsetMax = Vector2.zero;

        Image bg = pinModalObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.88f);

        float cardW = isDesktop ? 600f : 780f;
        float cardH = isDesktop ? 420f : 480f;
        GameObject card = CreateCard(pinModalObj.transform, 0, 0, cardW, cardH);
        card.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();

        float y = (cardH / 2f) - 50f;

        Text t = CreateText(card.transform, $"🔒 {title}", 0, y, 28, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        t.alignment = TextAnchor.MiddleCenter;
        y -= 45f;

        Text sub = CreateText(card.transform, L("MS_PIN_TITLE"), 0, y, 19, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        sub.alignment = TextAnchor.MiddleCenter;
        y -= 65f;

        InputField pinInput = CreateInputField(card.transform, 0, y, cardW - 120f, 65f, "Enter PIN...");
        pinInput.contentType = InputField.ContentType.Pin;
        y -= 55f;

        Text errTxt = CreateText(card.transform, "", 0, y, 20, Color.red, FontStyle.Normal);
        errTxt.alignment = TextAnchor.MiddleCenter;
        y -= 55f;

        float btnW = isDesktop ? 220f : 260f;
        float btnH = 65f;

        CreateButton(card.transform, L("BTN_USE"), -(btnW / 2f + 12f), y, btnW, btnH, () => {
            string entered = pinInput.text.Trim();
            if (AuthManager.Instance.CheckPIN(entered))
            {
                if (pinModalObj != null) Destroy(pinModalObj);
                AudioManager.Instance?.PlayUnlock();
                onPinVerified?.Invoke();
            }
            else
            {
                errTxt.text = L("MS_PIN_INCORRECT");
                AudioManager.Instance?.PlayClick();
            }
        }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();

        CreateButton(card.transform, L("DELETE_NO"), (btnW / 2f + 12f), y, btnW, btnH, () => {
            if (pinModalObj != null) Destroy(pinModalObj);
        }).GetComponent<Image>().color = new Color(0.35f, 0.4f, 0.5f);
    }

    private void ShowRevealPassphraseModal(string title, string mnemonic)
    {
        ScreenSecurityManager.EnableProtection();
        if (revealModalObj != null) Destroy(revealModalObj);

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        revealModalObj = new GameObject("RevealModal");
        revealModalObj.layer = 5;
        revealModalObj.transform.SetParent(mainCanvas.transform, false);

        RectTransform mRT = revealModalObj.AddComponent<RectTransform>();
        mRT.anchorMin = Vector2.zero; mRT.anchorMax = Vector2.one;
        mRT.offsetMin = Vector2.zero; mRT.offsetMax = Vector2.zero;

        Image bg = revealModalObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.90f);

        float cardW = isDesktop ? 800f : 900f;
        float cardH = isDesktop ? 550f : 640f;
        GameObject card = CreateCard(revealModalObj.transform, 0, 0, cardW, cardH);
        card.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();

        float y = (cardH / 2f) - 50f;

        Text t = CreateText(card.transform, $"🔑 {title}", 0, y, 30, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        t.alignment = TextAnchor.MiddleCenter;
        y -= 45f;

        Text sub = CreateText(card.transform, L("MS_SECRET_WARNING"), 0, y, 19, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        sub.alignment = TextAnchor.MiddleCenter;
        y -= 70f;

        // Words Box
        float boxH = isDesktop ? 180f : 230f;
        GameObject box = CreateCard(card.transform, 0, y - (boxH / 2f), cardW - 80f, boxH);
        box.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.11f, 0.15f, 0.22f) : new Color(0.92f, 0.95f, 0.99f);

        Text wordsTxt = CreateText(box.transform, string.IsNullOrEmpty(mnemonic) ? "No mnemonic passphrase saved." : mnemonic, 0, 0, 24, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
        wordsTxt.alignment = TextAnchor.MiddleCenter;
        wordsTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
        wordsTxt.rectTransform.sizeDelta = new Vector2(cardW - 120f, boxH - 30f);

        y -= (boxH + 40f);

        float btnW = isDesktop ? 260f : 300f;
        float btnH = 65f;

        CreateButton(card.transform, L("MS_BTN_COPY_PASS"), -(btnW / 2f + 12f), y, btnW, btnH, () => {
            if (!string.IsNullOrEmpty(mnemonic))
            {
                GUIUtility.systemCopyBuffer = mnemonic;
                AudioManager.Instance?.PlayCopy();
                sub.text = L("MS_PASS_COPIED");
                sub.color = Color.green;
            }
        }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();

        CreateButton(card.transform, L("MENU_CLOSE"), (btnW / 2f + 12f), y, btnW, btnH, () => {
            ScreenSecurityManager.DisableProtection();
            if (revealModalObj != null) Destroy(revealModalObj);
        }).GetComponent<Image>().color = new Color(0.35f, 0.4f, 0.5f);
    }

    private void ShowDeleteConfirmationModal(MultisigVaultConfig vault, bool isDesktop)
    {
        if (deleteModalObj != null) Destroy(deleteModalObj);
        if (vault == null) return;

        deleteModalObj = new GameObject("DeleteConfirmModal");
        deleteModalObj.layer = 5;
        deleteModalObj.transform.SetParent(mainCanvas.transform, false);

        RectTransform mRT = deleteModalObj.AddComponent<RectTransform>();
        mRT.anchorMin = Vector2.zero; mRT.anchorMax = Vector2.one;
        mRT.offsetMin = Vector2.zero; mRT.offsetMax = Vector2.zero;

        Image bg = deleteModalObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.90f);

        float cardW = isDesktop ? 800f : 900f;
        float cardH = isDesktop ? 500f : 580f;
        GameObject card = CreateCard(deleteModalObj.transform, 0, 0, cardW, cardH);
        card.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();

        float y = (cardH / 2f) - 50f;

        Text t = CreateText(card.transform, L("MS_DELETE_CONFIRM"), 0, y, 30, new Color(0.95f, 0.3f, 0.3f), FontStyle.Bold);
        t.alignment = TextAnchor.MiddleCenter;
        y -= 60f;

        string warnMsg = "Did you save this vaults redeem script or passphrase? Deleting will permanently erase all data. Make sure you save your backup information.";
        Text msgTxt = CreateText(card.transform, warnMsg, 0, y - 60f, 22, ThemeManager.Instance.GetTextColor(), FontStyle.Normal);
        msgTxt.alignment = TextAnchor.MiddleCenter;
        msgTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
        msgTxt.rectTransform.sizeDelta = new Vector2(cardW - 80f, 150f);
        y -= 180f;

        float btnW = isDesktop ? 280f : 320f;
        float btnH = 70f;

        CreateButton(card.transform, L("DELETE_NO"), -(btnW / 2f + 15f), y, btnW, btnH, () => {
            if (deleteModalObj != null) Destroy(deleteModalObj);
        }).GetComponent<Image>().color = new Color(0.35f, 0.4f, 0.5f);

        CreateButton(card.transform, L("MS_BTN_DELETE_PERM"), (btnW / 2f + 15f), y, btnW, btnH, () => {
            MultisigCore.DeleteVault(vault.vaultId, isTestNet);
            RefreshVaultsList();
            if (deleteModalObj != null) Destroy(deleteModalObj);
            BuildLandingView(contentRoot, isDesktop);
            UpdateViewVisibility();
            AudioManager.Instance?.PlayClick();
        }).GetComponent<Image>().color = new Color(0.85f, 0.25f, 0.25f);
    }

    private void ShowManualConfigModal()
    {
        if (manualConfigModalObj != null) Destroy(manualConfigModalObj);
        if (activeVault == null) return;

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        manualConfigModalObj = new GameObject("ManualConfigModal");
        manualConfigModalObj.layer = 5;
        manualConfigModalObj.transform.SetParent(mainCanvas.transform, false);

        RectTransform mRT = manualConfigModalObj.AddComponent<RectTransform>();
        mRT.anchorMin = Vector2.zero; mRT.anchorMax = Vector2.one;
        mRT.offsetMin = Vector2.zero; mRT.offsetMax = Vector2.zero;

        Image bg = manualConfigModalObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.88f);

        float cardW = isDesktop ? 800f : 900f;
        float cardH = isDesktop ? 500f : 580f;
        GameObject card = CreateCard(manualConfigModalObj.transform, 0, 0, cardW, cardH);
        card.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();

        float y = (cardH / 2f) - 50f;

        Text t = CreateText(card.transform, L("MS_DERIV_TITLE"), 0, y, 30, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        t.alignment = TextAnchor.MiddleCenter;
        y -= 45f;

        Text sub = CreateText(card.transform, L("MS_DERIV_DESC"), 0, y, 19, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        sub.alignment = TextAnchor.MiddleCenter;
        y -= 65f;

        Text pathLbl = CreateText(card.transform, L("MS_DERIV_PATH_LBL"), 0, y, 20, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        pathLbl.alignment = TextAnchor.MiddleLeft;
        pathLbl.rectTransform.anchoredPosition = new Vector2(-120f, y);
        y -= 45f;

        InputField pathInput = CreateInputField(card.transform, 0, y, cardW - 80f, 60f, "m/48'/0'/0'/2'");
        pathInput.text = !string.IsNullOrEmpty(activeVault.derivationPath) ? activeVault.derivationPath : "m/48'/0'/0'/2'";
        y -= 65f;

        Text statTxt = CreateText(card.transform, "", 0, y, 20, ORANGE_ACCENT, FontStyle.Normal);
        statTxt.alignment = TextAnchor.MiddleCenter;
        y -= 60f;

        float btnW = isDesktop ? 260f : 300f;
        float btnH = 65f;

        CreateButton(card.transform, L("MS_BTN_SAVE_CONFIG"), -(btnW / 2f + 12f), y, btnW, btnH, () => {
            string p = pathInput.text.Trim();
            if (!string.IsNullOrEmpty(p))
            {
                activeVault.derivationPath = p;
                MultisigCore.SaveVault(activeVault);
                statTxt.text = L("MS_CONFIG_SAVED");
                statTxt.color = ORANGE_ACCENT;
                AudioManager.Instance?.PlayClick();
            }
        }).GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();

        CreateButton(card.transform, L("MENU_CLOSE"), (btnW / 2f + 12f), y, btnW, btnH, () => {
            if (manualConfigModalObj != null) Destroy(manualConfigModalObj);
        }).GetComponent<Image>().color = new Color(0.35f, 0.4f, 0.5f);
    }

    private void ShowPubKeyGroupIndexModal()
    {
        if (groupIndexModalObj != null) Destroy(groupIndexModalObj);
        if (activeVault == null) return;

        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        string groupText = MultisigCore.GenerateGroupIndexText(activeVault);

        groupIndexModalObj = new GameObject("GroupIndexModal");
        groupIndexModalObj.layer = 5;
        groupIndexModalObj.transform.SetParent(mainCanvas.transform, false);

        RectTransform mRT = groupIndexModalObj.AddComponent<RectTransform>();
        mRT.anchorMin = Vector2.zero; mRT.anchorMax = Vector2.one;
        mRT.offsetMin = Vector2.zero; mRT.offsetMax = Vector2.zero;

        Image bg = groupIndexModalObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.88f);

        float cardW = isDesktop ? 880f : 980f;
        float cardH = isDesktop ? 750f : 960f;
        GameObject card = CreateCard(groupIndexModalObj.transform, 0, 0, cardW, cardH);
        card.GetComponent<Image>().color = ThemeManager.Instance.GetCardColor();

        float y = (cardH / 2f) - 50f;

        Text title = CreateText(card.transform, L("MS_GROUP_IDX_TITLE"), 0, y, 30, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        title.alignment = TextAnchor.MiddleCenter;
        y -= 45f;

        Text sub = CreateText(card.transform, L("MS_GROUP_IDX_DESC"), 0, y, 20, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        sub.alignment = TextAnchor.MiddleCenter;
        y -= 60f;

        // Content Card Box
        float boxH = isDesktop ? 380f : 500f;
        GameObject textBox = CreateCard(card.transform, 0, y - (boxH / 2f), cardW - 60f, boxH);
        textBox.GetComponent<Image>().color = ThemeManager.Instance.IsDarkMode ? new Color(0.10f, 0.12f, 0.17f) : new Color(0.94f, 0.95f, 0.98f);

        Text contentTxt = CreateText(textBox.transform, groupText, 0, 0, 21, ThemeManager.Instance.GetTextColor(), FontStyle.Normal);
        contentTxt.alignment = TextAnchor.UpperLeft;
        contentTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
        contentTxt.rectTransform.anchorMin = Vector2.zero;
        contentTxt.rectTransform.anchorMax = Vector2.one;
        contentTxt.rectTransform.offsetMin = new Vector2(25f, 25f);
        contentTxt.rectTransform.offsetMax = new Vector2(-25f, -25f);

        y -= (boxH + 35f);

        // Buttons: Copy, Save to file, Close
        float btnW = isDesktop ? 250f : 280f;
        float btnH = 65f;

        GameObject cIdxBtn = CreateButton(card.transform, L("MS_BTN_COPY_INDEX"), -(btnW + 15f), y, btnW, btnH, () => {
            GUIUtility.systemCopyBuffer = groupText;
            AudioManager.Instance?.PlayCopy();
        });
        var ciTxt = cIdxBtn.GetComponentInChildren<Text>();
        if (ciTxt != null) { ciTxt.fontSize = 20; ciTxt.fontStyle = FontStyle.Bold; }

        GameObject sFileBtn = CreateButton(card.transform, L("MS_BTN_SAVE_FILE"), 0, y, btnW, btnH, () => {
            try
            {
                string safeName = activeVault.vaultName.Replace(" ", "_");
                string fileName = $"multisig_group_index_{safeName}.txt";
                string savePath = System.IO.Path.Combine(Application.persistentDataPath, fileName);
                System.IO.File.WriteAllText(savePath, groupText);
                sub.text = $"Saved to: {fileName}";
                sub.color = ORANGE_ACCENT;
                AudioManager.Instance?.PlayClick();
            }
            catch (Exception ex)
            {
                sub.text = "Save error: " + ex.Message;
                sub.color = Color.red;
            }
        });
        var sfTxt = sFileBtn.GetComponentInChildren<Text>();
        if (sfTxt != null) { sfTxt.fontSize = 20; sfTxt.fontStyle = FontStyle.Bold; }

        GameObject clsBtn = CreateButton(card.transform, L("MENU_CLOSE"), (btnW + 15f), y, btnW, btnH, () => {
            if (groupIndexModalObj != null) Destroy(groupIndexModalObj);
        });
        clsBtn.GetComponent<Image>().color = new Color(0.35f, 0.4f, 0.5f);
        var clTxt = clsBtn.GetComponentInChildren<Text>();
        if (clTxt != null) { clTxt.fontSize = 20; clTxt.fontStyle = FontStyle.Bold; }
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
        catch (Exception ex)
        {
            Debug.LogWarning("[MultisigVaultUI] QR error: " + ex.Message);
        }
    }

    private void PopulateDashboard()
    {
        if (activeVault == null) return;

        if (dashboardTitleText != null) dashboardTitleText.text = activeVault.vaultName;
        if (vaultAddressText != null) vaultAddressText.text = activeVault.multisigAddress;
        if (vaultStatusText != null)
        {
            string net = isTestNet ? "TestNet" : "MainNet";
            vaultStatusText.text = $"{activeVault.requiredSignatures}-of-{activeVault.totalKeys} {(MultisigAddressType)activeVault.addressTypeInt} ({net})";
        }

        GenerateQRCode(activeVault.multisigAddress);
        FetchVaultBalance();
    }

    private async void FetchVaultBalance()
    {
        if (activeVault == null || string.IsNullOrEmpty(activeVault.multisigAddress)) return;

        if (vaultBalanceText != null) vaultBalanceText.text = L("MS_FETCHING_BAL");

        try
        {
            var api = new BlockCypherApiCore();
            decimal balance = await api.GetBalance(activeVault.multisigAddress, isTestNet);
            string cur = isTestNet ? "tBTC" : "BTC";
            if (vaultBalanceText != null)
                vaultBalanceText.text = $"Balance: {balance:F8} {cur}";
        }
        catch (Exception ex)
        {
            if (vaultBalanceText != null)
                vaultBalanceText.text = $"Balance Error: {ex.Message}";
        }
    }

    private void GenerateQRCode(string text)
    {
        if (vaultQrImage == null || string.IsNullOrEmpty(text)) return;
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
            vaultQrImage.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MultisigVaultUI] QR error: " + ex.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // UI Helpers & Responsive Containers
    // ─────────────────────────────────────────────────────────────────────────────

    private void UpdateViewVisibility()
    {
        if (landingViewObj != null) landingViewObj.SetActive(currentState == VaultViewState.Landing);
        if (configViewObj != null) configViewObj.SetActive(currentState == VaultViewState.Config);
        if (keyInputViewObj != null) keyInputViewObj.SetActive(currentState == VaultViewState.KeyInput);
        if (importViewObj != null) importViewObj.SetActive(currentState == VaultViewState.Import);
        if (dashboardViewObj != null)
        {
            dashboardViewObj.SetActive(currentState == VaultViewState.Dashboard);
            if (currentState == VaultViewState.Dashboard) PopulateDashboard();
        }
    }

    private GameObject CreateViewContainer(Transform parent, string name)
    {
        GameObject view = new GameObject(name);
        view.layer = 5;
        view.transform.SetParent(parent, false);
        RectTransform rt = view.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        // Solid background to prevent ANY underlying view or canvas from bleeding through
        Image bg = view.AddComponent<Image>();
        bg.color = ThemeManager.Instance.GetBackgroundColor();
        bg.raycastTarget = true;

        return view;
    }

    private (GameObject header, Transform content, ScrollRect scrollRect) CreateScrollableScreen(
        Transform parent, bool isDesktop, string title, Action onBack)
    {
        float safeTopPadding = isDesktop ? 20f : 85f; // Safe area below notch/status bar
        float headerH = isDesktop ? 90f : 110f;
        float totalTopOffset = safeTopPadding + headerH;

        // 1. Fixed Header at the top (pivot (0.5, 0.5) so y=0 is dead center of header)
        GameObject header = new GameObject("Header");
        header.layer = 5;
        header.transform.SetParent(parent, false);
        RectTransform hRT = header.AddComponent<RectTransform>();
        hRT.anchorMin = new Vector2(0.5f, 1f); hRT.anchorMax = new Vector2(0.5f, 1f);
        hRT.pivot = new Vector2(0.5f, 0.5f);
        hRT.anchoredPosition = new Vector2(0, -(safeTopPadding + headerH / 2f));
        hRT.sizeDelta = new Vector2(isDesktop ? 1000f : REFERENCE_WIDTH, headerH);
        Image hImg = header.AddComponent<Image>();
        hImg.color = ThemeManager.Instance.GetBackgroundColor();

        // Back Button
        GameObject backBtn = CreateButton(header.transform, "←", isDesktop ? -440f : -430f, 0, 90f, 80f, () => {
            AudioManager.Instance?.PlayBack();
            onBack?.Invoke();
        });
        backBtn.GetComponent<Image>().color = Color.clear;
        Text backTxt = backBtn.GetComponentInChildren<Text>();
        backTxt.fontSize = 44;
        backTxt.color = ThemeManager.Instance.GetTextColor();

        // Title
        Text titleTxt = CreateText(header.transform, title, 0, 0, 36, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);
        titleTxt.alignment = TextAnchor.MiddleCenter;
        RectTransform tRT = titleTxt.GetComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero;
        tRT.anchorMax = Vector2.one;
        tRT.offsetMin = new Vector2(100f, 0f);
        tRT.offsetMax = new Vector2(-100f, 0f);

        // 2. ScrollRect occupying the rest of the height
        GameObject scrollObj = new GameObject("ScrollArea");
        scrollObj.layer = 5;
        scrollObj.transform.SetParent(parent, false);
        RectTransform sRT = scrollObj.AddComponent<RectTransform>();
        sRT.anchorMin = new Vector2(0.5f, 0f); sRT.anchorMax = new Vector2(0.5f, 1f);
        sRT.pivot = new Vector2(0.5f, 0.5f);
        sRT.anchoredPosition = new Vector2(0, -(totalTopOffset / 2f));
        sRT.sizeDelta = new Vector2(isDesktop ? 960f : REFERENCE_WIDTH, -totalTopOffset);

        // Add 2D rect mask so scrolled content never overlaps the header
        scrollObj.AddComponent<RectMask2D>();

        ScrollRect scrollRect = scrollObj.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 30f;

        // 3. Scrollable Content Container (anchored to top)
        GameObject contentObj = new GameObject("Content");
        contentObj.layer = 5;
        contentObj.transform.SetParent(scrollObj.transform, false);
        RectTransform cRT = contentObj.AddComponent<RectTransform>();
        cRT.anchorMin = new Vector2(0.5f, 1f); cRT.anchorMax = new Vector2(0.5f, 1f);
        cRT.pivot = new Vector2(0.5f, 1f);
        cRT.anchoredPosition = Vector2.zero;
        cRT.sizeDelta = new Vector2(isDesktop ? 920f : 980f, 1500f);
        scrollRect.content = cRT;

        return (header, contentObj.transform, scrollRect);
    }

    private GameObject CreatePresetButton(Transform parent, string label, float x, float y, float w, float h, Action action)
    {
        GameObject btnObj = new GameObject("Preset_" + label);
        btnObj.layer = 5;
        btnObj.transform.SetParent(parent, false);
        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        Image img = btnObj.AddComponent<Image>();
        img.color = ThemeManager.Instance.IsDarkMode ? new Color(0.20f, 0.25f, 0.35f) : new Color(0.88f, 0.91f, 0.96f);
        img.raycastTarget = true;

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => action?.Invoke());

        GameObject txtObj = new GameObject("Text");
        txtObj.layer = 5;
        txtObj.transform.SetParent(btnObj.transform, false);
        RectTransform tRT = txtObj.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        Text t = txtObj.AddComponent<Text>();
        t.text = label;
        t.fontSize = (int)(h * 0.40f);
        t.color = ThemeManager.Instance.GetTextColor();
        t.alignment = TextAnchor.MiddleCenter;
        t.fontStyle = FontStyle.Bold;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.raycastTarget = false;

        return btnObj;
    }

    private GameObject CreatePillButton(Transform parent, string label, float x, float y, float w, float h, Action action)
    {
        GameObject btnObj = new GameObject("Pill_" + label);
        btnObj.layer = 5;
        btnObj.transform.SetParent(parent, false);
        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        Image img = btnObj.AddComponent<Image>();
        img.color = ThemeManager.Instance.IsDarkMode ? new Color(0.22f, 0.28f, 0.40f) : new Color(0.85f, 0.90f, 0.96f);
        img.raycastTarget = true;

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => action?.Invoke());

        GameObject txtObj = new GameObject("Text");
        txtObj.layer = 5;
        txtObj.transform.SetParent(btnObj.transform, false);
        RectTransform tRT = txtObj.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        Text t = txtObj.AddComponent<Text>();
        t.text = label;
        t.fontSize = (int)(h * 0.48f);
        t.color = ThemeManager.Instance.GetAccentColor();
        t.alignment = TextAnchor.MiddleCenter;
        t.fontStyle = FontStyle.Bold;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.raycastTarget = false; // MUST BE FALSE so clicks hit the button!

        return btnObj;
    }

    private Text CreateText(Transform parent, string text, float x, float y, int size, Color color, FontStyle style)
    {
        GameObject go = new GameObject("Text");
        go.layer = 5;
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();

        bool isContentChild = (parent != null && parent.name == "Content");
        if (isContentChild)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(880f, size * 1.5f);
        }
        else
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(880f, size * 1.5f);
        }

        Text t = go.AddComponent<Text>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.raycastTarget = false; // MUST BE FALSE so text does NOT block buttons!
        return t;
    }

    private GameObject CreateButton(Transform parent, string label, float x, float y, float w, float h, Action action)
    {
        GameObject btnObj = new GameObject("Btn_" + label);
        btnObj.layer = 5;
        btnObj.transform.SetParent(parent, false);
        RectTransform rt = btnObj.AddComponent<RectTransform>();

        bool isContentChild = (parent != null && parent.name == "Content");
        if (isContentChild)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
        }
        else
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        Image img = btnObj.AddComponent<Image>();
        img.color = ThemeManager.Instance.GetCardColor();
        img.raycastTarget = true;
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => action?.Invoke());

        GameObject txtObj = new GameObject("Text");
        txtObj.layer = 5;
        txtObj.transform.SetParent(btnObj.transform, false);
        RectTransform tRT = txtObj.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        Text t = txtObj.AddComponent<Text>();
        t.text = label;
        t.fontSize = (int)(h * 0.38f);
        t.color = ThemeManager.Instance.GetTextColor();
        t.alignment = TextAnchor.MiddleCenter;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.raycastTarget = false; // MUST BE FALSE so clicks hit the button!
        return btnObj;
    }

    private InputField CreateInputField(Transform parent, float x, float y, float w, float h, string placeholder)
    {
        GameObject inputObj = new GameObject("InputField");
        inputObj.layer = 5;
        inputObj.transform.SetParent(parent, false);
        RectTransform rt = inputObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        Image bg = inputObj.AddComponent<Image>();
        bg.color = ThemeManager.Instance.IsDarkMode ? new Color(0.14f, 0.16f, 0.22f) : new Color(0.95f, 0.95f, 0.98f);
        bg.raycastTarget = true;

        GameObject textObj = new GameObject("Text");
        textObj.layer = 5;
        textObj.transform.SetParent(inputObj.transform, false);
        RectTransform tRT = textObj.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = new Vector2(15, 0); tRT.offsetMax = new Vector2(-15, 0);
        Text t = textObj.AddComponent<Text>();
        t.fontSize = (int)(h * 0.36f);
        t.color = ThemeManager.Instance.GetTextColor();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.alignment = TextAnchor.MiddleLeft;

        GameObject phObj = new GameObject("Placeholder");
        phObj.layer = 5;
        phObj.transform.SetParent(inputObj.transform, false);
        RectTransform phRT = phObj.AddComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(15, 0); phRT.offsetMax = new Vector2(-15, 0);
        Text ph = phObj.AddComponent<Text>();
        ph.text = placeholder;
        ph.fontSize = (int)(h * 0.36f);
        ph.color = ThemeManager.Instance.GetSecondaryTextColor();
        ph.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ph.fontStyle = FontStyle.Italic;
        ph.alignment = TextAnchor.MiddleLeft;

        InputField inputField = inputObj.AddComponent<InputField>();
        inputField.textComponent = t;
        inputField.placeholder = ph;
        return inputField;
    }

    private GameObject CreateCard(Transform parent, float x, float y, float w, float h)
    {
        GameObject card = new GameObject("Card");
        card.layer = 5;
        card.transform.SetParent(parent, false);
        RectTransform rt = card.AddComponent<RectTransform>();

        bool isContentChild = (parent != null && parent.name == "Content");
        if (isContentChild)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
        }
        else
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        Image img = card.AddComponent<Image>();
        img.color = ThemeManager.Instance.GetCardColor();
        return card;
    }
}
