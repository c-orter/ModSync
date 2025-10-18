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

public class Server(Version pluginVersion, string hostname, ILogger logger)
{
    private async Task<string> GetJson(string path)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("modsync-version", pluginVersion.ToString());
            client.Timeout = TimeSpan.FromMinutes(5);
            var json = await client.GetStringAsync($"{hostname}{path}");
            return json;
        }
        catch (Exception e)
        {
            logger.LogError($"There was an error performing request.\n{e.Message}\n{e.StackTrace}");
            throw;
        }
    }

    public async Task DownloadFile(string file, string downloadDir, SemaphoreSlim limiter, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var downloadPath = Path.Combine(downloadDir, file);
        Directory.CreateDirectory(Directory.GetParent(downloadPath).FullName);

        var retryCount = 0;

        await limiter.WaitAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new HttpClient();
                if (retryCount > 0)
                    client.Timeout = TimeSpan.FromMinutes(10);

                using var response = await client.GetAsync($"{hostname}/modsync/fetch/{file}", cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    if (retryCount < 5 && !cancellationToken.IsCancellationRequested)
                    {
                        logger.LogError($"Failed to download '{file}'. Retrying ({retryCount + 1}/5)...");
                        logger.LogDebug(content);
                        await Task.Delay(500, cancellationToken);
                        retryCount++;
                        continue;
                    }

                    logger.LogError($"Failed to download '{file}'. Exiting...");
                    logger.LogError(content);
                    throw new DownloadException(content);
                }

                using var responseStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(downloadPath, FileMode.Create);

                if ((int)responseStream.Length > 0)
                    await responseStream.CopyToAsync(fileStream, (int)responseStream.Length, cancellationToken);

                limiter.Release();
                return;
            }
            catch (TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
            }
        }

        limiter.Release();
    }

    public async Task<string> GetVersion()
    {
        return JsonConvert.DeserializeObject<string>(await GetJson("/modsync/version"));
    }

    public async Task<List<SyncPath>> GetPaths()
    {
        return JsonConvert.DeserializeObject<List<SyncPath>>(await GetJson("/modsync/paths"));
    }

    public async Task<List<string>> GetExclusions()
    {
        return JsonConvert.DeserializeObject<List<string>>(await GetJson("/modsync/exclusions"));
    }

    public async Task<SyncPathModFiles> GetRemoteModFileHashes(List<SyncPath> syncPaths)
    {
        return JsonConvert
            .DeserializeObject<SyncPathModFiles>(
                await GetJson($"/modsync/hashes?path={string.Join("&path=", syncPaths.Select(path => Uri.EscapeUriString(path.path.Replace(@"\", "/"))))}")
            )
            .ToDictionary(
                item => item.Key,
                item => item.Value.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase
            );
    }
}
