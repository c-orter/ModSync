using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ModSync.Utility;
using SPT.Common.Http;
using SPT.Common.Utils;

namespace ModSync;

using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

public class Server(Version pluginVersion)
{
    private async Task<string> GetJson(string path)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("modsync-version", pluginVersion.ToString());
            client.Timeout = TimeSpan.FromMinutes(5);
            var json = await client.GetStringAsync($"{RequestHandler.Host}{path}");
            return json;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"There was an error performing request.\n{e.Message}\n{e.StackTrace}");
            throw;
        }
    }

    public async Task DownloadFile(string file, string downloadDir, SemaphoreSlim limiter, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var downloadPath = Path.Combine(downloadDir, file);
        VFS.CreateDirectory(downloadPath.GetDirectory());

        var retryCount = 0;

        await limiter.WaitAsync();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new HttpClient();
                if (retryCount > 0)
                    client.Timeout = TimeSpan.FromMinutes(10);

                using var responseStream = await client.GetStreamAsync($"{RequestHandler.Host}/modsync/fetch/{file}");
                using var fileStream = new FileStream(downloadPath, FileMode.Create);

                await responseStream.CopyToAsync(fileStream);

                limiter.Release();
                return;
            }
            catch (Exception e)
            {
                if (e is TaskCanceledException && cancellationToken.IsCancellationRequested)
                    throw;

                if (retryCount < 5)
                {
                    Plugin.Logger.LogError($"Failed to download '{file}'. Retrying ({retryCount + 1}/5)...");
                    Plugin.Logger.LogDebug(e);
                    await Task.Delay(500, cancellationToken);
                    retryCount++;
                    continue;
                }

                Plugin.Logger.LogError($"Failed to download '{file}'. Exiting...");
                Plugin.Logger.LogError(e);
                throw;
            }
        }
    }

    public async Task<string> GetModSyncVersion()
    {
        return Json.Deserialize<string>(await GetJson("/modsync/version"));
    }

    public async Task<List<SyncPath>> GetModSyncPaths()
    {
        return Json.Deserialize<List<SyncPath>>(await GetJson("/modsync/paths"));
    }

    public async Task<List<string>> GetModSyncExclusions()
    {
        return Json.Deserialize<List<string>>(await GetJson("/modsync/exclusions"));
    }

    public async Task<SyncPathModFiles> GetRemoteModFileHashes(List<SyncPath> syncPaths)
    {
        return Json.Deserialize<SyncPathModFiles>(
                await GetJson($"/modsync/hashes?path={string.Join("&path=", syncPaths.Select(path => Uri.EscapeUriString(path.path.Replace(@"\", "/"))))}")
            )
            .ToDictionary(
                item => item.Key,
                item => item.Value.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase
            );
    }
}
