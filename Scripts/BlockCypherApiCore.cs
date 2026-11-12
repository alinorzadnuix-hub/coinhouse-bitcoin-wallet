using System.Threading.Tasks;
using System.Net.Http;

public class BlockCypherApiCore
{
    private const string ApiUrl = "https://api.blockcypher.com/v1/btc/main/addrs/";

    public async Task<decimal> GetBalance(string address)
    {
        using (var client = new HttpClient())
        {
            var response = await client.GetAsync($"{ApiUrl}{address}/balance");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<BalanceData>(json);
            return data.balance / 100000000m;
        }
    }

    [System.Serializable]
    public class BalanceData
    {
        public long balance;
    }
}
