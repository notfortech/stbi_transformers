using DashboardAgents.Api.Services;
using DashboardAgents.BlueprintAgent;
using DashboardAgents.Core.Models;
using DashboardAgents.SchemaConnector;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace DashboardAgents.Api.Controllers;

/// <summary>
/// The 4-step template-generation pipeline:
///   1. POST /api/pipeline/connect          – connect to data (file upload or live DB)
///   2. POST /api/pipeline/{id}/transform   – validate and profile all columns
///   3. GET  /api/pipeline/{id}/designs     – ranked dashboard design options
///   4. POST /api/pipeline/{id}/generate    – generate the final blueprint + template match
/// </summary>
[ApiController]
[Route("api/pipeline")]
[Produces("application/json")]
public sealed class PipelineController : ControllerBase
{
    private readonly IPipelineSessionStore _sessions;
    private readonly IFileIngestionService _fileIngestion;
    private readonly IColumnValidationService _columnValidation;
    private readonly IDesignMatchingService _designMatching;
    private readonly IBlueprintGenerationService _blueprintGeneration;
    private readonly ISchemaReaderFactory _schemaReaderFactory;
    private readonly ILogger<PipelineController> _logger;

    public PipelineController(
        IPipelineSessionStore sessions,
        IFileIngestionService fileIngestion,
        IColumnValidationService columnValidation,
        IDesignMatchingService designMatching,
        IBlueprintGenerationService blueprintGeneration,
        ISchemaReaderFactory schemaReaderFactory,
        ILogger<PipelineController> logger)
    {
        _sessions = sessions;
        _fileIngestion = fileIngestion;
        _columnValidation = columnValidation;
        _designMatching = designMatching;
        _blueprintGeneration = blueprintGeneration;
        _schemaReaderFactory = schemaReaderFactory;
        _logger = logger;
    }

    // ── Step 1: Connect ────────────────────────────────────────────────────────

    /// <summary>
    /// Connect to a data source and extract its schema.
    /// Accepts a base64-encoded CSV/TSV file (provider = "file") or a live database
    /// connection string (provider = "sqlserver" | "postgres").
    /// Returns a sessionId used in subsequent steps.
    /// </summary>
    [HttpPost("connect")]
    [ProducesResponseType(typeof(ConnectResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> Connect([FromBody] ConnectRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
            return BadRequest(new { message = "provider is required (\"file\", \"sqlserver\", or \"postgres\")." });

        SchemaSnapshot schema;
        var source = request.Provider.ToLowerInvariant();
        string? fileName = null;

        try
        {
            if (source == "file")
            {
                if (string.IsNullOrWhiteSpace(request.FileBase64) || string.IsNullOrWhiteSpace(request.FileName))
                    return BadRequest(new { message = "fileBase64 and fileName are required for provider=\"file\"." });

                fileName = request.FileName;
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(request.FileBase64);
                }
                catch
                {
                    return BadRequest(new { message = "fileBase64 is not valid base64." });
                }

                schema = await _fileIngestion.IngestAsync(bytes, fileName, cancellationToken);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.ConnectionString))
                    return BadRequest(new { message = "connectionString is required for database providers." });

                DbProvider provider;
                try
                {
                    provider = source == "postgres"
                        ? DbProvider.PostgreSql
                        : DbProvider.SqlServer;
                }
                catch
                {
                    return BadRequest(new { message = $"Unknown provider '{request.Provider}'. Use \"file\", \"sqlserver\", or \"postgres\"." });
                }

                var reader = _schemaReaderFactory.Resolve(provider);
                schema = await reader.ReadSchemaAsync(request.ConnectionString, request.SchemaFilter?.ToList(), cancellationToken);
            }
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema extraction failed for provider {Provider}", request.Provider);
            return Problem(
                title: "Data connection failed",
                detail: "Could not connect to or read the data source. Check your connection details.",
                statusCode: 502);
        }

        var session = new PipelineSession
        {
            DataSource = source,
            FileName = fileName,
            Schema = schema,
            CurrentStep = PipelineStep.Connected
        };
        _sessions.Save(session);

        _logger.LogInformation("Pipeline session {SessionId} created: {Provider}, {TableCount} table(s)",
            session.SessionId, source, schema.Tables.Count);

        var tables = schema.Tables.Select(t => new TableSummary
        {
            Name = t.QualifiedName,
            ColumnCount = t.Columns.Count,
            ApproximateRowCount = t.ApproximateRowCount,
            ColumnNames = t.Columns.Select(c => c.ColumnName).ToList()
        }).ToList();

        var totalColumns = schema.Tables.Sum(t => t.Columns.Count);
        var summary = $"Connected to '{schema.DatabaseName}' ({source}): {schema.Tables.Count} table(s), {totalColumns} column(s) extracted.";

        return Ok(new ConnectResponse
        {
            SessionId = session.SessionId,
            DataSource = source,
            FileName = fileName,
            Tables = tables,
            TotalColumns = totalColumns,
            Summary = summary
        });
    }

    // ── Step 2: Transform (validate columns) ──────────────────────────────────

    /// <summary>
    /// Validate and profile all columns in the connected dataset.
    /// Returns column type inference, naming issues, data quality warnings,
    /// and ordered transformation recommendations.
    /// </summary>
    [HttpPost("{sessionId}/transform")]
    [ProducesResponseType(typeof(DataProfile), 200)]
    [ProducesResponseType(404)]
    public IActionResult Transform(string sessionId, [FromBody] TransformRequest? request)
    {
        var session = _sessions.Get(sessionId);
        if (session?.Schema is null)
            return NotFound(new { message = $"Session '{sessionId}' not found or has expired." });

        var profile = _columnValidation.Validate(session.Schema, request?.UserPrompt);
        session.Profile = profile;
        session.CurrentStep = PipelineStep.Transformed;
        _sessions.Save(session);

        _logger.LogInformation(
            "Session {SessionId}: transformation complete — {Columns} columns, {Errors} errors, {Warnings} warnings.",
            sessionId, profile.Columns.Count, profile.ErrorCount, profile.WarningCount);

        return Ok(profile);
    }

    // ── Step 3: Design options ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the top-ranked dashboard design options for this dataset,
    /// scored against the template catalog from koru-main (or built-in archetypes
    /// when koru-main is not configured). The UI presents these as cards for the user
    /// to select on the Blueprint Design screen.
    /// </summary>
    [HttpGet("{sessionId}/designs")]
    [ProducesResponseType(typeof(List<DesignOption>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetDesigns(string sessionId, CancellationToken cancellationToken)
    {
        var session = _sessions.Get(sessionId);
        if (session?.Schema is null)
            return NotFound(new { message = $"Session '{sessionId}' not found or has expired." });

        if (session.Profile is null)
        {
            // Auto-run validation if the caller skipped Step 2
            session.Profile = _columnValidation.Validate(session.Schema);
            session.CurrentStep = PipelineStep.Transformed;
        }

        var options = await _designMatching.MatchAsync(session.Schema, session.Profile, cancellationToken);
        session.DesignOptions = options;
        session.CurrentStep = PipelineStep.DesignSelected;
        _sessions.Save(session);

        return Ok(options);
    }

    // ── Step 4: Generate blueprint ─────────────────────────────────────────────

    /// <summary>
    /// Generate the final dashboard blueprint using the validated schema and (optionally)
    /// a selected design template. The AI agent produces a structured Analytics Deployment
    /// Contract and the best template match is returned alongside it.
    /// </summary>
    [HttpPost("{sessionId}/generate")]
    [ProducesResponseType(typeof(GenerateResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> Generate(
        string sessionId,
        [FromBody] GenerateRequest? request,
        CancellationToken cancellationToken)
    {
        var session = _sessions.Get(sessionId);
        if (session?.Schema is null)
            return NotFound(new { message = $"Session '{sessionId}' not found or has expired." });

        if (session.Profile is null)
            session.Profile = _columnValidation.Validate(session.Schema);

        if (session.Profile.ErrorCount > 0 && request?.BusinessGoal is null)
            return BadRequest(new
            {
                message = "There are validation errors in the dataset. Fix them or provide a businessGoal override to proceed.",
                errorCount = session.Profile.ErrorCount,
                errors = session.Profile.Issues.Where(i => i.Severity == "error").Select(i => i.Message).ToList()
            });

        // Determine selected design
        DesignOption? selectedDesign = null;
        if (!string.IsNullOrWhiteSpace(request?.SelectedTemplateId) && session.DesignOptions != null)
            selectedDesign = session.DesignOptions.FirstOrDefault(d => d.TemplateId == request.SelectedTemplateId);

        if (selectedDesign is null && session.DesignOptions is null)
        {
            session.DesignOptions = await _designMatching.MatchAsync(session.Schema, session.Profile, cancellationToken);
        }

        selectedDesign ??= session.DesignOptions?.FirstOrDefault();

        // Build blueprint generation options from the session data
        var schemaText = SchemaTextFormatter.Format(session.Schema);
        var businessGoal = request?.BusinessGoal?.Trim();

        if (string.IsNullOrWhiteSpace(businessGoal))
        {
            businessGoal = selectedDesign is not null
                ? $"Generate a {selectedDesign.Name} dashboard for the dataset '{session.Schema.DatabaseName}'."
                : $"Generate a comprehensive analytics dashboard for the dataset '{session.Schema.DatabaseName}'.";
        }

        var options = new BlueprintGenerationOptions
        {
            Mode = "schema",
            SchemaText = schemaText,
            BusinessGoal = businessGoal,
            Requirements = request?.BusinessRequirements,
            IndustryExplicit = request?.Industry ?? selectedDesign?.Industry,
            KnowledgePack = request?.KnowledgePack
        };

        var sw = Stopwatch.StartNew();
        Blueprint blueprint;
        try
        {
            blueprint = await _blueprintGeneration.GenerateAsync(options, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (BlueprintValidationException ex)
        {
            _logger.LogWarning("Blueprint validation failed: {Violations}", string.Join(" | ", ex.Violations));
            return Problem(
                title: "Generated blueprint failed validation",
                detail: string.Join("; ", ex.Violations),
                statusCode: 422);
        }
        catch (BlueprintParseException ex)
        {
            _logger.LogError(ex, "Blueprint parse failed");
            return Problem(title: "Blueprint generation failed", detail: ex.Message, statusCode: 502);
        }
        sw.Stop();

        // Build template match from the selected design
        TemplateMatch? templateMatch = null;
        if (selectedDesign != null)
        {
            templateMatch = new TemplateMatch
            {
                TemplateId = selectedDesign.TemplateId,
                TemplateName = selectedDesign.Name,
                Confidence = selectedDesign.MatchScore,
                Transformations = session.Profile.Recommendations
                    .Where(r => r.Action == "rename" || r.Action == "cast")
                    .Select(r => r.Description)
                    .ToList(),
                ColumnMappings = session.Profile.Columns
                    .Where(c => c.OriginalName != c.SuggestedName)
                    .Select(c => new ColumnMapping
                    {
                        TemplateColumn = c.SuggestedName,
                        ClientColumn = c.OriginalName,
                        Transform = "rename"
                    })
                    .ToList(),
                Notes = session.Profile.WarningCount > 0
                    ? $"{session.Profile.WarningCount} column warning(s) noted — review transformations before deployment."
                    : null
            };
        }

        session.GeneratedBlueprint = blueprint;
        session.BestTemplateMatch = templateMatch;
        session.CurrentStep = PipelineStep.Generated;
        _sessions.Save(session);

        var rawScore = blueprint.Confidence?.Score ?? 0;
        var confidence = rawScore > 0 ? rawScore / 100.0 : (blueprint.SelfReview?.CompositeScore > 0 ? blueprint.SelfReview.CompositeScore / 100.0 : 0.65);

        _logger.LogInformation(
            "Session {SessionId}: blueprint generated in {Ms}ms, confidence={Confidence:P0}.",
            sessionId, sw.ElapsedMilliseconds, confidence);

        return Ok(new GenerateResponse
        {
            SessionId = sessionId,
            Blueprint = blueprint,
            BestTemplateMatch = templateMatch,
            Confidence = confidence,
            GenerationTimeMs = sw.ElapsedMilliseconds
        });
    }

    // ── Session status ─────────────────────────────────────────────────────────

    /// <summary>Returns the current step and metadata for a pipeline session.</summary>
    [HttpGet("{sessionId}/status")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public IActionResult GetStatus(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        if (session is null)
            return NotFound(new { message = $"Session '{sessionId}' not found or has expired." });

        return Ok(new
        {
            sessionId = session.SessionId,
            currentStep = session.CurrentStep.ToString(),
            dataSource = session.DataSource,
            fileName = session.FileName,
            tableCount = session.Schema?.Tables.Count ?? 0,
            columnCount = session.Schema?.Tables.Sum(t => t.Columns.Count) ?? 0,
            hasProfile = session.Profile != null,
            hasDesignOptions = session.DesignOptions?.Count > 0,
            hasBlueprint = session.GeneratedBlueprint != null,
            isReadyForDesign = session.Profile?.IsReadyForDesign ?? false,
            expiresAt = session.ExpiresAt
        });
    }
}
