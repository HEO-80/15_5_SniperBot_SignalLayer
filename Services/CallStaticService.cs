using System;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Web3;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class CallStaticService
    {
        private readonly Web3 _web3;
        private const string ROUTER = "0xcF77a3Ba9A5CA399B7c97c74d54e5b1Beb874E43";
        private const string WETH   = "0x4200000000000000000000000000000000000006";

        public CallStaticService(Web3 web3)
        {
            _web3 = web3;
        }

        public async Task<bool> IsTokenTradableAsync(string tokenAddress, string symbol, decimal amountEth)
        {
            try
            {
                var amountIn = Web3.Convert.ToWei(amountEth);
                var path     = new[] { WETH, tokenAddress };
                var deadline = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();

                // 1. Simular BUY — getAmountsOut
                var buyFunction = new GetAmountsOutFunction
                {
                    AmountIn = amountIn,
                    Path     = path
                };

                var buyHandler = _web3.Eth.GetContractQueryHandler<GetAmountsOutFunction>();
                var buyResult  = await buyHandler.QueryAsync<GetAmountsOutOutputDTO>(ROUTER, buyFunction);
                var tokensOut  = buyResult.Amounts[1];

                if (tokensOut == 0)
                {
                    Logger.Reject($"{symbol} | callstatic: buy devuelve 0 tokens");
                    return false;
                }

                // 2. Simular SELL — getAmountsOut inverso
                var sellPath = new[] { tokenAddress, WETH };
                var sellFunction = new GetAmountsOutFunction
                {
                    AmountIn = tokensOut,
                    Path     = sellPath
                };

                var sellResult = await buyHandler.QueryAsync<GetAmountsOutOutputDTO>(ROUTER, sellFunction);
                var ethOut     = sellResult.Amounts[1];

                if (ethOut == 0)
                {
                    Logger.Reject($"{symbol} | callstatic: sell devuelve 0 ETH — honeypot");
                    return false;
                }

                var recoveryPct = (decimal)ethOut / (decimal)amountIn * 100;
                Logger.Success($"[CALLSTATIC ✅] {symbol} | recuperación: {recoveryPct:F1}% — token vendible");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Reject($"{symbol} | callstatic_error: {ex.Message[..Math.Min(80, ex.Message.Length)]}");
                return false;
            }
        }
    }

    [Function("getAmountsOut", "uint256[]")]
    public class GetAmountsOutFunction : FunctionMessage
    {
        [Parameter("uint256", "amountIn", 1)]
        public BigInteger AmountIn { get; set; }

        [Parameter("address[]", "path", 2)]
        public string[] Path { get; set; } = Array.Empty<string>();
    }

    [FunctionOutput]
    public class GetAmountsOutOutputDTO : IFunctionOutputDTO
    {
        [Parameter("uint256[]", "amounts", 1)]
        public System.Collections.Generic.List<BigInteger> Amounts { get; set; } = new();
    }
}