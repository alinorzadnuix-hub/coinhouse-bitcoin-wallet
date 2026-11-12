using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Android APK Builder - Clean and reliable version
/// </summary>
public class AndroidBuilder
{
    private static readonly BuildTargetGroup TARGET_GROUP = BuildTargetGroup.Android;

    [MenuItem("Bitcoin Wallet/Build Android APK (Current Variant)")]
    public static void BuildAndroidAPK()
    {
        bool isTestnet = IsTestnetActive();
        
        // Get latest values from BuildVariantSwitcher window if it's open
        string name = GetCurrentProductName(isTestnet);
        string pkg = GetCurrentPackageName(isTestnet);
        
        // Confirm build
        if (!EditorUtility.DisplayDialog("Build APK", 
            $"Confirm building {(isTestnet ? "TestNet" : "MainNet")} APK:\n\n" +
            $"Name: {name}\n" +
            $"Package: {pkg}\n" +
            $"Version: {EditorPrefs.GetString("BuildVariant_Version", "1.0.0")}", 
            "Build", "Cancel"))
            return;

        BuildVariant(isTestnet, pkg, name);
    }
    
    /// <summary>
    /// Gets the current product name from EditorPrefs (saved when Apply is clicked in BuildVariantSwitcher)
    /// </summary>
    static string GetCurrentProductName(bool isTestnet)
    {
        return isTestnet
            ? EditorPrefs.GetString("BuildVariant_NameTestNet", "CoinHouse Testnet")
            : EditorPrefs.GetString("BuildVariant_NameMainNet", "CoinHouse");
    }
    
    /// <summary>
    /// Gets the current package name from EditorPrefs
    /// </summary>
    static string GetCurrentPackageName(bool isTestnet)
    {
        return isTestnet
            ? EditorPrefs.GetString("BuildVariant_PackageTestNet", "com.coinhouse.wallet.testnet")
            : EditorPrefs.GetString("BuildVariant_PackageMainNet", "com.coinhouse.wallet");
    }

    [MenuItem("Bitcoin Wallet/Open Build Settings")]
    public static void OpenBuildSettings()
    {
        BuildSettingsWindow.ShowWindow();
    }

    [MenuItem("Bitcoin Wallet/Build Both Variants (MainNet + TestNet)")]
    public static void BuildBothVariants()
    {
        BuildBothAPKs();
    }

    /// <summary>
    /// Builds both MainNet and TestNet APKs sequentially.
    /// Restores the original variant after both builds are done.
    /// </summary>
    public static void BuildBothAPKs()
    {
        bool originalIsTestnet = IsTestnetActive();
        
        // Read all names and packages from EditorPrefs
        string pkgMainNet = GetCurrentPackageName(false);
        string pkgTestNet = GetCurrentPackageName(true);
        string nameMainNet = GetCurrentProductName(false);
        string nameTestNet = GetCurrentProductName(true);

        if (!EditorUtility.DisplayDialog(
            "Build Both Variants",
            $"This will build two APKs:\n\n🟠 MainNet: {nameMainNet}\n    ({pkgMainNet})\n🔵 TestNet: {nameTestNet}\n    ({pkgTestNet})\n\nThis may take several minutes.",
            "Start Building", "Cancel"))
            return;

        bool mainNetOk = false;
        bool testNetOk = false;

        try
        {
            // --- 1. Build MAINNET ---
            EditorUtility.DisplayProgressBar("Building APKs", $"🟠 Building MainNet ({nameMainNet})...", 0.05f);
            string mainNetPath = BuildVariant(false, pkgMainNet, nameMainNet, showDialog: false);
            mainNetOk = !string.IsNullOrEmpty(mainNetPath);

            // --- 2. Build TESTNET ---
            EditorUtility.DisplayProgressBar("Building APKs", $"🔵 Building TestNet ({nameTestNet})...", 0.55f);
            string testNetPath = BuildVariant(true, pkgTestNet, nameTestNet, showDialog: false);
            testNetOk = !string.IsNullOrEmpty(testNetPath);

            // --- 3. Restore original variant ---
            EditorUtility.DisplayProgressBar("Building APKs", "Restoring original settings...", 0.95f);
            string originalName = originalIsTestnet ? nameTestNet : nameMainNet;
            string originalPkg = originalIsTestnet ? pkgTestNet : pkgMainNet;
            ApplyVariantSettings(originalIsTestnet, originalPkg, originalName);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // Open the output folder
        string buildPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/Android"));
        EditorUtility.RevealInFinder(buildPath);

        string resultMsg = "";
        resultMsg += mainNetOk ? "✅ MainNet APK — Built Successfully\n" : "❌ MainNet APK — Failed\n";
        resultMsg += testNetOk ? "✅ TestNet APK — Built Successfully\n" : "❌ TestNet APK — Failed\n";
        resultMsg += $"\nOutput folder:\n{buildPath}";

        EditorUtility.DisplayDialog("Build Both — Finished", resultMsg, "OK");
    }

    // ─── Core Build Method ────────────────────────────────────────────────

    /// <summary>Builds the APK. Returns output path on success, null on failure.</summary>
    public static string BuildVariant(bool isTestnet, string packageName, string productName, bool showDialog = true)
    {
        // 1. Scene List
        List<string> scenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            if (scene.enabled) scenes.Add(scene.path);

        if (scenes.Count == 0)
        {
            Debug.LogError("Error: No scenes are enabled in Build Settings!");
            EditorUtility.DisplayDialog("Build Error", "No scenes are enabled in Build Settings.", "OK");
            return null;
        }

        // 2. Output path with timestamp
        string buildPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/Android"));
        if (!Directory.Exists(buildPath)) Directory.CreateDirectory(buildPath);

        string variantName = isTestnet ? "TestNet" : "MainNet";
        string version = EditorPrefs.GetString("BuildVariant_Version", "1.0.0");
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string apkName = $"CoinHouse_{variantName}_v{version}_{timestamp}.apk";
        string location = Path.Combine(buildPath, apkName);

        // 3. Apply settings
        ApplyVariantSettings(isTestnet, packageName, productName);

        // 4. Build
        BuildPlayerOptions opts = new BuildPlayerOptions();
        opts.scenes = scenes.ToArray();
        opts.locationPathName = location;
        opts.target = BuildTarget.Android;
        opts.options = BuildOptions.None;

        Debug.Log($"[Builder] Building {variantName} APK → {location}");
        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[Builder] ✅ {variantName} build succeeded: {summary.totalSize} bytes");
            if (showDialog)
            {
                EditorUtility.RevealInFinder(location);
                EditorUtility.DisplayDialog("Build Success",
                    $"✅ {variantName} APK created!\n\nLocation:\n{location}", "OK");
            }
            return location;
        }
        else
        {
            Debug.LogError($"[Builder] ❌ {variantName} build FAILED.");
            if (showDialog)
                EditorUtility.DisplayDialog("Build Failed",
                    $"❌ {variantName} build failed.\nCheck the Console for details.", "OK");
            return null;
        }
    }

    // ─── Apply Settings ───────────────────────────────────────────────────

    static void ApplyVariantSettings(bool isTestnet, string packageName, string productName)
    {
        // Product name and version
        PlayerSettings.productName = productName;
        string version = EditorPrefs.GetString("BuildVariant_Version", "1.0.0");
        PlayerSettings.bundleVersion = version;

        // Package name
#if UNITY_2023_1_OR_NEWER
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, packageName);
#else
        PlayerSettings.SetApplicationIdentifier(TARGET_GROUP, packageName);
#endif

        // Define symbol
        SetDefineSymbol("TESTNET_BUILD", isTestnet);

        // App icon (read user-selected icon path from BuildVariantSwitcher)
        string iconPath = isTestnet
            ? EditorPrefs.GetString("BuildVariant_IconTestNet", "Assets/Resources/testnet.png")
            : EditorPrefs.GetString("BuildVariant_IconMainNet", "Assets/Resources/Minnet.png");
        if (string.IsNullOrEmpty(iconPath) || iconPath.Contains("satolite_logo_bw") || iconPath.Contains("Bitcointa") || iconPath.Contains("y.png") || !File.Exists(iconPath))
        {
            iconPath = isTestnet ? "Assets/Resources/testnet.png" : "Assets/Resources/Minnet.png";
        }

        Debug.Log($"[Builder] Loading icon for {(isTestnet ? "TestNet" : "MainNet")}: {iconPath}");

        // Configure texture importer settings BEFORE loading
        ConfigureTextureForIcon(iconPath);

        Texture2D sourceIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
        if (sourceIcon != null)
        {
            // Set DEFAULT icons (fallback for all platforms)
            int[] defaultSizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Unknown);
            if (defaultSizes != null && defaultSizes.Length > 0)
            {
                Texture2D[] defaultIcons = new Texture2D[defaultSizes.Length];
                for (int i = 0; i < defaultSizes.Length; i++)
                    defaultIcons[i] = ScaleTexture(sourceIcon, defaultSizes[i], defaultSizes[i]);
                PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, defaultIcons);
                Debug.Log($"[Builder] Default icons set: {defaultSizes.Length} sizes");
            }

            // Set ANDROID-specific icons
            int[] sizes = PlayerSettings.GetIconSizesForTargetGroup(TARGET_GROUP);
            if (sizes != null && sizes.Length > 0)
            {
                Texture2D[] icons = new Texture2D[sizes.Length];
                for (int i = 0; i < sizes.Length; i++)
                    icons[i] = ScaleTexture(sourceIcon, sizes[i], sizes[i]);
                PlayerSettings.SetIconsForTargetGroup(TARGET_GROUP, icons);
                Debug.Log($"[Builder] Android icons set: {sizes.Length} sizes");
            }

#if UNITY_2023_1_OR_NEWER
            // Also set via NamedBuildTarget for newer Unity
            var namedTarget = UnityEditor.Build.NamedBuildTarget.Android;
            int[] namedSizes = PlayerSettings.GetIconSizes(namedTarget, IconKind.Application);
            if (namedSizes != null && namedSizes.Length > 0)
            {
                Texture2D[] namedIcons = new Texture2D[namedSizes.Length];
                for (int i = 0; i < namedSizes.Length; i++)
                    namedIcons[i] = ScaleTexture(sourceIcon, namedSizes[i], namedSizes[i]);
                PlayerSettings.SetIcons(namedTarget, namedIcons, IconKind.Application);
            }
#endif

            // Set Android adaptive icon (foreground + background) - this is what modern Android uses
            SetAdaptiveIcon(sourceIcon);
            
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
                            Object.DestroyImmediate(notifTex);
                    }
                }

                if (pngBytes != null && pngBytes.Length > 0)
                {
                    File.WriteAllBytes(destPath, pngBytes);
                }
                else if (File.Exists(iconPath) && iconPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(iconPath, destPath, true);
                }

                // Create project.properties
                File.WriteAllText(Path.Combine(libPath, "project.properties"), "target=android-30\nandroid.library=true\n");
                
                // Create AndroidManifest.xml
                File.WriteAllText(Path.Combine(libPath, "AndroidManifest.xml"), 
@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android""
    package=""com.coinhouse.notificon"">
</manifest>");

                Debug.Log($"[Builder] Copied notification icon to androidlib: {destPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Builder] Failed to setup notification androidlib: {e.Message}");
            }
            
            Debug.Log($"[Builder] ✅ All icons applied: {iconPath}");
        }
        else
        {
            Debug.LogError($"[Builder] ❌ Icon NOT found at: {iconPath}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Builder] Applied settings — Variant: {(isTestnet ? "TestNet" : "MainNet")} | Package: {packageName}");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    static bool IsTestnetActive()
    {
#if UNITY_2023_1_OR_NEWER
        string defines = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Android);
#else
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(TARGET_GROUP);
#endif
        return defines.Contains("TESTNET_BUILD");
    }

    static void SetDefineSymbol(string symbol, bool enable)
    {
#if UNITY_2023_1_OR_NEWER
        var targets = new[] { UnityEditor.Build.NamedBuildTarget.Android, UnityEditor.Build.NamedBuildTarget.Standalone };
        foreach (var target in targets)
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);
            var list = new List<string>(defines.Split(new char[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
            if (enable) { if (!list.Contains(symbol)) list.Add(symbol); }
            else { list.Remove(symbol); }
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
        }
#else
        var groups = new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone };
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

    // ─── Adaptive Icon Helper ────────────────────────────────────────────────
    
    /// <summary>
    /// Sets the Android adaptive icon (foreground + background layers). Modern Android (8.0+) uses adaptive icons.
    /// The background layer gets a solid color, and the foreground gets the icon image.
    /// This ensures the icon looks correct on all Android launchers regardless of shape (circle, square, squircle).
    /// </summary>
    static void SetAdaptiveIcon(Texture2D sourceIcon)
    {
        try
        {
            // Try to set adaptive icon via PlatformIconStruct (Unity 2019.3+)
            var platform = UnityEditor.Android.AndroidPlatformIconKind.Adaptive;
#if UNITY_2023_1_OR_NEWER
            var androidTarget = UnityEditor.Build.NamedBuildTarget.Android;
            var icons = PlayerSettings.GetPlatformIcons(androidTarget, platform);
#else
            var icons = PlayerSettings.GetPlatformIcons(TARGET_GROUP, platform);
#endif
            
            if (icons != null && icons.Length > 0)
            {
                Color bgColor = Color.black;
                if (sourceIcon != null && sourceIcon.name.ToLower().Contains("testnet"))
                    bgColor = new Color(50f / 255f, 118f / 255f, 202f / 255f);
                else if (sourceIcon != null && (sourceIcon.name.ToLower().Contains("minnet") || sourceIcon.name.ToLower().Contains("mainnet") || sourceIcon.name.Contains("satolite_logo_bw")))
                    bgColor = Color.black;
                Texture2D bgTexture = CreateSolidColorTexture(108, 108, bgColor);
                
                for (int i = 0; i < icons.Length; i++)
                {
                    // Layer 0 = background (solid color), Layer 1 = foreground (icon)
                    int layerCount = icons[i].GetTextures().Length;
                    Texture2D[] layers = new Texture2D[layerCount];
                    for (int l = 0; l < layerCount; l++)
                    {
                        if (l == 0 && layerCount > 1)
                            layers[l] = bgTexture; // background matches icon palette
                        else
                            layers[l] = sourceIcon; // foreground = icon
                    }
                    icons[i].SetTextures(layers);
                }
#if UNITY_2023_1_OR_NEWER
                PlayerSettings.SetPlatformIcons(androidTarget, platform, icons);
#else
                PlayerSettings.SetPlatformIcons(TARGET_GROUP, platform, icons);
#endif
                Debug.Log($"[Builder] Adaptive icons set ({icons.Length} entries)");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Builder] Adaptive icon setup failed (may not be supported in this Unity version): {e.Message}");
        }
    }

    // ─── Texture Configuration Helper ──────────────────────────────────────────
    
    /// <summary>
    /// Configures TextureImporter settings for optimal icon quality.
    /// Must be called BEFORE loading the texture to take effect.
    /// - Sets maxTextureSize to 1024 (prevents blurry icons on high-DPI phones)
    /// - Sets textureType to Default (ensures correct pixel reading)
    /// - Enables Read/Write
    /// - Disables mipmaps (not needed for icons)
    /// </summary>
    static void ConfigureTextureForIcon(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[Builder] Could not get TextureImporter for: {assetPath}");
            return;
        }
        
        bool needsReimport = false;
        
        // Ensure readable
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            needsReimport = true;
        }
        
        // Ensure max texture size is large enough for high-DPI devices
        // Android requires up to 192x192 for XXXHDPI, but we use 1024 as source
        if (importer.maxTextureSize < 1024)
        {
            importer.maxTextureSize = 1024;
            needsReimport = true;
        }
        
        // Disable mipmaps (not needed for UI icons)
        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            needsReimport = true;
        }
        
        // Ensure sRGB color space for correct colors
        if (!importer.sRGBTexture)
        {
            importer.sRGBTexture = true;
            needsReimport = true;
        }
        
        // Filter mode for quality
        if (importer.filterMode != FilterMode.Bilinear)
        {
            importer.filterMode = FilterMode.Bilinear;
            needsReimport = true;
        }
        
        if (needsReimport)
        {
            importer.SaveAndReimport();
            Debug.Log($"[Builder] TextureImporter configured for: {assetPath}");
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

    // ─── Texture Scaling Helper ─────────────────────────────────────────────
    
    /// <summary>
    /// Scales a texture to the specified dimensions using high-quality bilinear filtering.
    /// Creates a new texture with the target size.
    /// Falls back to direct copy if RenderTexture is unavailable.
    /// </summary>
    static Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        if (source == null) return null;
        if (source.width == targetWidth && source.height == targetHeight)
            return source;

        try
        {
            // Create render texture for scaling
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            
            // Blit source to render texture with aspect-ratio preservation
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.black);

            float srcAspect = (float)source.width / source.height;
            float dstAspect = (float)targetWidth / targetHeight;
            float scaleX = 1f;
            float scaleY = 1f;
            if (srcAspect > dstAspect)
            {
                scaleY = dstAspect / srcAspect;
            }
            else
            {
                scaleX = srcAspect / dstAspect;
            }

            Vector2 scale = new Vector2(scaleX, scaleY);
            Vector2 offset = new Vector2((1f - scaleX) * 0.5f, (1f - scaleY) * 0.5f);
            Graphics.Blit(source, rt, scale, offset);
            
            // Read pixels from render texture
            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.ARGB32, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();
            
            // Cleanup
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            
            return result;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Builder] ScaleTexture failed for {targetWidth}x{targetHeight}: {e.Message}. Using source.");
            return source;
        }
    }
}
