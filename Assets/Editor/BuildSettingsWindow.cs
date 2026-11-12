using UnityEditor;
using UnityEngine;
using System.IO;

public class BuildSettingsWindow : EditorWindow
{
    // ── Settings ────────────────────────────────────────────────────────────
    private string mainNetName;
    private string mainNetPackage;
    private string testNetName;
    private string testNetPackage;
    private string version;

    // Icon asset paths (stored in EditorPrefs)
    private string mainNetIconPath;
    private string testNetIconPath;

    // Cached Texture2D for preview
    private Texture2D mainNetIconPreview;
    private Texture2D testNetIconPreview;

    // Scroll position
    private Vector2 scrollPos;

    // UI Styles
    private GUIStyle headerStyle;
    private GUIStyle sectionStyle;
    private GUIStyle iconBoxStyle;
    private bool stylesInitialized;

    // ── Open Window ──────────────────────────────────────────────────────────
    [MenuItem("Bitcoin Wallet/Build Settings")]
    public static void ShowWindow()
    {
        var win = GetWindow<BuildSettingsWindow>("⚙️ Build Settings");
        win.minSize = new Vector2(480, 620);
    }

    private void OnEnable()
    {
        LoadSettings();
        RefreshIconPreviews();
    }

    // ── Load / Save ──────────────────────────────────────────────────────────
    private void LoadSettings()
    {
        mainNetName    = EditorPrefs.GetString("BuildVariant_NameMainNet",    "CoinHouse");
        mainNetPackage = EditorPrefs.GetString("BuildVariant_PackageMainNet", "com.coinhouse.wallet");
        mainNetIconPath= EditorPrefs.GetString("BuildVariant_IconMainNet",    "Assets/Resources/Minnet.png");
        if (string.IsNullOrEmpty(mainNetIconPath) || mainNetIconPath.Contains("satolite_logo_bw") || mainNetIconPath.Contains("y.png") || !File.Exists(mainNetIconPath))
        {
            mainNetIconPath = "Assets/Resources/Minnet.png";
            EditorPrefs.SetString("BuildVariant_IconMainNet", mainNetIconPath);
        }

        testNetName    = EditorPrefs.GetString("BuildVariant_NameTestNet",    "CoinHouse Testnet");
        testNetPackage = EditorPrefs.GetString("BuildVariant_PackageTestNet", "com.coinhouse.wallet.testnet");
        testNetIconPath= EditorPrefs.GetString("BuildVariant_IconTestNet",    "Assets/Resources/testnet.png");
        if (string.IsNullOrEmpty(testNetIconPath) || testNetIconPath.Contains("satolite_logo_bw") || testNetIconPath.Contains("Bitcointa") || !File.Exists(testNetIconPath))
        {
            testNetIconPath = "Assets/Resources/testnet.png";
            EditorPrefs.SetString("BuildVariant_IconTestNet", testNetIconPath);
        }

        version = EditorPrefs.GetString("BuildVariant_Version", "1.0.0");
    }

    private void SaveSettings()
    {
        EditorPrefs.SetString("BuildVariant_NameMainNet",    mainNetName);
        EditorPrefs.SetString("BuildVariant_PackageMainNet", mainNetPackage);
        EditorPrefs.SetString("BuildVariant_IconMainNet",    mainNetIconPath);

        EditorPrefs.SetString("BuildVariant_NameTestNet",    testNetName);
        EditorPrefs.SetString("BuildVariant_PackageTestNet", testNetPackage);
        EditorPrefs.SetString("BuildVariant_IconTestNet",    testNetIconPath);

        EditorPrefs.SetString("BuildVariant_Version", version);

        AssetDatabase.SaveAssets();
        Debug.Log("[BuildSettings] ✅ Settings saved.");
    }

    // ── Icon Helpers ─────────────────────────────────────────────────────────
    private void RefreshIconPreviews()
    {
        mainNetIconPreview = LoadPreview(mainNetIconPath);
        testNetIconPreview = LoadPreview(testNetIconPath);
    }

    private Texture2D LoadPreview(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Opens a file picker to choose a PNG/JPG icon.</summary>
    private string PickIcon(string currentPath, string label)
    {
        string dir   = string.IsNullOrEmpty(currentPath)
                         ? Application.dataPath
                         : Path.GetDirectoryName(Path.GetFullPath(currentPath));
        string picked = EditorUtility.OpenFilePanelWithFilters(
            $"Choose {label} Icon", dir,
            new[] { "Image Files", "png,jpg,jpeg", "All files", "*" });

        if (string.IsNullOrEmpty(picked)) return currentPath; // cancelled

        // Convert absolute path → relative path (Assets/...)
        if (picked.StartsWith(Application.dataPath))
            picked = "Assets" + picked.Substring(Application.dataPath.Length);

        return picked;
    }

    // ── Init Styles ──────────────────────────────────────────────────────────
    private void InitStyles()
    {
        if (stylesInitialized) return;
        stylesInitialized = true;

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 14,
            alignment = TextAnchor.MiddleLeft
        };

        sectionStyle = new GUIStyle("box")
        {
            padding = new RectOffset(12, 12, 10, 10)
        };

        iconBoxStyle = new GUIStyle("box")
        {
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(4, 4, 4, 4)
        };
    }

    // ── GUI ──────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        InitStyles();
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        // ── Title Bar ────────────────────────────────────────────────────────
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Bitcoin Wallet — Build Settings", headerStyle);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(6);

        DrawSeparator();

        // ─────────────────────────────────────────────────────────────────────
        // 🟠  MAINNET SECTION
        // ─────────────────────────────────────────────────────────────────────
        EditorGUILayout.BeginVertical(sectionStyle);

        // Section Header
        DrawSectionHeader("🟠  MainNet Settings", new Color(1f, 0.6f, 0.1f));

        EditorGUILayout.Space(6);

        // Icon Row
        EditorGUILayout.BeginHorizontal();
        {
            // Preview Box
            Rect previewRect = GUILayoutUtility.GetRect(96, 96, GUILayout.Width(96), GUILayout.Height(96));
            if (mainNetIconPreview != null)
                GUI.DrawTexture(previewRect, mainNetIconPreview, ScaleMode.ScaleToFit);
            else
            {
                GUI.Box(previewRect, "No Icon\n(96×96)", iconBoxStyle);
            }

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical();
            {
                EditorGUILayout.LabelField("App Icon", EditorStyles.boldLabel);

                // Object Field — drag & drop a Texture2D
                EditorGUI.BeginChangeCheck();
                var pickedTex = (Texture2D)EditorGUILayout.ObjectField(
                    mainNetIconPreview, typeof(Texture2D), false,
                    GUILayout.Width(200), GUILayout.Height(20));
                if (EditorGUI.EndChangeCheck() && pickedTex != null)
                {
                    mainNetIconPath    = AssetDatabase.GetAssetPath(pickedTex);
                    mainNetIconPreview = pickedTex;
                }

                EditorGUILayout.Space(4);

                // Browse Button
                if (GUILayout.Button("📂 Browse…", GUILayout.Width(200), GUILayout.Height(28)))
                {
                    mainNetIconPath    = PickIcon(mainNetIconPath, "MainNet");
                    mainNetIconPreview = LoadPreview(mainNetIconPath);
                }

                EditorGUILayout.Space(4);

                // Show current path
                EditorGUILayout.LabelField("Path:", EditorStyles.miniLabel);
                EditorGUILayout.SelectableLabel(
                    string.IsNullOrEmpty(mainNetIconPath) ? "(none)" : mainNetIconPath,
                    EditorStyles.miniTextField,
                    GUILayout.Height(18));
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // Name & Package
        mainNetName    = EditorGUILayout.TextField("App Name",     mainNetName);
        mainNetPackage = EditorGUILayout.TextField("Package ID",   mainNetPackage);

        EditorGUILayout.EndVertical(); // end MainNet box

        EditorGUILayout.Space(10);

        // ─────────────────────────────────────────────────────────────────────
        // 🔵  TESTNET SECTION
        // ─────────────────────────────────────────────────────────────────────
        EditorGUILayout.BeginVertical(sectionStyle);

        DrawSectionHeader("🔵  TestNet Settings", new Color(0.2f, 0.6f, 1f));

        EditorGUILayout.Space(6);

        // Icon Row
        EditorGUILayout.BeginHorizontal();
        {
            // Preview Box
            Rect previewRect = GUILayoutUtility.GetRect(96, 96, GUILayout.Width(96), GUILayout.Height(96));
            if (testNetIconPreview != null)
                GUI.DrawTexture(previewRect, testNetIconPreview, ScaleMode.ScaleToFit);
            else
            {
                GUI.Box(previewRect, "No Icon\n(96×96)", iconBoxStyle);
            }

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical();
            {
                EditorGUILayout.LabelField("App Icon", EditorStyles.boldLabel);

                // Object Field
                EditorGUI.BeginChangeCheck();
                var pickedTex = (Texture2D)EditorGUILayout.ObjectField(
                    testNetIconPreview, typeof(Texture2D), false,
                    GUILayout.Width(200), GUILayout.Height(20));
                if (EditorGUI.EndChangeCheck() && pickedTex != null)
                {
                    testNetIconPath    = AssetDatabase.GetAssetPath(pickedTex);
                    testNetIconPreview = pickedTex;
                }

                EditorGUILayout.Space(4);

                // Browse Button
                if (GUILayout.Button("📂 Browse…", GUILayout.Width(200), GUILayout.Height(28)))
                {
                    testNetIconPath    = PickIcon(testNetIconPath, "TestNet");
                    testNetIconPreview = LoadPreview(testNetIconPath);
                }

                EditorGUILayout.Space(4);

                EditorGUILayout.LabelField("Path:", EditorStyles.miniLabel);
                EditorGUILayout.SelectableLabel(
                    string.IsNullOrEmpty(testNetIconPath) ? "(none)" : testNetIconPath,
                    EditorStyles.miniTextField,
                    GUILayout.Height(18));
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        testNetName    = EditorGUILayout.TextField("App Name",   testNetName);
        testNetPackage = EditorGUILayout.TextField("Package ID", testNetPackage);

        EditorGUILayout.EndVertical(); // end TestNet box

        EditorGUILayout.Space(10);

        // ─────────────────────────────────────────────────────────────────────
        // 📝 GENERAL SETTINGS
        // ─────────────────────────────────────────────────────────────────────
        EditorGUILayout.BeginVertical(sectionStyle);
        DrawSectionHeader("📝  General Settings", Color.white);
        EditorGUILayout.Space(4);
        version = EditorGUILayout.TextField("App Version", version);
        
        EditorGUILayout.Space(8);
        
        // Build Numbers Display
        EditorGUILayout.LabelField("Build Numbers", EditorStyles.boldLabel);
        int mainNetBuild = EditorPrefs.GetInt("BuildVariant_BuildNumber_MainNet", 0);
        int testNetBuild = EditorPrefs.GetInt("BuildVariant_BuildNumber_TestNet", 0);
        int globalBuild = EditorPrefs.GetInt("BuildVariant_BuildNumber_Global", 0);
        int bundleCode = (globalBuild * 1000) + Mathf.Max(mainNetBuild, testNetBuild);
        
        EditorGUILayout.LabelField($"🟠 MainNet:  #{mainNetBuild:D3}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"🔵 TestNet:  #{testNetBuild:D3}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"📊 Global:   #{globalBuild:D3}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"📦 Bundle Code: {bundleCode}", EditorStyles.miniLabel);
        
        EditorGUILayout.Space(4);
        if (GUILayout.Button("🔄 Reset Build Numbers", GUILayout.Height(24)))
        {
            if (EditorUtility.DisplayDialog("Reset Build Numbers", 
                "Are you sure you want to reset all build numbers to 0?", "Yes, Reset", "Cancel"))
            {
                EditorPrefs.SetInt("BuildVariant_BuildNumber_MainNet", 0);
                EditorPrefs.SetInt("BuildVariant_BuildNumber_TestNet", 0);
                EditorPrefs.SetInt("BuildVariant_BuildNumber_Global", 0);
                ShowNotification(new GUIContent("✅ Build numbers reset!"));
            }
        }
        
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(14);

        DrawSeparator();
        EditorGUILayout.Space(6);

        // ── Action Buttons ────────────────────────────────────────────────────
        // Save
        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
        if (GUILayout.Button("💾  Save Settings", GUILayout.Height(44)))
        {
            SaveSettings();
            ShowNotification(new GUIContent("✅ Settings Saved!"));
        }
        GUI.backgroundColor = prevBg;

        EditorGUILayout.Space(6);

        EditorGUILayout.BeginHorizontal();
        {
            // Build MainNet
            GUI.backgroundColor = new Color(1f, 0.55f, 0.1f);
            if (GUILayout.Button("🟠  Build MainNet APK", GUILayout.Height(40)))
            {
                SaveSettings();
                string pkg  = EditorPrefs.GetString("BuildVariant_PackageMainNet", "com.coinhouse.wallet");
                string name = EditorPrefs.GetString("BuildVariant_NameMainNet",    "CoinHouse");
                AndroidBuilder.BuildVariant(false, pkg, name);
            }

            // Build TestNet
            GUI.backgroundColor = new Color(0.2f, 0.55f, 1f);
            if (GUILayout.Button("🔵  Build TestNet APK", GUILayout.Height(40)))
            {
                SaveSettings();
                string pkg  = EditorPrefs.GetString("BuildVariant_PackageTestNet", "com.coinhouse.wallet.testnet");
                string name = EditorPrefs.GetString("BuildVariant_NameTestNet",    "CoinHouse Testnet");
                AndroidBuilder.BuildVariant(true, pkg, name);
            }
            GUI.backgroundColor = prevBg;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        // Build Both
        GUI.backgroundColor = new Color(0.7f, 0.3f, 0.9f);
        if (GUILayout.Button("⚡  Build BOTH (MainNet + TestNet)", GUILayout.Height(40)))
        {
            SaveSettings();
            AndroidBuilder.BuildBothAPKs();
        }
        GUI.backgroundColor = prevBg;

        EditorGUILayout.Space(6);

        // Open Player Settings
        if (GUILayout.Button("Open Android Player Settings", GUILayout.Height(30)))
            SettingsService.OpenProjectSettings("Project/Player");

        EditorGUILayout.Space(12);
        DrawSeparator();
        EditorGUILayout.Space(6);

        // 💻 Editor & Windows Network Mode switcher
        EditorGUILayout.LabelField("💻  Editor & Windows Network Mode", EditorStyles.boldLabel);
        bool currentTestnet = IsEditorTestnetActive();
        string currentNetworkStr = currentTestnet ? "🔵 TestNet" : "🟠 MainNet";
        EditorGUILayout.LabelField($"Active Network: {currentNetworkStr}", EditorStyles.miniBoldLabel);
        
        EditorGUILayout.BeginHorizontal();
        {
            GUI.backgroundColor = new Color(1f, 0.55f, 0.1f);
            if (GUILayout.Button("Set Editor to MainNet", GUILayout.Height(32)))
            {
                SwitchEditorNetwork(false);
                ShowNotification(new GUIContent("Editor switched to MainNet!"));
            }
            
            GUI.backgroundColor = new Color(0.2f, 0.55f, 1f);
            if (GUILayout.Button("Set Editor to TestNet", GUILayout.Height(32)))
            {
                SwitchEditorNetwork(true);
                ShowNotification(new GUIContent("Editor switched to TestNet!"));
            }
            GUI.backgroundColor = prevBg;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        EditorGUILayout.EndScrollView();
    }

    private bool IsEditorTestnetActive()
    {
#if UNITY_2023_1_OR_NEWER
        string defines = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Standalone);
#else
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
#endif
        return defines.Contains("TESTNET_BUILD");
    }

    private void SwitchEditorNetwork(bool isTestnet)
    {
#if UNITY_2023_1_OR_NEWER
        SetDefineSymbolForNamedTarget(UnityEditor.Build.NamedBuildTarget.Standalone, "TESTNET_BUILD", isTestnet);
        SetDefineSymbolForNamedTarget(UnityEditor.Build.NamedBuildTarget.Android, "TESTNET_BUILD", isTestnet);
#else
        SetDefineSymbolForGroup(BuildTargetGroup.Standalone, "TESTNET_BUILD", isTestnet);
        SetDefineSymbolForGroup(BuildTargetGroup.Android, "TESTNET_BUILD", isTestnet);
#endif
        AssetDatabase.SaveAssets();
        Debug.Log($"[BuildSettings] Active network switched to: {(isTestnet ? "TestNet" : "MainNet")}");
    }

#if UNITY_2023_1_OR_NEWER
    private void SetDefineSymbolForNamedTarget(UnityEditor.Build.NamedBuildTarget target, string symbol, bool enable)
    {
        string defines = PlayerSettings.GetScriptingDefineSymbols(target);
        var list = new System.Collections.Generic.List<string>(defines.Split(new char[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));

        if (enable) 
        { 
            if (!list.Contains(symbol)) list.Add(symbol); 
        }
        else 
        { 
            list.Remove(symbol); 
        }

        string newDefines = string.Join(";", list);
        PlayerSettings.SetScriptingDefineSymbols(target, newDefines);
    }
#endif

    private void SetDefineSymbolForGroup(BuildTargetGroup group, string symbol, bool enable)
    {
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
        var list = new System.Collections.Generic.List<string>(defines.Split(new char[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));

        if (enable) 
        { 
            if (!list.Contains(symbol)) list.Add(symbol); 
        }
        else 
        { 
            list.Remove(symbol); 
        }

        string newDefines = string.Join(";", list);
        PlayerSettings.SetScriptingDefineSymbolsForGroup(group, newDefines);
    }

    // ── UI Helpers ────────────────────────────────────────────────────────────
    private void DrawSeparator()
    {
        Rect r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(0.35f, 0.35f, 0.35f));
    }

    private void DrawSectionHeader(string label, Color color)
    {
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 13,
            normal    = { textColor = color }
        };
        GUILayout.Label(label, style);
        Rect r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(color.r, color.g, color.b, 0.4f));
    }
}
