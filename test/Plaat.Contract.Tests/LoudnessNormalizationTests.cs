using Plaat.Contract.Loudness;
using Shouldly;
using Xunit;

namespace Plaat.Contract.Tests;

public sealed class LoudnessNormalizationTests
{
    [Fact]
    public void ComputeAttenuationFactor_WithNoMeasurement_ReturnsUnchanged()
    {
        LoudnessNormalization.ComputeAttenuationFactor(null).ShouldBe(1.0);
    }

    [Fact]
    public void ComputeAttenuationFactor_QuieterThanTarget_IsClampedToUnchanged()
    {
        // -20 LUFS is quieter than the -14 LUFS default target - never boost.
        LoudnessNormalization.ComputeAttenuationFactor(-20f).ShouldBe(1.0);
    }

    [Fact]
    public void ComputeAttenuationFactor_LouderThanTarget_AttenuatesByExpectedRatio()
    {
        // -4 LUFS is 10 LUFS louder than the -14 LUFS default target: 10^(-10/20) = ~0.316.
        var factor = LoudnessNormalization.ComputeAttenuationFactor(-4f);

        factor.ShouldBeLessThan(1.0);
        factor.ShouldBe(Math.Pow(10, -10.0 / 20.0), 0.0001);
    }

    [Fact]
    public void ComputeAttenuationFactor_ExactlyAtTarget_ReturnsUnchanged()
    {
        LoudnessNormalization.ComputeAttenuationFactor(-14f).ShouldBe(1.0, 0.0001);
    }

    [Fact]
    public void ComputeAttenuationFactor_ExtremelyLoud_NeverExceedsOne()
    {
        LoudnessNormalization.ComputeAttenuationFactor(0f).ShouldBeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void ComputeAttenuationFactor_NegativeInfinity_ClampsToUnchanged()
    {
        LoudnessNormalization.ComputeAttenuationFactor(float.NegativeInfinity).ShouldBe(1.0);
    }
}
