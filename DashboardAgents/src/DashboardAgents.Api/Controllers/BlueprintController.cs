using DashboardAgents.Api.Services;
using DashboardAgents.BlueprintAgent;
using DashboardAgents.Core.Models;
using DashboardAgents.SchemaConnector;
using Microsoft.AspNetCore.Mvc;

namespace DashboardAgents.Api.Controllers;

[ApiController]
[Route("api/blueprint")]
public sealed class BlueprintController : ControllerBase
{
    private readonly IBlueprintGenerationService _generationService;
    private readonly ISchemaReaderFactory _readerFactory;
    private readonly IBlueprintStore _store;
    private readonly ILogger<BlueprintController> _logger;

    public BlueprintController(
        IBlueprintGenerationService generationService,
        ISchemaReaderFactory readerFactory,
        IBlueprintStore store,
        ILogger<BlueprintController> logger)
    {
        _generationService = generationService;
        _readerFactory = readerFactory;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Generates a blueprint from requirements text or a pasted schema — the direct equivalent
    /// of the original tool's "Generate Blueprint" button.
    /// </summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(Blueprint), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Generate([FromBody] GenerateBlueprintRequest request, CancellationToken cancellationToken)
        => await GenerateInternal(request.Options, cancellationToken);

    /// <summary>
    /// End-to-end: connects to a live database, introspects its schema, formats it, and feeds
    /// it straight into the Blueprint Generation Agent — the full "connect and design" flow.
    /// </summary>
    [HttpPost("from-connection")]
    [ProducesResponseType(typeof(Blueprint), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(422)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> FromConnection([FromBody] ConnectAndGenerateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return BadRequest("connectionString is required.");

        SchemaSnapshot snapshot;
        try
        {
            var reader = _readerFactory.Resolve(request.Provider);
            snapshot = await reader.ReadSchemaAsync(request.ConnectionString, request.SchemaFilter, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live schema introspection failed for provider {Provider}", request.Provider);
            return Problem(
                title: "Schema introspection failed",
                detail: "Could not connect to or introspect the specified database.",
                statusCode: 502);
        }

        var options = request.Options;
        options.Mode = "schema";
        options.SchemaText = SchemaTextFormatter.Format(snapshot);

        return await GenerateInternal(options, cancellationToken);
    }

    /// <summary>Retrieves a previously generated blueprint by id — used by the tweak agent flow.</summary>
    [HttpGet("{blueprintId}")]
    [ProducesResponseType(typeof(Blueprint), 200)]
    [ProducesResponseType(404)]
    public IActionResult Get(string blueprintId)
    {
        var blueprint = _store.Get(blueprintId);
        return blueprint is null ? NotFound() : Ok(blueprint);
    }

    private async Task<IActionResult> GenerateInternal(BlueprintGenerationOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var blueprint = await _generationService.GenerateAsync(options, cancellationToken);
            _store.Save(blueprint);
            return Ok(blueprint);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (BlueprintValidationException ex)
        {
            _logger.LogWarning("Blueprint generation produced an invalid blueprint: {Violations}", string.Join(" | ", ex.Violations));
            return UnprocessableEntity(new { message = "Generated blueprint failed validation.", violations = ex.Violations });
        }
        catch (BlueprintParseException ex)
        {
            _logger.LogError(ex, "Failed to parse blueprint generation response");
            return Problem(title: "Blueprint generation failed", detail: ex.Message, statusCode: 502);
        }
    }
}
