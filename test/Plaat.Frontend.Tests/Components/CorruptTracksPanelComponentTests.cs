using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Components.Pages.AdminPanels;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

public sealed class CorruptTracksPanelComponentTests : BunitContext
{
    private readonly IPlaatClient _client;

    public CorruptTracksPanelComponentTests()
    {
        _client = Substitute.For<IPlaatClient>();
        Services.AddSingleton(_client);
        Services.AddSingleton(new PlaatBackendOptions { BaseUri = new Uri("http://localhost") });
    }

    [Fact]
    public void OnInit_LoadsCorruptTracksFromClient()
    {
        _client.GetPossiblyCorruptTracks(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<PossiblyCorruptTrackDto>>([
                new PossiblyCorruptTrackDto(1, "Bad Track", "Some Artist", "Some Album", 5, ["/music/bad.mp3"])
            ]));

        var cut = Render<CorruptTracksPanel>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr");
            rows.Count.ShouldBe(1);
            cut.Markup.ShouldContain("Bad Track");
            cut.Markup.ShouldContain("/music/bad.mp3");
        });
    }

    [Fact]
    public void OnInit_NoTracks_ShowsEmptyMessage()
    {
        _client.GetPossiblyCorruptTracks(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<PossiblyCorruptTrackDto>>([]));

        var cut = Render<CorruptTracksPanel>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("No possibly corrupt tracks found.");
        });
    }

    [Fact]
    public void OnInit_ClientThrows_ShowsErrorMessage()
    {
        _client.GetPossiblyCorruptTracks(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyCollection<PossiblyCorruptTrackDto>>(_ => throw new HttpRequestException("network down"));

        var cut = Render<CorruptTracksPanel>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Failed to load corrupt tracks");
        });
    }
}
