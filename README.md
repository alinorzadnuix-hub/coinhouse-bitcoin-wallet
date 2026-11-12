# CoinHouse Bitcoin Wallet

[![Unity](https://img.shields.io/badge/Unity-6000.x-blue.svg)](https://unity.com/)
[![Platform](https://img.shields.io/badge/Platform-Android%20%7C%20Windows-green.svg)]()
[![Bitcoin](https://img.shields.io/badge/Bitcoin-Native%20SegWit%20(BIP84)-orange.svg)](https://bitcoin.org/)
[![License](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE)

A high-performance, non-custodial, open-source Bitcoin wallet built with **Unity 6** and **NBitcoin**. Designed with enterprise-grade cryptographic security, dynamic responsive UI for both mobile and desktop, and native dual-network (MainNet & TestNet) support.

---

## ✨ Features

- **Non-Custodial & Trustless**: Private keys and 12-word seed phrases are encrypted locally on the device with a user-defined PIN / Password (`AuthManager`). Raw keys are never sent to any server.
- **Native SegWit (BIP84)**: Generates and derives Bech32 addresses (`bc1q...` on MainNet, `tb1q...` on TestNet) for lowest possible transaction fees and optimal block space.
- **Dual Network Architecture**:
  - **MainNet (`CoinHouse`)**: Real Bitcoin network integration with package `com.coinhouse.wallet`.
  - **TestNet (`CoinHouse Testnet`)**: Safe sandboxed testing network with test faucets and mining simulator.
  - Quick network switcher via Unity Editor (`Tools > Build Variant`).
- **Send & Receive**:
  - Real-time fee estimation and UTXO transaction construction.
  - Strict transaction verification (`builder.Verify(signedTx)`) prior to network broadcast.
  - Real-time address balance synchronization and activity tracking.
- **QR Code Scanner & Generator**:
  - Live camera QR scanning powered by `ZXing.Net`.
  - High-resolution dynamic QR code generation for receiving Bitcoin.
- **Responsive Multi-Platform UI**:
  - **Mobile Mode**: Portrait (9:16 reference resolution) with modern cards and touch-optimized navigation.
  - **Desktop Mode**: Landscape (16:9 reference resolution) with adapted dashboard and sidebars.
- **Security & Lock Protection**:
  - PIN protection (`AppLockUI`) with SHA-256 key stretching.
  - Automatic session timeout and hidden key display until authenticated.

---

## 🛠 Tech Stack

- **Engine**: Unity 6 (`6000.x`)
- **Bitcoin SDK**: `NBitcoin` v9.0.3 (.NET Standard 2.1)
- **QR Scanning / Codec**: `ZXing.Net` v0.16.11
- **JSON Serialization**: `Newtonsoft.Json` v13.0.1
- **Blockchain Gateway**: BlockCypher Public APIs & Mempool.space
- **Android Integration**: Native Java Foreground Service & Notification Channels (`NotifIcon.androidlib`)

---

## 🚀 Getting Started

### Prerequisites
1. **Unity Hub** with **Unity 6 (6000.x)** installed.
2. For Android builds: **Android Build Support** (Android SDK & NDK Tools, OpenJDK) installed via Unity Hub.

### Installation
1. Clone this repository:
   ```bash
   git clone https://github.com/<your-username>/<repo-name>.git
   ```
2. Open **Unity Hub**, click **Add**, and select the project folder.
3. Open the project with **Unity 6**.

---

## 📦 Building the App

### Switching Networks & Building via Editor
This project includes automated variant tooling:
1. In the Unity Editor top menu, navigate to:  
   `Tools` > `Build Variant`
2. Choose your target network:
   - **Switch to MainNet**
   - **Switch to TestNet**
3. Click **Build APK Now** or **Build BOTH Variants (MainNet + TestNet)**.
4. Output binaries will be generated under `Builds/Android/` or `Builds/Windows/`.

---

## 🔒 Security & Best Practices

- **Never share your 12-word seed phrase or private key.** Anyone with access to your seed phrase has complete control of your funds.
- **Test on TestNet First**: Always verify transfers on TestNet using free faucet coins before handling real Bitcoin.
- The project `.gitignore` strictly protects against committing wallet backup files, keys, and private credentials.

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## ⚠️ Disclaimer

This software is provided "as is", without warranty of any kind, express or implied. In no event shall the authors or copyright holders be liable for any claim, damages, or other liability arising from the use of this software.
