# Bitcoin Wallet SDK for Unity

## Overview

A comprehensive Bitcoin wallet SDK for Unity that supports both MainNet and TestNet. This SDK provides all the essential features needed to build a Bitcoin wallet application in Unity.

## Features

- ✅ **Wallet Creation**: Generate new wallets with 12-word seed phrases
- ✅ **Wallet Recovery**: Restore wallets from seed phrases
- ✅ **Private Key Import**: Import wallets from WIF or Hex private keys
- ✅ **Multiple Address Types**: Support for Legacy, SegWit, and P2SH-SegWit addresses
- ✅ **Balance Checking**: Get balance for single or multiple addresses
- ✅ **Transaction History**: Fetch transaction history for addresses
- ✅ **Send Bitcoin**: Send Bitcoin transactions with custom fees
- ✅ **Address Generation**: Generate addresses with custom derivation paths
- ✅ **Validation**: Validate Bitcoin addresses and seed phrases
- ✅ **MainNet & TestNet**: Full support for both networks
- ✅ **Batch Operations**: Efficient batch balance and transaction fetching

## Requirements

- Unity 2019.4 or higher
- NBitcoin library (included)
- Newtonsoft.Json library (included)
- .NET 4.x or higher

## Installation

1. Copy the `BitcoinSDK` folder to your Unity project's `Assets` folder
2. Ensure NBitcoin and Newtonsoft.Json libraries are in your project
3. Set your project to use .NET 4.x (Edit → Project Settings → Player → Other Settings → Api Compatibility Level)

## Quick Start

```csharp
using BitcoinSDK.Core;
using BitcoinSDK.Models;

// Initialize SDK
BitcoinWalletSDK sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.MainNet);

// Create new wallet
WalletInfo wallet = sdk.CreateNewWallet();
Debug.Log($"Address: {wallet.address}");
Debug.Log($"Seed: {wallet.seedPhrase}");

// Get balance
decimal balance = await sdk.GetBalance(wallet.address);
Debug.Log($"Balance: {balance} BTC");
```

## API Reference

### Initialization

#### Constructor
```csharp
BitcoinWalletSDK(NetworkType networkType = NetworkType.MainNet)
```

**Parameters:**
- `networkType`: `NetworkType.MainNet` or `NetworkType.TestNet` (default: MainNet)

**Example:**
```csharp
// MainNet
var sdk = new BitcoinWalletSDK();

// TestNet
var sdkTest = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.TestNet);
```

---

### Wallet Creation

#### CreateNewWallet()
Generate a new wallet with a 12-word seed phrase.

```csharp
WalletInfo CreateNewWallet()
```

**Returns:** `WalletInfo` object containing:
- `seedPhrase`: 12-word mnemonic phrase
- `address`: Default SegWit address
- `network`: "MainNet" or "TestNet"
- `createdAt`: Creation timestamp

**Example:**
```csharp
WalletInfo wallet = sdk.CreateNewWallet();
Debug.Log($"Seed: {wallet.seedPhrase}");
Debug.Log($"Address: {wallet.address}");

// IMPORTANT: Save the seed phrase securely!
PlayerPrefs.SetString("wallet_seed", wallet.seedPhrase);
```

---

#### RestoreWalletFromSeed()
Restore an existing wallet from a seed phrase.

```csharp
WalletInfo RestoreWalletFromSeed(string seedPhrase)
```

**Parameters:**
- `seedPhrase`: 12-word mnemonic phrase (space-separated)

**Returns:** `WalletInfo` object

**Throws:** Exception if seed phrase is invalid

**Example:**
```csharp
string seed = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
WalletInfo wallet = sdk.RestoreWalletFromSeed(seed);
```

---

#### ImportFromPrivateKey()
Import a wallet from a private key (WIF or Hex format).

```csharp
WalletInfo ImportFromPrivateKey(string privateKey)
```

**Parameters:**
- `privateKey`: Private key in WIF format (starts with L, K, or 5) or Hex format

**Returns:** `WalletInfo` object

**Throws:** Exception if private key is invalid

**Example:**
```csharp
// WIF format
string wif = "L1234567890abcdefghijklmnopqrstuvwxyz";
WalletInfo wallet = sdk.ImportFromPrivateKey(wif);

// Hex format
string hex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
WalletInfo wallet2 = sdk.ImportFromPrivateKey(hex);
```

---

### Address Generation

#### GenerateAddress()
Generate a Bitcoin address with a custom derivation path.

```csharp
string GenerateAddress(string seedPhrase, string derivationPath, ScriptPubKeyType addressType = ScriptPubKeyType.Segwit)
```

**Parameters:**
- `seedPhrase`: 12-word mnemonic phrase
- `derivationPath`: BIP32/BIP44 derivation path (e.g., "m/84'/0'/0'/0/0")
- `addressType`: Address type (Legacy, Segwit, or SegwitP2SH)

**Returns:** Bitcoin address string

**Example:**
```csharp
string address = sdk.GenerateAddress(
    seedPhrase,
    "m/84'/0'/0'/0/5",
    ScriptPubKeyType.Segwit
);
```

---

#### GenerateAddressSet()
Generate all three address types (Legacy, SegWit, P2SH-SegWit) for a given index.

```csharp
AddressSet GenerateAddressSet(string seedPhrase, int index = 0)
```

**Parameters:**
- `seedPhrase`: 12-word mnemonic phrase
- `index`: Address index (default: 0)

**Returns:** `AddressSet` object containing:
- `legacy`: Legacy address (starts with 1)
- `segwit`: SegWit address (starts with bc1)
- `p2shSegwit`: P2SH-SegWit address (starts with 3)

**Example:**
```csharp
AddressSet addresses = sdk.GenerateAddressSet(seedPhrase, 0);
Debug.Log($"Legacy: {addresses.legacy}");
Debug.Log($"SegWit: {addresses.segwit}");
Debug.Log($"P2SH: {addresses.p2shSegwit}");
```

---

### Balance & Transactions

#### GetBalance()
Get the balance for a single Bitcoin address.

```csharp
async Task<decimal> GetBalance(string address)
```

**Parameters:**
- `address`: Bitcoin address

**Returns:** Balance in BTC (decimal)

**Example:**
```csharp
decimal balance = await sdk.GetBalance("bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh");
Debug.Log($"Balance: {balance} BTC");
```

---

#### GetBatchBalances()
Get balances for multiple addresses in a single API call (more efficient).

```csharp
async Task<Dictionary<string, decimal>> GetBatchBalances(List<string> addresses)
```

**Parameters:**
- `addresses`: List of Bitcoin addresses

**Returns:** Dictionary mapping addresses to balances (in BTC)

**Example:**
```csharp
List<string> addresses = new List<string> {
    "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh",
    "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"
};

var balances = await sdk.GetBatchBalances(addresses);
foreach (var kvp in balances)
{
    Debug.Log($"{kvp.Key}: {kvp.Value} BTC");
}
```

---

#### ScanWalletBalance()
Scan all derived addresses from a seed phrase and return total balance.

```csharp
async Task<ScanResult> ScanWalletBalance(string seedPhrase, Action<string, int, int> progressCallback = null)
```

**Parameters:**
- `seedPhrase`: 12-word mnemonic phrase
- `progressCallback`: Optional callback for progress updates (status, current, total)

**Returns:** `ScanResult` object containing:
- `confirmedBalance`: Confirmed balance in BTC
- `unconfirmedBalance`: Unconfirmed balance in BTC

**Example:**
```csharp
ScanResult result = await sdk.ScanWalletBalance(seedPhrase, (status, current, total) =>
{
    Debug.Log($"Scanning: {status} ({current}/{total})");
});

Debug.Log($"Total: {result.confirmedBalance} BTC");
```

---

#### GetTransactionHistory()
Get transaction history for a single address.

```csharp
async Task<List<Transaction>> GetTransactionHistory(string address)
```

**Parameters:**
- `address`: Bitcoin address

**Returns:** List of `Transaction` objects, each containing:
- `txHash`: Transaction hash
- `type`: "Send" or "Receive"
- `amount`: Amount in BTC
- `date`: Transaction date
- `otherAddress`: Sender/recipient address

**Example:**
```csharp
List<Transaction> txs = await sdk.GetTransactionHistory(address);

foreach (var tx in txs)
{
    Debug.Log($"{tx.type}: {tx.amount} BTC on {tx.date}");
    Debug.Log($"TX: {tx.txHash}");
}
```

---

#### GetBatchTransactionHistory()
Get transaction history for multiple addresses.

```csharp
async Task<List<Transaction>> GetBatchTransactionHistory(List<string> addresses)
```

**Parameters:**
- `addresses`: List of Bitcoin addresses

**Returns:** Combined list of `Transaction` objects (duplicates removed)

**Example:**
```csharp
List<string> addresses = new List<string> { addr1, addr2, addr3 };
List<Transaction> allTxs = await sdk.GetBatchTransactionHistory(addresses);
```

---

### Send Bitcoin

#### SendBitcoin()
Send Bitcoin from a wallet (seed phrase).

```csharp
async Task<string> SendBitcoin(
    string seedPhrase,
    string toAddress,
    decimal amount,
    decimal fee,
    Action<string> statusCallback = null,
    CancellationToken cancellationToken = default
)
```

**Parameters:**
- `seedPhrase`: 12-word mnemonic phrase
- `toAddress`: Recipient Bitcoin address
- `amount`: Amount to send in BTC
- `fee`: Transaction fee in BTC
- `statusCallback`: Optional callback for status updates
- `cancellationToken`: Optional cancellation token

**Returns:** Transaction hash (string)

**Throws:** Exception if transaction fails

**Example:**
```csharp
try
{
    string txHash = await sdk.SendBitcoin(
        seedPhrase,
        "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh",
        0.001m, // 0.001 BTC
        0.00001m, // 0.00001 BTC fee
        (status) => Debug.Log($"Status: {status}")
    );
    
    Debug.Log($"Success! TX: {txHash}");
}
catch (Exception ex)
{
    Debug.LogError($"Failed: {ex.Message}");
}
```

---

#### SendBitcoinFromPrivateKey()
Send Bitcoin from an imported private key.

```csharp
async Task<string> SendBitcoinFromPrivateKey(
    string privateKey,
    string toAddress,
    decimal amount,
    decimal fee,
    Action<string> statusCallback = null,
    CancellationToken cancellationToken = default
)
```

**Parameters:** Same as SendBitcoin(), but uses `privateKey` instead of `seedPhrase`

**Example:**
```csharp
string txHash = await sdk.SendBitcoinFromPrivateKey(
    privateKey,
    toAddress,
    0.001m,
    0.00001m
);
```

---

### Validation

#### ValidateAddress()
Check if a Bitcoin address is valid.

```csharp
bool ValidateAddress(string address)
```

**Parameters:**
- `address`: Bitcoin address to validate

**Returns:** `true` if valid, `false` otherwise

**Example:**
```csharp
bool isValid = sdk.ValidateAddress("bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh");
if (isValid)
{
    Debug.Log("Valid address!");
}
```

---

#### ValidateSeedPhrase()
Check if a seed phrase is valid.

```csharp
bool ValidateSeedPhrase(string seedPhrase)
```

**Parameters:**
- `seedPhrase`: Seed phrase to validate

**Returns:** `true` if valid, `false` otherwise

**Example:**
```csharp
bool isValid = sdk.ValidateSeedPhrase("abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about");
```

---

### Utilities

#### BTCToSatoshi()
Convert BTC to Satoshi.

```csharp
static long BTCToSatoshi(decimal btc)
```

**Example:**
```csharp
long sats = BitcoinWalletSDK.BTCToSatoshi(0.001m);
Debug.Log($"0.001 BTC = {sats} satoshi"); // 100000
```

---

#### SatoshiToBTC()
Convert Satoshi to BTC.

```csharp
static decimal SatoshiToBTC(long satoshi)
```

**Example:**
```csharp
decimal btc = BitcoinWalletSDK.SatoshiToBTC(100000);
Debug.Log($"100000 sats = {btc} BTC"); // 0.001
```

---

#### GetNetworkType()
Get the current network type.

```csharp
string GetNetworkType()
```

**Returns:** "MainNet" or "TestNet"

**Example:**
```csharp
string network = sdk.GetNetworkType();
Debug.Log($"Network: {network}");
```

---

## Data Models

### WalletInfo
```csharp
public class WalletInfo
{
    public string seedPhrase;
    public string privateKey;
    public string address;
    public string network;
    public DateTime createdAt;
}
```

### Transaction
```csharp
public class Transaction
{
    public string txHash;
    public string type; // "Send" or "Receive"
    public decimal amount;
    public string date;
    public string otherAddress;
}
```

### ScanResult
```csharp
public class ScanResult
{
    public decimal confirmedBalance;
    public decimal unconfirmedBalance;
}
```

### AddressSet
```csharp
public class AddressSet
{
    public string legacy;
    public string segwit;
    public string p2shSegwit;
}
```

---

## Best Practices

### Security

1. **Never store seed phrases in plain text**
   - Use encryption (e.g., AES-256)
   - Consider hardware security modules for production

2. **Validate user input**
   - Always validate addresses before sending
   - Check amounts and fees

3. **Use TestNet for development**
   - Test all features on TestNet first
   - Switch to MainNet only when ready

### Performance

1. **Use batch operations**
   - `GetBatchBalances()` instead of multiple `GetBalance()` calls
   - `GetBatchTransactionHistory()` for multiple addresses

2. **Implement caching**
   - Cache balances and transaction history
   - Refresh periodically or on user request

3. **Handle rate limits**
   - SDK includes automatic rate limiting (1.5s between API calls)
   - Consider implementing exponential backoff for errors

### Error Handling

```csharp
try
{
    decimal balance = await sdk.GetBalance(address);
}
catch (Exception ex)
{
    Debug.LogError($"Error: {ex.Message}");
    // Show user-friendly error message
}
```

---

## Common Use Cases

### 1. Create and Save Wallet
```csharp
WalletInfo wallet = sdk.CreateNewWallet();

// Encrypt seed phrase before saving
string encryptedSeed = EncryptSeed(wallet.seedPhrase);
PlayerPrefs.SetString("wallet_seed", encryptedSeed);
PlayerPrefs.SetString("wallet_address", wallet.address);
```

### 2. Load and Restore Wallet
```csharp
string encryptedSeed = PlayerPrefs.GetString("wallet_seed");
string seedPhrase = DecryptSeed(encryptedSeed);

WalletInfo wallet = sdk.RestoreWalletFromSeed(seedPhrase);
```

### 3. Check Balance and Update UI
```csharp
async void UpdateBalance()
{
    try
    {
        decimal balance = await sdk.GetBalance(currentAddress);
        balanceText.text = $"{balance:F8} BTC";
    }
    catch (Exception ex)
    {
        balanceText.text = "Error loading balance";
    }
}
```

### 4. Send Bitcoin with Confirmation
```csharp
async void SendBitcoinWithConfirmation()
{
    // Show confirmation dialog
    bool confirmed = await ShowConfirmationDialog(
        $"Send {amount} BTC to {toAddress}?"
    );
    
    if (!confirmed) return;
    
    try
    {
        string txHash = await sdk.SendBitcoin(
            seedPhrase,
            toAddress,
            amount,
            fee,
            (status) => statusText.text = status
        );
        
        ShowSuccessDialog($"Transaction sent!\nTX: {txHash}");
    }
    catch (Exception ex)
    {
        ShowErrorDialog($"Failed: {ex.Message}");
    }
}
```

---

## Troubleshooting

### "Invalid seed phrase" error
- Ensure seed phrase has exactly 12 words
- Check for typos or extra spaces
- Verify words are from BIP39 wordlist

### "No UTXOs found" error
- Wallet has no confirmed balance
- Wait for confirmations
- Check if you're on correct network (MainNet vs TestNet)

### "Rate limit exceeded" error
- SDK automatically handles rate limiting
- If error persists, wait a few minutes
- Consider using batch operations

### Transaction not confirming
- Check fee amount (too low fees = slow confirmation)
- View transaction on blockchain explorer
- Wait for network confirmations (typically 10-60 minutes)

---

## API Rate Limits

The SDK uses BlockCypher API with the following limits:
- Free tier: 200 requests/hour, 3 requests/second
- SDK includes automatic 1.5s delay between calls
- Use batch operations to reduce API calls

---

## Support

For issues, questions, or contributions:
- GitHub: [Your Repository URL]
- Email: [Your Email]
- Documentation: [Your Docs URL]

---

## License

[Your License Here]

---

## Changelog

### Version 1.0.0
- Initial release
- MainNet and TestNet support
- Wallet creation, restoration, and import
- Balance checking and transaction history
- Send Bitcoin functionality
- Batch operations
- Address validation

---

## Credits

Built with:
- NBitcoin library
- BlockCypher API
- Unity Engine
