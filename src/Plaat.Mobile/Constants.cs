namespace Plaat.Mobile;

/// <summary>
/// Build-time app configuration. There's no bundled appsettings.json-style config file convention
/// on mobile (unlike Plaat.Api/Plaat.Frontend) - this only matters for a completely fresh install,
/// since the login screen's "Server address" field lets the user change it afterwards without a
/// rebuild (see <see cref="Plaat.Mobile.Services.ServerAddressStore"/>).
/// </summary>
public static class Constants
{
    // 10.0.2.2 is the Android emulator's alias for the host machine's localhost, matching
    // Plaat.Frontend's default PlaatBackendOptions:BaseUri (http://localhost:5014) in appsettings.json.
    // Point this at your actual Plaat.Api server address before building for a real device.
    public static readonly Uri BackendBaseUri = new("http://localhost:5014");
}
