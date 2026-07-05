using DashboardAgents.Core.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DashboardAgents.SchemaConnector;

/// <summary>
/// Read-only PostgreSQL schema introspection via information_schema and pg_catalog.
/// Row counts come from pg_stat_user_tables estimates (no full table scan).
/// </summary>
public sealed class PostgresSchemaReader : IDbSchemaReader
{
    private const int LowCardinalityThreshold = 20;
    private const int MaxSampleValues = 15;

    private readonly ILogger<PostgresSchemaReader> _logger;

    public DbProvider Provider => DbProvider.PostgreSql;

    public PostgresSchemaReader(ILogger<PostgresSchemaReader> logger)
    {
        _logger = logger;
    }

    public async Task<SchemaSnapshot> ReadSchemaAsync(
        string connectionString,
        IReadOnlyCollection<string>? schemaFilter,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        var snapshot = new SchemaSnapshot
        {
            Provider = DbProvider.PostgreSql,
            DatabaseName = conn.Database
        };

        var tables = await LoadTablesAsync(conn, schemaFilter, cancellationToken);

        foreach (var table in tables)
        {
            table.Columns = await LoadColumnsAsync(conn, table, cancellationToken);
            table.ForeignKeys = await LoadForeignKeysAsync(conn, table, cancellationToken);
            table.ApproximateRowCount = await LoadRowCountAsync(conn, table, cancellationToken);

            foreach (var column in table.Columns)
            {
                await TrySampleLowCardinalityAsync(conn, table, column, cancellationToken);
            }
        }

        snapshot.Tables = tables;
        return snapshot;
    }

    private static async Task<List<TableMetadata>> LoadTablesAsync(
        NpgsqlConnection conn, IReadOnlyCollection<string>? schemaFilter, CancellationToken ct)
    {
        const string sql = @"
            SELECT table_schema, table_name
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE'
              AND table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY table_schema, table_name;";

        var result = new List<TableMetadata>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var schemaName = reader.GetString(0);
            if (schemaFilter is { Count: > 0 } && !schemaFilter.Contains(schemaName, StringComparer.OrdinalIgnoreCase))
                continue;

            result.Add(new TableMetadata { SchemaName = schemaName, TableName = reader.GetString(1) });
        }
        return result;
    }

    private static async Task<List<ColumnMetadata>> LoadColumnsAsync(NpgsqlConnection conn, TableMetadata table, CancellationToken ct)
    {
        const string sql = @"
            SELECT c.column_name, c.data_type, c.is_nullable,
                   EXISTS (
                       SELECT 1
                       FROM information_schema.table_constraints tc
                       JOIN information_schema.key_column_usage kcu
                         ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                       WHERE tc.constraint_type = 'PRIMARY KEY'
                         AND tc.table_schema = c.table_schema AND tc.table_name = c.table_name
                         AND kcu.column_name = c.column_name
                   ) AS is_pk
            FROM information_schema.columns c
            WHERE c.table_schema = @schema AND c.table_name = @table
            ORDER BY c.ordinal_position;";

        var result = new List<ColumnMetadata>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", table.SchemaName);
        cmd.Parameters.AddWithValue("table", table.TableName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ColumnMetadata
            {
                ColumnName = reader.GetString(0),
                DataType = reader.GetString(1),
                IsNullable = reader.GetString(2) == "YES",
                IsPrimaryKey = reader.GetBoolean(3)
            });
        }
        return result;
    }

    private static async Task<List<ForeignKeyMetadata>> LoadForeignKeysAsync(NpgsqlConnection conn, TableMetadata table, CancellationToken ct)
    {
        const string sql = @"
            SELECT kcu.column_name AS from_column, ccu.table_name AS to_table, ccu.column_name AS to_column
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON tc.constraint_name = ccu.constraint_name AND tc.table_schema = ccu.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = @schema AND tc.table_name = @table;";

        var result = new List<ForeignKeyMetadata>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", table.SchemaName);
        cmd.Parameters.AddWithValue("table", table.TableName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ForeignKeyMetadata
            {
                FromColumn = reader.GetString(0),
                ToTable = reader.GetString(1),
                ToColumn = reader.GetString(2)
            });
        }
        return result;
    }

    /// <summary>Uses planner row-count estimates from pg_class, not COUNT(*), to avoid a full table scan.</summary>
    private static async Task<long?> LoadRowCountAsync(NpgsqlConnection conn, TableMetadata table, CancellationToken ct)
    {
        const string sql = @"
            SELECT c.reltuples::BIGINT
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relname = @table;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", table.SchemaName);
        cmd.Parameters.AddWithValue("table", table.TableName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as long?;
    }

    private async Task TrySampleLowCardinalityAsync(NpgsqlConnection conn, TableMetadata table, ColumnMetadata column, CancellationToken ct)
    {
        if (!IsEnumCandidate(column.DataType)) return;

        try
        {
            var countSql = $"SELECT COUNT(DISTINCT \"{column.ColumnName}\") FROM \"{table.SchemaName}\".\"{table.TableName}\";";
            await using var countCmd = new NpgsqlCommand(countSql, conn);
            var distinctCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct) ?? 0L);
            column.DistinctValueCount = distinctCount;

            if (distinctCount == 0 || distinctCount > LowCardinalityThreshold) return;

            var sampleSql = $"SELECT DISTINCT \"{column.ColumnName}\"::TEXT FROM \"{table.SchemaName}\".\"{table.TableName}\" " +
                            $"WHERE \"{column.ColumnName}\" IS NOT NULL LIMIT @max;";
            await using var sampleCmd = new NpgsqlCommand(sampleSql, conn);
            sampleCmd.Parameters.AddWithValue("max", MaxSampleValues);
            await using var reader = await sampleCmd.ExecuteReaderAsync(ct);
            var values = new List<string>();
            while (await reader.ReadAsync(ct)) values.Add(reader.GetString(0));
            column.DistinctSampleValues = values;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skipped low-cardinality sampling for {Table}.{Column}", table.QualifiedName, column.ColumnName);
        }
    }

    private static bool IsEnumCandidate(string pgDataType) => pgDataType.ToLowerInvariant() switch
    {
        "character varying" or "character" or "text" or "boolean" or "smallint" or "USER-DEFINED" => true,
        _ => false
    };
}
