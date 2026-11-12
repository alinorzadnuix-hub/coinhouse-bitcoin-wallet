using System;
using System.Security.Cryptography;
using System.Text;
using NBitcoin.DataEncoders;

/// <summary>
/// Service to generate and verify 12-digit Satolite Wallet Serial numbers (prefix SN)
/// and compute their SHA-256 validation hashes, as well as managing the app build/version serial.
/// </summary>
public static class SatoliteSerialService
{
    private const string PREF_SERIAL_KEY = "Satolite_Install_Serial";

    /// <summary>
    /// Gets or creates a persistent 12-digit installation serial number for this build/installation.
    /// Example: SN566775576677
    /// </summary>
    public static string GetSerialNumber()
    {
        string sn = UnityEngine.PlayerPrefs.GetString(PREF_SERIAL_KEY, "");
        if (string.IsNullOrEmpty(sn))
        {
            sn = GenerateSerialNumber();
            UnityEngine.PlayerPrefs.SetString(PREF_SERIAL_KEY, sn);
            UnityEngine.PlayerPrefs.Save();
        }
        return sn;
    }

    /// <summary>
    /// Returns the formatted version and serial number for display in menus and footers.
    /// Example: "Satolite v1.0.0 • SN566775576677"
    /// </summary>
    public static string GetVersionSerialString()
    {
        string v = UnityEngine.Application.version;
        if (string.IsNullOrEmpty(v)) v = "1.0.0";
        return $"Satolite v{v} • {GetSerialNumber()}";
    }

    /// <summary>
    /// Returns the short version and serial number.
    /// Example: "v1.0.0 • SN566775576677"
    /// </summary>
    public static string GetShortVersionSerialString()
    {
        string v = UnityEngine.Application.version;
        if (string.IsNullOrEmpty(v)) v = "1.0.0";
        return $"v{v} • {GetSerialNumber()}";
    }

    /// <summary>
    /// Generates a 12-digit random serial number with 'SN' prefix: SN + 12 digits
    /// Example: SN566775576677
    /// </summary>
    public static string GenerateSerialNumber()
    {
        using (var rng = RandomNumberGenerator.Create())
        {
            byte[] bytes = new byte[8];
            rng.GetBytes(bytes);
            ulong val = BitConverter.ToUInt64(bytes, 0);
            // 12-digit number: 100,000,000,000 to 999,999,999,999
            ulong twelveDigits = (val % 900000000000UL) + 100000000000UL;
            return $"SN{twelveDigits}";
        }
    }

    /// <summary>
    /// Computes the SHA-256 hash of the serial number (kept secure/hidden on server).
    /// </summary>
    public static string ComputeSerialSha256(string serialNumber)
    {
        if (string.IsNullOrEmpty(serialNumber)) return "";
        using (var sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(serialNumber.Trim()));
            return Encoders.Hex.EncodeData(bytes).ToLowerInvariant();
        }
    }
}
