using System;
using BepInEx.Logging;

namespace ModSync.Plugin;

public class SyncLogger(ManualLogSource logger) : Core.Util.ILogger
{
    public void LogDebug(object data)
    {
        logger.LogDebug(data);
    }

    public void LogError(object data)
    {
        logger.LogError(data);
    }

    public void LogFatal(object data)
    {
        logger.LogFatal(data);
    }

    public void LogInfo(object data)
    {
        logger.LogInfo(data);
    }

    public void LogMessage(object data)
    {
        logger.LogMessage(data);
    }

    public void LogWarning(object data)
    {
        logger.LogWarning(data);
    }
}
