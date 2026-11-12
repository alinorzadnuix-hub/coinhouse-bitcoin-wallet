using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// Build Variant Switcher — Tools → Build Variant
/// Switches the project between MainNet and Testnet APK builds.
/// Changes: app icon, scripting define symbol, product name, package name.
/// </summary>
public class BuildVariantSwitcher : EditorWindow
{
    // ─── Defaults ─────────────────────────────────────────────────────────
    private const string DEFAULT_ICON_MAINNET_PATH  = "Assets/Resources/Minnet.png";
    private const string DEFAULT_ICON_TESTNET_PATH  = "Assets/Resources/testnet.png";
    private const string DEFINE_TESTNET             = "TESTNET_BUILD";
    private const string DEFAULT_PRODUCT_MAINNET    = "CoinHouse";
    private const string DEFAULT_PRODUCT_TESTNET    = "CoinHouse Testnet";

    // ─── Default Package Names ─────────────────────────────────────────────
    private const string DEFAULT_PKG_MAINNET = "com.coinhouse.wallet";
    private const string DEFAULT_PKG_TESTNET = "com.coinhouse.wallet.testnet";

    // ─── EditorPrefs Keys (persisted between editor restarts) ─────────────
    private const string PREF_PKG_MAINNET     = "BuildVariant_PackageMainNet";
    private const string PREF_PKG_TESTNET     = "BuildVariant_PackageTestNet";
    private const string PREF_NAME_MAINNET    = "BuildVariant_NameMainNet";
    private const string PREF_NAME_TESTNET    = "BuildVariant_NameTestNet";
    private const string PREF_ICON_MAINNET    = "BuildVariant_IconMainNet";
    private const string PREF_ICON_TESTNET    = "BuildVariant_IconTestNet";

    private static readonly BuildTargetGroup TARGET_GROUP = BuildTargetGroup.Android;

    // ─── Local UI State ────────────────────────────────────────────────────
    private string pkgMainNet;
    private string pkgTestNet;
    private string nameMainNet;
    private string nameTestNet;
    private string version;
    private Texture2D iconMainNet;
    private Texture2D iconTestNet;
    private Vector2 scrollPos;

    // ─── Open Window ──────────────────────────────────────────────────────
    [MenuItem("Tools/Build Variant")]
    public static void ShowWindow()
    {
        var w = GetWindow<BuildVariantSwitcher>("Build Variant");
        w.minSize = new Vector2(460, 700);
        w.maxSize = new Vector2(560, 1100);
    }

    void OnEnable()
    {
        // Load saved package names (or default)
        pkgMainNet  = EditorPrefs.GetString(PREF_PKG_MAINNET,  DEFAULT_PKG_MAINNET);
        pkgTestNet  = EditorPrefs.GetString(PREF_PKG_TESTNET,  DEFAULT_PKG_TESTNET);
        // Load saved product names (or default)
        nameMainNet = EditorPrefs.GetString(PREF_NAME_MAINNET, DEFAULT_PRODUCT_MAINNET);
        nameTestNet = EditorPrefs.GetString(PREF_NAME_TESTNET, DEFAULT_PRODUCT_TESTNET);
        // Load saved version (or default)
        version = EditorPrefs.GetString("BuildVariant_Version", "1.0.0");
        // Load saved icon asset paths (or default)
        string iconMainPath = EditorPrefs.GetString(PREF_ICON_MAINNET, DEFAULT_ICON_MAINNET_PATH);
        string iconTestPath = EditorPrefs.GetString(PREF_ICON_TESTNET, DEFAULT_ICON_TESTNET_PATH);
        if (string.IsNullOrEmpty(iconMainPath) || iconMainPath.Contains("satolite_logo_bw") || iconMainPath.Contains("y.png") || !System.IO.File.Exists(iconMainPath))
        {
            iconMainPath = DEFAULT_ICON_MAINNET_PATH;
            EditorPrefs.SetString(PREF_ICON_MAINNET, iconMainPath);
        }
        if (string.IsNullOrEmpty(iconTestPath) || iconTestPath.Contains("satolite_logo_bw") || iconTestPath.Contains("Bitcointa") || !System.IO.File.Exists(iconTestPath))
        {
            iconTestPath = DEFAULT_ICON_TESTNET_PATH;
            EditorPrefs.SetString(PREF_ICON_TESTNET, iconTestPath);
        }
        iconMainNet = AssetDatabase.LoadAssetAtPath<Texture2D>(iconMainPath) ?? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Minnet.jpg");
        iconTestNet = AssetDatabase.LoadAssetAtPath<Texture2D>(iconTestPath) ?? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/testnet.jpg");
    }

    static string GetIconPath(string pref, string defaultPath)
    {
        return EditorPrefs.GetString(pref, defaultPath);
    }

    // ─── GUI ──────────────────────────────────────────────────────────────
    void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        bool isTestnet = IsTestnetActive();

        // Title
        GUILayout.Space(14);
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 17,
            alignment = TextAnchor.MiddleCenter
        };
        GUILayout.Label("⚙  Build Variant Switcher", titleStyle);
        GUILayout.Space(6);
        DrawSeparator();
        GUILayout.Space(8);

        // Current status badge
        GUIStyle badgeStyle = new GUIStyle(EditorStyles.helpBox)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = 40
        };

        GUI.backgroundColor = isTestnet ? new Color(0.2f, 0.6f, 1f) : new Color(1f, 0.55f, 0f);
        GUILayout.Label(
            isTestnet ? "🔵  TESTNET BUILD  (tb1 addresses)" : "🟠  MAINNET BUILD  (bc1 addresses)",
            badgeStyle
        );
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        // Info labels
        EditorGUILayout.LabelField("App Name:", PlayerSettings.productName, EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Version:  ", version, EditorStyles.miniLabel);
        string currentIconPath = isTestnet
            ? EditorPrefs.GetString(PREF_ICON_TESTNET, DEFAULT_ICON_TESTNET_PATH)
            : EditorPrefs.GetString(PREF_ICON_MAINNET, DEFAULT_ICON_MAINNET_PATH);
        EditorGUILayout.LabelField("Icon:     ", System.IO.Path.GetFileName(currentIconPath), EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Network:  ", isTestnet ? "Bitcoin Testnet3" : "Bitcoin Mainnet", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Package:  ", PlayerSettings.applicationIdentifier, EditorStyles.miniLabel);
        
        // Build Number
        int currentBuild = EditorPrefs.GetInt(isTestnet ? "BuildVariant_BuildNumber_TestNet" : "BuildVariant_BuildNumber_MainNet", 0);
        int globalBuild = EditorPrefs.GetInt("BuildVariant_BuildNumber_Global", 0);
        EditorGUILayout.LabelField($"Build:    ", $"#{currentBuild:D3} (Global: #{globalBuild:D3})", EditorStyles.miniLabel);

        GUILayout.Space(10);
        DrawSeparator();
        GUILayout.Space(8);

        // ─── Package Name Section ──────────────────────────────────────────
        GUIStyle sectionLabel = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        GUILayout.Label("📦  Package Names (Bundle Identifier)", sectionLabel);
        GUILayout.Space(4);

        // Auto-save package names on change
        EditorGUI.BeginChangeCheck();
        GUILayout.BeginHorizontal();
        GUILayout.Label("🟠 MainNet:", GUILayout.Width(80));
        string newPkgMainNet = EditorGUILayout.TextField(pkgMainNet);
        GUILayout.EndHorizontal();
        if (EditorGUI.EndChangeCheck())
        {
            pkgMainNet = newPkgMainNet;
            EditorPrefs.SetString(PREF_PKG_MAINNET, pkgMainNet);
            if (!isTestnet)
            {
#if UNITY_2023_1_OR_NEWER
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, pkgMainNet);
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, pkgMainNet);
#else
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, pkgMainNet);
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, pkgMainNet);
#endif
            }
        }

        EditorGUI.BeginChangeCheck();
        GUILayout.BeginHorizontal();
        GUILayout.Label("🔵 TestNet:", GUILayout.Width(80));
        string newPkgTestNet = EditorGUILayout.TextField(pkgTestNet);
        GUILayout.EndHorizontal();
        if (EditorGUI.EndChangeCheck())
        {
            pkgTestNet = newPkgTestNet;
            EditorPrefs.SetString(PREF_PKG_TESTNET, pkgTestNet);
            if (isTestnet)
            {
#if UNITY_2023_1_OR_NEWER
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, pkgTestNet);
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, pkgTestNet);
#else
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, pkgTestNet);
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, pkgTestNet);
#endif
            }
        }

        GUILayout.Space(4);
        EditorGUILayout.HelpBox("Package names are saved automatically. Must be lowercase like: com.company.appname", MessageType.Info);

        GUILayout.Space(8);
        DrawSeparator();
        GUILayout.Space(8);

        // ─── Product Names Section ─────────────────────────────────────────
        GUILayout.Label("🏷  Product Names (App Display Name)", sectionLabel);
        GUILayout.Space(4);

        // Track changes for auto-save
        EditorGUI.BeginChangeCheck();
        GUILayout.BeginHorizontal();
        GUILayout.Label("🟠 MainNet:", GUILayout.Width(80));
        string newNameMainNet = EditorGUILayout.TextField(nameMainNet);
        GUILayout.EndHorizontal();
        if (EditorGUI.EndChangeCheck())
        {
            nameMainNet = newNameMainNet;
            EditorPrefs.SetString(PREF_NAME_MAINNET, nameMainNet);
            if (!isTestnet) PlayerSettings.productName = nameMainNet;
        }

        EditorGUI.BeginChangeCheck();
        GUILayout.BeginHorizontal();
        GUILayout.Label("🔵 TestNet:", GUILayout.Width(80));
        string newNameTestNet = EditorGUILayout.TextField(nameTestNet);
        GUILayout.EndHorizontal();
        if (EditorGUI.EndChangeCheck())
        {
            nameTestNet = newNameTestNet;
            EditorPrefs.SetString(PREF_NAME_TESTNET, nameTestNet);
            if (isTestnet) PlayerSettings.productName = nameTestNet;
        }

        GUILayout.Space(4);
        EditorGUILayout.HelpBox("Names are saved automatically when you edit them.", MessageType.Info);

        GUILayout.Space(8);
        DrawSeparator();
        GUILayout.Space(8);

        // ─── Version Section ────────────────────────────────────────────────
        GUILayout.Label("🔢  App Version", sectionLabel);
        GUILayout.Space(4);

        EditorGUI.BeginChangeCheck();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Version:", GUILayout.Width(80));
        string newVersion = EditorGUILayout.TextField(version);
        GUILayout.EndHorizontal();
        if (EditorGUI.EndChangeCheck())
        {
            version = newVersion;
            EditorPrefs.SetString("BuildVariant_Version", version);
            PlayerSettings.bundleVersion = version;
        }

        GUILayout.Space(4);
        EditorGUILayout.HelpBox("Format: major.minor.patch (e.g., 1.0.0). Auto-saved.", MessageType.Info);

        GUILayout.Space(8);
        DrawSeparator();
        GUILayout.Space(8);

        // ─── App Icons Section ─────────────────────────────────────────────
        GUILayout.Label("🖼  App Icons (Logo per Variant)", sectionLabel);
        GUILayout.Space(4);

        // MainNet icon picker
        GUILayout.BeginHorizontal();
        GUILayout.Label("🟠 MainNet:", GUILayout.Width(80));
        Texture2D newIconMain = (Texture2D)EditorGUILayout.ObjectField(iconMainNet, typeof(Texture2D), false, GUILayout.Height(64), GUILayout.Width(64));
        if (newIconMain != iconMainNet)
        {
            iconMainNet = newIconMain;
            string p = AssetDatabase.GetAssetPath(iconMainNet);
            if (!string.IsNullOrEmpty(p))
            {
                EditorPrefs.SetString(PREF_ICON_MAINNET, p);
                // Auto-apply if currently on MainNet
                if (!isTestnet)
                {
                    ApplyAppIcon(p);
                    Debug.Log("[BuildVariant] MainNet icon applied immediately: " + p);
                }
            }
        }
        GUILayout.BeginVertical();
        EditorGUILayout.LabelField(iconMainNet != null ? AssetDatabase.GetAssetPath(iconMainNet) : "(none)", EditorStyles.miniLabel);
        if (GUILayout.Button("Apply to MainNet", GUILayout.Width(120)))
        {
            string p = AssetDatabase.GetAssetPath(iconMainNet);
            if (!string.IsNullOrEmpty(p))
            {
                EditorPrefs.SetString(PREF_ICON_MAINNET, p);
                ApplyAppIcon(p);
                Debug.Log("[BuildVariant] MainNet icon applied: " + p);
            }
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        GUILayout.Space(6);

        // TestNet icon picker
        GUILayout.BeginHorizontal();
        GUILayout.Label("🔵 TestNet:", GUILayout.Width(80));
        Texture2D newIconTest = (Texture2D)EditorGUILayout.ObjectField(iconTestNet, typeof(Texture2D), false, GUILayout.Height(64), GUILayout.Width(64));
        if (newIconTest != iconTestNet)
        {
            iconTestNet = newIconTest;
            string p = AssetDatabase.GetAssetPath(iconTestNet);
            if (!string.IsNullOrEmpty(p))
            {
                EditorPrefs.SetString(PREF_ICON_TESTNET, p);
                // Auto-apply if currently on TestNet
                if (isTestnet)
                {
                    ApplyAppIcon(p);
                    Debug.Log("[BuildVariant] TestNet icon applied immediately: " + p);
                }
            }
        }
        GUILayout.BeginVertical();
        EditorGUILayout.LabelField(iconTestNet != null ? AssetDatabase.GetAssetPath(iconTestNet) : "(none)", EditorStyles.miniLabel);
        if (GUILayout.Button("Apply to TestNet", GUILayout.Width(120)))
        {
            string p = AssetDatabase.GetAssetPath(iconTestNet);
            if (!string.IsNullOrEmpty(p))
            {
                EditorPrefs.SetString(PREF_ICON_TESTNET, p);
                ApplyAppIcon(p);
                Debug.Log("[BuildVariant] TestNet icon applied: " + p);
            }
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        EditorGUILayout.HelpBox("Icons are saved automatically. When you select an icon, it's immediately applied if you're on that variant. Use buttons to force-apply.", MessageType.Info);

        GUILayout.Space(8);
        DrawSeparator();
        GUILayout.Space(8);

        // ─── Switch buttons ────────────────────────────────────────────────
        GUILayout.BeginHorizontal();

        GUI.enabled = isTestnet;
        GUI.backgroundColor = new Color(1f, 0.55f, 0f);
        if (GUILayout.Button("🟠  Switch to MAINNET", GUILayout.Height(48)))
        {
            string iconPathMain = AssetDatabase.GetAssetPath(iconMainNet);
            if (string.IsNullOrEmpty(iconPathMain)) iconPathMain = DEFAULT_ICON_MAINNET_PATH;
            if (EditorUtility.DisplayDialog(
                    "Switch to MAINNET",
                    "This will:\n• Remove TESTNET_BUILD symbol\n• Set app icon to: " + iconPathMain + "\n• Set name to \"" + nameMainNet + "\"\n• Set package to: " + pkgMainNet + "\n\nContinue?",
                    "Yes, Switch", "Cancel"))
            {
                SwitchToMainNet(pkgMainNet, nameMainNet, iconPathMain);
            }
        }

        GUILayout.Space(6);

        GUI.enabled = !isTestnet;
        GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
        if (GUILayout.Button("🔵  Switch to TESTNET", GUILayout.Height(48)))
        {
            string iconPathTest = AssetDatabase.GetAssetPath(iconTestNet);
            if (string.IsNullOrEmpty(iconPathTest)) iconPathTest = DEFAULT_ICON_TESTNET_PATH;
            if (EditorUtility.DisplayDialog(
                    "Switch to TESTNET",
                    "This will:\n• Add TESTNET_BUILD define symbol\n• Set app icon to: " + iconPathTest + "\n• Set name to \"" + nameTestNet + "\"\n• Set package to: " + pkgTestNet + "\n\nContinue?",
                    "Yes, Switch", "Cancel"))
            {
                SwitchToTestnet(pkgTestNet, nameTestNet, iconPathTest);
            }
        }

        GUI.enabled = true;
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        GUILayout.Space(8);

        // Build current variant button
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
        if (GUILayout.Button("🔨  Build APK Now  (" + (isTestnet ? "Testnet" : "MainNet") + ")", GUILayout.Height(40)))
        {
            EditorApplication.delayCall += () => AndroidBuilder.BuildAndroidAPK();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(4);

        // Build BOTH variants button
        GUI.backgroundColor = new Color(0.7f, 0.3f, 0.9f);
        if (GUILayout.Button("🚀  Build BOTH Variants  (MainNet + TestNet)", GUILayout.Height(44)))
        {
            EditorApplication.delayCall += () => AndroidBuilder.BuildBothAPKs();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(8);
        DrawSeparator();
        GUILayout.Space(8);

        GUI.backgroundColor = new Color(0.2f, 0.65f, 0.85f);
        if (GUILayout.Button("💻  Build Windows App  (" + (isTestnet ? "Testnet" : "MainNet") + ")", GUILayout.Height(38)))
        {
            EditorApplication.delayCall += () => WindowsBuilder.BuildCurrentWindows();
        }

        GUILayout.Space(4);

        GUI.backgroundColor = new Color(0.4f, 0.5f, 0.85f);
        if (GUILayout.Button("🖥️  Build BOTH Windows Variants  (MainNet + TestNet)", GUILayout.Height(40)))
        {
            EditorApplication.delayCall += () => WindowsBuilder.BuildBothWindowsVariants();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndScrollView();
    }

    // ─── Switch Logic ─────────────────────────────────────────────────────

    static void SwitchToMainNet(string packageName, string productName, string iconPath)
    {
        Debug.Log("[BuildVariant] Switching to MAINNET...");
        SetDefineSymbol(DEFINE_TESTNET, false);
        ApplyAppIcon(iconPath);
        PlayerSettings.productName = productName;
#if UNITY_2023_1_OR_NEWER
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, packageName);
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, packageName);
#else
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, packageName);
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, packageName);
#endif
        // Apply version from EditorPrefs
        string version = EditorPrefs.GetString("BuildVariant_Version", "1.0.0");
        PlayerSettings.bundleVersion = version;
        EditorPrefs.SetString(PREF_PKG_MAINNET,  packageName);
        EditorPrefs.SetString(PREF_NAME_MAINNET, productName);
        EditorPrefs.SetString(PREF_ICON_MAINNET, iconPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[BuildVariant] ✅ Switched to MAINNET. Package: " + packageName + " | Name: " + productName + " | Version: " + version + " | Icon: " + iconPath);
        EditorUtility.DisplayDialog("Done", "Switched to MAINNET build.\n\nApp name: " + productName + "\nVersion: " + version + "\nPackage: " + packageName + "\nIcon: " + iconPath, "OK");
    }

    static void SwitchToTestnet(string packageName, string productName, string iconPath)
    {
        Debug.Log("[BuildVariant] Switching to TESTNET...");
        SetDefineSymbol(DEFINE_TESTNET, true);
        ApplyAppIcon(iconPath);
        PlayerSettings.productName = productName;
#if UNITY_2023_1_OR_NEWER
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, packageName);
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, packageName);
#else
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, packageName);
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, packageName);
#endif
        // Apply version from EditorPrefs
        string version = EditorPrefs.GetString("BuildVariant_Version", "1.0.0");
        PlayerSettings.bundleVersion = version;
        EditorPrefs.SetString(PREF_PKG_TESTNET,  packageName);
        EditorPrefs.SetString(PREF_NAME_TESTNET, productName);
        EditorPrefs.SetString(PREF_ICON_TESTNET, iconPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[BuildVariant] ✅ Switched to TESTNET. Package: " + packageName + " | Name: " + productName + " | Version: " + version + " | Icon: " + iconPath);
        EditorUtility.DisplayDialog("Done", "Switched to TESTNET build.\n\nApp name: " + productName + "\nVersion: " + version + "\nPackage: " + packageName + "\nIcon: " + iconPath, "OK");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    static bool IsTestnetActive()
    {
#if UNITY_2023_1_OR_NEWER
        var namedBuildTarget = UnityEditor.Build.NamedBuildTarget.Android;
        string defines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
#else
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(TARGET_GROUP);
#endif
        return defines.Contains(DEFINE_TESTNET);
    }

    static void SetDefineSymbol(string symbol, bool enable)
    {
#if UNITY_2023_1_OR_NEWER
        var targets = new[] { UnityEditor.Build.NamedBuildTarget.Android, UnityEditor.Build.NamedBuildTarget.Standalone };
        foreach (var target in targets)
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);
            var list = new System.Collections.Generic.List<string>(
                defines.Split(new char[]{';'}, System.StringSplitOptions.RemoveEmptyEntries)
            );
            if (enable) { if (!list.Contains(symbol)) list.Add(symbol); }
            else { list.Remove(symbol); }
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
        }
#else
        var groups = new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone };
        foreach (var group in groups)
        {
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            var list = new System.Collections.Generic.List<string>(
                defines.Split(new char[]{';'}, System.StringSplitOptions.RemoveEmptyEntries)
            );
            if (enable) { if (!list.Contains(symbol)) list.Add(symbol); }
            else { list.Remove(symbol); }
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", list));
        }
#endif
        Debug.Log($"[BuildVariant] Define symbol {symbol} set to {enable} for Android and Standalone.");
    }

    static void ApplyAppIcon(string assetPath)
    {
        // Configure texture settings BEFORE loading (prevents blurry icons on high-DPI phones)
        ConfigureTextureForIcon(assetPath);
        
        Texture2D sourceIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (sourceIcon == null)
        {
            Debug.LogError($"[BuildVariant] Icon not found at: {assetPath}");
            return;
        }

        // Set DEFAULT icons (fallback)
        int[] defaultSizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Unknown);
        if (defaultSizes != null && defaultSizes.Length > 0)
        {
            Texture2D[] defaultIcons = new Texture2D[defaultSizes.Length];
            for (int i = 0; i < defaultSizes.Length; i++)
                defaultIcons[i] = ScaleTexture(sourceIcon, defaultSizes[i], defaultSizes[i]);
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, defaultIcons);
        }

        // Set ANDROID icons
        int[] sizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Android);
        if (sizes != null && sizes.Length > 0)
        {
            Texture2D[] icons = new Texture2D[sizes.Length];
            for (int i = 0; i < sizes.Length; i++)
                icons[i] = ScaleTexture(sourceIcon, sizes[i], sizes[i]);
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, icons);
        }

        // Set STANDALONE (Windows) icons
        int[] standaloneSizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone);
        if (standaloneSizes != null && standaloneSizes.Length > 0)
        {
            Texture2D[] standaloneIcons = new Texture2D[standaloneSizes.Length];
            for (int i = 0; i < standaloneSizes.Length; i++)
                standaloneIcons[i] = ScaleTexture(sourceIcon, standaloneSizes[i], standaloneSizes[i]);
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone, standaloneIcons);
        }

#if UNITY_2023_1_OR_NEWER
        var targets = new[] { UnityEditor.Build.NamedBuildTarget.Android, UnityEditor.Build.NamedBuildTarget.Standalone };
        foreach (var target in targets)
        {
            int[] namedSizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
            if (namedSizes != null && namedSizes.Length > 0)
            {
                Texture2D[] namedIcons = new Texture2D[namedSizes.Length];
                for (int i = 0; i < namedSizes.Length; i++)
                    namedIcons[i] = ScaleTexture(sourceIcon, namedSizes[i], namedSizes[i]);
                PlayerSettings.SetIcons(target, namedIcons, IconKind.Application);
            }
        }
#endif

        // Set Android adaptive/round/legacy icons
        try
        {
            Color bgColor = Color.black;
            if (sourceIcon != null && sourceIcon.name.ToLower().Contains("testnet"))
                bgColor = new Color(50f / 255f, 118f / 255f, 202f / 255f);
            else if (sourceIcon != null && (sourceIcon.name.ToLower().Contains("minnet") || sourceIcon.name.ToLower().Contains("mainnet") || sourceIcon.name.Contains("satolite_logo_bw")))
                bgColor = Color.black;
            Texture2D bgTexture = CreateSolidColorTexture(108, 108, bgColor);

            var adaptiveKind = UnityEditor.Android.AndroidPlatformIconKind.Adaptive;
#if UNITY_2023_1_OR_NEWER
            var androidTarget = UnityEditor.Build.NamedBuildTarget.Android;
            var adaptiveIcons = PlayerSettings.GetPlatformIcons(androidTarget, adaptiveKind);
#else
            var adaptiveIcons = PlayerSettings.GetPlatformIcons(TARGET_GROUP, adaptiveKind);
#endif
            if (adaptiveIcons != null && adaptiveIcons.Length > 0)
            {
                for (int i = 0; i < adaptiveIcons.Length; i++)
                {
                    int layerCount = adaptiveIcons[i].GetTextures().Length;
                    Texture2D[] layers = new Texture2D[layerCount];
                    for (int l = 0; l < layerCount; l++)
                    {
                        if (l == 0 && layerCount > 1)
                            layers[l] = bgTexture; // background matches icon palette
                        else
                            layers[l] = sourceIcon; // foreground = icon
                    }
                    adaptiveIcons[i].SetTextures(layers);
                }
#if UNITY_2023_1_OR_NEWER
                PlayerSettings.SetPlatformIcons(androidTarget, adaptiveKind, adaptiveIcons);
#else
                PlayerSettings.SetPlatformIcons(TARGET_GROUP, adaptiveKind, adaptiveIcons);
#endif
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BuildVariant] Adaptive icon warning: {e.Message}");
        }

        // ✅ Copy icon to Plugins/Android/NotifIcon.androidlib/res/drawable for NotificationManager
        try
        {
            // Remove obsolete res folder and meta if it exists
            string obsoleteResPath = Path.Combine(Application.dataPath, "Plugins/Android/res");
            if (Directory.Exists(obsoleteResPath)) Directory.Delete(obsoleteResPath, true);
            string obsoleteResMeta = obsoleteResPath + ".meta";
            if (File.Exists(obsoleteResMeta)) File.Delete(obsoleteResMeta);
            AssetDatabase.DeleteAsset("Assets/Plugins/Android/res");

            string libPath = Path.Combine(Application.dataPath, "Plugins/Android/NotifIcon.androidlib");
            string drawablePath = Path.Combine(libPath, "res/drawable");
            if (!Directory.Exists(drawablePath)) Directory.CreateDirectory(drawablePath);
            
            string destPath = Path.Combine(drawablePath, "notif_icon.png");
            byte[] pngBytes = null;
            if (sourceIcon != null)
            {
                Texture2D notifTex = ScaleTexture(sourceIcon, 192, 192);
                if (notifTex != null)
                {
                    pngBytes = notifTex.EncodeToPNG();
                    if (notifTex != sourceIcon)
                        DestroyImmediate(notifTex);
                }
            }

            if (pngBytes != null && pngBytes.Length > 0)
            {
                System.IO.File.WriteAllBytes(destPath, pngBytes);
            }
            else if (System.IO.File.Exists(assetPath) && assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            {
                System.IO.File.Copy(assetPath, destPath, true);
            }

            // Create project.properties
            System.IO.File.WriteAllText(Path.Combine(libPath, "project.properties"), "target=android-30\nandroid.library=true\n");
            
            // Create AndroidManifest.xml
            System.IO.File.WriteAllText(Path.Combine(libPath, "AndroidManifest.xml"), 
@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android""
    package=""com.coinhouse.notificon"">
</manifest>");

            Debug.Log($"[BuildVariant] Copied notification icon to androidlib: {destPath}");
            AssetDatabase.Refresh();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BuildVariant] Failed to setup notification androidlib: {e.Message}");
        }

        Debug.Log($"[BuildVariant] ✅ All icons applied: {assetPath}");
    }

    /// <summary>
    /// Scales a texture to the specified dimensions using high-quality bilinear filtering.
    /// Preserves aspect ratio by centering the source on a transparent canvas to prevent
    /// distortion in Start Menu and Taskbar icons on Windows.
    /// </summary>
    static Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        if (source == null) return null;
        if (source.width == targetWidth && source.height == targetHeight)
            return source;

        try
        {
            // Step 1: If source is not square, create a square canvas with the image centered
            Texture2D squareSource = source;
            if (source.width != source.height)
            {
                int maxDim = Mathf.Max(source.width, source.height);
                RenderTexture squareRT = RenderTexture.GetTemporary(maxDim, maxDim, 0, RenderTextureFormat.ARGB32);
                squareRT.filterMode = FilterMode.Bilinear;
                
                RenderTexture prevRT = RenderTexture.active;
                RenderTexture.active = squareRT;
                GL.Clear(true, true, Color.clear); // Transparent background
                
                // Calculate centered position
                float scaleX = (float)source.width / maxDim;
                float scaleY = (float)source.height / maxDim;
                float offsetX = (1f - scaleX) * 0.5f;
                float offsetY = (1f - scaleY) * 0.5f;
                
                Graphics.DrawTexture(new Rect(offsetX * maxDim, offsetY * maxDim, source.width, source.height), source);
                
                squareSource = new Texture2D(maxDim, maxDim, TextureFormat.ARGB32, false);
                squareSource.ReadPixels(new Rect(0, 0, maxDim, maxDim), 0, 0);
                squareSource.Apply();
                
                RenderTexture.active = prevRT;
                RenderTexture.ReleaseTemporary(squareRT);
            }

            // Step 2: Scale to target dimensions
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            Graphics.Blit(squareSource, rt);
            
            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.ARGB32, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();
            
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            
            return result;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BuildVariant] ScaleTexture failed for {targetWidth}x{targetHeight}: {e.Message}. Using source.");
            return source;
        }
    }

    /// <summary>
    /// Configures TextureImporter settings for optimal icon quality.
    /// Must be called BEFORE loading the texture to take effect.
    /// Prevents blurry icons on high-DPI phones by ensuring maxTextureSize >= 1024.
    /// </summary>
    static void ConfigureTextureForIcon(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;
        
        bool needsReimport = false;
        
        if (!importer.isReadable) { importer.isReadable = true; needsReimport = true; }
        if (importer.maxTextureSize < 1024) { importer.maxTextureSize = 1024; needsReimport = true; }
        if (importer.mipmapEnabled) { importer.mipmapEnabled = false; needsReimport = true; }
        if (!importer.sRGBTexture) { importer.sRGBTexture = true; needsReimport = true; }
        if (importer.filterMode != FilterMode.Bilinear) { importer.filterMode = FilterMode.Bilinear; needsReimport = true; }
        
        if (needsReimport)
        {
            importer.SaveAndReimport();
            Debug.Log($"[BuildVariant] TextureImporter configured for: {assetPath}");
        }
    }

    /// <summary>
    /// Creates a solid color texture for use as adaptive icon background.
    /// </summary>
    static Texture2D CreateSolidColorTexture(int width, int height, Color color)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    void DrawSeparator()
    {
        var rect = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(rect, new Color(0.4f, 0.4f, 0.4f));
    }
}
