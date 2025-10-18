using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using EFT.UI;
using ModSync.Core;
using ModSync.Core.Util;
using ModSync.Plugin.UI;
using SPT.Common.Http;
using UnityEngine;

namespace ModSync.Plugin;

[BepInPlugin("corter.modsync", "Corter ModSync", "1.0.0")]
public class Plugin : BaseUnityPlugin, ISyncFrontend
{
    private static readonly string SPTDir = Environment.CurrentDirectory;
    private static readonly string UPDATER_PATH = Path.Combine(Directory.GetCurrentDirectory(), "ModSync.Updater.exe");
    private static bool IsDedicated => Chainloader.PluginInfos.ContainsKey("com.fika.dedicated");

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
    internal static ConfigEntry<bool> RunSync { get; set; }

    private Dictionary<string, ConfigEntry<bool>> configSyncPathToggles;
    private ConfigEntry<bool> configDeleteRemovedFiles;

    public static new readonly SyncLogger Logger = new(BepInEx.Logging.Logger.CreateLogSource("ModSync"));

    private bool pluginFinished = false;
    private Core.Syncer syncer;

    private readonly TextAlertWindow updateWindow = new("Installed mods do not match server", "Would you like to update?", new Vector2(800f, 640f));
    private readonly ProgressWindow progressWindow = new("Downloading Updates...", "Your game will need to be restarted\nafter update completes.");
    private readonly AlertWindow restartWindow = new(new Vector2(480f, 200f), "Update Complete.", "Please restart your game to continue.");
    private readonly TextAlertWindow downloadErrorWindow = new(
        "Download failed!",
        "There was an error updating mod files.\nPlease check BepInEx/LogOutput.log for more information.",
        new Vector2(800f, 320f),
        "HOW ARE YOU SEEING THIS???",
        "QUIT",
        showTooltip: false,
        centerText: true
    );

    private IEnumerator SyncCoroutine(List<Task> tasks)
    {
        var failed = false;
        foreach (var task in tasks)
        {
            if (failed)
                yield break;

            task.ContinueWith(
                failure =>
                {
                    failure.Exception?.Handle(e =>
                    {
                        if (e is SyncException syncException)
                        {
                            Chainloader.DependencyErrors.Add(
                                $"Could not load {Info.Metadata.Name} due to {syncException.Reason}. Please {syncException.Suggestion}."
                            );
                            failed = true;
                        }

                        return false;
                    });
                },
                TaskContinuationOptions.OnlyOnFaulted
            );

            task.Start();

            yield return new WaitUntil(() => task.IsCompleted);
        }
    }

    private void Awake()
    {
        syncer = new Syncer(this, Info.Metadata.Version, RequestHandler.Host, Logger);
        configDeleteRemovedFiles = Config.Bind("General", "Delete Removed Files", true, "Should the mod delete files that have been removed from the server?");
        RunSync = Config.Bind(
            "Actions",
            "Sync with Server",
            false,
            new ConfigDescription("Sync with the server now", null, new ConfigurationManagerAttributes { CustomDrawer = DrawSyncButton })
        );

        StartCoroutine(SyncCoroutine(syncer.GetTasks()));
    }

    private void DrawSyncButton(ConfigEntryBase entry)
    {
        if (GUILayout.Button("Sync with Server", GUILayout.ExpandWidth(true)))
            Logger.LogInfo("Doing the thing!");
    }

    private void OnGUI()
    {
        if (!Singleton<CommonUI>.Instantiated)
            return;

        if (restartWindow.Active)
            restartWindow.Draw();

        if (progressWindow.Active)
            progressWindow.Draw();

        if (updateWindow.Active)
        {
            updateWindow.Draw();
        }

        if (downloadErrorWindow.Active)
            downloadErrorWindow.Draw();
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

    public List<SyncPath> ConfigureSyncPaths(List<SyncPath> syncPaths)
    {
        try
        {
            configSyncPathToggles = syncPaths
                .Select(syncPath => new KeyValuePair<string, ConfigEntry<bool>>(
                    syncPath.path,
                    Config.Bind(
                        "Synced Paths",
                        syncPath.name.Replace("\\", "/"),
                        syncPath.enabled,
                        new ConfigDescription(
                            $"Should the mod attempt to sync files from {syncPath.path.Replace("\\", "/")}",
                            null,
                            new ConfigurationManagerAttributes { ReadOnly = syncPath.enforced }
                        )
                    )
                ))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return syncPaths.Where(syncPath => configSyncPathToggles[syncPath.path].Value).ToList();
        }
        catch (Exception e)
        {
            Logger.LogError($"Error binding sync path configs. This is likely a bug with ModSync. Please report it in the FIKA discord.\n{e}");
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to error binding sync path configs. Please check your server configuration and try again."
            );

            return [];
        }
    }

    public bool CanDeleteRemovedFiles()
    {
        return configDeleteRemovedFiles.Value;
    }

    public bool ReadyToHash()
    {
        return Singleton<CommonUI>.Instantiated;
    }

    public void ShouldUpdatePrompt(Dictionary<SyncPath, SyncDiff> syncDiffs, Action updateAction, Action cancelAction)
    {
        Dictionary<SyncPath, List<string>> syncDiffStrings = syncDiffs
            .Select(kvp =>
                (
                    kvp.Key,
                    Value: kvp.Value.Added.Select(file => $"ADDED {file}")
                        .Concat(kvp.Value.Updated.Select(file => $"UPDATED {file}"))
                        .Concat((configDeleteRemovedFiles.Value || kvp.Key.enforced) ? kvp.Value.Removed.Select(file => $"REMOVED {file}") : [])
                        .Concat(kvp.Value.Created.Select(file => $@"CREATED {file}\"))
                        .ToList()
                )
            )
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        List<string> optional = syncDiffStrings.Where(kvp => !kvp.Key.enforced).SelectMany(kvp => kvp.Value).ToList();
        List<string> required = syncDiffStrings.Where(kvp => kvp.Key.enforced).SelectMany(kvp => kvp.Value).ToList();

        updateWindow.Show(
            (optional.Count != 0 ? string.Join("\n", optional) : "")
                + (optional.Count != 0 && required.Count != 0 ? "\n\n" : "")
                + (required.Count != 0 ? "[Enforced]\n" + string.Join("\n", required) : ""),
            updateAction,
            cancelAction
        );
    }

    public string GetSPTDirectory()
    {
        return SPTDir;
    }

    public void ApplyUpdate()
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

    public void ShowInitialProgress(int downloadCount, Action cancelAction)
    {
        updateWindow.Hide();
        progressWindow.Show(
            0,
            downloadCount,
            () =>
            {
                cancelAction();
                progressWindow.Hide();
            }
        );
    }

    public void UpdateProgress(int current)
    {
        progressWindow.Update(current);
    }

    public void RestartPrompt(Action restartAction)
    {
        progressWindow.Hide();
        restartWindow.Show(restartAction);
    }

    public void DownloadErrorAlert(string errorMessage)
    {
        progressWindow.Hide();
        downloadErrorWindow.Show(errorMessage, null, Application.Quit);
    }

    public void Cleanup()
    {
        updateWindow.Hide();
        restartWindow.Hide();
        progressWindow.Hide();
        downloadErrorWindow.Hide();

        pluginFinished = true;
    }
}
