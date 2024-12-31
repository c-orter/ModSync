namespace ModSync.Core;

public class ModFile(string hash, bool directory = false)
{
    public readonly string hash = hash;
    public readonly bool directory = directory;
}
