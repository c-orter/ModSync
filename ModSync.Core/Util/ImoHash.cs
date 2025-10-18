namespace ModSync.Core.Util;

using System;
using System.IO;
using System.Threading.Tasks;

public class ImoHash
{
    private const long SAMPLE_THRESHOLD = 10 * 1024 * 1024;
    private const int SAMPLE_SIZE = 32 * 1024;

    private static void PutUvarint(byte[] buf, ulong x)
    {
        var i = 0;
        while (x >= 0x80)
        {
            buf[i] = (byte)((x & 0xff) | 0x80);
            x >>= 7;
            i++;
        }
        buf[i] = (byte)(x & 0xff);
    }

    private static async Task<byte[]> ReadChunk(Stream fs, long position, int length)
    {
        var buffer = new byte[length];
        fs.Seek(position, SeekOrigin.Begin);
        var bytesRead = await fs.ReadAsync(buffer, 0, length);
        if (bytesRead < length)
        {
            throw new Exception("Could not read enough data");
        }
        return buffer;
    }

    public static async Task<string> HashFileObject(Stream fs, long sampleThreshold = SAMPLE_THRESHOLD, int sampleSize = SAMPLE_SIZE)
    {
        var size = fs.Length;

        byte[] data;

        if (size < sampleThreshold || sampleSize < 1 || size < (4 * sampleSize))
        {
            data = await ReadChunk(fs, 0, (int)size);
            fs.Close(); // Close early to try and avoid conflicts with other mods
        }
        else
        {
            var start = await ReadChunk(fs, 0, sampleSize);
            var middle = await ReadChunk(fs, size / 2, sampleSize);
            var end = await ReadChunk(fs, size - sampleSize, sampleSize);
            fs.Close(); // Close early to try and avoid conflicts with other mods
            data = [.. start, .. middle, .. end];
        }

        var hashTmp = MetroHash128.Hash(data);

        PutUvarint(hashTmp, (ulong)size);

        return BitConverter.ToString(hashTmp).Replace("-", "").ToLower();
    }

    public static async Task<string> HashFile(string filename, long sampleThreshold = SAMPLE_THRESHOLD, int sampleSize = SAMPLE_SIZE)
    {
        using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read);
        return await HashFileObject(fs, sampleThreshold, sampleSize);
    }
}
