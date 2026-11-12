# Bitcoin Wallet SDK - Structure Overview

## 📁 Directory Structure

```
BitcoinSDK/
│
├── 📂 Core/                              # Core SDK functionality
│   ├── BitcoinWalletSDK.cs              # Main SDK class - Your entry point
│   └── BitcoinTransactionService.cs      # Transaction handling service
│
├── 📂 API/                               # Blockchain API integration
│   └── BlockchainAPI.cs                  # BlockCypher API wrapper
│
├── 📂 Models/                            # Data models and structures
│   └── SDKModels.cs                      # WalletInfo, Transaction, etc.
│
├── 📂 Utils/                             # Utility helpers
│   └── SDKUtils.cs                       # HexHelper, AddressValidator, etc.
│
├── 📂 Documentation/                     # Complete documentation
│   ├── README.md                         # Full API reference
│   ├── QUICKSTART.md                     # 5-minute quick start
│   └── TESTNET_GUIDE.md                  # TestNet usage guide
│
├── 📄 BitcoinSDKExample.cs               # Example usage code
├── 📄 README.md                          # Main SDK readme
└── 📄 CHANGELOG.md                       # Version history
```

## 🎯 File Descriptions

### Core Files

#### `Core/BitcoinWalletSDK.cs`
**Purpose:** Main SDK class - your primary interface

**Key Features:**
- Wallet creation and restoration
- Balance checking
- Transaction sending
- Address generation
- Validation utilities

**Usage:**
```csharp
var sdk = new BitcoinWalletSDK(NetworkType.MainNet);
```

---

#### `Core/BitcoinTransactionService.cs`
**Purpose:** Handles complex transaction operations

**Key Features:**
- Address scanning
- UTXO management
- Transaction building
- Transaction signing
- Broadcasting

**Usage:** Used internally by BitcoinWalletSDK

---

### API Files

#### `API/BlockchainAPI.cs`
**Purpose:** Blockchain API wrapper (BlockCypher)

**Key Features:**
- Balance fetching
- Transaction history
- Batch operations
- Rate limiting
- Error handling

**Usage:** Used internally by SDK

---

### Model Files

#### `Models/SDKModels.cs`
**Purpose:** Data structures and models

**Contains:**
- `WalletInfo` - Wallet information
- `Transaction` - Transaction data
- `ScanResult` - Balance scan results
- `AddressSet` - Multiple address types
- `UTXO` - Unspent transaction output
- `SendResult` - Transaction send result

**Usage:**
```csharp
WalletInfo wallet = sdk.CreateNewWallet();
List<Transaction> txs = await sdk.GetTransactionHistory(address);
```

---

### Utility Files

#### `Utils/SDKUtils.cs`
**Purpose:** Helper utilities

**Contains:**
- `HexHelper` - Hex conversion
- `AddressValidator` - Address validation
- `AmountFormatter` - Amount formatting

**Usage:**
```csharp
byte[] bytes = HexHelper.HexToBytes(hex);
bool valid = AddressValidator.IsValidBitcoinAddress(addr);
string formatted = AmountFormatter.FormatBTC(amount);
```

---

### Documentation Files

#### `Documentation/README.md`
**Purpose:** Complete API reference

**Contains:**
- All API methods
- Parameters and return types
- Code examples
- Best practices
- Troubleshooting

**When to use:** Detailed API information

---

#### `Documentation/QUICKSTART.md`
**Purpose:** Get started quickly

**Contains:**
- 5-minute setup
- Basic examples
- Common tasks
- TestNet instructions

**When to use:** First time using SDK

---

#### `Documentation/TESTNET_GUIDE.md`
**Purpose:** TestNet usage guide

**Contains:**
- TestNet setup
- Getting free coins
- Testing workflow
- Faucet links
- Explorer links

**When to use:** Testing your application

---

### Example Files

#### `BitcoinSDKExample.cs`
**Purpose:** Working code examples

**Contains:**
- 10+ usage examples
- Complete workflows
- Best practices
- Error handling

**When to use:** Learning by example

---

## 🚀 Getting Started Path

### For Beginners:
1. Read `README.md` (this file)
2. Follow `Documentation/QUICKSTART.md`
3. Study `BitcoinSDKExample.cs`
4. Read `Documentation/TESTNET_GUIDE.md`
5. Test on TestNet
6. Read `Documentation/README.md` for details

### For Experienced Developers:
1. Read `README.md`
2. Review `Core/BitcoinWalletSDK.cs` API
3. Check `BitcoinSDKExample.cs`
4. Start building

---

## 📚 Documentation Hierarchy

```
1. README.md (Start here)
   ├── Overview and features
   ├── Quick example
   └── Links to detailed docs

2. Documentation/QUICKSTART.md (Next)
   ├── 5-minute setup
   ├── Basic examples
   └── Common tasks

3. Documentation/TESTNET_GUIDE.md (Testing)
   ├── TestNet setup
   ├── Getting coins
   └── Testing workflow

4. Documentation/README.md (Reference)
   ├── Complete API reference
   ├── All methods documented
   └── Advanced usage

5. BitcoinSDKExample.cs (Examples)
   ├── Working code
   ├── Multiple scenarios
   └── Best practices
```

---

## 🔧 Class Relationships

```
BitcoinWalletSDK (Main Entry Point)
    │
    ├── Uses → BlockchainAPI
    │            └── Handles API calls
    │
    ├── Uses → BitcoinTransactionService
    │            └── Handles transactions
    │
    ├── Returns → WalletInfo
    ├── Returns → Transaction[]
    ├── Returns → ScanResult
    └── Returns → AddressSet

Utils (Helper Classes)
    ├── HexHelper
    ├── AddressValidator
    └── AmountFormatter
```

---

## 💡 Usage Patterns

### Pattern 1: Simple Balance Check
```csharp
// Files involved:
// - Core/BitcoinWalletSDK.cs
// - API/BlockchainAPI.cs

var sdk = new BitcoinWalletSDK();
decimal balance = await sdk.GetBalance(address);
```

### Pattern 2: Create and Save Wallet
```csharp
// Files involved:
// - Core/BitcoinWalletSDK.cs
// - Models/SDKModels.cs (WalletInfo)

var sdk = new BitcoinWalletSDK();
WalletInfo wallet = sdk.CreateNewWallet();
// Save wallet.seedPhrase securely
```

### Pattern 3: Send Transaction
```csharp
// Files involved:
// - Core/BitcoinWalletSDK.cs
// - Core/BitcoinTransactionService.cs
// - API/BlockchainAPI.cs

var sdk = new BitcoinWalletSDK();
string txHash = await sdk.SendBitcoin(
    seedPhrase, toAddress, amount, fee
);
```

---

## 📦 Dependencies

### External Libraries:
- **NBitcoin** - Bitcoin protocol implementation
- **Newtonsoft.Json** - JSON serialization
- **System.Net.Http** - HTTP client
- **UnityEngine** - Unity integration

### Internal Dependencies:
```
BitcoinWalletSDK
    ├── Depends on: BlockchainAPI
    ├── Depends on: BitcoinTransactionService
    └── Depends on: SDKModels

BitcoinTransactionService
    ├── Depends on: BlockchainAPI
    └── Depends on: NBitcoin

BlockchainAPI
    ├── Depends on: System.Net.Http
    └── Depends on: Newtonsoft.Json
```

---

## 🎓 Learning Resources

### Start Here:
1. `README.md` - Overview
2. `Documentation/QUICKSTART.md` - Quick start

### Then:
3. `BitcoinSDKExample.cs` - Examples
4. `Documentation/TESTNET_GUIDE.md` - Testing

### Finally:
5. `Documentation/README.md` - Full reference
6. `CHANGELOG.md` - Version history

---

## 🔍 Finding What You Need

### "How do I create a wallet?"
→ `Documentation/QUICKSTART.md` or `BitcoinSDKExample.cs`

### "What methods are available?"
→ `Documentation/README.md` (API Reference section)

### "How do I test safely?"
→ `Documentation/TESTNET_GUIDE.md`

### "What's the complete API?"
→ `Documentation/README.md`

### "Show me working code"
→ `BitcoinSDKExample.cs`

### "What changed in this version?"
→ `CHANGELOG.md`

---

## 🛠️ Customization Points

### Easy to Customize:
- **API Provider**: Change `API/BlockchainAPI.cs` to use different provider
- **Network**: Pass `NetworkType.TestNet` or `NetworkType.MainNet`
- **Derivation Paths**: Use `GenerateAddress()` with custom paths
- **Fee Strategy**: Pass custom fee amounts

### Advanced Customization:
- **Transaction Building**: Modify `BitcoinTransactionService.cs`
- **UTXO Selection**: Customize in `BitcoinTransactionService.cs`
- **Rate Limiting**: Adjust in `BlockchainAPI.cs`
- **Error Handling**: Extend exception handling

---

## 📊 File Sizes (Approximate)

| File | Lines | Purpose |
|------|-------|---------|
| BitcoinWalletSDK.cs | ~350 | Main SDK |
| BitcoinTransactionService.cs | ~300 | Transactions |
| BlockchainAPI.cs | ~400 | API wrapper |
| SDKModels.cs | ~80 | Data models |
| SDKUtils.cs | ~120 | Utilities |
| BitcoinSDKExample.cs | ~250 | Examples |
| README.md | ~500 | Main docs |
| Documentation/README.md | ~1000 | Full API reference |
| QUICKSTART.md | ~300 | Quick start |
| TESTNET_GUIDE.md | ~500 | TestNet guide |

**Total:** ~3,800 lines of code and documentation

---

## ✅ Quality Checklist

- [x] Complete API implementation
- [x] MainNet support
- [x] TestNet support
- [x] Error handling
- [x] Rate limiting
- [x] Batch operations
- [x] Documentation
- [x] Examples
- [x] Quick start guide
- [x] TestNet guide
- [x] Changelog
- [x] Clean code structure
- [x] Async/await support
- [x] Cancellation support
- [x] Progress callbacks

---

## 🎯 Next Steps

1. **Read** `README.md`
2. **Follow** `Documentation/QUICKSTART.md`
3. **Study** `BitcoinSDKExample.cs`
4. **Test** on TestNet using `Documentation/TESTNET_GUIDE.md`
5. **Build** your application
6. **Reference** `Documentation/README.md` as needed

---

**Happy Coding! 🚀**

This SDK is designed to be:
- ✅ Easy to use
- ✅ Well documented
- ✅ Production ready
- ✅ Fully featured
- ✅ Safe to test
