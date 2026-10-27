using Plaat.Db.Models;

namespace Plaat.Frontend.E2E.Tests;

/// <summary>
/// Everything <see cref="PlaatE2EFixture"/> seeds up front, so individual tests can reference it by name/id instead of re-seeding.
/// </summary>
public sealed class SeededCatalog
{
    public required User Admin { get; init; }

    /// <summary>
    /// A non-admin user for tests that just need to log in as someone other than admin (e.g. checking an Admin-only page redirects). Its credentials must stay stable for the whole run.
    /// </summary>
    public required User SecondaryUser { get; init; }
    public const string SecondaryUserPassword = "SecondaryPass123!";

    /// <summary>
    /// A separate non-admin user dedicated to the one test that actually changes its own password - kept apart from <see cref="SecondaryUser"/> so that mutation can never break other tests logging in with the latter's original credentials.
    /// </summary>
    public required User PasswordChangeUser { get; init; }
    public const string PasswordChangeUserPassword = "PasswordChangePass123!";
    public required Artist ArtistOne { get; init; }
    public required Artist ArtistTwo { get; init; }
    public required Album AlbumOne { get; init; }
    public required Album AlbumTwo { get; init; }
    public required Track TrackOne { get; init; }
    public required Track TrackTwo { get; init; }
    public required Track TrackThree { get; init; }
    public required Playlist Playlist { get; init; }
    public required MonitoredFolder MonitoredFolder { get; init; }
}
