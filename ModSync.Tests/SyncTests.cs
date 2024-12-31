using System.Text;
using System.Text.RegularExpressions;
using ModSync.Core;
using ModSync.Core.Util;

namespace ModSync.Tests;

[TestFixture]
public class AddedFilesTests
{
    private readonly ILogger logger = new TestLogger();
    private readonly Comparator comparator;

    AddedFilesTests()
    {
        comparator = new Comparator(logger);
    }

    [Test]
    public void TestSingleAdded()
    {
        var localModFiles = new Dictionary<string, ModFile> { { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") } };

        var remoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var addedFiles = comparator.GetAddedFiles(new SyncPath(@"BepInEx\plugins"), localModFiles, remoteModFiles);

        Assert.That(addedFiles, Is.EquivalentTo(new List<string> { @"BepInEx\plugins\Corter-ModSync.dll" }));
    }

    [Test]
    public void TestNoneAdded()
    {
        var localModFiles = new Dictionary<string, ModFile> { { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") } };

        var remoteModFiles = new Dictionary<string, ModFile> { { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") } };

        var addedFiles = comparator.GetAddedFiles(new SyncPath(@"BepInEx\plugins"), localModFiles, remoteModFiles);

        Assert.That(addedFiles, Is.Empty);
    }
}

[TestFixture]
public class UpdatedFilesTests
{
    private readonly ILogger logger = new TestLogger();
    private readonly Comparator comparator;

    UpdatedFilesTests()
    {
        comparator = new Comparator(logger);
    }

    [Test]
    public void TestSingleAdded()
    {
        var localModFiles = new Dictionary<string, ModFile> { { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") } };

        var remoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var previousRemoteModFiles = new Dictionary<string, ModFile> { { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") } };

        var updatedFiles = comparator.GetUpdatedFiles(new SyncPath(@"BepInEx\plugins"), localModFiles, remoteModFiles, previousRemoteModFiles);

        Assert.That(updatedFiles, Is.Empty);
    }

    [Test]
    public void TestSingleUpdated()
    {
        var localModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var remoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("2345678") },
        };

        var previousRemoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var updatedFiles = comparator.GetUpdatedFiles(new SyncPath(@"BepInEx\plugins"), localModFiles, remoteModFiles, previousRemoteModFiles);

        Assert.That(updatedFiles, Is.EquivalentTo(new List<string> { @"BepInEx\plugins\Corter-ModSync.dll" }));
    }

    [Test]
    public void TestOnlyLocalUpdated()
    {
        var localModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("2345678") },
        };

        var remoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var previousRemoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var updatedFiles = comparator.GetUpdatedFiles(new SyncPath(@"BepInEx\plugins"), localModFiles, remoteModFiles, previousRemoteModFiles);

        Assert.That(updatedFiles, Is.Empty);
    }

    [Test]
    public void TestFilesExistButPreviousEmpty()
    {
        var localModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var remoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("2345678") },
            { @"BepInEx\plugins\New-Mod.dll", new ModFile("1234567") },
        };

        var previousRemoteModFiles = new Dictionary<string, ModFile>();

        var updatedFiles = comparator.GetUpdatedFiles(new SyncPath(@"BepInEx\plugins"), localModFiles, remoteModFiles, previousRemoteModFiles);

        Assert.Multiple(() =>
        {
            Assert.That(updatedFiles, Has.Count.EqualTo(1));
            Assert.That(updatedFiles[0], Is.EqualTo(@"BepInEx\plugins\Corter-ModSync.dll"));
        });
    }

    [Test]
    public void TestBothUpdated()
    {
        var localModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("2345678") },
        };

        var remoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("2345678") },
        };

        var previousRemoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var updatedFiles = comparator.GetUpdatedFiles(new SyncPath(@"BepInEx\plugins"), localModFiles, remoteModFiles, previousRemoteModFiles);

        Assert.That(updatedFiles, Is.Empty);
    }

    [Test]
    public void TestSingleUpdatedEnforced()
    {
        var localModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("2345678") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var remoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("2345678") },
        };

        var previousRemoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var updatedFiles = comparator.GetUpdatedFiles(new SyncPath(@"BepInEx\plugins", enforced: true), localModFiles, remoteModFiles, previousRemoteModFiles);

        Assert.Multiple(() =>
        {
            Assert.That(updatedFiles, Has.Count.EqualTo(2));
            Assert.That(updatedFiles, Does.Contain(@"BepInEx\plugins\Corter-ModSync.dll"));
            Assert.That(updatedFiles, Does.Contain(@"BepInEx\plugins\SAIN\SAIN.dll"));
        });
    }
}

[TestFixture]
public class RemovedFilesTests
{
    private readonly ILogger logger = new TestLogger();
    private readonly Comparator comparator;

    RemovedFilesTests()
    {
        comparator = new Comparator(logger);
    }

    [Test]
    public void TestSingleRemoved()
    {
        var localModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var remoteModFiles = new Dictionary<string, ModFile> { { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") } };

        var previousRemoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var removedFiles = comparator.GetRemovedFiles(new SyncPath(@"BepInEx\plugins"), localModFiles, remoteModFiles, previousRemoteModFiles);

        Assert.That(removedFiles, Is.EquivalentTo(new List<string> { @"BepInEx\plugins\Corter-ModSync.dll" }));
    }

    [Test]
    public void TestSingleRemovedEnforced()
    {
        var localModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\OtherPlugin\OtherPlugin.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567", true) },
        };

        var remoteModFiles = new Dictionary<string, ModFile> { { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") } };

        var previousRemoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\SAIN\SAIN.dll", new ModFile("1234567") },
            { @"BepInEx\plugins\Corter-ModSync.dll", new ModFile("1234567") },
        };

        var removedFiles = comparator.GetRemovedFiles(new SyncPath(@"BepInEx\plugins", enforced: true), localModFiles, remoteModFiles, previousRemoteModFiles);

        Assert.That(removedFiles, Is.EquivalentTo(new List<string> { @"BepInEx\plugins\Corter-ModSync.dll", @"BepInEx\plugins\OtherPlugin\OtherPlugin.dll" }));
    }
}

[TestFixture]
public class CreatedDirectoriesTests
{
    private readonly ILogger logger = new TestLogger();
    private readonly Comparator comparator;

    CreatedDirectoriesTests()
    {
        comparator = new Comparator(logger);
    }

    [Test]
    public void TestCreatedDirectories()
    {
        var localModFiles = new Dictionary<string, ModFile>();

        var remoteModFiles = new Dictionary<string, ModFile>
        {
            { @"BepInEx\plugins\ModThatDoesntErrorCheckFolders\SuperImportantEmptyFolder", new ModFile("1234567", directory: true) },
        };

        var createdDirectories = comparator.GetCreatedDirectories("", new SyncPath(@"BepInEx\plugins", enforced: true), localModFiles, remoteModFiles);

        Assert.That(createdDirectories, Is.EquivalentTo(new List<string> { @"BepInEx\plugins\ModThatDoesntErrorCheckFolders\SuperImportantEmptyFolder" }));
    }
}

[TestFixture]
public class HashLocalFilesTests
{
    private readonly ILogger logger = new TestLogger();
    private readonly Comparator comparator;

    HashLocalFilesTests()
    {
        comparator = new Comparator(logger);
    }

    private readonly List<Regex> exclusions =
    [
        Glob.CreateNoEnd("**/*.nosync"),
        Glob.CreateNoEnd("**/*.nosync.txt"),
        Glob.CreateNoEnd("plugins/file2.dll"),
        Glob.CreateNoEnd("plugins/file3.dll"),
        Glob.CreateNoEnd("plugins/ModName"),
        Glob.CreateNoEnd("plugins/OtherMod/subdir"),
    ];

    private readonly Dictionary<string, string> fileContents = new()
    {
        { @"plugins\file1.dll", "Test content" },
        { @"plugins\file2.dll", "Test content 2" },
        { @"plugins\file2.dll.nosync", "" },
        { @"plugins\file3.dll", "Test content 3" },
        { @"plugins\file3.dll.nosync.txt", "" },
        { @"plugins\ModName\mod_name.dll", "Test content 4" },
        { @"plugins\ModName\.nosync", "" },
        { @"plugins\OtherMod\other_mod.dll", "Test content 5" },
        { @"plugins\OtherMod\subdir\image.png", "Test Image" },
        { @"plugins\OtherMod\subdir\.nosync", "" },
    };

    private string testDirectory = string.Empty;

    [SetUp]
    public void Setup()
    {
        testDirectory = TestUtils.GetTemporaryDirectory();

        Directory.CreateDirectory(testDirectory);

        // Create test files
        foreach (var kvp in fileContents)
        {
            var filePath = Path.Combine(testDirectory, kvp.Key);
            var fileParent = Path.GetDirectoryName(filePath);

            if (fileParent != null && !Directory.Exists(fileParent))
                Directory.CreateDirectory(fileParent);

            File.WriteAllText(filePath, kvp.Value);
        }

        Console.WriteLine(testDirectory);
    }

    [TearDown]
    public void Cleanup()
    {
        Directory.Delete(testDirectory, true);
    }

    [Test]
    public void TestHashLocalFiles()
    {
        var expected = fileContents.Where(kvp => !comparator.IsExcluded(exclusions, kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var result = comparator.HashLocalFiles(testDirectory, [new SyncPath("plugins")], exclusions, []).Result;

        Assert.That(result, Is.Not.Null);

        foreach (var kvp in expected)
        {
            Assert.That(result["plugins"], Does.ContainKey(kvp.Key));
            Assert.That(result["plugins"][kvp.Key].hash, Is.EqualTo(ImoHash.HashFileObject(new MemoryStream(Encoding.ASCII.GetBytes(kvp.Value))).Result));
        }

        Assert.That(result["plugins"], Has.Count.EqualTo(2));
    }

    [Test]
    public void TestHashLocalFilesWithDirectoryThatDoesNotExist()
    {
        var result = comparator.HashLocalFiles(testDirectory, [new SyncPath("bad_directory")], exclusions, []).Result;
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result["bad_directory"], Is.Empty);
        });
    }

    [Test]
    public void TestHashLocalFilesWithSingleFile()
    {
        var syncPath = Path.Combine(testDirectory, @"plugins\file1.dll");

        var result = comparator.HashLocalFiles(testDirectory, [new SyncPath(syncPath)], exclusions, []).Result;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result[syncPath], Has.Count.EqualTo(1));
            Assert.That(result[syncPath], Does.ContainKey(@"plugins\file1.dll"));
            Assert.That(result[syncPath][@"plugins\file1.dll"].hash, Is.EqualTo("0ce304b7ff04260d67adfdee0af9dd3b"));
        });
    }

    [Test]
    public void TestHashLocalFilesWithSingleFileThatDoesNotExist()
    {
        var syncPath = Path.Combine(testDirectory, "does_not_exist.dll");
        var result = comparator.HashLocalFiles(testDirectory, [new SyncPath(syncPath)], exclusions, []).Result;
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result[syncPath], Is.Empty);
        });
    }

    [Test]
    public void TestHashLocalFilesEnforcedIgnoresLocalExclusions()
    {
        var expected = fileContents.Where(kvp => !comparator.IsExcluded(exclusions, kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var result = comparator.HashLocalFiles(testDirectory, [new SyncPath("plugins", enforced: true)], exclusions, [Glob.Create("plugins/file1.dll")]).Result;
        Assert.That(result["plugins"].Keys, Is.EquivalentTo(expected.Keys));
    }
}

[TestFixture]
public class CreateModFileTest
{
    private readonly ILogger logger = new TestLogger();
    private readonly Comparator comparator;

    CreateModFileTest()
    {
        comparator = new Comparator(logger);
    }

    private readonly Dictionary<string, string> fileContents = new()
    {
        { "file1.dll", "" },
        { "file2.dll", "" },
        { "file2.dll.nosync", "" },
        { "file3.dll", "Test content 3" },
        { "file3.dll.nosync.txt", "" },
        { @"ModName\mod_name.dll", "Test content 4" },
        { @"ModName\.nosync", "" },
        { @"OtherMod\other_mod.dll", "Test content 5" },
        { @"OtherMod\subdir\image.png", "Test Image" },
        { @"OtherMod\subdir\.nosync", "" },
    };

    private string testDirectory = string.Empty;
    private readonly SemaphoreSlim limiter = new(1024);

    [SetUp]
    public void Setup()
    {
        testDirectory = TestUtils.GetTemporaryDirectory();

        Directory.CreateDirectory(testDirectory);

        // Create test files
        foreach (var kvp in fileContents)
        {
            var filePath = Path.Combine(testDirectory, kvp.Key);
            var fileParent = Path.GetDirectoryName(filePath);

            if (!Directory.Exists(fileParent))
                Directory.CreateDirectory(fileParent!);

            File.WriteAllText(filePath, kvp.Value);
        }

        Console.WriteLine(testDirectory);
    }

    [TearDown]
    public void Cleanup()
    {
        Directory.Delete(testDirectory, true);
    }

    [Test]
    public void TestCreateModFile()
    {
        var modFile = comparator.CreateModFile(Path.Combine(testDirectory, "file1.dll")).Result;

        Assert.Multiple(() =>
        {
            Assert.That(modFile, Is.Not.Null);
            Assert.That(modFile.hash, Is.EqualTo("00d1413dcaf30500b65fc68446b10646"));
        });
    }

    [Test]
    public void TestCreateModFileWithContent()
    {
        var modFile = comparator.CreateModFile(Path.Combine(testDirectory, "file3.dll")).Result;

        Assert.Multiple(() =>
        {
            Assert.That(modFile, Is.Not.Null);
            Assert.That(modFile.hash, Is.EqualTo("0e51ecd1fbd55148997270d6634ff6db"));
        });
    }
}

[TestFixture]
public class IsExcludedTest
{
    private readonly ILogger logger = new TestLogger();
    private readonly Comparator comparator;

    IsExcludedTest()
    {
        comparator = new Comparator(logger);
    }

    private readonly Dictionary<string, string> fileContents = new()
    {
        { "file1.dll", "Test content" },
        { "file2.dll", "Test content 2" },
        { "file3.dll", "Test content 3" },
        { @"ModName\mod_name.dll", "Test content 4" },
        { @"ModName\.nosync", "" },
        { @"ModName\subdir\image.png", "Test Image 1" },
        { @"OtherMod\other_mod.dll", "Test content 5" },
        { @"OtherMod\subdir\image.png", "Test Image 2" },
        { @"OtherMod\subdir\.nosync", "" },
    };

    private readonly List<Regex> exclusions =
    [
        Glob.Create("**/*.nosync"),
        Glob.Create("**/*.nosync.txt"),
        Glob.Create("file2.dll"),
        Glob.Create("file3.dll"),
        Glob.Create(@"ModName"),
        Glob.Create(@"OtherMod\subdir"),
    ];

    private string testDirectory = string.Empty;

    [SetUp]
    public void Setup()
    {
        testDirectory = TestUtils.GetTemporaryDirectory();

        Directory.CreateDirectory(testDirectory);

        // Create test files
        foreach (var kvp in fileContents)
        {
            var filePath = Path.Combine(testDirectory, kvp.Key);
            var fileParent = Path.GetDirectoryName(filePath);

            if (!Directory.Exists(fileParent))
                Directory.CreateDirectory(fileParent!);

            File.WriteAllText(filePath, kvp.Value);
        }

        Console.WriteLine(testDirectory);
    }

    [TearDown]
    public void Cleanup()
    {
        Directory.Delete(testDirectory, true);
    }

    [Test]
    public void TestIsNotExcluded()
    {
        var result = comparator.IsExcluded(exclusions, "file1.dll");
        Assert.That(result, Is.False);
    }

    [Test]
    public void TestIsExcluded()
    {
        var result = comparator.IsExcluded(exclusions, "file2.dll");
        Assert.That(result, Is.True);
    }
}
