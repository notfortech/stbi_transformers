using DashboardAgents.Api.Models;
using DashboardAgents.Api.Services;
using DashboardAgents.BlueprintAgent;
using Microsoft.AspNetCore.Mvc;

namespace DashboardAgents.Api.Controllers;

/// <summary>
/// AI-assisted schema/model matching: a single-shot call (no pipeline session needed)
/// used by koru-main when its own deterministic name-overlap matching against the
/// SchemaModel directory scores below its confidence gate. Headers/types only.
/// </summary>
[ApiController]
[Route("api/schema-model-match")]
public sealed class SchemaModelMatchController : ControllerBase
{
    private readonly ISchemaModelMatchingService _matching;
    private readonly ILogger<SchemaModelMatchController> _logger;

    public SchemaModelMatchController(ISchemaModelMatchingService matching, ILogger<SchemaModelMatchController> logger)
    {
        _matching = matching;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(SchemaModelMatchResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> MatchAsync([FromBody] SchemaModelMatchRequest request, CancellationToken cancellationToken)
    {
        if (request.Columns.Count == 0)
            return BadRequest(new { message = "columns must contain at least one entry." });
        if (request.CandidateModels.Count == 0)
            return BadRequest(new { message = "candidateModels must contain at least one entry." });

        var correlationId = Request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) && !string.IsNullOrWhiteSpace(headerValue)
            ? headerValue.ToString()
            : Guid.NewGuid().ToString();

        try
        {
            var result = await _matching.MatchAsync(request, correlationId, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (BlueprintParseException ex)
        {
            _logger.LogError(ex, "SchemaModelMatch controller: AI response could not be used.");
            return Problem(title: "Schema model match failed", detail: ex.Message, statusCode: 502);
        }
    }
}
