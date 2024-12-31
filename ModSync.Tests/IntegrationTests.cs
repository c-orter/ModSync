using System.Text.RegularExpressions;
using ModSync.Core;
using ModSync.Core.Util;
using Newtonsoft.Json;

namespace ModSync.Tests;

using SyncPathFileList = Dictionary<string, List<string>>;
using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

[TestFixture]
public class IntegrationTests
{
    private readonly ILogger logger = new TestLogger();
    private readonly Comparator comparator;

    IntegrationTests()
    {
        comparator = new Comparator(logger);
    }

    private (SyncPathModFiles, List<string>) RunPlugin(
        string testPath,
        List<SyncPath> syncPaths,
        bool configDeleteRemovedFiles,
        out Dictionary<SyncPath, SyncDiff> syncDiffs,
        ref List<string> downloadedFiles
    )
    {
        var remotePath = Path.Combine(testPath, "remote");
        if (!Directory.Exists(remotePath))
            Directory.CreateDirectory(remotePath);

        var localPath = Path.Combine(testPath, "local");
        if (!Directory.Exists(localPath))
            Directory.CreateDirectory(localPath);

        var previousSyncPath = Path.Combine(localPath, "ModSync_Data", "PreviousSync.json");
        var previousSync = File.Exists(previousSyncPath) ? JsonConvert.DeserializeObject<SyncPathModFiles>(File.ReadAllText(previousSyncPath)) : [];

        var localExclusionsPath = Path.Combine(localPath, "ModSync_Data", "Exclusions.json");
        var localExclusions = File.Exists(localExclusionsPath)
            ? JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(localExclusionsPath))!.Select(Glob.Create).ToList()
            : [];

        List<Regex> remoteExclusions = [Glob.Create("**/*.nosync"), Glob.Create("**/*.nosync.txt")];

        var remoteModFiles = comparator.HashLocalFiles(remotePath, syncPaths, remoteExclusions, localExclusions).Result;
        var localModFiles = comparator.HashLocalFiles(localPath, syncPaths, remoteExclusions, localExclusions).Result;

        comparator.CompareModFiles(Path.Combine(testPath, "local"), syncPaths, localModFiles, remoteModFiles, previousSync, out syncDiffs);

        downloadedFiles.AddRange(syncDiffs.SelectMany(kvp => kvp.Value.Added.Concat(kvp.Value.Updated)));

        return (remoteModFiles, configDeleteRemovedFiles ? syncDiffs.SelectMany(kvp => kvp.Value.Removed).ToList() : []);
    }

    [Test]
    public void TestInitialEmptySingleFile()
    {
        var testPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\IntegrationTests", "InitialEmptySingleFile"));

        List<string> downloadedFiles = [];

        var syncPath = new SyncPath("SAIN.dll");

        var (previousSync, filesToDelete) = RunPlugin(testPath, syncPaths: [syncPath], configDeleteRemovedFiles: true, out var syncDiffs, ref downloadedFiles);

        var syncDiff = syncDiffs[syncPath];

        Assert.Multiple(() =>
        {
            Assert.That(syncDiff.Added, Has.Count.EqualTo(1));
            Assert.That(syncDiff.Updated, Is.Empty);
            Assert.That(syncDiff.Removed, Is.Empty);

            Assert.That(downloadedFiles, Has.Count.EqualTo(1));
            Assert.That(downloadedFiles, Does.Contain("SAIN.dll"));

            Assert.That(filesToDelete, Is.Empty);

            Assert.That(previousSync["SAIN.dll"], Has.Count.EqualTo(1));
            Assert.That(previousSync.Keys, Does.Contain("SAIN.dll"));
        });
    }

    [Test]
    public void TestInitialEmptyManyFiles()
    {
        var testPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\IntegrationTests", "InitialEmptyManyFiles"));

        List<string> downloadedFiles = [];

        var syncPath = new SyncPath("plugins");

        var (previousSync, filesToDelete) = RunPlugin(testPath, syncPaths: [syncPath], configDeleteRemovedFiles: true, out var syncDiffs, ref downloadedFiles);

        var syncDiff = syncDiffs[syncPath];

        Assert.Multiple(() =>
        {
            Assert.That(syncDiff.Added, Has.Count.EqualTo(2));
            Assert.That(syncDiff.Updated, Is.Empty);
            Assert.That(syncDiff.Removed, Is.Empty);

            Assert.That(downloadedFiles, Has.Count.EqualTo(2));

            Assert.That(downloadedFiles, Is.EquivalentTo(new List<string> { @"plugins\SAIN.dll", @"plugins\Corter-ModSync.dll" }));

            Assert.That(filesToDelete, Is.Empty);

            Assert.That(previousSync["plugins"], Has.Count.EqualTo(2));
            Assert.That(previousSync["plugins"].Keys, Is.EquivalentTo(new List<string> { @"plugins\SAIN.dll", @"plugins\Corter-ModSync.dll" }));
        });
    }

    [Test]
    public void TestUpdateSingleFile()
    {
        var testPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\IntegrationTests", "UpdateSingleFile"));

        List<string> downloadedFiles = [];

        var syncPath = new SyncPath("SAIN.dll");

        var (previousSync, filesToDelete) = RunPlugin(testPath, syncPaths: [syncPath], configDeleteRemovedFiles: true, out var syncDiffs, ref downloadedFiles);

        var syncDiff = syncDiffs[syncPath];

        Assert.Multiple(() =>
        {
            Assert.That(syncDiff.Added, Is.Empty);
            Assert.That(syncDiff.Updated, Has.Count.EqualTo(1));
            Assert.That(syncDiff.Removed, Is.Empty);

            Assert.That(downloadedFiles, Has.Count.EqualTo(1));
            Assert.That(downloadedFiles, Does.Contain("SAIN.dll"));

            Assert.That(filesToDelete, Is.Empty);

            Assert.That(previousSync["SAIN.dll"], Has.Count.EqualTo(1));
            Assert.That(previousSync["SAIN.dll"].Keys, Does.Contain("SAIN.dll"));
        });
    }

    [Test]
    public void TestDoNotUpdateWhenLocalChanges()
    {
        var testPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\IntegrationTests", "DoNotUpdateWhenLocalChanges"));

        List<string> downloadedFiles = [];

        var syncPath = new SyncPath("SAIN.dll");

        var (previousSync, _) = RunPlugin(testPath, syncPaths: [syncPath], configDeleteRemovedFiles: true, out var syncDiffs, ref downloadedFiles);

        var syncDiff = syncDiffs[syncPath];

        Assert.Multiple(() =>
        {
            Assert.That(syncDiff.Added, Is.Empty);
            Assert.That(syncDiff.Updated, Is.Empty);
            Assert.That(syncDiff.Removed, Is.Empty);

            Assert.That(downloadedFiles, Is.Empty);
            Assert.That(previousSync["SAIN.dll"]["SAIN.dll"].hash, Is.EqualTo("00d1413dcaf30500b65fc68446b10646"));
        });
    }

    [Test]
    public void TestRemoveSingleFile()
    {
        var testPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\IntegrationTests", "RemoveSingleFile"));

        List<string> downloadedFiles = [];

        var syncPath = new SyncPath("SAIN.dll");

        var (_, filesToDelete) = RunPlugin(testPath, syncPaths: [syncPath], configDeleteRemovedFiles: true, out var syncDiffs, ref downloadedFiles);

        var syncDiff = syncDiffs[syncPath];

        Assert.Multiple(() =>
        {
            Assert.That(syncDiff.Added, Is.Empty);
            Assert.That(syncDiff.Updated, Is.Empty);
            Assert.That(syncDiff.Removed, Has.Count.EqualTo(1));

            Assert.That(downloadedFiles, Is.Empty);
            Assert.That(filesToDelete, Is.EquivalentTo(new List<string> { "SAIN.dll" }));
        });
    }

    [Test]
    public void TestMismatchedCases()
    {
        var testPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\IntegrationTests", "MismatchedCases"));

        List<string> downloadedFiles = [];

        var syncPath = new SyncPath("plugins");

        RunPlugin(testPath, syncPaths: [syncPath], configDeleteRemovedFiles: true, out var syncDiffs, ref downloadedFiles);

        var syncDiff = syncDiffs[syncPath];

        Assert.Multiple(() =>
        {
            Assert.That(syncDiff.Added, Is.Empty);
            Assert.That(syncDiff.Updated, Has.Count.EqualTo(1));
            Assert.That(syncDiff.Removed, Is.Empty);

            Assert.That(downloadedFiles, Has.Count.EqualTo(1));
            Assert.That(downloadedFiles[0], Is.EqualTo(@"plugins\sain.dll"));
        });
    }

    [Test]
    public void TestClientNoSync()
    {
        var testPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\IntegrationTests", "ClientNoSync"));

        List<string> downloadedFiles = [];

        var syncPath = new SyncPath("plugins");

        var (_, filesToDelete) = RunPlugin(testPath, syncPaths: [syncPath], configDeleteRemovedFiles: true, out var syncDiffs, ref downloadedFiles);

        var syncDiff = syncDiffs[syncPath];

        Assert.Multiple(() =>
        {
            Assert.That(syncDiff.Added, Is.Empty);
            Assert.That(syncDiff.Updated, Is.Empty);
            Assert.That(syncDiff.Removed, Is.Empty);

            Assert.That(downloadedFiles, Is.Empty);
            Assert.That(filesToDelete, Is.Empty);
        });
    }

    [Test]
    public void TestCreateEmptyDirectories()
    {
        var testPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\IntegrationTests", "CreateEmptyDirectories"));

        Directory.CreateDirectory(Path.Combine(testPath, @"remote\plugins\TestMod\SuperImportantEmptyFolder"));

        List<string> downloadedFiles = [];

        var syncPath = new SyncPath("plugins");

        RunPlugin(testPath, syncPaths: [syncPath], configDeleteRemovedFiles: true, out var syncDiffs, ref downloadedFiles);

        var syncDiff = syncDiffs[syncPath];

        Assert.Multiple(() =>
        {
            Assert.That(syncDiff.Created, Has.Count.EqualTo(1));
            Assert.That(syncDiff.Created[0], Is.EqualTo(@"plugins\TestMod\SuperImportantEmptyFolder"));
        });
    }

    [Test]
    public void TestIgnoreCreateDirectoryThatIsNotEmpty()
    {
        var testPath = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\IntegrationTests", "EmptyDirectoryOnServerButPopulatedOnClient")
        );

        Directory.CreateDirectory(Path.Combine(testPath, @"remote\plugins\EmptyDirectory"));

        List<string> downloadedFiles = [];

        var syncPath = new SyncPath("plugins");

        RunPlugin(testPath, syncPaths: [syncPath], configDeleteRemovedFiles: true, out var syncDiffs, ref downloadedFiles);

        var syncDiff = syncDiffs[syncPath];

        Assert.Multiple(() =>
        {
            Assert.That(syncDiff.Created, Has.Count.EqualTo(0));
            Assert.That(syncDiff.Created, Has.No.Member(@"plugins\EmptyDirectory"));
        });
    }

    [Test]
    public void TestEnforcedBypassesLocalExclusions()
    {
        var testPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\IntegrationTests", "EnforcedBypassesLocalExclusions"));

        List<string> downloadedFiles = [];

        var syncPath = new SyncPath("plugins", enforced: true);

        RunPlugin(testPath, syncPaths: [syncPath], configDeleteRemovedFiles: true, out var syncDiffs, ref downloadedFiles);

        var syncDiff = syncDiffs[syncPath];

        Assert.Multiple(() =>
        {
            Assert.That(syncDiff.Added, Is.EquivalentTo(new List<string> { @"plugins\SAIN\SAIN.dll", @"plugins\SAIN\config.txt" }));
            Assert.That(syncDiff.Updated, Is.EquivalentTo(new List<string> { @"plugins\SAIN\config.txt" }));
            Assert.That(syncDiff.Removed, Is.EquivalentTo(new List<string> { @"plugins\SAIN\ExtraFile.txt" }));
        });
    }

    [Test]
    public void TestEnforcedOnlySyncedWhenUpdated()
    {
        var testPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\IntegrationTests", "EnforcedOnlySyncedWhenUpdated"));

        List<string> downloadedFiles = [];

        var syncPath1 = new SyncPath("test1.txt", enforced: true);
        var syncPath2 = new SyncPath("test2.txt", enforced: true);

        RunPlugin(testPath, syncPaths: [syncPath1, syncPath2], configDeleteRemovedFiles: true, out var syncDiffs, ref downloadedFiles);

        var syncDiff1 = syncDiffs[syncPath1];
        var syncDiff2 = syncDiffs[syncPath2];

        Assert.Multiple(() =>
        {
            Assert.That(syncDiff1.Added, Is.Empty);
            Assert.That(syncDiff1.Updated, Is.Empty);
            Assert.That(syncDiff1.Removed, Is.Empty);

            Assert.That(syncDiff2.Added, Is.Empty);
            Assert.That(syncDiff2.Updated, Is.EquivalentTo(new List<string> { @"test2.txt" }));
            Assert.That(syncDiff2.Removed, Is.Empty);
        });
    }
}
