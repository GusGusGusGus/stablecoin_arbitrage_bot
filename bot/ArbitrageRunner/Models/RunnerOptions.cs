using System;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ArbitrageRunner.Models;

public enum RunnerMode
{
    Loop,
    OnDemand,
    Backtest
}

public sealed record RunnerOptions
{
    public RunnerMode Mode { get; init; }
    public string? OpportunityId { get; init; }
    public string? SnapshotFile { get; init; }
    public string? OpportunityPayload { get; init; }
    public bool ExecuteOnOptimism { get; init; }

    public static RunnerOptions FromConfiguration(IConfiguration configuration)
    {
        var modeValue = configuration["mode"] ?? "loop";
        var mode = modeValue.ToLowerInvariant() switch
        {
            "loop" => RunnerMode.Loop,
            "on-demand" or "ondemand" => RunnerMode.OnDemand,
            "backtest" => RunnerMode.Backtest,
            _ => RunnerMode.Loop
        };

        return new RunnerOptions
        {
            Mode = mode,
            OpportunityId = configuration["opportunity"],
            SnapshotFile = configuration["snapshot"],
            OpportunityPayload = configuration["opportunityPayload"],
            ExecuteOnOptimism = string.Equals(configuration["network"], "optimism", StringComparison.OrdinalIgnoreCase)
        };
    }
}
