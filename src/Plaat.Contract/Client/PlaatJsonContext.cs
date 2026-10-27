using System.Text.Json;
using System.Text.Json.Serialization;
using Plaat.Contract.Models;
using Plaat.Contract.Models.Authorization;

namespace Plaat.Contract.Client;

/// <summary>
/// Source-generated System.Text.Json metadata for every request/response body <see cref="PlaatClient"/> sends or reads.
/// Web defaults (camelCase, case-insensitive), the same as the HttpClient JSON extensions used before.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(PagedResult<TrackDto>))]
[JsonSerializable(typeof(uint[]))]
[JsonSerializable(typeof(IReadOnlyCollection<uint>))]
[JsonSerializable(typeof(IReadOnlyList<uint>))]
[JsonSerializable(typeof(List<ulong>))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(TrackDto[]))]
[JsonSerializable(typeof(TrackDetailsDto))]
[JsonSerializable(typeof(SetTrackTrimRequest))]
[JsonSerializable(typeof(PossiblyCorruptTrackDto[]))]
[JsonSerializable(typeof(SearchResultsDto))]
[JsonSerializable(typeof(RecentlyAddedDto))]
[JsonSerializable(typeof(StatsDto))]
[JsonSerializable(typeof(AlbumOnlyDto[]))]
[JsonSerializable(typeof(AlbumDetailsDto))]
[JsonSerializable(typeof(TrackRatingDto))]
[JsonSerializable(typeof(SetTrackRatingRequest))]
[JsonSerializable(typeof(ArtistOnlyDto[]))]
[JsonSerializable(typeof(ArtistDetailsDto))]
[JsonSerializable(typeof(ForceDiscogsArtistRequest))]
[JsonSerializable(typeof(MusicState))]
[JsonSerializable(typeof(TrackPlaybackState))]
[JsonSerializable(typeof(PlaylistState))]
[JsonSerializable(typeof(PlaylistOnlyDto[]))]
[JsonSerializable(typeof(PlaylistDetailsDto))]
[JsonSerializable(typeof(CreateSmartPlaylistRequest))]
[JsonSerializable(typeof(SmartPlaylistRulesDto))]
[JsonSerializable(typeof(SetPlaylistPinnedRequest))]
[JsonSerializable(typeof(DuplicatePlaylistRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(CurrentUserDto))]
[JsonSerializable(typeof(SetLoudnessNormalizationRequest))]
[JsonSerializable(typeof(UserSummaryDto[]))]
[JsonSerializable(typeof(CreateUserRequest))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(SetPasswordRequest))]
[JsonSerializable(typeof(SetAdminRequest))]
[JsonSerializable(typeof(MonitoredFolderDto[]))]
[JsonSerializable(typeof(AddMonitoredFolderRequest))]
[JsonSerializable(typeof(TasksStatusDto))]
[JsonSerializable(typeof(LogEntryDto[]))]
[JsonSerializable(typeof(AppCompatibilityDto))]
internal sealed partial class PlaatJsonContext : JsonSerializerContext;
