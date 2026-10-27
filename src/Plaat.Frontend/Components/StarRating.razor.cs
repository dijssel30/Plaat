using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components;

/// <summary>
/// A 0-5 star rating, set by tapping a star or by swiping across the row.
/// </summary>
/// <remarks>
/// Every pointer interaction - swipe <em>and</em> tap - is served by the pointer handlers rather
/// than by the stars' click event, because the row captures the pointer for the duration of a
/// gesture and a captured pointer's click is retargeted to the capturing row, never reaching the
/// star that was pressed. The click handler is therefore left to keyboard activation alone, which
/// arrives as a click carrying no click count.
/// </remarks>
public partial class StarRating : ComponentBase
{
    /// <summary>
    /// The star row's bounding box, measured once at the start of a gesture.
    /// </summary>
    public sealed record StarRowRect(double Left, double Width);

    /// <summary>
    /// How far the pointer has to travel before a press counts as a swipe rather than a tap on the
    /// star it started on.
    /// </summary>
    private const double DragThresholdPx = 6;

    [Inject]
    public required IJSRuntime JsRuntime { get; init; }

    /// <summary>
    /// Current rating, 0 (unrated) to <see cref="TrackRatingDto.MaxStars"/>.
    /// </summary>
    [Parameter]
    public int Value { get; set; }

    [Parameter]
    public EventCallback<int> ValueChanged { get; set; }

    /// <summary>
    /// Disables the stars while the caller is persisting a change.
    /// </summary>
    [Parameter]
    public bool Busy { get; set; }

    [Parameter]
    public string Label { get; set; } = "Rating";

    private ElementReference _rootRef;
    private StarRowRect? _rect;
    private bool _dragging;
    private bool _moved;
    private double _pressX;
    private int? _pressedStar;
    private int? _preview;

    /// <summary>
    /// Stars to paint: where a swipe currently stands, or the committed value.
    /// </summary>
    private int Display
    {
        get
        {
            return _preview ?? Value;
        }
    }

    private async Task OnStarPointerDown(int star, PointerEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        // Everything the rest of the gesture needs is set before the first await: the matching
        // pointerup is already on its way, and Blazor is free to run it while this handler is
        // suspended on the round trip below. Setting the state afterwards would leave the gesture
        // half-started - and _dragging stuck on, so the row would then follow the cursor on hover.
        _dragging = true;
        _moved = false;
        _pressX = e.ClientX;
        _pressedStar = star;
        _preview = null;
        _rect = null;

        var rect = await JsRuntime.InvokeAsync<StarRowRect?>("uiHelpers.beginPointerGesture", _rootRef, e.PointerId);

        // The press can be over before the measurement lands; a tap needs neither.
        if (_dragging)
        {
            _rect = rect;
        }
    }

    private void OnPointerMove(PointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        // Nothing pressed any more: the release happened somewhere we never heard about it. Without
        // this the row would go on following the cursor around on hover.
        if (e.Buttons == 0)
        {
            ResetGesture();
            return;
        }

        if (_rect is null || (!_moved && Math.Abs(e.ClientX - _pressX) < DragThresholdPx))
        {
            return;
        }

        _moved = true;
        _preview = StarsAt(e.ClientX);
    }

    private async Task OnPointerUp(PointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var swipedTo = _moved ? _preview : null;
        var pressedStar = _pressedStar;

        ResetGesture();

        await ReleaseCapture(e.PointerId);

        // A swipe lands on whatever star it ended over (including none, off the left edge); a tap
        // takes the star it pressed, and clears the rating when that star is the one already set.
        var newValue = swipedTo ?? (pressedStar == Value ? TrackRatingDto.MinStars : pressedStar);

        if (newValue is not null && newValue != Value)
        {
            await ValueChanged.InvokeAsync(newValue.Value);
        }
    }

    private async Task OnPointerCancel(PointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        ResetGesture();

        await ReleaseCapture(e.PointerId);
    }

    /// <summary>
    /// Keyboard activation only - see the note on the class. A click carrying a click count came
    /// from a pointer, which the pointer handlers have already dealt with.
    /// </summary>
    private Task OnStarClick(int star, MouseEventArgs e)
    {
        if (e.Detail > 0)
        {
            return Task.CompletedTask;
        }

        var newValue = star == Value ? TrackRatingDto.MinStars : star;

        return newValue == Value ? Task.CompletedTask : ValueChanged.InvokeAsync(newValue);
    }

    private void ResetGesture()
    {
        _dragging = false;
        _moved = false;
        _pressedStar = null;
        _preview = null;
    }

    private ValueTask ReleaseCapture(long pointerId)
    {
        return JsRuntime.InvokeVoidAsync("uiHelpers.endPointerGesture", _rootRef, pointerId);
    }

    /// <summary>
    /// The rating a pointer position stands for: the row is split into
    /// <see cref="TrackRatingDto.MaxStars"/> equal bands, and anything at or left of the row's start
    /// reads as 0, so swiping off the left edge clears the rating.
    /// </summary>
    private int StarsAt(double clientX)
    {
        if (_rect is not { Width: > 0 } rect)
        {
            return Value;
        }

        var relative = Math.Clamp((clientX - rect.Left) / rect.Width, 0, 1);

        return (int)Math.Ceiling(relative * TrackRatingDto.MaxStars);
    }

    private string StarTitle(int star)
    {
        return star == Value
        ? "Clear rating"
        : $"Rate {star} star{(star == 1 ? "" : "s")}";
    }
}
