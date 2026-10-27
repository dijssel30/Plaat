using Android.Views;

namespace Plaat.Mobile.Platforms.Android;

/// <summary>
/// Tells every ancestor view (walking all the way up, per Android's ViewParent contract) to stop
/// intercepting touch for the duration of a gesture that starts on this view. Needed because a
/// CollectionView's native RecyclerView otherwise claims any vertical touch stream for its own
/// scrolling before a child PanGestureRecognizer (e.g. a drag handle) gets a chance to see it.
/// Hooked via the View.Touch C# event rather than SetOnTouchListener so it coexists with whatever
/// touch listener the PanGestureRecognizer itself installs on the same native view, instead of
/// replacing it. e.Handled is deliberately left false - this only stops the parent from stealing
/// the touch, it doesn't consume it itself.
/// </summary>
internal static class DisallowInterceptTouchListener
{
    public static void OnTouch(object? sender, global::Android.Views.View.TouchEventArgs e)
    {
        var v = sender as global::Android.Views.View;
        switch (e.Event?.Action)
        {
            case MotionEventActions.Down:
            case MotionEventActions.Move:
                v?.Parent?.RequestDisallowInterceptTouchEvent(true);
                break;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                v?.Parent?.RequestDisallowInterceptTouchEvent(false);
                break;
        }
    }
}
