using System;

namespace ModSync.Core.Util;

public interface ILogger
{
    /// <summary>
    ///     Logs a message with <see cref="LogLevel.Fatal" /> level.
    /// </summary>
    /// <param name="data">Data to log.</param>
    public void LogFatal(object data);

    /// <summary>
    ///     Logs a message with <see cref="LogLevel.Error" /> level.
    /// </summary>
    /// <param name="data">Data to log.</param>
    public void LogError(object data);

    /// <summary>
    ///     Logs a message with <see cref="LogLevel.Warning" /> level.
    /// </summary>
    /// <param name="data">Data to log.</param>
    public void LogWarning(object data);

    /// <summary>
    ///     Logs a message with <see cref="LogLevel.Message" /> level.
    /// </summary>
    /// <param name="data">Data to log.</param>
    public void LogMessage(object data);

    /// <summary>
    ///     Logs a message with <see cref="LogLevel.Info" /> level.
    /// </summary>
    /// <param name="data">Data to log.</param>
    public void LogInfo(object data);

    /// <summary>
    ///     Logs a message with <see cref="LogLevel.Debug" /> level.
    /// </summary>
    /// <param name="data">Data to log.</param>
    public void LogDebug(object data);
}
