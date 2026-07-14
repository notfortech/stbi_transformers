using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using DashboardAgents.Api.Services;
using DashboardAgents.BlueprintAgent;
using DashboardAgents.Llm;
using DashboardAgents.SchemaConnector;
using DashboardAgents.TweakAgent;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

// ── Bootstrap logger (captures startup errors before full logging is wired) ──
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Port binding for Azure App Service Linux ─────────────────────────────
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

    // ── Flat env-var → nested config path mapping ────────────────────────────
    // Azure App Service Application Settings can't use colon syntax; map them here.
    var envOverrides = new Dictionary<string, string?>
    {
        ["OpenAI:ApiKey"]          = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
        ["Anthropic:ApiKey"]       = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
        ["Jwt:Key"]                = Environment.GetEnvironmentVariable("JWT_KEY"),
        ["Jwt:Issuer"]             = Environment.GetEnvironmentVariable("JWT_ISSUER"),
        ["Jwt:Audience"]           = Environment.GetEnvironmentVariable("JWT_AUDIENCE"),
        ["Koru:BaseUrl"]           = Environment.GetEnvironmentVariable("KORU_BASE_URL"),
        ["Koru:ServiceApiKey"]     = Environment.GetEnvironmentVariable("KORU_API_KEY"),
        ["Redis:ConnectionString"] = Environment.GetEnvironmentVariable("REDIS_CONNECTION"),
        ["KeyVault:VaultUrl"]      = Environment.GetEnvironmentVariable("KEY_VAULT_URL"),
        ["Cors:AllowedOrigins:0"]  = Environment.GetEnvironmentVariable("CORS_ORIGINS"),
        ["Llm:Provider"]           = Environment.GetEnvironmentVariable("LLM_PROVIDER"),
    };
    builder.Configuration.AddInMemoryCollection(
        envOverrides.Where(kv => kv.Value is not null)!);

    // ── Azure Key Vault (optional — activated by KeyVault:VaultUrl) ──────────
    var vaultUrl = builder.Configuration["KeyVault:VaultUrl"];
    if (!string.IsNullOrWhiteSpace(vaultUrl))
    {
        builder.Configuration.AddAzureKeyVault(new Uri(vaultUrl), new DefaultAzureCredential());
    }

    // ── Serilog structured logging ───────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7));

    // ── OpenAI (primary provider) ────────────────────────────────────────────
    builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));
    builder.Services.PostConfigure<OpenAiOptions>(opts =>
    {
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            opts.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
    });

    // ── Anthropic (optional) ─────────────────────────────────────────────────
    builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
    builder.Services.PostConfigure<AnthropicOptions>(opts =>
    {
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            opts.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
    });

    // ── Koru integration ─────────────────────────────────────────────────────
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

    // ── LLM HTTP clients ─────────────────────────────────────────────────────
    builder.Services.AddHttpClient<OpenAiClient>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(3);
    });
    builder.Services.AddHttpClient<AnthropicClient>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(3);
    });

    // Resolve ILlmClient: Anthropic by default now (OpenAI's org hit its TPM rate limit in
    // production). Falls back to OpenAI only if no Anthropic key is configured, or if
    // Llm:Provider is explicitly set to "openai".
    builder.Services.AddScoped<ILlmClient>(sp =>
    {
        var cfg = sp.GetRequiredService<IConfiguration>();
        var provider = cfg["Llm:Provider"]?.Trim().ToLowerInvariant() ?? "anthropic";
        if (provider == "anthropic")
        {
            var anthropicOpts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(anthropicOpts.ApiKey))
                return sp.GetRequiredService<AnthropicClient>();
        }
        return sp.GetRequiredService<OpenAiClient>();
    });

    // ── Schema connector ─────────────────────────────────────────────────────
    builder.Services.AddScoped<IDbSchemaReader, SqlServerSchemaReader>();
    builder.Services.AddScoped<IDbSchemaReader, PostgresSchemaReader>();
    builder.Services.AddScoped<ISchemaReaderFactory, SchemaReaderFactory>();

    // ── Distributed cache + session/blueprint stores ──────────────────────────
    var redisConn = builder.Configuration["Redis:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(redisConn))
    {
        builder.Services.AddStackExchangeRedisCache(opts => opts.Configuration = redisConn);
        builder.Services.AddSingleton<IPipelineSessionStore, RedisPipelineSessionStore>();
        builder.Services.AddSingleton<IBlueprintStore, RedisBlueprintStore>();
        Log.Information("Redis distributed cache configured.");
    }
    else
    {
        // Local dev / no Redis configured — fall back to in-process stores
        builder.Services.AddSingleton<IPipelineSessionStore, InMemoryPipelineSessionStore>();
        builder.Services.AddSingleton<IBlueprintStore, InMemoryBlueprintStore>();
        Log.Information("Redis not configured — using in-memory stores (not suitable for multi-instance).");
    }

    // ── Pipeline + agent services ────────────────────────────────────────────
    builder.Services.AddScoped<IFileIngestionService, FileIngestionService>();
    builder.Services.AddScoped<IColumnValidationService, ColumnValidationService>();
    builder.Services.AddScoped<IDesignMatchingService, DesignMatchingService>();
    builder.Services.AddScoped<IBlueprintGenerationService, BlueprintGenerationService>();
    builder.Services.AddScoped<IUseCaseTweakService, UseCaseTweakService>();
    builder.Services.AddScoped<ISchemaModelMatchingService, SchemaModelMatchingService>();

    // ── JWT auth (shared with koru-main) ─────────────────────────────────────
    var jwtKey = builder.Configuration["Jwt:Key"] ?? "";
    var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "";
    var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "";

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

    // ── Health checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ── Swagger ───────────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    var swaggerEnabled = builder.Environment.IsDevelopment()
        || string.Equals(builder.Configuration["Swagger:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

    if (swaggerEnabled)
    {
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
    }

    // ── CORS ──────────────────────────────────────────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("PortalClient", policy =>
        {
            var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                          ?? Array.Empty<string>();
            if (origins.Length > 0)
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            else
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        });
    });

    // ────────────────────────────────────────────────────────────────────────
    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // Warmup / root endpoint — required for Azure App Service health probe
    app.MapGet("/", () => Results.Ok(new { status = "running", service = "DashboardAgents API" }))
       .AllowAnonymous();

    // Health check endpoint
    app.MapHealthChecks("/health").AllowAnonymous();

    if (swaggerEnabled)
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
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
