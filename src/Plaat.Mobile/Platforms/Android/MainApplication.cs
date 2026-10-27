using Android.App;
using Android.Runtime;

namespace Plaat.Mobile;

[Application]
public sealed class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp()
    {
        return MauiProgram.CreateMauiApp();
    }
}
