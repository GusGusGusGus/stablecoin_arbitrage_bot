using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Infrastructure;
using ArbitrageRunner.Models;
using Microsoft.Extensions.Logging;

namespace ArbitrageRunner.Services;

public sealed class BacktestService
{
    private readonly SnapshotStore _snapshotStore;
    private readonly ILogger<BacktestService> _logger;

    public BacktestService(SnapshotStore snapshotStore, ILogger<BacktestService> logger)
    {
        _snapshotStore = snapshotStore;
        _logger = logger;
    }

    public Task<IReadOnlyList<ArbitrageOpportunity>> LoadRecentAsync(int? limit, CancellationToken cancellationToken)
        => LoadInternalAsync(opportunityId: null, limit, cancellationToken);

    public Task<IReadOnlyList<ArbitrageOpportunity>> LoadByIdAsync(string opportunityId, CancellationToken cancellationToken)
        => LoadInternalAsync(opportunityId, limit: 1, cancellationToken);

    public Task PersistAsync(ArbitrageOpportunity opportunity, CancellationToken cancellationToken)
        => _snapshotStore.UpsertAsync(opportunity, cancellationToken);

    private async Task<IReadOnlyList<ArbitrageOpportunity>> LoadInternalAsync(
        string? opportunityId,
        int? limit,
        CancellationToken cancellationToken)
    {
        await _snapshotStore.InitializeAsync(cancellationToken);
        var snapshots = await _snapshotStore.GetSnapshotsAsync(opportunityId, limit, cancellationToken);

        if (snapshots.Count == 0)
        {
            _logger.LogWarning("No snapshots found for id {OpportunityId}", opportunityId);
        }

        return snapshots;
    }
}