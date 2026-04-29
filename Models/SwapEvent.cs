namespace _15_5_SniperBot_SignalLayer.Models
{
    public class SwapEvent
    {
        public string TokenAddress { get; set; } = "";
        public string TokenSymbol  { get; set; } = "";
        public string PoolAddress  { get; set; } = "";
        public string WalletSender { get; set; } = "";
        public decimal AmountIn    { get; set; }
        public decimal AmountInUsd { get; set; }
        public decimal AmountOut   { get; set; }
        public bool    IsBuy       { get; set; }
        public DateTime Timestamp  { get; set; } = DateTime.UtcNow;
    }
}