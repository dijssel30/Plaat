using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests;

public sealed class PlayHistoryChartBuilderTests
{
    [Fact]
    public void BucketByHour_PlacesPlayInCorrectHourBucket()
    {
        var play = DateTime.Now.AddHours(-2);

        var (labels, counts) = PlayHistoryChartBuilder.BucketByHour([play]);

        labels.Length.ShouldBe(24);
        counts.Length.ShouldBe(24);
        counts.Sum().ShouldBe(1);
        counts[^3].ShouldBe(1);
    }

    [Fact]
    public void BucketByHour_IgnoresPlaysOutsideTheWindow()
    {
        var play = DateTime.Now.AddDays(-2);

        var (_, counts) = PlayHistoryChartBuilder.BucketByHour([play]);

        counts.Sum().ShouldBe(0);
    }

    [Fact]
    public void BucketByDay_PlacesPlayInCorrectDayBucket()
    {
        var play = DateTime.Today.AddDays(-3);

        var (labels, counts) = PlayHistoryChartBuilder.BucketByDay([play], 7);

        labels.Length.ShouldBe(7);
        counts.Length.ShouldBe(7);
        counts.Sum().ShouldBe(1);
        counts[^4].ShouldBe(1);
    }

    [Fact]
    public void BucketByDay_IgnoresPlaysOutsideTheWindow()
    {
        var play = DateTime.Today.AddDays(-30);

        var (_, counts) = PlayHistoryChartBuilder.BucketByDay([play], 7);

        counts.Sum().ShouldBe(0);
    }

    [Fact]
    public void BucketByMonth_PlacesPlayInCorrectMonthBucket()
    {
        var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var play = currentMonth.AddMonths(-2);

        var (labels, counts) = PlayHistoryChartBuilder.BucketByMonth([play], 12);

        labels.Length.ShouldBe(12);
        counts.Length.ShouldBe(12);
        counts.Sum().ShouldBe(1);
        counts[^3].ShouldBe(1);
    }

    [Fact]
    public void MonthSpan_ComputesInclusiveMonthCount()
    {
        var now = DateTime.Now;
        var earliest = new DateTime(now.Year, now.Month, 1).AddMonths(-5);

        var span = PlayHistoryChartBuilder.MonthSpan(earliest);

        span.ShouldBe(6);
    }

    [Fact]
    public void MonthSpan_SameMonthAsNow_ReturnsOne()
    {
        var span = PlayHistoryChartBuilder.MonthSpan(DateTime.Now);

        span.ShouldBe(1);
    }

    [Fact]
    public void BucketByHourOfDay_PilesTheSameHourOfDifferentDaysOntoOneBucket()
    {
        var evening = new DateTime(2026, 3, 1, 21, 30, 0);

        var (labels, counts) = PlayHistoryChartBuilder.BucketByHourOfDay(
            [evening, evening.AddDays(-40), evening.AddDays(-400), evening.AddHours(-13)]);

        labels.Length.ShouldBe(24);
        labels[21].ShouldBe("21:00");
        counts[21].ShouldBe(3);
        counts[8].ShouldBe(1);
        counts.Sum().ShouldBe(4);
    }

    [Fact]
    public void BucketByHourOfDay_WithNoPlays_ReturnsAFullEmptyClock()
    {
        var (labels, counts) = PlayHistoryChartBuilder.BucketByHourOfDay([]);

        labels.Length.ShouldBe(24);
        labels[0].ShouldBe("00:00");
        counts.Sum().ShouldBe(0);
    }
}
