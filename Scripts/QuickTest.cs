using UnityEngine;
using NBitcoin;

public class QuickTest : MonoBehaviour
{
    void Start()
    {
        TestWallet();
    }

    void TestWallet()
    {
        Debug.Log("========================================");
        Debug.Log("BITCOIN WALLET TEST - STARTING");
        Debug.Log("========================================\n");

        // Create TestNet Wallet
        Debug.Log("Creating TestNet Wallet...\n");
        
        var wallet = new BitcoinWalletCore(NBitcoin.Network.TestNet);
        
        string address = wallet.GetAddress(0);
        string privateKey = wallet.GetPrivateKey(0);
        string seedPhrase = wallet.GetSeedPhrase();
        
        Debug.Log("✅ WALLET CREATED SUCCESSFULLY!\n");
        
        Debug.Log("📍 ADDRESS:");
        Debug.Log(address + "\n");
        
        Debug.Log("🔑 PRIVATE KEY:");
        Debug.Log(privateKey + "\n");
        
        Debug.Log("🌱 SEED PHRASE (12 words):");
        Debug.Log(seedPhrase + "\n");
        
        // Test Recovery
        Debug.Log("----------------------------------------");
        Debug.Log("Testing Wallet Recovery...\n");
        
        var recoveredWallet = new BitcoinWalletCore(seedPhrase, NBitcoin.Network.TestNet);
        string recoveredAddress = recoveredWallet.GetAddress(0);
        
        Debug.Log("Recovered Address:");
        Debug.Log(recoveredAddress + "\n");
        
        if (address == recoveredAddress)
        {
            Debug.Log("✅ RECOVERY TEST PASSED!");
            Debug.Log("Original and recovered addresses match!\n");
        }
        else
        {
            Debug.LogError("❌ RECOVERY TEST FAILED!");
        }
        
        Debug.Log("========================================");
        Debug.Log("TEST COMPLETED");
        Debug.Log("========================================");
        
        Debug.Log("\n💡 TIP: Copy the seed phrase to test recovery manually");
        Debug.Log("💡 TIP: Use the address to check balance on blockchain explorer");
    }

    void Update()
    {
        // Press G to generate new wallet
        if (Input.GetKeyDown(KeyCode.G))
        {
            Debug.Log("\n\n");
            TestWallet();
        }
        
        // Press M to generate Mainnet wallet
        if (Input.GetKeyDown(KeyCode.M))
        {
            Debug.Log("\n\n========================================");
            Debug.Log("MAINNET WALLET");
            Debug.Log("========================================\n");
            
            var wallet = new BitcoinWalletCore(NBitcoin.Network.Main);
            Debug.Log("📍 Address: " + wallet.GetAddress(1));
            Debug.Log("🔑 Private Key: " + wallet.GetPrivateKey(1));
            Debug.Log("🌱 Seed: " + wallet.GetSeedPhrase());
        }
    }
}
