using UnityEngine;

/// <summary>
/// ScreenSecurityManager manages OS-level protection against screen recording and screenshots on Android.
/// Applies WindowManager.LayoutParams.FLAG_SECURE to prevent unauthorized capture of sensitive UI
/// such as PIN/password entry, seed phrases, and private keys.
/// </summary>
public static class ScreenSecurityManager
{
    private const int FLAG_SECURE = 8192; // 0x00002000: WindowManager.LayoutParams.FLAG_SECURE
    private static bool isProtected = false;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern System.IntPtr GetActiveWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(System.IntPtr hWnd, uint dwAffinity);

    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_MONITOR = 0x00000001;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // Windows 10 2004+
#endif

    public static bool IsProtected => isProtected;

    /// <summary>
    /// Enables screen recording and screenshot protection.
    /// When active on Android, screen recorders record only a black screen,
    /// screenshots are blocked by the OS, and recent app previews are obscured.
    /// </summary>
    public static void EnableProtection()
    {
        SetProtection(true);
    }

    /// <summary>
    /// Disables screen recording and screenshot protection.
    /// </summary>
    public static void DisableProtection()
    {
        SetProtection(false);
    }

    /// <summary>
    /// Toggles screen recording protection on Android.
    /// </summary>
    public static void SetProtection(bool enable)
    {
        isProtected = enable;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (currentActivity == null)
                {
                    Debug.LogWarning("[ScreenSecurityManager] currentActivity is null.");
                    return;
                }

                currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using (AndroidJavaObject window = currentActivity.Call<AndroidJavaObject>("getWindow"))
                        {
                            if (window != null)
                            {
                                if (enable)
                                {
                                    window.Call("addFlags", FLAG_SECURE);
                                    Debug.Log("[ScreenSecurityManager] FLAG_SECURE added. Screen recording & capture blocked.");
                                }
                                else
                                {
                                    window.Call("clearFlags", FLAG_SECURE);
                                    Debug.Log("[ScreenSecurityManager] FLAG_SECURE cleared. Screen recording allowed.");
                                }
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning("[ScreenSecurityManager] Failed to modify window flags on UI thread: " + ex.Message);
                    }
                }));
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[ScreenSecurityManager] Failed to set FLAG_SECURE: " + ex.Message);
        }
#elif UNITY_STANDALONE_WIN
        try
        {
            System.IntPtr hwnd = GetActiveWindow();
            if (hwnd == System.IntPtr.Zero)
            {
                hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }

            if (hwnd != System.IntPtr.Zero)
            {
                if (enable)
                {
                    // Try WDA_EXCLUDEFROMCAPTURE (Win10 2004+), fallback to WDA_MONITOR
                    bool ok = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
                    if (!ok) ok = SetWindowDisplayAffinity(hwnd, WDA_MONITOR);
                    Debug.Log($"[ScreenSecurityManager] Windows DisplayAffinity set to protected: {ok}");
                }
                else
                {
                    bool ok = SetWindowDisplayAffinity(hwnd, WDA_NONE);
                    Debug.Log($"[ScreenSecurityManager] Windows DisplayAffinity set to none: {ok}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[ScreenSecurityManager] Failed to set Windows DisplayAffinity: " + ex.Message);
        }
#else
        Debug.Log($"[ScreenSecurityManager] (Editor/Platform) Screen capture protection: {(enable ? "ENABLED" : "DISABLED")}");
#endif
    }

    /// <summary>
    /// Checks whether active screen recording, mirroring, or virtual displays are present.
    /// </summary>
    public static bool IsScreenRecordingActive()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (currentActivity == null) return false;

                using (AndroidJavaObject displayManager = currentActivity.Call<AndroidJavaObject>("getSystemService", "display"))
                {
                    if (displayManager != null)
                    {
                        AndroidJavaObject[] displays = displayManager.Call<AndroidJavaObject[]>("getDisplays");
                        if (displays != null && displays.Length > 1)
                        {
                            foreach (var display in displays)
                            {
                                if (display == null) continue;
                                int displayId = display.Call<int>("getDisplayId");
                                // displayId 0 is Display.DEFAULT_DISPLAY.
                                // Any secondary / virtual display indicates screen recording or mirroring.
                                if (displayId != 0)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[ScreenSecurityManager] IsScreenRecordingActive check failed: " + ex.Message);
        }
#endif
        return false;
    }
}
