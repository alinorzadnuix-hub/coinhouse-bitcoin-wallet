using System;
using System.Net;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== TESTING IPCRYPT2 ALGORITHM ===");

        string[] testIps = new string[] {
            "192.168.1.1",
            "10.0.0.1",
            "127.0.0.1",
            "8.8.8.8",
            "1.1.1.1",
            "0.0.0.0",
            "255.255.255.255",
            "172.16.254.1",
            "203.0.113.195"
        };

        int passed = 0;
        foreach (var ip in testIps)
        {
            string encrypted = IpCrypt2.Encrypt(ip);
            string decrypted = IpCrypt2.Decrypt(encrypted);

            if (decrypted == ip && encrypted != ip)
            {
                Console.WriteLine($"[PASS] {ip} -> Encrypted: {encrypted} -> Decrypted: {decrypted}");
                passed++;
            }
            else
            {
                Console.WriteLine($"[FAIL] {ip} -> Encrypted: {encrypted} -> Decrypted: {decrypted}");
            }
        }

        // Test with custom 16-byte key
        byte[] customKey = new byte[16] {
            0xaa, 0xbb, 0xcc, 0xdd, 0x11, 0x22, 0x33, 0x44,
            0x55, 0x66, 0x77, 0x88, 0x99, 0x00, 0xef, 0xab
        };

        string encCustom = IpCrypt2.Encrypt("192.168.1.100", customKey);
        string decCustom = IpCrypt2.Decrypt(encCustom, customKey);
        if (decCustom == "192.168.1.100")
        {
            Console.WriteLine($"[PASS] Custom key test passed: {encCustom} -> {decCustom}");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] Custom key test failed: {encCustom} -> {decCustom}");
        }

        Console.WriteLine($"\nIpCrypt2 Result: {passed}/{testIps.Length + 1} passed.");
    }
}
