using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Models;
using Microsoft.Extensions.Logging;

namespace ArbitrageRunner.Services;

public sealed class ExecutionPlanner
{
    private readonly GasOracleService _gasOracle;
    private readonly AppConfig _config;
    private readonly ILogger<ExecutionPlanner> _logger;

    public ExecutionPlanner(GasOracleService gasOracle, AppConfig config, ILogger<ExecutionPlanner> logger)
    {
        _gasOracle = gasOracle;
        _config = config;
        _logger = logger;
    }

    public async Task<ArbitrageOpportunity?> ValidateAsync(
        ArbitrageOpportunity opportunity,
        CancellationToken cancellationToken)
    {
        if (opportunity.Calldata.Length == 0)
        {
            _logger.LogWarning("Opportunity {Opportunity} missing calldata", opportunity.OpportunityId);
            return null;
        }

        var network = opportunity.ExecuteOnOptimism ? GasNetwork.Optimism : GasNetwork.Mainnet;
        var profile = opportunity.ExecuteOnOptimism ? _config.Risk.Optimism : _config.Risk.Mainnet;

        var gasQuote = await _gasOracle.GetGasQuoteAsync(network, cancellationToken);

        var baseFeeUpperBound = ComputeBaseFeeUpperBound(gasQuote.GasPriceWei, _config.Risk.BaseFeeSafetyMultiplier);

        var executionCostUsd = opportunity.EstimatedGasUsd * _config.Risk.PriorityFeeMultiplier
            + opportunity.EstimatedL1DataUsd
            + opportunity.EstimatedFlashLoanFeeUsd
            + _config.Risk.RelayerFeeUsd
            + _config.Risk.MevProtectionFeeUsd;

        if (!_gasOracle.IsExecutionAffordable(network, executionCostUsd))
        {
            _logger.LogInformation(
                "Opportunity {Opportunity} rejected: execution cost {CostUsd:F2} exceeds allowed {Ceiling:F2} on {Network}",
                opportunity.OpportunityId,
                executionCostUsd,
                profile.MaxExecutionUsd,
                network);
            return null;
        }

        var sandwichBufferUsd = (opportunity.EstimatedProfitUsd * _config.Risk.SandwichBufferBps) / 10_000m;
        var projectedNetProfit = opportunity.EstimatedProfitUsd - executionCostUsd - sandwichBufferUsd;
        if (projectedNetProfit < profile.MinimumProfitUsd)
        {
            _logger.LogInformation(
                "Opportunity {Opportunity} rejected: net profit {NetProfit:F2} below threshold {Threshold:F2}",
                opportunity.OpportunityId,
                projectedNetProfit,
                profile.MinimumProfitUsd);
            return null;
        }

        var enriched = opportunity with
        {
            BaseFeeUpperBoundWei = baseFeeUpperBound
        };

        _logger.LogDebug(
            "Opportunity {Opportunity} validated with execution cost {CostUsd:F2} USD and net profit {NetProfit:F2} USD",
            enriched.OpportunityId,
            executionCostUsd,
            projectedNetProfit);

        return enriched;
    }

    private static BigInteger ComputeBaseFeeUpperBound(BigInteger gasPriceWei, decimal multiplier)
    {
        if (multiplier <= 1m)
        {
            return gasPriceWei;
        }

        var scaled = (BigInteger)Math.Ceiling(multiplier * 1_000m);
        return (gasPriceWei * scaled) / 1_000;
    }
}