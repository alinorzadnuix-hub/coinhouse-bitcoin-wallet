using UnityEngine;
using BitcoinSDK.Core;
using BitcoinSDK.Models;
using System.Collections.Generic;

/// <summary>
/// Example usage of Bitcoin Wallet SDK
/// </summary>
public class BitcoinSDKExample : MonoBehaviour
{
    private BitcoinWalletSDK sdk;

    async void Start()
    {
        // Initialize SDK with MainNet (default) or TestNet
        sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.MainNet);
        
        // Example 1: Create new wallet
        await Example_CreateNewWallet();
        
        // Example 2: Restore wallet from seed
        // await Example_RestoreWallet();
        
        // Example 3: Import from private key
        // await Example_ImportPrivateKey();
        
        // Example 4: Get balance
        // await Example_GetBalance();
        
        // Example 5: Send Bitcoin
        // await Example_SendBitcoin();
        
        // Example 6: Get transaction history
        // await Example_GetTransactionHistory();
    }

    async System.Threading.Tasks.Task Example_CreateNewWallet()
    {
        Debug.Log("=== Creating New Wallet ===");
        
        WalletInfo wallet = sdk.CreateNewWallet();
        
        Debug.Log($"Seed Phrase: {wallet.seedPhrase}");
        Debug.Log($"Address: {wallet.address}");
        Debug.Log($"Network: {wallet.network}");
        Debug.Log($"Created At: {wallet.createdAt}");
        
        // IMPORTANT: Save seed phrase securely!
        // PlayerPrefs.SetString("wallet_seed", wallet.seedPhrase);
    }

    async System.Threading.Tasks.Task Example_RestoreWallet()
    {
        Debug.Log("=== Restoring Wallet ===");
        
        string seedPhrase = "your twelve word seed phrase goes here like this example phrase";
        
        try
        {
            WalletInfo wallet = sdk.RestoreWalletFromSeed(seedPhrase);
            Debug.Log($"Wallet Restored! Address: {wallet.address}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to restore wallet: {ex.Message}");
        }
    }

    async System.Threading.Tasks.Task Example_ImportPrivateKey()
    {
        Debug.Log("=== Importing Private Key ===");
        
        string privateKey = "L1234567890abcdefghijklmnopqrstuvwxyz"; // WIF format
        // OR hex format: "0123456789abcdef..."
        
        try
        {
            WalletInfo wallet = sdk.ImportFromPrivateKey(privateKey);
            Debug.Log($"Wallet Imported! Address: {wallet.address}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to import: {ex.Message}");
        }
    }

    async System.Threading.Tasks.Task Example_GetBalance()
    {
        Debug.Log("=== Getting Balance ===");
        
        string address = "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh";
        
        try
        {
            decimal balance = await sdk.GetBalance(address);
            Debug.Log($"Balance: {balance} BTC");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to get balance: {ex.Message}");
        }
    }

    async System.Threading.Tasks.Task Example_GetBatchBalances()
    {
        Debug.Log("=== Getting Batch Balances ===");
        
        List<string> addresses = new List<string>
        {
            "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh",
            "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"
        };
        
        try
        {
            var balances = await sdk.GetBatchBalances(addresses);
            
            foreach (var kvp in balances)
            {
                Debug.Log($"Address: {kvp.Key} - Balance: {kvp.Value} BTC");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to get balances: {ex.Message}");
        }
    }

    async System.Threading.Tasks.Task Example_ScanWallet()
    {
        Debug.Log("=== Scanning Wallet ===");
        
        string seedPhrase = "your twelve word seed phrase goes here like this example phrase";
        
        try
        {
            BitcoinSDK.Models.ScanResult result = await sdk.ScanWalletBalance(seedPhrase, (status, current, total) =>
            {
                Debug.Log($"Progress: {status} ({current}/{total})");
            });
            
            Debug.Log($"Total Balance: {result.confirmedBalance} BTC");
            Debug.Log($"Unconfirmed: {result.unconfirmedBalance} BTC");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to scan wallet: {ex.Message}");
        }
    }

    async System.Threading.Tasks.Task Example_SendBitcoin()
    {
        Debug.Log("=== Sending Bitcoin ===");
        
        string seedPhrase = "your twelve word seed phrase goes here like this example phrase";
        string toAddress = "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh";
        decimal amount = 0.001m; // BTC
        decimal fee = 0.00001m; // BTC
        
        try
        {
            string txHash = await sdk.SendBitcoin(
                seedPhrase,
                toAddress,
                amount,
                fee,
                (status) => Debug.Log($"Status: {status}")
            );
            
            Debug.Log($"Transaction sent! TX Hash: {txHash}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to send: {ex.Message}");
        }
    }

    async System.Threading.Tasks.Task Example_GetTransactionHistory()
    {
        Debug.Log("=== Getting Transaction History ===");
        
        string address = "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh";
        
        try
        {
            List<Transaction> transactions = await sdk.GetTransactionHistory(address);
            
            Debug.Log($"Found {transactions.Count} transactions");
            
            foreach (var tx in transactions)
            {
                Debug.Log($"Type: {tx.type}");
                Debug.Log($"Amount: {tx.amount} BTC");
                Debug.Log($"Date: {tx.date}");
                Debug.Log($"Other Address: {tx.otherAddress}");
                Debug.Log($"TX Hash: {tx.txHash}");
                Debug.Log("---");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to get history: {ex.Message}");
        }
    }

    async System.Threading.Tasks.Task Example_GenerateAddresses()
    {
        Debug.Log("=== Generating Addresses ===");
        
        string seedPhrase = "your twelve word seed phrase goes here like this example phrase";
        
        // Generate address set (Legacy, SegWit, P2SH-SegWit)
        AddressSet addressSet = sdk.GenerateAddressSet(seedPhrase, index: 0);
        
        Debug.Log($"Legacy: {addressSet.legacy}");
        Debug.Log($"SegWit: {addressSet.segwit}");
        Debug.Log($"P2SH-SegWit: {addressSet.p2shSegwit}");
        
        // Generate custom address
        string customAddress = sdk.GenerateAddress(
            seedPhrase,
            "m/84'/0'/0'/0/5", // Custom derivation path
            NBitcoin.ScriptPubKeyType.Segwit
        );
        
        Debug.Log($"Custom Address: {customAddress}");
    }

    void Example_Validation()
    {
        Debug.Log("=== Validation Examples ===");
        
        // Validate address
        bool isValid = sdk.ValidateAddress("bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh");
        Debug.Log($"Address valid: {isValid}");
        
        // Validate seed phrase
        bool isSeedValid = sdk.ValidateSeedPhrase("abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about");
        Debug.Log($"Seed valid: {isSeedValid}");
        
        // Get network type
        string network = sdk.GetNetworkType();
        Debug.Log($"Current network: {network}");
    }

    void Example_Utilities()
    {
        Debug.Log("=== Utility Examples ===");
        
        // Convert BTC to Satoshi
        long satoshi = BitcoinWalletSDK.BTCToSatoshi(0.001m);
        Debug.Log($"0.001 BTC = {satoshi} satoshi");
        
        // Convert Satoshi to BTC
        decimal btc = BitcoinWalletSDK.SatoshiToBTC(100000);
        Debug.Log($"100000 satoshi = {btc} BTC");
    }
}
