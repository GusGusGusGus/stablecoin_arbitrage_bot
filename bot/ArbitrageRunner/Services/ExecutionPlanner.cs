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

    public async Task<ArbitrageOpportunity?> ValidateAsync(ArbitrageOpportunity opportunity, CancellationToken cancellationToken)
    {
        if (opportunity.Calldata.Length == 0)
        {
            _logger.LogWarning("Opportunity {Opportunity} missing calldata", opportunity.OpportunityId);
            return null;
        }

        if (opportunity.EstimatedProfitUsd < _config.Risk.MinimumProfitUsd)
        {
            _logger.LogInformation("Opportunity {Opportunity} rejected: insufficient projected profit", opportunity.OpportunityId);
            return null;
        }

        if (!_gasOracle.IsGasAffordable(opportunity.EstimatedGasUsd))
        {
            _logger.LogInformation("Opportunity {Opportunity} rejected: gas cost exceeds threshold", opportunity.OpportunityId);
            return null;
        }

        await Task.CompletedTask;
        return opportunity;
    }
}