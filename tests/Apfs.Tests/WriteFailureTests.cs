using Apfs;
using Apfs.IO;
using Apfs.Mount;
using Apfs.Volume;
using Apfs.Write;

namespace Apfs.Tests;

public class WriteFailureTests(LinuxFixtures fixtures) : IClassFixture<LinuxFixtures>
{
    private sealed class FlushFaultDevice(IWritableBlockDevice inner) : IWritableBlockDevice
    {
        public bool FailNextFlush { get; set; }
        public long Length => inner.Length;
        public int SectorSize => inner.SectorSize;
        public void Read(long offset, Span<byte> buffer) => inner.Read(offset, buffer);
        public void Write(long offset, ReadOnlySpan<byte> data) => inner.Write(offset, data);
        public void Flush()
        {
            if (FailNextFlush)
            {
                FailNextFlush = false;
                throw new ApfsException(ApfsErrorCode.IoError, "fallo de barrera simulado");
            }
            inner.Flush();
        }
        public void Dispose() => inner.Dispose();
    }

    [Fact]
    public void FailedCommitIsReportedAndSessionCannotContinue()
    {
        var (path, grant) = FsOpsTests.Prepare(fixtures, "mk-ci-64m");
        ulong startXid;
        using (var c = TestImages.Open(path)) startXid = c.Xid;
        var errors = new List<string>();

        using (var raw = new FileImageWriter(path, grant))
        using (var device = new FlushFaultDevice(raw))
        using (var session = WriteSession.Open(device, grant))
        using (var fs = new ApfsWritableFileSystem(session, 0, TimeSpan.FromHours(1)))
        {
            fs.Error += errors.Add;
            session.Volume(0).CreateFile(Inode.RootDirId, "commit-pendiente.txt");
            device.FailNextFlush = true;

            int firstFlush = fs.Flush(new object(), new object(), out _);
            int secondFlush = fs.Flush(new object(), new object(), out _);
            int laterCreate = fs.Create("\\tras-el-fallo.txt", 0, 0, 0, [], 0,
                out _, out _, out _, out _);

            Assert.NotEqual(0, firstFlush);
            Assert.Equal(firstFlush, secondFlush);
            Assert.NotEqual(0, laterCreate);
            Assert.NotEmpty(errors);
            Assert.Throws<ApfsException>(() => session.Close());
        }

        using var recovered = TestImages.Open(path);
        Assert.Equal(startXid, recovered.Xid);
        Assert.True(Apfs.Diagnostics.FullCheck.Run(recovered).IsClean);
    }
}
