using System.Data;
using System.Net.Mime;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Plaat.Biz.Scanners;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static class PlaylistEndpointMapper
{
    public static void MapPlaylistEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/playlists")
            .WithTags("Playlist")
            .RequireAuthorization();

        apiGroup.MapGet(string.Empty, GetAllPlaylists)
            .WithSummary("Get list of all playlists")
            .Produces<IReadOnlyCollection<PlaylistOnlyDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPost("{playlistName}", AddPlaylist)
            .WithSummary("Create a new playlist")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        apiGroup.MapPost("smart/{playlistName}", AddSmartPlaylist)
            .WithSummary("Create a new smart playlist")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        apiGroup.MapGet("{name}", GetPlaylistDetails)
            .WithSummary("Get playlist by ID")
            .Produces<PlaylistDetailsDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet("smart/{id:int}/rules", GetSmartPlaylistRules)
            .WithSummary("Get a smart playlist's rule tree for editing")
            .Produces<SmartPlaylistRulesDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("smart/{id:int}", UpdateSmartPlaylist)
            .WithSummary("Replace an existing smart playlist's rule tree")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("{id:int}", AddToPlaylist)
            .WithSummary("Update a playlist with new tracks")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapDelete("{id:int}/tracks/{trackId:int}", RemoveFromPlaylist)
            .WithSummary("Remove a track from a playlist")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("{id:int}/order", ReorderPlaylist)
            .WithSummary("Reorder the tracks in a playlist")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPost("{id:int}/duplicate", DuplicatePlaylist)
            .WithSummary("Copy an existing playlist under a new name")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        apiGroup.MapDelete("{id:int}", DeletePlaylist)
            .WithSummary("Delete a playlist by id")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("{id:int}/pin", SetPlaylistPinned)
            .WithSummary("Pin or unpin a playlist in the navigation menu")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPost("import/{filePath:file}", ImportPlaylist)
            .WithSummary("Import a playlist from m3u or m3u8 file")
            .RequireAuthorization(Policies.AdminPolicy)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ImportPlaylist(
        [FromRoute] string filePath,
        [FromServices] PlaylistScanner playlistScanner,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        filePath = filePath.Replace("%2F", "/");

        if (!File.Exists(filePath) ||
            !Path.GetExtension(filePath).StartsWith(".m3u", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        await playlistScanner.ImportM3uPlaylist(filePath, user.GetUserId(), ct);

        return Results.Ok();
    }

    private static async Task<IResult> GetPlaylistDetails(
        [FromRoute] string name,
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        name = name.Replace("%2F", "/");

        var playlist = await repository.GetPlaylist(name, user.GetUserId(), ct);

        return playlist is null
            ? Results.NoContent()
            : Results.Ok(playlist);
    }

    private static async Task<IResult> GetSmartPlaylistRules(
        [FromRoute] uint id,
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var rules = await repository.GetSmartPlaylistRules(id, user.GetUserId(), ct);

        return rules is null
            ? Results.NoContent()
            : Results.Ok(rules);
    }

    private static async Task<IResult> UpdateSmartPlaylist(
        [FromRoute] uint id,
        [FromBody] CreateSmartPlaylistRequest request,
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var updated = await repository.UpdateSmartPlaylist(
            id, user.GetUserId(), request.RootGroup, request.Limit, request.LimitOrderBy, ct);

        return updated ? Results.Ok() : Results.NotFound();
    }

    private static async Task<IReadOnlyCollection<PlaylistOnlyDto>> GetAllPlaylists(
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        return await repository.GetAllPlaylists(user.GetUserId(), ct);
    }

    private static async Task<IResult> AddPlaylist(
        [FromRoute] string playlistName,
        [FromBody] IReadOnlyCollection<uint> trackIds,
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        playlistName = playlistName.Replace("%2F", "/");

        try
        {
            await repository.CreatePlaylist(playlistName, user.GetUserId(), trackIds, ct);
            return Results.Ok();
        }
        catch (DuplicateNameException)
        {
            return Results.Conflict();
        }
    }

    private static async Task<IResult> AddSmartPlaylist(
        [FromRoute] string playlistName,
        [FromBody] CreateSmartPlaylistRequest request,
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        playlistName = playlistName.Replace("%2F", "/");

        try
        {
            await repository.CreateSmartPlaylist(
                playlistName, user.GetUserId(), request.RootGroup, request.Limit, request.LimitOrderBy, ct);
            return Results.Ok();
        }
        catch (DuplicateNameException)
        {
            return Results.Conflict();
        }
    }

    private static async Task AddToPlaylist(
        [FromRoute] uint id,
        [FromBody] IReadOnlyCollection<uint> trackIds,
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await repository.AddToPlaylist(id, user.GetUserId(), trackIds, ct);
    }

    private static async Task RemoveFromPlaylist(
        [FromRoute] uint id,
        [FromRoute] uint trackId,
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await repository.RemoveFromPlaylist(id, user.GetUserId(), trackId, ct);
    }

    private static async Task<IResult> ReorderPlaylist(
        [FromRoute] uint id,
        [FromBody] IReadOnlyList<uint> orderedTrackIds,
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var success = await repository.ReorderPlaylist(id, user.GetUserId(), orderedTrackIds, ct);

        return success ? Results.Ok() : Results.BadRequest();
    }

    private static async Task<IResult> DuplicatePlaylist(
        [FromRoute] uint id,
        [FromBody] DuplicatePlaylistRequest request,
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var newName = request.NewName.Trim();

        if (newName.Length == 0)
        {
            return Results.BadRequest();
        }

        try
        {
            var duplicated = await repository.DuplicatePlaylist(
                id, request.IsSmart, user.GetUserId(), newName, ct);

            return duplicated ? Results.Ok() : Results.NotFound();
        }
        catch (DuplicateNameException)
        {
            return Results.Conflict();
        }
    }

    private static async Task DeletePlaylist(
        [FromRoute] uint id,
        [FromQuery] bool isSmart,
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await repository.RemovePlaylist(id, isSmart, user.GetUserId(), ct);
    }

    private static async Task SetPlaylistPinned(
        [FromRoute] uint id,
        [FromBody] SetPlaylistPinnedRequest request,
        [FromServices] IPlaylistRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await repository.SetPlaylistPinned(id, request.IsSmart, user.GetUserId(), request.IsPinned, ct);
    }
}
