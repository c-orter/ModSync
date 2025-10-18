using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ModSync.Core.Util;

namespace ModSync.Core;

using SyncPathFileList = Dictionary<string, List<string>>;
using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

public class Comparator(ILogger logger)
{
    public List<string> GetAddedFiles(SyncPath syncPath, Dictionary<string, ModFile> localModFiles, Dictionary<string, ModFile> remoteModFiles)
    {
        return remoteModFiles
            .Where((kvp) => !kvp.Value.directory)
            .Select((kvp) => kvp.Key)
            .Except(localModFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<string> GetUpdatedFiles(
        SyncPath syncPath,
        Dictionary<string, ModFile> localModFiles,
        Dictionary<string, ModFile> remoteModFiles,
        Dictionary<string, ModFile> previousRemoteModFiles
    )
    {
        var query = remoteModFiles.Keys.Intersect(localModFiles.Keys, StringComparer.OrdinalIgnoreCase);

        if (!syncPath.enforced)
            query = query.Where(file => !previousRemoteModFiles.TryGetValue(file, out var modFile) || remoteModFiles[file].hash != modFile.hash);

        query = query.Where(file => remoteModFiles[file].hash != localModFiles[file].hash);

        return query.ToList();
    }

    public List<string> GetRemovedFiles(
        SyncPath syncPath,
        Dictionary<string, ModFile> localModFiles,
        Dictionary<string, ModFile> remoteModFiles,
        Dictionary<string, ModFile> previousRemoteModFiles
    )
    {
        IEnumerable<string> query;
        if (syncPath.enforced)
            query = localModFiles.Keys.Except(remoteModFiles.Keys, StringComparer.OrdinalIgnoreCase);
        else
            query = previousRemoteModFiles
                .Keys.Intersect(localModFiles.Keys, StringComparer.OrdinalIgnoreCase)
                .Except(remoteModFiles.Keys, StringComparer.OrdinalIgnoreCase);

        return query.ToList();
    }

    public List<string> GetCreatedDirectories(
        string basePath,
        SyncPath syncPath,
        Dictionary<string, ModFile> localModFiles,
        Dictionary<string, ModFile> remoteModFiles
    )
    {
        return remoteModFiles
            .Where((kvp) => kvp.Value.directory)
            .Select((kvp) => kvp.Key)
            .Except(localModFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .Where((dir) => !Directory.Exists(Path.Combine(basePath, dir)))
            .ToList();
    }

    private List<string> GetFilesInDirectory(string basePath, string directory, List<Regex> exclusions)
    {
        if (File.Exists(directory))
            return [directory];

        if (!Directory.Exists(directory))
            return [];

        return Directory
            .GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where((file) => !IsExcluded(exclusions, file.Replace($"{basePath}\\", "")))
            .Concat(
                Directory
                    .GetDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                    .Where((subDir) => !IsExcluded(exclusions, subDir.Replace($"{basePath}\\", "")))
                    .SelectMany((subDir) => Directory.GetFileSystemEntries(subDir).Length == 0 ? [subDir] : GetFilesInDirectory(basePath, subDir, exclusions))
            )
            .ToList();
    }

    public async Task<SyncPathModFiles> HashLocalFiles(string basePath, List<SyncPath> syncPaths, List<Regex> remoteExclusions, List<Regex> localExclusions)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var processedFiles = new HashSet<string>();
        var limitOpenFiles = new SemaphoreSlim(1024);

        var results = new SyncPathModFiles();

        foreach (var syncPath in syncPaths)
        {
            var path = Path.Combine(basePath, syncPath.path);

            results[syncPath.path] = (
                await Task.WhenAll(
                    GetFilesInDirectory(basePath, path, [.. remoteExclusions, .. syncPath.enforced ? [] : localExclusions])
                        .Where((file) => !processedFiles.Contains(file))
                        .AsParallel()
                        .Select(
                            async (file) =>
                            {
                                await limitOpenFiles.WaitAsync();
                                var modFile = await CreateModFile(file);
                                limitOpenFiles.Release();

                                processedFiles.Add(file);
                                return new KeyValuePair<string, ModFile>(file.Replace($"{basePath}\\", ""), modFile);
                            }
                        )
                )
            ).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        }

        watch.Stop();
        logger.LogInfo($"Corter-ModSync: Hashed {processedFiles.Count} files in {watch.Elapsed.TotalMilliseconds}ms");

        return results;
    }

    public async Task<ModFile> CreateModFile(string file)
    {
        var hash = "";

        if (Directory.Exists(file))
            return new ModFile(hash, true);

        try
        {
            hash = await ImoHash.HashFile(file);
        }
        catch (Exception e)
        {
            logger.LogError($"Corter-ModSync: Error hashing '{file}': {e.Message}");
            hash = "";
        }

        return new ModFile(hash);
    }

    public void CompareModFiles(
        string basePath,
        List<SyncPath> syncPaths,
        SyncPathModFiles localModFiles,
        SyncPathModFiles remoteModFiles,
        SyncPathModFiles previousSync,
        out Dictionary<SyncPath, SyncDiff> syncDiffs
    )
    {
        syncDiffs = syncPaths
            .Select(syncPath =>
                (
                    syncPath,
                    localPathFiles: localModFiles.TryGetValue(syncPath.path, out var localPathFiles) ? localPathFiles : [],
                    remotePathFiles: remoteModFiles.TryGetValue(syncPath.path, out var remotePathFiles) ? remotePathFiles : [],
                    previousPathSync: previousSync.TryGetValue(syncPath.path, out var previousPathSync) ? previousPathSync : []
                )
            )
            .Select(
                (value) =>
                    (
                        value.syncPath,
                        syncDiff: new SyncDiff(
                            GetAddedFiles(value.syncPath, value.localPathFiles, value.remotePathFiles),
                            GetUpdatedFiles(value.syncPath, value.localPathFiles, value.remotePathFiles, value.previousPathSync),
                            GetRemovedFiles(value.syncPath, value.localPathFiles, value.remotePathFiles, value.previousPathSync),
                            GetCreatedDirectories(basePath, value.syncPath, value.localPathFiles, value.remotePathFiles)
                        )
                    )
            )
            .ToDictionary(kvp => kvp.syncPath, kvp => kvp.syncDiff);
    }

    public bool IsExcluded(List<Regex> exclusions, string path)
    {
        return exclusions.Any(regex => regex.IsMatch(path.Replace(@"\", "/")));
    }
}
