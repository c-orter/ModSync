using System.Text;
using ModSync.Core;
using ModSync.Core.Util;
using Snapper.Attributes;
using Snapper.Nunit;

namespace ModSync.Tests;

[TestFixture]
public class SyncerTests
{
    private TestUtils.TestServer _server;
    private TestUtils.TestFrontend _frontend;
    private TestUtils.TestLogger _logger;

    [SetUp]
    public void Setup()
    {
        var tempDirectory = TestUtils.GetTemporaryDirectory();

        Directory.CreateDirectory(Path.Combine(tempDirectory, @"BepInEx\plugins"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, @"ModSync_Data"));

        _server = new TestUtils.TestServer(
            Version.Parse("99.99.99"),
            [new SyncPath(@"BepInEx\plugins")],
            ["BepInEx/plugins/SAIN.dll"],
            new Dictionary<string, Dictionary<string, ModFile>>()
            {
                [@"BepInEx\plugins"] = new Dictionary<string, ModFile>() { [@"BepInEx\plugins\SAIN.dll"] = new ModFile("1234567") },
            },
            Encoding.UTF8.GetBytes("1234567")
        );

        _frontend = new TestUtils.TestFrontend(sptDirectory: tempDirectory);
        _logger = new TestUtils.TestLogger();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _server.Shutdown();

        Directory.Delete(_frontend.sptDirectory, true);
    }

    [Test, Timeout(1000)]
    public async Task TestNominalSyncer()
    {
        var syncer = new Syncer(_frontend, Version.Parse("99.99.99"), _server.url, _logger);

        await syncer.Run();

        while (!_frontend.Finished)
            await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(_frontend.ApplyUpdateCallCount, Is.EqualTo(1));
            Assert.That(_frontend.ShouldUpdatePromptCallCount, Is.EqualTo(1));
            Assert.That(_frontend.ShouldUpdatePromptParams.syncDiffs, Is.Not.Null);
            Assert.That(_frontend.ShouldUpdatePromptParams.syncDiffs, Matches.Snapshot());
        });
    }

    [Test, Timeout(1000)]
    public async Task TestFailDownloadOnce()
    {
        _server.failFileDataCount = 1;

        var syncer = new Syncer(_frontend, Version.Parse("99.99.99"), _server.url, _logger);

        await syncer.Run();

        while (!_frontend.Finished)
            await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(_frontend.ApplyUpdateCallCount, Is.EqualTo(1));
            Assert.That(_frontend.ShouldUpdatePromptCallCount, Is.EqualTo(1));
            Assert.That(_frontend.ShouldUpdatePromptParams.syncDiffs, Is.Not.Null);
            Assert.That(_frontend.ShouldUpdatePromptParams.syncDiffs, Matches.Snapshot());
        });
    }

    [Test, Timeout(5000)]
    public async Task TestFailDownloadFiveTimes()
    {
        _server.failFileDataCount = 6;

        var syncer = new Syncer(_frontend, Version.Parse("99.99.99"), _server.url, _logger);

        await syncer.Run();

        while (_frontend.DownloadErrorAlertCallCount == 0)
            await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(_frontend.DownloadErrorAlertCallCount, Is.EqualTo(1));
            Assert.That(_frontend.DownloadErrorAlertParams, Matches.Snapshot());
        });
    }

    [Test, Timeout(5000)]
    public async Task TestSkipNormalUpdate()
    {
        _frontend.acceptUpdate = false;

        var syncer = new Syncer(_frontend, Version.Parse("99.99.99"), _server.url, _logger);

        await syncer.Run();

        while (!_frontend.Finished)
            await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(_frontend.ShouldUpdatePromptCallCount, Is.EqualTo(1));
            Assert.That(_frontend.ApplyUpdateCallCount, Is.EqualTo(0));
        });
    }

    [Test, Timeout(5000)]
    public async Task TestSkipEnforcedUpdate()
    {
        _frontend.acceptUpdate = false;
        _server.syncPaths[0].enforced = true;

        var syncer = new Syncer(_frontend, Version.Parse("99.99.99"), _server.url, _logger);

        await syncer.Run();

        while (!_frontend.Finished)
            await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(_frontend.ShouldUpdatePromptCallCount, Is.EqualTo(1));
            Assert.That(_frontend.ApplyUpdateCallCount, Is.EqualTo(1));
        });
    }

    [Test, Timeout(5000)]
    public async Task TestCreatedDirectories()
    {
        _server.modFiles[_server.syncPaths[0].path][@"BepInEx\plugins\SAIN.dll"].directory = true;

        var syncer = new Syncer(_frontend, Version.Parse("99.99.99"), _server.url, _logger);

        await syncer.Run();

        while (!_frontend.Finished)
            await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(_frontend.ShouldUpdatePromptCallCount, Is.EqualTo(1));
            Assert.That(_frontend.ApplyUpdateCallCount, Is.EqualTo(1));
        });
    }

    [Test, Timeout(5000)]
    public async Task TestCancelDownload()
    {
        _frontend.cancelDownload = true;

        var syncer = new Syncer(_frontend, Version.Parse("99.99.99"), _server.url, _logger);

        await syncer.Run();

        while (!_frontend.Finished)
            await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(_frontend.ShouldUpdatePromptCallCount, Is.EqualTo(1));
            Assert.That(_frontend.ApplyUpdateCallCount, Is.EqualTo(0));
        });
    }

    [Test, Timeout(5000)]
    public async Task TestWarningOnFailedPreviousUpdate()
    {
        Directory.CreateDirectory(Path.Combine(_frontend.sptDirectory, "ModSync_Data", "PendingUpdates"));   

        var syncer = new Syncer(_frontend, Version.Parse("99.99.99"), _server.url, _logger);

        await syncer.Run();

        while (!_frontend.Finished)
            await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(
                _logger.Logs,
                Does.Contain(
                    (
                        "WARNING",
                        "ModSync found previous update. Updater may have failed, check the 'ModSync_Data/Updater.log' for details. Attempting to continue."
                    )
                )
            );

            Assert.That(_frontend.ApplyUpdateCallCount, Is.EqualTo(1));
        });
    }
}
