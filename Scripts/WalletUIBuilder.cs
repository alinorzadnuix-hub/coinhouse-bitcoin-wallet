using UnityEngine;
using UnityEngine.UI;

namespace UnityBitcoinWallet
{
    public class WalletUIBuilder : MonoBehaviour
    {
        private WalletManager walletManager;
        private InputField addressField, privateKeyField, seedPhraseField;
        private Dropdown networkDropdown;
        private InputField[] seedWords = new InputField[12];
        private InputField recoveredAddressField, recoveredKeyField;

        void Start()
        {
            walletManager = gameObject.AddComponent<WalletManager>();
            BuildUI();
        }

        void BuildUI()
        {
            // Canvas
            Canvas canvas = new GameObject("Canvas").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.gameObject.AddComponent<CanvasScaler>();
            canvas.gameObject.AddComponent<GraphicRaycaster>();

            // Panel اصلی
            GameObject mainPanel = CreatePanel(canvas.transform, new Rect(50, 50, 700, 900));

            // تب 1: تولید کیف پول
            CreateGenerateTab(mainPanel.transform);

            // تب 2: بازیابی
            CreateRecoveryTab(mainPanel.transform);
        }

        void CreateGenerateTab(Transform parent)
        {
            float y = -50;

            // عنوان
            CreateText(parent, "تولید کیف پول جدید", new Vector2(0, y), 20);
            y -= 50;

            // Network Dropdown
            CreateText(parent, "شبکه:", new Vector2(-250, y), 14);
            networkDropdown = CreateDropdown(parent, new Vector2(0, y), new string[] { "TestNet", "Mainnet" });
            y -= 50;

            // دکمه تولید
            CreateButton(parent, "تولید", new Vector2(-100, y), OnGenerate);
            CreateButton(parent, "پاک کردن", new Vector2(100, y), OnClear);
            y -= 60;

            // آدرس
            CreateText(parent, "آدرس:", new Vector2(-250, y), 14);
            y -= 30;
            addressField = CreateInputField(parent, new Vector2(0, y), 400);
            y -= 50;

            // کلید خصوصی
            CreateText(parent, "کلید خصوصی:", new Vector2(-250, y), 14);
            y -= 30;
            privateKeyField = CreateInputField(parent, new Vector2(0, y), 400);
            y -= 50;

            // Seed Phrase
            CreateText(parent, "Seed Phrase:", new Vector2(-250, y), 14);
            y -= 30;
            seedPhraseField = CreateInputField(parent, new Vector2(0, y), 400, 100, true);
            y -= 120;

            // دکمه موجودی
            CreateButton(parent, "چک موجودی", new Vector2(0, y), OnCheckBalance);
        }

        void CreateRecoveryTab(Transform parent)
        {
            float y = -500;

            CreateText(parent, "بازیابی کیف پول", new Vector2(0, y), 20);
            y -= 50;

            // 12 کلمه
            for (int i = 0; i < 12; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float x = col == 0 ? -150 : 150;
                float yPos = y - (row * 40);

                CreateText(parent, $"{i + 1:00}.", new Vector2(x - 120, yPos), 12);
                seedWords[i] = CreateInputField(parent, new Vector2(x, yPos), 200, 30);
            }
            y -= 260;

            // دکمه بازیابی
            CreateButton(parent, "بازیابی", new Vector2(0, y), OnRecover);
            y -= 50;

            // نتیجه
            CreateText(parent, "آدرس:", new Vector2(-250, y), 14);
            y -= 30;
            recoveredAddressField = CreateInputField(parent, new Vector2(0, y), 400);
            y -= 50;

            CreateText(parent, "کلید:", new Vector2(-250, y), 14);
            y -= 30;
            recoveredKeyField = CreateInputField(parent, new Vector2(0, y), 400);
        }

        GameObject CreatePanel(Transform parent, Rect rect)
        {
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(parent, false);
            
            RectTransform rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            
            Image img = panel.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);
            
            return panel;
        }

        Text CreateText(Transform parent, string text, Vector2 pos, int fontSize)
        {
            GameObject obj = new GameObject("Text");
            obj.transform.SetParent(parent, false);
            
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(200, 30);
            
            Text txt = obj.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = Color.white;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.alignment = TextAnchor.MiddleLeft;
            
            return txt;
        }

        InputField CreateInputField(Transform parent, Vector2 pos, float width, float height = 30, bool multiline = false)
        {
            GameObject obj = new GameObject("InputField");
            obj.transform.SetParent(parent, false);
            
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, height);
            
            Image img = obj.AddComponent<Image>();
            img.color = Color.white;
            
            InputField input = obj.AddComponent<InputField>();
            
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);
            Text txt = textObj.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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

        Dropdown CreateDropdown(Transform parent, Vector2 pos, string[] options)
        {
            GameObject obj = new GameObject("Dropdown");
            obj.transform.SetParent(parent, false);
            
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(200, 30);
            
            Image img = obj.AddComponent<Image>();
            img.color = Color.white;
            
            Dropdown dropdown = obj.AddComponent<Dropdown>();
            
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(obj.transform, false);
            Text label = labelObj.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 12;
            label.color = Color.black;
            
            RectTransform labelRt = labelObj.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(10, 2);
            labelRt.offsetMax = new Vector2(-25, -2);
            
            dropdown.captionText = label;
            dropdown.ClearOptions();
            dropdown.AddOptions(new System.Collections.Generic.List<string>(options));
            
            return dropdown;
        }

        void CreateButton(Transform parent, string text, Vector2 pos, UnityEngine.Events.UnityAction action)
        {
            GameObject obj = new GameObject("Button");
            obj.transform.SetParent(parent, false);
            
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(150, 35);
            
            Image img = obj.AddComponent<Image>();
            img.color = new Color(0.2f, 0.6f, 1f);
            
            Button btn = obj.AddComponent<Button>();
            btn.onClick.AddListener(action);
            
            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(obj.transform, false);
            Text txt = txtObj.AddComponent<Text>();
            txt.text = text;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = 14;
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
            walletManager.GenerateNewWallet(isMainnet);
            
            addressField.text = walletManager.GetAddress(isMainnet ? 1 : 0);
            privateKeyField.text = walletManager.GetPrivateKey(isMainnet ? 1 : 0);
            seedPhraseField.text = walletManager.GetSeedPhrase().Replace(" ", "\n");
            
            Debug.Log($"کیف پول ساخته شد: {(isMainnet ? "Mainnet" : "TestNet")}");
        }

        void OnClear()
        {
            addressField.text = "";
            privateKeyField.text = "";
            seedPhraseField.text = "";
        }

        async void OnCheckBalance()
        {
            if (string.IsNullOrEmpty(addressField.text))
            {
                Debug.LogWarning("آدرس خالی است!");
                return;
            }
            
            try
            {
                decimal balance = await walletManager.GetBalance(addressField.text);
                Debug.Log($"موجودی: {balance} BTC");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"خطا: {ex.Message}");
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
            
            try
            {
                walletManager.RecoverWallet(seedPhrase, true);
                recoveredAddressField.text = walletManager.GetAddress(0);
                recoveredKeyField.text = walletManager.GetPrivateKey(0);
                Debug.Log("کیف پول بازیابی شد!");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"خطا: {ex.Message}");
            }
        }
    }
}
