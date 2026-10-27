namespace Plaat.Mobile;

/// <summary>
/// Service locator for the handful of places DI constructor injection isn't available - XAML
/// instantiates ContentPage/ContentView types via their parameterless constructor, so those resolve
/// their dependencies from here instead. Initialized once in MauiProgram.CreateMauiApp after the
/// service provider is built.
/// </summary>
public static class ServiceHelper
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services
    {
        get
        {
            return _services ?? throw new InvalidOperationException($"{nameof(ServiceHelper)} was not initialized.");
        }
    }

    public static void Initialize(IServiceProvider services)
    {
        _services = services;
    }

    public static T GetRequiredService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }
}
