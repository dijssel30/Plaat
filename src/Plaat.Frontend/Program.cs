using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using MudBlazor.Services;
using Plaat.Contract;
using Plaat.Contract.Models.Authorization;
using Plaat.Frontend;
using Plaat.Frontend.Auth;
using Plaat.Frontend.Components;
using Plaat.Frontend.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Mobile browsers (Chrome in particular) throttle JS timers and can stall the SignalR
// connection for a while when the tab/app is backgrounded, which otherwise trips the default
// 30s client timeout and kills the circuit - breaking playback since @onended etc. can no
// longer reach the server. These give a backgrounded tab more slack before that happens, and
// keep the circuit around longer so it can be reconnected to instead of forcing a full reload.
builder.Services.Configure<HubOptions>(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
});

builder.Services.Configure<CircuitOptions>(options =>
{
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
});

var keysDirectory = Path.Combine(builder.Environment.ContentRootPath, "DataProtectionKeys");

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
    .SetApplicationName("Plaat.Frontend");

var backendOptions = LoadRequiredOptions<PlaatBackendOptions>();
builder.Services.AddPlaatClient(backendOptions);
builder.Services.AddMudServices();

builder.Services.AddHttpClient("proxy");

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<MusicPlaylist>();
builder.Services.AddScoped<NavPlaylistsState>();
builder.Services.AddScoped<TrackRatingState>();

builder.Services.AddScoped<JwtAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    provider => provider.GetRequiredService<JwtAuthStateProvider>());
builder.Services.AddScoped<TokenStorageService>();
builder.Services.AddScoped<PersistentTokenStore>();
builder.Services.AddScoped<ITokenSessionStore, TokenSessionStore>();
builder.Services.AddScoped<IProxySessionSync, ProxySessionSync>();
builder.Services.AddScoped<AuthService>();

builder.Services.AddSingleton<ProxySessionStore>();

// The source of truth for login state is still the JWT held client-side by JwtAuthStateProvider.
// This cookie scheme mirrors it (signed in from SessionEndpoints.Establish whenever the client
// establishes/refreshes its session) purely so the initial, pre-circuit HTTP request for an
// [Authorize] page - e.g. a browser refresh - sees the same authenticated/roles state the circuit
// already has, instead of always challenging and bouncing through /login.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.ReturnUrlParameter = "returnUrl";

        // Without this, a hard navigation (not a soft in-app NavLink click) to an Admin-only page
        // by a signed-in-but-non-admin user hits ASP.NET Core's cookie-auth default AccessDeniedPath
        // ("/Account/AccessDenied"), which doesn't exist here and 404s - bypassing Routes.razor's own
        // <NotAuthorized> -> RedirectToLogin handling entirely, since that only runs once Blazor's
        // client-side router is already active.
        options.AccessDeniedPath = "/login";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy =>
    {
        policy.RequireRole("Admin");
    });
});
builder.Services.AddCascadingAuthenticationState();

await using var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Deliberately no script-src/default-src: Blazor and MudBlazor rely on inline scripts and styles.
        headers["Content-Security-Policy"] = "frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'";

        return Task.CompletedTask;
    });

    await next();
});

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapSessionEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return;

T LoadRequiredOptions<T>() where T : class
{
    return builder.Configuration
        .GetRequiredSection(typeof(T).Name)
        .Get<T>() ?? throw new Exception($"Missing {typeof(T).Name}");
}
