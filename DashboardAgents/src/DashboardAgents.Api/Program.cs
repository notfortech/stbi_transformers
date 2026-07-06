using DashboardAgents.Api.Services;
using DashboardAgents.BlueprintAgent;
using DashboardAgents.Llm;
using DashboardAgents.SchemaConnector;
using DashboardAgents.TweakAgent;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));

// Allow the API key to come from an environment variable even if appsettings.json is committed
// without one — never commit a real key to source control.
builder.Services.PostConfigure<AnthropicOptions>(opts =>
{
    if (string.IsNullOrWhiteSpace(opts.ApiKey))
    {
        opts.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
    }
});

builder.Services.Configure<KoruAuthOptions>(builder.Configuration.GetSection(KoruAuthOptions.SectionName));
builder.Services.PostConfigure<KoruAuthOptions>(opts =>
{
    if (string.IsNullOrWhiteSpace(opts.ApiKey))
        opts.ApiKey = Environment.GetEnvironmentVariable("KORU_API_KEY") ?? "";
});

// ── HTTP client for the Anthropic API ───────────────────────────────────
builder.Services.AddHttpClient<IAnthropicClient, AnthropicClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(3); // blueprint generation is a large single completion
});

// ── Schema connector (live DB introspection) ────────────────────────────
builder.Services.AddScoped<IDbSchemaReader, SqlServerSchemaReader>();
builder.Services.AddScoped<IDbSchemaReader, PostgresSchemaReader>();
builder.Services.AddScoped<ISchemaReaderFactory, SchemaReaderFactory>();

// ── Blueprint generation agent ───────────────────────────────────────────
builder.Services.AddScoped<IBlueprintGenerationService, BlueprintGenerationService>();

// ── Use-case tweak agent ─────────────────────────────────────────────────
builder.Services.AddScoped<IUseCaseTweakService, UseCaseTweakService>();

// ── Blueprint persistence (swap for real storage later) ──────────────────
builder.Services.AddSingleton<IBlueprintStore, InMemoryBlueprintStore>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("PortalClient", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("PortalClient");
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

app.Run();
