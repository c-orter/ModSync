using System;

namespace ModSync.Core.Util;

public class SyncException(string reason, string suggestion) : Exception($"Could not load Corter.ModSync due to {reason}. Please {suggestion}.")
{
    public readonly string Reason = reason;
    public readonly string Suggestion = suggestion;
}
