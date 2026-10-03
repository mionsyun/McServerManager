using System.Buffers.Binary;
using System.Text;

namespace McServerManager.Services.Fabric;

/// <summary>
/// Conservative ZIP32 profile. Validates both directories before ZipArchive sees data.
/// Rejects encryption, ZIP64, multi-disk, symlinks, overlapping records, ambiguous names,
/// extra data, unsupported methods and excessive ratios, including on unread entries.
/// No entry is ever extracted to disk.
/// </summary>
internal static class FabricZipStructure
{
    internal const int MaxEntries = 16_384;
    internal const long MaxArchiveBytes = 512L * 1024 * 1024;
    internal const int MaxCompressionRatio = 200;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal sealed record Entry(string Name, uint Crc, uint CompressedSize, uint Size, uint Offset, ushort Flags, ushort Method, byte[] NameBytes);

    internal static IReadOnlyDictionary<string, Entry> Validate(Stream stream, ref int cumulativeEntries, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (stream.Length is < 22 or > MaxArchiveBytes) Fail("ArchiveSize", "JAR size is outside the ZIP inspection limit.");
        var tailSize = (int)Math.Min(stream.Length, 65_557);
        var tail = Read(stream, stream.Length - tailSize, tailSize, ct);
        var eocd = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
        {
            if (U32(tail, i) != 0x06054b50 || i + 22 + U16(tail, i + 20) != tail.Length) continue;
            if (eocd != -1) Fail("AmbiguousZip", "Multiple end-of-directory records are unsupported.");
            eocd = i;
        }
        if (eocd < 0) Fail("InvalidZip", "Missing ZIP directory or trailing data.");
        var endOffset = stream.Length - tailSize + eocd;
        var entryCount = U16(tail, eocd + 10);
        var centralSize = U32(tail, eocd + 12);
        var centralOffset = U32(tail, eocd + 16);
        if (U16(tail, eocd + 4) != 0 || U16(tail, eocd + 6) != 0 || U16(tail, eocd + 8) != entryCount)
            Fail("UnsupportedZip", "Multi-disk ZIP archives are unsupported.");
        if (entryCount == ushort.MaxValue || centralSize == uint.MaxValue || centralOffset == uint.MaxValue)
            Fail("UnsupportedZip", "ZIP64 archives are unsupported.");
        if ((long)centralOffset + centralSize != endOffset) Fail("InvalidZip", "ZIP central directory boundaries are inconsistent.");
        cumulativeEntries += entryCount;
        if (entryCount == 0 || cumulativeEntries > MaxEntries) Fail("EntryLimit", "JAR tree exceeds the cumulative archive entry limit.");
        Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
        HashSet<string> portableNames = new(StringComparer.OrdinalIgnoreCase);
        long cursor = centralOffset;
        long totalInflated = 0;
        for (var i = 0; i < entryCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var header = Read(stream, cursor, 46, ct);
            if (U32(header, 0) != 0x02014b50) Fail("InvalidZip", "Invalid ZIP central directory record.");
            var flags = U16(header, 8);
            var method = U16(header, 10);
            var compressed = U32(header, 20);
            var size = U32(header, 24);
            var nameLength = U16(header, 28);
            var extraLength = U16(header, 30);
            var commentLength = U16(header, 32);
            var offset = U32(header, 42);
            if (U16(header, 6) > 20 || compressed == uint.MaxValue || size == uint.MaxValue || offset == uint.MaxValue || U16(header, 34) != 0)
                Fail("UnsupportedZip", "ZIP64 or an unsupported ZIP feature is present.");
            if ((flags & ~0x080e) != 0 || method is not (0 or 8) || (method == 0 && (flags & 14) != 0))
                Fail("UnsupportedZip", "Encrypted or unsupported ZIP compression/flags are present.");
            if (((U32(header, 38) >> 16) & 0xf000) == 0xa000) Fail("UnsafePath", "Symbolic link entries are unsupported.");
            if (nameLength is 0 or > 1024 || cursor + 46L + nameLength + extraLength + commentLength > endOffset)
                Fail("InvalidZip", "Invalid ZIP name or directory length.");
            var nameBytes = Read(stream, cursor + 46, nameLength, ct);
            if ((flags & 0x800) == 0 && nameBytes.Any(x => x >= 128)) Fail("UnsupportedZip", "Non-UTF8 ambiguous ZIP names are unsupported.");
            var name = StrictUtf8.GetString(nameBytes);
            ValidatePath(name);
            if (!portableNames.Add(name)) Fail("DuplicateEntry", "Duplicate or case-colliding ZIP entry names are unsupported.");
            ValidateExtra(Read(stream, cursor + 46 + nameLength, extraLength, ct));
            if (size > MaxArchiveBytes || (compressed == 0 && size != 0) || (long)size > (long)compressed * MaxCompressionRatio ||
                (method == 0 && size != compressed)) Fail("CompressionLimit", "Entry exceeds the 200:1 compression ratio or declared size policy.");
            totalInflated += size;
            if (totalInflated > 2L * 1024 * 1024 * 1024) Fail("CompressionLimit", "Archive declared contents exceed the 2 GiB aggregate limit.");
            entries.Add(name, new(name, U32(header, 16), compressed, size, offset, flags, method, nameBytes));
            cursor += 46L + nameLength + extraLength + commentLength;
        }
        if (cursor != endOffset) Fail("InvalidZip", "Unexpected data after the ZIP directory entries.");
        var files = entries.Keys.Where(x => !x.EndsWith('/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in entries.Keys)
        {
            var parts = name.TrimEnd('/').Split('/');
            for (var i = 1; i < parts.Length; i++)
                if (files.Contains(string.Join('/', parts.Take(i)))) Fail("UnsafePath", "An archive file also acts as a directory prefix.");
            if (name.EndsWith('/') && files.Contains(name.TrimEnd('/'))) Fail("UnsafePath", "An archive path is both a file and a directory.");
        }
        long expectedOffset = 0;
        foreach (var entry in entries.Values.OrderBy(x => x.Offset))
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Offset != expectedOffset) Fail("InvalidZip", "ZIP entry overlap, preamble or unreferenced local entry detected.");
            var local = Read(stream, entry.Offset, 30, ct);
            if (U32(local, 0) != 0x04034b50 || U16(local, 4) > 20 || U16(local, 6) != entry.Flags || U16(local, 8) != entry.Method)
                Fail("InvalidZip", "Local and central ZIP headers disagree.");
            var nameLength = U16(local, 26);
            var extraLength = U16(local, 28);
            if (nameLength != entry.NameBytes.Length || !Read(stream, entry.Offset + 30L, nameLength, ct).SequenceEqual(entry.NameBytes))
                Fail("InvalidZip", "Local and central ZIP names disagree.");
            ValidateExtra(Read(stream, entry.Offset + 30L + nameLength, extraLength, ct));
            var descriptor = (entry.Flags & 8) != 0;
            if (!HeaderValueMatches(U32(local, 14), entry.Crc, descriptor) ||
                !HeaderValueMatches(U32(local, 18), entry.CompressedSize, descriptor) || !HeaderValueMatches(U32(local, 22), entry.Size, descriptor))
                Fail("InvalidZip", "Local and central ZIP lengths or CRC disagree.");
            expectedOffset = entry.Offset + 30L + nameLength + extraLength + entry.CompressedSize;
            if (expectedOffset > centralOffset) Fail("InvalidZip", "ZIP compressed data exceeds its directory boundary.");
            if (descriptor)
            {
                var data = Read(stream, expectedOffset, 12, ct);
                var signed = U32(data, 0) == 0x08074b50;
                if (signed) data = Read(stream, expectedOffset + 4, 12, ct);
                if (U32(data, 0) != entry.Crc || U32(data, 4) != entry.CompressedSize || U32(data, 8) != entry.Size)
                    Fail("InvalidZip", "ZIP data descriptor is inconsistent.");
                expectedOffset += signed ? 16 : 12;
            }
        }
        if (expectedOffset != centralOffset) Fail("InvalidZip", "ZIP data and central directory are not contiguous.");
        return entries;
    }

    internal static void ValidatePath(string name)
    {
        if (name.Length is 0 or > 1024 || name[0] == '/' || name.Contains('\\') || name.Contains(':') || name.Contains("//", StringComparison.Ordinal) || name.Any(char.IsControl))
            Fail("UnsafePath", "Unsafe archive entry path.");
        var parts = name.TrimEnd('/').Split('/');
        if (parts.Any(x => x is "" or "." or ".." || x.EndsWith(' ') || x.EndsWith('.')))
            Fail("UnsafePath", "Unsafe or ambiguous archive path segments.");
    }

    private static void ValidateExtra(byte[] extra)
    {
        var offset = 0;
        while (offset < extra.Length)
        {
            if (offset + 4 > extra.Length) Fail("InvalidZip", "Malformed ZIP extra field.");
            var id = U16(extra, offset);
            var size = U16(extra, offset + 2);
            if (id is 0x0001 or 0x0017 or 0x9901 or 0x7075 or 0x6375)
                Fail("UnsupportedZip", "ZIP64, encryption or name-override extra fields are unsupported.");
            offset += 4 + size;
            if (offset > extra.Length) Fail("InvalidZip", "Malformed ZIP extra field length.");
        }
    }

    private static bool HeaderValueMatches(uint local, uint central, bool descriptor) => local == central || (descriptor && local == 0);
    private static byte[] Read(Stream stream, long offset, int count, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (offset < 0 || offset > stream.Length - count) Fail("InvalidZip", "Truncated ZIP record.");
        stream.Position = offset;
        byte[] bytes = new byte[count];
        stream.ReadExactly(bytes);
        return bytes;
    }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    internal static void Fail(string code, string message) => throw new FabricInspectionException(code, message);
}

internal sealed class FabricInspectionException(string code, string message) : Exception(message)
{
    internal string Code { get; } = code;
}
