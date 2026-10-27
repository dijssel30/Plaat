namespace Plaat.Mobile.Services;

/// <summary>
/// Persists the Plaat.Api server address entered on the login screen (via <see cref="Preferences"/>,
/// plain local key-value storage - the address isn't a secret) so it survives app restarts without
/// needing a rebuild. Falls back to <see cref="Constants.BackendBaseUri"/> - the old build-time-only
/// default - until the user saves one explicitly.
/// </summary>
public static class ServerAddressStore
{
    private const string PreferenceKey = "ServerBaseUri";

    public static Uri Load()
    {
        return Uri.TryCreate(Preferences.Default.Get(PreferenceKey, (string?)null), UriKind.Absolute, out var uri)
            ? uri
            : Constants.BackendBaseUri;
    }

    public static void Save(Uri uri)
    {
        Preferences.Default.Set(PreferenceKey, uri.ToString());
    }
}
