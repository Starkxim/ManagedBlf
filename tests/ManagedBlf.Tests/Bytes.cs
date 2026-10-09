using System.Buffers.Binary;

namespace ManagedBlf.Tests;

// Literal format offsets, independent of reader constants, padding and demo generation.
internal static class Bytes
{
    internal static void U16(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), v);
    internal static void U32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
    internal static void U64(byte[] b, int o, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(o), v);
    internal static byte[] Object(uint type = 999, byte[]? body = null, ushort version = 1, uint flags = 2, ulong time = 123)
    {
        int header = version == 2 ? 40 : 32;
        var b = new byte[header + (body?.Length ?? 0)];
        "LOBJ"u8.CopyTo(b); U16(b, 4, (ushort)header); U16(b, 6, version);
        U32(b, 8, (uint)b.Length); U32(b, 12, type); U32(b, 16, flags); U64(b, 24, time);
        body?.CopyTo(b, header); return b;
    }
    internal static byte[] File(params byte[][] objects)
    {
        var b = new byte[144 + objects.Sum(x => x.Length)];
        "LOGG"u8.CopyTo(b); U32(b, 4, 144);
        int offset = 144;
        foreach (var item in objects) { item.CopyTo(b, offset); offset += item.Length; }
        return b;
    }
    internal static byte[] Container(byte[] data, ushort compression = 0, uint? declared = null)
    {
        // RFC1950 + a single RFC1951 stored block, no production compressor involved.
        byte[] payload = data;
        if (compression == 2)
        {
            payload = new byte[data.Length + 11]; payload[0] = 0x78; payload[1] = 0x01; payload[2] = 1;
            U16(payload, 3, checked((ushort)data.Length)); U16(payload, 5, (ushort)~data.Length);
            data.CopyTo(payload, 7); uint a = 1, b = 0;
            foreach (byte v in data) { a = (a + v) % 65521; b = (b + a) % 65521; }
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(payload.Length - 4), (b << 16) | a);
        }
        int size = 32 + payload.Length;
        var result = new byte[size + size % 4]; // Type 10 legacy padding, deliberately not generic alignment.
        "LOBJ"u8.CopyTo(result); U16(result, 4, 16); U32(result, 8, (uint)size); U32(result, 12, 10);
        U16(result, 16, compression); U32(result, 24, declared ?? (uint)data.Length);
        payload.CopyTo(result, 32); return result;
    }
}
