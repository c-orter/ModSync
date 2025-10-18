using System.Collections.Generic;

namespace ModSync.Core.Util;

public class SyncDiff(List<string> added, List<string> updated, List<string> removed, List<string> created)
{
    public List<string> Added => added;
    public List<string> Updated => updated;
    public List<string> Removed => removed;
    public List<string> Created => created;
    public int Count => added.Count + updated.Count + removed.Count + created.Count;
}
