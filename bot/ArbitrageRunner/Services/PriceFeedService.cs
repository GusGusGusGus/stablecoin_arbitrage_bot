using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Models;
using Microsoft.Extensions.Logging;
using Nethereum.Web3;

namespace ArbitrageRunner.Services;

public sealed class PriceFeedService
{
    private readonly EthereumClientFactory _clientFactory;
    private readonly AppConfig _config;
    private readonly ILogger<PriceFeedService> _logger;

    public PriceFeedService(EthereumClientFactory clientFactory, AppConfig config, ILogger<PriceFeedService> logger)
    {
        _clientFactory = clientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<decimal> GetStablePairMidAsync(string exchange, string assetIn, string assetOut, CancellationToken cancellationToken)
    {
        // TODO: Connect to Uniswap/Balancer subgraphs or on-chain pools for actual pricing.
        _logger.LogDebug("Fetching price for {Exchange} {AssetIn}/{AssetOut}", exchange, assetIn, assetOut);
        await Task.Delay(10, cancellationToken);
        return 1.0m;
    }

    public Web3 GetMainnetClient() => _clientFactory.CreateMainnetClient();
    public Web3 GetOptimismClient() => _clientFactory.CreateOptimismClient();
}