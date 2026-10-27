using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Plaat.Contract.Models;
using Plaat.Contract.Models.Authorization;

namespace Plaat.Contract.Client;

/// <summary>
/// Typed HTTP client for the Plaat API - covers tracks, albums, artists, playlists, playback state,
/// auth, users, monitored folders, background tasks and logs.
/// </summary>
public interface IPlaatClient
{
    /// <summary>
    /// Gets one filtered/sorted page of tracks plus the total match count.
    /// </summary>
    Task<PagedResult<TrackDto>> GetTracksPage(TrackQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the ordered IDs of every track matching a query (for building a play queue).
    /// </summary>
    Task<IReadOnlyList<uint>> GetTrackIds(TrackQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves track IDs to their summaries, preserving order (for rebuilding a saved queue).
    /// </summary>
    Task<IReadOnlyList<TrackDto>> GetTracksByIds(IReadOnlyCollection<uint> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the most recently played distinct tracks, most-recent first.
    /// </summary>
    Task<IReadOnlyList<TrackDto>> GetRecentlyPlayed(CancellationToken cancellationToken);

    /// <summary>
    /// Searches tracks, albums and artists; each section is capped server-side.
    /// </summary>
    Task<SearchResultsDto> Search(string query, CancellationToken cancellationToken);

    /// <summary>
    /// Gets recently added standalone tracks and albums.
    /// </summary>
    Task<RecentlyAddedDto> GetRecentlyAdded(CancellationToken cancellationToken);

    /// <summary>
    /// Gets listening stats for one time range and listener scope - play history and top
    /// genres/artists within it - plus the range-independent library totals.
    /// </summary>
    Task<StatsDto> GetStats(StatsRange range, StatsScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// Gets full details for a single track. Returns null if not found.
    /// </summary>
    Task<TrackDetailsDto?> GetTrackDetails(uint trackId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks one of a track's locations as its default playback location.
    /// </summary>
    Task SetDefaultTrackLocation(uint trackId, uint locationId, CancellationToken cancellationToken);

    /// <summary>
    /// Forces a track's metadata to be re-read from its file on disk. Returns false if not found.
    /// </summary>
    Task<bool> ReloadTrackMetadata(uint trackId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets (or clears, by passing null) a track's admin-only playback trim points. Returns false if
    /// the track was not found.
    /// </summary>
    Task<bool> SetTrackTrim(uint trackId, TimeSpan? trimStart, TimeSpan? trimEnd, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all tracks flagged as possibly corrupt.
    /// </summary>
    Task<IReadOnlyCollection<PossiblyCorruptTrackDto>> GetPossiblyCorruptTracks(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a summary of every album in the library.
    /// </summary>
    Task<IReadOnlyCollection<AlbumOnlyDto>> GetAlbums(CancellationToken cancellationToken);

    /// <summary>
    /// Gets full details for a single album. Returns null if not found.
    /// </summary>
    Task<AlbumDetailsDto?> GetAlbum(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a summary of every album the current user has marked as a favorite.
    /// </summary>
    Task<IReadOnlyCollection<AlbumOnlyDto>> GetFavoriteAlbums(CancellationToken cancellationToken);

    /// <summary>
    /// Gets whether the current user has marked an album as a favorite.
    /// </summary>
    Task<bool> IsAlbumFavorite(uint albumId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds or removes an album from the current user's favorites.
    /// </summary>
    Task SetAlbumFavorite(uint albumId, bool isFavorite, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current user's star rating for a track. Returns 0 when they never rated it.
    /// </summary>
    Task<int> GetTrackRating(uint trackId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the current user's star rating for a track; 0 clears it. Returns false if the track
    /// does not exist.
    /// </summary>
    Task<bool> SetTrackRating(uint trackId, int stars, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads and persists a new cover image for an album. Returns false if the album was not found.
    /// </summary>
    Task<bool> UploadAlbumCover(int albumId, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a summary of every artist in the library.
    /// </summary>
    Task<IReadOnlyCollection<ArtistOnlyDto>> GetArtists(CancellationToken cancellationToken);

    /// <summary>
    /// Gets full details for a single artist. Returns null if not found.
    /// </summary>
    Task<ArtistDetailsDto?> GetArtist(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Triggers Discogs enrichment for an artist and returns the updated details.
    /// </summary>
    Task<ArtistDetailsDto?> EnrichArtist(uint artistId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a summary of every artist the current user has marked as a favorite.
    /// </summary>
    Task<IReadOnlyCollection<ArtistOnlyDto>> GetFavoriteArtists(CancellationToken cancellationToken);

    /// <summary>
    /// Gets whether the current user has marked an artist as a favorite.
    /// </summary>
    Task<bool> IsArtistFavorite(uint artistId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds or removes an artist from the current user's favorites.
    /// </summary>
    Task SetArtistFavorite(uint artistId, bool isFavorite, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current playback state (queue, current track, position). Returns null if none is stored.
    /// </summary>
    Task<MusicState?> GetCurrentState(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the playback position of the currently playing track. Sent on every player tick.
    /// </summary>
    Task UpdateCurrentTrackPlayback(TrackPlaybackState playback, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the current play queue. Sent only when the queue actually changes.
    /// </summary>
    Task UpdateCurrentPlaylist(PlaylistState playlist, CancellationToken cancellationToken);

    /// <summary>
    /// Clears any stored playback state.
    /// </summary>
    Task ClearCurrentState(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a summary of every playlist (regular and smart) belonging to the current user.
    /// </summary>
    Task<IReadOnlyCollection<PlaylistOnlyDto>> GetPlaylists(CancellationToken cancellationToken);

    /// <summary>
    /// Gets full details for a playlist by name. Returns null if not found.
    /// </summary>
    Task<PlaylistDetailsDto?> GetPlaylist(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a track to a playlist.
    /// </summary>
    Task AddToPlaylist(ulong playlistId, ulong trackId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a track from a playlist.
    /// </summary>
    Task RemoveFromPlaylist(uint playlistId, uint trackId, CancellationToken cancellationToken);

    /// <summary>
    /// Reorders a playlist's tracks to match the given order.
    /// </summary>
    Task ReorderPlaylist(uint playlistId, IReadOnlyList<uint> orderedTrackIds, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new empty regular playlist. Returns false if the name is already taken.
    /// </summary>
    Task<bool> CreatePlaylist(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new smart playlist with the given rules. Returns false if the name is already taken.
    /// </summary>
    Task<bool> CreateSmartPlaylist(string name, CreateSmartPlaylistRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a smart playlist's current rule tree and limit settings for editing. Returns null if not found.
    /// </summary>
    Task<SmartPlaylistRulesDto?> GetSmartPlaylistRules(uint id, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an existing smart playlist's entire rule tree. Returns false if not found.
    /// </summary>
    Task<bool> UpdateSmartPlaylist(uint id, CreateSmartPlaylistRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Copies an existing regular or smart playlist under a new name, keeping its tracks (in order)
    /// or its rules. Returns false if the new name is already taken.
    /// </summary>
    Task<bool> DuplicatePlaylist(uint id, bool isSmart, string newName, CancellationToken cancellationToken);

    /// <summary>
    /// Sets whether a regular or smart playlist is pinned.
    /// </summary>
    Task SetPlaylistPinned(uint id, bool isSmart, bool isPinned, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a regular or smart playlist.
    /// </summary>
    Task DeletePlaylist(uint id, bool isSmart, CancellationToken cancellationToken);

    /// <summary>
    /// Logs in with a username and password, returning a fresh token pair. Returns null on failure.
    /// <paramref name="clientIp"/> is the originating browser's IP (this call itself is made
    /// server-side, so the API would otherwise only see this server's own IP) - forwarded so
    /// failed-login logging on the API reflects the real source.
    /// </summary>
    Task<TokenResponse?> Login(string username, string password, string? clientIp, CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a refresh token for a fresh token pair. Returns null if it is invalid or expired.
    /// </summary>
    Task<TokenResponse?> RefreshToken(string refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the range of app versions this backend is compatible with. Returns null if the call
    /// fails (including a pre-compatibility-check backend that predates this endpoint), which
    /// callers should treat as "assume compatible" rather than as a warning-worthy result.
    /// </summary>
    Task<AppCompatibilityDto?> GetAppCompatibility(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the currently-authenticated user's own account info and settings.
    /// </summary>
    Task<CurrentUserDto?> GetCurrentUser(CancellationToken cancellationToken);

    /// <summary>
    /// Enables or disables loudness normalization for the current user.
    /// </summary>
    Task SetLoudnessNormalization(bool enabled, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a summary of every user account.
    /// </summary>
    Task<IReadOnlyCollection<UserSummaryDto>> GetUsers(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new user account.
    /// </summary>
    Task CreateUser(CreateUserRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the current user's own password. Returns false if the current password is wrong.
    /// </summary>
    Task<bool> ChangeOwnPassword(ChangePasswordRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Sets another user's password (admin action).
    /// </summary>
    Task SetUserPassword(string username, SetPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Grants or revokes admin rights for a user (admin action).
    /// </summary>
    Task SetUserAdmin(string username, bool isAdmin, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all folders currently monitored for library scans.
    /// </summary>
    Task<IReadOnlyCollection<MonitoredFolderDto>> GetMonitoredFolders(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a folder to monitor. Returns false if it is already monitored.
    /// </summary>
    Task<bool> AddMonitoredFolder(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Stops monitoring a folder.
    /// </summary>
    Task RemoveMonitoredFolder(uint id, CancellationToken cancellationToken);

    /// <summary>
    /// Triggers a scan of all monitored folders.
    /// </summary>
    Task<bool> TriggerFolderScan(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current status of background scan/convert tasks.
    /// </summary>
    Task<TasksStatusDto> GetTasksStatus(CancellationToken cancellationToken);

    /// <summary>
    /// Triggers the background audio-conversion queue.
    /// </summary>
    Task<bool> TriggerConvert(CancellationToken cancellationToken);

    /// <summary>
    /// Triggers the background loudness-analysis queue over the whole library.
    /// </summary>
    Task<bool> TriggerLoudnessAnalysis(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the most recent application log entries matching the given filter.
    /// </summary>
    Task<IReadOnlyCollection<LogEntryDto>> GetLogs(LogQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Re-runs Discogs enrichment for an artist.
    /// </summary>
    Task RefreshArtistDiscogsInfo(uint artistId, CancellationToken cancellationToken);

    /// <summary>
    /// Forces an artist to be linked to a specific Discogs artist ID.
    /// </summary>
    Task ForceArtistDiscogsId(uint artistId, int discogsArtistId, CancellationToken cancellationToken);
}

/// <summary>
/// Default <see cref="IPlaatClient"/> implementation: calls the Plaat API over HTTP and
/// transparently refreshes the access token before it expires.
/// </summary>
internal sealed class PlaatClient(
    IHttpClientFactory httpClientFactory,
    TokenStorageService tokenStorage,
    ITokenSessionStore tokenSessionStore) : IPlaatClient, IDisposable
{
    // How long before actual expiry we proactively refresh, so a request doesn't
    // start out mid-flight with a token that expires before the server sees it.
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    // The refresh call rotates the server-side refresh token as a side effect of the server
    // receiving it, whether or not the client sticks around for the response. If a caller's own
    // (possibly short) cancellation token were used here and fired while the request was still
    // in flight, the server could commit the rotation while the client never learns the new
    // token - leaving it holding a refresh token the server has already revoked, which then reads
    // as token theft (and nukes every session for the account) the next time anyone refreshes.
    // So the refresh call gets its own budget, independent of whatever the caller was willing to
    // wait for its own request.
    private static readonly TimeSpan RefreshCallTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private async Task<HttpClient> CreateAuthenticatedClient(CancellationToken cancellationToken)
    {
        await EnsureFreshAccessToken(cancellationToken);

        var client = httpClientFactory.CreateClient(nameof(PlaatClient));
        var accessToken = tokenStorage.AccessToken;

        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    private async Task EnsureFreshAccessToken(CancellationToken cancellationToken)
    {
        if (!NeedsRefresh())
        {
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // another call may have already refreshed it while we were waiting for the lock
            if (!NeedsRefresh())
            {
                return;
            }

            using var refreshCts = new CancellationTokenSource(RefreshCallTimeout);

            var tokenResponse = await RefreshToken(tokenStorage.RefreshToken!, refreshCts.Token);

            if (tokenResponse is not null)
            {
                await tokenSessionStore.PersistRefreshedToken(tokenResponse, refreshCts.Token);
            }
        }
        catch
        {
            // best-effort: proceed with whatever token we have. If the refresh token turned
            // out to be invalid too, the request below will fail with its own 401.
        }
        finally
        {
            _refreshLock.Release();
        }

        return;

        bool NeedsRefresh()
        {
            if (tokenStorage.AccessToken is null || tokenStorage.RefreshToken is null)
            {
                return false;
            }

            return tokenStorage.ExpiresAt is null || tokenStorage.ExpiresAt <= DateTime.UtcNow.Add(RefreshMargin);
        }
    }

    public async Task<TokenResponse?> Login(
        string username,
        string password,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(nameof(PlaatClient));

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "api/auth/login");
        requestMessage.Content = JsonContent.Create(new LoginRequest(username, password), PlaatJsonContext.Default.LoginRequest);

        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            requestMessage.Headers.Add("X-Forwarded-For", clientIp);
        }

        var response = await client.SendAsync(requestMessage, cancellationToken);

        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync(PlaatJsonContext.Default.TokenResponse, cancellationToken);

        return tokenResponse;
    }

    public async Task<TokenResponse?> RefreshToken(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(nameof(PlaatClient));

        var response = await client.PostAsJsonAsync(
            "api/auth/refresh",
            new RefreshRequest(refreshToken),
            PlaatJsonContext.Default.RefreshRequest,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync(PlaatJsonContext.Default.TokenResponse, cancellationToken);

        return tokenResponse;
    }

    public async Task<AppCompatibilityDto?> GetAppCompatibility(CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(nameof(PlaatClient));

        using var response = await client.GetAsync("api/app/compatibility", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync(PlaatJsonContext.Default.AppCompatibilityDto, cancellationToken);
    }

    public async Task<PagedResult<TrackDto>> GetTracksPage(TrackQuery query, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync(
            $"api/music?{BuildTrackQuery(query)}&skip={query.Skip}&take={query.Take}", PlaatJsonContext.Default.PagedResultTrackDto, cancellationToken);

        return response ?? new PagedResult<TrackDto>();
    }

    public async Task<IReadOnlyList<uint>> GetTrackIds(TrackQuery query, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync(
            $"api/music/ids?{BuildTrackQuery(query)}", PlaatJsonContext.Default.UInt32Array, cancellationToken);

        return response ?? [];
    }

    public async Task<IReadOnlyList<TrackDto>> GetTracksByIds(
        IReadOnlyCollection<uint> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsJsonAsync("api/music/by-ids", ids, PlaatJsonContext.Default.IReadOnlyCollectionUInt32, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(PlaatJsonContext.Default.TrackDtoArray, cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<TrackDto>> GetRecentlyPlayed(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync("api/music/recently-played", PlaatJsonContext.Default.TrackDtoArray, cancellationToken);

        return response ?? [];
    }

    public async Task<SearchResultsDto> Search(string query, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync(
            $"api/search?q={Uri.EscapeDataString(query)}", PlaatJsonContext.Default.SearchResultsDto, cancellationToken);

        return response ?? new SearchResultsDto();
    }

    private static string BuildTrackQuery(TrackQuery query)
    {
        var search = string.IsNullOrWhiteSpace(query.Search)
            ? string.Empty
            : $"search={Uri.EscapeDataString(query.Search)}&";

        return $"{search}sort={query.Sort}&desc={(query.Descending ? "true" : "false")}";
    }

    public async Task<RecentlyAddedDto> GetRecentlyAdded(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync("api/music/recently-added", PlaatJsonContext.Default.RecentlyAddedDto, cancellationToken);

        return response ?? new RecentlyAddedDto();
    }

    public async Task<StatsDto> GetStats(StatsRange range, StatsScope scope, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync(
            $"api/stats?range={range}&scope={scope}", PlaatJsonContext.Default.StatsDto, cancellationToken);

        return response ?? new StatsDto
        {
            Library = new LibraryStatsDto
            {
                TrackCount = 0,
                AlbumCount = 0,
                ArtistCount = 0,
                PossiblyCorruptCount = 0,
                TotalDuration = TimeSpan.Zero
            }
        };
    }

    public async Task<TrackDetailsDto?> GetTrackDetails(uint trackId, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"api/music/{trackId}/details");

        return await CallAndGetResponse(message, PlaatJsonContext.Default.TrackDetailsDto, cancellationToken);
    }

    public async Task SetDefaultTrackLocation(uint trackId, uint locationId, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PutAsync(
            $"api/music/{trackId}/locations/{locationId}/default", null, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> SetTrackTrim(uint trackId, TimeSpan? trimStart, TimeSpan? trimEnd, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PutAsJsonAsync(
            $"api/music/{trackId}/trim", new SetTrackTrimRequest(trimStart, trimEnd), PlaatJsonContext.Default.SetTrackTrimRequest, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }

    public async Task<bool> ReloadTrackMetadata(uint trackId, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsync($"api/music/{trackId}/reload-metadata", null, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }

    public async Task<IReadOnlyCollection<PossiblyCorruptTrackDto>> GetPossiblyCorruptTracks(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync("api/music/possibly-corrupt", PlaatJsonContext.Default.PossiblyCorruptTrackDtoArray, cancellationToken);

        return response ?? [];
    }

    public async Task<AlbumDetailsDto?> GetAlbum(int id, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"api/albums/{id}");

        return await CallAndGetResponse(message, PlaatJsonContext.Default.AlbumDetailsDto, cancellationToken);
    }

    public async Task<IReadOnlyCollection<AlbumOnlyDto>> GetAlbums(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync("api/albums", PlaatJsonContext.Default.AlbumOnlyDtoArray, cancellationToken);

        return response ?? [];
    }

    public async Task<IReadOnlyCollection<AlbumOnlyDto>> GetFavoriteAlbums(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync("api/favorites/albums", PlaatJsonContext.Default.AlbumOnlyDtoArray, cancellationToken);

        return response ?? [];
    }

    public async Task<bool> IsAlbumFavorite(uint albumId, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.GetAsync($"api/albums/{albumId}/favorite", cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(PlaatJsonContext.Default.Boolean, cancellationToken);
    }

    public async Task SetAlbumFavorite(uint albumId, bool isFavorite, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = isFavorite
            ? await client.PutAsync($"api/albums/{albumId}/favorite", null, cancellationToken)
            : await client.DeleteAsync($"api/albums/{albumId}/favorite", cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<int> GetTrackRating(uint trackId, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync(
            $"api/ratings/tracks/{trackId}", PlaatJsonContext.Default.TrackRatingDto, cancellationToken);

        return response?.Stars ?? TrackRatingDto.MinStars;
    }

    public async Task<bool> SetTrackRating(uint trackId, int stars, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PutAsJsonAsync(
            $"api/ratings/tracks/{trackId}", new SetTrackRatingRequest(stars), PlaatJsonContext.Default.SetTrackRatingRequest, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }

    public async Task<bool> UploadAlbumCover(int albumId, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(fileContent, "file", "cover");

        using var response = await client.PostAsync($"api/albums/{albumId}/cover", form, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }

    public async Task<ArtistDetailsDto?> GetArtist(int id, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"api/artists/{id}");

        return await CallAndGetResponse(message, PlaatJsonContext.Default.ArtistDetailsDto, cancellationToken);
    }

    public async Task<ArtistDetailsDto?> EnrichArtist(uint artistId, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsync($"api/artists/{artistId}/enrich", null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(PlaatJsonContext.Default.ArtistDetailsDto, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ArtistOnlyDto>> GetArtists(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync("api/artists", PlaatJsonContext.Default.ArtistOnlyDtoArray, cancellationToken);

        return response ?? [];
    }

    public async Task<IReadOnlyCollection<ArtistOnlyDto>> GetFavoriteArtists(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync("api/favorites/artists", PlaatJsonContext.Default.ArtistOnlyDtoArray, cancellationToken);

        return response ?? [];
    }

    public async Task<bool> IsArtistFavorite(uint artistId, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.GetAsync($"api/artists/{artistId}/favorite", cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(PlaatJsonContext.Default.Boolean, cancellationToken);
    }

    public async Task SetArtistFavorite(uint artistId, bool isFavorite, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = isFavorite
            ? await client.PutAsync($"api/artists/{artistId}/favorite", null, cancellationToken)
            : await client.DeleteAsync($"api/artists/{artistId}/favorite", cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateCurrentTrackPlayback(TrackPlaybackState playback, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsJsonAsync("api/state/track", playback, PlaatJsonContext.Default.TrackPlaybackState, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateCurrentPlaylist(PlaylistState playlist, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsJsonAsync("api/state/playlist", playlist, PlaatJsonContext.Default.PlaylistState, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<MusicState?> GetCurrentState(CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "api/state");

        return await CallAndGetResponse(message, PlaatJsonContext.Default.MusicState, cancellationToken);
    }

    public async Task ClearCurrentState(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.DeleteAsync("api/state", cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyCollection<PlaylistOnlyDto>> GetPlaylists(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync("api/playlists", PlaatJsonContext.Default.PlaylistOnlyDtoArray, cancellationToken);

        return response ?? [];
    }

    public async Task<PlaylistDetailsDto?> GetPlaylist(string name, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"api/playlists/{Uri.EscapeDataString(name)}");

        return await CallAndGetResponse(message, PlaatJsonContext.Default.PlaylistDetailsDto, cancellationToken);
    }

    public async Task AddToPlaylist(ulong playlistId, ulong trackId, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PutAsJsonAsync(
            $"api/playlists/{playlistId}", [trackId], PlaatJsonContext.Default.ListUInt64, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveFromPlaylist(uint playlistId, uint trackId, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.DeleteAsync(
            $"api/playlists/{playlistId}/tracks/{trackId}", cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task ReorderPlaylist(uint playlistId, IReadOnlyList<uint> orderedTrackIds, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PutAsJsonAsync(
            $"api/playlists/{playlistId}/order", orderedTrackIds, PlaatJsonContext.Default.IReadOnlyListUInt32, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task DeletePlaylist(uint id, bool isSmart, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.DeleteAsync(
            $"api/playlists/{id}?isSmart={isSmart}", cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> CreatePlaylist(string name, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsJsonAsync(
            $"api/playlists/{Uri.EscapeDataString(name)}", [], PlaatJsonContext.Default.UInt32Array, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Conflict)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }

    public async Task<bool> CreateSmartPlaylist(string name, CreateSmartPlaylistRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsJsonAsync(
            $"api/playlists/smart/{Uri.EscapeDataString(name)}", request, PlaatJsonContext.Default.CreateSmartPlaylistRequest, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Conflict)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }

    public async Task<SmartPlaylistRulesDto?> GetSmartPlaylistRules(uint id, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"api/playlists/smart/{id}/rules");

        return await CallAndGetResponse(message, PlaatJsonContext.Default.SmartPlaylistRulesDto, cancellationToken);
    }

    public async Task<bool> UpdateSmartPlaylist(uint id, CreateSmartPlaylistRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PutAsJsonAsync($"api/playlists/smart/{id}", request, PlaatJsonContext.Default.CreateSmartPlaylistRequest, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }

    public async Task<bool> DuplicatePlaylist(uint id, bool isSmart, string newName, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsJsonAsync(
            $"api/playlists/{id}/duplicate", new DuplicatePlaylistRequest(isSmart, newName), PlaatJsonContext.Default.DuplicatePlaylistRequest, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Conflict)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }

    public async Task SetPlaylistPinned(uint id, bool isSmart, bool isPinned, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PutAsJsonAsync(
            $"api/playlists/{id}/pin", new SetPlaylistPinnedRequest(isSmart, isPinned), PlaatJsonContext.Default.SetPlaylistPinnedRequest, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<CurrentUserDto?> GetCurrentUser(CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "api/users/me");

        return await CallAndGetResponse(message, PlaatJsonContext.Default.CurrentUserDto, cancellationToken);
    }

    public async Task SetLoudnessNormalization(bool enabled, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PutAsJsonAsync(
            "api/users/me/loudness-normalization", new SetLoudnessNormalizationRequest(enabled), PlaatJsonContext.Default.SetLoudnessNormalizationRequest, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyCollection<UserSummaryDto>> GetUsers(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync("api/users", PlaatJsonContext.Default.UserSummaryDtoArray, cancellationToken);

        return response ?? [];
    }

    public async Task CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsJsonAsync("api/users", request, PlaatJsonContext.Default.CreateUserRequest, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> ChangeOwnPassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PutAsJsonAsync("api/users/me/password", request, PlaatJsonContext.Default.ChangePasswordRequest, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }

    public async Task SetUserPassword(string username, SetPasswordRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PutAsJsonAsync(
            $"api/users/{Uri.EscapeDataString(username)}/password", request, PlaatJsonContext.Default.SetPasswordRequest, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task SetUserAdmin(string username, bool isAdmin, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PutAsJsonAsync(
            $"api/users/{Uri.EscapeDataString(username)}/admin", new SetAdminRequest(isAdmin), PlaatJsonContext.Default.SetAdminRequest, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyCollection<MonitoredFolderDto>> GetMonitoredFolders(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync("api/monitored-folders", PlaatJsonContext.Default.MonitoredFolderDtoArray, cancellationToken);

        return response ?? [];
    }

    public async Task<bool> AddMonitoredFolder(string path, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsJsonAsync(
            "api/monitored-folders", new AddMonitoredFolderRequest(path), PlaatJsonContext.Default.AddMonitoredFolderRequest, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Conflict)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }

    public async Task RemoveMonitoredFolder(uint id, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.DeleteAsync($"api/monitored-folders/{id}", cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> TriggerFolderScan(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsync("api/monitored-folders/scan", null, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(PlaatJsonContext.Default.Boolean, cancellationToken);
    }

    public async Task<TasksStatusDto> GetTasksStatus(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync("api/tasks/status", PlaatJsonContext.Default.TasksStatusDto, cancellationToken);

        return response ?? new TasksStatusDto(false, null, false, [], false, 0, 0);
    }

    public async Task<bool> TriggerConvert(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsync("api/queue/convert", null, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(PlaatJsonContext.Default.Boolean, cancellationToken);
    }

    public async Task<bool> TriggerLoudnessAnalysis(CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsync("api/loudness-analysis/scan", null, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(PlaatJsonContext.Default.Boolean, cancellationToken);
    }

    public async Task<IReadOnlyCollection<LogEntryDto>> GetLogs(LogQuery query, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        var response = await client.GetFromJsonAsync(
            $"api/logs?{BuildLogQuery(query)}", PlaatJsonContext.Default.LogEntryDtoArray, cancellationToken);

        return response ?? [];
    }

    private static string BuildLogQuery(LogQuery query)
    {
        var parameters = new List<string> { $"limit={query.Limit}" };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parameters.Add($"search={Uri.EscapeDataString(query.Search)}");
        }

        if (query.Levels is { Count: > 0 })
        {
            parameters.AddRange(query.Levels.Select(l => $"levels={Uri.EscapeDataString(l)}"));
        }

        return string.Join('&', parameters);
    }

    public async Task RefreshArtistDiscogsInfo(uint artistId, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsync($"api/artists/{artistId}/discogs/refresh", null, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task ForceArtistDiscogsId(uint artistId, int discogsArtistId, CancellationToken cancellationToken)
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.PostAsJsonAsync(
            $"api/artists/{artistId}/discogs/force", new ForceDiscogsArtistRequest(discogsArtistId), PlaatJsonContext.Default.ForceDiscogsArtistRequest, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private async Task<T?> CallAndGetResponse<T>(
        HttpRequestMessage message,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        using var client = await CreateAuthenticatedClient(cancellationToken);

        using var response = await client.SendAsync(message, cancellationToken);

        response.EnsureSuccessStatusCode();

        if (response.StatusCode is HttpStatusCode.NoContent)
        {
            return null;
        }

        // Straight off the stream rather than via an intermediate string - some of these bodies
        // (a playlist or artist with all its tracks) are large.
        return await response.Content.ReadFromJsonAsync(jsonTypeInfo, cancellationToken);
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
    }
}
