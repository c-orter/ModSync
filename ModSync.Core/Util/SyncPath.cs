namespace ModSync.Core.Util;

public class SyncPath(string path, string name = "", bool enabled = true, bool enforced = false, bool silent = false, bool restartRequired = true)
{
    public string path = path;
    public string name = string.IsNullOrEmpty(name) ? path : name;
    public bool enabled = enabled;
    public bool enforced = enforced;
    public bool silent = silent;
    public bool restartRequired = restartRequired;
}
