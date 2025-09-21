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

        var executionCostUsd = opportunity.EstimatedGasUsd
            + opportunity.EstimatedL1DataUsd
            + opportunity.EstimatedFlashLoanFeeUsd;

        if (executionCostUsd > _config.Risk.MaxGasUsd)
        {
            _logger.LogInformation(
                "Opportunity {Opportunity} rejected: execution cost {CostUsd:F2} exceeds ceiling {Ceiling:F2}",
                opportunity.OpportunityId,
                executionCostUsd,
                _config.Risk.MaxGasUsd);
            return null;
        }

        var projectedNetProfit = opportunity.EstimatedProfitUsd - executionCostUsd;
        if (projectedNetProfit < _config.Risk.MinimumProfitUsd)
        {
            _logger.LogInformation(
                "Opportunity {Opportunity} rejected: net profit {NetProfit:F2} below threshold {Threshold:F2}",
                opportunity.OpportunityId,
                projectedNetProfit,
                _config.Risk.MinimumProfitUsd);
            return null;
        }

        // Optional additional guard: ensure current basefee is still compatible with the plan
        if (opportunity.EstimatedGasUnits > 0)
        {
            var l1Fee = await _gasOracle.GetL1PriorityFeeAsync(cancellationToken);
            _logger.LogDebug(
                "Current L1 base+priority fee {Fee} wei for opportunity {Opportunity}",
                l1Fee,
                opportunity.OpportunityId);
        }

        return opportunity;
    }
}