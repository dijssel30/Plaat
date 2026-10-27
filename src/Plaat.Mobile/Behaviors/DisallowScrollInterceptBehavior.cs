namespace Plaat.Mobile.Behaviors;

/// <summary>
/// Attach to a drag handle inside a CollectionView row so its PanGestureRecognizer actually gets
/// touch-move events on Android instead of the CollectionView's own RecyclerView scrolling stealing
/// them first. No-op on other platforms - see DisallowInterceptTouchListener for why it's needed.
/// </summary>
public sealed class DisallowScrollInterceptBehavior : Behavior<View>
{
    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.HandlerChanged += OnHandlerChanged;
    }

    protected override void OnDetachingFrom(View bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        base.OnDetachingFrom(bindable);
    }

    private static void OnHandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        // Uses the View.Touch C# event (not SetOnTouchListener directly): the Android binding
        // multiplexes that event internally, so this coexists with the PanGestureRecognizer's own
        // touch handling on the same native view instead of replacing it outright.
        if (sender is View { Handler.PlatformView: global::Android.Views.View platformView })
        {
            platformView.Touch -= Platforms.Android.DisallowInterceptTouchListener.OnTouch;
            platformView.Touch += Platforms.Android.DisallowInterceptTouchListener.OnTouch;
        }
#endif
    }
}
