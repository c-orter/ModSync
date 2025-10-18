namespace ModSync.Core.Util;

public class ModFile(string hash, bool directory = false)
{
    public string hash = hash;
    public bool directory = directory;
}
