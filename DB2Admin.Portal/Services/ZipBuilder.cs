using System.IO.Compression;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

public static class ZipBuilder
{
    public static byte[] BuildZip(IEnumerable<GeneratedFile> files)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(file.Content);
            }
        }

        return ms.ToArray();
    }
}
