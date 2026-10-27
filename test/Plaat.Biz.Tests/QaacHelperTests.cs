using Microsoft.Extensions.Logging.Abstractions;
using Plaat.Biz.Options;
using Plaat.Biz.Qaac;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class QaacHelperTests
{
    private static QaacHelper Build(string? qaacPath)
    {
        return new QaacHelper(NullLogger<QaacHelper>.Instance, new PlaatOptions
        {
            ImagesStoragePath = "x",
            TargetReconvertedPath = "x",
            JwtOptions = new JwtOptions { Key = "k" },
            QaacPath = qaacPath
        });
    }

    [Fact]
    public async Task ConvertFileUsingQaac_WithoutConfiguredPath_Throws()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => Build(null).ConvertFileUsingQaac("in.wav", "out.m4a"));

        ex.Message.ShouldBe("Qaac path not found");
    }

    [Fact]
    public async Task ConvertFileUsingQaac_WithMissingExecutable_Throws()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => Build("/nonexistent/qaac.exe").ConvertFileUsingQaac("in.wav", "out.m4a"));

        ex.Message.ShouldBe("Qaac path not found");
    }

    [Fact]
    public async Task ConvertFileUsingQaac_WhenQaacCannotRun_Throws()
    {
        // Not a real Windows executable: either wine is missing, or wine/qaac fails with a
        // non-zero exit code - both surface as an InvalidOperationException for the caller.
        var fakeQaac = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(fakeQaac, "not an exe");

            await Should.ThrowAsync<InvalidOperationException>(
                () => Build(fakeQaac).ConvertFileUsingQaac("/nonexistent/in.wav", Path.Combine(Path.GetTempPath(), "out.m4a")));
        }
        finally
        {
            File.Delete(fakeQaac);
        }
    }
}
