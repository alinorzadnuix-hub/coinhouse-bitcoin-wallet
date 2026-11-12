using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NBitcoin;
using System.Collections.Generic;

public class AutoWalletUI : MonoBehaviour
{
    private BitcoinWalletCore wallet;
    private InputField addressField, privateKeyField, seedPhraseField;
    private Dropdown networkDropdown;
    private InputField[] seedWords = new InputField[12];
    private InputField recoveredAddressField, recoveredKeyField;

    void Start()
    {
        CreateEventSystem();
        CreateUI();
    }

    void CreateEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }
    }

    void CreateUI()
    {
        Canvas canvas = new GameObject("Canvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.gameObject.AddComponent<CanvasScaler>();
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        GameObject panel = CreatePanel(canvas.transform, 800, 950);
        
        float y = 450;

        // Generate Wallet
        AddLabel(panel.transform, "Bitcoin Wallet Generator", 0, y, 22, true);
        y -= 50;

        AddLabel(panel.transform, "Network:", -300, y, 14);
        networkDropdown = AddDropdown(panel.transform, -100, y, new[] { "TestNet", "Mainnet" });
        y -= 50;

        AddButton(panel.transform, "Generate", -150, y, OnGenerate);
        AddButton(panel.transform, "Clear", 0, y, OnClear);
        AddButton(panel.transform, "Balance", 150, y, OnBalance);
        y -= 60;

        AddLabel(panel.transform, "Address:", -350, y, 14);
        y -= 30;
        addressField = AddInputField(panel.transform, 0, y, 650, 30);
        y -= 50;

        AddLabel(panel.transform, "Private Key:", -350, y, 14);
        y -= 30;
        privateKeyField = AddInputField(panel.transform, 0, y, 650, 30);
        y -= 50;

        AddLabel(panel.transform, "Seed Phrase (12 words):", -350, y, 14);
        y -= 30;
        seedPhraseField = AddInputField(panel.transform, 0, y, 650, 100, true);
        y -= 130;

        // Recovery
        AddLabel(panel.transform, "Recover Wallet", 0, y, 20, true);
        y -= 40;

        for (int i = 0; i < 12; i++)
        {
            int col = i % 3;
            int row = i / 3;
            float x = -250 + (col * 250);
            float yPos = y - (row * 45);
            
            AddLabel(panel.transform, $"{i + 1:00}", x - 80, yPos, 12);
            seedWords[i] = AddInputField(panel.transform, x, yPos, 150, 30);
        }
        y -= 200;

        AddButton(panel.transform, "Recover Wallet", 0, y, OnRecover);
        y -= 50;

        AddLabel(panel.transform, "Recovered Address:", -350, y, 14);
        y -= 30;
        recoveredAddressField = AddInputField(panel.transform, 0, y, 650, 30);
        y -= 50;

        AddLabel(panel.transform, "Recovered Key:", -350, y, 14);
        y -= 30;
        recoveredKeyField = AddInputField(panel.transform, 0, y, 650, 30);
    }

    GameObject CreatePanel(Transform parent, float width, float height)
    {
        GameObject panel = new GameObject("MainPanel");
        panel.transform.SetParent(parent, false);
        
        RectTransform rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);
        
        Image img = panel.AddComponent<Image>();
        img.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
        
        return panel;
    }

    void AddLabel(Transform parent, string text, float x, float y, int size, bool center = false)
    {
        GameObject obj = new GameObject("Label");
        obj.transform.SetParent(parent, false);
        
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(center ? 700 : 300, 30);
        
        Text txt = obj.AddComponent<Text>();
        txt.text = text;
        txt.fontSize = size;
        txt.color = Color.white;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.alignment = center ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
    }

    InputField AddInputField(Transform parent, float x, float y, float width, float height, bool multiline = false)
    {
        GameObject obj = new GameObject("InputField");
        obj.transform.SetParent(parent, false);
        
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(width, height);
        
        Image img = obj.AddComponent<Image>();
        img.color = Color.white;
        
        InputField input = obj.AddComponent<InputField>();
        
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(obj.transform, false);
        Text txt = textObj.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 12;
        txt.color = Color.black;
        txt.supportRichText = false;
        
        RectTransform txtRt = textObj.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = new Vector2(5, 5);
        txtRt.offsetMax = new Vector2(-5, -5);
        
        input.textComponent = txt;
        input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        
        return input;
    }

    Dropdown AddDropdown(Transform parent, float x, float y, string[] options)
    {
        GameObject obj = new GameObject("Dropdown");
        obj.transform.SetParent(parent, false);
        
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(200, 30);
        
        Image img = obj.AddComponent<Image>();
        img.color = Color.white;
        
        Dropdown dropdown = obj.AddComponent<Dropdown>();
        
        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(obj.transform, false);
        Text label = labelObj.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 12;
        label.color = Color.black;
        
        RectTransform labelRt = labelObj.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(10, 2);
        labelRt.offsetMax = new Vector2(-25, -2);
        
        // Template
        GameObject template = new GameObject("Template");
        template.transform.SetParent(obj.transform, false);
        RectTransform templateRt = template.AddComponent<RectTransform>();
        templateRt.anchorMin = new Vector2(0, 0);
        templateRt.anchorMax = new Vector2(1, 0);
        templateRt.pivot = new Vector2(0.5f, 1);
        templateRt.anchoredPosition = new Vector2(0, 2);
        templateRt.sizeDelta = new Vector2(0, 150);
        
        Image templateImg = template.AddComponent<Image>();
        templateImg.color = Color.white;
        
        ScrollRect scrollRect = template.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        
        // Viewport
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(template.transform, false);
        RectTransform viewportRt = viewport.AddComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.sizeDelta = Vector2.zero;
        viewportRt.pivot = new Vector2(0, 1);
        
        Image viewportImg = viewport.AddComponent<Image>();
        viewportImg.color = Color.white;
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        
        // Content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRt = content.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1);
        contentRt.sizeDelta = new Vector2(0, 28);
        
        // Item
        GameObject item = new GameObject("Item");
        item.transform.SetParent(content.transform, false);
        RectTransform itemRt = item.AddComponent<RectTransform>();
        itemRt.anchorMin = new Vector2(0, 0.5f);
        itemRt.anchorMax = new Vector2(1, 0.5f);
        itemRt.sizeDelta = new Vector2(0, 20);
        
        Toggle toggle = item.AddComponent<Toggle>();
        
        // Item Background
        Image itemBg = item.AddComponent<Image>();
        itemBg.color = Color.white;
        
        // Item Checkmark
        GameObject checkmark = new GameObject("Item Checkmark");
        checkmark.transform.SetParent(item.transform, false);
        RectTransform checkRt = checkmark.AddComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0, 0.5f);
        checkRt.anchorMax = new Vector2(0, 0.5f);
        checkRt.sizeDelta = new Vector2(20, 20);
        checkRt.anchoredPosition = new Vector2(10, 0);
        
        Image checkImg = checkmark.AddComponent<Image>();
        checkImg.color = Color.black;
        
        // Item Label
        GameObject itemLabel = new GameObject("Item Label");
        itemLabel.transform.SetParent(item.transform, false);
        RectTransform itemLabelRt = itemLabel.AddComponent<RectTransform>();
        itemLabelRt.anchorMin = Vector2.zero;
        itemLabelRt.anchorMax = Vector2.one;
        itemLabelRt.offsetMin = new Vector2(20, 1);
        itemLabelRt.offsetMax = new Vector2(-10, -2);
        
        Text itemText = itemLabel.AddComponent<Text>();
        itemText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        itemText.fontSize = 12;
        itemText.color = Color.black;
        
        toggle.targetGraphic = itemBg;
        toggle.graphic = checkImg;
        toggle.isOn = true;
        
        scrollRect.content = contentRt;
        scrollRect.viewport = viewportRt;
        
        dropdown.template = templateRt;
        dropdown.captionText = label;
        dropdown.itemText = itemText;
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(options));
        
        template.SetActive(false);
        
        return dropdown;
    }

    void AddButton(Transform parent, string text, float x, float y, UnityEngine.Events.UnityAction action)
    {
        GameObject obj = new GameObject("Button");
        obj.transform.SetParent(parent, false);
        
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(140, 35);
        
        Image img = obj.AddComponent<Image>();
        img.color = new Color(0.2f, 0.5f, 0.9f);
        
        Button btn = obj.AddComponent<Button>();
        btn.onClick.AddListener(action);
        
        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(obj.transform, false);
        Text txt = txtObj.AddComponent<Text>();
        txt.text = text;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 13;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;
        
        RectTransform txtRt = txtObj.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;
    }

    void OnGenerate()
    {
        bool isMainnet = networkDropdown.value == 1;
        NBitcoin.Network network = isMainnet ? NBitcoin.Network.Main : NBitcoin.Network.TestNet;
        
        wallet = new BitcoinWalletCore(network);
        
        int index = isMainnet ? 1 : 0;
        addressField.text = wallet.GetAddress(index);
        privateKeyField.text = wallet.GetPrivateKey(index);
        seedPhraseField.text = wallet.GetSeedPhrase().Replace(" ", "\n");
        
        Debug.Log($"✅ Wallet Created: {(isMainnet ? "Mainnet" : "TestNet")}");
        Debug.Log($"Address: {addressField.text}");
    }

    void OnClear()
    {
        addressField.text = "";
        privateKeyField.text = "";
        seedPhraseField.text = "";
    }

    async void OnBalance()
    {
        if (string.IsNullOrEmpty(addressField.text))
        {
            Debug.LogWarning("⚠️ Address is empty!");
            return;
        }
        
        Debug.Log($"Checking balance for: {addressField.text}");
        
        try
        {
            var api = new BlockCypherApiCore();
            decimal balance = await api.GetBalance(addressField.text);
            Debug.Log($"💰 Balance: {balance} BTC");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"❌ Error: {ex.Message}");
        }
    }

    void OnRecover()
    {
        string seedPhrase = "";
        for (int i = 0; i < 12; i++)
        {
            seedPhrase += seedWords[i].text.Trim() + " ";
        }
        seedPhrase = seedPhrase.Trim();
        
        if (string.IsNullOrEmpty(seedPhrase))
        {
            Debug.LogWarning("⚠️ Please enter 12 words!");
            return;
        }
        
        try
        {
            wallet = new BitcoinWalletCore(seedPhrase, NBitcoin.Network.Main);
            recoveredAddressField.text = wallet.GetAddress(0);
            recoveredKeyField.text = wallet.GetPrivateKey(0);
            Debug.Log("✅ Wallet recovered successfully!");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"❌ Recovery error: {ex.Message}");
        }
    }
}
