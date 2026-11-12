using System;

[Serializable]
public class CoinGeckoResponse
{
    public CoinGeckoBitcoin bitcoin;
}

[Serializable]
public class CoinGeckoBitcoin
{
    public float usd;
}

[Serializable]
public class PaprikaTicker
{
    public PaprikaQuotes quotes;
}

[Serializable]
public class PaprikaQuotes
{
    public PaprikaUSD USD;
}

[Serializable]
public class PaprikaUSD
{
    public float price;
}
