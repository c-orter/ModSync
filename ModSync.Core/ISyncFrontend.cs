using System;
using System.Collections.Generic;
using ModSync.Core.Util;

namespace ModSync.Core;

public interface ISyncFrontend
{
    /// <summary>
    ///     Allows frontend to configure enabled sync paths
    /// </summary>
    /// <returns>List of enabled sync paths</returns>
    List<SyncPath> ConfigureSyncPaths(List<SyncPath> syncPaths);

    /// <summary>
    ///     Check if frontend allows deleting removed files
    /// </summary>
    bool CanDeleteRemovedFiles();

    /// <summary>
    ///     Check if frontend is ready to hash local mod files
    /// </summary>
    bool ReadyToHash();

    /// <summary>
    ///     Check if the user wants to update
    /// </summary>
    /// <returns>Should return true if update should continue, false if update should be skipped</returns>
    void ShouldUpdatePrompt(Dictionary<SyncPath, SyncDiff> syncDiffs, Action updateAction, Action cancelAction);

    /// <summary>
    ///     Get path to SPT directory
    /// </summary>
    string GetSPTDirectory();

    /// <summary>
    ///     Any extra work to be done after the update completes
    /// </summary>
    void ApplyUpdate();

    /// <summary>
    ///     Cleanup frontend state after update is completed
    /// </summary>
    void Cleanup();

    /// <summary>
    ///     Show initial progress display
    /// </summary>
    void ShowInitialProgress(int downloadCount, Action cancelAction);

    /// <summary>
    ///     Update progress display
    /// </summary>
    void UpdateProgress(int current);

    /// <summary>
    ///     Show restart prompt
    /// </summary>
    void RestartPrompt(Action restartAction);

    /// <summary>
    ///     Show error alert
    /// </summary>
    void DownloadErrorAlert(string errorMessage);
}
