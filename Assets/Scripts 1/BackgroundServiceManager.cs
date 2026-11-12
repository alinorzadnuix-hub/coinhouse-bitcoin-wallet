using UnityEngine;
using System;

public class BackgroundServiceManager : MonoBehaviour
{
    private static BackgroundServiceManager _instance;
    public static BackgroundServiceManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject obj = new GameObject("BackgroundServiceManager");
                _instance = obj.AddComponent<BackgroundServiceManager>();
                DontDestroyOnLoad(obj);
            }
            return _instance;
        }
    }

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    public void StartForegroundService()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayerForActivityOrService");
            AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent", activity, new AndroidJavaClass("com.antigravity.wallet.WalletBackgroundService"));
            
            // Pass the selected currency to the service
            string currencyCode = PlayerPrefs.GetString("SelectedCurrency", "USD").ToLower();
            string symbol = CurrencyHelper.GetSymbol(currencyCode.ToUpper());
            intent.Call<AndroidJavaObject>("putExtra", "currency", currencyCode);
            intent.Call<AndroidJavaObject>("putExtra", "symbol", symbol);
#if TESTNET_BUILD
            intent.Call<AndroidJavaObject>("putExtra", "isTestNet", "true");
#else
            intent.Call<AndroidJavaObject>("putExtra", "isTestNet", "false");
#endif

            if (GetAndroidSDKVersion() >= 26) // Android 8.0 Oreo
            {
                activity.Call<AndroidJavaObject>("startForegroundService", intent);
            }
            else
            {
                activity.Call<AndroidJavaObject>("startService", intent);
            }
            Debug.Log("[BackgroundServiceManager] Native Foreground Service Started.");
        }
        catch (Exception ex)
        {
            Debug.LogError("[BackgroundServiceManager] Failed to start foreground service: " + ex.Message);
        }
#endif
    }

    public void StopForegroundService()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayerForActivityOrService");
            AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent", activity, new AndroidJavaClass("com.antigravity.wallet.WalletBackgroundService"));
            activity.Call<bool>("stopService", intent);
            Debug.Log("[BackgroundServiceManager] Native Foreground Service Stopped.");
        }
        catch (Exception ex)
        {
            Debug.LogError("[BackgroundServiceManager] Failed to stop foreground service: " + ex.Message);
        }
#endif
    }

    private int GetAndroidSDKVersion()
    {
        int version = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AndroidJavaClass buildVersion = new AndroidJavaClass("android.os.Build$VERSION");
            version = buildVersion.GetStatic<int>("SDK_INT");
        }
        catch { }
#endif
        return version;
    }
}
