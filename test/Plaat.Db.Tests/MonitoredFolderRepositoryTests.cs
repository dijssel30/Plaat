using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class MonitoredFolderRepositoryTests
{
    [Fact]
    public async Task AddFolder_NewPath_AddsIt()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateMonitoredFolderRepository();

        var folder = await repo.AddFolder("/music/library", CancellationToken.None);

        folder.Path.ShouldBe("/music/library");
        folder.LastScannedAt.ShouldBeNull();
    }

    [Fact]
    public async Task AddFolder_DuplicatePath_Throws()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateMonitoredFolderRepository();

        await repo.AddFolder("/music/library", CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            repo.AddFolder("/music/library", CancellationToken.None));

        exception.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetAll_ReturnsAddedFolders()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateMonitoredFolderRepository();

        await repo.AddFolder("/music/b", CancellationToken.None);
        await repo.AddFolder("/music/a", CancellationToken.None);

        var folders = await repo.GetAll(CancellationToken.None);

        folders.Select(f => f.Path).ShouldBe(["/music/a", "/music/b"]);
    }

    [Fact]
    public async Task RemoveFolder_RemovesIt()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateMonitoredFolderRepository();

        var folder = await repo.AddFolder("/music/library", CancellationToken.None);

        await repo.RemoveFolder(folder.Id, CancellationToken.None);

        var folders = await repo.GetAll(CancellationToken.None);
        folders.ShouldBeEmpty();
    }
}
