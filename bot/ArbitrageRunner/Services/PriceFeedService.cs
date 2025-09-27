using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Models;
using Microsoft.Extensions.Logging;

namespace ArbitrageRunner.Services;

public sealed class PriceFeedService
{
    public const string UniswapSourceKey = "UNISWAPV3";
    public const string BalancerSourceKey = "BALANCER";
    public const string UniswapHttpClientName = "uniswap-subgraph";
    public const string BalancerHttpClientName = "balancer-subgraph";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppConfig _config;
    private readonly ILogger<PriceFeedService> _logger;
    private readonly IReadOnlyDictionary<string, IPriceSource> _sources;

    public PriceFeedService(
        IHttpClientFactory httpClientFactory,
        AppConfig config,
        ILogger<PriceFeedService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;

        _sources = BuildSources();
    }

    public async Task<decimal> GetStablePairMidAsync(
        string exchange,
        string assetIn,
        string assetOut,
        CancellationToken cancellationToken)
    {
        var key = exchange.Trim().ToUpperInvariant();

        if (_sources.TryGetValue(key, out var source))
        {
            var price = await source.TryGetMidPriceAsync(assetIn, assetOut, cancellationToken);
            if (price.HasValue)
            {
                return price.Value;
            }
        }

        if (_config.PriceFeeds.ManualQuotes.TryGetValue(CreatePairKey(assetIn, assetOut), out var manualQuote))
        {
            _logger.LogWarning(
                "Falling back to manual quote for {Exchange} {Pair}: {Quote}",
                exchange,
                CreatePairKey(assetIn, assetOut),
                manualQuote);
            return manualQuote;
        }

        _logger.LogWarning(
            "No price source available for exchange {Exchange} pair {Pair}; defaulting to 1.0",
            exchange,
            CreatePairKey(assetIn, assetOut));
        return 1.0m;
    }

    private IReadOnlyDictionary<string, IPriceSource> BuildSources()
    {
        var sources = new Dictionary<string, IPriceSource>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_config.PriceFeeds.UniswapV3.Endpoint))
        {
            sources[UniswapSourceKey] = new UniswapSubgraphPriceSource(
                _httpClientFactory,
                _config.PriceFeeds.UniswapV3,
                _logger);
        }

        if (!string.IsNullOrWhiteSpace(_config.PriceFeeds.Balancer.Endpoint))
        {
            sources[BalancerSourceKey] = new BalancerSubgraphPriceSource(
                _httpClientFactory,
                _config.PriceFeeds.Balancer,
                _logger);
        }

        return sources;
    }

    private static string CreatePairKey(string assetIn, string assetOut)
        => $"{assetIn.ToUpperInvariant()}/{assetOut.ToUpperInvariant()}";

    private interface IPriceSource
    {
        Task<decimal?> TryGetMidPriceAsync(string assetIn, string assetOut, CancellationToken cancellationToken);
    }

    private sealed class UniswapSubgraphPriceSource : IPriceSource
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly UniswapPriceFeedConfig _config;
        private readonly ILogger _logger;

        public UniswapSubgraphPriceSource(
            IHttpClientFactory clientFactory,
            UniswapPriceFeedConfig config,
            ILogger logger)
        {
            _clientFactory = clientFactory;
            _config = config;
            _logger = logger;
        }

        public async Task<decimal?> TryGetMidPriceAsync(string assetIn, string assetOut, CancellationToken cancellationToken)
        {
            var pairKey = CreatePairKey(assetIn, assetOut);
            if (!_config.Pools.TryGetValue(pairKey, out var poolId))
            {
                _logger.LogDebug("No Uniswap pool configured for {Pair}", pairKey);
                return null;
            }

            var client = _clientFactory.CreateClient(UniswapHttpClientName);
            var payload = new
            {
                query = "query ($poolId: ID!) { pool(id: $poolId) { token0 { id } token1 { id } token0Price token1Price } }",
                variables = new { poolId = poolId.ToLowerInvariant() }
            };

            using var response = await client.PostAsync(
                string.Empty,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                !dataElement.TryGetProperty("pool", out var poolElement))
            {
                _logger.LogWarning("Unexpected payload from Uniswap subgraph for pool {PoolId}", poolId);
                return null;
            }

            var token0Address = poolElement.GetProperty("token0").GetProperty("id").GetString();
            var token1Address = poolElement.GetProperty("token1").GetProperty("id").GetString();
            var token0Price = decimal.Parse(poolElement.GetProperty("token0Price").GetString() ?? "0", CultureInfo.InvariantCulture);
            var token1Price = decimal.Parse(poolElement.GetProperty("token1Price").GetString() ?? "0", CultureInfo.InvariantCulture);

            var assetInLower = assetIn.ToLowerInvariant();
            if (!string.IsNullOrEmpty(token0Address) && token0Address.Equals(assetInLower, StringComparison.OrdinalIgnoreCase))
            {
                return token1Price;
            }

            if (!string.IsNullOrEmpty(token1Address) && token1Address.Equals(assetInLower, StringComparison.OrdinalIgnoreCase))
            {
                return token0Price;
            }

            _logger.LogWarning("Uniswap pool {PoolId} does not contain asset {AssetIn}", poolId, assetIn);
            return null;
        }
    }

    private sealed class BalancerSubgraphPriceSource : IPriceSource
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly BalancerPriceFeedConfig _config;
        private readonly ILogger _logger;

        public BalancerSubgraphPriceSource(
            IHttpClientFactory clientFactory,
            BalancerPriceFeedConfig config,
            ILogger logger)
        {
            _clientFactory = clientFactory;
            _config = config;
            _logger = logger;
        }

        public async Task<decimal?> TryGetMidPriceAsync(string assetIn, string assetOut, CancellationToken cancellationToken)
        {
            var pairKey = CreatePairKey(assetIn, assetOut);
            if (!_config.Pools.TryGetValue(pairKey, out var poolId))
            {
                _logger.LogDebug("No Balancer pool configured for {Pair}", pairKey);
                return null;
            }

            var client = _clientFactory.CreateClient(BalancerHttpClientName);
            var payload = new
            {
                query = "query ($poolId: ID!) { pool(id: $poolId) { tokens { address balance weight } } }",
                variables = new { poolId = poolId.ToLowerInvariant() }
            };

            using var response = await client.PostAsync(
                string.Empty,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                !dataElement.TryGetProperty("pool", out var poolElement))
            {
                _logger.LogWarning("Unexpected payload from Balancer subgraph for pool {PoolId}", poolId);
                return null;
            }

            var tokensElement = poolElement.GetProperty("tokens");
            var (tokenIn, tokenOut) = FindTokens(tokensElement, assetIn, assetOut);
            if (tokenIn == null || tokenOut == null)
            {
                _logger.LogWarning("Balancer pool {PoolId} missing token pair {Pair}", poolId, pairKey);
                return null;
            }

            var balanceIn = decimal.Parse(tokenIn.Value.GetProperty("balance").GetString() ?? "0", CultureInfo.InvariantCulture);
            var balanceOut = decimal.Parse(tokenOut.Value.GetProperty("balance").GetString() ?? "0", CultureInfo.InvariantCulture);
            var weightIn = decimal.Parse(tokenIn.Value.GetProperty("weight").GetString() ?? "0", CultureInfo.InvariantCulture);
            var weightOut = decimal.Parse(tokenOut.Value.GetProperty("weight").GetString() ?? "0", CultureInfo.InvariantCulture);

            if (weightIn == 0 || weightOut == 0)
            {
                return null;
            }

            var normalizedIn = balanceIn / weightIn;
            var normalizedOut = balanceOut / weightOut;
            return normalizedOut / normalizedIn;
        }

        private static (JsonElement? tokenIn, JsonElement? tokenOut) FindTokens(JsonElement tokens, string assetIn, string assetOut)
        {
            JsonElement? inElement = null;
            JsonElement? outElement = null;
            var assetInLower = assetIn.ToLowerInvariant();
            var assetOutLower = assetOut.ToLowerInvariant();

            foreach (var token in tokens.EnumerateArray())
            {
                var address = token.GetProperty("address").GetString()?.ToLowerInvariant();
                if (address == assetInLower)
                {
                    inElement = token;
                }
                else if (address == assetOutLower)
                {
                    outElement = token;
                }
            }

            return (inElement, outElement);
        }
    }
}