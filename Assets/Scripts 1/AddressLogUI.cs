using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using NBitcoin;
using System.Threading.Tasks;

public class AddressLogUI : MonoBehaviour
{
    private GameObject contentContainer;
    private Text statusText;




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

        // BG - Full Stretch
        GameObject bg = MakePanel(canvas.transform, "BG", 0, 0, 0, 0, ThemeManager.Instance.GetBackgroundColor());
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;

        // Top Bar
        float topBarH = isDesktop ? 150 : 200;
        GameObject topBar = MakePanel(canvas.transform, "TopBar", 0, 0, isDesktop ? 1920 : 1080, topBarH, ThemeManager.Instance.GetCardColor());
        RectTransform topBarRT = topBar.GetComponent<RectTransform>();
        topBarRT.anchorMin = new Vector2(0, 1); topBarRT.anchorMax = new Vector2(1, 1);
        topBarRT.pivot = new Vector2(0.5f, 1); topBarRT.anchoredPosition = Vector2.zero;

        MakeButton(topBar.transform, LocalizationManager.Instance.Get("GEN_BACK"), isDesktop ? -800 : -400, 0, 200, isDesktop ? 70 : 80, () => SceneManager.LoadScene("MainWallet"))
            .GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
            
        MakeText(topBar.transform, LocalizationManager.Instance.Get("LOG_TITLE"), 0, 0, isDesktop ? 34 : 40, ThemeManager.Instance.GetTextColor(), FontStyle.Bold);

        // Scroll View - Anchored to fill space BELOW TopBar
        GameObject scrollView = new GameObject("ScrollView");
        scrollView.layer = 5;
        scrollView.transform.SetParent(canvas.transform, false);
        
        // Add transparent image to capture drag events for scrolling
        Image scrollImg = scrollView.AddComponent<Image>();
        scrollImg.color = new Color(0,0,0,0); 

        RectTransform scrollRT = scrollView.GetComponent<RectTransform>();
        if (isDesktop)
        {
            scrollRT.anchorMin = new Vector2(0.5f, 0);
            scrollRT.anchorMax = new Vector2(0.5f, 1);
            scrollRT.pivot = new Vector2(0.5f, 0.5f);
            scrollRT.sizeDelta = new Vector2(1200, -topBarH);
            scrollRT.anchoredPosition = new Vector2(0, -topBarH / 2);
        }
        else
        {
            scrollRT.anchorMin = Vector2.zero; // Bottom Left
            scrollRT.anchorMax = Vector2.one;  // Top Right
            scrollRT.offsetMin = new Vector2(0, 0); // Bottom, Left margin
            scrollRT.offsetMax = new Vector2(0, -topBarH); // Top margin (below TopBar)
        }
        
        ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();
        scrollRect.horizontal = false; scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 40f;
        scrollRect.movementType = ScrollRect.MovementType.Elastic; // Better feel
        
        GameObject viewport = new GameObject("Viewport");
        viewport.layer = 5; viewport.transform.SetParent(scrollView.transform, false);
        RectTransform viewportRT = viewport.AddComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero; viewportRT.anchorMax = Vector2.one; 
        viewportRT.sizeDelta = Vector2.zero;
        viewport.AddComponent<RectMask2D>();
        viewport.AddComponent<Image>().color = new Color(0,0,0,0);

        GameObject content = new GameObject("Content");
        content.layer = 5; content.transform.SetParent(viewport.transform, false);
        RectTransform contentRT = content.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1); contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1); contentRT.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.padding = new RectOffset(0, 0, 40, 40);
        vlg.spacing = 30;
        vlg.childControlWidth = false; 
        vlg.childControlHeight = true; // Let layout group control height calculations
        
        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize; 
        
        scrollRect.content = contentRT;
        scrollRect.viewport = viewportRT;
        contentContainer = content;
        
        statusText = MakeText(canvas.transform, LocalizationManager.Instance.Get("GEN_LOADING"), 0, 0, 32, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Italic);
        
        // Ensure TopBar is strictly on top visually and for raycasts
        topBar.transform.SetAsLastSibling();
        // Status text on very top
        statusText.transform.SetAsLastSibling();
    }
    
    async void LoadAddresses()
    {
        await Task.Delay(100); // UI render
        
        WalletData wallet = WalletManager2.Instance.GetCurrentWallet();
        if (wallet == null) { statusText.text = LocalizationManager.Instance.Get("GEN_ERROR"); return; }
        
        statusText.text = LocalizationManager.Instance.Get("GEN_LOADING");
        
        // Show current main address
        GameObject mainHeader = MakeText(contentContainer.transform, 
            LocalizationManager.Instance.Get("LOG_MAIN_NET"), 
            0, 0, 36, new Color(1f, 0.84f, 0f), FontStyle.Bold).gameObject;
        mainHeader.AddComponent<LayoutElement>().minHeight = 70;
        
        CreateAddressRow(contentContainer.transform, "", wallet.address, "");
        await Task.Yield();
        
        // ✅ Show Parent Address (original first address) in ORANGE - always visible
        string parentAddress = wallet.parentAddress;
        
        // Fallback for older wallets
        if (string.IsNullOrEmpty(parentAddress))
        {
            if (wallet.generatedAddresses != null && wallet.generatedAddresses.Count > 0)
            {
                parentAddress = wallet.generatedAddresses[0].address;
                int lowestIdx = wallet.generatedAddresses[0].index;
                foreach (var ga in wallet.generatedAddresses)
                {
                    if (ga.index < lowestIdx)
                    {
                        lowestIdx = ga.index;
                        parentAddress = ga.address;
                    }
                }
            }
            else
            {
                parentAddress = wallet.address;
            }
        }
        
        // Always show parent address label in orange
        Color orangeColor = new Color(1f, 0.65f, 0f); // Orange
        GameObject parentLabel = MakeText(contentContainer.transform, 
            LocalizationManager.Instance.Get("LOG_PARENT_ADDR"), 
            0, 0, 34, orangeColor, FontStyle.Bold).gameObject;
        parentLabel.AddComponent<LayoutElement>().minHeight = 60;
        
        // Show the parent address row
        CreateAddressRow(contentContainer.transform, "", parentAddress, "");
        await Task.Yield();
        
        // Show all generated addresses
        if (wallet.generatedAddresses != null && wallet.generatedAddresses.Count > 0)
        {
            GameObject genHeader = MakeText(contentContainer.transform, 
                LocalizationManager.Instance.GetFormat("LOG_GEN_COUNT", wallet.generatedAddresses.Count), 
                0, 0, 36, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold).gameObject;
            genHeader.AddComponent<LayoutElement>().minHeight = 70;
            
            // Sort by index (newest first)
            var sortedAddresses = new List<GeneratedAddress>(wallet.generatedAddresses);
            sortedAddresses.Sort((a, b) => b.index.CompareTo(a.index));
            
            foreach (var genAddr in sortedAddresses)
            {
                string dateInfo = string.IsNullOrEmpty(genAddr.dateGenerated) ? 
                    $"Index {genAddr.index}" : 
                    $"Index {genAddr.index} - {genAddr.dateGenerated}";
                    
                CreateAddressRow(contentContainer.transform, dateInfo, genAddr.address, "");
                await Task.Yield();
            }
        }
        else
        {
            // No generated addresses yet
            GameObject noGenHeader = MakeText(contentContainer.transform, 
                LocalizationManager.Instance.Get("LOG_NONE"), 
                0, 0, 28, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Italic).gameObject;
            noGenHeader.AddComponent<LayoutElement>().minHeight = 100;
        }
        
        statusText.text = "";
        Destroy(statusText.gameObject);
    }
    
    void CreateAddressRow(Transform parent, string title, string address, string status = "")
    {
        bool isDesktop = false;
#if UNITY_STANDALONE || UNITY_EDITOR
        isDesktop = true;
#endif

        float rowW = isDesktop ? 1100 : 950;
        float rowH = isDesktop ? 100 : 120;
        GameObject card = MakePanel(parent, "Row", 0, 0, rowW, rowH, ThemeManager.Instance.GetCardColor());
        card.GetComponent<LayoutElement>().minHeight = rowH;
        
        // Title with status
        string displayTitle = string.IsNullOrEmpty(status) ? title : $"{title} [{status}]";
        Text titleText = MakeText(card.transform, displayTitle, isDesktop ? -460 : -400, isDesktop ? 22 : 30, isDesktop ? 20 : 24, ThemeManager.Instance.GetSecondaryTextColor(), FontStyle.Normal);
        titleText.alignment = TextAnchor.MiddleLeft;
        
        // Highlight active address
        if (!string.IsNullOrEmpty(status) && status == LocalizationManager.Instance.Get("STATUS_ACTIVE"))
        {
            titleText.color = new Color(0.3f, 1f, 0.4f); // Green for active
            titleText.fontStyle = FontStyle.Bold;
        }
        
        string displayAddr = address.Length > 28 ? address.Substring(0, 14) + "..." + address.Substring(address.Length-14) : address;
        if (isDesktop)
            displayAddr = address; // Show full address on desktop! Wide enough!
        
        Text addrText = MakeText(card.transform, displayAddr, isDesktop ? -35 : -50, isDesktop ? -12 : -10, isDesktop ? 24 : 28, ThemeManager.Instance.GetTextColor(), FontStyle.Normal);
        if (isDesktop)
        {
            addrText.GetComponent<RectTransform>().sizeDelta = new Vector2(850, 50);
            addrText.alignment = TextAnchor.MiddleLeft;
        }
        
        MakeButton(card.transform, LocalizationManager.Instance.Get("BTN_COPY"), isDesktop ? 470 : 380, 0, isDesktop ? 120 : 140, isDesktop ? 50 : 60, () => { GUIUtility.systemCopyBuffer = address; });
    }

    void Start()
    {
        // Ensure EventSystem exists
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        BuildUI();
        LoadAddresses();
    }

    // ... (BuildUI and LoadAddresses remain mostly same, focusing on helpers below)

    // --- HELPERS ---

    GameObject MakePanel(Transform parent, string name, float x, float y, float w, float h, Color color) {
        GameObject obj = new GameObject(name);
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(w, h);
        obj.AddComponent<Image>().color = color;
        obj.AddComponent<LayoutElement>();
        return obj;
    }

    Text MakeText(Transform parent, string txt, float x, float y, int size, Color col, FontStyle style) {
        GameObject obj = new GameObject("Text");
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(950, 60);
        Text text = obj.AddComponent<Text>();
        text.text = txt; text.fontSize = size; text.color = col; text.fontStyle = style;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false; // CRITICAL FIX: Don't block clicks/drags
        return text;
    }

    GameObject MakeButton(Transform parent, string txt, float x, float y, float w, float h, System.Action onClick) {
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
        
        GameObject tObj = new GameObject("Text");
        tObj.layer = 5; tObj.transform.SetParent(obj.transform, false);
        RectTransform tRT = tObj.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        Text t = tObj.AddComponent<Text>();
        t.text = txt; t.fontSize = 28; t.color = Color.white;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false; // CRITICAL FIX: Don't block button click
        return obj;
    }
}
