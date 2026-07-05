using DashboardAgents.Core.Models;

namespace DashboardAgents.SchemaConnector;

public interface ISchemaReaderFactory
{
    IDbSchemaReader Resolve(DbProvider provider);
}

public sealed class SchemaReaderFactory : ISchemaReaderFactory
{
    private readonly IEnumerable<IDbSchemaReader> _readers;

    public SchemaReaderFactory(IEnumerable<IDbSchemaReader> readers)
    {
        _readers = readers;
    }

    public IDbSchemaReader Resolve(DbProvider provider)
    {
        var reader = _readers.FirstOrDefault(r => r.Provider == provider);
        if (reader is null)
            throw new NotSupportedException($"No schema reader registered for provider '{provider}'.");
        return reader;
    }
}
