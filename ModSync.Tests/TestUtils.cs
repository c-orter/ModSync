using System.Net;
using System.Text;
using ModSync.Core;
using ModSync.Core.Util;
using Newtonsoft.Json;

namespace ModSync.Tests;

public static class TestUtils
{
    public static string GetTemporaryDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        if (File.Exists(tempDirectory))
            return GetTemporaryDirectory();

        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

    public class TestLogger : ILogger
    {
        private readonly List<(string, string)> _Logs = [];
        public List<(string, string)> Logs => _Logs;

        public void LogDebug(object data)
        {
            _Logs.Add(("DEBUG", $"{data}"));
        }

        public void LogError(object data)
        {
            _Logs.Add(("ERROR", $"{data}"));
        }

        public void LogFatal(object data)
        {
            _Logs.Add(("FATAL", $"{data}"));
        }

        public void LogInfo(object data)
        {
            _Logs.Add(("INFO", $"{data}"));
        }

        public void LogMessage(object data)
        {
            _Logs.Add(("MESSAGE", $"{data}"));
        }

        public void LogWarning(object data)
        {
            _Logs.Add(("WARNING", $"{data}"));
        }
    }

    public class TestFrontend : ISyncFrontend
    {
        private readonly TestLogger logger = new();
        private bool _Finished = false;
        public bool Finished => _Finished;

        public bool canDelete;
        public Func<SyncPath, bool>? enableSyncPath;
        public string sptDirectory;
        public bool acceptUpdate;
        public bool cancelDownload;

        public TestFrontend(
            bool canDelete = true,
            Func<SyncPath, bool>? enableSyncPath = null,
            string sptDirectory = "",
            bool acceptUpdate = true,
            bool cancelDownload = false
        )
        {
            this.canDelete = canDelete;
            this.enableSyncPath = enableSyncPath;
            this.sptDirectory = sptDirectory;
            this.acceptUpdate = acceptUpdate;
            this.cancelDownload = cancelDownload;
        }

        public int ApplyUpdateCallCount = 0;

        public void ApplyUpdate()
        {
            ApplyUpdateCallCount++;
            logger.LogInfo("Applying update");
            _Finished = true;
        }

        public int CanDeleteRemovedFilesCallCount = 0;

        public bool CanDeleteRemovedFiles()
        {
            CanDeleteRemovedFilesCallCount++;
            logger.LogInfo("CanDeleteRemovedFiles");
            return canDelete;
        }

        public int CleanupCallCount = 0;

        public void Cleanup()
        {
            CleanupCallCount++;
            logger.LogInfo("Cleanup");
            _Finished = true;
        }

        public int ConfigureSyncPathsCallCount = 0;
        public List<SyncPath> ConfigureSyncPathsParams = [];
        public List<SyncPath> ConfigureSyncPathsReturn = [];

        public List<SyncPath> ConfigureSyncPaths(List<SyncPath> syncPaths)
        {
            ConfigureSyncPathsCallCount++;
            ConfigureSyncPathsParams = syncPaths;
            ConfigureSyncPathsReturn = syncPaths.Where((SyncPath path) => enableSyncPath == null || enableSyncPath(path)).ToList();
            logger.LogInfo("ConfigureSyncPaths");
            return ConfigureSyncPathsReturn;
        }

        public int DownloadErrorAlertCallCount = 0;
        public string DownloadErrorAlertParams = string.Empty;

        public void DownloadErrorAlert(string errorMessage)
        {
            DownloadErrorAlertCallCount++;
            DownloadErrorAlertParams = errorMessage;
            logger.LogInfo("DownloadErrorAlert");
        }

        public int GetSPTDirectoryCallCount = 0;
        public string GetSPTDirectoryReturn = string.Empty;

        public string GetSPTDirectory()
        {
            GetSPTDirectoryCallCount++;
            GetSPTDirectoryReturn = sptDirectory;
            logger.LogInfo("GetSPTDirectory");
            return GetSPTDirectoryReturn;
        }

        public int ReadToHashCallCount = 0;
        public bool ReadToHashReturn = false;

        public bool ReadyToHash()
        {
            ReadToHashCallCount++;
            logger.LogInfo("ReadyToHash");
            return true;
        }

        public int RestartPromptCallCount = 0;
        public Action RestartPromptParams = null;

        public void RestartPrompt(Action restartAction)
        {
            RestartPromptCallCount++;
            RestartPromptParams = restartAction;
            logger.LogInfo("RestartPrompt");
            restartAction();
        }

        public int ShouldUpdatePromptCallCount = 0;
        public (Dictionary<SyncPath, SyncDiff> syncDiffs, Action updateAction, Action cancelAction) ShouldUpdatePromptParams = (null, null, null);

        public void ShouldUpdatePrompt(Dictionary<SyncPath, SyncDiff> syncDiffs, Action updateAction, Action cancelAction)
        {
            ShouldUpdatePromptCallCount++;
            ShouldUpdatePromptParams = (syncDiffs, updateAction, cancelAction);
            logger.LogInfo("ShouldUpdatePrompt");

            if (acceptUpdate)
                updateAction();
            else
                cancelAction();
        }

        public int ShowInitialProgressCallCount = 0;
        public (int downloadCount, Action cancelAction) ShowInitialProgressParams = (-1, null);

        public void ShowInitialProgress(int downloadCount, Action cancelAction)
        {
            ShowInitialProgressCallCount++;
            ShowInitialProgressParams = (downloadCount, cancelAction);
            logger.LogInfo("ShowInitialProgress");
        }

        public int UpdateProgressCallCount = 0;
        public int UpdateProgressParams = -1;

        public void UpdateProgress(int current)
        {
            UpdateProgressCallCount++;
            UpdateProgressParams = current;
            logger.LogInfo("UpdateProgress");

            if (cancelDownload)
                ShowInitialProgressParams.cancelAction();
        }
    }

    public class TestServer
    {
        public Version version;
        public List<SyncPath> syncPaths;
        public List<string> exclusions;
        public Dictionary<string, Dictionary<string, ModFile>> modFiles;
        public byte[] fileData;
        public CancellationTokenSource cts;
        public HttpListener listener;
        public string url;
        public bool failVersion;
        public bool failSyncPaths;
        public bool failExclusions;
        public bool failModFiles;
        public int failFileDataCount;

        Task listenerTask;

        public TestServer(
            Version version,
            List<SyncPath> syncPaths,
            List<string> exclusions,
            Dictionary<string, Dictionary<string, ModFile>> modFiles,
            byte[] fileData,
            bool failVersion = false,
            bool failSyncPaths = false,
            bool failExclusions = false,
            bool failModFiles = false,
            int failFileDataCount = 0
        )
        {
            this.version = version;
            this.syncPaths = syncPaths;
            this.exclusions = exclusions;
            this.modFiles = modFiles;
            this.fileData = fileData;
            this.failVersion = failVersion;
            this.failSyncPaths = failSyncPaths;
            this.failExclusions = failExclusions;
            this.failModFiles = failModFiles;
            this.failFileDataCount = failFileDataCount;
            url = $"http://localhost:{new Random().Next(10000, 20000)}";
            cts = new CancellationTokenSource();

            listener = new HttpListener();
            listener.Prefixes.Add($"{url}/");
            listener.Start();

            listenerTask = Task.Run(() => HandleIncomingConnections(cts.Token));
        }

        public async Task Shutdown()
        {
            cts.Cancel();
            listener.Close();
            await listenerTask;
        }

        public async Task HandleIncomingConnections(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Will wait here until we hear from a connection
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync();
                }
                catch (Exception e) when (e is HttpListenerException || e is ObjectDisposedException)
                {
                    break;
                }

                // Peel out the requests and response objects
                HttpListenerRequest req = ctx.Request;
                using HttpListenerResponse resp = ctx.Response;
                resp.ContentType = "text/json";
                resp.ContentEncoding = Encoding.UTF8;
                resp.StatusCode = (int)HttpStatusCode.OK;

                string data;
                switch (req.Url?.AbsolutePath)
                {
                    case "/modsync/version":
                        if (failVersion)
                        {
                            resp.StatusCode = (int)HttpStatusCode.InternalServerError;
                            data = "Failed to get version";
                            break;
                        }

                        data = JsonConvert.SerializeObject(version.ToString());
                        break;
                    case "/modsync/paths":
                        if (failSyncPaths)
                        {
                            resp.StatusCode = (int)HttpStatusCode.InternalServerError;
                            data = "Failed to get paths";
                            break;
                        }

                        data = JsonConvert.SerializeObject(syncPaths);
                        break;
                    case "/modsync/exclusions":
                        if (failExclusions)
                        {
                            resp.StatusCode = (int)HttpStatusCode.InternalServerError;
                            data = "Failed to get exclusions";
                            break;
                        }

                        data = JsonConvert.SerializeObject(exclusions);
                        break;
                    case "/modsync/hashes":
                        if (failModFiles)
                        {
                            resp.StatusCode = (int)HttpStatusCode.InternalServerError;
                            data = "Failed to get mod files";
                            break;
                        }

                        data = JsonConvert.SerializeObject(modFiles);
                        break;
                    case string s when s.StartsWith("/modsync/fetch/"):
                        if (failFileDataCount > 0)
                        {
                            resp.StatusCode = (int)HttpStatusCode.InternalServerError;
                            data = "Failed to get file data";
                            failFileDataCount--;
                            break;
                        }

                        data = Encoding.UTF8.GetString(fileData);
                        resp.ContentType = "application/octet-stream";
                        break;
                    default:
                        data = "Not found";
                        resp.StatusCode = (int)HttpStatusCode.NotFound;
                        break;
                }

                // Write the response info
                byte[] body = Encoding.UTF8.GetBytes(data);
                resp.ContentLength64 = body.LongLength;

                // Write out to the response stream (asynchronously), then close it
                await resp.OutputStream.WriteAsync(body, cancellationToken);
            }
        }
    }
}
