using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Plaat.Frontend.Components.Player;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

/// <summary>
/// The player's seek bar: clicking or dragging maps the pointer position onto the value range.
/// </summary>
public sealed class SeekBarComponentTests : BunitContext
{
    /// <summary>
    /// Answers "seekbar.getRect" with a 200px-wide bar starting at x=100.
    /// </summary>
    private sealed class FixedRectRuntime : IJSRuntime
    {
        public int RectRequests { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            RectRequests++;

            return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>("""{ "Left": 100, "Width": 200 }""")!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, args);
        }
    }

    private readonly FixedRectRuntime _js = new();
    private readonly List<double> _changes = [];

    public SeekBarComponentTests()
    {
        Services.AddSingleton<IJSRuntime>(_js);
    }

    private IRenderedComponent<SeekBar> RenderBar(double value, double min = 0, double max = 60)
    {
        return Render<SeekBar>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Min, min)
            .Add(x => x.Max, max)
            .Add(x => x.ValueChanged, v => _changes.Add(v)));
    }

    [Theory]
    [InlineData(15, 0, 60, "25.00")]
    [InlineData(-5, 0, 60, "0.00")]
    [InlineData(90, 0, 60, "100.00")]
    [InlineData(5, 10, 10, "0.00")]
    public void Progress_IsTheClampedShareOfTheRange(double value, double min, double max, string percent)
    {
        var cut = RenderBar(value, min, max);

        cut.Find(".seekbar-progress").GetAttribute("style").ShouldBe($"width: {percent}%");
    }

    [Fact]
    public void PointerDown_SeeksToThatPosition()
    {
        var cut = RenderBar(0);

        cut.Find(".seekbar").PointerDown(new PointerEventArgs { ClientX = 150 });

        _changes.ShouldBe([15]);
        cut.Find(".seekbar-thumb").GetAttribute("style").ShouldBe("left: 25.00%");
    }

    [Fact]
    public void Dragging_FollowsThePointerUntilReleasedOrLeft()
    {
        var cut = RenderBar(0);
        var bar = cut.Find(".seekbar");

        bar.PointerMove(new PointerEventArgs { ClientX = 200 });
        _changes.ShouldBeEmpty();

        cut.Find(".seekbar").PointerDown(new PointerEventArgs { ClientX = 0 });
        cut.Find(".seekbar").PointerMove(new PointerEventArgs { ClientX = 500 });
        cut.Find(".seekbar").PointerUp(new PointerEventArgs());
        cut.Find(".seekbar").PointerMove(new PointerEventArgs { ClientX = 200 });
        cut.Find(".seekbar").PointerDown(new PointerEventArgs { ClientX = 200 });
        cut.Find(".seekbar").PointerLeave(new PointerEventArgs());
        cut.Find(".seekbar").PointerMove(new PointerEventArgs { ClientX = 300 });

        _changes.ShouldBe([0, 60, 30]);
    }
}
