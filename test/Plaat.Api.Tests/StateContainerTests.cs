using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

public sealed class StateContainerTests
{
    [Fact]
    public void InitialState_IsNull()
    {
        var container = new StateContainer();

        container.State.ShouldBeNull();
    }

    [Fact]
    public void State_CanBeSet()
    {
        var container = new StateContainer();
        var state = new MusicState { CurrentTrack = 1, CurrentSecond = 0 };

        container.State = state;

        container.State.ShouldBeSameAs(state);
    }

    [Fact]
    public void CurrentTrack_DefaultIsZero()
    {
        var container = new StateContainer();

        container.CurrentTrack.ShouldBe(0u);
    }

    [Fact]
    public void SecondsPlayed_InitiallyEmpty()
    {
        var container = new StateContainer();

        container.SecondsPlayed.ShouldBeEmpty();
    }

    [Fact]
    public void PlayCounted_DefaultIsFalse()
    {
        var container = new StateContainer();

        container.PlayCounted.ShouldBeFalse();
    }

    [Fact]
    public void SecondsPlayed_CanBeReplaced()
    {
        var container = new StateContainer();
        container.SecondsPlayed.Add(1);
        container.SecondsPlayed.Add(2);

        container.SecondsPlayed = [];

        container.SecondsPlayed.ShouldBeEmpty();
    }
}
