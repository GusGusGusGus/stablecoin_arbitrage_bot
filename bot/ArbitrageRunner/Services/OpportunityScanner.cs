using System;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Models;
using Microsoft.Extensions.Logging;

namespace ArbitrageRunner.Services;

public sealed class OpportunityScanner
{
    private readonly PriceFeedService _priceFeed;
    private readonly AppConfig _config;
    private readonly ILogger<OpportunityScanner> _logger;

    public OpportunityScanner(
        PriceFeedService priceFeed,
        AppConfig config,
        ILogger<OpportunityScanner> logger)
    {
        _priceFeed = priceFeed;
        _config = config;
        _logger = logger;
    }

    public async Task<ArbitrageOpportunity?> DetectAsync(CancellationToken cancellationToken)
    {
        var usdcUsdtUniswap = await _priceFeed.GetStablePairMidAsync("UniswapV3", "USDC", "USDT", cancellationToken);
        var usdcUsdtBalancer = await _priceFeed.GetStablePairMidAsync("Balancer", "USDC", "USDT", cancellationToken);

        var spread = Math.Abs(usdcUsdtUniswap - usdcUsdtBalancer);
        _logger.LogInformation("Current spread between venues: {Spread}", spread);

        if (spread < 0.0001m)
        {
            return null;
        }

        _logger.LogDebug("Spread threshold met ({Spread}). Further path planning required.", spread);
        return null;
    }
}