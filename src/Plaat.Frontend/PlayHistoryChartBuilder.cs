namespace Plaat.Frontend;

public static class PlayHistoryChartBuilder
{
    public static int MonthSpan(DateTime earliest)
    {
        var now = DateTime.Now;

        return ((now.Year - earliest.Year) * 12) + now.Month - earliest.Month + 1;
    }

    public static (string[] Labels, int[] Counts) BucketByHour(IReadOnlyCollection<DateTime> plays)
    {
        var start = TruncateToHour(DateTime.Now).AddHours(-23);
        var labels = new string[24];
        var counts = new int[24];

        for (var i = 0; i < 24; i++)
        {
            labels[i] = start.AddHours(i).ToString("HH:00");
        }

        foreach (var play in plays)
        {
            var index = (int)(TruncateToHour(play) - start).TotalHours;

            if (index is >= 0 and < 24)
            {
                counts[index]++;
            }
        }

        return (labels, counts);
    }

    private static DateTime TruncateToHour(DateTime value)
    {
        return value.Date.AddHours(value.Hour);
    }

    /// <summary>
    /// Folds every play onto a single 24-hour clock, so the same hour on different days lands in
    /// the same bucket. Unlike the other builders this has no window: it answers "what time of day
    /// do I listen", which only means anything once many days are piled on top of each other.
    /// </summary>
    public static (string[] Labels, int[] Counts) BucketByHourOfDay(IReadOnlyCollection<DateTime> plays)
    {
        var labels = new string[24];
        var counts = new int[24];

        for (var hour = 0; hour < 24; hour++)
        {
            labels[hour] = $"{hour:00}:00";
        }

        foreach (var play in plays)
        {
            counts[play.Hour]++;
        }

        return (labels, counts);
    }

    public static (string[] Labels, int[] Counts) BucketByDay(IReadOnlyCollection<DateTime> plays, int days)
    {
        var start = DateTime.Today.AddDays(-(days - 1));
        var labels = new string[days];
        var counts = new int[days];

        for (var i = 0; i < days; i++)
        {
            labels[i] = start.AddDays(i).ToString("d MMM");
        }

        foreach (var play in plays)
        {
            var index = (play.Date - start).Days;

            if (index >= 0 && index < days)
            {
                counts[index]++;
            }
        }

        return (labels, counts);
    }

    public static (string[] Labels, int[] Counts) BucketByMonth(IReadOnlyCollection<DateTime> plays, int months)
    {
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-(months - 1));
        var labels = new string[months];
        var counts = new int[months];

        for (var i = 0; i < months; i++)
        {
            labels[i] = start.AddMonths(i).ToString("MMM yyyy");
        }

        foreach (var play in plays)
        {
            var playMonth = new DateTime(play.Year, play.Month, 1);
            var index = ((playMonth.Year - start.Year) * 12) + playMonth.Month - start.Month;

            if (index >= 0 && index < months)
            {
                counts[index]++;
            }
        }

        return (labels, counts);
    }
}
