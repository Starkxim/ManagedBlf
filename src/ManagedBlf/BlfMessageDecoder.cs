using System.Buffers.Binary;
using System.Text;

namespace ManagedBlf;

/// <summary>Small managed presentation model, independent of any application or native ABI.</summary>
public sealed record BlfMessage(string Protocol, ushort? Channel, uint? Identifier, byte? Dlc,
    bool IsExtended, bool IsRemote, bool IsTransmit, bool BitrateSwitch, bool ErrorStateIndicator,
    ReadOnlyMemory<byte> Data, string? Text = null, string? Warning = null);

/// <summary>Decodes a documented subset; unknown types and unsupported header versions remain raw.</summary>
public static class BlfMessageDecoder
{
    private static readonly int[] FdLengths = [0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 20, 24, 32, 48, 64];

    public static bool TryDecode(BlfObject value, out BlfMessage? message, Encoding? textEncoding = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        message = null;
        if (value.Header.HeaderVersion is not (1 or 2)) return false;
        var body = value.Body.Span;
        switch (value.Header.ObjectType)
        {
            case BlfObjectTypes.CanMessage:
                Require(body, 16);
                if (body[3] > 8) throw new InvalidDataException("Classic CAN DLC exceeds eight.");
                message = Can(U16(body, 0), U32(body, 4), body[3], body[2], false, false,
                    value.Body.Slice(8, (body[2] & 0x80) != 0 ? 0 : body[3]), "CAN");
                return true;
            case BlfObjectTypes.CanMessage2:
                // Unlike type 1, type 86 permits a variable data field followed by eight metadata bytes.
                Require(body, 16);
                if (body[3] > 8) throw new InvalidDataException("Classic CAN DLC exceeds eight.");
                int can2Length = (body[2] & 0x80) != 0 ? 0 : body[3];
                if (can2Length > body.Length - 16)
                    throw new InvalidDataException("CAN_MESSAGE2 data overlaps its trailing metadata.");
                message = Can(U16(body, 0), U32(body, 4), body[3], body[2], false, false,
                    value.Body.Slice(8, can2Length), "CAN");
                return true;
            case BlfObjectTypes.CanFdMessage:
                Require(body, 84);
                int length = FdLength(body[3]);
                if (body[14] > length) throw new InvalidDataException("CAN FD valid byte count exceeds DLC length.");
                byte flags = body[13];
                message = Can(U16(body, 0), U32(body, 4), body[3], body[2], (flags & 2) != 0,
                    (flags & 4) != 0, value.Body.Slice(20, body[14]), (flags & 1) != 0 ? "CAN FD" : "CAN");
                return true;
            case BlfObjectTypes.CanFdMessage64:
                Require(body, 40);
                int requested = body[2];
                if (requested > FdLength(body[1])) throw new InvalidDataException("CAN FD valid byte count exceeds DLC length.");
                // Extended attribute offsets vary across producers; retain these records raw.
                if (body[35] != 0) return false;
                uint fdFlags = U32(body, 12);
                int available = Math.Min(requested, body.Length - 40);
                message = new BlfMessage((fdFlags & 0x1000) != 0 ? "CAN FD" : "CAN", body[0],
                    U32(body, 4) & 0x1FFFFFFF, body[1], (U32(body, 4) & 0x80000000) != 0,
                    (fdFlags & 0x10) != 0, body[34] != 0, (fdFlags & 0x2000) != 0, (fdFlags & 0x4000) != 0,
                    value.Body.Slice(40, available), Warning: available < requested
                        ? $"Only {available}/{requested} declared payload bytes are stored; no bytes were invented." : null);
                return true;
            case BlfObjectTypes.LinMessage:
                Require(body, 20);
                if (body[3] > 8) throw new InvalidDataException("LIN DLC exceeds eight.");
                message = new BlfMessage("LIN", U16(body, 0), body[2], body[3], false, false,
                    body[18] != 0, false, false, value.Body.Slice(4, body[3]));
                return true;
            case BlfObjectTypes.LinMessage2:
                Require(body, 132);
                if (body[38] > 8) throw new InvalidDataException("LIN DLC exceeds eight.");
                message = new BlfMessage("LIN", U16(body, 12), body[37], body[38], false, false,
                    body[122] != 0, false, false, value.Body.Slice(112, body[38]));
                return true;
            case BlfObjectTypes.AppText:
                Require(body, 16);
                uint textLength = U32(body, 8);
                if (textLength > body.Length - 16) throw new InvalidDataException("APP_TEXT length exceeds object body.");
                message = new BlfMessage("Text", null, null, null, false, false, false, false, false,
                    value.Body.Slice(16, (int)textLength), (textEncoding ?? Encoding.UTF8).GetString(body.Slice(16, (int)textLength)));
                return true;
            default:
                return false;
        }
    }

    private static BlfMessage Can(ushort channel, uint id, byte dlc, byte flags, bool brs, bool esi,
        ReadOnlyMemory<byte> data, string protocol) => new(protocol, channel, id & 0x1FFFFFFF, dlc,
        (id & 0x80000000) != 0, (flags & 0x80) != 0, (flags & 1) != 0, brs, esi, data);
    private static int FdLength(byte dlc) => dlc < FdLengths.Length ? FdLengths[dlc]
        : throw new InvalidDataException("CAN FD DLC exceeds fifteen.");
    private static void Require(ReadOnlySpan<byte> bytes, int size)
    {
        if (bytes.Length < size) throw new InvalidDataException($"Message body has {bytes.Length} bytes; at least {size} required.");
    }
    private static ushort U16(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
}
