using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Infrastructure;
using ArbitrageRunner.Models;
using Microsoft.Extensions.Logging;
using Nethereum.RPC.Eth.DTOs;

namespace ArbitrageRunner.Services;

public sealed class GasOracleService
{
    private readonly EthereumClientFactory _clientFactory;
    private readonly AppConfig _config;
    private readonly ILogger<GasOracleService> _logger;

    public GasOracleService(EthereumClientFactory clientFactory, AppConfig config, ILogger<GasOracleService> logger)
    {
        _clientFactory = clientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<BigInteger> GetL1PriorityFeeAsync(CancellationToken cancellationToken)
    {
        var client = _clientFactory.CreateMainnetClient();
        var feeHistory = await client.Eth.FeeHistory.SendRequestAsync(1, BlockParameter.CreateLatest(), new[] { 50d });
        var reward = feeHistory.Reward?[0]?[0] ?? 0m;
        var baseFee = feeHistory.BaseFeePerGas?[0] ?? 0;
        var estimate = (BigInteger)baseFee + (BigInteger)reward;
        _logger.LogDebug("Estimated gas price {Gas}", estimate);
        return estimate;
    }

    public bool IsGasAffordable(decimal estimatedGasUsd)
    {
        return estimatedGasUsd <= _config.Risk.MaxGasUsd;
    }
}