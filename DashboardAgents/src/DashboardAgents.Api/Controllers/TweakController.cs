using DashboardAgents.Api.Services;
using DashboardAgents.Core.Models;
using DashboardAgents.TweakAgent;
using Microsoft.AspNetCore.Mvc;

namespace DashboardAgents.Api.Controllers;

[ApiController]
[Route("api/blueprint")]
public sealed class TweakController : ControllerBase
{
    private readonly IUseCaseTweakService _tweakService;
    private readonly IBlueprintStore _store;
    private readonly ILogger<TweakController> _logger;

    public TweakController(IUseCaseTweakService tweakService, IBlueprintStore store, ILogger<TweakController> logger)
    {
        _tweakService = tweakService;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Adapts an existing blueprint to a specific use-case scenario. Either matches existing
    /// page(s) or composes a new one, built only from fields already present in the blueprint.
    /// </summary>
    [HttpPost("{blueprintId}/adapt")]
    [ProducesResponseType(typeof(TweakResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Adapt(string blueprintId, [FromBody] AdaptRequestBody body, CancellationToken cancellationToken)
    {
        var blueprint = _store.Get(blueprintId);
        if (blueprint is null)
            return NotFound($"No blueprint found with id '{blueprintId}'. Generate one first via POST /api/blueprint/generate.");

        if (string.IsNullOrWhiteSpace(body.Scenario))
            return BadRequest("scenario is required.");

        try
        {
            var result = await _tweakService.AdaptAsync(blueprint, body.Scenario, cancellationToken);

            if (body.PersistToBlueprint && result.Mode == "composed_new")
            {
                blueprint.Pages.AddRange(result.Pages);
                _store.Save(blueprint);
            }

            return Ok(result);
        }
        catch (TweakValidationException ex)
        {
            _logger.LogWarning("Tweak agent output failed allow-list validation: {Violations}", string.Join(" | ", ex.Violations));
            return UnprocessableEntity(new { message = "Tweak agent referenced fields outside the blueprint's allow-list.", violations = ex.Violations });
        }
    }
}

public sealed class AdaptRequestBody
{
    public string Scenario { get; set; } = "";
    public bool PersistToBlueprint { get; set; } = true;
}
