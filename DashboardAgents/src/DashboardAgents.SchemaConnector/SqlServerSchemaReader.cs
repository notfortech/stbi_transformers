using DashboardAgents.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DashboardAgents.SchemaConnector;

/// <summary>
/// Read-only SQL Server schema introspection via INFORMATION_SCHEMA and sys catalog views.
/// Requires only VIEW DEFINITION / SELECT permission on system views — no access to user data
/// beyond bounded DISTINCT sampling on low-cardinality columns.
/// </summary>
public sealed class SqlServerSchemaReader : IDbSchemaReader
{
    private const int LowCardinalityThreshold = 20;
    private const int MaxSampleValues = 15;

    private readonly ILogger<SqlServerSchemaReader> _logger;

    public DbProvider Provider => DbProvider.SqlServer;

    public SqlServerSchemaReader(ILogger<SqlServerSchemaReader> logger)
    {
        _logger = logger;
    }

    public async Task<SchemaSnapshot> ReadSchemaAsync(
        string connectionString,
        IReadOnlyCollection<string>? schemaFilter,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        var snapshot = new SchemaSnapshot
        {
            Provider = DbProvider.SqlServer,
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
        SqlConnection conn, IReadOnlyCollection<string>? schemaFilter, CancellationToken ct)
    {
        const string sql = @"
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
              AND TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
            ORDER BY TABLE_SCHEMA, TABLE_NAME;";

        var result = new List<TableMetadata>();
        await using var cmd = new SqlCommand(sql, conn);
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

    private static async Task<List<ColumnMetadata>> LoadColumnsAsync(SqlConnection conn, TableMetadata table, CancellationToken ct)
    {
        const string sql = @"
            SELECT c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE,
                   CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_PK
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                SELECT ku.TABLE_SCHEMA, ku.TABLE_NAME, ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                  ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ) pk ON pk.TABLE_SCHEMA = c.TABLE_SCHEMA AND pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
            WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
            ORDER BY c.ORDINAL_POSITION;";

        var result = new List<ColumnMetadata>();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", table.SchemaName);
        cmd.Parameters.AddWithValue("@table", table.TableName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ColumnMetadata
            {
                ColumnName = reader.GetString(0),
                DataType = reader.GetString(1),
                IsNullable = reader.GetString(2) == "YES",
                IsPrimaryKey = reader.GetInt32(3) == 1
            });
        }
        return result;
    }

    private static async Task<List<ForeignKeyMetadata>> LoadForeignKeysAsync(SqlConnection conn, TableMetadata table, CancellationToken ct)
    {
        const string sql = @"
            SELECT fk_cols.COLUMN_NAME AS FromColumn, pk_tab.TABLE_NAME AS ToTable, pk_cols.COLUMN_NAME AS ToColumn
            FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
            JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS fk_tab
              ON rc.CONSTRAINT_NAME = fk_tab.CONSTRAINT_NAME AND rc.CONSTRAINT_SCHEMA = fk_tab.CONSTRAINT_SCHEMA
            JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS pk_tab
              ON rc.UNIQUE_CONSTRAINT_NAME = pk_tab.CONSTRAINT_NAME AND rc.UNIQUE_CONSTRAINT_SCHEMA = pk_tab.CONSTRAINT_SCHEMA
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE fk_cols
              ON fk_cols.CONSTRAINT_NAME = fk_tab.CONSTRAINT_NAME AND fk_cols.TABLE_SCHEMA = fk_tab.TABLE_SCHEMA
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE pk_cols
              ON pk_cols.CONSTRAINT_NAME = pk_tab.CONSTRAINT_NAME AND pk_cols.ORDINAL_POSITION = fk_cols.ORDINAL_POSITION
            WHERE fk_tab.TABLE_SCHEMA = @schema AND fk_tab.TABLE_NAME = @table;";

        var result = new List<ForeignKeyMetadata>();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", table.SchemaName);
        cmd.Parameters.AddWithValue("@table", table.TableName);
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

    /// <summary>Uses catalog partition stats (not COUNT(*)) to avoid a full table scan on large tables.</summary>
    private static async Task<long?> LoadRowCountAsync(SqlConnection conn, TableMetadata table, CancellationToken ct)
    {
        const string sql = @"
            SELECT SUM(p.rows)
            FROM sys.partitions p
            JOIN sys.tables t ON p.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.name = @table AND p.index_id IN (0, 1);";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", table.SchemaName);
        cmd.Parameters.AddWithValue("@table", table.TableName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long l ? l : (result is int i ? i : null);
    }

    /// <summary>
    /// For columns likely to be status/enum flags (small VARCHAR/CHAR/BIT types), samples distinct
    /// values bounded by MaxSampleValues. Skipped entirely if distinct count exceeds the threshold —
    /// this never touches columns holding free-text, PII-shaped, or high-cardinality data.
    /// </summary>
    private async Task TrySampleLowCardinalityAsync(SqlConnection conn, TableMetadata table, ColumnMetadata column, CancellationToken ct)
    {
        if (!IsEnumCandidate(column.DataType)) return;

        try
        {
            var countSql = $"SELECT COUNT(DISTINCT [{column.ColumnName}]) FROM [{table.SchemaName}].[{table.TableName}];";
            await using var countCmd = new SqlCommand(countSql, conn);
            var distinctCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct) ?? 0L);
            column.DistinctValueCount = distinctCount;

            if (distinctCount == 0 || distinctCount > LowCardinalityThreshold) return;

            var sampleSql = $"SELECT DISTINCT TOP (@max) CONVERT(NVARCHAR(200), [{column.ColumnName}]) " +
                            $"FROM [{table.SchemaName}].[{table.TableName}] WHERE [{column.ColumnName}] IS NOT NULL;";
            await using var sampleCmd = new SqlCommand(sampleSql, conn);
            sampleCmd.Parameters.AddWithValue("@max", MaxSampleValues);
            await using var reader = await sampleCmd.ExecuteReaderAsync(ct);
            var values = new List<string>();
            while (await reader.ReadAsync(ct)) values.Add(reader.GetString(0));
            column.DistinctSampleValues = values;
        }
        catch (Exception ex)
        {
            // Sampling is best-effort enrichment, never fatal to introspection.
            _logger.LogWarning(ex, "Skipped low-cardinality sampling for {Table}.{Column}", table.QualifiedName, column.ColumnName);
        }
    }

    private static bool IsEnumCandidate(string sqlDataType) => sqlDataType.ToLowerInvariant() switch
    {
        "varchar" or "nvarchar" or "char" or "nchar" or "bit" or "tinyint" => true,
        _ => false
    };
}
