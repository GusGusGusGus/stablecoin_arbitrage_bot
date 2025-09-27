using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Infrastructure;
using ArbitrageRunner.Models;
using Microsoft.Extensions.Logging;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;

namespace ArbitrageRunner.Services;

public sealed class GasOracleService
{
    private const int MaxSamplesPerNetwork = 64;

    private readonly EthereumClientFactory _clientFactory;
    private readonly AppConfig _config;
    private readonly ILogger<GasOracleService> _logger;
    private readonly ConcurrentDictionary<GasNetwork, ConcurrentQueue<GasQuote>> _recentQuotes = new();

    public GasOracleService(
        EthereumClientFactory clientFactory,
        AppConfig config,
        ILogger<GasOracleService> logger)
    {
        _clientFactory = clientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<GasQuote> GetGasQuoteAsync(GasNetwork network, CancellationToken cancellationToken)
    {
        GasQuote quote = network switch
        {
            GasNetwork.Mainnet => await GetMainnetQuoteAsync(cancellationToken),
            GasNetwork.Optimism => await GetOptimismQuoteAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(network), network, null)
        };

        RecordQuote(quote);
        return quote;
    }

    public bool IsExecutionAffordable(GasNetwork network, decimal executionCostUsd)
    {
        var profile = network == GasNetwork.Optimism ? _config.Risk.Optimism : _config.Risk.Mainnet;
        return executionCostUsd <= profile.MaxExecutionUsd;
    }

    public IReadOnlyList<GasQuote> GetRecentQuotes(GasNetwork network)
    {
        if (_recentQuotes.TryGetValue(network, out var queue))
        {
            return queue.ToArray();
        }

        return Array.Empty<GasQuote>();
    }

    private async Task<GasQuote> GetMainnetQuoteAsync(CancellationToken cancellationToken)
    {
        var client = _clientFactory.CreateMainnetClient();
        var feeHistory = await client.Eth.FeeHistory.SendRequestAsync(
            new HexBigInteger(1),
            BlockParameter.CreateLatest(),
            new decimal[] { 55m });

        var baseFee = feeHistory.BaseFeePerGas?[0]?.Value ?? BigInteger.Zero;
        var reward = feeHistory.Reward?[0]?[0]?.Value ?? BigInteger.Zero;
        var total = baseFee + reward;

        return new GasQuote(
            GasNetwork.Mainnet,
            baseFee,
            reward,
            total,
            BigInteger.Zero,
            DateTimeOffset.UtcNow);
    }

    private async Task<GasQuote> GetOptimismQuoteAsync(CancellationToken cancellationToken)
    {
        var client = _clientFactory.CreateOptimismClient();
        var gasPriceResponse = await client.Eth.GasPrice.SendRequestAsync();
        var l2GasPrice = gasPriceResponse.Value;
        BigInteger l1GasPrice = BigInteger.Zero;

        try
        {
            var request = new RpcRequest(Guid.NewGuid().ToString(), "rollup_gasPrices", Array.Empty<object>());
            var response = await client.Client.SendRequestAsync<OptimismGasPriceResponse>(request);
            if (!string.IsNullOrWhiteSpace(response?.L1GasPrice))
            {
                l1GasPrice = HexStringToBigInteger(response.L1GasPrice);
            }

            if (!string.IsNullOrWhiteSpace(response?.L2GasPrice))
            {
                l2GasPrice = HexStringToBigInteger(response.L2GasPrice);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query Optimism rollup gas prices; using eth_gasPrice only");
        }

        return new GasQuote(
            GasNetwork.Optimism,
            l2GasPrice,
            BigInteger.Zero,
            l2GasPrice,
            l1GasPrice,
            DateTimeOffset.UtcNow);
    }

    private void RecordQuote(GasQuote quote)
    {
        var queue = _recentQuotes.GetOrAdd(quote.Network, _ => new ConcurrentQueue<GasQuote>());
        queue.Enqueue(quote);

        while (queue.Count > MaxSamplesPerNetwork && queue.TryDequeue(out _))
        {
        }
    }

    private static BigInteger HexStringToBigInteger(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BigInteger.Zero;
        }

        var span = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value.AsSpan(2)
            : value.AsSpan();

        if (span.IsEmpty)
        {
            return BigInteger.Zero;
        }

        return BigInteger.Parse(span, NumberStyles.HexNumber);
    }

    private sealed class OptimismGasPriceResponse
    {
        [JsonPropertyName("l1GasPrice")]
        public string? L1GasPrice { get; init; }

        [JsonPropertyName("l2GasPrice")]
        public string? L2GasPrice { get; init; }
    }
}

public enum GasNetwork
{
    Mainnet,
    Optimism
}

public readonly record struct GasQuote(
    GasNetwork Network,
    BigInteger BaseFeeWei,
    BigInteger PriorityFeeWei,
    BigInteger GasPriceWei,
    BigInteger L1DataPriceWei,
    DateTimeOffset Timestamp);
