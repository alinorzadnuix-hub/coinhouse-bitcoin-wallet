using System;
using System.Net;

/// <summary>
/// Pure C# implementation of ipcrypt2 (Wider-Wipe Feistel cipher for IPv4).
/// Cross-platform compatible with Android (arm64-v8a, armeabi-v7a), Windows, iOS, and WebGL without native plugins.
/// </summary>
public static class IpCrypt2
{
    // Default 16-byte key if none is provided
    private static readonly byte[] DefaultKey = new byte[16] {
        0x01, 0x23, 0x45, 0x67, 0x89, 0xab, 0xcd, 0xef,
        0xfe, 0xdc, 0xba, 0x98, 0x76, 0x54, 0x32, 0x10
    };

    /// <summary>
    /// Encrypts an IPv4 address using 4-round Feistel Wider-Wipe cipher.
    /// </summary>
    public static IPAddress Encrypt(IPAddress ip, byte[] key = null)
    {
        if (ip == null) throw new ArgumentNullException("ip");
        if (key == null) key = DefaultKey;
        if (key.Length != 16) throw new ArgumentException("Key must be 16 bytes.", "key");
        byte[] bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) throw new NotSupportedException("Only IPv4 is supported.");
        byte[] result = new byte[4];
        Array.Copy(bytes, result, 4);

        unchecked
        {
            for (int r = 0; r < 4; r++)
            {
                byte b0 = (byte)(result[0] ^ key[r * 4 + 0]);
                byte b1 = (byte)(result[1] ^ key[r * 4 + 1]);
                byte b2 = (byte)(result[2] ^ key[r * 4 + 2]);
                byte b3 = (byte)(result[3] ^ key[r * 4 + 3]);

                b0 = (byte)((b0 + b1) ^ (b1 << 1 | b1 >> 7));
                b2 = (byte)((b2 + b3) ^ (b3 << 2 | b3 >> 6));
                b1 = (byte)((b1 + b2) ^ (b2 << 3 | b2 >> 5));
                b3 = (byte)((b3 + b0) ^ (b0 << 4 | b0 >> 4));

                result[0] = b2;
                result[1] = b3;
                result[2] = b0;
                result[3] = b1;
            }
        }
        return new IPAddress(result);
    }

    /// <summary>
    /// Decrypts an encrypted IPv4 address using inverse 4-round Feistel Wider-Wipe cipher.
    /// </summary>
    public static IPAddress Decrypt(IPAddress encryptedIp, byte[] key = null)
    {
        if (encryptedIp == null) throw new ArgumentNullException("encryptedIp");
        if (key == null) key = DefaultKey;
        if (key.Length != 16) throw new ArgumentException("Key must be 16 bytes.", "key");
        byte[] bytes = encryptedIp.GetAddressBytes();
        if (bytes.Length != 4) throw new NotSupportedException("Only IPv4 is supported.");
        byte[] result = new byte[4];
        Array.Copy(bytes, result, 4);

        unchecked
        {
            for (int r = 3; r >= 0; r--)
            {
                byte b0 = result[2];
                byte b1 = result[3];
                byte b2 = result[0];
                byte b3 = result[1];

                b3 = (byte)((b3 ^ (b0 << 4 | b0 >> 4)) - b0);
                b1 = (byte)((b1 ^ (b2 << 3 | b2 >> 5)) - b2);
                b2 = (byte)((b2 ^ (b3 << 2 | b3 >> 6)) - b3);
                b0 = (byte)((b0 ^ (b1 << 1 | b1 >> 7)) - b1);

                result[0] = (byte)(b0 ^ key[r * 4 + 0]);
                result[1] = (byte)(b1 ^ key[r * 4 + 1]);
                result[2] = (byte)(b2 ^ key[r * 4 + 2]);
                result[3] = (byte)(b3 ^ key[r * 4 + 3]);
            }
        }
        return new IPAddress(result);
    }

    /// <summary>
    /// Encrypts an IPv4 string representation (e.g. "192.168.1.1").
    /// Returns the original string if parsing fails.
    /// </summary>
    public static string Encrypt(string ipAddressString, byte[] key = null)
    {
        if (string.IsNullOrEmpty(ipAddressString)) return ipAddressString;
        IPAddress ip;
        if (IPAddress.TryParse(ipAddressString, out ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return Encrypt(ip, key).ToString();
        }
        return ipAddressString;
    }

    /// <summary>
    /// Decrypts an encrypted IPv4 string representation.
    /// Returns the original string if parsing fails.
    /// </summary>
    public static string Decrypt(string encryptedIpString, byte[] key = null)
    {
        if (string.IsNullOrEmpty(encryptedIpString)) return encryptedIpString;
        IPAddress ip;
        if (IPAddress.TryParse(encryptedIpString, out ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return Decrypt(ip, key).ToString();
        }
        return encryptedIpString;
    }
}
