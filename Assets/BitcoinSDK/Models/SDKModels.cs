using System;
using System.Collections.Generic;

namespace BitcoinSDK.Models
{
    /// <summary>
    /// Wallet information
    /// </summary>
    [Serializable]
    public class WalletInfo
    {
        public string seedPhrase;
        public string privateKey;
        public string address;
        public string network;
        public DateTime createdAt;
    }

    /// <summary>
    /// Address set (Legacy, SegWit, P2SH-SegWit)
    /// </summary>
    [Serializable]
    public class AddressSet
    {
        public string legacy;
        public string segwit;
        public string p2shSegwit;
    }

    /// <summary>
    /// Transaction information
    /// </summary>
    [Serializable]
    public class Transaction
    {
        public string txHash;
        public string type; // "Send" or "Receive"
        public decimal amount;
        public string date;
        public string otherAddress;
    }

    /// <summary>
    /// Scan result with balance information
    /// </summary>
    [Serializable]
    public class ScanResult
    {
        public decimal confirmedBalance;
        public decimal unconfirmedBalance;
    }

    /// <summary>
    /// UTXO (Unspent Transaction Output)
    /// </summary>
    [Serializable]
    public class UTXO
    {
        public string txHash;
        public int outputIndex;
        public long value; // satoshis
        public string scriptHex;
    }

    /// <summary>
    /// Transaction send result
    /// </summary>
    [Serializable]
    public class SendResult
    {
        public bool success;
        public string txHash;
        public string errorMessage;
    }
}
