# Bitcoin Wallet SDK - Quick Start Guide

## 5-Minute Setup

### Step 1: Import SDK
Copy the `BitcoinSDK` folder to your Unity project's `Assets` folder.

### Step 2: Create a Script
Create a new C# script in Unity and add this code:

```csharp
using UnityEngine;
using BitcoinSDK.Core;
using BitcoinSDK.Models;

public class MyBitcoinWallet : MonoBehaviour
{
    private BitcoinWalletSDK sdk;
    
    async void Start()
    {
        // Initialize SDK
        sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.TestNet);
        
        // Create new wallet
        WalletInfo wallet = sdk.CreateNewWallet();
        
        Debug.Log("=== NEW WALLET CREATED ===");
        Debug.Log($"Address: {wallet.address}");
        Debug.Log($"Seed Phrase: {wallet.seedPhrase}");
        Debug.Log("SAVE THIS SEED PHRASE SECURELY!");
        
        // Get balance
        decimal balance = await sdk.GetBalance(wallet.address);
        Debug.Log($"Balance: {balance} BTC");
    }
}
```

### Step 3: Attach Script
1. Create an empty GameObject in your scene
2. Attach the `MyBitcoinWallet` script to it
3. Press Play

### Step 4: See Results
Check the Unity Console to see your new wallet address and seed phrase!

---

## Common Tasks

### Create Wallet
```csharp
WalletInfo wallet = sdk.CreateNewWallet();
// Save wallet.seedPhrase securely!
```

### Restore Wallet
```csharp
string seed = "your twelve word seed phrase here";
WalletInfo wallet = sdk.RestoreWalletFromSeed(seed);
```

### Check Balance
```csharp
decimal balance = await sdk.GetBalance("bc1q...");
Debug.Log($"Balance: {balance} BTC");
```

### Send Bitcoin
```csharp
string txHash = await sdk.SendBitcoin(
    seedPhrase,
    "bc1qrecipientaddress...",
    0.001m,  // amount in BTC
    0.00001m // fee in BTC
);
Debug.Log($"Sent! TX: {txHash}");
```

### Get Transaction History
```csharp
List<Transaction> txs = await sdk.GetTransactionHistory("bc1q...");
foreach (var tx in txs)
{
    Debug.Log($"{tx.type}: {tx.amount} BTC");
}
```

---

## TestNet vs MainNet

### TestNet (for development)
```csharp
var sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.TestNet);
```
- Use for testing
- Free test coins from faucets
- No real money

### MainNet (for production)
```csharp
var sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.MainNet);
```
- Real Bitcoin network
- Real money - be careful!
- Use only after thorough testing

---

## Getting TestNet Coins

1. Create a TestNet wallet using the SDK
2. Copy your TestNet address (starts with `tb1` or `m` or `n`)
3. Visit a TestNet faucet:
   - https://testnet-faucet.mempool.co/
   - https://coinfaucet.eu/en/btc-testnet/
4. Paste your address and request coins
5. Wait 10-30 minutes for confirmation
6. Check balance with `sdk.GetBalance(address)`

---

## Important Security Notes

⚠️ **NEVER share your seed phrase with anyone!**

⚠️ **NEVER store seed phrases in plain text!**

⚠️ **ALWAYS test on TestNet first!**

✅ **DO encrypt seed phrases before saving**

✅ **DO validate addresses before sending**

✅ **DO use proper error handling**

---

## Next Steps

1. Read the full documentation: `Documentation/README.md`
2. Check example code: `BitcoinSDKExample.cs`
3. Test all features on TestNet
4. Implement proper encryption for seed phrases
5. Add UI for your wallet
6. Test thoroughly before using MainNet

---

## Need Help?

- Full API Reference: See `Documentation/README.md`
- Example Code: See `BitcoinSDKExample.cs`
- Common Issues: See Troubleshooting section in main docs

---

## Complete Example: Simple Wallet

```csharp
using UnityEngine;
using UnityEngine.UI;
using BitcoinSDK.Core;
using BitcoinSDK.Models;

public class SimpleBitcoinWallet : MonoBehaviour
{
    public Text addressText;
    public Text balanceText;
    public Button refreshButton;
    
    private BitcoinWalletSDK sdk;
    private string currentAddress;
    
    void Start()
    {
        // Initialize SDK (TestNet for safety)
        sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.TestNet);
        
        // Create or load wallet
        if (PlayerPrefs.HasKey("wallet_seed"))
        {
            // Load existing wallet
            string seed = PlayerPrefs.GetString("wallet_seed");
            WalletInfo wallet = sdk.RestoreWalletFromSeed(seed);
            currentAddress = wallet.address;
        }
        else
        {
            // Create new wallet
            WalletInfo wallet = sdk.CreateNewWallet();
            currentAddress = wallet.address;
            
            // Save seed (in production, encrypt this!)
            PlayerPrefs.SetString("wallet_seed", wallet.seedPhrase);
            PlayerPrefs.Save();
            
            Debug.Log($"NEW WALLET CREATED!");
            Debug.Log($"Seed: {wallet.seedPhrase}");
            Debug.Log("SAVE THIS SEED PHRASE!");
        }
        
        // Update UI
        addressText.text = $"Address: {currentAddress}";
        
        // Setup button
        refreshButton.onClick.AddListener(RefreshBalance);
        
        // Load initial balance
        RefreshBalance();
    }
    
    async void RefreshBalance()
    {
        balanceText.text = "Loading...";
        
        try
        {
            decimal balance = await sdk.GetBalance(currentAddress);
            balanceText.text = $"Balance: {balance:F8} BTC";
        }
        catch (System.Exception ex)
        {
            balanceText.text = $"Error: {ex.Message}";
        }
    }
}
```

---

Happy coding! 🚀
