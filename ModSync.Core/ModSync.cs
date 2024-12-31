using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ModSync.Core.Util;
using Newtonsoft.Json;

namespace ModSync.Core;

using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

public class ModSync(ISyncFrontend frontend, Version version, string hostname, ILogger logger)
{
    private readonly Server server = new(version, hostname, logger);
    private readonly Comparator comparator = new(logger);
    private string MODSYNC_DIR => Path.Combine(frontend.GetSPTDirectory(), "ModSync_Data");
    private string PENDING_UPDATES_DIR => Path.Combine(MODSYNC_DIR, "PendingUpdates");
    private string PREVIOUS_SYNC_PATH => Path.Combine(MODSYNC_DIR, "PreviousSync.json");
    private string LOCAL_HASHES_PATH => Path.Combine(MODSYNC_DIR, "LocalHashes.json");
    private string REMOVED_FILES_PATH => Path.Combine(MODSYNC_DIR, "RemovedFiles.json");
    private string LOCAL_EXCLUSIONS_PATH => Path.Combine(MODSYNC_DIR, "Exclusions.json");

    private bool CheckPreviousUpdate()
    {
        if (Directory.Exists(PENDING_UPDATES_DIR) || File.Exists(REMOVED_FILES_PATH))
            logger.LogWarning(
                "ModSync found previous update. Updater may have failed, check the 'ModSync_Data/Updater.log' for details. Attempting to continue."
            );

        return true;
    }

    private Version serverVersion;

    /// <exception cref="HttpRequestException"></exception>
    private async Task FetchVersion()
    {
        logger.LogDebug("Fetching server version");
        try
        {
            serverVersion = new Version(await server.GetVersion());

            logger.LogInfo($"ModSync found server version: {serverVersion}");
            if (serverVersion != version)
                logger.LogWarning(
                    $"ModSync server version does not match plugin version. Found server version: {serverVersion}. Plugin may not work as expected!"
                );
        }
        catch (Exception e)
        {
            logger.LogError(e);

            throw new SyncException("requesting server version", "ensure the server mod is properly installed and try again");
        }
    }

    private List<SyncPath> syncPaths;
    private List<SyncPath> enabledSyncPaths;
    private bool canDeleteRemovedFiles;

    private async Task FetchSyncPaths()
    {
        logger.LogDebug("Fetching sync paths");
        try
        {
            syncPaths = await server.GetPaths();
        }
        catch (Exception e)
        {
            logger.LogError(e);
            throw new SyncException("error requesting sync paths", "ensure the server mod is properly installed and try again");
        }

        logger.LogDebug("Processing sync paths");
        foreach (var syncPath in syncPaths)
        {
            if (Path.IsPathRooted(syncPath.path))
                throw new SyncException("invalid sync path", $"ensure paths are relative to SPT server root! Invalid path '{syncPath}'");

            if (!Path.GetFullPath(syncPath.path).StartsWith(Directory.GetCurrentDirectory()))
                throw new SyncException("invalid sync path", $"ensure paths are relative to SPT server root! Invalid path '{syncPath}'");
        }

        enabledSyncPaths = frontend.ConfigureSyncPaths(syncPaths);
        canDeleteRemovedFiles = frontend.CanDeleteRemovedFiles();
    }

    private void RunMigrator()
    {
        logger.LogDebug("Running migrator");
        new Migrator(Environment.CurrentDirectory, logger).TryMigrate(version, syncPaths);
    }

    private SyncPathModFiles previousSync;
    private List<string> localExclusions;

    private void LoadLocalData()
    {
        logger.LogDebug("Loading previous sync data");
        try
        {
            previousSync = File.Exists(PREVIOUS_SYNC_PATH) ? JsonConvert.DeserializeObject<SyncPathModFiles>(File.ReadAllText(PREVIOUS_SYNC_PATH)) : [];
        }
        catch (Exception e)
        {
            logger.LogError(e);
            throw new SyncException("malformed previous sync data", "check ModSync_Data/PreviousSync.json for errors or delete it, and try again");
        }

        try
        {
            localExclusions = File.Exists(LOCAL_EXCLUSIONS_PATH) ? JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(LOCAL_EXCLUSIONS_PATH)) : [];
        }
        catch (Exception e)
        {
            logger.LogError(e);
            throw new SyncException("malformed local exclusion data", "check ModSync_Data/Exclusions.json for errors or delete it, and try again");
        }
    }

    private List<string> remoteExclusions;
    private SyncPathModFiles remoteModFiles;

    private async Task FetchRemoteData()
    {
        logger.LogDebug("Fetching remote exclusions");
        try
        {
            remoteExclusions = await server.GetExclusions();
        }
        catch (Exception e)
        {
            logger.LogError(e);
            throw new SyncException("error requesting remote exclusions", "ensure the server mod is properly installed and try again");
        }

        logger.LogDebug("Fetching remote mod files");
        try
        {
            var remoteHashes = await server.GetRemoteModFileHashes(enabledSyncPaths);
            var localExclusionsForRemote = localExclusions.Select(Glob.CreateNoEnd).ToList();

            remoteModFiles = enabledSyncPaths
                .Select(
                    (syncPath) =>
                    {
                        var remotePathHashes = remoteHashes[syncPath.path];

                        if (!syncPath.enforced)
                            remotePathHashes = remotePathHashes
                                .Where((kvp) => !comparator.IsExcluded(localExclusionsForRemote, kvp.Key))
                                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

                        return new KeyValuePair<string, Dictionary<string, ModFile>>(syncPath.path, remotePathHashes);
                    }
                )
                .ToDictionary((kvp) => kvp.Key, (kvp) => kvp.Value, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            logger.LogError(e);
            throw new SyncException("error requesting server mod list", "check the server log and try again");
        }
    }

    private SyncPathModFiles localModFiles;
    private Dictionary<SyncPath, SyncDiff> syncDiffs;

    private async Task HashLocalFiles()
    {
        while (!frontend.ReadyToHash())
        {
            await Task.Delay(500);
        }

        localModFiles = await comparator.HashLocalFiles(
            Directory.GetCurrentDirectory(),
            enabledSyncPaths,
            remoteExclusions.Select(Glob.Create).ToList(),
            localExclusions.Select(Glob.Create).ToList()
        );

        File.WriteAllText(LOCAL_HASHES_PATH, JsonConvert.SerializeObject(localModFiles, Formatting.Indented));

        comparator.CompareModFiles(frontend.GetSPTDirectory(), enabledSyncPaths, localModFiles, remoteModFiles, previousSync, out syncDiffs);

        var modifiedCount = syncDiffs.Sum(s => s.Value.Count);
        logger.LogInfo($"Found {modifiedCount} modified files.");
        if (modifiedCount > 0)
        {
            logger.LogInfo($"- {syncDiffs.Sum(s => s.Value.Added.Count)} added");
            logger.LogInfo($"- {syncDiffs.Sum(s => s.Value.Updated.Count)} updated");
            logger.LogInfo($"- {syncDiffs.Sum(s => s.Value.Removed.Count)} removed");
            logger.LogInfo($"- {syncDiffs.Sum(s => s.Value.Created.Count)} created directories");

            frontend.ShouldUpdatePrompt(syncDiffs, () => _ = DownloadModifiedFiles(syncDiffs), () => _ = SkipUpdate());
        }
        else
        {
            WriteModSyncData();
        }
    }

    private async Task SkipUpdate()
    {
        var enforcedSyncDiffs = syncDiffs.Where(kvp => kvp.Key.enforced).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        if (enforcedSyncDiffs.Sum(s => s.Value.Count) > 0)
        {
            await DownloadModifiedFiles(enforcedSyncDiffs);
        }
        else
        {
            frontend.Cleanup();
        }
    }

    private void WriteModSyncData()
    {
        File.WriteAllText(PREVIOUS_SYNC_PATH, JsonConvert.SerializeObject(remoteModFiles, Formatting.Indented));
        if (canDeleteRemovedFiles && syncDiffs.Sum(s => s.Value.Removed.Count) > 0)
        {
            File.WriteAllText(REMOVED_FILES_PATH, JsonConvert.SerializeObject(syncDiffs.SelectMany(kvp => kvp.Value.Removed).ToList(), Formatting.Indented));
        }
    }

    private async Task DownloadModifiedFiles(Dictionary<SyncPath, SyncDiff> queuedSyncDiffs)
    {
        var cts = new CancellationTokenSource();
        var cannotCancel = queuedSyncDiffs.Any((kvp) => (kvp.Key.enforced || !kvp.Key.restartRequired) && (kvp.Value.Added.Any() || kvp.Value.Updated.Any()));
        var filesToDownload = queuedSyncDiffs.SelectMany(kvp => kvp.Value.Added.Concat(kvp.Value.Updated)).ToList();

        if (!Directory.Exists(PENDING_UPDATES_DIR))
            Directory.CreateDirectory(PENDING_UPDATES_DIR);

        foreach (var syncDiff in queuedSyncDiffs.Values)
        {
            foreach (var createdDirectory in syncDiff.Created)
            {
                try
                {
                    Directory.CreateDirectory(createdDirectory);
                }
                catch (Exception e)
                {
                    logger.LogError($"Failed to create empty directories: {e}");
                }
            }
        }

        var limiter = new SemaphoreSlim(8);
        logger.LogInfo($"Starting download of {filesToDownload.Count} files.");

        var downloadTasks = filesToDownload.Select((file) => server.DownloadFile(file, PENDING_UPDATES_DIR, limiter, cts.Token)).ToList();

        frontend.ShowInitialProgress(filesToDownload.Count, cannotCancel ? null : () => CancelDownload(cts));

        while (downloadTasks.Count > 0 && !cts.IsCancellationRequested)
        {
            var task = await Task.WhenAny(downloadTasks);

            try
            {
                await task;
            }
            catch (TaskCanceledException)
            {
                if (cts.IsCancellationRequested)
                    break;
            }
            catch (DownloadException e)
            {
                cts.Cancel();
                frontend.DownloadErrorAlert(e.Message);
                break;
            }

            downloadTasks.Remove(task);
            frontend.UpdateProgress(filesToDownload.Count - downloadTasks.Count);
        }

        await Task.WhenAll(downloadTasks);
        downloadTasks.Clear();

        if (!cts.IsCancellationRequested)
        {
            logger.LogInfo("Finished downloading files.");
            WriteModSyncData();

            if (queuedSyncDiffs.All((kvp) => !kvp.Key.restartRequired || (kvp.Value.Count == 0)))
            {
                Directory.Delete(PENDING_UPDATES_DIR, true);
                frontend.ApplyUpdate();
            }
            else
            {
                frontend.RestartPrompt(frontend.ApplyUpdate);
            }
        }
        else
        {
            Directory.Delete(PENDING_UPDATES_DIR, true);
            logger.LogInfo("Download cancelled.");
        }
    }

    private void CancelDownload(CancellationTokenSource cts)
    {
        cts.Cancel();
        frontend.Cleanup();
    }

    public async Task Start()
    {
        foreach (var task in GetTasks())
            await task;
    }

    public List<Task> GetTasks()
    {
        return
        [
            new Task(() => CheckPreviousUpdate()),
            new Task(() => FetchVersion().Wait()),
            new Task(() => FetchSyncPaths().Wait()),
            new Task(RunMigrator),
            new Task(LoadLocalData),
            new Task(() => FetchRemoteData().Wait()),
            new Task(() => HashLocalFiles().Wait()),
        ];
    }
}
