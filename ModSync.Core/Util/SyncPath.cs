namespace ModSync.Core;

public class SyncPath(string path, string name = "", bool enabled = true, bool enforced = false, bool silent = false, bool restartRequired = true)
{
    public readonly string path = path;
    public readonly string name = string.IsNullOrEmpty(name) ? path : name;
    public readonly bool enabled = enabled;
    public readonly bool enforced = enforced;
    public readonly bool silent = silent;
    public readonly bool restartRequired = restartRequired;
}
