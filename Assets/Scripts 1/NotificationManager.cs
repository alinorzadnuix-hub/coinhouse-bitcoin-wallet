using UnityEngine;
using System;
using System.Collections.Generic;

#if UNITY_ANDROID
using Unity.Notifications.Android;
using UnityEngine.Android;
#endif

public class NotificationManager : MonoBehaviour
{
    private static NotificationManager _instance;
    public static NotificationManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<NotificationManager>();
                if (_instance == null)
                {
                    GameObject obj = new GameObject("NotificationManager");
                    _instance = obj.AddComponent<NotificationManager>();
                }
            }
            return _instance;
        }
    }

    private const string CHANNEL_ID = "wallet_alerts";
    private const string CHANNEL_NAME = "Wallet Alerts";
    private List<int> scheduledPriceAlertIds = new List<int>();

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    void Initialize()
    {
        #if UNITY_ANDROID
        // Create Channel
        var channel = new AndroidNotificationChannel()
        {
            Id = CHANNEL_ID,
            Name = CHANNEL_NAME,
            Importance = Importance.High,
            Description = "Notifications for wallet transactions and mining.",
        };
        AndroidNotificationCenter.RegisterNotificationChannel(channel);
        
        // Request Permission for Android 13+
        if (!Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS"))
        {
            Permission.RequestUserPermission("android.permission.POST_NOTIFICATIONS");
        }

        Debug.Log("[NotificationManager] Channel Registered & Permissions Requested");
        #endif
    }
    public void SendNotification(string title, string text, int delaySeconds = 0)
    {
        // Check if notifications are enabled
        if (!IsEnabled())
        {
            Debug.Log($"[NotificationManager] Notifications disabled. Skipped: {title}");
            return;
        }
        
        Debug.Log($"[NotificationManager] Sending: {title} - {text}");

        #if UNITY_ANDROID
        if (Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS"))
        {
            var notification = new AndroidNotification();
            notification.Title = title;
            notification.Text = text;
            notification.FireTime = System.DateTime.Now.AddSeconds(delaySeconds);
            // Use the specific logo for the variant (copied by BuildVariantSwitcher)
            notification.SmallIcon = "notif_icon";
            notification.LargeIcon = "notif_icon";
            
            AndroidNotificationCenter.SendNotification(notification, CHANNEL_ID);
        }
        else
        {
            Debug.LogWarning("[NotificationManager] Permission denied for notifications");
        }
        #endif

        if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
        {
            try
            {
                string escapedTitle = title.Replace("\"", "`\"");
                string escapedText = text.Replace("\"", "`\"");
                string delayCmd = delaySeconds > 0 ? $"Start-Sleep -s {delaySeconds}; " : "";
                string psCommand = $"-Command \"{delayCmd}[void] [System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms'); $icon = New-Object System.Windows.Forms.NotifyIcon; $icon.Icon = [System.Drawing.SystemIcons]::Information; $icon.BalloonTipIcon = 'Info'; $icon.BalloonTipTitle = '{escapedTitle}'; $icon.BalloonTipText = '{escapedText}'; $icon.Visible = $True; $icon.ShowBalloonTip(5000); Start-Sleep -s 6; $icon.Dispose()\"";

                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = psCommand;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                System.Diagnostics.Process.Start(psi);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[NotificationManager] Windows notification failed: " + ex.Message);
            }
        }
        
        if (Application.isEditor)
        {
            Debug.Log($"[NotificationManager] Editor: {title} - {text}");
        }
    }    
    // --- Notification Settings ---
    
    private const string PREF_ENABLED = "NotifEnabled";
    private const string PREF_INTERVAL = "NotifInterval";
    
    // Available intervals in minutes
    public static readonly int[] IntervalOptions = { 30, 60, 360, 1440, 10080 };
    public static readonly string[] IntervalLabels = { "30 Min", "1 Hour", "6 Hours", "1 Day", "7 Days" };
    
    public bool IsEnabled()
    {
        return PlayerPrefs.GetInt(PREF_ENABLED, 1) == 1; // On by default
    }
    
    public void SetEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(PREF_ENABLED, enabled ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[NotificationManager] Notifications {(enabled ? "enabled" : "disabled")}");
    }
    
    public void ToggleEnabled()
    {
        SetEnabled(!IsEnabled());
    }
    
    public int GetIntervalMinutes()
    {
        return PlayerPrefs.GetInt(PREF_INTERVAL, 60); // Default: 1 hour
    }
    
    public void SetIntervalMinutes(int minutes)
    {
        PlayerPrefs.SetInt(PREF_INTERVAL, minutes);
        PlayerPrefs.Save();
        Debug.Log($"[NotificationManager] Interval set to {minutes} minutes");
    }
    
    public string GetIntervalLabel()
    {
        int current = GetIntervalMinutes();
        for (int i = 0; i < IntervalOptions.Length; i++)
        {
            if (IntervalOptions[i] == current) return IntervalLabels[i];
        }
        return $"{current} Min";
    }
    
    public void CycleInterval()
    {
        int current = GetIntervalMinutes();
        int nextIndex = 0;
        for (int i = 0; i < IntervalOptions.Length; i++)
        {
            if (IntervalOptions[i] == current)
            {
                nextIndex = (i + 1) % IntervalOptions.Length;
                break;
            }
        }
        SetIntervalMinutes(IntervalOptions[nextIndex]);
    }

    // --- Background Price Alert Scheduling ---

    /// <summary>
    /// Schedule future notifications when app goes to background.
    /// These fire at the user's chosen cycle interval to remind them of BTC price.
    /// </summary>
    public void ScheduleBackgroundPriceAlerts(double lastPrice, string currencySymbol, string currencyCode)
    {
        CancelScheduledPriceAlerts();

        if (PlayerPrefs.GetInt("PriceAlertEnabled", 0) != 1) return;
        if (!IsEnabled()) return;

        #if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS"))
        {
            Debug.LogWarning("[NotificationManager] No notification permission for price alerts");
            return;
        }

        string cycle = PlayerPrefs.GetString("PriceAlertCycle", "1hr");
        int intervalSeconds = GetCycleIntervalSeconds(cycle);

        // Schedule up to 5 future notifications based on cycle
        for (int i = 1; i <= 5; i++)
        {
            var notification = new AndroidNotification();
#if TESTNET_BUILD
            notification.Title = "\u23F0 TBTC Price Alert";
            notification.Text = $"TBTC was {currencySymbol}{lastPrice:N6} {currencyCode} \u2014 Open app for live price";
#else
            notification.Title = "\u23F0 BTC Price Alert";
            notification.Text = $"BTC was {currencySymbol}{lastPrice:N0} {currencyCode} \u2014 Open app for live price";
#endif
            notification.FireTime = DateTime.Now.AddSeconds(intervalSeconds * i);
            
            // Use the specific logo for the variant (copied by BuildVariantSwitcher)
            notification.SmallIcon = "notif_icon";
            notification.LargeIcon = "notif_icon";

            int id = AndroidNotificationCenter.SendNotification(notification, CHANNEL_ID);
            scheduledPriceAlertIds.Add(id);
            Debug.Log($"[NotificationManager] Scheduled price alert #{i} in {intervalSeconds * i}s (ID: {id})");
        }
        #endif

        Debug.Log($"[NotificationManager] Scheduled {scheduledPriceAlertIds.Count} background price alerts");
    }

    /// <summary>
    /// Cancel all previously scheduled price alert notifications.
    /// Called when app returns to foreground.
    /// </summary>
    public void CancelScheduledPriceAlerts()
    {
        #if UNITY_ANDROID && !UNITY_EDITOR
        foreach (int id in scheduledPriceAlertIds)
        {
            AndroidNotificationCenter.CancelScheduledNotification(id);
        }
        #endif

        if (scheduledPriceAlertIds.Count > 0)
            Debug.Log($"[NotificationManager] Cancelled {scheduledPriceAlertIds.Count} scheduled price alerts");

        scheduledPriceAlertIds.Clear();
    }

    private int GetCycleIntervalSeconds(string cycle)
    {
        switch (cycle)
        {
            case "15m":    return 15 * 60;
            case "30m":    return 30 * 60;
            case "1hr":    return 60 * 60;
            case "24hr":   return 24 * 60 * 60;
            case "7 days": return 7 * 24 * 60 * 60;
            default:       return 60 * 60;
        }
    }
}
