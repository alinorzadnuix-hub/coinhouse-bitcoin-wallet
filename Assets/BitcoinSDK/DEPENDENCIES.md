# Dependencies

## Required Dependencies

### 1. NBitcoin
- **Version:** 7.0.0 or higher
- **Purpose:** Bitcoin protocol implementation
- **License:** MIT
- **Repository:** https://github.com/MetacoSA/NBitcoin
- **Installation:** NuGet Package Manager or Unity Package

**Features Used:**
- Mnemonic seed phrase generation (BIP39)
- HD wallet key derivation (BIP32/BIP44)
- Address generation (Legacy, SegWit, P2SH)
- Transaction building and signing
- Script creation
- Network support (MainNet/TestNet)

### 2. Newtonsoft.Json
- **Version:** 13.0.0 or higher
- **Purpose:** JSON serialization/deserialization
- **License:** MIT
- **Repository:** https://github.com/JamesNK/Newtonsoft.Json
- **Installation:** Unity Package Manager or Asset Store

**Features Used:**
- API response parsing
- Data serialization
- Configuration management

### 3. System.Net.Http
- **Version:** Built-in (.NET 4.x)
- **Purpose:** HTTP client for API calls
- **License:** MIT
- **Source:** .NET Framework

**Features Used:**
- HTTP GET/POST requests
- Async/await operations
- Timeout management

## Optional Dependencies

### 1. Unity UI
- **Version:** Built-in
- **Purpose:** Example UI implementation
- **Required for:** BitcoinSDKExample.cs only

### 2. Unity TextMeshPro (Optional)
- **Version:** 3.0.0+
- **Purpose:** Better text rendering
- **Installation:** Unity Package Manager
- **Note:** Can replace standard Unity Text

## Installation Guide

### Method 1: NuGet for Unity

1. Install NuGet for Unity from Asset Store
2. Open NuGet Package Manager
3. Search and install:
   - NBitcoin
   - Newtonsoft.Json

### Method 2: Manual Installation

#### NBitcoin
1. Download from https://www.nuget.org/packages/NBitcoin/
2. Extract DLL files
3. Copy to `Assets/Plugins/` folder
4. Restart Unity

#### Newtonsoft.Json
1. Download from https://www.nuget.org/packages/Newtonsoft.Json/
2. Extract DLL files
3. Copy to `Assets/Plugins/` folder
4. Restart Unity

### Method 3: Unity Package Manager

1. Open Package Manager (Window → Package Manager)
2. Click "+" → "Add package from git URL"
3. Add packages (if available)

## Dependency Tree

```
BitcoinSDK
├── NBitcoin (Required)
│   ├── BouncyCastle.Crypto (Included with NBitcoin)
│   └── System.Buffers (Included)
├── Newtonsoft.Json (Required)
└── System.Net.Http (Built-in)
```

## Version Compatibility

### Unity Versions
- ✅ Unity 2019.4 LTS
- ✅ Unity 2020.3 LTS
- ✅ Unity 2021.3 LTS
- ✅ Unity 2022.3 LTS
- ✅ Unity 2023.x

### .NET Versions
- ✅ .NET 4.x
- ✅ .NET Standard 2.0
- ✅ .NET Standard 2.1
- ❌ .NET 3.5 (Not supported)

### Platform Support
- ✅ Windows (Standalone)
- ✅ macOS (Standalone)
- ✅ Linux (Standalone)
- ✅ Android
- ✅ iOS
- ⚠️ WebGL (Limited - HTTP restrictions)

## Troubleshooting

### NBitcoin Not Found
**Solution:**
1. Ensure .NET 4.x is selected
2. Check DLL is in Plugins folder
3. Restart Unity
4. Reimport assets

### Newtonsoft.Json Conflicts
**Solution:**
1. Remove duplicate versions
2. Keep only one version
3. Use latest version
4. Clear Library folder

### System.Net.Http Missing
**Solution:**
1. Switch to .NET 4.x
2. Player Settings → API Compatibility Level
3. Select ".NET 4.x"
4. Restart Unity

### Android Build Issues
**Solution:**
1. Enable "Custom Main Manifest"
2. Add internet permission
3. Set minimum API level to 21+
4. Use IL2CPP backend

### iOS Build Issues
**Solution:**
1. Enable "Require Encryption" in capabilities
2. Add NSAppTransportSecurity exception
3. Use IL2CPP backend
4. Set minimum iOS version to 11+

## Security Considerations

### NBitcoin
- Cryptographically secure
- Well-tested library
- Active maintenance
- Large community

### Newtonsoft.Json
- Widely used
- Security updates
- No known vulnerabilities
- Regular maintenance

### System.Net.Http
- Built-in .NET library
- Microsoft maintained
- Regular security updates

## License Compliance

All dependencies use MIT License, which allows:
- ✅ Commercial use
- ✅ Modification
- ✅ Distribution
- ✅ Private use

Requirements:
- Include license notice
- Include copyright notice

## Updating Dependencies

### Check for Updates
```bash
# Check NBitcoin updates
# Visit: https://www.nuget.org/packages/NBitcoin/

# Check Newtonsoft.Json updates
# Visit: https://www.nuget.org/packages/Newtonsoft.Json/
```

### Update Process
1. Backup project
2. Test on TestNet
3. Update one dependency at a time
4. Test thoroughly
5. Update documentation

## Alternative Dependencies

### Instead of NBitcoin
- ⚠️ Not recommended - NBitcoin is industry standard
- Alternatives: BitcoinLib, Bitcoin.NET (less maintained)

### Instead of Newtonsoft.Json
- System.Text.Json (Built-in .NET Core)
- LitJson (Lighter weight)
- SimpleJSON (Unity-specific)

## Dependency Sizes

| Dependency | Size | Impact |
|------------|------|--------|
| NBitcoin | ~2 MB | Medium |
| Newtonsoft.Json | ~500 KB | Low |
| System.Net.Http | Built-in | None |
| **Total** | **~2.5 MB** | **Low** |

## Support

For dependency-related issues:
1. Check dependency documentation
2. Search existing issues
3. Contact dependency maintainers
4. Ask in Unity forums

---

**Last Updated:** 2024
**SDK Version:** 1.0.0
