using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ArbitrageRunner.Models;

namespace ArbitrageRunner.Services;

public sealed class RunTelemetryService
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, RunTelemetryEntry> _runs = new();
    private readonly LinkedList<Guid> _runOrder = new();
    private decimal _totalProfitUsd;
    private decimal _totalCostUsd;
    private decimal _totalInvestedUsd;
    private int _activeBots;
    private int _selectedBots = 1;

    public event EventHandler<RunTelemetryEntry>? RunUpdated;

    public decimal TotalProfitUsd
    {
        get
        {
            lock (_sync)
            {
                return _totalProfitUsd;
            }
        }
    }

    public decimal TotalCostUsd
    {
        get
        {
            lock (_sync)
            {
                return _totalCostUsd;
            }
        }
    }

    public decimal TotalInvestedUsd
    {
        get
        {
            lock (_sync)
            {
                return _totalInvestedUsd;
            }
        }
    }

    public int ActiveBots
    {
        get
        {
            lock (_sync)
            {
                return _activeBots;
            }
        }
    }

    public int SelectedBots
    {
        get
        {
            lock (_sync)
            {
                return _selectedBots;
            }
        }
        set
        {
            lock (_sync)
            {
                _selectedBots = Math.Max(1, value);
            }
        }
    }

    public IReadOnlyList<RunTelemetryEntry> GetRuns()
    {
        lock (_sync)
        {
            return _runOrder.Select(id => _runs[id]).ToList();
        }
    }

    public RunTelemetryEntry RecordRunScheduled(RunnerMode mode, ArbitrageOpportunity opportunity, RunTelemetryMetadata metadata)
    {
        var runId = Guid.NewGuid();
        var entry = new RunTelemetryEntry
        {
            RunId = runId,
            Mode = mode,
            OpportunityId = opportunity.OpportunityId,
            BorrowAsset = opportunity.BorrowAsset,
            BorrowAmount = opportunity.BorrowAmount,
            EstimatedProfitUsd = opportunity.EstimatedProfitUsd,
            ExecutionCostUsd = opportunity.ExecutionCostEstimateUsd,
            ProjectedNetProfitUsd = opportunity.ProjectedNetProfitUsd,
            AppFeeAmount = opportunity.AppFeeAmount,
            AppFeePercentage = opportunity.AppFeePercentage,
            AppFeeEnabled = opportunity.AppFeeEnabled,
            RouteTargets = opportunity.RouteTargets,
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Scheduled,
            Dex = metadata.Dex,
            Network = metadata.Network,
            BaseCoin = metadata.BaseCoin,
            QuoteCoin = metadata.QuoteCoin,
            ProcessId = metadata.ProcessId
        };

        lock (_sync)
        {
            _runs[runId] = entry;
            _runOrder.AddFirst(runId);
            _activeBots++;
            _totalInvestedUsd += EstimateInvestedUsd(opportunity.BorrowAmount);
        }

        RunUpdated?.Invoke(this, entry);
        return entry;
    }

    public void MarkRunExecuting(Guid runId)
    {
        UpdateRun(runId, entry => entry with
        {
            Status = RunStatus.Executing,
            LastUpdatedAt = DateTimeOffset.UtcNow
        });
    }

    public void MarkRunSucceeded(Guid runId, string transactionHash)
    {
        UpdateRun(runId, entry => entry with
        {
            Status = RunStatus.Succeeded,
            CompletedAt = DateTimeOffset.UtcNow,
            TransactionHash = transactionHash,
            LastUpdatedAt = DateTimeOffset.UtcNow
        }, afterUpdate: updated =>
        {
            lock (_sync)
            {
                _totalProfitUsd += updated.ProjectedNetProfitUsd;
                _totalCostUsd += updated.ExecutionCostUsd;
                _activeBots = Math.Max(0, _activeBots - 1);
            }
        });
    }

    public void MarkRunFailed(Guid runId, string error)
    {
        UpdateRun(runId, entry => entry with
        {
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = error,
            LastUpdatedAt = DateTimeOffset.UtcNow
        }, afterUpdate: updated =>
        {
            lock (_sync)
            {
                _totalCostUsd += updated.ExecutionCostUsd;
                _activeBots = Math.Max(0, _activeBots - 1);
            }
        });
    }

    public void MarkRunCancelled(Guid runId)
    {
        UpdateRun(runId, entry => entry with
        {
            Status = RunStatus.Cancelled,
            CompletedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        }, afterUpdate: _ =>
        {
            lock (_sync)
            {
                _activeBots = Math.Max(0, _activeBots - 1);
            }
        });
    }

    private void UpdateRun(Guid runId, Func<RunTelemetryEntry, RunTelemetryEntry> update, Action<RunTelemetryEntry>? afterUpdate = null)
    {
        RunTelemetryEntry? updated = null;
        lock (_sync)
        {
            if (!_runs.TryGetValue(runId, out var existing))
            {
                return;
            }

            updated = update(existing);
            _runs[runId] = updated;
        }

        if (updated is not null)
        {
            afterUpdate?.Invoke(updated);
            RunUpdated?.Invoke(this, updated);
        }
    }

    private static decimal EstimateInvestedUsd(BigInteger borrowAmount)
    {
        if (borrowAmount.IsZero)
        {
            return 0m;
        }

        if (borrowAmount > new BigInteger(decimal.MaxValue))
        {
            return decimal.MaxValue;
        }

        var asDecimal = (decimal)borrowAmount;
        return asDecimal / 1_000_000m;
    }
}

public static class RunStatus
{
    public const string Scheduled = "Scheduled";
    public const string Executing = "Executing";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public sealed record RunTelemetryEntry
{
    public Guid RunId { get; init; }
    public RunnerMode Mode { get; init; }
    public string OpportunityId { get; init; } = string.Empty;
    public string BorrowAsset { get; init; } = string.Empty;
    public BigInteger BorrowAmount { get; init; }
    public decimal EstimatedProfitUsd { get; init; }
    public decimal ExecutionCostUsd { get; init; }
    public decimal ProjectedNetProfitUsd { get; init; }
    public BigInteger AppFeeAmount { get; init; }
    public decimal AppFeePercentage { get; init; }
    public bool AppFeeEnabled { get; init; }
    public string[] RouteTargets { get; init; } = Array.Empty<string>();
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? LastUpdatedAt { get; init; }
    public string Status { get; init; } = RunStatus.Scheduled;
    public string? TransactionHash { get; init; }
    public string? Error { get; init; }
    public string Dex { get; init; } = string.Empty;
    public string Network { get; init; } = string.Empty;
    public string BaseCoin { get; init; } = string.Empty;
    public string QuoteCoin { get; init; } = string.Empty;
    public string ProcessId { get; init; } = string.Empty;
}

public sealed record RunTelemetryMetadata
{
    public string ProcessId { get; init; } = RunControlService.DefaultKey;
    public string Dex { get; init; } = "Auto";
    public string Network { get; init; } = "Mainnet";
    public string BaseCoin { get; init; } = "USDC";
    public string QuoteCoin { get; init; } = string.Empty;

    public static RunTelemetryMetadata Default { get; } = new();
}
