using System.Net.Mime;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static partial class StateEndpointMapper
{
    public static void MapStateEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/state")
            .WithTags("State")
            .RequireAuthorization();

        apiGroup.MapPost("/track", UpdateTrackPlayback)
            .WithSummary("Update the playback position of the currently playing track")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPost("/playlist", UpdatePlaylist)
            .WithSummary("Update the current play queue (only sent when the queue actually changes)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapGet(string.Empty, GetCurrentMusicState)
            .WithSummary("Get the current state of music track and playlist")
            .Produces<MusicState>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapDelete(string.Empty, ClearState)
            .WithSummary("Clear the current state of music track and playlist")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static async Task UpdateTrackPlayback(
        [FromBody] TrackPlaybackState playback,
        [FromServices] UserStateStore stateStore,
        [FromServices] ITrackRepository trackRepository,
        [FromServices] ILogger<Program> logger,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetUserId();
        var stateContainer = stateStore.GetOrCreate(userId);

        var duration = await trackRepository.GetDuration(playback.CurrentTrack, ct);
        var trim = await trackRepository.GetTrimPoints(playback.CurrentTrack, ct);
        var windowStart = trim?.TrimStart ?? TimeSpan.Zero;
        var windowEnd = trim?.TrimEnd ?? duration;
        var windowLength = windowEnd is not null ? windowEnd.Value - windowStart : (TimeSpan?)null;

        bool countPlay;

        lock (stateContainer.SyncRoot)
        {
            var previous = stateContainer.State;
            stateContainer.State = new MusicState
            {
                CurrentTrack = playback.CurrentTrack,
                CurrentSecond = playback.CurrentSecond,
                TrackIds = previous?.TrackIds ?? [],
                IsShuffled = previous?.IsShuffled ?? false,
                OriginalTrackIds = previous?.OriginalTrackIds
            };

            if (stateContainer.CurrentTrack != playback.CurrentTrack)
            {
                stateContainer.CurrentTrack = playback.CurrentTrack;
                stateContainer.SecondsPlayed = [];
                stateContainer.PlayCounted = false;

                LogPlayingNewTrack(logger, userId, playback.CurrentTrack);
            }
            else if (!stateContainer.PlayCounted
                && playback.CurrentSecond >= windowStart.TotalSeconds
                && (windowEnd is null || playback.CurrentSecond < windowEnd.Value.TotalSeconds)
                && !stateContainer.SecondsPlayed.Contains((int)playback.CurrentSecond))
            {
                LogPlayingTrackAt(logger, userId, playback.CurrentTrack, playback.CurrentSecond);

                stateContainer.SecondsPlayed.Add((int)playback.CurrentSecond);
            }

            countPlay = windowLength is not null
                && (stateContainer.SecondsPlayed.Count > 100 || stateContainer.SecondsPlayed.Count > 0.4 * windowLength.Value.TotalSeconds)
                && !stateContainer.PlayCounted;

            if (countPlay)
            {
                // Claim the play inside the lock so concurrent requests can't count it twice.
                stateContainer.PlayCounted = true;
            }
        }

        if (countPlay)
        {
            await trackRepository.IncreasePlayCount(playback.CurrentTrack, userId, ct);

            LogPlayCounted(logger, userId, playback.CurrentTrack);
        }
    }

    private static void UpdatePlaylist(
        [FromBody] PlaylistState playlist,
        [FromServices] UserStateStore stateStore,
        [FromServices] ILogger<Program> logger,
        ClaimsPrincipal user)
    {
        var userId = user.GetUserId();

        var stateContainer = stateStore.GetOrCreate(userId);

        double currentSecond;

        lock (stateContainer.SyncRoot)
        {
            var previous = stateContainer.State;

            // Keep the last known position only while we're still on the same track; a moved
            // current-track pointer (next/previous/pick) starts the new track from the beginning.
            currentSecond = previous is not null && previous.CurrentTrack == playlist.CurrentTrack
                ? previous.CurrentSecond
                : 0;

            stateContainer.State = new MusicState
            {
                CurrentTrack = playlist.CurrentTrack,
                CurrentSecond = currentSecond,
                TrackIds = playlist.TrackIds,
                IsShuffled = playlist.IsShuffled,
                OriginalTrackIds = playlist.OriginalTrackIds
            };
        }

        LogPlaylistStateUpdated(logger, userId, playlist.CurrentTrack, currentSecond, string.Join(", ", playlist.TrackIds), playlist.IsShuffled, string.Join(", ", playlist.OriginalTrackIds ?? []));
    }

    private static IResult GetCurrentMusicState(
        [FromServices] UserStateStore stateStore,
        ClaimsPrincipal user)
    {
        var stateContainer = stateStore.GetOrCreate(user.GetUserId());
        MusicState? musicState;

        lock (stateContainer.SyncRoot)
        {
            musicState = stateContainer.State;
        }

        return musicState is null
            ? Results.NoContent()
            : Results.Ok(musicState);
    }

    private static void ClearState(
        [FromServices] UserStateStore stateStore,
        ClaimsPrincipal user)
    {
        var stateContainer = stateStore.GetOrCreate(user.GetUserId());

        lock (stateContainer.SyncRoot)
        {
            stateContainer.State = null;
            stateContainer.CurrentTrack = 0;
            stateContainer.SecondsPlayed = [];
            stateContainer.PlayCounted = false;
        }
    }

    [LoggerMessage(LogLevel.Trace, "User {UserId} playing new track {TrackId}.")]
    private static partial void LogPlayingNewTrack(ILogger logger, int userId, uint trackId);

    [LoggerMessage(LogLevel.Trace, "User {UserId} playing track {TrackId} at second {CurrentSecond}")]
    private static partial void LogPlayingTrackAt(ILogger logger, int userId, uint trackId, double currentSecond);

    [LoggerMessage(LogLevel.Trace, "Play counted for user {Id}. Track {TrackId}")]
    private static partial void LogPlayCounted(ILogger logger, int id, uint trackId);

    [LoggerMessage(LogLevel.Trace, "User {userId} updated playlist state. Current track {TrackId}. Current second {CurrentSecond}. Tracks [{TrackIds}]. Is shuffled {IsShuffled}. Original [{OriginalTrackIds}]")]
    private static partial void LogPlaylistStateUpdated(ILogger logger, int userId, uint trackId, double currentSecond, string trackIds, bool isShuffled, string originalTrackIds);
}
