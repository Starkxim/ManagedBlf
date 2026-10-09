using System.Buffers.Binary;
using Xunit;

namespace ManagedBlf.Tests;

public class WriterTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 3, 4, 5, 6, DateTimeKind.Unspecified);

    private static BlfCanFrame Frame(long time = 0) => new()
    {
        TimestampNanoseconds = time, Channel = 2, Identifier = 0x123, Dlc = 3,
        Data = new byte[] { 0x11, 0x22, 0x33 }, IsTransmit = true
    };

    [Fact]
    public void EmptyFileCompletesWithoutAnEmptyContainer()
    {
        using var stream = new MemoryStream();
        using var writer = new BlfWriter(stream, Start, leaveOpen: true);
        Assert.False(writer.IsCompleted);
        writer.Complete();
        Assert.True(writer.IsCompleted);
        Assert.Equal(0U, writer.ObjectsWritten);
        Assert.Equal(144, stream.Length);
        stream.Position = 0;
        using var reader = new BlfReader(stream, leaveOpen: true);
        Assert.Equal(144UL, reader.Header.FileSize);
        Assert.Equal(144UL, reader.Header.UncompressedFileSize);
        Assert.Equal(0U, reader.Header.ObjectCount);
        Assert.Equal(Start, reader.Header.MeasurementStartTime.ToDateTime());
        Assert.Equal(Start, reader.Header.LastObjectTime.ToDateTime());
        Assert.False(reader.ReadNext(out _));
    }

    [Fact]
    public void SingleCanFileMatchesIndependentLiteralBytes()
    {
        // Complete 224-byte fixture, hand-specified from the public LOGG/LOBJ field layouts.
        // No reader constants, production writer helpers, or fixture generators build this oracle.
        // Wall clock: 2026-01-02 Friday 03:04:05.006; final millisecond: .007.
        // CAN: channel 2, TX, DLC 3, standard ID 0x123, 1,234,567 ns, 11 22 33.
        byte[] expected = Convert.FromHexString(
            "4C4F4747900000000000000000000000" +
            "E000000000000000E000000000000000" +
            "0100000000000000EA07010005000200" +
            "0300040005000600EA07010005000200" +
            "03000400050007000000000000000000" +
            "00000000000000000000000000000000" +
            "00000000000000000000000000000000" +
            "00000000000000000000000000000000" +
            "00000000000000000000000000000000" +
            "4C4F424A10000100500000000A000000" +
            "00000000000000003000000000000000" +
            "4C4F424A200001003000000001000000" +
            "020000000000000087D6120000000000" +
            "02000103230100001122330000000000");
        using var stream = new MemoryStream();
        using (var writer = new BlfWriter(stream, Start, leaveOpen: true))
        {
            writer.WriteCanMessage(Frame(1_234_567));
            writer.Complete();
        }
        Assert.Equal(224, expected.Length);
        Assert.Equal(expected, stream.ToArray());
    }

    private static IEnumerable<BlfCanFrame> ValidFrames()
    {
        yield return new BlfCanFrame { Channel = 1, Identifier = 0, Dlc = 0 };
        yield return new BlfCanFrame { Channel = 65535, Identifier = 0x7FF, Dlc = 8, Data = new byte[] { 0, 1, 2, 3, 4, 5, 6, 255 } };
        yield return new BlfCanFrame { Channel = 7, Identifier = 0x42, IsExtended = true, Dlc = 1, Data = new byte[] { 0xAB } };
        yield return new BlfCanFrame { Channel = 2, Identifier = 0x1FFFFFFF, IsExtended = true, IsTransmit = true, Dlc = 8, Data = new byte[8] };
        yield return new BlfCanFrame { Channel = 3, Identifier = 0x456, IsRemote = true, Dlc = 8 };
        yield return new BlfCanFrame { Channel = 1, Identifier = 0x42, IsExtended = true, IsRemote = true, IsTransmit = true, Dlc = 0 };
        yield return new BlfCanFrame { TimestampNanoseconds = long.MaxValue, Channel = 1, Identifier = 0x123, Dlc = 1, Data = new byte[] { 0xFF } };
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void TypedCanRoundTripPreservesAllSupportedFields(int scenario)
    {
        var frame = ValidFrames().ElementAt(scenario);
        using var stream = new MemoryStream();
        using (var writer = new BlfWriter(stream, Start, leaveOpen: true))
        {
            writer.WriteCanMessage(frame);
            Assert.Equal(1U, writer.ObjectsWritten);
        }
        stream.Position = 0;
        using var reader = new BlfReader(stream, leaveOpen: true);
        Assert.True(reader.ReadNext(out var value));
        Assert.Equal(1U, value!.Header.ObjectType);
        Assert.Equal((ushort)32, value.Header.HeaderSize);
        Assert.Equal((ushort)1, value.Header.HeaderVersion);
        Assert.Equal((ulong)frame.TimestampNanoseconds, value.TimestampNanoseconds);
        Assert.True(BlfMessageDecoder.TryDecode(value, out var decoded));
        Assert.Equal((ushort)frame.Channel, decoded!.Channel);
        Assert.Equal(frame.Identifier, decoded.Identifier);
        Assert.Equal((byte)frame.Dlc, decoded.Dlc);
        Assert.Equal(frame.IsExtended, decoded.IsExtended);
        Assert.Equal(frame.IsRemote, decoded.IsRemote);
        Assert.Equal(frame.IsTransmit, decoded.IsTransmit);
        Assert.Equal(frame.Data.ToArray(), decoded.Data.ToArray());
        Assert.False(reader.ReadNext(out _));
    }

    [Theory]
    [InlineData(48, 5)]
    [InlineData(49, 5)]
    [InlineData(96, 3)]
    [InlineData(97, 3)]
    [InlineData(240, 1)]
    public void ContainersContainOnlyWholeObjectsAndRespectTheirDataLimit(int limit, int containers)
    {
        using var stream = new MemoryStream();
        using (var writer = new BlfWriter(stream, Start, leaveOpen: true,
                   options: new BlfWriterOptions { MaxContainerDataSize = limit }))
        {
            for (int i = 0; i < 5; i++) writer.WriteCanMessage(Frame(i * 1_000_000));
        }
        byte[] bytes = stream.ToArray();
        int offset = 144, seen = 0, objects = 0;
        while (offset < bytes.Length)
        {
            Assert.Equal("LOBJ"u8.ToArray(), bytes.AsSpan(offset, 4).ToArray());
            Assert.Equal(16, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 4)));
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 6)));
            Assert.Equal(10U, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 12)));
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 16)));
            uint objectSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 8));
            uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 24));
            Assert.InRange(payloadSize, 48U, (uint)limit);
            Assert.Equal(0U, payloadSize % 48);
            Assert.Equal(payloadSize + 32, objectSize);
            objects += checked((int)payloadSize / 48);
            offset += checked((int)objectSize);
            seen++;
        }
        Assert.Equal(bytes.Length, offset);
        Assert.Equal(containers, seen);
        Assert.Equal(5, objects);
        Assert.Equal(144 + 5 * 48 + containers * 32, bytes.Length);
        stream.Position = 0;
        using var reader = new BlfReader(stream, leaveOpen: true);
        Assert.Equal((ulong)bytes.Length, reader.Header.FileSize);
        Assert.Equal((ulong)bytes.Length, reader.Header.UncompressedFileSize);
        Assert.Equal(5U, reader.Header.ObjectCount);
        for (int i = 0; i < 5; i++)
        {
            Assert.True(reader.ReadNext(out var value));
            Assert.Equal((ulong)(i * 1_000_000), value!.TimestampNanoseconds);
        }
        Assert.False(reader.ReadNext(out _));
    }

    [Fact]
    public void EndMetadataUsesLastWrittenTimestampWithoutReorderingObjects()
    {
        using var stream = new MemoryStream();
        using (var writer = new BlfWriter(stream, Start, leaveOpen: true))
        {
            writer.WriteCanMessage(Frame(3_999_999));
            writer.WriteCanMessage(Frame(1));
        }
        stream.Position = 0;
        using var reader = new BlfReader(stream, leaveOpen: true);
        Assert.Equal(Start, reader.Header.MeasurementStartTime.ToDateTime());
        Assert.Equal(Start, reader.Header.LastObjectTime.ToDateTime());
        Assert.True(reader.ReadNext(out var first));
        Assert.Equal(3_999_999UL, first!.TimestampNanoseconds);
        Assert.True(reader.ReadNext(out var second));
        Assert.Equal(1UL, second!.TimestampNanoseconds);
    }

    [Fact]
    public void EndSystemTimeTracksCalendarRolloverAndDayOfWeek()
    {
        var start = new DateTime(2026, 12, 31, 23, 59, 59, 999, DateTimeKind.Unspecified);
        using var stream = new MemoryStream();
        using (var writer = new BlfWriter(stream, start, leaveOpen: true)) writer.WriteCanMessage(Frame(2_000_000));
        stream.Position = 0;
        using var reader = new BlfReader(stream, leaveOpen: true);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, 1, DateTimeKind.Unspecified), reader.Header.LastObjectTime.ToDateTime());
        Assert.Equal((ushort)5, reader.Header.LastObjectTime.DayOfWeek);
    }

    [Fact]
    public void WriterCopiesPayloadBeforeReturningToCaller()
    {
        byte[] payload = [1, 2, 3];
        using var stream = new MemoryStream();
        using (var writer = new BlfWriter(stream, Start, leaveOpen: true))
        {
            writer.WriteCanMessage(Frame() with { Data = payload });
            Array.Fill(payload, (byte)0xFF);
        }
        stream.Position = 0;
        using var reader = new BlfReader(stream, leaveOpen: true);
        Assert.True(reader.ReadNext(out var value));
        Assert.True(BlfMessageDecoder.TryDecode(value!, out var decoded));
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded!.Data.ToArray());
    }

    private static IEnumerable<BlfCanFrame> InvalidFrames()
    {
        yield return Frame() with { TimestampNanoseconds = -1 };
        yield return Frame() with { Channel = -1 };
        yield return Frame() with { Channel = 0 };
        yield return Frame() with { Channel = 65536 };
        yield return Frame() with { Identifier = 0x800 };
        yield return Frame() with { Identifier = 0x20000000, IsExtended = true };
        yield return Frame() with { Identifier = 0x80000000, IsExtended = true };
        yield return Frame() with { Dlc = -1 };
        yield return Frame() with { Dlc = 9 };
        yield return Frame() with { Dlc = 0 };
        yield return Frame() with { Dlc = 8 };
        yield return Frame() with { Data = ReadOnlyMemory<byte>.Empty };
        yield return Frame() with { IsRemote = true };
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void InvalidFrameDoesNotWriteOrFaultTheWriter(int scenario)
    {
        var invalid = InvalidFrames().ElementAt(scenario);
        using var stream = new MemoryStream();
        using var writer = new BlfWriter(stream, Start, leaveOpen: true,
            options: new BlfWriterOptions { MaxContainerDataSize = 48 });
        writer.WriteCanMessage(Frame());
        byte[] before = stream.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => writer.WriteCanMessage(invalid));
        Assert.Equal(before, stream.ToArray());
        Assert.Equal(1U, writer.ObjectsWritten);
        writer.WriteCanMessage(Frame(1));
        writer.Complete();
        Assert.Equal(2U, writer.ObjectsWritten);
        Assert.True(writer.IsCompleted);
    }

    [Fact]
    public void NullFrameDoesNotFaultWriter()
    {
        using var stream = new MemoryStream();
        using var writer = new BlfWriter(stream, Start, leaveOpen: true);
        Assert.Throws<ArgumentNullException>(() => writer.WriteCanMessage(null!));
        writer.WriteCanMessage(Frame());
        writer.Complete();
        Assert.Equal(1U, writer.ObjectsWritten);
    }

    [Fact]
    public void WallClockOverflowIsRejectedBeforeBufferMutation()
    {
        var start = new DateTime(9999, 12, 31, 23, 59, 59, 999, DateTimeKind.Unspecified);
        using var stream = new MemoryStream();
        using var writer = new BlfWriter(stream, start, leaveOpen: true);
        Assert.ThrowsAny<ArgumentException>(() => writer.WriteCanMessage(Frame(long.MaxValue)));
        Assert.ThrowsAny<ArgumentException>(() => writer.WriteCanMessage(Frame(999_901)));
        Assert.Equal(0U, writer.ObjectsWritten);
        writer.WriteCanMessage(Frame(999_900));
        writer.Complete();
        stream.Position = 0;
        using var reader = new BlfReader(stream, leaveOpen: true);
        Assert.True(reader.ReadNext(out var value));
        Assert.Equal(999_900UL, value!.TimestampNanoseconds);
        Assert.Equal(start, reader.Header.LastObjectTime.ToDateTime());
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(47)]
    [InlineData(67_108_833)] [InlineData(int.MaxValue)]
    public void InvalidContainerLimitsAreRejectedBeforeWriting(int limit)
    {
        using var stream = new MemoryStream();
        Assert.ThrowsAny<ArgumentException>(() => new BlfWriter(stream, Start,
            options: new BlfWriterOptions { MaxContainerDataSize = limit }));
        Assert.Empty(stream.ToArray());
    }

    [Fact]
    public void MaximumContainerLimitIsAccepted()
    {
        using var stream = new MemoryStream();
        using var writer = new BlfWriter(stream, Start, leaveOpen: true,
            options: new BlfWriterOptions { MaxContainerDataSize = 67_108_832 });
        writer.WriteCanMessage(Frame());
        writer.Complete();
        Assert.Equal(224, stream.Length);
    }

    public static IEnumerable<object[]> InvalidWallClocks()
    {
        yield return [DateTime.SpecifyKind(Start, DateTimeKind.Utc)];
        yield return [DateTime.SpecifyKind(Start, DateTimeKind.Local)];
        yield return [Start.AddTicks(1)];
        yield return [new DateTime(1600, 12, 31)];
        yield return [DateTime.MinValue];
    }

    [Theory]
    [MemberData(nameof(InvalidWallClocks))]
    public void InvalidMeasurementStartIsRejectedBeforeWriting(DateTime start)
    {
        using var stream = new MemoryStream();
        Assert.ThrowsAny<ArgumentException>(() => new BlfWriter(stream, start));
        Assert.Empty(stream.ToArray());
    }

    [Fact]
    public void NonemptyStreamIsPreserved()
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 1, 2, 3 });
        stream.Position = 0;
        Assert.ThrowsAny<ArgumentException>(() => new BlfWriter(stream, Start));
        Assert.Equal(new byte[] { 1, 2, 3 }, stream.ToArray());
    }

    [Fact]
    public void EmptyStreamMustStartAtPositionZero()
    {
        using var stream = new MemoryStream();
        stream.Position = 1;
        Assert.ThrowsAny<ArgumentException>(() => new BlfWriter(stream, Start));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void StreamMustBeWritableSeekableAndNonnull()
    {
        Assert.Throws<ArgumentNullException>(() => new BlfWriter(null!, Start));
        using var readOnly = new MemoryStream(Array.Empty<byte>(), writable: false);
        Assert.ThrowsAny<ArgumentException>(() => new BlfWriter(readOnly, Start));
        using var nonSeekable = new ProbeStream { SeekEnabled = false };
        Assert.ThrowsAny<ArgumentException>(() => new BlfWriter(nonSeekable, Start));
        Assert.Empty(nonSeekable.ToArray());
    }

    [Fact]
    public void CreateNeverOverwritesAnExistingFile()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "现有.blf");
        File.WriteAllBytes(path, new byte[] { 9, 8, 7 });
        try
        {
            Assert.ThrowsAny<IOException>(() => BlfWriter.Create(path, Start));
            Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CreateWritesAndClosesAUnicodeFilename()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "新建帧.blf");
        try
        {
            using (var writer = BlfWriter.Create(path, Start)) writer.WriteCanMessage(Frame(5));
            using (var reader = BlfReader.Open(path))
            {
                Assert.True(reader.ReadNext(out var value));
                Assert.Equal(5UL, value!.TimestampNanoseconds);
                Assert.False(reader.ReadNext(out _));
            }
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.Equal(224, exclusive.Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CompleteIsIdempotentAndPreventsFurtherWrites()
    {
        using var stream = new ProbeStream();
        using var writer = new BlfWriter(stream, Start, leaveOpen: true);
        writer.WriteCanMessage(Frame());
        writer.Complete();
        byte[] completed = stream.ToArray();
        int writes = stream.WriteCalls, flushes = stream.FlushCalls;
        writer.Complete();
        Assert.Equal(completed, stream.ToArray());
        Assert.Equal(writes, stream.WriteCalls);
        Assert.Equal(flushes, stream.FlushCalls);
        Assert.Throws<InvalidOperationException>(() => writer.WriteCanMessage(Frame()));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DisposeCompletesExactlyOnceAndHonorsStreamOwnership(bool leaveOpen)
    {
        var stream = new ProbeStream();
        var writer = new BlfWriter(stream, Start, leaveOpen: leaveOpen);
        writer.WriteCanMessage(Frame());
        writer.Dispose();
        Assert.True(writer.IsCompleted);
        Assert.Equal(1U, writer.ObjectsWritten);
        int writes = stream.WriteCalls;
        writer.Dispose();
        Assert.Equal(writes, stream.WriteCalls);
        Assert.Equal(!leaveOpen, stream.WasDisposed);
        Assert.Equal(224, stream.ToArray().Length);
        Assert.Throws<ObjectDisposedException>(() => writer.WriteCanMessage(Frame()));
        Assert.Throws<ObjectDisposedException>(() => writer.Complete());
        stream.Dispose();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ContainerWriteFailureRemainsFaultedAndDisposeDoesNotRetry(bool leaveOpen)
    {
        var stream = new ProbeStream();
        var writer = new BlfWriter(stream, Start, leaveOpen: leaveOpen,
            options: new BlfWriterOptions { MaxContainerDataSize = 48 });
        writer.WriteCanMessage(Frame());
        stream.FailWrites = true;
        Assert.Throws<IOException>(() => writer.WriteCanMessage(Frame(1)));
        Assert.False(writer.IsCompleted);
        Assert.Equal(1U, writer.ObjectsWritten);
        stream.FailWrites = false;
        int attempts = stream.WriteCalls;
        Assert.Throws<IOException>(() => writer.WriteCanMessage(Frame(2)));
        Assert.Throws<IOException>(() => writer.Complete());
        writer.Dispose();
        Assert.Equal(attempts, stream.WriteCalls);
        Assert.Equal(!leaveOpen, stream.WasDisposed);
        stream.Dispose();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PartialContainerPayloadFailureDoesNotRetryOrBackfill(bool leaveOpen)
    {
        var stream = new ProbeStream();
        var writer = new BlfWriter(stream, Start, leaveOpen: leaveOpen);
        writer.WriteCanMessage(Frame());
        stream.FailContainerPayload = true;
        Assert.Throws<IOException>(() => writer.Complete());
        Assert.Equal(183, stream.Length); // 144-byte LOGG + complete 32-byte container header + 7-byte fragment.
        byte[] partial = stream.ToArray();
        Assert.Equal(Convert.FromHexString("4C4F424A200001"), partial.AsSpan(176).ToArray());
        Assert.Equal(0UL, BinaryPrimitives.ReadUInt64LittleEndian(partial.AsSpan(16)));
        Assert.Equal(0U, BinaryPrimitives.ReadUInt32LittleEndian(partial.AsSpan(32)));
        Assert.False(writer.IsCompleted);
        Assert.Equal(1U, writer.ObjectsWritten);
        stream.FailContainerPayload = false;
        int attempts = stream.WriteCalls;
        Assert.Throws<IOException>(() => writer.WriteCanMessage(Frame(1)));
        Assert.Throws<IOException>(() => writer.Complete());
        writer.Dispose();
        Assert.Equal(attempts, stream.WriteCalls);
        Assert.Equal(partial, stream.ToArray());
        Assert.Equal(!leaveOpen, stream.WasDisposed);
        stream.Dispose();
    }

    [Theory]
    [InlineData("seek")] [InlineData("header")] [InlineData("flush")] [InlineData("restore-end")]
    public void CompletionFailuresPersistEvenIfTheStreamRecovers(string operation)
    {
        var stream = new ProbeStream();
        var writer = new BlfWriter(stream, Start, leaveOpen: true);
        writer.WriteCanMessage(Frame());
        stream.FailSeek = operation == "seek";
        stream.FailHeaderRewrite = operation == "header";
        stream.FailFlush = operation == "flush";
        stream.FailRestoreEnd = operation == "restore-end";
        Assert.Throws<IOException>(() => writer.Complete());
        Assert.False(writer.IsCompleted);
        stream.FailSeek = stream.FailHeaderRewrite = stream.FailFlush = stream.FailRestoreEnd = false;
        int writes = stream.WriteCalls, flushes = stream.FlushCalls;
        Assert.Throws<IOException>(() => writer.Complete());
        Assert.Throws<IOException>(() => writer.WriteCanMessage(Frame()));
        writer.Dispose();
        Assert.Equal(writes, stream.WriteCalls);
        Assert.Equal(flushes, stream.FlushCalls);
        Assert.False(stream.WasDisposed);
        stream.Dispose();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DisposeClosesOwnedStreamWhenAutomaticCompletionFails(bool leaveOpen)
    {
        var stream = new ProbeStream();
        var writer = new BlfWriter(stream, Start, leaveOpen: leaveOpen);
        writer.WriteCanMessage(Frame());
        stream.FailFlush = true;
        Assert.Throws<IOException>(() => writer.Dispose());
        Assert.False(writer.IsCompleted);
        Assert.Equal(!leaveOpen, stream.WasDisposed);
        int attempts = stream.WriteCalls;
        writer.Dispose();
        Assert.Equal(attempts, stream.WriteCalls);
        stream.Dispose();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InitialHeaderIoFailureDoesNotTakeOwnership(bool leaveOpen)
    {
        using var stream = new ProbeStream { FailWrites = true };
        Assert.Throws<IOException>(() => new BlfWriter(stream, Start, leaveOpen: leaveOpen));
        Assert.Empty(stream.ToArray());
        Assert.False(stream.WasDisposed);
    }

    private sealed class ProbeStream : MemoryStream
    {
        public bool SeekEnabled { get; set; } = true;
        public bool FailWrites { get; set; }
        public bool FailHeaderRewrite { get; set; }
        public bool FailContainerPayload { get; set; }
        public bool FailSeek { get; set; }
        public bool FailRestoreEnd { get; set; }
        public bool FailFlush { get; set; }
        public bool WasDisposed { get; private set; }
        public int WriteCalls { get; private set; }
        public int FlushCalls { get; private set; }
        public override bool CanSeek => SeekEnabled && base.CanSeek;
        public override long Position
        {
            get => base.Position;
            set
            {
                if (FailSeek) throw new IOException("synthetic seek failure");
                if (FailRestoreEnd && value == Length && Length > 144)
                    throw new IOException("synthetic restore-end seek failure");
                base.Position = value;
            }
        }
        public override long Seek(long offset, SeekOrigin loc)
        {
            if (FailSeek) throw new IOException("synthetic seek failure");
            return base.Seek(offset, loc);
        }
        private void BeforeWrite()
        {
            WriteCalls++;
            if (FailWrites || (FailHeaderRewrite && Position == 0 && Length != 0))
                throw new IOException("synthetic write failure");
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            BeforeWrite();
            WritePartialPayloadIfRequested(buffer);
            base.Write(buffer);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            BeforeWrite();
            WritePartialPayloadIfRequested(buffer.AsSpan(offset, count));
            base.Write(buffer, offset, count);
        }
        private void WritePartialPayloadIfRequested(ReadOnlySpan<byte> buffer)
        {
            if (!FailContainerPayload || Position < 176) return;
            byte[] fragment = buffer[..Math.Min(7, buffer.Length)].ToArray();
            base.Write(fragment, 0, fragment.Length);
            throw new IOException("synthetic partial payload write failure");
        }
        public override void Flush()
        {
            FlushCalls++;
            if (FailFlush) throw new IOException("synthetic flush failure");
            base.Flush();
        }
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
