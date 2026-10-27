using Plaat.Contract.Models;

namespace Plaat.Api.EndpointMappers;

public static class AppCompatibilityEndpointMapper
{
    // Bump when a backend change requires a newer app build, or when support for old app builds
    // is dropped. Keep MinimumCompatibleAppVersion in sync with what's actually still supported.
    // MaximumCompatibleAppVersion stays null unless a future app version is known to break against
    // this backend. Compared against Plaat.Mobile.csproj's ApplicationDisplayVersion at runtime.
    private const string MinimumCompatibleAppVersion = "1.0";
    private const string? MaximumCompatibleAppVersion = null;

    public static void MapAppCompatibilityEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/app")
            .WithTags("App");

        apiGroup.MapGet("compatibility", GetCompatibility)
            .WithSummary("Gets the range of Plaat.Mobile app versions compatible with this backend")
            .Produces<AppCompatibilityDto>()
            .AllowAnonymous();
    }

    private static IResult GetCompatibility()
    {
        return Results.Ok(new AppCompatibilityDto(MinimumCompatibleAppVersion, MaximumCompatibleAppVersion));
    }
}
