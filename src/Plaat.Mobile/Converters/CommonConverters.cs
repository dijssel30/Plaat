using System.Globalization;

namespace Plaat.Mobile.Converters;

public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not true;
    }
}

/// <summary>
/// Collapses a group header whose name is empty (a single-disc album): an IsVisible="False" label
/// still leaves the group header cell itself reserving its measured height, so the cell needs an
/// explicit zero. Non-empty names get -1, MAUI's "size to content".
/// </summary>
public sealed class EmptyStringToZeroHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? 0d : -1d;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// True when the bound string is non-null/non-empty - used to show error/status labels only when set.
/// </summary>
public sealed class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class IntGreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is > 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class FavoriteLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "Favorited" : "Favorite";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class ExpandToggleLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "Show less" : "Show more";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class SupportedFormatLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "Supported format" : "Unsupported format";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class SmartLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "Smart playlist" : "Playlist";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class ShuffleOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? 1.0 : 0.4;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class PlayPauseIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "pause.svg" : "play.svg";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class RepeatIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Services.Player.RepeatMode.One ? "repeat_one.svg" : "repeat.svg";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Dims the repeat icon when repeat is Off - without this, Off and All look identical since
/// they share the same icon, making it look like the button does nothing when cycling through Off.
/// </summary>
public sealed class RepeatOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Services.Player.RepeatMode.Off ? 0.4 : 1.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Tints the currently-playing row in the queue so it's clearly distinguishable from the rest.
/// </summary>
public sealed class CurrentTrackBackgroundConverter : IValueConverter
{
    private static readonly Color HighlightColor = Color.FromArgb("#26D600AA");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? HighlightColor : Colors.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class SecondsToTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var seconds = value is double d ? d : 0;
        var ts = TimeSpan.FromSeconds(Math.Max(seconds, 0));
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Approximates a height for a CollectionView nested inside a ScrollView (needed since CollectionView
/// otherwise collapses to zero height there) by multiplying an item count by a fixed row height. Only
/// appropriate for small, bounded lists (e.g. Recently Added) - not a substitute for virtualization.
/// </summary>
public sealed class CountToHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var rowHeight = parameter is string s && double.TryParse(s, culture, out var h) ? h : 56;
        return count * rowHeight;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
