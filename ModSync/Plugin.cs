using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT.UI;
using ModSync.UI;
using ModSync.Utility;
using SPT.Common.Utils;
using UnityEngine;

namespace ModSync;

using SyncPathFileList = Dictionary<string, List<string>>;
using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

[BepInPlugin("corter.modsync", "Corter ModSync", "0.10.1")]
public class Plugin : BaseUnityPlugin
{
    private static readonly string MODSYNC_DIR = Path.Combine(Directory.GetCurrentDirectory(), "ModSync_Data");
    private static readonly string PENDING_UPDATES_DIR = Path.Combine(MODSYNC_DIR, "PendingUpdates");
    private static readonly string PREVIOUS_SYNC_PATH = Path.Combine(MODSYNC_DIR, "PreviousSync.json");
    private static readonly string LOCAL_HASHES_PATH = Path.Combine(MODSYNC_DIR, "LocalHashes.json");
    private static readonly string REMOVED_FILES_PATH = Path.Combine(MODSYNC_DIR, "RemovedFiles.json");
    private static readonly string LOCAL_EXCLUSIONS_PATH = Path.Combine(MODSYNC_DIR, "Exclusions.json");
    private static readonly string UPDATER_PATH = Path.Combine(Directory.GetCurrentDirectory(), "ModSync.Updater.exe");

    private static readonly List<string> DEDICATED_DEFAULT_EXCLUSIONS =
    [
        "BepInEx/plugins/AmandsGraphics.dll",
        "BepInEx/plugins/AmandsSense.dll",
        "BepInEx/plugins/Sense",
        "BepInEx/plugins/MoreCheckmarks",
        "BepInEx/plugins/kmyuhkyuk-EFTApi",
        "BepInEx/plugins/DynamicMaps",
        "BepInEx/plugins/LootValue",
        "BepInEx/plugins/CactusPie.RamCleanerInterval.dll",
        "BepInEx/plugins/TYR_DeClutterer.dll",
    ];

    // Configuration
    private Dictionary<string, ConfigEntry<bool>> configSyncPathToggles;
    private ConfigEntry<bool> configDeleteRemovedFiles;

    private List<SyncPath> syncPaths = [];
    private SyncPathModFiles remoteModFiles = [];
    private SyncPathModFiles previousSync = [];
    private List<string> localExclusions = [];

    private SyncPathFileList addedFiles = [];
    private SyncPathFileList updatedFiles = [];
    private SyncPathFileList removedFiles = [];
    private SyncPathFileList createdDirectories = [];

    private List<Task> downloadTasks = [];

    private bool pluginFinished;
    private int downloadCount;
    private int totalDownloadCount;

    private Server server;
    private CancellationTokenSource cts = new();

    public static new readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("ModSync");

    private int UpdateCount =>
        EnabledSyncPaths
            .Select(syncPath =>
                addedFiles[syncPath.path].Count
                + updatedFiles[syncPath.path].Count
                + (configDeleteRemovedFiles.Value || syncPath.enforced ? removedFiles[syncPath.path].Count : 0)
                + createdDirectories[syncPath.path].Count
            )
            .Sum();
    private static bool IsDedicated => Chainloader.PluginInfos.ContainsKey("com.fika.dedicated");
    private List<SyncPath> EnabledSyncPaths => syncPaths.Where(syncPath => configSyncPathToggles[syncPath.path].Value || syncPath.enforced).ToList();

    private bool SilentMode =>
        IsDedicated
        || EnabledSyncPaths.All(syncPath =>
            syncPath.silent
            || (
                addedFiles[syncPath.path].Count == 0
                && updatedFiles[syncPath.path].Count == 0
                && (!(configDeleteRemovedFiles.Value || syncPath.enforced) || removedFiles[syncPath.path].Count == 0)
                && createdDirectories[syncPath.path].Count == 0
            )
        );

    private bool NoRestartMode =>
        EnabledSyncPaths.All(syncPath =>
            !syncPath.restartRequired
            || (
                addedFiles[syncPath.path].Count == 0
                && updatedFiles[syncPath.path].Count == 0
                && (!(configDeleteRemovedFiles.Value || syncPath.enforced) || removedFiles[syncPath.path].Count == 0)
                && createdDirectories[syncPath.path].Count == 0
            )
        );

    private void AnalyzeModFiles(SyncPathModFiles localModFiles)
    {
        Sync.CompareModFiles(
            EnabledSyncPaths,
            localModFiles,
            remoteModFiles,
            previousSync,
            out addedFiles,
            out updatedFiles,
            out removedFiles,
            out createdDirectories
        );

        Logger.LogInfo($"Found {UpdateCount} files to download.");
        Logger.LogInfo($"- {addedFiles.SelectMany(path => path.Value).Count()} added");
        Logger.LogInfo($"- {updatedFiles.SelectMany(path => path.Value).Count()} updated");
        if (removedFiles.Count > 0)
            Logger.LogInfo($"- {removedFiles.SelectMany(path => path.Value).Count()} removed");

        if (UpdateCount > 0)
        {
            if (SilentMode)
                Task.Run(() => SyncMods(addedFiles, updatedFiles, createdDirectories));
            else
                updateWindow.Show();
        }
        else
            WriteModSyncData();
    }

    private void SkipUpdatingMods()
    {
        var enforcedAddedFiles = EnabledSyncPaths
            .ToDictionary(syncPath => syncPath.path, syncPath => syncPath.enforced ? addedFiles[syncPath.path] : [], StringComparer.OrdinalIgnoreCase);

        var enforcedUpdatedFiles = EnabledSyncPaths
            .ToDictionary(syncPath => syncPath.path, syncPath => syncPath.enforced ? updatedFiles[syncPath.path] : [], StringComparer.OrdinalIgnoreCase);

        var enforcedCreatedDirectories = EnabledSyncPaths
            .ToDictionary(syncPath => syncPath.path, syncPath => syncPath.enforced ? createdDirectories[syncPath.path] : [], StringComparer.OrdinalIgnoreCase);

        if (enforcedAddedFiles.Values.Any(files => files.Any())
            || enforcedUpdatedFiles.Values.Any(files => files.Any())
            || enforcedCreatedDirectories.Values.Any(files => files.Any()))
        {
            Task.Run(() => SyncMods(enforcedAddedFiles, enforcedUpdatedFiles, enforcedCreatedDirectories));
        }
        else
        {
            pluginFinished = true;
            updateWindow.Hide();
        }
    }

    private async Task SyncMods(SyncPathFileList filesToAdd, SyncPathFileList filesToUpdate, SyncPathFileList directoriesToCreate)
    {
        updateWindow.Hide();

        if (!Directory.Exists(PENDING_UPDATES_DIR))
            Directory.CreateDirectory(PENDING_UPDATES_DIR);

        foreach (var syncPath in EnabledSyncPaths)
        {
            foreach (var dir in directoriesToCreate[syncPath.path])
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch (Exception e)
                {
                    Logger.LogError("Failed to create empty directories: " + e);
                }
            }
        }

        downloadCount = 0;
        totalDownloadCount = 0;

        var limiter = new SemaphoreSlim(8);
        var filesToDownload = EnabledSyncPaths
            .Select((syncPath) => new KeyValuePair<string, List<string>>(syncPath.path, [.. filesToAdd[syncPath.path], .. filesToUpdate[syncPath.path]]))
            .ToDictionary((kvp) => kvp.Key, (kvp) => kvp.Value);

        Logger.LogInfo($"Starting download of {UpdateCount} files.");
        downloadTasks = EnabledSyncPaths
            .SelectMany(syncPath =>
                filesToDownload.TryGetValue(syncPath.path, out var pathFilesToDownload)
                    ? pathFilesToDownload.Select(file =>
                        server.DownloadFile(file, syncPath.restartRequired ? PENDING_UPDATES_DIR : Directory.GetCurrentDirectory(), limiter, cts.Token)
                    )
                    : []
            )
            .ToList();

        totalDownloadCount = downloadTasks.Count;

        if (!IsDedicated)
            progressWindow.Show();

        while (downloadTasks.Count > 0 && !cts.IsCancellationRequested)
        {
            var task = await Task.WhenAny(downloadTasks);

            try
            {
                await task;
            }
            catch (Exception e)
            {
                if (e is TaskCanceledException && cts.IsCancellationRequested)
                    continue;

                cts.Cancel();
                progressWindow.Hide();
                if (!IsDedicated)
                    downloadErrorWindow.Show();
            }

            downloadTasks.Remove(task);
            downloadCount++;
        }

        downloadTasks.Clear();
        progressWindow.Hide();

        Logger.LogInfo("Download of files finished.");

        if (!cts.IsCancellationRequested)
        {
            WriteModSyncData();

            if (NoRestartMode)
            {
                Directory.Delete(PENDING_UPDATES_DIR, true);
                pluginFinished = true;
            }
            else if (!IsDedicated)
                restartWindow.Show();
            else
                StartUpdaterProcess();
        }
    }

    private async Task CancelUpdatingMods()
    {
        progressWindow.Hide();
        cts.Cancel();

        await Task.WhenAll(downloadTasks);

        Directory.Delete(PENDING_UPDATES_DIR, true);
        pluginFinished = true;
    }

    private void WriteModSyncData()
    {
        VFS.WriteTextFile(PREVIOUS_SYNC_PATH, Json.Serialize(remoteModFiles));
        if (EnabledSyncPaths.Any(syncPath => (configDeleteRemovedFiles.Value || syncPath.enforced) && removedFiles[syncPath.path].Count != 0))
            VFS.WriteTextFile(REMOVED_FILES_PATH, Json.Serialize(removedFiles.SelectMany(kvp => kvp.Value).ToList()));
    }

    private void StartUpdaterProcess()
    {
        List<string> options = [];

        if (IsDedicated)
            options.Add("--silent");

        Logger.LogInfo($"Starting Updater with arguments {string.Join(" ", options)} {Process.GetCurrentProcess().Id}");
        var updaterStartInfo = new ProcessStartInfo
        {
            FileName = UPDATER_PATH,
            Arguments = string.Join(" ", options) + " " + Process.GetCurrentProcess().Id,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var updaterProcess = new Process { StartInfo = updaterStartInfo };

        updaterProcess.Start();
        Application.Quit();
    }

    private IEnumerator StartPlugin()
    {
        cts = new CancellationTokenSource();
        if (Directory.Exists(PENDING_UPDATES_DIR) || File.Exists(REMOVED_FILES_PATH))
            Logger.LogWarning(
                "ModSync found previous update. Updater may have failed, check the 'ModSync_Data/Updater.log' for details. Attempting to continue."
            );

        Logger.LogDebug("Fetching server version");
        var versionTask = server.GetModSyncVersion();
        yield return new WaitUntil(() => versionTask.IsCompleted);
        try
        {
            var version = versionTask.Result;

            Logger.LogInfo($"ModSync found server version: {version}");
            if (version != Info.Metadata.Version.ToString())
                Logger.LogWarning($"ModSync server version does not match plugin version. Found server version: {version}. Plugin may not work as expected!");
        }
        catch (Exception e)
        {
            Logger.LogError(e);
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to error requesting server version. Please ensure the server mod is properly installed and try again."
            );
            yield break;
        }

        Logger.LogDebug("Fetching sync paths");
        var syncPathTask = server.GetModSyncPaths();
        yield return new WaitUntil(() => syncPathTask.IsCompleted);
        try
        {
            syncPaths = syncPathTask.Result;
        }
        catch (Exception e)
        {
            Logger.LogError(e);
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to error requesting sync paths. Please ensure the server mod is properly installed and try again."
            );
            yield break;
        }

        Logger.LogDebug("Processing sync paths");
        foreach (var syncPath in syncPaths)
        {
            if (Path.IsPathRooted(syncPath.path))
            {
                Chainloader.DependencyErrors.Add(
                    $"Could not load {Info.Metadata.Name} due to invalid sync path. Paths must be relative to SPT server root! Invalid path '{syncPath}'"
                );
                yield break;
            }

            if (!Path.GetFullPath(syncPath.path).StartsWith(Directory.GetCurrentDirectory()))
            {
                Chainloader.DependencyErrors.Add(
                    $"Could not load {Info.Metadata.Name} due to invalid sync path. Paths must be within SPT server root! Invalid path '{syncPath}'"
                );
                yield break;
            }
        }

        Logger.LogDebug("Running migrator");
        new Migrator(Directory.GetCurrentDirectory()).TryMigrate(Info.Metadata.Version, syncPaths);

        Logger.LogDebug("Loading syncPath configs");
        configSyncPathToggles = syncPaths
            .Select(syncPath => new KeyValuePair<string, ConfigEntry<bool>>(
                syncPath.path,
                Config.Bind(
                    "Synced Paths",
                    syncPath.path.Replace("\\", "/"),
                    syncPath.enabled,
                    new ConfigDescription(
                        $"Should the mod attempt to sync files from {syncPath}",
                        null,
                        new ConfigurationManagerAttributes { ReadOnly = syncPath.enforced }
                    )
                )
            ))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Logger.LogDebug("Loading previous sync data");
        try
        {
            previousSync = VFS.Exists(PREVIOUS_SYNC_PATH) ? Json.Deserialize<SyncPathModFiles>(VFS.ReadTextFile(PREVIOUS_SYNC_PATH)) : [];
        }
        catch (Exception e)
        {
            Logger.LogError(e);
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to malformed previous sync data. Please check ModSync_Data/PreviousSync.json for errors or delete it, and try again."
            );
            yield break;
        }

        Logger.LogDebug("Loading local exclusions");
        if (IsDedicated && !VFS.Exists(LOCAL_EXCLUSIONS_PATH))
        {
            try
            {
                VFS.WriteTextFile(LOCAL_EXCLUSIONS_PATH, Json.Serialize(DEDICATED_DEFAULT_EXCLUSIONS));
            }
            catch (Exception e)
            {
                Logger.LogError(e);
                Chainloader.DependencyErrors.Add(
                    $"Could not load {Info.Metadata.Name} due to error writing local exclusions file for dedicated client. Please check BepInEx/LogOutput.log for more information."
                );
                yield break;
            }
        }

        try
        {
            localExclusions = VFS.Exists(LOCAL_EXCLUSIONS_PATH) ? Json.Deserialize<List<string>>(VFS.ReadTextFile(LOCAL_EXCLUSIONS_PATH)) : [];
        }
        catch (Exception e)
        {
            Logger.LogError(e);
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to malformed local exclusion data. Please check ModSync_Data/Exclusions.json for errors or delete it, and try again."
            );
            yield break;
        }

        Logger.LogDebug("Fetching exclusions");

        List<string> exclusions;
        var exclusionsTask = server.GetModSyncExclusions();
        yield return new WaitUntil(() => exclusionsTask.IsCompleted);
        try
        {
            exclusions = exclusionsTask.Result;
        }
        catch (Exception e)
        {
            Logger.LogError(e);
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to error requesting exclusions. Please ensure the server mod is properly installed and try again."
            );
            yield break;
        }

        yield return new WaitUntil(() => Singleton<CommonUI>.Instantiated);

        Logger.LogDebug("Hashing local files");
        var localModFilesTask = Sync.HashLocalFiles(
            Directory.GetCurrentDirectory(),
            EnabledSyncPaths,
            exclusions.Select(Glob.Create).ToList(),
            localExclusions.Select(Glob.Create).ToList()
        );

        yield return new WaitUntil(() => localModFilesTask.IsCompleted);
        var localModFiles = localModFilesTask.Result;

        VFS.WriteTextFile(LOCAL_HASHES_PATH, Json.Serialize(localModFiles));

        Logger.LogDebug("Fetching remote file hashes");
        var remoteHashesTask = server.GetRemoteModFileHashes(EnabledSyncPaths);
        yield return new WaitUntil(() => remoteHashesTask.IsCompleted);
        try
        {
            var remoteHashes = remoteHashesTask.Result;

            var localExclusionsForRemote = localExclusions.Select(Glob.CreateNoEnd).ToList();
            remoteModFiles = EnabledSyncPaths
                .Select(
                    (syncPath) =>
                    {
                        var remotePathHashes = remoteHashes[syncPath.path];

                        if (!syncPath.enforced)
                            remotePathHashes = remotePathHashes
                                .Where((kvp) => !Sync.IsExcluded(localExclusionsForRemote, kvp.Key))
                                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

                        return new KeyValuePair<string, Dictionary<string, ModFile>>(syncPath.path, remotePathHashes);
                    }
                )
                .ToDictionary((kvp) => kvp.Key, (kvp) => kvp.Value, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            Logger.LogError(e);
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to error requesting server mod list. Please check the server log and try again."
            );
        }

        Logger.LogDebug("Comparing file hashes");
        try
        {
            AnalyzeModFiles(localModFiles);
        }
        catch (Exception e)
        {
            Logger.LogError(e);
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to error hashing local mods. Please ensure none of the files are open and try again."
            );
        }
    }

    private readonly UpdateWindow updateWindow = new("Installed mods do not match server", "Would you like to update?");
    private readonly ProgressWindow progressWindow = new("Downloading Updates...", "Your game will need to be restarted\nafter update completes.");
    private readonly AlertWindow restartWindow = new(new Vector2(480f, 200f), "Update Complete.", "Please restart your game to continue.");
    private readonly AlertWindow downloadErrorWindow =
        new(
            new Vector2(640f, 240f),
            "Download failed!",
            "There was an error updating mod files.\nPlease check BepInEx/LogOutput.log for more information.",
            "QUIT"
        );

    private void Awake()
    {
        ConsoleScreen.Processor.RegisterCommand(
            "modsync",
            () =>
            {
                ConsoleScreen.Log("Checking for updates.");
                StartCoroutine(StartPlugin());
            }
        );

        server = new Server(Info.Metadata.Version);

        configDeleteRemovedFiles = Config.Bind("General", "Delete Removed Files", true, "Should the mod delete files that have been removed from the server?");
    }

    private List<string> _optional;
    private List<string> optional =>
        _optional ??= EnabledSyncPaths
            .Where(syncPath => !syncPath.enforced)
            .SelectMany(syncPath =>
                addedFiles[syncPath.path]
                    .Select(file => $"ADDED {file}")
                    .Concat(updatedFiles[syncPath.path].Select(file => $"UPDATED {file}"))
                    .Concat(configDeleteRemovedFiles.Value || syncPath.enforced ? removedFiles[syncPath.path].Select(file => $"REMOVED {file}") : [])
                    .Concat(createdDirectories[syncPath.path].Select(file => $@"CREATED {file}\"))
            )
            .ToList();

    private List<string> _required;
    private List<string> required =>
        _required ??= EnabledSyncPaths
            .Where(syncPath => syncPath.enforced)
            .SelectMany(syncPath =>
                addedFiles[syncPath.path]
                    .Select(file => $"ADDED {file}")
                    .Concat(updatedFiles[syncPath.path].Select(file => $"UPDATED {file}"))
                    .Concat(configDeleteRemovedFiles.Value ? removedFiles[syncPath.path].Select(file => $"REMOVED {file}") : [])
                    .Concat(createdDirectories[syncPath.path].Select(file => $@"CREATED {file}\"))
            )
            .ToList();

    private List<string> _noRestart;
    private List<string> noRestart =>
        _noRestart ??= EnabledSyncPaths
            .Where(syncPath => !syncPath.restartRequired)
            .SelectMany(syncPath =>
                addedFiles[syncPath.path]
                    .Concat(updatedFiles[syncPath.path])
                    .Concat((configDeleteRemovedFiles.Value || syncPath.enforced) ? removedFiles[syncPath.path] : [])
                    .Concat(createdDirectories[syncPath.path])
            )
            .ToList();

    private void OnGUI()
    {
        if (!Singleton<CommonUI>.Instantiated)
            return;

        if (restartWindow.Active)
            restartWindow.Draw(StartUpdaterProcess);

        if (progressWindow.Active)
            progressWindow.Draw(downloadCount, totalDownloadCount, required.Count != 0 || noRestart.Count != 0 ? null : () => Task.Run(CancelUpdatingMods));

        if (updateWindow.Active)
        {
            updateWindow.Draw(
                (optional.Count != 0 ? string.Join("\n", optional) : "")
                    + (optional.Count != 0 && required.Count != 0 ? "\n\n" : "")
                    + (required.Count != 0 ? "[Enforced]\n" + string.Join("\n", required) : ""),
                () => Task.Run(() => SyncMods(addedFiles, updatedFiles, createdDirectories)),
                required.Count != 0 && optional.Count == 0 ? null : SkipUpdatingMods
            );
        }

        if (downloadErrorWindow.Active)
            downloadErrorWindow.Draw(Application.Quit);
    }

    public void Start()
    {
        StartCoroutine(StartPlugin());
    }

    public void Update()
    {
        if (updateWindow.Active || progressWindow.Active || restartWindow.Active || downloadErrorWindow.Active)
        {
            if (Singleton<LoginUI>.Instantiated && Singleton<LoginUI>.Instance.gameObject.activeSelf)
                Singleton<LoginUI>.Instance.gameObject.SetActive(false);

            if (Singleton<PreloaderUI>.Instantiated && Singleton<PreloaderUI>.Instance.gameObject.activeSelf)
                Singleton<PreloaderUI>.Instance.gameObject.SetActive(false);

            if (Singleton<CommonUI>.Instantiated && Singleton<CommonUI>.Instance.gameObject.activeSelf)
                Singleton<CommonUI>.Instance.gameObject.SetActive(false);
        }
        else if (pluginFinished)
        {
            pluginFinished = false;
            if (Singleton<LoginUI>.Instantiated && !Singleton<LoginUI>.Instance.gameObject.activeSelf)
                Singleton<LoginUI>.Instance.gameObject.SetActive(true);

            if (Singleton<PreloaderUI>.Instantiated && !Singleton<PreloaderUI>.Instance.gameObject.activeSelf)
                Singleton<PreloaderUI>.Instance.gameObject.SetActive(true);

            if (Singleton<CommonUI>.Instantiated && !Singleton<CommonUI>.Instance.gameObject.activeSelf)
                Singleton<CommonUI>.Instance.gameObject.SetActive(true);
        }
    }
}
