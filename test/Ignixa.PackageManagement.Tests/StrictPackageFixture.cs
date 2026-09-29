using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Ignixa.PackageManagement.Tests;

internal static class StrictPackageFixture
{
    internal const string Manifest = """{"name":"example.fhir.search","version":"1.2.0","fhirVersions":["4.0.1","3.0.2"]}""";
    internal const string SearchParameter = """{"resourceType":"SearchParameter","id":"test","url":"https://example.org/SearchParameter/test","code":"test","base":["Patient"],"type":"string","expression":"Patient.name"}""";

    internal static byte[] Tar(params (string Path, string Json)[] files) =>
        TarEntries(files.Select(f => File(f.Path, f.Json)).ToArray());

    internal static UstarTarEntry File(string path, string content) => new(TarEntryType.RegularFile, path)
    {
        DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
        ModificationTime = DateTimeOffset.UnixEpoch
    };

    internal static byte[] TarEntries(params TarEntry[] entries)
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, leaveOpen: true))
        {
            foreach (TarEntry entry in entries)
            {
                writer.WriteEntry(entry);
                entry.DataStream?.Dispose();
            }
        }
        return output.ToArray();
    }

    internal static byte[] Gzip(byte[] tar)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(tar);
        }
        return output.ToArray();
    }

    internal static byte[] Package(params (string Path, string Json)[] files) =>
        Gzip(Tar([("package/package.json", Manifest), .. files]));

    internal static byte[] Pax(string attributes, string path = "package/a.json")
    {
        byte[] tar = Tar(
            ("package/package.json", Manifest),
            ("PaxHeaders/entry", attributes),
            (path, "{}"));
        SetHeader(tar, 1024, 156, 1, "x");
        return tar;
    }

    internal static string PaxAttribute(string key, string value)
    {
        string body = $" {key}={value}\n";
        int length = Encoding.UTF8.GetByteCount(body) + 1;
        while (length != Encoding.UTF8.GetByteCount(body) + length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length)
        {
            length = Encoding.UTF8.GetByteCount(body) + length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        }
        return length.ToString(System.Globalization.CultureInfo.InvariantCulture) + body;
    }

    internal static void SetHeader(byte[] tar, int offset, int fieldOffset, int fieldLength, string value)
    {
        Array.Clear(tar, offset + fieldOffset, fieldLength);
        Encoding.UTF8.GetBytes(value).CopyTo(tar, offset + fieldOffset);
        Array.Fill(tar, (byte)' ', offset + 148, 8);
        int checksum = tar.AsSpan(offset, 512).ToArray().Sum(b => (int)b);
        Encoding.ASCII.GetBytes(Convert.ToString(checksum, 8).PadLeft(6, '0') + "\0 ").CopyTo(tar, offset + 148);
    }
}
