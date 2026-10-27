using Foundation;

namespace Plaat.Mobile;

[Register("AppDelegate")]
public sealed class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp()
	{
		return MauiProgram.CreateMauiApp();
	}
}
