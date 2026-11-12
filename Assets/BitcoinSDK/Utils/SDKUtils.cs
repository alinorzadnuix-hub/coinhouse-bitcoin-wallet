using System;
using System.Text;

namespace BitcoinSDK.Utils
{
    /// <summary>
    /// Hex conversion utilities
    /// </summary>
    public static class HexHelper
    {
        public static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0)
                throw new ArgumentException("Hex string must have even length");

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        public static string BytesToHex(byte[] bytes)
        {
            StringBuilder hex = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                hex.AppendFormat("{0:x2}", b);
            }
            return hex.ToString();
        }
    }

    /// <summary>
    /// Address validation utilities
    /// </summary>
    public static class AddressValidator
    {
        public static bool IsValidBitcoinAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;
            
            // Basic validation
            if (address.Length < 26 || address.Length > 62) return false;
            
            // Check prefix
            if (address.StartsWith("1") || // Legacy
                address.StartsWith("3") || // P2SH
                address.StartsWith("bc1") || // SegWit MainNet
                address.StartsWith("tb1") || // SegWit TestNet
                address.StartsWith("m") || // TestNet Legacy
                address.StartsWith("n") || // TestNet Legacy
                address.StartsWith("2")) // TestNet P2SH
            {
                return true;
            }
            
            return false;
        }

        public static string GetAddressType(string address)
        {
            if (string.IsNullOrEmpty(address)) return "Unknown";
            
            if (address.StartsWith("1")) return "Legacy (P2PKH)";
            if (address.StartsWith("3")) return "P2SH";
            if (address.StartsWith("bc1")) return "SegWit (Bech32)";
            if (address.StartsWith("tb1")) return "TestNet SegWit";
            if (address.StartsWith("m") || address.StartsWith("n")) return "TestNet Legacy";
            if (address.StartsWith("2")) return "TestNet P2SH";
            
            return "Unknown";
        }
    }

    /// <summary>
    /// Amount formatting utilities
    /// </summary>
    public static class AmountFormatter
    {
        public static string FormatBTC(decimal amount, int decimals = 8)
        {
            return amount.ToString($"F{decimals}") + " BTC";
        }

        public static string FormatSatoshi(long satoshi)
        {
            return satoshi.ToString("N0") + " sats";
        }

        public static string FormatWithUnit(decimal btc, bool useSatoshi = false)
        {
            if (useSatoshi)
            {
                long sats = (long)(btc * 100000000m);
                return FormatSatoshi(sats);
            }
            return FormatBTC(btc);
        }
    }
}
