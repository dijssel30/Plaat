using Plaat.Contract.Models.Authorization;
using Shouldly;
using Xunit;

namespace Plaat.Contract.Tests;

public sealed class TokenStorageServiceTests
{
    [Fact]
    public void IsAuthenticated_WithTokenAndFutureExpiry_IsTrue()
    {
        var storage = new TokenStorageService { AccessToken = "a", ExpiresAt = DateTime.UtcNow.AddMinutes(5) };

        storage.IsAuthenticated.ShouldBeTrue();
    }

    [Fact]
    public void IsAuthenticated_WithoutAccessToken_IsFalse()
    {
        var storage = new TokenStorageService { ExpiresAt = DateTime.UtcNow.AddMinutes(5) };

        storage.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public void IsAuthenticated_WithoutExpiry_IsFalse()
    {
        var storage = new TokenStorageService { AccessToken = "a" };

        storage.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public void IsAuthenticated_WithPastExpiry_IsFalse()
    {
        var storage = new TokenStorageService { AccessToken = "a", ExpiresAt = DateTime.UtcNow.AddMinutes(-1) };

        storage.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public void Clear_ResetsEveryField()
    {
        var storage = new TokenStorageService { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow };

        storage.Clear();

        storage.AccessToken.ShouldBeNull();
        storage.RefreshToken.ShouldBeNull();
        storage.ExpiresAt.ShouldBeNull();
    }
}
