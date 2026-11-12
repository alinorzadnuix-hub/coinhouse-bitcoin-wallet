using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace UnityBitcoinWallet
{
    [Serializable]
    public class Balance
    {
        public string address { get; set; }
        public long balance { get; set; }
    }

    public class BlockCypherApi
    {
        private const string ApiUrl = "https://api.blockcypher.com/v1/btc/main/addrs/";

        public async Task<decimal> GetWalletBalanceAsync(List<string> addresses)
        {
            using (var httpClient = new HttpClient())
            {
                var addressesString = string.Join(";", addresses);
                var response = await httpClient.GetAsync($"{ApiUrl}{addressesString}/balance");
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var balances = JsonConvert.DeserializeObject<List<Balance>>(json);
                decimal totalBalance = 0;
                foreach (var balance in balances)
                {
                    totalBalance = balance.balance / 100000000m;
                }
                return totalBalance;
            }
        }
    }
}
