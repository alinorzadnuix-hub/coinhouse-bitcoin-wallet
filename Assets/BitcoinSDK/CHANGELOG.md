# Changelog

All notable changes to Bitcoin Wallet SDK will be documented in this file.

## [1.0.0] - 2024

### Added
- Initial release of Bitcoin Wallet SDK for Unity
- Wallet creation with BIP39 12-word seed phrases
- Wallet restoration from seed phrase
- Private key import (WIF and Hex formats)
- Balance checking for single addresses
- Batch balance checking for multiple addresses
- Transaction history fetching
- Batch transaction history for multiple addresses
- Send Bitcoin functionality with custom fees
- Address generation with custom derivation paths
- Multiple address type support (Legacy, SegWit, P2SH-SegWit)
- Address validation
- Seed phrase validation
- MainNet support
- TestNet support
- Automatic rate limiting (1.5s between API calls)
- BTC to Satoshi conversion utilities
- Satoshi to BTC conversion utilities
- Comprehensive documentation
- Quick start guide
- TestNet usage guide
- Example code with 10+ usage scenarios
- BlockCypher API integration
- Automatic UTXO selection for transactions
- Change address handling
- Transaction signing and broadcasting
- Progress callbacks for long operations
- Status callbacks for transaction sending
- Cancellation token support for async operations

### Core Features
- `BitcoinWalletSDK` - Main SDK class
- `BlockchainAPI` - Blockchain API wrapper
- `BitcoinTransactionService` - Transaction handling service
- `SDKModels` - Data models (WalletInfo, Transaction, ScanResult, etc.)
- `SDKUtils` - Utility helpers (HexHelper, AddressValidator, AmountFormatter)

### API Methods
- `CreateNewWallet()` - Generate new wallet
- `RestoreWalletFromSeed()` - Restore from seed phrase
- `ImportFromPrivateKey()` - Import from private key
- `GenerateAddress()` - Generate address with custom path
- `GenerateAddressSet()` - Generate Legacy, SegWit, P2SH addresses
- `GetBalance()` - Get single address balance
- `GetBatchBalances()` - Get multiple address balances
- `ScanWalletBalance()` - Scan all derived addresses
- `GetTransactionHistory()` - Get single address history
- `GetBatchTransactionHistory()` - Get multiple address history
- `SendBitcoin()` - Send from seed phrase
- `SendBitcoinFromPrivateKey()` - Send from private key
- `ValidateAddress()` - Validate Bitcoin address
- `ValidateSeedPhrase()` - Validate seed phrase
- `BTCToSatoshi()` - Convert BTC to Satoshi
- `SatoshiToBTC()` - Convert Satoshi to BTC
- `GetNetworkType()` - Get current network

### Documentation
- Complete API reference (README.md)
- Quick start guide (QUICKSTART.md)
- TestNet usage guide (TESTNET_GUIDE.md)
- Example code (BitcoinSDKExample.cs)
- Main SDK README

### Security Features
- Secure key derivation using NBitcoin
- BIP32/BIP39/BIP44 compliance
- Multiple address type support
- Proper UTXO handling
- Transaction verification before broadcast

### Performance Optimizations
- Batch API operations
- Automatic rate limiting
- Efficient UTXO selection
- Minimal API calls

### Developer Experience
- Clean, intuitive API
- Comprehensive examples
- Detailed documentation
- Error handling with meaningful messages
- Progress and status callbacks
- Async/await support
- Cancellation support

## [Unreleased]

### Planned Features
- Hardware wallet support
- Multi-signature wallets
- Lightning Network integration
- Fee estimation API
- Transaction replacement (RBF)
- Child-pays-for-parent (CPFP)
- Address book management
- QR code generation
- QR code scanning
- Fiat currency conversion
- Price tracking
- Push notifications
- Backup and restore to cloud
- Biometric authentication
- PIN protection
- Auto-lock feature
- Transaction notes
- Contact management
- CSV export
- PDF reports

### Future Improvements
- Additional blockchain API providers
- Improved error messages
- More address types (Taproot)
- Enhanced batch operations
- Caching layer
- Offline transaction signing
- Watch-only wallets
- HD wallet account management
- Custom fee strategies
- Transaction priority
- Mempool monitoring
- Network status checking
- Automatic retry logic
- Better rate limit handling

## Version History

### Version 1.0.0 (Current)
- First stable release
- Full MainNet and TestNet support
- Complete wallet functionality
- Comprehensive documentation
- Production ready

## Breaking Changes

None (initial release)

## Migration Guide

Not applicable (initial release)

## Known Issues

None reported

## Deprecations

None

## Security Updates

None (initial release)

## Contributors

- Initial development and release

## Support

For issues, questions, or feature requests:
- Check documentation in `Documentation/` folder
- Review examples in `BitcoinSDKExample.cs`
- Open an issue on GitHub

## License

[Your License Here]

---

**Note:** This SDK follows [Semantic Versioning](https://semver.org/).

Format: [MAJOR.MINOR.PATCH]
- MAJOR: Incompatible API changes
- MINOR: New functionality (backwards compatible)
- PATCH: Bug fixes (backwards compatible)
