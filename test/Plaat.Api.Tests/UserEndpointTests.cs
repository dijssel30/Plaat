using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Plaat.Contract.Models.Authorization;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class UserEndpointTests(PlaatApiWebApplicationFactory factory)
    : IClassFixture<PlaatApiWebApplicationFactory>
{
    // Must match the claims set up by TestAuthHandler.
    private const int TestUserId = 1;
    private const string TestUsername = "testuser";

    private readonly HttpClient _client = CreateClient(factory);

    private static HttpClient CreateClient(PlaatApiWebApplicationFactory factory)
    {
        // The factory (and its substitute) is shared across tests in this class.
        factory.UserRepository.ClearReceivedCalls();

        return factory.CreateClient();
    }

    [Fact]
    public async Task GetUsers_ReturnsAllUsers()
    {
        factory.UserRepository.GetAll(Arg.Any<CancellationToken>())
            .Returns([new UserSummaryDto(1, "alice", true), new UserSummaryDto(2, "bob", false)]);

        var response = await _client.GetAsync("/api/users");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var users = await response.Content.ReadFromJsonAsync<UserSummaryDto[]>();
        users.ShouldNotBeNull();
        users.Length.ShouldBe(2);
    }

    [Fact]
    public async Task CreateUser_WhenUsernameAvailable_ReturnsOk()
    {
        factory.UserRepository.CreateUser(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var response = await _client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest
            {
                Username = "  newuser  ",
                Password = "password123!",
                IsAdmin = false
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _ = factory.UserRepository.Received().CreateUser(
            "newuser", Arg.Any<string>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateUser_WhenUsernameTaken_ReturnsConflict()
    {
        factory.UserRepository.CreateUser(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var response = await _client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest
            {
                Username = "newuser",
                Password = "password123!",
                IsAdmin = false
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ChangeOwnPassword_WhenCurrentPasswordCorrect_ReturnsOk()
    {
        var hasher = new PasswordHasher<string>();
        var hash = hasher.HashPassword(TestUsername, "OldPass123!");

        factory.UserRepository.GetById(TestUserId, Arg.Any<CancellationToken>())
            .Returns(new UserSummaryDto(TestUserId, TestUsername, false));
        factory.UserRepository.GetPasswordHash(TestUsername, Arg.Any<CancellationToken>())
            .Returns(hash);

        var response = await _client.PutAsJsonAsync(
            "/api/users/me/password",
            new ChangePasswordRequest("OldPass123!", "NewPass456!"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory.UserRepository.Received(1).UpdateHashedPassword(
            TestUsername, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await factory.UserRepository.Received(1).RevokeAllRefreshTokens(
            TestUsername, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeOwnPassword_WhenCurrentPasswordWrong_ReturnsUnauthorized()
    {
        var hasher = new PasswordHasher<string>();
        var hash = hasher.HashPassword(TestUsername, "OldPass123!");

        factory.UserRepository.GetById(TestUserId, Arg.Any<CancellationToken>())
            .Returns(new UserSummaryDto(TestUserId, TestUsername, false));
        factory.UserRepository.GetPasswordHash(TestUsername, Arg.Any<CancellationToken>())
            .Returns(hash);

        var response = await _client.PutAsJsonAsync(
            "/api/users/me/password",
            new ChangePasswordRequest("WrongPassword!", "NewPass456!"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await factory.UserRepository.DidNotReceive().RevokeAllRefreshTokens(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetUserPassword_WhenUserExists_ReturnsOk()
    {
        factory.UserRepository.GetPasswordHash("bob", Arg.Any<CancellationToken>())
            .Returns("existing-hash");

        var response = await _client.PutAsJsonAsync(
            "/api/users/bob/password",
            new SetPasswordRequest("NewPass456!"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory.UserRepository.Received(1).RevokeAllRefreshTokens(
            "bob", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetUserPassword_WhenUserDoesNotExist_ReturnsNotFound()
    {
        factory.UserRepository.GetPasswordHash("nobody", Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var response = await _client.PutAsJsonAsync(
            "/api/users/nobody/password",
            new SetPasswordRequest("NewPass456!"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await factory.UserRepository.DidNotReceive().RevokeAllRefreshTokens(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetUserAdmin_WhenRevokingOwnAdminRole_ReturnsBadRequest()
    {
        factory.UserRepository.GetById(TestUserId, Arg.Any<CancellationToken>())
            .Returns(new UserSummaryDto(TestUserId, TestUsername, false));

        var response = await _client.PutAsJsonAsync(
            $"/api/users/{TestUsername}/admin",
            new SetAdminRequest(false));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await factory.UserRepository.DidNotReceive().SetIsAdmin(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetUserAdmin_WhenGrantingOtherUser_ReturnsOk()
    {
        factory.UserRepository.SetIsAdmin("bob", true, Arg.Any<CancellationToken>())
            .Returns(true);

        var response = await _client.PutAsJsonAsync(
            "/api/users/bob/admin",
            new SetAdminRequest(true));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetUserAdmin_WhenRevokingOtherUser_RevokesTheirRefreshTokens()
    {
        factory.UserRepository.GetById(TestUserId, Arg.Any<CancellationToken>())
            .Returns(new UserSummaryDto(TestUserId, TestUsername, true));
        factory.UserRepository.SetIsAdmin("bob", false, Arg.Any<CancellationToken>())
            .Returns(true);

        var response = await _client.PutAsJsonAsync(
            "/api/users/bob/admin",
            new SetAdminRequest(false));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory.UserRepository.Received(1).RevokeAllRefreshTokens(
            "bob", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetUserAdmin_WhenGranting_DoesNotRevokeRefreshTokens()
    {
        factory.UserRepository.SetIsAdmin("bob", true, Arg.Any<CancellationToken>())
            .Returns(true);

        await _client.PutAsJsonAsync("/api/users/bob/admin", new SetAdminRequest(true));

        await factory.UserRepository.DidNotReceive().RevokeAllRefreshTokens(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
