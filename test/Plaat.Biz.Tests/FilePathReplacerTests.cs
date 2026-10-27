using Plaat.Biz.Options;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class FilePathReplacerTests
{
    private static PlaatOptions BuildOptions(ReplaceOptions? replaceOptions = null)
    {
        return new()
        {
            ImagesStoragePath = "/images",
            TargetReconvertedPath = "/converted",
            JwtOptions = new JwtOptions { Key = "test-key" },
            ReplaceOptions = replaceOptions
        };
    }

    [Fact]
    public void GetTargetPath_NullPath_ReturnsNull()
    {
        var replacer = new FilePathReplacer(BuildOptions());

        var result = replacer.GetTargetPath(null);

        result.ShouldBeNull();
    }

    [Fact]
    public void GetTargetPath_NoReplaceOptionsConfigured_OnlyNormalizesSlashes()
    {
        var replacer = new FilePathReplacer(BuildOptions());

        var result = replacer.GetTargetPath(@"C:\Music\song.mp3");

        result.ShouldBe("C:/Music/song.mp3");
    }

    [Fact]
    public void GetTargetPath_MatchingPrefix_ReplacesPrefixAndNormalizesSlashes()
    {
        var options = BuildOptions(new ReplaceOptions
        {
            ReplacePaths = [new PathReplacement { From = @"C:\Music", To = "/mnt/music" }]
        });
        var replacer = new FilePathReplacer(options);

        var result = replacer.GetTargetPath(@"C:\Music\Artist\song.mp3");

        result.ShouldBe("/mnt/music/Artist/song.mp3");
    }

    [Fact]
    public void GetTargetPath_PrefixMatchIsCaseInsensitive()
    {
        var options = BuildOptions(new ReplaceOptions
        {
            ReplacePaths = [new PathReplacement { From = @"c:\music", To = "/mnt/music" }]
        });
        var replacer = new FilePathReplacer(options);

        var result = replacer.GetTargetPath(@"C:\Music\song.mp3");

        result.ShouldBe("/mnt/music/song.mp3");
    }

    [Fact]
    public void GetTargetPath_FromFragmentRepeatedLaterInPath_ReplacesOnlyThePrefix()
    {
        var options = BuildOptions(new ReplaceOptions
        {
            ReplacePaths = [new PathReplacement { From = @"C:\Music", To = "/mnt/music" }]
        });
        var replacer = new FilePathReplacer(options);

        var result = replacer.GetTargetPath(@"C:\Music\Backup\C:\Music\song.mp3");

        result.ShouldBe("/mnt/music/Backup/C:/Music/song.mp3");
    }

    [Fact]
    public void GetTargetPath_MultipleReplacements_UsesFirstMatchingPrefix()
    {
        var options = BuildOptions(new ReplaceOptions
        {
            ReplacePaths =
            [
                new PathReplacement { From = @"D:\Other", To = "/mnt/other" },
                new PathReplacement { From = @"C:\Music", To = "/mnt/music" }
            ]
        });
        var replacer = new FilePathReplacer(options);

        var result = replacer.GetTargetPath(@"C:\Music\song.mp3");

        result.ShouldBe("/mnt/music/song.mp3");
    }

    [Fact]
    public void GetTargetPath_NoMatchingPrefix_OnlyNormalizesSlashes()
    {
        var options = BuildOptions(new ReplaceOptions
        {
            ReplacePaths = [new PathReplacement { From = @"D:\Other", To = "/mnt/other" }]
        });
        var replacer = new FilePathReplacer(options);

        var result = replacer.GetTargetPath(@"C:\Music\song.mp3");

        result.ShouldBe("C:/Music/song.mp3");
    }
}
