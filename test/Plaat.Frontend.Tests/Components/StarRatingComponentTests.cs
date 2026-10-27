using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Plaat.Contract.Models;
using Plaat.Frontend.Components;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

public sealed class StarRatingComponentTests : BunitContext
{
    // The row the swipe tests pretend to drag across: 100px wide starting at x = 0, so each star
    // owns a 20px band and the arithmetic in the assertions stays obvious.
    private const double RowLeft = 0;
    private const double RowWidth = 100;

    public StarRatingComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<StarRating.StarRowRect?>("uiHelpers.beginPointerGesture", _ => true)
            .SetResult(new StarRating.StarRowRect(RowLeft, RowWidth));
    }

    private IRenderedComponent<StarRating> RenderStars(int value, Action<int>? onChange = null)
    {
        return Render<StarRating>(parameters =>
        {
            parameters.Add(p => p.Value, value);

            if (onChange is not null)
            {
                parameters.Add(p => p.ValueChanged, EventCallback.Factory.Create(this, onChange));
            }
        });
    }

    /// <summary>
    /// The star whose band <paramref name="x"/> falls in, i.e. the one a press there hits.
    /// </summary>
    private static int StarIndexAt(double x)
    {
        return Math.Clamp((int)(x / (RowWidth / TrackRatingDto.MaxStars)), 0, TrackRatingDto.MaxStars - 1);
    }

    private static Task Press(IRenderedComponent<StarRating> cut, double x)
    {
        return cut.FindAll("button.star")[StarIndexAt(x)]
            .TriggerEventAsync("onpointerdown", new PointerEventArgs { PointerId = 1, ClientX = x, Buttons = 1 });
    }

    private static Task Move(IRenderedComponent<StarRating> cut, double x)
    {
        return cut.Find(".star-rating")
            .TriggerEventAsync("onpointermove", new PointerEventArgs { PointerId = 1, ClientX = x, Buttons = 1 });
    }

    private static Task Release(IRenderedComponent<StarRating> cut, double x)
    {
        return cut.Find(".star-rating")
            .TriggerEventAsync("onpointerup", new PointerEventArgs { PointerId = 1, ClientX = x });
    }

    private static async Task Tap(IRenderedComponent<StarRating> cut, double x)
    {
        await Press(cut, x);
        await Release(cut, x);
    }

    private static async Task Swipe(IRenderedComponent<StarRating> cut, double from, double to)
    {
        await Press(cut, from);
        await Move(cut, to);
        await Release(cut, to);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    public void Render_FillsExactlyAsManyStarsAsTheValue(int value, int expectedFilled)
    {
        var cut = RenderStars(value);

        cut.FindAll(".star").Count.ShouldBe(TrackRatingDto.MaxStars);
        cut.FindAll(".star-filled").Count.ShouldBe(expectedFilled);
        cut.FindAll(".icon-star").Count.ShouldBe(expectedFilled);
        cut.FindAll(".icon-star-outline").Count.ShouldBe(TrackRatingDto.MaxStars - expectedFilled);
    }

    [Fact]
    public async Task Tap_ReportsTheStarItLandsOn()
    {
        var reported = -1;
        var cut = RenderStars(1, v => reported = v);

        await Tap(cut, 70); // the fourth star's band

        reported.ShouldBe(4);
    }

    [Fact]
    public async Task Tap_AfterMovingTheCursorOverTheRow_StillReportsTheStar()
    {
        // Regression test: a real cursor travels over the row before pressing, and the row captures
        // the pointer once pressed - between them these used to leave the tap unhandled entirely.
        var reported = -1;
        var cut = RenderStars(1, v => reported = v);

        await Move(cut, 30);
        await Move(cut, 70);
        await Tap(cut, 70);

        reported.ShouldBe(4);
    }

    [Fact]
    public async Task TappingTheCurrentRating_ClearsIt()
    {
        // The only way back to 0 stars by tapping - there is no zeroth star to press.
        var reported = -1;
        var cut = RenderStars(3, v => reported = v);

        await Tap(cut, 50); // the third star's band

        reported.ShouldBe(TrackRatingDto.MinStars);
    }

    [Fact]
    public void KeyboardActivation_ReportsTheStar()
    {
        // A keyboard-driven click carries no click count, which is how it is told apart from the
        // click a pointer produces (already handled by the pointer handlers).
        var reported = -1;
        var cut = RenderStars(1, v => reported = v);

        cut.FindAll("button.star")[3].Click(new MouseEventArgs { Detail = 0 });

        reported.ShouldBe(4);
    }

    [Fact]
    public void MouseClick_IsIgnored_BecauseThePointerHandlersAlreadyRanIt()
    {
        var reported = -1;
        var cut = RenderStars(1, v => reported = v);

        cut.FindAll("button.star")[3].Click(new MouseEventArgs { Detail = 1 });

        reported.ShouldBe(-1);
    }

    [Fact]
    public void Busy_DisablesEveryStar()
    {
        var cut = Render<StarRating>(parameters => parameters
            .Add(p => p.Value, 2)
            .Add(p => p.Busy, true));

        cut.FindAll("button.star").ShouldAllBe(button => button.HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(90, 5)]
    [InlineData(50, 3)]
    [InlineData(30, 2)]
    public async Task Swipe_ReportsTheStarItEndsOn(double releaseX, int expected)
    {
        var reported = -1;
        var cut = RenderStars(1, v => reported = v);

        await Swipe(cut, from: 10, to: releaseX);

        reported.ShouldBe(expected);
    }

    [Fact]
    public async Task Swipe_OffTheLeftOfTheRow_ClearsTheRating()
    {
        var reported = -1;
        var cut = RenderStars(4, v => reported = v);

        await Swipe(cut, from: 70, to: -40);

        reported.ShouldBe(TrackRatingDto.MinStars);
    }

    [Fact]
    public async Task Swipe_ShowsTheStarsUnderThePointerBeforeItIsReleased()
    {
        var cut = RenderStars(1);

        await Press(cut, 10);
        await Move(cut, 90);

        cut.FindAll(".star-filled").Count.ShouldBe(5);
    }

    [Fact]
    public async Task Swipe_BackToTheCurrentRating_ChangesNothing()
    {
        var reported = -1;
        var cut = RenderStars(3, v => reported = v);

        await Swipe(cut, from: 10, to: 50);

        reported.ShouldBe(-1);
        cut.FindAll(".star-filled").Count.ShouldBe(3);
    }

    [Fact]
    public async Task Swipe_IgnoresTheClickItEndsOn()
    {
        // A swipe releases over a star, and the browser turns that into a click. Handling it too
        // would clear the rating the swipe had just set.
        var changes = new List<int>();
        var cut = RenderStars(1, changes.Add);

        await Swipe(cut, from: 10, to: 90);
        cut.FindAll("button.star")[4].Click(new MouseEventArgs { Detail = 1 });

        changes.ShouldBe([5]);
    }

    [Fact]
    public async Task Release_WithoutTheMatchingPress_ChangesNothing()
    {
        var reported = -1;
        var cut = RenderStars(2, v => reported = v);

        await Release(cut, 90);

        reported.ShouldBe(-1);
    }

    [Fact]
    public async Task Hover_AfterTheButtonWasReleasedElsewhere_StopsTrackingTheCursor()
    {
        // A release the row never sees (the pointer capture failed, say) would otherwise leave the
        // gesture running, and the stars would follow the cursor around on plain hover.
        var reported = -1;
        var cut = RenderStars(2, v => reported = v);

        await Press(cut, 10);
        await cut.Find(".star-rating")
            .TriggerEventAsync("onpointermove", new PointerEventArgs { PointerId = 1, ClientX = 90, Buttons = 0 });

        cut.FindAll(".star-filled").Count.ShouldBe(2);

        await Release(cut, 90);

        reported.ShouldBe(-1);
    }

    [Fact]
    public async Task PointerCancel_AbandonsTheSwipe()
    {
        var reported = -1;
        var cut = RenderStars(2, v => reported = v);

        await Press(cut, 10);
        await Move(cut, 90);
        await cut.Find(".star-rating")
            .TriggerEventAsync("onpointercancel", new PointerEventArgs { PointerId = 1, ClientX = 90 });

        reported.ShouldBe(-1);
        cut.FindAll(".star-filled").Count.ShouldBe(2);
    }
}
