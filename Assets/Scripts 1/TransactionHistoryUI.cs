using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using NBitcoin; // For Mnemonic, ExtKey, etc.

public class TransactionHistoryUI : MonoBehaviour
{
    private GameObject contentContainer;
    private Text statusText;

    private struct AddressConfig
    {
        public string Name;
        public string Path;
        public ScriptPubKeyType ScriptType;
    }

    void Start()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
        
        BuildUI();
        LoadTransactionsFromAPI();
    }

    async void LoadTransactionsFromAPI()
    {
        WalletData wallet = WalletManager2.Instance.GetCurrentWallet();
        if (wallet == null)
        {
            if (statusText != null)
                statusText.text = LocalizationManager.Instance.Get("GEN_NO_WALLET");
            return;
        }

        // Debug: show which address will be scanned
#if TESTNET_BUILD
        WalletManager2.Instance.EnsureTestnetAddress();
        string debugAddr = wallet.testnetAddress ?? wallet.address ?? "(none)";
        Debug.Log($"[TxHistory] TestNet scan address: '{debugAddr}'");
        if (statusText != null) statusText.text = LocalizationManager.Instance.GetFormat("HIST_SCANNING_ADDR", debugAddr.Substring(0, Mathf.Min(20, debugAddr.Length)));
#else
        string debugAddr = wallet.address ?? "(none)";
        Debug.Log($"[TxHistory] MainNet scan address: '{debugAddr}'");
        if (statusText != null) statusText.text = LocalizationManager.Instance.GetFormat("HIST_SCANNING_ADDR", debugAddr.Substring(0, Mathf.Min(20, debugAddr.Length)));
#endif

        // Show cached transactions IMMEDIATELY while API loads
        if (wallet.transactions != null && wallet.transactions.Count > 0)
        {
            ShowCachedTransactions(wallet.transactions);
            if (statusText != null)
                statusText.text = LocalizationManager.Instance.Get("HIST_REFRESHING");
        }
        
        try
        {
            List<BlockCypherApiCore.TransactionDisplayData> allTxs =
                await GetAllAddressesTransactions(wallet.seedPhrase);

            if (statusText != null) statusText.text = "";

            if (allTxs == null || allTxs.Count == 0)
            {
                // Only show "no transactions" if there are also no cached ones
                if (wallet.transactions == null || wallet.transactions.Count == 0)
                {
                    if (statusText != null)
                        statusText.text = LocalizationManager.Instance.Get("HIST_NO_TX");
                }
                // else: leave the cached transactions visible
            }
            else
            {
                // ✅ Save fresh results back to wallet (enables instant cached display next time)
                wallet.transactions = new System.Collections.Generic.List<TransactionHistory>();
                foreach (var tx in allTxs)
                {
                    if (tx == null) continue;
                    wallet.transactions.Add(new TransactionHistory
                    {
                        type    = tx.type,
                        address = tx.otherAddress,
                        amount  = tx.amount,
                        date    = tx.date,
                        txHash  = tx.txHash,
                        fee     = tx.fee
                    });
                }
                // Persist to disk
                try
                {
                    string json = UnityEngine.JsonUtility.ToJson(
                        WalletManager2.Instance.GetCurrentWallet(), true);
                    string path = System.IO.Path.Combine(
                        UnityEngine.Application.persistentDataPath,
#if TESTNET_BUILD
                        "wallet_testnet.json"
#else
                        "wallet_mainnet.json"
#endif
                    );
#if UNITY_EDITOR
                    path = System.IO.Path.Combine(
                        UnityEngine.Application.persistentDataPath, "wallet_editor.json");
#endif
                    System.IO.File.WriteAllText(path, json);
                    Debug.Log($"[TxHistory] Saved {wallet.transactions.Count} transactions to disk");
                }
                catch (System.Exception saveEx)
                {
                    Debug.LogWarning("[TxHistory] Could not persist transactions: " + saveEx.Message);
                }

                // ✅ Render the fresh list
                ShowApiTxList(allTxs);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TxHistory] Load failed: {ex.Message}\n{ex.StackTrace}");
            if (statusText != null)
            {
                statusText.text = $"Error: {ex.Message}";
                statusText.color = Color.red;
            }
        }
    }

    // ✅ Render a list of TransactionDisplayData (from fresh API results)
    void ShowApiTxList(List<BlockCypherApiCore.TransactionDisplayData> txs)
    {
        if (contentContainer == null || txs == null) return;
        foreach (Transform child in contentContainer.transform) Destroy(child.gameObject);

        foreach (var tx in txs)
        {
            if (tx == null) continue;

            GameObject card = MakeCard(contentContainer.transform, 950, 200);
            Button cardBtn  = card.AddComponent<Button>();
            cardBtn.onClick.AddListener(() => ShowTransactionDetails(tx));

            bool    isRec   = tx.type == "Receive";
            Color   txColor = isRec ? new Color(1f, 0.5f, 0f) : new Color(1f, 0.3f, 0.3f);
            string  sym     = isRec ? "+" : "-";

            MakeText(card.transform, tx.type ?? "TX", 0, 75, 38, txColor);
#if TESTNET_BUILD
            MakeText(card.transform, $"{sym}{tx.amount} tBTC", 0, 35, 36, ThemeManager.Instance.GetTextColor());
#else
            MakeText(card.transform, $"{sym}{tx.amount} BTC", 0, 35, 36, ThemeManager.Instance.GetTextColor());
#endif
            string addrLabel = isRec
                ? LocalizationManager.Instance.Get("HIST_FROM") + " "
                : LocalizationManager.Instance.Get("HIST_TO")   + " ";
            string displayAddr = tx.otherAddress ?? LocalizationManager.Instance.Get("GEN_UNKNOWN");
            if (!string.IsNullOrEmpty(displayAddr) && displayAddr.Length > 32)
                displayAddr = displayAddr.Substring(0, 16) + "..." +
                              displayAddr.Substring(displayAddr.Length - 10);
            MakeText(card.transform, addrLabel + displayAddr, 0, -5, 24,
                     ThemeManager.Instance.GetSecondaryTextColor());

            string dateDisplay = tx.date ?? LocalizationManager.Instance.Get("GEN_UNKNOWN_DATE");
            Color  dateColor   = dateDisplay == "Unconfirmed"
                ? new Color(1f, 0.8f, 0.2f)
                : ThemeManager.Instance.GetSecondaryTextColor();
            string dateIcon = dateDisplay == "Unconfirmed" ? "⏳ " : "📅 ";
            MakeText(card.transform, dateIcon + dateDisplay, 0, -40, 26, dateColor);

            if (!string.IsNullOrEmpty(tx.txHash))
            {
                string hash = tx.txHash.Length > 16
                    ? tx.txHash.Substring(0, 16) + "..."
                    : tx.txHash;
                MakeText(card.transform, "TX: " + hash, 0, -75, 22, new Color(0.6f, 0.6f, 0.7f));
            }
        }
    }

    // ✅ NEW: Show cached transactions from wallet storage (instant, no API call needed)
    void ShowCachedTransactions(System.Collections.Generic.List<TransactionHistory> cachedTxs)
    {
        if (contentContainer == null) return;
        foreach(Transform child in contentContainer.transform) Destroy(child.gameObject);
        
        foreach (var tx in cachedTxs)
        {
            if (tx == null) continue;
            
            GameObject card = MakeCard(contentContainer.transform, 950, 200);
            Button cardBtn = card.AddComponent<Button>();
            
            // Build temporary TransactionDisplayData to call ShowTransactionDetails
            var tempTx = new BlockCypherApiCore.TransactionDisplayData
            {
                type = tx.type,
                otherAddress = tx.address,
                amount = tx.amount,
                date = tx.date,
                txHash = tx.txHash,
                fee = tx.fee
            };
            cardBtn.onClick.AddListener(() => ShowTransactionDetails(tempTx));
            
            bool isReceive = tx.type == "Receive";
            Color txColor = isReceive ? new Color(1f, 0.5f, 0f) : new Color(1f, 0.3f, 0.3f);
            string symbol = isReceive ? "+" : "-";
            
            MakeText(card.transform, tx.type ?? "TX", 0, 75, 38, txColor);
#if TESTNET_BUILD
            MakeText(card.transform, $"{symbol}{tx.amount} tBTC", 0, 35, 36, ThemeManager.Instance.GetTextColor());
#else
            MakeText(card.transform, $"{symbol}{tx.amount} BTC", 0, 35, 36, ThemeManager.Instance.GetTextColor());
#endif
            string addrLabel = isReceive ? LocalizationManager.Instance.Get("HIST_FROM") + " " : LocalizationManager.Instance.Get("HIST_TO") + " ";
            string displayAddr = tx.address ?? LocalizationManager.Instance.Get("GEN_UNKNOWN");
            if (!string.IsNullOrEmpty(displayAddr) && displayAddr.Length > 32)
                displayAddr = displayAddr.Substring(0, 16) + "..." + displayAddr.Substring(displayAddr.Length - 10);
            MakeText(card.transform, addrLabel + displayAddr, 0, -5, 24, ThemeManager.Instance.GetSecondaryTextColor());
            
            string dateDisplay = tx.date ?? LocalizationManager.Instance.Get("GEN_UNKNOWN_DATE");
            MakeText(card.transform, "📅 " + dateDisplay, 0, -40, 26, ThemeManager.Instance.GetSecondaryTextColor());
            
            if (!string.IsNullOrEmpty(tx.txHash))
            {
                string txShort = tx.txHash.Length > 16 ? tx.txHash.Substring(0, 16) + "..." : tx.txHash;
                MakeText(card.transform, "TX: " + txShort, 0, -75, 22, new Color(0.6f, 0.6f, 0.7f));
            }
        }
    }
    

    // ── Helper: fetch one address, deduplicate, return count added ────────────
    // (must be a real method, not a local function, for broad Unity C# compatibility)
    private BlockCypherApiCore _txApi;
    private bool _txIsTestNet;
    private List<BlockCypherApiCore.TransactionDisplayData> _txAllList;
    private System.Collections.Generic.HashSet<string> _txSeen;
    private System.Collections.Generic.HashSet<string> _txScanned;
    private Text _txStatusText;

    private async System.Threading.Tasks.Task<int> FetchAddressTxs(string addr)
    {
        if (string.IsNullOrEmpty(addr)) return 0;
        if (_txScanned != null && _txScanned.Contains(addr)) return 0;
        if (_txScanned != null) _txScanned.Add(addr);
        try
        {
            string shortAddr = addr.Length > 16 ? addr.Substring(0, 16) + "..." : addr;
            Debug.Log($"[TxHistory] Fetching: {addr} (isTestNet={_txIsTestNet})");
            if (_txStatusText != null)
                _txStatusText.text = LocalizationManager.Instance.GetFormat("HIST_SCANNING_ADDR", shortAddr);

            var txs = await _txApi.GetAllTransactions(addr, _txIsTestNet);
            int added = 0;
            if (txs != null)
            {
                foreach (var tx in txs)
                {
                    if (tx != null && !string.IsNullOrEmpty(tx.txHash) && !_txSeen.Contains(tx.txHash))
                    {
                        _txAllList.Add(tx);
                        _txSeen.Add(tx.txHash);
                        added++;
                    }
                }
            }
            Debug.Log($"[TxHistory] {shortAddr} → API returned {txs?.Count ?? 0} txs, {added} new added");
            return added;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TxHistory] FAILED for {addr}: {ex.Message}");
            if (_txStatusText != null)
            {
                _txStatusText.text = $"API Error: {ex.Message}";
                _txStatusText.color = Color.red;
            }
            return 0;
        }
    }

    async System.Threading.Tasks.Task<List<BlockCypherApiCore.TransactionDisplayData>> GetAllAddressesTransactions(string seedPhrase)
    {
#if TESTNET_BUILD
        _txIsTestNet = true;
#else
        _txIsTestNet = false;
#endif
        WalletData wallet = WalletManager2.Instance.GetCurrentWallet();
        if (wallet == null)
        {
            if (statusText != null) statusText.text = LocalizationManager.Instance.Get("GEN_NO_WALLET");
            return new List<BlockCypherApiCore.TransactionDisplayData>();
        }

        // Initialise shared state (FetchAddressTxs reads/writes these)
        _txApi     = new BlockCypherApiCore();
        _txAllList = new List<BlockCypherApiCore.TransactionDisplayData>();
        _txSeen    = new System.Collections.Generic.HashSet<string>();
        _txScanned = new System.Collections.Generic.HashSet<string>();
        _txStatusText = statusText;

        // ── STEP 0: Main wallet address ─────────────────────────────────────
#if TESTNET_BUILD
        // In TestNet, wallet.address is a MainNet address — we must use testnetAddress instead
        WalletManager2.Instance.EnsureTestnetAddress();
        string tbAddr = WalletManager2.Instance.GetTestnetAddress();
        if (!string.IsNullOrEmpty(tbAddr))
        {
            Debug.Log($"[TxHistory] Scanning TestNet address: {tbAddr}");
            await FetchAddressTxs(tbAddr);
        }
        else
        {
            Debug.LogWarning("[TxHistory] No testnet address found!");
        }
#else
        if (wallet != null && !string.IsNullOrEmpty(wallet.address))
            await FetchAddressTxs(wallet.address);
#endif

        // Show STEP 0 results immediately so user sees something right away
        if (_txAllList.Count > 0)
        {
            var earlyList = new List<BlockCypherApiCore.TransactionDisplayData>(_txAllList);
            ShowApiTxList(earlyList);
            if (statusText != null) statusText.text = LocalizationManager.Instance.Get("HIST_LOADING_MORE");
        }

        // ── STEP 1: HD-derived addresses (reduced for speed) ──────────────
        if (!string.IsNullOrEmpty(seedPhrase))
        {
            try
            {
                Mnemonic mnemonic  = new Mnemonic(seedPhrase, Wordlist.English);
                ExtKey   masterKey = mnemonic.DeriveExtKey();
#if TESTNET_BUILD
                NBitcoin.Network net  = NBitcoin.Network.TestNet;
                int              coin = 1;
#else
                NBitcoin.Network net  = NBitcoin.Network.Main;
                int              coin = 0;
#endif
                var configs = new[]
                {
                    new { Path = $"m/84'/{coin}'/0'/0", Type = ScriptPubKeyType.Segwit,      Name = "SegWit"      },
                    new { Path = $"m/44'/{coin}'/0'/0", Type = ScriptPubKeyType.Legacy,      Name = "Legacy"      },
                    new { Path = $"m/49'/{coin}'/0'/0", Type = ScriptPubKeyType.SegwitP2SH,  Name = "P2SH-SegWit" },
                };

                const int GAP   = 3;  // stop after 3 consecutive empty = no more activity
                const int LIMIT = 10; // max 10 per type (was 30)

                foreach (var cfg in configs)
                {
                    int gap = 0;
                    for (int i = 0; i < LIMIT && gap < GAP; i++)
                    {
                        string addrStr = masterKey
                            .Derive(new KeyPath($"{cfg.Path}/{i}"))
                            .PrivateKey.PubKey
                            .GetAddress(cfg.Type, net)
                            .ToString();

                        bool alreadyScanned = _txScanned.Contains(addrStr);
                        int before = _txAllList.Count;
                        await FetchAddressTxs(addrStr);
                        int added = _txAllList.Count - before;

                        if (alreadyScanned) continue;
                        gap = added > 0 ? 0 : gap + 1;
                        Debug.Log($"[TxHistory] {cfg.Name}[{i}] +{added} gap={gap}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TxHistory] HD derivation error: {ex.Message}");
            }
        }

        // ── STEP 2: Manually generated addresses ────────────────────────────
#if !TESTNET_BUILD
        // Generated addresses are MainNet (bc1/1/3...) — skip for TestNet to avoid wrong-network lookups
        if (wallet?.generatedAddresses != null)
            foreach (var ga in wallet.generatedAddresses)
                await FetchAddressTxs(ga.address);
#endif

        _txAllList.Sort((a, b) => string.Compare(b.date ?? "", a.date ?? ""));
        Debug.Log($"[TxHistory] Complete — {_txAllList.Count} txs from {_txScanned.Count} addresses");
        return _txAllList;
    }
    
    async System.Threading.Tasks.Task<List<BlockCypherApiCore.TransactionDisplayData>> GetAddressTransactions(string address)
    {
        var txs = new List<BlockCypherApiCore.TransactionDisplayData>();
        
        try 
        {
             BlockCypherApiCore api = new BlockCypherApiCore();
             // Use build-variant-aware network selection
#if TESTNET_BUILD
             bool isTestNet = true;
#else
             bool isTestNet = false;
#endif
             var coreTxs = await api.GetAllTransactions(address, isTestNet);
             if(coreTxs != null) txs.AddRange(coreTxs);
        }
        catch (System.Exception ex)
        {
             Debug.LogError($"API Error: {ex.Message}");
        }
        
        return txs;
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
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
        }
        
        canvasObj.AddComponent<GraphicRaycaster>();

        // BG - Themed
        GameObject bg = new GameObject("BG");
        bg.layer = 5;
        bg.transform.SetParent(canvas.transform, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one; bgRT.sizeDelta = Vector2.zero;
        bg.AddComponent<Image>().color = ThemeManager.Instance.GetBackgroundColor();

        if (isDesktop)
        {
            // Desktop landscape layout
            float y = 420;
            MakeText(canvas.transform, LocalizationManager.Instance.Get("HIST_TITLE"), 0, y, 65, ThemeManager.Instance.GetTextColor());

            WalletData wallet = WalletManager2.Instance.GetCurrentWallet();
            if (wallet != null)
            {
#if TESTNET_BUILD
                WalletManager2.Instance.EnsureTestnetAddress();
                string showAddr = wallet.testnetAddress;
#else
                string showAddr = wallet.address;
#endif
                if (!string.IsNullOrEmpty(showAddr))
                {
                    string displayAddress = showAddr.Length > 20
                        ? showAddr.Substring(0, 16) + "..." + showAddr.Substring(showAddr.Length - 6)
                        : showAddr;
                    MakeText(canvas.transform, $"Wallet: {displayAddress}", 0, y - 80, 28, ThemeManager.Instance.GetAccentColor());
                }
            }

            GameObject scrollView = new GameObject("ScrollView");
            scrollView.layer = 5;
            scrollView.transform.SetParent(canvas.transform, false);
            RectTransform scrollRT = scrollView.AddComponent<RectTransform>();
            scrollRT.anchoredPosition = new Vector2(0, -120);
            scrollRT.sizeDelta = new Vector2(1100, 700);
            
            // Themed Scroll BG
            Image scrollBg = scrollView.AddComponent<Image>();
            scrollBg.color = ThemeManager.Instance.IsDarkMode ? new Color(0.05f, 0.05f, 0.08f) : new Color(0.9f, 0.9f, 0.95f);
            
            ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();
            scrollRect.horizontal = false; scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 30f;
            
            GameObject viewport = new GameObject("Viewport");
            viewport.layer = 5; viewport.transform.SetParent(scrollView.transform, false);
            RectTransform viewportRT = viewport.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero; viewportRT.anchorMax = Vector2.one; viewportRT.sizeDelta = Vector2.zero;
            viewport.AddComponent<RectMask2D>();
            viewport.AddComponent<Image>().color = new Color(0,0,0,0);

            GameObject content = new GameObject("Content");
            content.layer = 5; content.transform.SetParent(viewport.transform, false);
            RectTransform contentRT = content.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1); contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1); contentRT.anchoredPosition = Vector2.zero;

            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.padding = new RectOffset(0, 0, 20, 20);
            vlg.spacing = 20; 
            vlg.childControlWidth = false; vlg.childControlHeight = false;
            
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize; 

            scrollRect.content = contentRT;
            scrollRect.viewport = viewportRT;
            
            contentContainer = content;
            
            statusText = MakeText(canvas.transform, LocalizationManager.Instance.Get("GEN_LOADING"), 0, -120, 40, ThemeManager.Instance.GetSecondaryTextColor());
            
            // Back Button
            MakeButton(canvas.transform, LocalizationManager.Instance.Get("GEN_BACK"), -750, y, 240, 80, () => SceneManager.LoadScene("MainWallet"))
                .GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
        }
        else
        {
            // Mobile portrait layout (original)
            float y = 700;
            MakeText(canvas.transform, LocalizationManager.Instance.Get("HIST_TITLE"), 0, y, 65, ThemeManager.Instance.GetTextColor());
            y -= 160;

            WalletData wallet = WalletManager2.Instance.GetCurrentWallet();
            if (wallet != null)
            {
#if TESTNET_BUILD
                WalletManager2.Instance.EnsureTestnetAddress();
                string showAddr = wallet.testnetAddress;
#else
                string showAddr = wallet.address;
#endif
                if (!string.IsNullOrEmpty(showAddr))
                {
                    string displayAddress = showAddr.Length > 20
                        ? showAddr.Substring(0, 16) + "..." + showAddr.Substring(showAddr.Length - 6)
                        : showAddr;
                    MakeText(canvas.transform, $"Wallet: {displayAddress}", 0, y, 28, ThemeManager.Instance.GetAccentColor());
                    y -= 100;
                }
            }

            GameObject scrollView = new GameObject("ScrollView");
            scrollView.layer = 5;
            scrollView.transform.SetParent(canvas.transform, false);
            RectTransform scrollRT = scrollView.AddComponent<RectTransform>();
            scrollRT.anchoredPosition = new Vector2(0, -100);
            scrollRT.sizeDelta = new Vector2(1000, 1100);
            
            Image scrollBg = scrollView.AddComponent<Image>();
            scrollBg.color = ThemeManager.Instance.IsDarkMode ? new Color(0.05f, 0.05f, 0.08f) : new Color(0.9f, 0.9f, 0.95f);
            
            ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();
            scrollRect.horizontal = false; scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 30f;
            
            GameObject viewport = new GameObject("Viewport");
            viewport.layer = 5; viewport.transform.SetParent(scrollView.transform, false);
            RectTransform viewportRT = viewport.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero; viewportRT.anchorMax = Vector2.one; viewportRT.sizeDelta = Vector2.zero;
            viewport.AddComponent<RectMask2D>();
            viewport.AddComponent<Image>().color = new Color(0,0,0,0);

            GameObject content = new GameObject("Content");
            content.layer = 5; content.transform.SetParent(viewport.transform, false);
            RectTransform contentRT = content.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1); contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1); contentRT.anchoredPosition = Vector2.zero;

            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.padding = new RectOffset(0, 0, 20, 20);
            vlg.spacing = 20; 
            vlg.childControlWidth = false; vlg.childControlHeight = false;
            
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize; 

            scrollRect.content = contentRT;
            scrollRect.viewport = viewportRT;
            
            contentContainer = content;
            
            statusText = MakeText(canvas.transform, LocalizationManager.Instance.Get("GEN_LOADING"), 0, 0, 40, ThemeManager.Instance.GetSecondaryTextColor());
            
            // Back Button
            MakeButton(canvas.transform, LocalizationManager.Instance.Get("GEN_BACK"), -400, 850, 280, 100, () => SceneManager.LoadScene("MainWallet"))
                .GetComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
        }
    }

    GameObject MakeCard(Transform parent, float w, float h)
    {
        GameObject card = new GameObject("Card");
        card.layer = 5;
        card.transform.SetParent(parent, false);
        RectTransform rt = card.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        Image img = card.AddComponent<Image>();
        img.color = ThemeManager.Instance.GetCardColor();
        LayoutElement le = card.AddComponent<LayoutElement>();
        le.preferredWidth = w; le.preferredHeight = h; le.minHeight = h;
        return card;
    }

    Text MakeText(Transform parent, string txt, float x, float y, int size, Color col)
    {
        GameObject obj = new GameObject("Text");
        obj.layer = 5; obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(950, 50);
        Text text = obj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt); text.fontSize = size; text.color = col;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        return text;
    }

    GameObject MakeButton(Transform parent, string txt, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
    {
        GameObject obj = new GameObject("Btn");
        obj.layer = 5; obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        Image img = obj.AddComponent<Image>();
        img.color = new Color(0.2f, 0.5f, 1f);
        Button btn = obj.AddComponent<Button>();
        btn.onClick.AddListener(action);
        
        GameObject txtObj = new GameObject("Text");
        txtObj.layer = 5; txtObj.transform.SetParent(obj.transform, false);
        RectTransform txtRT = txtObj.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
        Text text = txtObj.AddComponent<Text>();
        text.text = LocalizationManager.CleanIcons(txt); text.fontSize = 40; text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter; text.fontStyle = FontStyle.Bold;
        return obj;
    }
    
    void ShowTransactionDetails(BlockCypherApiCore.TransactionDisplayData tx)
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("Canvas not found");
            return;
        }
        
        GameObject overlay = new GameObject("TxDetailsOverlay");
        overlay.layer = 5;
        overlay.transform.SetParent(canvas.transform, false);
        RectTransform overlayRT = overlay.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero; overlayRT.anchorMax = Vector2.one; overlayRT.sizeDelta = Vector2.zero;
        Image overlayImg = overlay.AddComponent<Image>();
        overlayImg.color = new Color(0, 0, 0, 0.85f);
        
        GameObject panel = new GameObject("DetailsPanel");
        panel.layer = 5; panel.transform.SetParent(overlay.transform, false);
        RectTransform panelRT = panel.AddComponent<RectTransform>();
        
        bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktop = true;
#elif UNITY_EDITOR
        isDesktop = (Screen.width > Screen.height);
#endif

        if (isDesktop)
        {
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.anchoredPosition = Vector2.zero;
            panelRT.sizeDelta = new Vector2(980, 1050);
            panel.transform.localScale = new Vector3(0.85f, 0.85f, 1f);
        }
        else
        {
            panelRT.anchorMin = new Vector2(0, 0);
            panelRT.anchorMax = new Vector2(1, 0.5f);
            panelRT.sizeDelta = Vector2.zero;
        }

        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = ThemeManager.Instance.GetCardColor();
        
        float y = 450;
        
        // Content
        Color titleColor = tx.type == "Receive" ? new Color(1f, 0.5f, 0f) : new Color(1f, 0.3f, 0.3f);
        MakeText(panel.transform, LocalizationManager.Instance.Get("HIST_DETAILS"), 0, y, 48, titleColor);
        y -= 100;
        
        // Amount
        string symbol = tx.type == "Receive" ? "+" : "-";
        MakeText(panel.transform, LocalizationManager.Instance.Get("HIST_AMOUNT_LABEL"), 0, y, 32, ThemeManager.Instance.GetTextColor());
        y -= 50;
#if TESTNET_BUILD
        MakeText(panel.transform, $"{symbol}{tx.amount} tBTC", 0, y, 42, new Color(1f, 0.8f, 0f));
#else
        MakeText(panel.transform, $"{symbol}{tx.amount} BTC", 0, y, 42, new Color(1f, 0.8f, 0f));
#endif
        y -= 80;
        
        // Date
        MakeText(panel.transform, LocalizationManager.Instance.Get("HIST_DATE"), 0, y, 32, ThemeManager.Instance.GetTextColor());
        y -= 50;
        string dateDisplay = tx.date ?? LocalizationManager.Instance.Get("GEN_UNKNOWN_DATE");
        Color dateColor = dateDisplay == "Unconfirmed" ? new Color(1f, 0.8f, 0.2f) : new Color(0.7f, 0.9f, 1f);
        MakeText(panel.transform, dateDisplay, 0, y, 36, dateColor);
        y -= 80;

        // Fee
        MakeText(panel.transform, LocalizationManager.Instance.Get("HIST_FEE_LABEL"), 0, y, 32, ThemeManager.Instance.GetTextColor());
        y -= 50;
        string feeVal = string.IsNullOrEmpty(tx.fee) ? "0.00000000" : tx.fee;
#if TESTNET_BUILD
        string feeDisplay = $"{feeVal} tBTC";
#else
        string feeDisplay = $"{feeVal} BTC";
#endif
        MakeText(panel.transform, feeDisplay, 0, y, 36, new Color(0.7f, 0.9f, 1f));
        y -= 80;
        
        // Address
        MakeText(panel.transform, tx.type == "Receive" ? LocalizationManager.Instance.Get("HIST_FROM") : LocalizationManager.Instance.Get("HIST_TO"), 0, y, 32, ThemeManager.Instance.GetTextColor());
        y -= 50;
        CreateCopyableBox(panel.transform, 0, y, 950, 80, tx.otherAddress ?? LocalizationManager.Instance.Get("GEN_UNKNOWN"));
        y -= 110;
        
        // Hash
        MakeText(panel.transform, LocalizationManager.Instance.Get("HIST_TXID"), 0, y, 32, ThemeManager.Instance.GetTextColor());
        y -= 50;
        CreateCopyableBox(panel.transform, 0, y, 950, 80, tx.txHash ?? LocalizationManager.Instance.Get("GEN_UNKNOWN"));
        y -= 110;
 
        // Close
        MakeButton(panel.transform, LocalizationManager.Instance.Get("GEN_CLOSE"), 0, -450, 500, 90, () => Destroy(overlay))
            .GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f);
    }
    
    GameObject CreateCopyableBox(Transform parent, float x, float y, float w, float h, string content)
    {
        GameObject box = new GameObject("CopyableBox");
        box.layer = 5; box.transform.SetParent(parent, false);
        RectTransform boxRT = box.AddComponent<RectTransform>();
        boxRT.anchoredPosition = new Vector2(x, y); boxRT.sizeDelta = new Vector2(w, h);
        Image boxImg = box.AddComponent<Image>();
        boxImg.color = ThemeManager.Instance.IsDarkMode ? new Color(0.1f, 0.1f, 0.15f) : new Color(0.95f, 0.95f, 0.98f);
        
        GameObject textObj = new GameObject("Text");
        textObj.layer = 5; textObj.transform.SetParent(box.transform, false);
        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(20, 0); textRT.offsetMax = new Vector2(-(h + 10), 0);
        
        Text text = textObj.AddComponent<Text>();
        string displayContent = content;
        if (!string.IsNullOrEmpty(content) && content.Length > 40)
        {
            int len = Mathf.Min(40, content.Length);
            displayContent = content.Substring(0, len) + "...";
        }
        text.text = displayContent;
        text.fontSize = 28;
        text.color = ThemeManager.Instance.GetAccentColor();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleLeft;
        
        // Button
        GameObject btnObj = new GameObject("CopyBtn");
        btnObj.layer = 5; btnObj.transform.SetParent(box.transform, false);
        RectTransform btnRT = btnObj.AddComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(1, 0.5f);
        btnRT.anchorMax = new Vector2(1, 0.5f);
        btnRT.pivot = new Vector2(1, 0.5f);
        btnRT.anchoredPosition = Vector2.zero;
        btnRT.sizeDelta = new Vector2(h, h);
        btnObj.AddComponent<Image>().color = ThemeManager.Instance.GetAccentColor();
        btnObj.AddComponent<Button>().onClick.AddListener(()=> GUIUtility.systemCopyBuffer=content);
        
        // Icon
        GameObject i = new GameObject("I"); i.layer=5; i.transform.SetParent(btnObj.transform, false);
        RectTransform iRT = i.AddComponent<RectTransform>();
        iRT.anchorMin=Vector2.zero; iRT.anchorMax=Vector2.one;
        iRT.offsetMin=Vector2.zero; iRT.offsetMax=Vector2.zero;
        Text it = i.AddComponent<Text>(); it.text="❐"; it.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        it.alignment=TextAnchor.MiddleCenter; it.fontSize=32; it.color=Color.white;

        return box;
    }
}


