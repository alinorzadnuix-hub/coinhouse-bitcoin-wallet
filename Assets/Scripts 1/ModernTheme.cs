using UnityEngine;

/// <summary>
/// Modern White & Gray Theme for Satolite Bitcoin Wallet
/// Provides a clean, professional color scheme with milky white backgrounds
/// </summary>
public static class ModernTheme
{
    // ===== BACKGROUND COLORS =====
    public static readonly Color BackgroundMilkyWhite = new Color(0.97f, 0.97f, 0.97f); // #F8F8F8
    public static readonly Color BackgroundPureWhite = Color.white;
    
    // ===== CARD COLORS =====
    public static readonly Color CardLightGray = new Color(0.91f, 0.91f, 0.91f); // #E8E8E8
    public static readonly Color CardMediumGray = new Color(0.85f, 0.85f, 0.85f); // #D9D9D9
    public static readonly Color CardDarkGray = new Color(0.75f, 0.75f, 0.75f); // #C0C0C0
    
    // ===== BUTTON COLORS =====
    public static readonly Color ButtonPrimary = new Color(0.2f, 0.4f, 0.8f); // Blue
    public static readonly Color ButtonSecondary = new Color(0.5f, 0.5f, 0.5f); // Gray
    public static readonly Color ButtonSuccess = new Color(1f, 0.5f, 0f); // Orange
    public static readonly Color ButtonWarning = new Color(0.9f, 0.6f, 0.1f); // Orange
    public static readonly Color ButtonDanger = new Color(0.8f, 0.2f, 0.2f); // Red
    public static readonly Color ButtonHover = new Color(0.15f, 0.35f, 0.75f); // Darker Blue
    
    // ===== TEXT COLORS =====
    public static readonly Color TextPrimary = new Color(0.15f, 0.15f, 0.15f); // Almost Black
    public static readonly Color TextSecondary = new Color(0.4f, 0.4f, 0.4f); // Medium Gray
    public static readonly Color TextMuted = new Color(0.6f, 0.6f, 0.6f); // Light Gray
    public static readonly Color TextWhite = Color.white;
    
    // ===== ACCENT COLORS =====
    public static readonly Color AccentGold = new Color(1f, 0.84f, 0f); // Bitcoin Gold
    public static readonly Color AccentGreen = new Color(1f, 0.6f, 0f); // Success Orange
    public static readonly Color AccentRed = new Color(1f, 0.3f, 0.3f); // Error Red
    public static readonly Color AccentBlue = new Color(0.2f, 0.6f, 1f); // Info Blue
    
    // ===== NETWORK COLORS =====
    // TestNet Orange Removed
    public static readonly Color MainNetGold = new Color(1f, 0.7f, 0f); // #FFB300
    
    // ===== SHADOW & BORDER =====
    public static readonly Color ShadowLight = new Color(0f, 0f, 0f, 0.05f);
    public static readonly Color ShadowMedium = new Color(0f, 0f, 0f, 0.1f);
    public static readonly Color BorderLight = new Color(0.8f, 0.8f, 0.8f);
    
    // ===== HELPER METHODS =====
    
    /// <summary>
    /// Get button color based on type
    /// </summary>
    public static Color GetButtonColor(ButtonType type)
    {
        switch (type)
        {
            case ButtonType.Primary: return ButtonPrimary;
            case ButtonType.Secondary: return ButtonSecondary;
            case ButtonType.Success: return ButtonSuccess;
            case ButtonType.Warning: return ButtonWarning;
            case ButtonType.Danger: return ButtonDanger;
            default: return ButtonPrimary;
        }
    }
    
    /// <summary>
    /// Get network color
    /// </summary>
    public static Color GetNetworkColor(bool isTestNet)
    {
        return MainNetGold; // Always MainNet
    }
    
    /// <summary>
    /// Get text color based on importance
    /// </summary>
    public static Color GetTextColor(TextImportance importance)
    {
        switch (importance)
        {
            case TextImportance.Primary: return TextPrimary;
            case TextImportance.Secondary: return TextSecondary;
            case TextImportance.Muted: return TextMuted;
            case TextImportance.White: return TextWhite;
            default: return TextPrimary;
        }
    }
}

public enum ButtonType
{
    Primary,
    Secondary,
    Success,
    Warning,
    Danger
}

public enum TextImportance
{
    Primary,
    Secondary,
    Muted,
    White
}
