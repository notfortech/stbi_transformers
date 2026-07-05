using DashboardAgents.Core.Models;

namespace DashboardAgents.SchemaConnector;

/// <summary>
/// Contract for a read-only schema introspection provider.
/// Implementations must never execute anything beyond metadata queries and bounded
/// DISTINCT/COUNT sampling on low-cardinality columns — never SELECT * over row data.
/// </summary>
public interface IDbSchemaReader
{
    DbProvider Provider { get; }

    Task<SchemaSnapshot> ReadSchemaAsync(
        string connectionString,
        IReadOnlyCollection<string>? schemaFilter,
        CancellationToken cancellationToken = default);
}
