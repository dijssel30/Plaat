using Plaat.Contract.Models;
using Plaat.Mobile.Services.Compatibility;
using Shouldly;
using Xunit;

namespace Plaat.Mobile.Tests;

public sealed class AppCompatibilityCheckerTests
{
    [Fact]
    public void IsCompatible_VersionAboveMinimumNoMaximum_ReturnsTrue()
    {
        var result = AppCompatibilityChecker.IsCompatible("2.0", new AppCompatibilityDto("1.0", null));

        result.ShouldBeTrue();
    }

    [Fact]
    public void IsCompatible_VersionEqualsMinimum_ReturnsTrue()
    {
        var result = AppCompatibilityChecker.IsCompatible("1.0", new AppCompatibilityDto("1.0", null));

        result.ShouldBeTrue();
    }

    [Fact]
    public void IsCompatible_VersionBelowMinimum_ReturnsFalse()
    {
        var result = AppCompatibilityChecker.IsCompatible("0.9", new AppCompatibilityDto("1.0", null));

        result.ShouldBeFalse();
    }

    [Fact]
    public void IsCompatible_VersionAboveMaximum_ReturnsFalse()
    {
        var result = AppCompatibilityChecker.IsCompatible("3.0", new AppCompatibilityDto("1.0", "2.0"));

        result.ShouldBeFalse();
    }

    [Fact]
    public void IsCompatible_VersionWithinRange_ReturnsTrue()
    {
        var result = AppCompatibilityChecker.IsCompatible("1.5", new AppCompatibilityDto("1.0", "2.0"));

        result.ShouldBeTrue();
    }

    [Fact]
    public void IsCompatible_UnparseableCurrentVersion_ReturnsTrue()
    {
        var result = AppCompatibilityChecker.IsCompatible("not-a-version", new AppCompatibilityDto("1.0", null));

        result.ShouldBeTrue();
    }
}
