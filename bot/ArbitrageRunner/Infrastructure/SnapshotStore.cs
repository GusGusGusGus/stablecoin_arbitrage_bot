using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ArbitrageRunner.Infrastructure;

public sealed class SnapshotStore
{
    private const string TableName = "arbitrage_snapshots";

    private readonly string _connectionString;
    private readonly HistoricalDataConfig _settings;
    private readonly ILogger<SnapshotStore> _logger;

    public SnapshotStore(AppConfig config, ILogger<SnapshotStore> logger)
    {
        _settings = config.HistoricalData;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.DatabasePath))
        {
            throw new InvalidOperationException("Historical data database path must be configured");
        }

        var directory = Path.GetDirectoryName(_settings.DatabasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _settings.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        _connectionString = builder.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (!_settings.AutoMigrate)
        {
            return;
        }

        var commandText = $"""
        CREATE TABLE IF NOT EXISTS {TableName} (
            opportunity_id TEXT PRIMARY KEY,
            borrow_asset TEXT NOT NULL,
            borrow_amount TEXT NOT NULL,
            minimum_profit TEXT NOT NULL,
            route_targets TEXT NOT NULL,
            calldata TEXT NOT NULL,
            estimated_profit_usd REAL NOT NULL,
            estimated_gas_usd REAL NOT NULL,
            estimated_l1_data_usd REAL NOT NULL DEFAULT 0,
            estimated_flash_loan_fee_usd REAL NOT NULL DEFAULT 0,
            estimated_gas_units INTEGER NOT NULL DEFAULT 0,
            flash_loan_fee_bps INTEGER NOT NULL DEFAULT 9,
            execute_on_optimism INTEGER NOT NULL DEFAULT 0,
            base_fee_upper_bound_wei TEXT NOT NULL DEFAULT '0',
            deadline INTEGER NOT NULL,
            captured_at INTEGER NOT NULL DEFAULT (strftime('%s','now'))
        );
        CREATE INDEX IF NOT EXISTS IX_{TableName}_captured_at ON {TableName}(captured_at DESC);
        CREATE INDEX IF NOT EXISTS IX_{TableName}_borrow_asset ON {TableName}(borrow_asset);
        """;

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAsync(ArbitrageOpportunity snapshot, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
        INSERT INTO {TableName} (
            opportunity_id,
            borrow_asset,
            borrow_amount,
            minimum_profit,
            route_targets,
            calldata,
            estimated_profit_usd,
            estimated_gas_usd,
            estimated_l1_data_usd,
            estimated_flash_loan_fee_usd,
            estimated_gas_units,
            flash_loan_fee_bps,
            execute_on_optimism,
            base_fee_upper_bound_wei,
            deadline,
            captured_at
        ) VALUES (
            @opportunity_id,
            @borrow_asset,
            @borrow_amount,
            @minimum_profit,
            @route_targets,
            @calldata,
            @estimated_profit_usd,
            @estimated_gas_usd,
            @estimated_l1_data_usd,
            @estimated_flash_loan_fee_usd,
            @estimated_gas_units,
            @flash_loan_fee_bps,
            @execute_on_optimism,
            @base_fee_upper_bound_wei,
            @deadline,
            strftime('%s','now')
        )
        ON CONFLICT(opportunity_id) DO UPDATE SET
            borrow_asset=excluded.borrow_asset,
            borrow_amount=excluded.borrow_amount,
            minimum_profit=excluded.minimum_profit,
            route_targets=excluded.route_targets,
            calldata=excluded.calldata,
            estimated_profit_usd=excluded.estimated_profit_usd,
            estimated_gas_usd=excluded.estimated_gas_usd,
            estimated_l1_data_usd=excluded.estimated_l1_data_usd,
            estimated_flash_loan_fee_usd=excluded.estimated_flash_loan_fee_usd,
            estimated_gas_units=excluded.estimated_gas_units,
            flash_loan_fee_bps=excluded.flash_loan_fee_bps,
            execute_on_optimism=excluded.execute_on_optimism,
            base_fee_upper_bound_wei=excluded.base_fee_upper_bound_wei,
            deadline=excluded.deadline,
            captured_at=excluded.captured_at;
        """;

        BindSnapshotParameters(command, snapshot);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ArbitrageOpportunity>> GetSnapshotsAsync(
        string? opportunityId,
        int? limit,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        if (!string.IsNullOrWhiteSpace(opportunityId))
        {
            command.CommandText = $"SELECT * FROM {TableName} WHERE opportunity_id = @opportunity_id LIMIT 1;";
            command.Parameters.AddWithValue("@opportunity_id", opportunityId);
        }
        else
        {
            var limitClause = limit.HasValue ? " LIMIT @limit" : string.Empty;
            command.CommandText = $"SELECT * FROM {TableName} ORDER BY captured_at DESC{limitClause};";
            if (limit.HasValue)
            {
                command.Parameters.AddWithValue("@limit", limit.Value);
            }
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<ArbitrageOpportunity>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadSnapshot(reader));
        }

        return results;
    }

    private static void BindSnapshotParameters(SqliteCommand command, ArbitrageOpportunity snapshot)
    {
        command.Parameters.AddWithValue("@opportunity_id", snapshot.OpportunityId);
        command.Parameters.AddWithValue("@borrow_asset", snapshot.BorrowAsset);
        command.Parameters.AddWithValue("@borrow_amount", snapshot.BorrowAmount.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@minimum_profit", snapshot.MinimumProfit.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@route_targets", JsonSerializer.Serialize(snapshot.RouteTargets));
        var calldata = JsonSerializer.Serialize(EncodeCalldata(snapshot.Calldata));
        command.Parameters.AddWithValue("@calldata", calldata);
        command.Parameters.AddWithValue("@estimated_profit_usd", snapshot.EstimatedProfitUsd);
        command.Parameters.AddWithValue("@estimated_gas_usd", snapshot.EstimatedGasUsd);
        command.Parameters.AddWithValue("@estimated_l1_data_usd", snapshot.EstimatedL1DataUsd);
        command.Parameters.AddWithValue("@estimated_flash_loan_fee_usd", snapshot.EstimatedFlashLoanFeeUsd);
        command.Parameters.AddWithValue("@estimated_gas_units", snapshot.EstimatedGasUnits);
        command.Parameters.AddWithValue("@flash_loan_fee_bps", snapshot.FlashLoanFeeBps);
        command.Parameters.AddWithValue("@execute_on_optimism", snapshot.ExecuteOnOptimism ? 1 : 0);
        command.Parameters.AddWithValue("@base_fee_upper_bound_wei", snapshot.BaseFeeUpperBoundWei.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@deadline", snapshot.Deadline);
    }

    private static ArbitrageOpportunity ReadSnapshot(IDataRecord record)
    {
        var routeTargetsJson = record.GetString(record.GetOrdinal("route_targets"));
        var calldataJson = record.GetString(record.GetOrdinal("calldata"));

        var routeTargets = JsonSerializer.Deserialize<string[]>(routeTargetsJson) ?? Array.Empty<string>();
        var encodedCalldata = JsonSerializer.Deserialize<string[]>(calldataJson) ?? Array.Empty<string>();

        return new ArbitrageOpportunity
        {
            OpportunityId = record.GetString(record.GetOrdinal("opportunity_id")),
            BorrowAsset = record.GetString(record.GetOrdinal("borrow_asset")),
            BorrowAmount = BigInteger.Parse(record.GetString(record.GetOrdinal("borrow_amount"))),
            MinimumProfit = BigInteger.Parse(record.GetString(record.GetOrdinal("minimum_profit"))),
            RouteTargets = routeTargets,
            Calldata = DecodeCalldata(encodedCalldata),
            EstimatedProfitUsd = Convert.ToDecimal(record["estimated_profit_usd"]),
            EstimatedGasUsd = Convert.ToDecimal(record["estimated_gas_usd"]),
            EstimatedL1DataUsd = Convert.ToDecimal(record["estimated_l1_data_usd"]),
            EstimatedFlashLoanFeeUsd = Convert.ToDecimal(record["estimated_flash_loan_fee_usd"]),
            EstimatedGasUnits = Convert.ToUInt32(record["estimated_gas_units"]),
            FlashLoanFeeBps = Convert.ToUInt32(record["flash_loan_fee_bps"]),
            ExecuteOnOptimism = Convert.ToInt32(record["execute_on_optimism"]) == 1,
            BaseFeeUpperBoundWei = BigInteger.Parse(record["base_fee_upper_bound_wei"].ToString() ?? "0"),
            Deadline = (ulong)Convert.ToInt64(record["deadline"])
        };
    }

    private static string[] EncodeCalldata(IReadOnlyList<byte[]> data)
    {
        var result = new string[data.Count];
        for (var i = 0; i < data.Count; i++)
        {
            result[i] = Convert.ToBase64String(data[i]);
        }

        return result;
    }

    private static byte[][] DecodeCalldata(IReadOnlyList<string> encoded)
    {
        var result = new byte[encoded.Count][];
        for (var i = 0; i < encoded.Count; i++)
        {
            result[i] = Convert.FromBase64String(encoded[i]);
        }

        return result;
    }
}