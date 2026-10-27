namespace Plaat.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Aurora is a dark-only theme: never follow the device's light mode. Setting this in the
        // App() constructor (before the native Activity exists) forces an AppCompatDelegate night-mode
        // switch that races Shell's first TabBar (BottomNavigationView) inflation and crashes with
        // "This component requires that you specify a valid TextAppearance attribute" - setting it here,
        // once a window/activation state exists, avoids that race.
        UserAppTheme = AppTheme.Dark;

        return new Window(new AppShell());
    }
}
