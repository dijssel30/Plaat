using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Plaat.Api;
using Plaat.Api.EndpointMappers;
using Plaat.Biz;
using Plaat.Biz.Extensions;
using Plaat.Biz.Options;
using Plaat.Db;
using Plaat.Infrastructure.Discogs;
using IPNetwork = System.Net.IPNetwork;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Secrets.json", optional: true)
    .Build();

var builder = WebApplication.CreateBuilder(args);

var connectionString = configuration.GetConnectionString("SqlLiteConnection")
                       ?? throw new Exception("SqlLiteConnection is missing connection string");

var plaatOptions = configuration
    .GetSection(nameof(PlaatOptions))
    .Get<PlaatOptions>() ?? throw new Exception("PlaatOptions is missing from config");

// HmacSha256 needs a signing key of at least 32 bytes; without this check a short key only fails
// at the first login instead of at startup.
const int minimumJwtKeyBytes = 32;
if (string.IsNullOrEmpty(plaatOptions.JwtOptions?.Key)
    || Encoding.UTF8.GetByteCount(plaatOptions.JwtOptions.Key) < minimumJwtKeyBytes)
{
    throw new InvalidOperationException(
        $"PlaatOptions:JwtOptions:Key must be at least {minimumJwtKeyBytes} bytes long (HS256).");
}

builder.Services.AddValidation();
builder.Services.AddTransient<IPasswordHasher<string>, PasswordHasher<string>>();
builder.Services.AddSingleton(plaatOptions);
builder.Services.AddSingleton(new DiscogsOptions
{
    Token = plaatOptions.DiscogsSettings?.Token,
    RequestsPerMinute = plaatOptions.DiscogsSettings?.RequestsPerMinute ?? 25
});
builder.Services.AddDbServices(builder.Environment, connectionString);
builder.Services.AddBizServices();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Paste a token directly, or use the 'password' option below to log in with a username and password instead.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityDefinition("password", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            Password = new OpenApiOAuthFlow
            {
                TokenUrl = new Uri("/api/auth/token", UriKind.Relative)
            }
        }
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("password", document)] = []
    });
});

// JSON bodies here get big (every album/artist in the library, whole playlists, track pages) and
// compress several-fold - most noticeable for Plaat.Mobile on a phone connection. Only applies when
// the client asks for it via Accept-Encoding, and the default MIME type list covers text/JSON only,
// so audio streams and cover images (already compressed) are never touched.
builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

// Limits are resolved from PlaatOptions at first use (not captured here) so they stay configurable
// in appsettings and overridable in tests.
builder.Services.AddRateLimiter(_ => { });
builder.Services.AddOptions<RateLimiterOptions>()
    .Configure<PlaatOptions>((options, plaat) =>
{
    var settings = plaat.RateLimitSettings ?? new RateLimitSettings();

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitPolicies.Login, context => CreateLimiter(context, settings, settings.LoginPermitLimit));
    options.AddPolicy(RateLimitPolicies.Refresh, context => CreateLimiter(context, settings, settings.RefreshPermitLimit));
});

// Plaat.Frontend calls this API server-side, so the connection IP is the frontend's; it passes the
// browser's IP as X-Forwarded-For. Only honour that header from the configured trusted proxies.
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<PlaatOptions>((options, plaat) =>
{
    var settings = plaat.RateLimitSettings ?? new RateLimitSettings();

    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    foreach (var proxy in settings.TrustedProxies)
    {
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});

builder.Services.AddSingleton<UserStateStore>();
builder.Services.AddTransient<TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidIssuer = JwtOptions.Issuer,
            ValidAudience = JwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(plaatOptions.JwtOptions.Key)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.AdminPolicy, p =>
    {
        p.RequireRole(Roles.Admin);
    });
});

await using var app = builder.Build();

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";

        // Swagger UI needs inline scripts/styles; everything else is JSON, images and audio.
        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; sandbox";
        }

        return Task.CompletedTask;
    });

    await next();
});
app.UseResponseCompression();

if (builder.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    // Optional: when Swagger:AllowedNetworks (CIDR list, e.g. "192.168.0.0/16") is non-empty, only
    // clients inside those networks may reach Swagger; everyone else gets a 404. Empty = open to all.
    var swaggerNetworks = (builder.Configuration.GetSection("Swagger:AllowedNetworks").Get<string[]>() ?? [])
        .Select(IPNetwork.Parse)
        .ToList();

    if (swaggerNetworks.Count > 0)
    {
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/swagger"),
            branch => branch.Use(async (context, next) =>
            {
                var remote = context.Connection.RemoteIpAddress;

                if (remote is null || !swaggerNetworks.Any(n => n.Contains(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote)))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await next();
            }));
    }

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Every endpoint group below is mapped onto this root group rather than onto the app directly,
// which is what makes ExceptionLoggingEndpointFilter apply to all of them from this one line:
// a group's filters are inherited by the groups nested inside it. The prefix is empty, so routes
// are unaffected - the mappers still own their own "/api/..." paths.
var api = app.MapGroup(string.Empty)
    .AddEndpointFilter<ExceptionLoggingEndpointFilter>();

api.MapStateEndpoints();
api.MapAppCompatibilityEndpoints();

api.MapMusicEndpoints();
api.MapSearchEndpoints();
api.MapStatsEndpoints();
api.MapCoverEndpoints();
api.MapArtistsEndpoints();
api.MapAlbumsEndpoints();
api.MapFavoritesEndpoints();
api.MapRatingsEndpoints();
api.MapPlaylistEndpoints();
api.MapAuthEndpoints();
api.MapConvertQueueEndpoints();
api.MapLoudnessAnalysisEndpoints();
api.MapUsersEndpoints();
api.MapMonitoredFoldersEndpoints();
api.MapTasksEndpoints();
api.MapLogsEndpoints();

if (!app.Environment.IsEnvironment("Testing"))
{
    await app.Services.EnsureDbCreated();
}

app.Run();
return;

static RateLimitPartition<string> CreateLimiter(HttpContext context, RateLimitSettings settings, int permitLimit)
{
    if (!settings.Enabled)
    {
        return RateLimitPartition.GetNoLimiter("disabled");
    }

    return RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
            QueueLimit = 0
        });
}
