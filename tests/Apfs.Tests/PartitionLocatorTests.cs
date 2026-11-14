using System.Buffers.Binary;
using Apfs;
using Apfs.IO;
using Apfs.Partitions;

namespace Apfs.Tests;

public class PartitionLocatorTests(TestImages images) : IClassFixture<TestImages>
{
    private const int SectorSize = 512;
    private const int HeaderOffset = SectorSize;
    private const int EntriesOffset = 2 * SectorSize;

    [Fact]
    public void InvalidPrimaryHeaderCrcIsRejected()
    {
        string path = CopyGptImage();
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
            {
                stream.Position = HeaderOffset + 24;
                stream.WriteByte(0x02);
            }

            using var device = new FileImageDevice(path);
            var error = Assert.Throws<ApfsException>(() => PartitionLocator.FindApfsPartitions(device));
            Assert.Equal(ApfsErrorCode.Corrupt, error.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidPartitionArrayCrcIsRejected()
    {
        string path = CopyGptImage();
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
            {
                stream.Position = EntriesOffset + 56;
                stream.WriteByte(0x7F);
            }

            using var device = new FileImageDevice(path);
            var error = Assert.Throws<ApfsException>(() => PartitionLocator.FindApfsPartitions(device));
            Assert.Equal(ApfsErrorCode.Corrupt, error.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PartitionLbaThatWouldWrapOffsetIsIgnored()
    {
        string path = CopyGptImage();
        try
        {
            var image = File.ReadAllBytes(path);
            ulong wrappingLba = 1UL << 55;
            BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(EntriesOffset + 32), wrappingLba);
            BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(EntriesOffset + 40), wrappingLba);
            UpdatePrimaryGptCrcs(image);
            File.WriteAllBytes(path, image);

            using var device = new FileImageDevice(path);
            Assert.Empty(PartitionLocator.FindApfsPartitions(device));
        }
        finally { File.Delete(path); }
    }

    private string CopyGptImage()
    {
        string source = images.Get("basic", gpt: true).Path;
        string copy = Path.Combine(Path.GetTempPath(), $"gpt-validacion-{Guid.NewGuid():N}.img");
        File.Copy(source, copy);
        return copy;
    }

    private static void UpdatePrimaryGptCrcs(byte[] image)
    {
        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(HeaderOffset + 12));
        ulong entriesLba = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(HeaderOffset + 72));
        uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(HeaderOffset + 80));
        uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(HeaderOffset + 84));
        int entriesOffset = checked((int)(entriesLba * SectorSize));
        int entriesLength = checked((int)(entryCount * entrySize));

        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(HeaderOffset + 88), ComputeCrc32(image.AsSpan(entriesOffset, entriesLength)));
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(HeaderOffset + 16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(HeaderOffset + 16), ComputeCrc32(image.AsSpan(HeaderOffset, headerSize)));
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }
}
