using System;
using System.Collections.Generic;

/// <summary>
/// Data structures for Mempool.space API responses
/// Used for TestNet transaction history
/// </summary>

[Serializable]
public class MempoolTx
{
    public string txid;
    public int version;
    public int locktime;
    public int size;
    public int weight;
    public int fee;
    public List<MempoolVin> vin;
    public List<MempoolVout> vout;
    public MempoolStatus status;
}

[Serializable]
public class MempoolVin
{
    public string txid;
    public int vout;
    public MempoolVout prevout; // Previous output (contains address and value)
    public string scriptsig;
    public string scriptsig_asm;
    public List<string> witness;
    public int sequence;
    public bool is_coinbase;
}

[Serializable]
public class MempoolVout
{
    public string scriptpubkey;
    public string scriptpubkey_asm;
    public string scriptpubkey_type;
    public string scriptpubkey_address;
    public long value; // Value in satoshis
}

[Serializable]
public class MempoolStatus
{
    public bool confirmed;
    public int block_height;
    public string block_hash;
    public long block_time; // Unix timestamp
}

[Serializable]
public class MempoolRecommendedFees
{
    public int fastestFee = 15;
    public int halfHourFee = 10;
    public int hourFee = 8;
    public int economyFee = 5;
    public int minimumFee = 2;
}
