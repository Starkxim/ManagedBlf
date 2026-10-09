using ManagedBlf;

// Project-authored, public synthetic inputs. No native DLL, captured log or third-party
// fixture is used. The Python checker declares its expected values independently.
if (args.Length != 2)
    throw new ArgumentException("Usage: generate <new-directory> | verify <python-can-control.blf>");

switch (args[0])
{
    case "generate":
        Generate(args[1]);
        break;
    case "verify":
        VerifyExternalControl(args[1]);
        break;
    default:
        throw new ArgumentException("Expected generate or verify.");
}

static void Generate(string directory)
{
    Directory.CreateDirectory(directory);
    var start = new DateTime(2024, 1, 2, 3, 4, 5, 123, DateTimeKind.Unspecified);
    using (var empty = BlfWriter.Create(Path.Combine(directory, "empty.blf"), start))
        empty.Complete();

    using (var writer = BlfWriter.Create(Path.Combine(directory, "flags-multicontainer.blf"), start,
        new BlfWriterOptions { MaxContainerDataSize = 96 }))
    {
        writer.WriteCanMessage(new BlfCanFrame
        {
            TimestampNanoseconds = 0, Channel = 1, Identifier = 0x123,
            Dlc = 2, Data = new byte[] { 0x12, 0x34 }
        });
        writer.WriteCanMessage(new BlfCanFrame
        {
            TimestampNanoseconds = 1_000_000, Channel = 65535, Identifier = 0x42,
            IsExtended = true, IsTransmit = true, Dlc = 8,
            Data = new byte[] { 0x00, 0xFF, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60 }
        });
        writer.WriteCanMessage(new BlfCanFrame
        {
            TimestampNanoseconds = 2_000_000, Channel = 1, Identifier = 0x7FF,
            IsRemote = true, Dlc = 8
        });
        writer.WriteCanMessage(new BlfCanFrame
        {
            TimestampNanoseconds = 3_000_000, Channel = 65535, Identifier = 0x1FFFFFFF,
            IsExtended = true, IsTransmit = true, Dlc = 0
        });
        writer.Complete();
    }
    Console.WriteLine("Generated two public synthetic fixtures (empty and four CAN frames in two containers).");
}

static void VerifyExternalControl(string path)
{
    using var stream = File.OpenRead(path);
    using var reader = new BlfReader(stream);
    Check(reader.Header.ObjectCount == 2, "External object count");
    Check(reader.ReadNext(out var first), "External first object");
    Check(first!.TimestampNanoseconds == 0, "External first nanoseconds");
    Check(BlfMessageDecoder.TryDecode(first, out var a), "External first decoder");
    Check(a!.Protocol == "CAN" && a.Channel == 1 && a.Identifier == 0x321 && a.Dlc == 2,
        "External standard CAN fields");
    Check(!a.IsExtended && !a.IsRemote && !a.IsTransmit, "External standard CAN flags");
    Check(a.Data.Span.SequenceEqual(new byte[] { 0xDE, 0xAD }), "External standard CAN payload");
    Check(reader.ReadNext(out var second), "External second object");
    Check(second!.TimestampNanoseconds == 500_000_000, "External second nanoseconds");
    Check(BlfMessageDecoder.TryDecode(second, out var b), "External second decoder");
    Check(b!.Protocol == "CAN" && b.Channel == 65535 && b.Identifier == 0x55 && b.Dlc == 8,
        "External extended remote CAN fields");
    Check(b.IsExtended && b.IsRemote && b.IsTransmit && b.Data.IsEmpty, "External remote CAN flags and empty payload");
    Check(!reader.ReadNext(out _), "External clean EOF");
    Console.WriteLine("ManagedBlf independently read two frames from the python-can 4.6.1 control file.");
}

static void Check(bool condition, string field)
{
    if (!condition) throw new InvalidDataException($"Interop mismatch: {field}.");
}
