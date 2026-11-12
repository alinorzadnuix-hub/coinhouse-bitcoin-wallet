# Bitcoin Wallet SDK for Unity

A complete, production-ready Bitcoin wallet SDK for Unity with MainNet and TestNet support.

## 🚀 Features

- ✅ **Wallet Management**: Create, restore, and import wallets
- ✅ **Multiple Address Types**: Legacy, SegWit, P2SH-SegWit
- ✅ **Balance Checking**: Single and batch operations
- ✅ **Transaction History**: Full transaction tracking
- ✅ **Send Bitcoin**: Secure transaction broadcasting
- ✅ **MainNet & TestNet**: Full support for both networks
- ✅ **Address Validation**: Built-in validation utilities
- ✅ **Batch Operations**: Efficient API usage
- ✅ **Well Documented**: Complete API reference and examples

## 📦 What's Included

```
BitcoinSDK/
├── Core/
│   ├── BitcoinWalletSDK.cs          # Main SDK class
│   └── BitcoinTransactionService.cs  # Transaction handling
├── API/
│   └── BlockchainAPI.cs              # Blockchain API wrapper
├── Models/
│   └── SDKModels.cs                  # Data models
├── Utils/
│   └── SDKUtils.cs                   # Utility helpers
├── Documentation/
│   ├── README.md                     # Full API documentation
│   └── QUICKSTART.md                 # Quick start guide
└── BitcoinSDKExample.cs              # Example usage
```

## ⚡ Quick Start

```csharp
using BitcoinSDK.Core;

// Initialize SDK
var sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.TestNet);

// Create wallet
var wallet = sdk.CreateNewWallet();
Debug.Log($"Address: {wallet.address}");

// Get balance
decimal balance = await sdk.GetBalance(wallet.address);
Debug.Log($"Balance: {balance} BTC");
```

## 📚 Documentation

- **[Quick Start Guide](Documentation/QUICKSTART.md)** - Get started in 5 minutes
- **[Full API Reference](Documentation/README.md)** - Complete documentation
- **[Example Code](BitcoinSDKExample.cs)** - Working examples

## 🔧 Requirements

- Unity 2019.4 or higher
- .NET 4.x or higher
- NBitcoin library
- Newtonsoft.Json library

## 📖 Basic Usage

### Create New Wallet
```csharp
WalletInfo wallet = sdk.CreateNewWallet();
// Save wallet.seedPhrase securely!
```

### Restore Wallet
```csharp
WalletInfo wallet = sdk.RestoreWalletFromSeed("your 12 word seed phrase");
```

### Check Balance
```csharp
decimal balance = await sdk.GetBalance("bc1q...");
```

### Send Bitcoin
```csharp
string txHash = await sdk.SendBitcoin(
    seedPhrase,
    "bc1qrecipient...",
    0.001m,  // amount
    0.00001m // fee
);
```

### Get Transaction History
```csharp
List<Transaction> txs = await sdk.GetTransactionHistory("bc1q...");
```

## 🔐 Security Best Practices

⚠️ **CRITICAL SECURITY NOTES:**

1. **Never store seed phrases in plain text**
2. **Always encrypt sensitive data**
3. **Test on TestNet first**
4. **Validate all user inputs**
5. **Use secure random number generation**

## 🌐 Network Support

### TestNet (Development)
```csharp
var sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.TestNet);
```
- Free test coins from faucets
- Safe for development
- No real money

### MainNet (Production)
```csharp
var sdk = new BitcoinWalletSDK(BitcoinWalletSDK.NetworkType.MainNet);
```
- Real Bitcoin network
- Real money - be careful!
- Use only after thorough testing

## 🎯 Key Features

### Wallet Creation
- Generate new wallets with BIP39 seed phrases
- 12-word mnemonic generation
- Automatic address derivation

### Wallet Recovery
- Restore from seed phrase
- Import from private key (WIF/Hex)
- Support for multiple derivation paths

### Address Management
- Legacy addresses (P2PKH)
- SegWit addresses (Bech32)
- P2SH-SegWit addresses
- Custom derivation paths

### Transaction Features
- Send Bitcoin with custom fees
- Automatic UTXO selection
- Change address handling
- Transaction signing and broadcasting

### Balance & History
- Single address balance
- Batch balance checking
- Transaction history
- Batch transaction fetching

## 📊 API Overview

| Feature | Method | Description |
|---------|--------|-------------|
| Create Wallet | `CreateNewWallet()` | Generate new wallet |
| Restore Wallet | `RestoreWalletFromSeed()` | Restore from seed |
| Import Key | `ImportFromPrivateKey()` | Import private key |
| Get Balance | `GetBalance()` | Check address balance |
| Batch Balance | `GetBatchBalances()` | Check multiple balances |
| Send BTC | `SendBitcoin()` | Send transaction |
| Get History | `GetTransactionHistory()` | Fetch transactions |
| Validate Address | `ValidateAddress()` | Validate BTC address |
| Generate Address | `GenerateAddress()` | Create new address |

## 🔄 Batch Operations

Efficient batch operations to reduce API calls:

```csharp
// Batch balance check
List<string> addresses = new List<string> { addr1, addr2, addr3 };
var balances = await sdk.GetBatchBalances(addresses);

// Batch transaction history
var allTxs = await sdk.GetBatchTransactionHistory(addresses);
```

## 🛠️ Utilities

```csharp
// Convert BTC to Satoshi
long sats = BitcoinWalletSDK.BTCToSatoshi(0.001m);

// Convert Satoshi to BTC
decimal btc = BitcoinWalletSDK.SatoshiToBTC(100000);

// Validate address
bool isValid = sdk.ValidateAddress("bc1q...");

// Get network type
string network = sdk.GetNetworkType();
```

## 📱 Example Application

See `BitcoinSDKExample.cs` for a complete working example including:
- Wallet creation and restoration
- Balance checking
- Transaction sending
- History fetching
- Address generation
- Validation

## 🐛 Troubleshooting

### Common Issues

**"Invalid seed phrase"**
- Check for typos
- Ensure 12 words exactly
- Verify BIP39 wordlist

**"No UTXOs found"**
- Wallet has no balance
- Wait for confirmations
- Check correct network

**"Rate limit exceeded"**
- SDK auto-handles rate limiting
- Use batch operations
- Wait a few minutes

## 📈 Performance Tips

1. **Use batch operations** for multiple addresses
2. **Cache balances** and refresh periodically
3. **Implement pagination** for transaction history
4. **Use async/await** properly
5. **Handle rate limits** gracefully

## 🔗 Blockchain API

The SDK uses BlockCypher API:
- Free tier: 200 requests/hour
- Automatic rate limiting (1.5s delay)
- Supports MainNet and TestNet

## 📄 License

[Your License Here]

## 🤝 Contributing

Contributions are welcome! Please:
1. Test on TestNet first
2. Follow existing code style
3. Add documentation
4. Include examples

## 📞 Support

- Documentation: See `Documentation/` folder
- Examples: See `BitcoinSDKExample.cs`
- Issues: [Your GitHub Issues URL]

## ⚠️ Disclaimer

This SDK is provided as-is. Always:
- Test thoroughly on TestNet
- Implement proper security measures
- Encrypt sensitive data
- Follow best practices
- Use at your own risk

## 🎓 Learning Resources

- [Bitcoin Developer Guide](https://bitcoin.org/en/developer-guide)
- [BIP39 Specification](https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki)
- [BIP44 Specification](https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki)
- [NBitcoin Documentation](https://github.com/MetacoSA/NBitcoin)

## 🚀 Getting Started

1. **Read** the [Quick Start Guide](Documentation/QUICKSTART.md)
2. **Review** the [API Reference](Documentation/README.md)
3. **Study** the example code in `BitcoinSDKExample.cs`
4. **Test** on TestNet
5. **Build** your application
6. **Deploy** with confidence

---

**Made with ❤️ for Unity developers**

**Version 1.0.0**
