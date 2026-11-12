using UnityEngine;
using System.IO;
using System;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class WalletFileService
{
    private const string BACKUP_FILENAME = "wallet_backup.json";
    
    // Callback for file picker result
    private static Action<string> filePickerCallback;

    /// <summary>
    /// Export wallet to file including device language and app settings.
    /// Returns the path where the file was saved, or null on failure.
    /// </summary>
    public static string ExportWallet(string fileName = null)
    {
        if (fileName == null) fileName = BACKUP_FILENAME;
        
        if (!WalletManager2.Instance.HasWallet())
        {
            Debug.LogWarning("[WalletFileService] No wallet to export.");
            return null;
        }

        WalletData w = WalletManager2.Instance.GetCurrentWallet();
        
        // Build export data with settings
        WalletExportData exportData = new WalletExportData();
        exportData.walletData = w;
        
        // App Settings
        exportData.appLanguage = PlayerPrefs.GetString("SelectedLanguage", "English");
        exportData.appCurrency = PlayerPrefs.GetString("SelectedCurrency", "USD");
        exportData.isDarkMode = PlayerPrefs.GetInt("IsDarkMode", 1) == 1;
        exportData.soundEnabled = PlayerPrefs.GetInt("SoundEnabled", 1) == 1;
        
        // Price Alert Settings
        exportData.priceAlertEnabled = PlayerPrefs.GetInt("PriceAlertEnabled", 0) == 1;
        exportData.priceAlertCycle = PlayerPrefs.GetString("PriceAlertCycle", "1hr");
        
        // Device Info
        exportData.deviceLanguage = Application.systemLanguage.ToString();
        exportData.exportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        exportData.appVersion = Application.version;
        
        string json = JsonUtility.ToJson(exportData, true);
        
        // 1. Always save to persistentDataPath (guaranteed to work on all platforms)
        string primaryPath = Path.Combine(Application.persistentDataPath, fileName);
        try
        {
            File.WriteAllText(primaryPath, json);
            Debug.Log($"[WalletFileService] ✅ Wallet exported to: {primaryPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WalletFileService] ❌ Failed to export to persistentDataPath: {ex.Message}");
            return null;
        }
        
        // 2. Platform-specific secondary location
        string secondaryPath = null;
        
#if UNITY_ANDROID && !UNITY_EDITOR
        // Android: Save to Downloads folder
        try
        {
            RequestStoragePermission();
            
            // Try external Downloads folder
            string downloadsPath = "/storage/emulated/0/Download";
            if (Directory.Exists(downloadsPath))
            {
                secondaryPath = Path.Combine(downloadsPath, fileName);
                File.WriteAllText(secondaryPath, json);
                Debug.Log($"[WalletFileService] ✅ Wallet also exported to Downloads: {secondaryPath}");
            }
            else
            {
                // Fallback: try Android's external files directory
                string extPath = Application.persistentDataPath.Replace("/files", "/files/Download");
                Directory.CreateDirectory(extPath);
                secondaryPath = Path.Combine(extPath, fileName);
                File.WriteAllText(secondaryPath, json);
                Debug.Log($"[WalletFileService] ✅ Wallet also exported to: {secondaryPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WalletFileService] ⚠️ Could not export to Downloads: {ex.Message}");
        }
#else
        // Desktop: Save to Desktop
        try
        {
            string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrEmpty(desktop))
            {
                secondaryPath = Path.Combine(desktop, fileName);
                File.WriteAllText(secondaryPath, json);
                Debug.Log($"[WalletFileService] ✅ Wallet also exported to Desktop: {secondaryPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WalletFileService] ⚠️ Could not export to Desktop: {ex.Message}");
        }
#endif
        
        // Return the most accessible path
        string resultPath = secondaryPath ?? primaryPath;
        Debug.Log($"[WalletFileService] 📁 Final export path: {resultPath}");
        return resultPath;
    }

    /// <summary>
    /// Import wallet from JSON string content.
    /// Supports both new WalletExportData format and legacy WalletData format.
    /// </summary>
    public static bool ImportWallet(string jsonContent)
    {
        try
        {
            // Try new WalletExportData format first
            WalletExportData exportData = JsonUtility.FromJson<WalletExportData>(jsonContent);
            
            if (exportData != null && exportData.walletData != null && 
                (!string.IsNullOrEmpty(exportData.walletData.seedPhrase) || !string.IsNullOrEmpty(exportData.walletData.address)))
            {
                WalletData w = exportData.walletData;
                
                WalletManager2.Instance.SaveWallet(
                    w.seedPhrase ?? "", 
                    false, // Always MainNet
                    w.address, 
                    w.privateKey
                );
                
                // Restore app settings if they exist
                if (!string.IsNullOrEmpty(exportData.appLanguage))
                {
                    PlayerPrefs.SetString("SelectedLanguage", exportData.appLanguage);
                    LocalizationManager.Instance.SetLanguage(exportData.appLanguage);
                }
                if (!string.IsNullOrEmpty(exportData.appCurrency))
                    PlayerPrefs.SetString("SelectedCurrency", exportData.appCurrency);
                
                PlayerPrefs.SetInt("IsDarkMode", exportData.isDarkMode ? 1 : 0);
                PlayerPrefs.SetInt("SoundEnabled", exportData.soundEnabled ? 1 : 0);
                PlayerPrefs.SetInt("PriceAlertEnabled", exportData.priceAlertEnabled ? 1 : 0);
                
                if (!string.IsNullOrEmpty(exportData.priceAlertCycle))
                    PlayerPrefs.SetString("PriceAlertCycle", exportData.priceAlertCycle);
                
                PlayerPrefs.Save();
                
                Debug.Log($"[WalletFileService] ✅ Wallet imported with settings! Address: {w.address}");
                return true;
            }
            
            // Fallback: Try legacy WalletData format
            WalletData legacyData = JsonUtility.FromJson<WalletData>(jsonContent);
            if (legacyData != null && (!string.IsNullOrEmpty(legacyData.seedPhrase) || !string.IsNullOrEmpty(legacyData.address)))
            {
                WalletManager2.Instance.SaveWallet(
                    legacyData.seedPhrase ?? "", 
                    false, // Always MainNet
                    legacyData.address, 
                    legacyData.privateKey
                );
                Debug.Log($"[WalletFileService] ✅ Wallet imported (legacy format)! Address: {legacyData.address}");
                return true;
            }
            
            Debug.LogError("[WalletFileService] ❌ Import Failed: No valid wallet data found in file.");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[WalletFileService] ❌ Import Failed: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Import wallet from a file path. Reads the file and calls ImportWallet.
    /// </summary>
    public static bool ImportWalletFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[WalletFileService] ❌ File not found: {filePath}");
                return false;
            }
            
            string json = File.ReadAllText(filePath);
            Debug.Log($"[WalletFileService] 📂 Reading wallet file from: {filePath} ({json.Length} bytes)");
            return ImportWallet(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[WalletFileService] ❌ Failed to read file: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get the default import file path based on platform.
    /// </summary>
    public static string GetDefaultImportPath()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string downloadsPath = "/storage/emulated/0/Download";
        return Path.Combine(downloadsPath, BACKUP_FILENAME);
#else
        string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
        string desktopPath = Path.Combine(desktop, BACKUP_FILENAME);
        if (File.Exists(desktopPath)) return desktopPath;
        return Path.Combine(Application.persistentDataPath, BACKUP_FILENAME);
#endif
    }

    /// <summary>
    /// Check if a backup file exists at the default import location.
    /// </summary>
    public static bool BackupFileExists()
    {
        string path = GetDefaultImportPath();
        bool exists = File.Exists(path);
        Debug.Log($"[WalletFileService] Checking backup at: {path} → {(exists ? "Found" : "Not found")}");
        
        if (!exists)
        {
            string fallback = Path.Combine(Application.persistentDataPath, BACKUP_FILENAME);
            exists = File.Exists(fallback);
            if (exists) Debug.Log($"[WalletFileService] Found backup at fallback: {fallback}");
        }
        
        return exists;
    }

    /// <summary>
    /// Request storage permissions on Android with proper callback handling.
    /// Returns true if permission is already granted.
    /// </summary>
    public static bool RequestStoragePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
        {
            Debug.Log("[WalletFileService] 🔐 Requesting WRITE_EXTERNAL_STORAGE permission...");
            Permission.RequestUserPermission(Permission.ExternalStorageWrite);
        }
        
        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
        {
            Debug.Log("[WalletFileService] 🔐 Requesting READ_EXTERNAL_STORAGE permission...");
            Permission.RequestUserPermission(Permission.ExternalStorageRead);
        }
        
        return Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead) &&
               Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite);
#else
        return true;
#endif
    }

    /// <summary>
    /// Check if we have storage permission (without requesting).
    /// </summary>
    public static bool HasStoragePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead) &&
               Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite);
#else
        return true;
#endif
    }

    /// <summary>
    /// Open Android native file picker to select a wallet backup file.
    /// Calls the callback with the file content when done.
    /// </summary>
    public static void OpenFilePicker(Action<string> onFileContent)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            // Request permission first
            if (!HasStoragePermission())
            {
                RequestStoragePermission();
            }
            
            // Use Unity's NativeFilePicker or fallback to Intent
            using (AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent"))
            using (AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent"))
            {
                // ACTION_OPEN_DOCUMENT for file selection
                intent.Call<AndroidJavaObject>("setAction", "android.intent.action.OPEN_DOCUMENT");
                intent.Call<AndroidJavaObject>("addCategory", "android.intent.category.OPENABLE");
                intent.Call<AndroidJavaObject>("setType", "*/*");
                
                // Get current activity - FIXED: Use UnityPlayer instead of UnityPlayerActivity
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    filePickerCallback = onFileContent;
                    currentActivity.Call("startActivityForResult", intent, 1001);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WalletFileService] File picker failed: {ex.Message}. Falling back to default path.");
            // Fallback: try to read from default path
            string defaultPath = GetDefaultImportPath();
            if (File.Exists(defaultPath))
            {
                string content = File.ReadAllText(defaultPath);
                onFileContent?.Invoke(content);
            }
            else
            {
                // Try persistentDataPath
                string fallbackPath = Path.Combine(Application.persistentDataPath, BACKUP_FILENAME);
                if (File.Exists(fallbackPath))
                {
                    string content = File.ReadAllText(fallbackPath);
                    onFileContent?.Invoke(content);
                }
                else
                {
                    onFileContent?.Invoke(null);
                }
            }
        }
#else
        // Desktop fallback: search multiple locations for backup file
        string[] searchPaths = new string[]
        {
            GetDefaultImportPath(),
            Path.Combine(Application.persistentDataPath, BACKUP_FILENAME),
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), BACKUP_FILENAME),
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), BACKUP_FILENAME),
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads", BACKUP_FILENAME)
        };
        
        foreach (string searchPath in searchPaths)
        {
            try
            {
                if (!string.IsNullOrEmpty(searchPath) && File.Exists(searchPath))
                {
                    string content = File.ReadAllText(searchPath);
                    Debug.Log($"[WalletFileService] ✅ Found wallet backup at: {searchPath}");
                    onFileContent?.Invoke(content);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WalletFileService] Could not read {searchPath}: {ex.Message}");
            }
        }
        
        Debug.LogError($"[WalletFileService] ❌ No backup file found in any search location.");
        onFileContent?.Invoke(null);
#endif
    }

    /// <summary>
    /// Called from Android when file picker returns a result.
    /// This needs to be connected via a MonoBehaviour message receiver.
    /// </summary>
    public static void OnFilePickerResult(string filePath)
    {
        if (filePickerCallback != null && !string.IsNullOrEmpty(filePath))
        {
            try
            {
                string content = File.ReadAllText(filePath);
                filePickerCallback.Invoke(content);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WalletFileService] Failed to read picked file: {ex.Message}");
                filePickerCallback.Invoke(null);
            }
            filePickerCallback = null;
        }
    }
}
