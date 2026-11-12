<div align="center">

# ⚡ CoinHouse Bitcoin Wallet
### *The Next-Generation Non-Custodial Bitcoin Wallet Built with Unity 6 & Powered by Gemini 3.8 AI*

[![AI Powered](https://img.shields.io/badge/AI%20Engineered-Google%20Gemini%203.8-8E75C2.svg?style=for-the-badge&logo=google)](https://deepmind.google/technologies/gemini/)
[![Unity 6](https://img.shields.io/badge/Unity%206-6000.x-000000.svg?style=for-the-badge&logo=unity)](https://unity.com/)
[![Bitcoin](https://img.shields.io/badge/Bitcoin-BIP84%20Native%20SegWit-F7931A.svg?style=for-the-badge&logo=bitcoin)](https://bitcoin.org/)
[![Platform](https://img.shields.io/badge/Platform-Android%20%7C%20Windows-4285F4.svg?style=for-the-badge)]()
[![Security](https://img.shields.io/badge/Security-Non--Custodial%20Encrypted-2DA44E.svg?style=for-the-badge)]()

<br/>

**CoinHouse** is a production-grade, non-custodial Bitcoin wallet co-engineered from scratch with **Google DeepMind's Gemini 3.8** AI model. It bridges the precision of enterprise cryptographic standards with the fluidity of the **Unity 6 engine**, delivering a responsive, multi-platform wallet for both mobile (Android) and desktop (Windows).

[Explore Features](#-key-features) • [Downloads & Releases](#-downloads--releases) • [The AI Collaboration](#-co-engineered-with-gemini-38-ai) • [For Bitcoin Devs](#-for-the-bitcoin-community) • [For Unity Devs](#-for-the-unity-community) • [Quickstart](#-build--development-guide)

</div>

---

## 🤖 Co-Engineered with Gemini 3.8 AI

This repository stands as a pioneering benchmark in autonomous software engineering. Built through continuous collaboration with **Google Gemini 3.8** in the Antigravity agentic environment, Gemini 3.8 served as a principal architect across every tier:

- **Cryptographic Security**: Designed strict derivation paths (`m/84'/0'/0'/0/x` and `m/84'/1'/0'/0/x`), transaction signing verifiers, and encrypted local key storage routines.
- **Adaptive Canvas Architecture**: Conceived the dynamic multi-resolution scaling engine that dynamically toggles between smartphone portrait (`9:16`) and desktop widescreen (`16:9`) layouts at runtime.
- **Android Native Bridge**: Generated custom Android foreground services and notification libraries (`.androidlib`) adhering to strict Unity 6 requirements.
- **Automated Variant Switching**: Created editor automation (`Tools > Build Variant`) allowing 1-click toggling between MainNet and TestNet environments.

---

## 📥 Downloads & Releases

Pre-compiled binary packages are available directly via [GitHub Releases](https://github.com/alinorzadnuix-hub/coinhouse-bitcoin-wallet/releases):

| Package | Platform | Network | Description | Download |
| :--- | :--- | :--- | :--- | :---: |
| **CoinHouse Desktop** | Windows (x64) | **MainNet** | Standalone portable executable for real Bitcoin transactions. | [Download Release](https://github.com/alinorzadnuix-hub/coinhouse-bitcoin-wallet/releases) |
| **CoinHouse TestNet Desktop** | Windows (x64) | **TestNet** | Risk-free sandboxed environment with built-in testnet faucet tools. | [Download Release](https://github.com/alinorzadnuix-hub/coinhouse-bitcoin-wallet/releases) |
| **CoinHouse Mobile** | Android (APK) | **TestNet / MainNet** | Mobile APK supporting native live QR camera scanning & notifications. | [Download Release](https://github.com/alinorzadnuix-hub/coinhouse-bitcoin-wallet/releases) |

> 💡 **Recommendation**: If you are exploring CoinHouse for the first time, download the **TestNet build** to test sending, receiving, and address generation with zero financial risk.

---

## 💎 Key Features

- 🔐 **Non-Custodial & Trustless**: You own your keys. Raw private keys and 12-word seed phrases are encrypted locally on the device using PIN-based SHA-256 key stretching (`AuthManager`). No centralized servers or accounts.
- ⚡ **Native SegWit (BIP84)**: Generates native Bech32 addresses (`bc1q...` on MainNet, `tb1q...` on TestNet) for minimal transaction fees and fastest blockchain confirmation times.
- 🔄 **Seamless Dual-Network Support**:
  - **MainNet**: Live Bitcoin network connectivity (`com.coinhouse.wallet`).
  - **TestNet**: Complete sandbox featuring faucet integration and simulated mining.
- 📸 **Built-In QR Scanner & High-Res Generator**: Instant address scanning via device camera powered by `ZXing.Net`, plus dynamic vector-clear QR generation.
- 📊 **Real-Time UTXO & Fee Tracking**: Live balance updates, automated dynamic fee estimation, and comprehensive transaction history.
- 🔒 **PIN & Screen Protection**: Auto-locking session security, biometric-ready PIN architecture, and masked credential viewing.

---

## 🌐 For the Bitcoin Community

CoinHouse adheres to modern Bitcoin protocol best practices:

- **BIP84 Derivation Standard**: Native SegWit Bech32 address derivation by default (`m/84'/0'/0'/0/i` for MainNet, `m/84'/1'/0'/0/i` for TestNet).
- **Offline Signature Verification**: Every signed raw transaction is strictly verified through `builder.Verify(signedTx)` before being broadcasted.
- **UTXO Management**: Aggregates unspent transaction outputs safely, calculates exact change addresses, and prevents dust outputs.
- **API Rate-Limiting**: Multi-gateway fallback handling with structured request throttling to prevent IP rate-limiting.

---

## 🎮 For the Unity Community

Who says Unity is only for games? CoinHouse demonstrates how the Unity 6 engine can be leveraged for reactive consumer applications:

- **Dynamic Dual-Aspect Scaling**:
  ```csharp
  // Intelligent viewport adaptation across Mobile & Desktop
  bool isDesktop = false;
  #if UNITY_STANDALONE && !UNITY_EDITOR
  isDesktop = true;
  #elif UNITY_EDITOR
  isDesktop = (Screen.width > Screen.height);
  #endif
  ```
- **Modern Clean UI**: 60fps hardware-accelerated animations, crisp typography, and unified card components without external heavy UI frameworks.
- **Unity 6 Compliant**: Completely free of obsolete APIs, featuring clean Android Native Libraries (`.androidlib`) for notifications and background services.
- **Automated Editor Pipeline**: Custom editor windows (`Assets/Editor/BuildVariantSwitcher.cs`) to switch bundle IDs, define symbols (`TESTNET_BUILD`), and build targets in seconds.

---

## 🛠 Tech Stack

| Component | Technology | Version / Specification |
| :--- | :--- | :--- |
| **Engine** | Unity 6 | `6000.x` |
| **AI Co-Architect** | Google DeepMind Gemini | `Gemini 3.8` |
| **Bitcoin Library** | NBitcoin | `9.0.3` (.NET Standard 2.1) |
| **Barcode / QR** | ZXing.Net | `0.16.11` |
| **Serialization** | Newtonsoft.Json | `13.0.1` |
| **Network Gateway** | BlockCypher & Mempool APIs | REST / HTTPS |
| **Android Integration** | Java Foreground Service | `NotifIcon.androidlib` |

---

## 🚀 Build & Development Guide

### Prerequisites
1. **Unity Hub** with **Unity 6 (6000.x)** installed.
2. (Optional for Android builds): **Android Build Support** (Android SDK, NDK, OpenJDK).

### Cloning & Opening the Project
```bash
# Clone the repository
git clone https://github.com/alinorzadnuix-hub/coinhouse-bitcoin-wallet.git

# Open Unity Hub -> Add -> Select the cloned project folder
# Launch with Unity 6 (6000.x)
```

### Switching Networks & Automated Builds
1. In the Unity Editor menu bar, navigate to **`Tools > Build Variant`**.
2. Select your desired network:
   - **Switch to MainNet**
   - **Switch to TestNet**
3. Click **Build APK Now** or **Build Windows Executable**.

---

## 🛡 Security & Best Practices

- **Self-Custody Responsibility**: Never disclose your 12-word seed phrase or private key to anyone.
- **Repository Safety**: The repository `.gitignore` strictly protects against committing wallet data, keystores, and credentials.
- **Always Test First**: Before moving significant funds, experiment with TestNet coins to familiarize yourself with the interface and confirmation flow.

---

## 📜 License

This project is open-source and released under the **MIT License**.

---

## 🤝 Community & Contributions

Contributions, bug reports, and pull requests from both the **Bitcoin** and **Unity** communities are warmly welcomed! Feel free to open an issue or start a discussion on GitHub.

⭐ **If you find this project innovative, don't forget to star the repository!**
