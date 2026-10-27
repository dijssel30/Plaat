using System.Globalization;
using Plaat.Mobile.Services.Streaming;

namespace Plaat.Mobile.Converters;

/// <summary>
/// Converts a numeric id bound in XAML into a loopback-proxy cover/artwork URL. Resolves the
/// LoopbackProxyService singleton via ServiceHelper since converters are instantiated by the XAML
/// parser with no constructor injection available.
/// </summary>
public abstract class IdToProxyUrlConverter : IValueConverter
{
    protected abstract Func<LoopbackProxyService, uint, Uri> BuildUrl { get; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return null;
        }

        var id = System.Convert.ToUInt32(value, culture);
        var proxy = ServiceHelper.GetRequiredService<LoopbackProxyService>();
        return BuildUrl(proxy, id).ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class AlbumCoverConverter : IdToProxyUrlConverter
{
    protected override Func<LoopbackProxyService, uint, Uri> BuildUrl
    {
        get
        {
            return (p, id) => p.AlbumCoverUrl(id);
        }
    }
}

public sealed class TrackCoverConverter : IdToProxyUrlConverter
{
    protected override Func<LoopbackProxyService, uint, Uri> BuildUrl
    {
        get
        {
            return (p, id) => p.TrackCoverUrl(id);
        }
    }
}
