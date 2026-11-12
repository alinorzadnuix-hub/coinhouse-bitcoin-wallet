using UnityEngine;
using UnityEngine.UI;

namespace UnityBitcoinWallet
{
    public class SimpleWalletUI : MonoBehaviour
    {
        void Start()
        {
            BuildUI();
        }

        void BuildUI()
        {
            Canvas canvas = new GameObject("Canvas").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.gameObject.AddComponent<CanvasScaler>();
            canvas.gameObject.AddComponent<GraphicRaycaster>();

            GameObject panel = CreatePanel(canvas.transform);
            CreateControls(panel.transform);
        }

        GameObject CreatePanel(Transform parent)
        {
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(parent, false);
            
            RectTransform rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600, 700);
            
            Image img = panel.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);
            
            return panel;
        }

        void CreateControls(Transform parent)
        {
            float y = 300;

            CreateText(parent, "Bitcoin Wallet Manager", new Vector2(0, y), 24);
            y -= 60;

            CreateButton(parent, "تولید کیف پول TestNet", new Vector2(0, y), GenerateTestNet);
            y -= 50;

            CreateButton(parent, "تولید کیف پول MainNet", new Vector2(0, y), GenerateMainNet);
            y -= 80;

            CreateText(parent, "برای مشاهده نتایج Console را چک کنید", new Vector2(0, y), 14);
            y -= 40;

            CreateText(parent, "کلید G = تولید جدید", new Vector2(0, y), 12);
            y -= 30;

            CreateText(parent, "کلید T = تست", new Vector2(0, y), 12);
        }

        Text CreateText(Transform parent, string text, Vector2 pos, int fontSize)
        {
            GameObject obj = new GameObject("Text");
            obj.transform.SetParent(parent, false);
            
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(500, 40);
            
            Text txt = obj.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = Color.white;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            
            return txt;
        }

        void CreateButton(Transform parent, string text, Vector2 pos, UnityEngine.Events.UnityAction action)
        {
            GameObject obj = new GameObject("Button");
            obj.transform.SetParent(parent, false);
            
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(300, 40);
            
            Image img = obj.AddComponent<Image>();
            img.color = new Color(0.2f, 0.6f, 1f);
            
            Button btn = obj.AddComponent<Button>();
            btn.onClick.AddListener(action);
            
            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(obj.transform, false);
            Text txt = txtObj.AddComponent<Text>();
            txt.text = text;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = 16;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            
            RectTransform txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
        }

        void GenerateTestNet()
        {
            var manager = gameObject.AddComponent<WalletManager>();
            manager.GenerateNewWallet(false);
            Debug.Log("=== TestNet Wallet ===");
            Debug.Log($"Address: {manager.GetAddress(0)}");
            Debug.Log($"Private Key: {manager.GetPrivateKey(0)}");
            Debug.Log($"Seed: {manager.GetSeedPhrase()}");
        }

        void GenerateMainNet()
        {
            var manager = gameObject.AddComponent<WalletManager>();
            manager.GenerateNewWallet(true);
            Debug.Log("=== MainNet Wallet ===");
            Debug.Log($"Address: {manager.GetAddress(1)}");
            Debug.Log($"Private Key: {manager.GetPrivateKey(1)}");
            Debug.Log($"Seed: {manager.GetSeedPhrase()}");
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.G))
            {
                GenerateTestNet();
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                TestWallet();
            }
        }

        void TestWallet()
        {
            Debug.Log("=== شروع تست ===");
            var manager = gameObject.AddComponent<WalletManager>();
            manager.GenerateNewWallet(false);
            
            string addr = manager.GetAddress(0);
            string key = manager.GetPrivateKey(0);
            string seed = manager.GetSeedPhrase();
            
            Debug.Log($"آدرس: {addr}");
            Debug.Log($"کلید: {key}");
            Debug.Log($"Seed: {seed}");
            
            manager.RecoverWallet(seed, false);
            string recovered = manager.GetAddress(0);
            
            Debug.Log(addr == recovered ? "✅ تست موفق" : "❌ تست ناموفق");
        }
    }
}
