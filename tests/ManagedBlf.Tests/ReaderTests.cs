using Xunit;

namespace ManagedBlf.Tests;

public class ReaderTests
{
    private static BlfReader Reader(byte[] b, BlfReaderOptions? options = null) => new(new MemoryStream(b), options: options);
    [Fact] public void EmptyIsRepeatableEof()
    {
        using var r = Reader(Bytes.File()); Assert.False(r.ReadNext(out _)); Assert.False(r.SkipObject()); Assert.Equal(0UL, r.ObjectsRead);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(143)]
    public void TruncatedFileHeaderThrows(int length) => Assert.Throws<InvalidDataException>(() => Reader(Bytes.File()[..length]));
    [Fact] public void BadFileSignatureThrows()
    {
        var b = Bytes.File(); b[0] = 0; Assert.Throws<InvalidDataException>(() => Reader(b));
    }
    [Theory] [InlineData(1)] [InlineData(15)] [InlineData(31)]
    public void TruncatedObjectThrowsAndStaysFaulted(int length)
    {
        using var r = Reader(Bytes.File(Bytes.Object()[..length]));
        Assert.Throws<InvalidDataException>(() => r.ReadNext(out _)); Assert.Throws<InvalidDataException>(() => r.SkipObject());
    }
    [Fact] public void BadObjectSignatureThrows()
    {
        var b = Bytes.Object(); b[0] = 0; using var r = Reader(Bytes.File(b)); Assert.Throws<InvalidDataException>(() => r.ReadNext(out _));
    }
    [Theory] [InlineData(0)] [InlineData(2)]
    public void ContainersAndDirectObjectsPreserveBytes(int compression)
    {
        var obj = Bytes.Object(body: new byte[] { 9, 8, 7 });
        using var r = Reader(Bytes.File(Bytes.Container(obj[..17], (ushort)compression), Bytes.Container(obj[17..], 0), obj));
        for (int i = 0; i < 2; i++) { Assert.True(r.ReadNext(out var value)); Assert.Equal(obj, value!.RawData.ToArray()); Assert.False(BlfMessageDecoder.TryDecode(value, out _)); }
        Assert.False(r.ReadNext(out _)); Assert.Equal(2UL, r.ObjectsRead);
    }
    [Theory] [InlineData(1, 1, 70000UL)] [InlineData(1, 2, 7UL)] [InlineData(2, 1, 70000UL)] [InlineData(2, 2, 7UL)]
    public void TimestampUnits(int version, int flags, ulong expected)
    {
        using var r = Reader(Bytes.File(Bytes.Object(version: (ushort)version, flags: (uint)flags, time: 7)));
        Assert.True(r.PeekTimestamp(out var ns)); Assert.Equal(expected, ns); Assert.True(r.ReadNext(out var v)); Assert.Equal(expected, v!.TimestampNanoseconds);
    }
    [Theory] [InlineData(3, 2)] [InlineData(1, 0)]
    public void UnknownTimeRemainsRaw(int version, int flags)
    {
        var b = Bytes.Object(version: (ushort)version, flags: (uint)flags); using var r = Reader(Bytes.File(b));
        Assert.False(r.PeekTimestamp(out _)); Assert.True(r.ReadNext(out var v)); Assert.Null(v!.TimestampNanoseconds); Assert.Equal(b, v.RawData.ToArray());
    }
    [Fact] public void TimestampOverflowFaults()
    {
        using var r = Reader(Bytes.File(Bytes.Object(flags: 1, time: ulong.MaxValue))); Assert.Throws<InvalidDataException>(() => r.ReadNext(out _)); Assert.Throws<InvalidDataException>(() => r.ReadNext(out _));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(31)] [InlineData(32)] [InlineData(40)]
    public void SpanCopyIsBoundedAndConsumesWholeObject(int size)
    {
        var obj = Bytes.Object(); using var r = Reader(Bytes.File(obj, obj));
        var target = Enumerable.Repeat((byte)0xCC, size + 2).ToArray(); Assert.True(r.ReadObject(target.AsSpan(1, size)));
        Assert.Equal(0xCC, target[0]); Assert.Equal(0xCC, target[^1]); Assert.Equal(obj.Take(size), target.Skip(1).Take(Math.Min(size, obj.Length)));
        Assert.All(target.Skip(1 + Math.Min(size, obj.Length)).Take(Math.Max(0, size - obj.Length)), v => Assert.Equal(0, v));
        Assert.True(r.SkipObject()); Assert.False(r.SkipObject());
    }
    [Fact] public void PeekAndDisposeOwnership()
    {
        var s = new MemoryStream(Bytes.File(Bytes.Object())); var r = new BlfReader(s, leaveOpen: true);
        Assert.True(r.PeekObject(out var a)); Assert.True(r.PeekObject(out var b)); Assert.Equal(a, b); Assert.Equal(0UL, r.ObjectsRead);
        r.Dispose(); r.Dispose(); Assert.True(s.CanRead); Assert.Throws<ObjectDisposedException>(() => r.SkipObject()); s.Dispose();
        var owned = new MemoryStream(Bytes.File()); new BlfReader(owned).Dispose(); Assert.False(owned.CanRead);
    }
    [Theory] [InlineData(0)] [InlineData(2)]
    public void ExpandedLengthMismatchThrows(int compression)
    {
        foreach (uint size in new uint[] { 31, 33 }) { using var r = Reader(Bytes.File(Bytes.Container(Bytes.Object(), (ushort)compression, size))); Assert.Throws<InvalidDataException>(() => r.ReadNext(out _)); }
    }
    [Theory] [InlineData(0, 0)] [InlineData(0, 0x88)] [InlineData(1, 0)] [InlineData(1, 0x20)] [InlineData(42, 0)]
    public void CorruptZlibThrows(int offset, int value)
    {
        var b = Bytes.Container(Bytes.Object(), 2); b[32 + offset] = (byte)value;
        using var r = Reader(Bytes.File(b)); Assert.Throws<InvalidDataException>(() => r.ReadNext(out _)); Assert.Throws<InvalidDataException>(() => r.PeekObject(out _));
    }
    [Fact] public void UnsupportedCompressionThrows()
    {
        using var r = Reader(Bytes.File(Bytes.Container(Bytes.Object(), 3))); Assert.Throws<InvalidDataException>(() => r.SkipObject());
    }
    [Fact] public void ObjectAndExpandedLimitsThrow()
    {
        var limits = new BlfReaderOptions { MaxObjectSize = 40, MaxContainerSize = 80, MaxBufferedBytes = 80 };
        using var r = Reader(Bytes.File(Bytes.Object(body: new byte[9])), limits); Assert.Throws<InvalidDataException>(() => r.SkipObject());
        using var z = Reader(Bytes.File(Bytes.Container(new byte[81], 2)), limits); Assert.Throws<InvalidDataException>(() => z.SkipObject());
    }
    [Fact] public void BufferLimitRejectsPendingPlusNewContainer()
    {
        var b = Bytes.Object(body: new byte[48]);
        var limits = new BlfReaderOptions { MaxObjectSize = 80, MaxContainerSize = 80, MaxBufferedBytes = 80 };
        using var r = Reader(Bytes.File(Bytes.Container(b[..40]), Bytes.Container(b[40..].Concat(new byte[8]).ToArray(), 2)), limits);
        Assert.Throws<InvalidDataException>(() => r.ReadNext(out _));
    }
    [Theory] [InlineData(1)] [InlineData(2)]
    public void MinimumVersionHeaderIsEnforced(int version)
    {
        var obj = Bytes.Object(version: (ushort)version); Bytes.U16(obj, 4, (ushort)(version == 1 ? 31 : 39));
        using var r = Reader(Bytes.File(obj)); Assert.Throws<InvalidDataException>(() => r.SkipObject());
    }
    [Fact] public void TruncatedZlibCannotInventBytes()
    {
        var b = Bytes.Container(Bytes.Object(), 2); uint size = 32 + 43;
        var truncated = b[..(int)(size - 5)]; Bytes.U32(truncated, 8, (uint)truncated.Length);
        using var r = Reader(Bytes.File(truncated)); Assert.Throws<InvalidDataException>(() => r.ReadNext(out _));
    }
    [Fact] public void PresetDictionaryIsRejectedEvenWithValidFcheck()
    {
        var b = Bytes.Container(Bytes.Object(), 2); b[33] = 0x20;
        while (((b[32] << 8) | b[33]) % 31 != 0) b[33]++;
        using var r = Reader(Bytes.File(b)); Assert.Throws<InvalidDataException>(() => r.SkipObject());
    }
    [Fact] public void IoErrorsPropagateAndReaderStaysFaulted()
    {
        using var stream = new FailingStream(Bytes.File(Bytes.Object())); using var r = new BlfReader(stream);
        stream.Fail = true; Assert.Throws<IOException>(() => r.ReadNext(out _)); stream.Fail = false;
        Assert.Throws<IOException>(() => r.ReadNext(out _));
    }
    private sealed class FailingStream(byte[] data) : MemoryStream(data)
    {
        public bool Fail { get; set; }
        public override int Read(Span<byte> buffer) => Fail ? throw new IOException("synthetic I/O failure") : base.Read(buffer);
    }
    [Fact] public void HandleLifecycleAndExactReadAccess()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "中文.blf"); File.WriteAllBytes(path, Bytes.File(Bytes.Object(), Bytes.Object()));
        IntPtr handle = ManagedBinlog.INVALID_HANDLE_VALUE;
        try
        {
            Assert.Equal(handle, ManagedBinlog.BLCreateFile(path, ManagedBinlog.GENERIC_READ | 1, out var bad)); Assert.IsType<NotSupportedException>(bad);
            handle = ManagedBinlog.BLCreateFile(path, ManagedBinlog.GENERIC_READ, out var error); Assert.Null(error); Assert.NotEqual(ManagedBinlog.INVALID_HANDLE_VALUE, handle);
            Assert.True(ManagedBinlog.BLPeekObject(handle, out _)); Assert.True(ManagedBinlog.BLPeekTimestamp(handle, out var ns)); Assert.Equal(123UL, ns);
            Assert.True(ManagedBinlog.BLReadObjectSecure(handle, new byte[1])); Assert.True(ManagedBinlog.BLSkipObject(handle)); Assert.False(ManagedBinlog.BLSkipObject(handle));
            Assert.True(ManagedBinlog.BLGetFileStatisticsEx(handle, out var stats)); Assert.Equal(2UL, stats!.ObjectsRead);
            Assert.Equal(1, ManagedBinlog.BLCloseHandle(handle)); Assert.Equal(0, ManagedBinlog.BLCloseHandle(handle)); Assert.False(ManagedBinlog.BLPeekObject(handle, out _)); Assert.False(ManagedBinlog.BLReadObjectSecure(handle, new byte[1]));
        }
        finally { ManagedBinlog.BLCloseHandle(handle); File.Delete(path); }
    }
}
