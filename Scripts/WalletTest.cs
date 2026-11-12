using UnityEngine;

namespace UnityBitcoinWallet
{
    public class WalletTest : MonoBehaviour
    {
        private WalletManager walletManager;

        void Start()
        {
            walletManager = gameObject.AddComponent<WalletManager>();
            
            // تست خودکار
            TestWallet();
        }

        void TestWallet()
        {
            Debug.Log("=== شروع تست کیف پول ===");

            // 1. تولید کیف پول جدید
            Debug.Log("\n1. تولید کیف پول جدید...");
            walletManager.GenerateNewWallet(isMainnet: false); // TestNet

            // 2. دریافت اطلاعات
            string address = walletManager.GetAddress(0);
            string privateKey = walletManager.GetPrivateKey(0);
            string seedPhrase = walletManager.GetSeedPhrase();

            Debug.Log($"آدرس: {address}");
            Debug.Log($"کلید خصوصی: {privateKey}");
            Debug.Log($"Seed Phrase: {seedPhrase}");

            // 3. تست بازیابی
            Debug.Log("\n2. تست بازیابی کیف پول...");
            walletManager.RecoverWallet(seedPhrase, isMainnet: false);
            
            string recoveredAddress = walletManager.GetAddress(0);
            Debug.Log($"آدرس بازیابی شده: {recoveredAddress}");

            if (address == recoveredAddress)
            {
                Debug.Log("✅ تست موفق! آدرس ها یکسان هستند");
            }
            else
            {
                Debug.LogError("❌ خطا! آدرس ها متفاوت هستند");
            }

            Debug.Log("\n=== پایان تست ===");
        }

        // تست دستی با دکمه
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.G))
            {
                Debug.Log("تولید کیف پول جدید...");
                walletManager.GenerateNewWallet(false);
                Debug.Log($"آدرس: {walletManager.GetAddress(0)}");
            }

            if (Input.GetKeyDown(KeyCode.B))
            {
                CheckBalance();
            }
        }

        async void CheckBalance()
        {
            string address = walletManager.GetAddress(0);
            if (string.IsNullOrEmpty(address))
            {
                Debug.LogWarning("ابتدا کیف پول بسازید!");
                return;
            }

            Debug.Log($"چک کردن موجودی برای: {address}");
            try
            {
                decimal balance = await walletManager.GetBalance(address);
                Debug.Log($"💰 موجودی: {balance} BTC");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"خطا: {ex.Message}");
            }
        }
    }
}
