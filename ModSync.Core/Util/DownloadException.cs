using System;

namespace ModSync.Core.Util;

public class DownloadException(string message) : Exception(message) { }
