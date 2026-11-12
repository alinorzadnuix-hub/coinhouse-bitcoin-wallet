using UnityEngine;

public class ThemeManager : MonoBehaviour
{
    private static ThemeManager instance;
    public static ThemeManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject obj = new GameObject("ThemeManager");
                instance = obj.AddComponent<ThemeManager>();
                DontDestroyOnLoad(obj);
            }
            return instance;
        }
    }

    // Modern theme is now the default (Light mode)
    public bool IsDarkMode { get; private set; }

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            // Default to Dark Mode (1)
            IsDarkMode = PlayerPrefs.GetInt("DarkMode", 1) == 1;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    public void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        PlayerPrefs.SetInt("DarkMode", IsDarkMode ? 1 : 0);
        PlayerPrefs.Save();
    }

    // ===== MODERN THEME COLORS (White & Gray) =====
    
    public Color GetBackgroundColor()
    {
        return IsDarkMode ? new Color(0.1f, 0.1f, 0.15f) : ModernTheme.BackgroundMilkyWhite;
    }

    public Color GetCardColor()
    {
        return IsDarkMode ? new Color(0.15f, 0.15f, 0.2f) : ModernTheme.CardLightGray;
    }

    public Color GetAccentColor()
    {
        return IsDarkMode ? new Color(0.15f, 0.5f, 0.95f) : ModernTheme.ButtonPrimary;
    }

    public Color GetTextColor()
    {
        return IsDarkMode ? Color.white : ModernTheme.TextPrimary;
    }

    public Color GetSecondaryTextColor()
    {
        return IsDarkMode ? new Color(0.7f, 0.7f, 0.7f) : ModernTheme.TextSecondary;
    }
    
    // ===== NEW HELPER METHODS FOR MODERN THEME =====
    
    public Color GetButtonColor(ButtonType type = ButtonType.Primary)
    {
        if (IsDarkMode)
        {
            // Keep old dark theme button colors
            return new Color(0.15f, 0.5f, 0.95f);
        }
        return ModernTheme.GetButtonColor(type);
    }
    
    public Color GetNetworkColor(bool isTestNet)
    {
        return ModernTheme.GetNetworkColor(isTestNet);
    }
    
    public Color GetSuccessColor()
    {
        return ModernTheme.AccentGreen;
    }
    
    public Color GetErrorColor()
    {
        return ModernTheme.AccentRed;
    }
    
    public Color GetWarningColor()
    {
        return ModernTheme.AccentGold;
    }
}
