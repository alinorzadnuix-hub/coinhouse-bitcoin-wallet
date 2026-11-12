using UnityEngine;
using System.Collections.Generic;

public static class CurrencyHelper 
{
    private static Dictionary<string, string> symbols = new Dictionary<string, string>() {
        {"USD", "$"},
        {"EUR", "€"},
        {"JPY", "¥"},
        {"GBP", "£"},
        {"CNY", "¥"},
        {"AUD", "A$"},
        {"CAD", "C$"},
        {"RUB", "₽"},
        {"THB", "฿"},
        {"IDR", "Rp"},
        {"TRY", "₺"},
        {"MXN", "Mex$"},
        {"BRL", "R$"}
    };

    public static string GetSymbol(string code)
    {
        if (string.IsNullOrEmpty(code)) return "$";
        code = code.ToUpper();
        if (symbols.ContainsKey(code)) return symbols[code];
        return code + " ";
    }
}
