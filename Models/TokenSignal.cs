namespace _15_5_SniperBot_SignalLayer.Models
{
    public class TokenSignal
    {
        public string TokenAddress  { get; set; } = "";
        public string PoolAddress   { get; set; } = "";
        public int    Swaps30s      { get; set; }
        public int    Swaps120s     { get; set; }
        public int    Buys60s       { get; set; }
        public int    Sells60s      { get; set; }
        public int    UniqueWallets { get; set; }
        public decimal BuySellRatio => Sells60s == 0 ? Buys60s : (decimal)Buys60s / Sells60s;
        public DateTime LastSeen    { get; set; } = DateTime.UtcNow;

        public override string ToString() =>
            $"swaps30={Swaps30s} | swaps120={Swaps120s} | " +
            $"ratio={BuySellRatio:F1} | wallets={UniqueWallets}";
    }
}