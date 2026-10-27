using Plaat.Db.Repositories;
using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class UserRepositoryTests
{
    [Fact]
    public async Task CreateUser_PersistsUserWithIsAdmin()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateUserRepository();

        await repo.CreateUser("newuser", "hashed", true, CancellationToken.None);

        var hash = await repo.GetPasswordHash("newuser", CancellationToken.None);
        hash.ShouldBe("hashed");
    }

    [Fact]
    public async Task CreateUser_WhenUsernameDiffersOnlyByCase_ReturnsFalse()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateUserRepository();

        (await repo.CreateUser("Admin", "hashed", false, CancellationToken.None)).ShouldBeTrue();
        (await repo.CreateUser("admin", "hashed", false, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task GetAll_ReturnsAllUsersWithIsAdmin()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedUser("alice", isAdmin: true);
        fixture.SeedUser("bob", isAdmin: false);

        var repo = fixture.CreateUserRepository();
        var result = await repo.GetAll(CancellationToken.None);

        result.Count.ShouldBe(2);
        result.ShouldContain(u => u.Username == "alice" && u.IsAdmin);
        result.ShouldContain(u => u.Username == "bob" && !u.IsAdmin);
    }

    [Fact]
    public async Task SetIsAdmin_WhenUserExists_UpdatesFlagAndReturnsTrue()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedUser("alice");

        var repo = fixture.CreateUserRepository();
        var result = await repo.SetIsAdmin("alice", true, CancellationToken.None);

        result.ShouldBeTrue();
        var all = await repo.GetAll(CancellationToken.None);
        all.Single(u => u.Username == "alice").IsAdmin.ShouldBeTrue();
    }

    [Fact]
    public async Task SetIsAdmin_WhenUserDoesNotExist_ReturnsFalse()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateUserRepository();

        var result = await repo.SetIsAdmin("nobody", true, CancellationToken.None);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task SetLoudnessNormalizationEnabled_WhenUserExists_UpdatesFlagAndReturnsTrue()
    {
        using var fixture = new DbTestFixture();
        var seeded = fixture.SeedUser("alice");

        var repo = fixture.CreateUserRepository();
        var result = await repo.SetLoudnessNormalizationEnabled("alice", false, CancellationToken.None);

        result.ShouldBeTrue();
        var current = await repo.GetCurrentUser(seeded.Id, CancellationToken.None);
        current.ShouldNotBeNull();
        current.LoudnessNormalizationEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task SetLoudnessNormalizationEnabled_WhenUserDoesNotExist_ReturnsFalse()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateUserRepository();

        var result = await repo.SetLoudnessNormalizationEnabled("nobody", false, CancellationToken.None);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCurrentUser_DefaultsToLoudnessNormalizationEnabled()
    {
        using var fixture = new DbTestFixture();
        var seeded = fixture.SeedUser("alice");

        var repo = fixture.CreateUserRepository();
        var result = await repo.GetCurrentUser(seeded.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.LoudnessNormalizationEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task GetById_WhenUserExists_ReturnsUser()
    {
        using var fixture = new DbTestFixture();
        var seeded = fixture.SeedUser("alice", isAdmin: true);

        var repo = fixture.CreateUserRepository();
        var result = await repo.GetById(seeded.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Username.ShouldBe("alice");
        result.IsAdmin.ShouldBeTrue();
    }

    [Fact]
    public async Task GetById_WhenUserDoesNotExist_ReturnsNull()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateUserRepository();

        var result = await repo.GetById(9999, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task RevokeAllRefreshTokens_PreventsExistingTokensFromRotating()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedUser("alice");
        fixture.SeedUser("bob");
        var repo = fixture.CreateUserRepository();
        var aliceOne = await repo.CreateRefreshToken("alice", CancellationToken.None);
        var aliceTwo = await repo.CreateRefreshToken("alice", CancellationToken.None);
        var bob = await repo.CreateRefreshToken("bob", CancellationToken.None);

        await repo.RevokeAllRefreshTokens("alice", CancellationToken.None);

        (await repo.RotateRefreshToken(aliceOne.RefreshToken, CancellationToken.None)).ShouldBeNull();
        (await repo.RotateRefreshToken(aliceTwo.RefreshToken, CancellationToken.None)).ShouldBeNull();
        (await repo.RotateRefreshToken(bob.RefreshToken, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task RevokeAllRefreshTokens_WhenUserDoesNotExist_DoesNotThrow()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateUserRepository();

        await repo.RevokeAllRefreshTokens("nobody", CancellationToken.None);
    }

    [Fact]
    public async Task CreateRefreshToken_StartsASeparateFamilyPerLogin()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedUser("alice");
        var repo = fixture.CreateUserRepository();

        await repo.CreateRefreshToken("alice", CancellationToken.None);
        await repo.CreateRefreshToken("alice", CancellationToken.None);

        using var ctx = fixture.CreateContext();
        ctx.RefreshTokens.Select(t => t.FamilyId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task RotateRefreshToken_CarriesTheFamilyForwardAndRecordsTheSuccessor()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedUser("alice");
        var repo = fixture.CreateUserRepository();
        var first = await repo.CreateRefreshToken("alice", CancellationToken.None);

        var rotated = await repo.RotateRefreshToken(first.RefreshToken, CancellationToken.None);

        rotated.ShouldNotBeNull();
        using var ctx = fixture.CreateContext();
        var tokens = ctx.RefreshTokens.OrderBy(t => t.Id).ToList();
        tokens.Count.ShouldBe(2);
        tokens[0].FamilyId.ShouldBe(tokens[1].FamilyId);
        tokens[0].IsRevoked.ShouldBeTrue();
        tokens[0].RevokedAt.ShouldNotBeNull();
        tokens[0].ReplacedByTokenHash.ShouldBe(tokens[1].TokenHash);
        tokens[1].IsRevoked.ShouldBeFalse();
    }

    [Fact]
    public async Task RotateRefreshToken_WhenReplayedInsideTheGracePeriod_IssuesATokenAndSparesTheFamily()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedUser("alice");
        var repo = fixture.CreateUserRepository();
        var first = await repo.CreateRefreshToken("alice", CancellationToken.None);
        var winner = await repo.RotateRefreshToken(first.RefreshToken, CancellationToken.None);
        winner.ShouldNotBeNull();

        // The loser of the race (or a client whose response was lost) presents the old token again.
        var replay = await repo.RotateRefreshToken(first.RefreshToken, CancellationToken.None);

        replay.ShouldNotBeNull();
        replay.Username.ShouldBe("alice");
        replay.RefreshToken.ShouldNotBe(winner.RefreshToken);

        // The winner's token is untouched - this is the whole point: nobody gets signed out.
        (await repo.RotateRefreshToken(winner.RefreshToken, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task RotateRefreshToken_WhenReplayedAfterTheGracePeriod_RevokesTheFamily()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedUser("alice");
        var repo = fixture.CreateUserRepository();
        var first = await repo.CreateRefreshToken("alice", CancellationToken.None);
        var winner = await repo.RotateRefreshToken(first.RefreshToken, CancellationToken.None);
        winner.ShouldNotBeNull();

        BackdateRevocation(fixture, UserRepository.RotationGracePeriod + TimeSpan.FromMinutes(1));

        (await repo.RotateRefreshToken(first.RefreshToken, CancellationToken.None)).ShouldBeNull();

        // Stale replay this late reads as theft, so the rest of the chain dies with it.
        (await repo.RotateRefreshToken(winner.RefreshToken, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task RotateRefreshToken_ReuseDetection_LeavesTheSameUsersOtherDevicesSignedIn()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedUser("alice");
        var repo = fixture.CreateUserRepository();

        // Two logins by one user - the browser and the phone.
        var web = await repo.CreateRefreshToken("alice", CancellationToken.None);
        var mobile = await repo.CreateRefreshToken("alice", CancellationToken.None);

        var webRotated = await repo.RotateRefreshToken(web.RefreshToken, CancellationToken.None);
        webRotated.ShouldNotBeNull();
        BackdateRevocation(fixture, UserRepository.RotationGracePeriod + TimeSpan.FromMinutes(1));

        // Detonate the browser's family.
        (await repo.RotateRefreshToken(web.RefreshToken, CancellationToken.None)).ShouldBeNull();
        (await repo.RotateRefreshToken(webRotated.RefreshToken, CancellationToken.None)).ShouldBeNull();

        // The phone is a different family and must be completely unaffected.
        (await repo.RotateRefreshToken(mobile.RefreshToken, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task RotateRefreshToken_AfterRevokeAll_IsNotReplayableThroughTheGracePeriod()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedUser("alice");
        var repo = fixture.CreateUserRepository();
        var token = await repo.CreateRefreshToken("alice", CancellationToken.None);

        // A password change revokes without a successor, so the grace period must not apply
        // however recent the revocation is.
        await repo.RevokeAllRefreshTokens("alice", CancellationToken.None);

        (await repo.RotateRefreshToken(token.RefreshToken, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task RotateRefreshToken_WhenTokenIsUnknown_RevokesNothing()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedUser("alice");
        var repo = fixture.CreateUserRepository();
        var token = await repo.CreateRefreshToken("alice", CancellationToken.None);

        (await repo.RotateRefreshToken("not-a-real-token", CancellationToken.None)).ShouldBeNull();

        (await repo.RotateRefreshToken(token.RefreshToken, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task RotateRefreshToken_WhenTokenPredatesFamilies_IsRejectedWithoutTouchingOtherSessions()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedUser("alice");
        var repo = fixture.CreateUserRepository();
        var legacy = await repo.CreateRefreshToken("alice", CancellationToken.None);
        var other = await repo.CreateRefreshToken("alice", CancellationToken.None);

        // The shape the migration leaves behind for a token that was already revoked before
        // families existed: revoked, but with no successor and no revocation time recorded.
        using (var ctx = fixture.CreateContext())
        {
            var token = ctx.RefreshTokens.OrderBy(t => t.Id).First();
            token.IsRevoked = true;
            token.RevokedAt = null;
            token.ReplacedByTokenHash = null;
            ctx.SaveChanges();
        }

        (await repo.RotateRefreshToken(legacy.RefreshToken, CancellationToken.None)).ShouldBeNull();

        // The migration gives every pre-existing token a family of its own, so rejecting one
        // cannot reach a session that merely happened to predate the migration too.
        (await repo.RotateRefreshToken(other.RefreshToken, CancellationToken.None)).ShouldNotBeNull();
    }

    /// <summary>
    /// Pushes every recorded revocation further into the past, so a test can cross the rotation
    /// grace period without waiting out real time.
    /// </summary>
    private static void BackdateRevocation(DbTestFixture fixture, TimeSpan by)
    {
        using var ctx = fixture.CreateContext();

        foreach (var token in ctx.RefreshTokens.Where(t => t.RevokedAt != null).ToList())
        {
            token.RevokedAt -= by;
        }

        ctx.SaveChanges();
    }
}
