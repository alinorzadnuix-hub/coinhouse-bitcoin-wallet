# TestNet Guide - Bitcoin Wallet SDK

## What is TestNet?

TestNet is a separate Bitcoin blockchain used for testing. It works exactly like MainNet but:
- ✅ Coins have no real value
- ✅ Free coins from faucets
- ✅ Safe for development
- ✅ Same features as MainNet
- ✅ No risk of losing money

## Why Use TestNet?

**ALWAYS test on TestNet before using MainNet!**

- Test your application safely
- Learn how Bitcoin works
- Debug issues without risk
- Verify transaction logic
- Practice wallet management

## Setting Up TestNet

### 1. Initialize SDK with TestNet

```csharp
using BitcoinSDK.Core;

// Create TestNet SDK instance
var sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.TestNet);
```

### 2. Create TestNet Wallet

```csharp
WalletInfo wallet = sdk.CreateNewWallet();

Debug.Log($"TestNet Address: {wallet.address}");
Debug.Log($"Seed Phrase: {wallet.seedPhrase}");

// Save for later use
PlayerPrefs.SetString("testnet_seed", wallet.seedPhrase);
PlayerPrefs.SetString("testnet_address", wallet.address);
```

### 3. Get Free TestNet Coins

Your TestNet address will start with:
- `tb1...` (SegWit)
- `m...` or `n...` (Legacy)
- `2...` (P2SH)

Visit these faucets to get free TestNet coins:

**Recommended Faucets:**
1. **Mempool TestNet Faucet**
   - URL: https://testnet-faucet.mempool.co/
   - Amount: 0.01 BTC
   - Fast delivery

2. **Coinfaucet.eu**
   - URL: https://coinfaucet.eu/en/btc-testnet/
   - Amount: 0.001-0.01 BTC
   - Reliable

3. **Bitcoin TestNet Sandbox**
   - URL: https://bitcoinfaucet.uo1.net/
   - Amount: 0.001 BTC
   - Simple interface

**How to use faucets:**
1. Copy your TestNet address
2. Visit faucet website
3. Paste your address
4. Complete captcha (if required)
5. Click "Send"
6. Wait 10-30 minutes for confirmation

### 4. Check Balance

```csharp
async void CheckTestNetBalance()
{
    string address = PlayerPrefs.GetString("testnet_address");
    
    try
    {
        decimal balance = await sdk.GetBalance(address);
        Debug.Log($"TestNet Balance: {balance} BTC");
        
        if (balance == 0)
        {
            Debug.Log("No balance yet. Visit a faucet to get free TestNet coins!");
        }
    }
    catch (Exception ex)
    {
        Debug.LogError($"Error: {ex.Message}");
    }
}
```

## Complete TestNet Example

```csharp
using UnityEngine;
using BitcoinSDK.Core;
using BitcoinSDK.Models;
using System.Collections.Generic;

public class TestNetExample : MonoBehaviour
{
    private BitcoinWalletSDK sdk;
    private string testnetAddress;
    private string testnetSeed;
    
    async void Start()
    {
        // Initialize TestNet SDK
        sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.TestNet);
        
        // Create or load wallet
        if (PlayerPrefs.HasKey("testnet_seed"))
        {
            // Load existing wallet
            testnetSeed = PlayerPrefs.GetString("testnet_seed");
            WalletInfo wallet = sdk.RestoreWalletFromSeed(testnetSeed);
            testnetAddress = wallet.address;
            Debug.Log($"Loaded TestNet wallet: {testnetAddress}");
        }
        else
        {
            // Create new wallet
            WalletInfo wallet = sdk.CreateNewWallet();
            testnetAddress = wallet.address;
            testnetSeed = wallet.seedPhrase;
            
            // Save wallet
            PlayerPrefs.SetString("testnet_seed", testnetSeed);
            PlayerPrefs.SetString("testnet_address", testnetAddress);
            PlayerPrefs.Save();
            
            Debug.Log("=== NEW TESTNET WALLET ===");
            Debug.Log($"Address: {testnetAddress}");
            Debug.Log($"Seed: {testnetSeed}");
            Debug.Log("Get free coins from: https://testnet-faucet.mempool.co/");
        }
        
        // Check balance
        await CheckBalance();
        
        // Get transaction history
        await GetHistory();
    }
    
    async System.Threading.Tasks.Task CheckBalance()
    {
        Debug.Log("Checking balance...");
        
        try
        {
            decimal balance = await sdk.GetBalance(testnetAddress);
            Debug.Log($"Balance: {balance} BTC");
            
            if (balance == 0)
            {
                Debug.Log("⚠️ No balance. Get free TestNet coins from a faucet!");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Balance check failed: {ex.Message}");
        }
    }
    
    async System.Threading.Tasks.Task GetHistory()
    {
        Debug.Log("Fetching transaction history...");
        
        try
        {
            List<Transaction> txs = await sdk.GetTransactionHistory(testnetAddress);
            
            if (txs.Count == 0)
            {
                Debug.Log("No transactions yet.");
            }
            else
            {
                Debug.Log($"Found {txs.Count} transactions:");
                foreach (var tx in txs)
                {
                    Debug.Log($"- {tx.type}: {tx.amount} BTC on {tx.date}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"History fetch failed: {ex.Message}");
        }
    }
    
    async System.Threading.Tasks.Task SendTestNetBitcoin()
    {
        // Example: Send TestNet Bitcoin
        string recipientAddress = "tb1qw508d6qejxtdg4y5r3zarvary0c5xw7kxpjzsx"; // Example TestNet address
        decimal amount = 0.0001m; // 0.0001 BTC
        decimal fee = 0.00001m; // 0.00001 BTC
        
        Debug.Log($"Sending {amount} BTC to {recipientAddress}...");
        
        try
        {
            string txHash = await sdk.SendBitcoin(
                testnetSeed,
                recipientAddress,
                amount,
                fee,
                (status) => Debug.Log($"Status: {status}")
            );
            
            Debug.Log($"✅ Transaction sent!");
            Debug.Log($"TX Hash: {txHash}");
            Debug.Log($"View on explorer: https://blockstream.info/testnet/tx/{txHash}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Send failed: {ex.Message}");
        }
    }
}
```

## TestNet vs MainNet Differences

| Feature | TestNet | MainNet |
|---------|---------|---------|
| **Coins** | No value | Real money |
| **Faucets** | Free coins | No faucets |
| **Risk** | Zero risk | Financial risk |
| **Purpose** | Testing | Production |
| **Address Prefix** | tb1, m, n, 2 | bc1, 1, 3 |
| **Block Time** | ~10 minutes | ~10 minutes |
| **Confirmations** | Same as MainNet | Same as TestNet |

## TestNet Blockchain Explorers

View your transactions and addresses:

1. **Blockstream TestNet Explorer**
   - URL: https://blockstream.info/testnet/
   - Best UI
   - Real-time updates

2. **Mempool.space TestNet**
   - URL: https://mempool.space/testnet/
   - Detailed stats
   - Fee estimates

3. **BlockCypher TestNet**
   - URL: https://live.blockcypher.com/btc-testnet/
   - API integration
   - Developer friendly

**How to use:**
- Paste your address or transaction hash
- View balance, transactions, UTXOs
- Check confirmation status

## Testing Checklist

Before moving to MainNet, test these on TestNet:

- [ ] Create new wallet
- [ ] Restore wallet from seed
- [ ] Import private key
- [ ] Generate multiple addresses
- [ ] Check balance
- [ ] Receive coins from faucet
- [ ] Send small amount
- [ ] Send with different fees
- [ ] View transaction history
- [ ] Handle errors gracefully
- [ ] Test batch operations
- [ ] Validate addresses
- [ ] Test with multiple wallets

## Common TestNet Issues

### "Balance is 0"
**Solution:** Get coins from a faucet. Wait 10-30 minutes for confirmation.

### "Transaction not confirming"
**Solution:** TestNet can be slow. Wait longer or use higher fee.

### "Faucet not working"
**Solution:** Try a different faucet. Some have daily limits.

### "Can't send transaction"
**Solution:** Ensure you have confirmed balance and sufficient fee.

## TestNet Best Practices

1. **Always start with TestNet**
   - Never test on MainNet first
   - Verify all features work

2. **Use realistic amounts**
   - Test with amounts similar to MainNet usage
   - Test edge cases (very small, very large)

3. **Test error handling**
   - Invalid addresses
   - Insufficient balance
   - Network errors

4. **Test different scenarios**
   - Multiple transactions
   - Different address types
   - Batch operations

5. **Keep TestNet coins**
   - Don't send all coins away
   - Keep some for future testing

## Switching to MainNet

When ready to switch to MainNet:

```csharp
// Change this line:
var sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.TestNet);

// To this:
var sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.MainNet);
```

**⚠️ IMPORTANT BEFORE SWITCHING:**
- [ ] All features tested on TestNet
- [ ] Error handling implemented
- [ ] Security measures in place
- [ ] Seed phrase encryption working
- [ ] User confirmations for transactions
- [ ] Backup and recovery tested
- [ ] Code reviewed
- [ ] Team approval obtained

## TestNet Resources

**Faucets:**
- https://testnet-faucet.mempool.co/
- https://coinfaucet.eu/en/btc-testnet/
- https://bitcoinfaucet.uo1.net/

**Explorers:**
- https://blockstream.info/testnet/
- https://mempool.space/testnet/
- https://live.blockcypher.com/btc-testnet/

**Documentation:**
- https://en.bitcoin.it/wiki/Testnet
- https://developer.bitcoin.org/examples/testing.html

## Example: Complete TestNet Workflow

```csharp
// 1. Create TestNet wallet
var sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.TestNet);
WalletInfo wallet = sdk.CreateNewWallet();
Debug.Log($"Address: {wallet.address}");

// 2. Get free coins from faucet (manual step)
// Visit: https://testnet-faucet.mempool.co/
// Paste your address and request coins

// 3. Wait for confirmation (10-30 minutes)
await Task.Delay(TimeSpan.FromMinutes(15));

// 4. Check balance
decimal balance = await sdk.GetBalance(wallet.address);
Debug.Log($"Balance: {balance} BTC");

// 5. Send test transaction
string txHash = await sdk.SendBitcoin(
    wallet.seedPhrase,
    "tb1qw508d6qejxtdg4y5r3zarvary0c5xw7kxpjzsx",
    0.0001m,
    0.00001m
);
Debug.Log($"Sent! TX: {txHash}");

// 6. View on explorer
Debug.Log($"https://blockstream.info/testnet/tx/{txHash}");

// 7. Check history
List<Transaction> txs = await sdk.GetTransactionHistory(wallet.address);
foreach (var tx in txs)
{
    Debug.Log($"{tx.type}: {tx.amount} BTC");
}
```

## Safety Reminders

✅ **TestNet is safe** - No real money involved

✅ **Test everything** - All features, all scenarios

✅ **Take your time** - Don't rush to MainNet

⚠️ **Never use TestNet seeds on MainNet** - Create new wallets for MainNet

⚠️ **Don't skip testing** - It prevents costly mistakes

---

**Happy Testing! 🧪**

Remember: Better safe than sorry. Test thoroughly on TestNet before going live!
