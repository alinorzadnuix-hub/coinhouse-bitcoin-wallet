using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Windows Standalone Builder for CoinHouse Wallet (MainNet & TestNet)
/// Supports both interactive Editor builds and headless batchmode builds.
/// </summary>
public class WindowsBuilder
{
    private const string MAINNET_NAME = "CoinHouse";
    private const string TESTNET_NAME = "CoinHouse Testnet";
    private const string MAINNET_ICON = "Assets/Resources/y.png";
    private const string TESTNET_ICON = "Assets/Resources/Bitcointa.png";
    private const string DEFINE_TESTNET = "TESTNET_BUILD";

    [MenuItem("Bitcoin Wallet/Build Windows (Current Variant)")]
    public static void BuildCurrentWindows()
    {
        bool isTestnet = IsTestnetActive();
        string name = isTestnet ? TESTNET_NAME : MAINNET_NAME;
        if (!EditorUtility.DisplayDialog("Build Windows App",
            $"Confirm building Windows 64-bit {(isTestnet ? "TestNet" : "MainNet")} application:\n\n" +
            $"Name: {name}\n" +
            $"Version: {EditorPrefs.GetString("BuildVariant_Version", "1.0.0")}",
            "Build", "Cancel"))
            return;

        BuildWindowsVariant(isTestnet, name, showDialog: true);
    }

    [MenuItem("Bitcoin Wallet/Build Windows (MainNet)")]
    public static void BuildMainNetWindows()
    {
        BuildWindowsVariant(false, MAINNET_NAME, showDialog: true);
    }

    [MenuItem("Bitcoin Wallet/Build Windows (TestNet)")]
    public static void BuildTestNetWindows()
    {
        BuildWindowsVariant(true, TESTNET_NAME, showDialog: true);
    }

    [MenuItem("Bitcoin Wallet/Build Both Windows Variants (MainNet + TestNet)")]
    public static void BuildBothWindowsVariants()
    {
        BuildBothWindows(showDialog: true);
    }

    /// <summary>
    /// Headless entry point for command-line batch builds:
    /// Unity.exe -batchmode -quit -projectPath "..." -executeMethod WindowsBuilder.BuildBothVariantsBatch
    /// </summary>
    public static void BuildBothVariantsBatch()
    {
        BuildBothWindows(showDialog: false);
    }

    public static void BuildMainNetBatch()
    {
        BuildWindowsVariant(false, MAINNET_NAME, showDialog: false);
    }

    public static void BuildTestNetBatch()
    {
        BuildWindowsVariant(true, TESTNET_NAME, showDialog: false);
    }

    /// <summary>
    /// Builds both MainNet and TestNet Windows binaries sequentially.
    /// Restores the original active variant upon completion.
    /// </summary>
    public static void BuildBothWindows(bool showDialog = false)
    {
        bool originalIsTestnet = IsTestnetActive();

        if (showDialog && !Application.isBatchMode)
        {
            if (!EditorUtility.DisplayDialog(
                "Build Both Windows Variants",
                $"This will build two Windows 64-bit applications:\n\n" +
                $"🟠 MainNet: {MAINNET_NAME}\n" +
                $"🔵 TestNet: {TESTNET_NAME}\n\n" +
                "Output directory: Builds/Windows/",
                "Start Building", "Cancel"))
                return;
        }

        bool mainNetOk = false;
        bool testNetOk = false;

        try
        {
            if (showDialog && !Application.isBatchMode)
                EditorUtility.DisplayProgressBar("Building Windows", "🟠 Building MainNet...", 0.1f);
            
            string mainNetExe = BuildWindowsVariant(false, MAINNET_NAME, showDialog: false);
            mainNetOk = !string.IsNullOrEmpty(mainNetExe);

            if (showDialog && !Application.isBatchMode)
                EditorUtility.DisplayProgressBar("Building Windows", "🔵 Building TestNet...", 0.6f);

            string testNetExe = BuildWindowsVariant(true, TESTNET_NAME, showDialog: false);
            testNetOk = !string.IsNullOrEmpty(testNetExe);

            // Restore original network state
            ApplyWindowsVariantSettings(originalIsTestnet, originalIsTestnet ? TESTNET_NAME : MAINNET_NAME);
        }
        finally
        {
            if (showDialog && !Application.isBatchMode)
                EditorUtility.ClearProgressBar();
        }

        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/Windows"));
        Debug.Log($"[WindowsBuilder] Build Both finished. MainNet: {mainNetOk}, TestNet: {testNetOk}. Output: {outDir}");

        if (showDialog && !Application.isBatchMode)
        {
            EditorUtility.RevealInFinder(outDir);
            string msg = (mainNetOk ? "✅ MainNet Windows — Success\n" : "❌ MainNet Windows — Failed\n")
                       + (testNetOk ? "✅ TestNet Windows — Success\n" : "❌ TestNet Windows — Failed\n")
                       + $"\nFolder: {outDir}";
            EditorUtility.DisplayDialog("Windows Build Complete", msg, "OK");
        }
    }

    /// <summary>
    /// Builds a single variant for Windows Standalone 64-bit.
    /// </summary>
    public static string BuildWindowsVariant(bool isTestnet, string productName, bool showDialog = false)
    {
        // 1. Collect enabled scenes
        List<string> scenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled) scenes.Add(scene.path);
        }

        if (scenes.Count == 0)
        {
            Debug.LogError("[WindowsBuilder] Error: No enabled scenes found in Build Settings!");
            return null;
        }

        // 2. Prepare output path
        string variantFolder = isTestnet ? "TestNet" : "MainNet";
        string exeName = isTestnet ? "CoinHouse_Testnet.exe" : "CoinHouse.exe";
        string outputDir = Path.GetFullPath(Path.Combine(Application.dataPath, $"../Builds/Windows/{variantFolder}"));
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        string exePath = Path.Combine(outputDir, exeName);

        // 3. Apply variant settings (Defines, Icons, ProductName)
        ApplyWindowsVariantSettings(isTestnet, productName);

        // 4. Configure build options
        BuildPlayerOptions opts = new BuildPlayerOptions();
        opts.scenes = scenes.ToArray();
        opts.locationPathName = exePath;
        opts.target = BuildTarget.StandaloneWindows64;
        opts.targetGroup = BuildTargetGroup.Standalone;
        opts.options = BuildOptions.None;

        Debug.Log($"[WindowsBuilder] Starting build for {(isTestnet ? "TestNet" : "MainNet")} → {exePath}");
        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[WindowsBuilder] ✅ Build succeeded: {summary.totalSize} bytes at {exePath}");
            if (showDialog && !Application.isBatchMode)
            {
                EditorUtility.RevealInFinder(exePath);
                EditorUtility.DisplayDialog("Build Success", $"✅ Windows build created:\n\n{exePath}", "OK");
            }
            return exePath;
        }
        else
        {
            Debug.LogError($"[WindowsBuilder] ❌ Build FAILED with {summary.totalErrors} errors.");
            return null;
        }
    }

    private static void ApplyWindowsVariantSettings(bool isTestnet, string productName)
    {
        PlayerSettings.productName = productName;
        PlayerSettings.companyName = "CoinHouse";

        string version = EditorPrefs.GetString("BuildVariant_Version", "1.0.0");
        PlayerSettings.bundleVersion = version;

        // Set defines for Standalone
        SetDefineSymbol(DEFINE_TESTNET, isTestnet);

        // Set Icon
        string iconPath = isTestnet ? TESTNET_ICON : MAINNET_ICON;
        if (!File.Exists(iconPath))
        {
            iconPath = isTestnet ? "Assets/Resources/testnet.png" : "Assets/Resources/Minnet.png";
        }

        Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
        if (icon != null)
        {
            int[] sizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone);
            if (sizes != null && sizes.Length > 0)
            {
                Texture2D[] icons = new Texture2D[sizes.Length];
                for (int i = 0; i < sizes.Length; i++)
                    icons[i] = ScaleTexture(icon, sizes[i], sizes[i]);
                PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone, icons);
            }

#if UNITY_2023_1_OR_NEWER
            var target = UnityEditor.Build.NamedBuildTarget.Standalone;
            int[] namedSizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
            if (namedSizes != null && namedSizes.Length > 0)
            {
                Texture2D[] namedIcons = new Texture2D[namedSizes.Length];
                for (int i = 0; i < namedSizes.Length; i++)
                    namedIcons[i] = ScaleTexture(icon, namedSizes[i], namedSizes[i]);
                PlayerSettings.SetIcons(target, namedIcons, IconKind.Application);
            }
#endif
        }

        AssetDatabase.SaveAssets();
    }

    private static bool IsTestnetActive()
    {
#if UNITY_2023_1_OR_NEWER
        string defines = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Standalone);
#else
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
#endif
        return defines.Contains(DEFINE_TESTNET);
    }

    private static void SetDefineSymbol(string symbol, bool enable)
    {
#if UNITY_2023_1_OR_NEWER
        var targets = new[] { UnityEditor.Build.NamedBuildTarget.Standalone, UnityEditor.Build.NamedBuildTarget.Android };
        foreach (var target in targets)
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);
            var list = new List<string>(defines.Split(new char[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
            if (enable) { if (!list.Contains(symbol)) list.Add(symbol); }
            else { list.Remove(symbol); }
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
        }
#else
        var groups = new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android };
        foreach (var group in groups)
        {
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            var list = new List<string>(defines.Split(new char[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
            if (enable) { if (!list.Contains(symbol)) list.Add(symbol); }
            else { list.Remove(symbol); }
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", list));
        }
#endif
    }

    private static Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        if (source == null) return null;
        if (source.width == targetWidth && source.height == targetHeight) return source;

        try
        {
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.black);
            Graphics.Blit(source, rt);
            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }
        catch
        {
            return source;
        }
    }
}
