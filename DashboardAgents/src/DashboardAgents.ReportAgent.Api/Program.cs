using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using ReportAgent.Api.Models;
using ReportAgent.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PythonAgentOptions>(builder.Configuration.GetSection("PythonAgent"));
builder.Services.AddSingleton<PythonAgentRunner>();
builder.Services.AddSingleton<TemplateRegistryService>();

const long MaxInputFileBytes = 60 * 1024 * 1024; // 60 MB
const long MaxRequestBodyBytes = MaxInputFileBytes + (1 * 1024 * 1024);

builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
});

builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("report-generation", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

var app = builder.Build();

app.UseRateLimiter();

// --- Health check (no auth required, no sensitive data) --------------------
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// --- API key gate ------------------------------------------------------------
// Same X-Api-Key convention used between koru-main and stbi-agenthost /
// stbi_transformers. Configured via ReportAgent:ApiKey (empty = disabled,
// dev-only — must be set in production, see README.md).
var apiKey = builder.Configuration["ReportAgent:ApiKey"];
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health") || string.IsNullOrEmpty(apiKey))
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) || provided != apiKey)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid X-Api-Key." });
        return;
    }

    await next();
});

// --- Template registry --------------------------------------------------------
app.MapGet("/api/templates", (TemplateRegistryService registry) =>
    Results.Ok(registry.ListTemplates()));

// --- Report generation ---------------------------------------------------------
app.MapPost("/api/reports/generate", async (
        HttpRequest request,
        PythonAgentRunner runner,
        ILogger<Program> logger,
        CancellationToken ct) =>
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new { error = "Expected multipart/form-data." });

        var maxBodyFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodyFeature is { IsReadOnly: false })
            maxBodyFeature.MaxRequestBodySize = MaxRequestBodyBytes;

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (BadHttpRequestException)
        {
            return Results.BadRequest(new { error = "Malformed or oversized multipart body." });
        }

        var file = form.Files["file"];
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "'file' form field is required (.xlsx or .csv)." });
        if (file.Length > MaxInputFileBytes)
            return Results.BadRequest(new { error = $"'file' must be under {MaxInputFileBytes} bytes." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".csv"))
            return Results.BadRequest(new { error = "Only .xlsx or .csv files are supported." });

        await using var fileStream = new MemoryStream();
        await file.OpenReadStream().CopyToAsync(fileStream, ct);

        var templateId = form["templateId"].ToString();

        var filters = form["filters"].ToString();
        if (!string.IsNullOrWhiteSpace(filters))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(filters);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return Results.BadRequest(new { error = "'filters' must be a JSON object of {column: value} pairs." });
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "'filters' is not valid JSON." });
            }
        }

        var result = await runner.GenerateAsync(
            fileStream.ToArray(), file.FileName,
            string.IsNullOrWhiteSpace(templateId) ? null : templateId,
            string.IsNullOrWhiteSpace(filters) ? null : filters, ct);

        if (!result.Success)
        {
            logger.LogWarning("Report generation failed: {Code} {Message}", result.ErrorCode, result.ErrorMessage);
            return result.ErrorCode switch
            {
                "TIMEOUT_OR_CANCELLED" => Results.Problem(result.ErrorMessage, statusCode: 504),
                _ => Results.Problem(result.ErrorMessage, statusCode: 502),
            };
        }

        return Results.Content(result.ResultJson, "application/json");
    })
    .RequireRateLimiting("report-generation")
    .DisableAntiforgery(); // sits behind koru-main, which owns browser-facing CSRF/auth

app.Run();
