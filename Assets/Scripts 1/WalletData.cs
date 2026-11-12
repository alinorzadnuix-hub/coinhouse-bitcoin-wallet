using System;
using System.Collections.Generic;

[Serializable]
public class WalletData
{
    public string seedPhrase;
    public bool isTestNet = false; // Forced MainNet
    public string address;
    public string privateKey;
    public double balance;
    public List<TransactionHistory> transactions = new List<TransactionHistory>();
    
    // ✅ NEW: Cached balance for instant display
    public double cachedBalance; // Last known balance
    public string lastBalanceUpdate; // Timestamp of last update (HH:mm:ss)
    
    // ✅ NEW: First-time scan tracking
    public bool hasCompletedInitialScan = false; // Track if initial scan is done
    
    // Track if wallet was imported via private key (no seed phrase available)
    public bool isPrivateKeyImport = false;
    
    // Track if the backup keys overlay has been shown to the user
    public bool hasShownKeys = false;
    
    // The very first address ever created for this wallet (never changes)
    public string parentAddress;
    
    // ✅ Testnet equivalent of this wallet (same key, different network)
    public string testnetAddress;      // tb1... testnet SegWit address
    public string testnetPrivateKey;   // WIF testnet private key
    public double cachedTestnetBalance; // Last known tBTC balance
    
    // Address generation tracking
    public int currentAddressIndex = 0;
    public List<GeneratedAddress> generatedAddresses = new List<GeneratedAddress>();
    
    // Satolite Wallet Serial (SN + 12 digits)
    public string satoliteSerial;
}

[Serializable]
public class GeneratedAddress
{
    public string address;
    public int index;
    public string dateGenerated;
}

[Serializable]
public class TransactionHistory
{
    public string type; // "Send" or "Receive"
    public string address;
    public string amount;
    public string date;
    public string txHash;
    public string fee;
}

[Serializable]
public class WalletExportData
{
    public WalletData walletData;
    
    // App Settings
    public string appLanguage;
    public string appCurrency;
    public bool isDarkMode;
    public bool soundEnabled;
    
    // Price Alert Settings
    public bool priceAlertEnabled;
    public string priceAlertCycle;
    
    // Device Info
    public string deviceLanguage;
    public string exportDate;
    public string appVersion;
}
