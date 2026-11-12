---
name: coinhouse-bitcoin-wallet
description: Comprehensive architecture, build pipeline, cryptographic security, UI scaling, and API guidelines for the CoinHouse Unity Bitcoin Wallet project. Use when inspecting, modifying, building, or debugging the Bitcoin wallet app.
---

# CoinHouse Bitcoin Wallet - Developer & Architecture Guide

## 1. Project Overview & Architecture
- **Engine**: Unity 6 (6000.x)
- **Primary Platforms**: Android (APK/AAB) & Windows Standalone
- **Product Variants**:
  - **MainNet**: `CoinHouse` (Package: `com.coinhouse.wallet`, Network: `NBitcoin.Network.Main`, Bech32 addresses: `bc1...`, Icon: `Assets/Resources/y.png`)
  - **TestNet**: `CoinHouse Testnet` (Package: `com.coinhouse.wallet.testnet`, Network: `NBitcoin.Network.TestNet`, Bech32 addresses: `tb1...`, Icon: `Assets/Resources/Bitcointa.png`, Define Symbol: `TESTNET_BUILD`)

## 2. Immutable Architecture & UI Contracts (STRICTLY FROZEN)
- **UI Frozen Contract**:
  - The UI design, styling, layout, coordinates, font sizing, and screen structure in `ModernMobileWallet.cs`, `FinalWalletUI.cs`, `SendReceiveUI.cs`, `RecoverWalletUI.cs`, `TransactionHistoryUI.cs`, `MiningUI.cs`, `AppLockUI.cs`, and `OptionsUI.cs` are in their final, optimal state.
  - **Never modify, redesign, delete, or rewrite UI components, layouts, or visual styles.**
- **Project Structure Preservation**:
  - Never move, rename, clean up, or delete existing files or directories (such as `Assets/Scripts 1/`, `Assets/BitcoinSDK/`, `Assets/Plugins/`, `Assets/Editor/`, or root folders `Bitcoin/`, `Scripts/`).
  - Maintain all `.meta` files to protect Unity scene asset GUID references.

## 3. Key Directories & Responsibilities
- `Assets/Scripts 1/`: Active runtime C# scripts (UI, Singletons, Core crypto, Network managers).
- `Assets/BitcoinSDK/`: Standalone Bitcoin SDK (BIP84, BIP44, BIP49, Mnemonic, Transaction Builders).
- `Assets/Plugins/Android/`: Native Android integration:
  - `WalletBackgroundService.java`: Foreground service polling live Bitcoin ticker and notifying user.
  - `NotifIcon.androidlib`: Native Android Library holding notification drawables (Unity 6 compliant).
  - `AndroidManifest.xml`: Core permissions (Internet, Camera, Foreground Service, Storage).
- `Assets/Editor/`: Editor automation tools:
  - `AndroidBuilder.cs`: Automated variant APK build pipeline.
  - `BuildVariantSwitcher.cs`: Window (`Tools > Build Variant`) for instant switching between MainNet and TestNet.
  - `BuildSettingsWindow.cs`: Settings configuration window (`Bitcoin Wallet > Build Settings`).
  - `PurgeObsoleteRes.cs`: Automatically prevents obsolete `Assets/Plugins/Android/res` folder in Unity 6.

## 4. Manager Singletons & Event Architecture
- **`WalletManager2`**: Central singleton holding current wallet state, cached balance, and transaction history.
  - `WalletManager2.OnBalanceChanged`: Central event used to synchronize balances across all active screens.
- **`ThemeManager`**: Manages Light/Dark theme palettes (`GetBackgroundColor()`, `GetCardColor()`, `GetTextColor()`, `GetAccentColor()`).
- **`LocalizationManager`**: Manages multilingual string tables and cleans icon emojis.
- **`AudioManager`**: Sound effects (button clicks, back navigation, success, copy feedback).
- **`AuthManager`**: PIN encryption, session token validation, and SHA256 key stretching.
- **`NotificationManager`**: Unity Mobile Notification Center wrapper for Android channels and price alerts.
- **`BackgroundServiceManager`**: Bridges to Java foreground service using `UnityPlayerForActivityOrService`.

## 5. Cryptography & Transaction Standards
- **Library**: `NBitcoin` (v9.0.3 targeting .NET Standard 2.1).
- **Default Derivation (SegWit Native - BIP84)**:
  - MainNet Path: `m/84'/0'/0'/0/index` (Bech32 `bc1q...`)
  - TestNet Path: `m/84'/1'/0'/0/index` (Bech32 `tb1q...`)
- **Key Storage**: PIN-encrypted wallet data saved in `Application.persistentDataPath/wallet_mainnet.json`. Never store raw unencrypted private keys in PlayerPrefs.
- **Transaction Signing**: Always verify signatures with `builder.Verify(signedTx)` before broadcasting via `BlockCypherApiCore` or `BlockchainAPI`.

## 6. UI Scaling & Responsive Resolution
- All runtime UI screens dynamically detect orientation in the Unity Editor:
  ```csharp
  bool isDesktop = false;
  #if UNITY_STANDALONE && !UNITY_EDITOR
  isDesktop = true;
  #elif UNITY_EDITOR
  isDesktop = (Screen.width > Screen.height);
  #endif
  ```
- **Mobile Mode**: Portrait (9:16 or 1080x1920 reference resolution).
- **Desktop Mode**: Landscape (16:9 or 1920x1080 reference resolution).

## 7. Build & Deployment Guidelines
1. In Unity 6, always ensure the Android Build Support module (SDK, NDK, OpenJDK) is installed in Unity Hub.
2. The active platform in Unity must be switched to **Android** (`File > Build Profiles/Settings > Android > Switch Platform`).
3. To build APKs, open `Tools > Build Variant` and click:
   - `Build APK Now (Testnet / MainNet)`
   - Or `Build BOTH Variants (MainNet + TestNet)`
4. Output binaries are saved to `Builds/Android/`.
