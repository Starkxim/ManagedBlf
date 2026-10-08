using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ManagedBlf.Viewer;

/// <summary>Limited synthetic fixture builder for this demo; not a general BLF writer.</summary>
public static class DemoSample
{
    public static byte[] Create()
    {
        using var objects = new MemoryStream();
        byte[] can = new byte[16];
        U16(can, 0, 1); can[3] = 8; U32(can, 4, 0x123);
        new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 }.CopyTo(can, 8);
        AddObject(objects, BlfObjectTypes.CanMessage, 0, can);
        can[2] = 1; can[3] = 2; U32(can, 4, 0x80000042);
        AddObject(objects, BlfObjectTypes.CanMessage2, 1_000_000, can.Concat(new byte[8]).ToArray());
        byte[] fd = new byte[84];
        U16(fd, 0, 2); fd[3] = 9; U32(fd, 4, 0x456); fd[13] = 3; fd[14] = 12;
        for (int i = 0; i < 12; i++) fd[20 + i] = (byte)i;
        AddObject(objects, BlfObjectTypes.CanFdMessage, 2_000_000, fd);
        byte[] fd64 = new byte[52];
        fd64[0] = 2; fd64[1] = 9; fd64[2] = 12; U32(fd64, 4, 0x80000123); U32(fd64, 12, 0x7000);
        for (int i = 0; i < 12; i++) fd64[40 + i] = (byte)(0xA0 + i);
        AddObject(objects, BlfObjectTypes.CanFdMessage64, 3_000_000, fd64);
        byte[] lin = new byte[20];
        U16(lin, 0, 1); lin[2] = 0x22; lin[3] = 4;
        new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }.CopyTo(lin, 4);
        AddObject(objects, BlfObjectTypes.LinMessage, 4_000_000, lin);
        byte[] lin2 = new byte[132];
        U16(lin2, 12, 1); lin2[37] = 0x23; lin2[38] = 2; lin2[112] = 0xCA; lin2[113] = 0xFE;
        AddObject(objects, BlfObjectTypes.LinMessage2, 5_000_000, lin2);
        byte[] text = Encoding.UTF8.GetBytes("Hello from ManagedBlf! / 托管解析演示");
        byte[] appText = new byte[16 + text.Length];
        U32(appText, 0, 1); U32(appText, 8, (uint)text.Length); text.CopyTo(appText, 16);
        AddObject(objects, BlfObjectTypes.AppText, 6_000_000, appText);
        byte[] raw = objects.ToArray();
        using var file = new MemoryStream();
        file.Write(new byte[BlfReader.FileHeaderSize]);
        // Split inside the first CAN object, exercising objects crossing container boundaries.
        WriteContainer(file, raw.AsSpan(0, 37), compress: true);
        WriteContainer(file, raw.AsSpan(37), compress: false);
        byte[] result = file.ToArray();
        U32(result, 0, BlfReader.FileSignature); U32(result, 4, BlfReader.FileHeaderSize);
        U32(result, 8, 5050600); result[13] = 6;
        U64(result, 16, (ulong)result.Length); U64(result, 24, (ulong)(144 + 64 + raw.Length)); U32(result, 32, 7);
        WriteDate(result.AsSpan(40)); WriteDate(result.AsSpan(56));
        U16(result, 70, 6);
        return result;
    }

    private static void AddObject(Stream stream, uint type, ulong timestamp, byte[] body)
    {
        byte[] header = new byte[32];
        uint size = (uint)(32 + body.Length);
        U32(header, 0, BlfReader.ObjectSignature); U16(header, 4, 32); U16(header, 6, 1);
        U32(header, 8, size); U32(header, 12, type); U32(header, 16, 2); U64(header, 24, timestamp);
        stream.Write(header); stream.Write(body);
        if (BlfReader.HasPadding(type)) stream.Write(new byte[size % 4]);
    }

    private static void WriteContainer(Stream stream, ReadOnlySpan<byte> raw, bool compress)
    {
        byte[] payload;
        if (compress)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw);
            payload = output.ToArray();
        }
        else payload = raw.ToArray();
        byte[] header = new byte[32];
        uint size = (uint)(32 + payload.Length);
        U32(header, 0, BlfReader.ObjectSignature); U16(header, 4, 16); U16(header, 6, 1);
        U32(header, 8, size); U32(header, 12, BlfObjectTypes.LogContainer);
        U16(header, 16, compress ? (ushort)2 : (ushort)0); U32(header, 24, (uint)raw.Length);
        stream.Write(header); stream.Write(payload); stream.Write(new byte[size % 4]);
    }

    private static void WriteDate(Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 2026);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[4..], 4);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[6..], 1);
    }
    private static void U16(byte[] b, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset), value);
    private static void U32(byte[] b, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset), value);
    private static void U64(byte[] b, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(offset), value);
}
