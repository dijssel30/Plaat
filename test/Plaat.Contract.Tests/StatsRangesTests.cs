using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Contract.Tests;

public sealed class StatsRangesTests
{
    private static readonly DateTime Now = new(2026, 3, 17, 14, 42, 13);

    [Fact]
    public void StartOf_Day_IsTheStartOfTheHour23HoursBack()
    {
        StatsRanges.StartOf(StatsRange.Day, Now).ShouldBe(new DateTime(2026, 3, 16, 15, 0, 0));
    }

    [Fact]
    public void StartOf_Week_IsMidnight6DaysBack()
    {
        StatsRanges.StartOf(StatsRange.Week, Now).ShouldBe(new DateTime(2026, 3, 11));
    }

    [Fact]
    public void StartOf_Month_IsMidnight29DaysBack()
    {
        StatsRanges.StartOf(StatsRange.Month, Now).ShouldBe(new DateTime(2026, 2, 16));
    }

    [Fact]
    public void StartOf_Year_IsTheFirstOfTheMonth11MonthsBack()
    {
        StatsRanges.StartOf(StatsRange.Year, Now).ShouldBe(new DateTime(2025, 4, 1));
    }

    [Fact]
    public void StartOf_All_HasNoCutoff()
    {
        StatsRanges.StartOf(StatsRange.All, Now).ShouldBeNull();
    }

    [Fact]
    public void StartOf_KeepsTheKindOfTheClockItWasGiven()
    {
        // The API converts the result to UTC to filter with, which is only correct if the value it
        // gets back is still local.
        var local = DateTime.SpecifyKind(Now, DateTimeKind.Local);

        foreach (var range in Enum.GetValues<StatsRange>())
        {
            StatsRanges.StartOf(range, local)?.Kind.ShouldBe(DateTimeKind.Local);
        }
    }
}
