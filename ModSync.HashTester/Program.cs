// See https://aka.ms/new-console-template for more information
using ModSync.Core.Util;

if (args.Length == 0)
{
    Console.WriteLine("Usage: ModSync.HashTester.exe <filename>");
    return;
}

var metroHash = MetroHash128.Hash(File.ReadAllBytes(args[0]));
Console.WriteLine($"MetroHash: {BitConverter.ToString(metroHash).Replace("-", string.Empty).ToLowerInvariant()}");

var imoHash = await ImoHash.HashFile(args[0]);
Console.WriteLine($"ImoHash: {imoHash}");
