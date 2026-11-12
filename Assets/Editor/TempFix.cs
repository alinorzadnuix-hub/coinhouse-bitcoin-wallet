using UnityEditor;
using UnityEngine;
using System.IO;

[InitializeOnLoad]
public class TempFix {
    static TempFix() {
        FixIcon();
    }

    [InitializeOnLoadMethod]
    public static void FixIcon() {
        bool isTestnet = false;
#if TESTNET_BUILD
        isTestnet = true;
#endif
        string targetIconPath = isTestnet ? "Assets/Resources/testnet.png" : "Assets/Resources/Minnet.png";
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(targetIconPath)
                ?? AssetDatabase.LoadAssetAtPath<Texture2D>(isTestnet ? "Assets/testnet.png" : "Assets/Minnet.png")
                ?? AssetDatabase.LoadAssetAtPath<Texture2D>(isTestnet ? "Assets/testnet.jpg" : "Assets/Minnet.jpg");
        
        PlayerSettings.productName = isTestnet ? "CoinHouse Testnet" : "CoinHouse";
        
        if (icon != null) {
            // Android application icons (legacy)
#if UNITY_2023_1_OR_NEWER
            var androidTarget = UnityEditor.Build.NamedBuildTarget.Android;
            int[] sizes = PlayerSettings.GetIconSizes(androidTarget, IconKind.Application);
            Texture2D[] icons = new Texture2D[sizes.Length];
            for (int i = 0; i < sizes.Length; i++) icons[i] = icon;
            PlayerSettings.SetIcons(androidTarget, icons, IconKind.Application);
#else
            int[] sizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Android);
            Texture2D[] icons = new Texture2D[sizes.Length];
            for (int i = 0; i < sizes.Length; i++) icons[i] = icon;
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, icons);
#endif
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new Texture2D[] { icon });

            // Android Adaptive and Round icons
            try {
                Color bgColor = isTestnet ? new Color(50f / 255f, 118f / 255f, 202f / 255f) : Color.black;
                Texture2D bgTexture = new Texture2D(108, 108, TextureFormat.RGBA32, false);
                Color[] cols = new Color[108 * 108];
                for (int c = 0; c < cols.Length; c++) cols[c] = bgColor;
                bgTexture.SetPixels(cols);
                bgTexture.Apply();

                var adaptiveKind = UnityEditor.Android.AndroidPlatformIconKind.Adaptive;
#if UNITY_2023_1_OR_NEWER
                var adaptiveIcons = PlayerSettings.GetPlatformIcons(androidTarget, adaptiveKind);
#else
                var adaptiveIcons = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, adaptiveKind);
#endif
                if (adaptiveIcons != null && adaptiveIcons.Length > 0) {
                    for (int i = 0; i < adaptiveIcons.Length; i++) {
                        int layerCount = adaptiveIcons[i].GetTextures().Length;
                        Texture2D[] layers = new Texture2D[layerCount];
                        for (int l = 0; l < layerCount; l++)
                            layers[l] = (l == 0 && layerCount > 1) ? bgTexture : icon;
                        adaptiveIcons[i].SetTextures(layers);
                    }
#if UNITY_2023_1_OR_NEWER
                    PlayerSettings.SetPlatformIcons(androidTarget, adaptiveKind, adaptiveIcons);
#else
                    PlayerSettings.SetPlatformIcons(BuildTargetGroup.Android, adaptiveKind, adaptiveIcons);
#endif
                }
            } catch (System.Exception ex) {
                Debug.LogWarning("[TempFix] Adaptive icon setup: " + ex.Message);
            }

            // Sync EditorPrefs
            EditorPrefs.SetString("BuildVariant_IconTestNet", "Assets/Resources/testnet.png");
            EditorPrefs.SetString("BuildVariant_IconMainNet", "Assets/Resources/Minnet.png");

            // Notification icon in NotifIcon.androidlib
            try {
                string libPath = Path.Combine(Application.dataPath, "Plugins", "Android", "NotifIcon.androidlib", "res", "drawable");
                if (Directory.Exists(libPath)) {
                    string destPath = Path.Combine(libPath, "notif_icon.png");
                    if (icon.isReadable) {
                        RenderTexture rt = RenderTexture.GetTemporary(192, 192, 0, RenderTextureFormat.ARGB32);
                        Graphics.Blit(icon, rt);
                        RenderTexture prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        Texture2D scaled = new Texture2D(192, 192, TextureFormat.RGBA32, false);
                        scaled.ReadPixels(new Rect(0, 0, 192, 192), 0, 0);
                        scaled.Apply();
                        RenderTexture.active = prev;
                        RenderTexture.ReleaseTemporary(rt);
                        byte[] pngBytes = scaled.EncodeToPNG();
                        Object.DestroyImmediate(scaled);
                        if (pngBytes != null && pngBytes.Length > 0) {
                            File.WriteAllBytes(destPath, pngBytes);
                        }
                    }
                }
            } catch (System.Exception ex) {
                Debug.LogWarning("[TempFix] Notification icon sync: " + ex.Message);
            }

            // Splash Screen Configuration via SerializedObject and SplashScreen API
            try {
                Texture2D splashPng = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/splash scren.png")
                                   ?? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/splash_screen.png")
                                   ?? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/splash scren.jpg");
                Sprite splashSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/splash scren.png")
                                   ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/splash_screen.png");

                if (splashSprite != null) {
                    PlayerSettings.SplashScreenLogo logo = PlayerSettings.SplashScreenLogo.Create(2f, splashSprite);
                    PlayerSettings.SplashScreen.logos = new PlayerSettings.SplashScreenLogo[] { logo };
                }

                var playerSettingsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (playerSettingsAsset != null && playerSettingsAsset.Length > 0 && splashPng != null) {
                    SerializedObject so = new SerializedObject(playerSettingsAsset[0]);
                    var androidSplashProp = so.FindProperty("androidSplashScreen");
                    if (androidSplashProp != null) androidSplashProp.objectReferenceValue = splashPng;
                    var vrSplashProp = so.FindProperty("m_VirtualRealitySplashScreen");
                    if (vrSplashProp != null) vrSplashProp.objectReferenceValue = splashPng;
                    var bgPortraitProp = so.FindProperty("splashScreenBackgroundSourcePortrait");
                    if (bgPortraitProp != null) bgPortraitProp.objectReferenceValue = splashPng;
                    var bgLandscapeProp = so.FindProperty("splashScreenBackgroundSourceLandscape");
                    if (bgLandscapeProp != null) bgLandscapeProp.objectReferenceValue = splashPng;
                    so.ApplyModifiedProperties();
                }
            } catch (System.Exception ex) {
                Debug.LogWarning("[TempFix] Splash screen setup: " + ex.Message);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Build Variant] Enforced App Icon and Name: " + PlayerSettings.productName + " (" + icon.name + ")");
        }
    }
}
