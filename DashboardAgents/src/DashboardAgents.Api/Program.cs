using DashboardAgents.Api.Services;
using DashboardAgents.BlueprintAgent;
using DashboardAgents.Llm;
using DashboardAgents.SchemaConnector;
using DashboardAgents.TweakAgent;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── OpenAI (primary provider) ────────────────────────────────────────────────
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));
builder.Services.PostConfigure<OpenAiOptions>(opts =>
{
    if (string.IsNullOrWhiteSpace(opts.ApiKey))
        opts.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
});

// ── Anthropic (optional — configure Anthropic:ApiKey to enable) ──────────────
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.PostConfigure<AnthropicOptions>(opts =>
{
    if (string.IsNullOrWhiteSpace(opts.ApiKey))
        opts.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
});

// ── Koru integration ─────────────────────────────────────────────────────────
builder.Services.Configure<KoruOptions>(builder.Configuration.GetSection(KoruOptions.SectionName));
builder.Services.AddHttpClient<KoruApiClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<KoruOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
    {
        client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/'));
        client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds > 0 ? opts.TimeoutSeconds : 30);

        if (!string.IsNullOrWhiteSpace(opts.ServiceApiKey))
            client.DefaultRequestHeaders.Add("X-Service-Api-Key", opts.ServiceApiKey);
    }
});

// ── HTTP clients ─────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<OpenAiClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
});

// Anthropic client registered but NOT bound to ILlmClient unless selected below
builder.Services.AddHttpClient<AnthropicClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
});

// Resolve ILlmClient: use Anthropic only when explicitly selected AND key is present;
// otherwise default to OpenAI.
builder.Services.AddScoped<ILlmClient>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var provider = cfg["Llm:Provider"]?.Trim().ToLowerInvariant() ?? "openai";

    if (provider == "anthropic")
    {
        var anthropicOpts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(anthropicOpts.ApiKey))
            return sp.GetRequiredService<AnthropicClient>();
    }

    return sp.GetRequiredService<OpenAiClient>();
});

// ── Schema connector (live DB introspection) ────────────────────────────────
builder.Services.AddScoped<IDbSchemaReader, SqlServerSchemaReader>();
builder.Services.AddScoped<IDbSchemaReader, PostgresSchemaReader>();
builder.Services.AddScoped<ISchemaReaderFactory, SchemaReaderFactory>();

// ── Pipeline services ────────────────────────────────────────────────────────
builder.Services.AddSingleton<IPipelineSessionStore, InMemoryPipelineSessionStore>();
builder.Services.AddSingleton<IBlueprintStore, InMemoryBlueprintStore>();
builder.Services.AddScoped<IFileIngestionService, FileIngestionService>();
builder.Services.AddScoped<IColumnValidationService, ColumnValidationService>();
builder.Services.AddScoped<IDesignMatchingService, DesignMatchingService>();

// ── Blueprint + tweak agents ─────────────────────────────────────────────────
builder.Services.AddScoped<IBlueprintGenerationService, BlueprintGenerationService>();
builder.Services.AddScoped<IUseCaseTweakService, UseCaseTweakService>();

// ── Auth: validate the same JWT issued by koru-main ─────────────────────────
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? Environment.GetEnvironmentVariable("JWT_KEY") ?? "";
var jwtIssuer = jwtSection["Issuer"] ?? "";
var jwtAudience = jwtSection["Audience"] ?? "";

if (!string.IsNullOrWhiteSpace(jwtKey))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateIssuer = !string.IsNullOrWhiteSpace(jwtIssuer),
                ValidIssuer = jwtIssuer,
                ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
                ValidAudience = jwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };
        });
    builder.Services.AddAuthorization();
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "DashboardAgents API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Bearer token issued by koru-main"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("PortalClient", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (origins.Length > 0)
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        else
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
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

if (!string.IsNullOrWhiteSpace(jwtKey))
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapControllers();

app.Run();
