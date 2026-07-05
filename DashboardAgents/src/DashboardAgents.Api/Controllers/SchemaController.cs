using DashboardAgents.Core.Models;
using DashboardAgents.SchemaConnector;
using Microsoft.AspNetCore.Mvc;

namespace DashboardAgents.Api.Controllers;

[ApiController]
[Route("api/schema")]
public sealed class SchemaController : ControllerBase
{
    private readonly ISchemaReaderFactory _readerFactory;
    private readonly ILogger<SchemaController> _logger;

    public SchemaController(ISchemaReaderFactory readerFactory, ILogger<SchemaController> logger)
    {
        _readerFactory = readerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Connects to a live database (read-only) and returns its structural metadata plus the
    /// formatted schema-text block the Blueprint Generator consumes. Does not generate a
    /// blueprint — use POST /api/blueprint/from-connection for the end-to-end flow.
    /// </summary>
    [HttpPost("introspect")]
    [ProducesResponseType(typeof(IntrospectResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Introspect([FromBody] IntrospectRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return BadRequest("connectionString is required.");

        try
        {
            var reader = _readerFactory.Resolve(request.Provider);
            var snapshot = await reader.ReadSchemaAsync(request.ConnectionString, request.SchemaFilter, cancellationToken);
            var schemaText = SchemaTextFormatter.Format(snapshot);

            return Ok(new IntrospectResponse
            {
                Snapshot = snapshot,
                SchemaText = schemaText
            });
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema introspection failed for provider {Provider}", request.Provider);
            return Problem(
                title: "Schema introspection failed",
                detail: "Could not connect to or introspect the specified database. Verify the connection string and that the credentials have read access to schema metadata.",
                statusCode: 502);
        }
    }
}

public sealed class IntrospectResponse
{
    public SchemaSnapshot Snapshot { get; set; } = new();
    public string SchemaText { get; set; } = "";
}
