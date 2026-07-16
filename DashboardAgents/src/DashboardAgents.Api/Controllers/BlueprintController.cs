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
    private readonly ITmdlAuthoringService _tmdlAuthoring;
    private readonly ITmdlValidationService _tmdlValidation;
    private readonly ISchemaReaderFactory _readerFactory;
    private readonly IBlueprintStore _store;
    private readonly ILogger<BlueprintController> _logger;

    public BlueprintController(
        IBlueprintGenerationService generationService,
        ITmdlAuthoringService tmdlAuthoring,
        ITmdlValidationService tmdlValidation,
        ISchemaReaderFactory readerFactory,
        IBlueprintStore store,
        ILogger<BlueprintController> logger)
    {
        _generationService = generationService;
        _tmdlAuthoring = tmdlAuthoring;
        _tmdlValidation = tmdlValidation;
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

    /// <summary>
    /// S7 — converts an already-generated blueprint into a TMDL semantic model definition
    /// (database.tmdl, model.tmdl, relationships.tmdl, expressions.tmdl, cultures/en-US.tmdl,
    /// tables/*.tmdl). Deliberately separate from generation: only an already-approved blueprint
    /// should reach this step. Output is proposed, not validated or deployed — see S8.
    /// </summary>
    [HttpPost("{blueprintId}/author-tmdl")]
    [ProducesResponseType(typeof(TmdlAuthoringResult), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> AuthorTmdl(string blueprintId, CancellationToken cancellationToken)
    {
        var blueprint = _store.Get(blueprintId);
        if (blueprint is null)
            return NotFound($"No blueprint found with id '{blueprintId}'. Generate one first via POST /api/blueprint/generate.");

        return await AuthorTmdlInternal(blueprint, cancellationToken);
    }

    /// <summary>
    /// S9 — same as above, but the blueprint is sent directly in the request body instead of
    /// looked up by id. Needed because koru-main's actual AI-assisted flow calls
    /// PipelineController.Generate, which returns its Blueprint straight in the HTTP response
    /// and never saves it to IBlueprintStore (unlike Generate/FromConnection above) — so
    /// koru-main sends back the blueprint it already has rather than this service needing to
    /// have persisted it first.
    /// </summary>
    [HttpPost("author-tmdl")]
    [ProducesResponseType(typeof(TmdlAuthoringResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(422)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> AuthorTmdlFromBody([FromBody] AuthorTmdlRequest request, CancellationToken cancellationToken)
        => await AuthorTmdlInternal(request.Blueprint, cancellationToken);

    private async Task<IActionResult> AuthorTmdlInternal(Blueprint blueprint, CancellationToken cancellationToken)
    {
        var correlationId = Request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) && !string.IsNullOrWhiteSpace(headerValue)
            ? headerValue.ToString()
            : Guid.NewGuid().ToString();

        try
        {
            var result = await _tmdlAuthoring.AuthorAsync(blueprint, correlationId, cancellationToken);
            result.Validation = _tmdlValidation.Validate(blueprint, result);

            if (!result.Validation.IsValid)
            {
                _logger.LogWarning(
                    "TMDL authoring for blueprint {BlueprintId} failed deterministic validation: {Violations}",
                    blueprint.BlueprintId, string.Join(" | ", result.Validation.Violations));
                return UnprocessableEntity(new
                {
                    message = "Authored TMDL failed deterministic validation.",
                    violations = result.Validation.Violations,
                    files = result.Files
                });
            }

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (BlueprintParseException ex)
        {
            _logger.LogError(ex, "TMDL authoring failed for blueprint {BlueprintId}", blueprint.BlueprintId);
            return Problem(title: "TMDL authoring failed", detail: ex.Message, statusCode: 502);
        }
    }

    private async Task<IActionResult> GenerateInternal(BlueprintGenerationOptions options, CancellationToken cancellationToken)
    {
        var correlationId = Request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) && !string.IsNullOrWhiteSpace(headerValue)
            ? headerValue.ToString()
            : Guid.NewGuid().ToString();

        try
        {
            var blueprint = await _generationService.GenerateAsync(options, correlationId, cancellationToken);
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
