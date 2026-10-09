using System.Text;
using Xunit;

namespace ManagedBlf.Tests;

public class DecoderTests
{
    private static BlfObject Value(uint type, byte[] body)
    {
        var raw = Bytes.Object(type, body); return new(new(32, 1, (uint)raw.Length, type), 123, raw);
    }
    [Theory] [InlineData(1)] [InlineData(86)] [InlineData(100)] [InlineData(101)] [InlineData(11)] [InlineData(57)] [InlineData(65)]
    public void SevenLiteralLayoutsDecode(int type)
    {
        byte[] body = type switch { 1 => new byte[16], 86 => new byte[18], 100 => new byte[84], 101 => new byte[42], 11 => new byte[20], 57 => new byte[132], _ => new byte[18] };
        int dataOffset;
        if (type is 1 or 86 or 100)
        {
            Bytes.U16(body, 0, 3); body[3] = 2; Bytes.U32(body, 4, 0x80000042); dataOffset = type == 100 ? 20 : 8;
            if (type == 100) { body[13] = 7; body[14] = 2; }
            if (type == 86) Array.Fill(body, (byte)0xEE, 10, 8);
        }
        else if (type == 101)
        {
            body[0] = 3; body[1] = 2; body[2] = 2; Bytes.U32(body, 4, 0x80000042); Bytes.U32(body, 12, 0x7000); dataOffset = 40;
        }
        else if (type == 11) { Bytes.U16(body, 0, 3); body[2] = 0x42; body[3] = 2; dataOffset = 4; }
        else if (type == 57) { Bytes.U16(body, 12, 3); body[37] = 0x42; body[38] = 2; dataOffset = 112; }
        else { Bytes.U32(body, 8, 2); dataOffset = 16; }
        body[dataOffset] = 65; body[dataOffset + 1] = 66;
        var v = Value((uint)type, body); var before = v.RawData.ToArray();
        Assert.True(BlfMessageDecoder.TryDecode(v, out var m)); Assert.Equal(new byte[] {65,66}, m!.Data.ToArray());
        Assert.Equal(before, v.RawData.ToArray());
        if (type == 65) Assert.Equal("AB", m.Text);
        else { Assert.Equal((ushort)3, m.Channel); Assert.Equal(0x42U, m.Identifier); Assert.Equal((byte)2, m.Dlc); Assert.Equal(type is 1 or 86 or 100 or 101, m.IsExtended); }
        if (type is 100 or 101) { Assert.Equal("CAN FD", m.Protocol); Assert.True(m.BitrateSwitch); Assert.True(m.ErrorStateIndicator); }
    }
    [Fact] public void Can86MetadataCannotBecomePayload()
    {
        var body = new byte[16]; body[3] = 1; Assert.Throws<InvalidDataException>(() => BlfMessageDecoder.TryDecode(Value(86, body), out _));
    }
    [Fact] public void Fd101ShortPayloadAndExtension()
    {
        var body = new byte[42]; body[1] = 8; body[2] = 8; body[40] = 0xAB; body[41] = 0xCD;
        Assert.True(BlfMessageDecoder.TryDecode(Value(101, body), out var m)); Assert.Equal(new byte[] {0xAB,0xCD}, m!.Data.ToArray()); Assert.NotNull(m.Warning);
        body[35] = 40; var v = Value(101, body); Assert.False(BlfMessageDecoder.TryDecode(v, out _)); Assert.Equal(body, v.Body.ToArray());
    }
    [Fact] public void RemoteCanDoesNotExposeStoredData()
    {
        var body = new byte[16]; body[2] = 0x81; body[3] = 8; Bytes.U32(body, 4, 0x42);
        Assert.True(BlfMessageDecoder.TryDecode(Value(1, body), out var m)); Assert.True(m!.IsRemote); Assert.True(m.IsTransmit); Assert.False(m.IsExtended); Assert.Empty(m.Data.ToArray());
    }
    [Fact] public void TextEncodingIsExplicitAndLengthChecked()
    {
        var text = Encoding.UTF8.GetBytes("中文"); var body = new byte[16 + text.Length]; Bytes.U32(body, 8, (uint)text.Length); text.CopyTo(body, 16);
        Assert.True(BlfMessageDecoder.TryDecode(Value(65, body), out var m)); Assert.Equal("中文", m!.Text);
        Assert.True(BlfMessageDecoder.TryDecode(Value(65, body), out var latin, Encoding.Latin1)); Assert.Equal(Encoding.Latin1.GetString(text), latin!.Text);
        Bytes.U32(body, 8, (uint)text.Length + 1); Assert.Throws<InvalidDataException>(() => BlfMessageDecoder.TryDecode(Value(65, body), out _));
    }
    [Theory] [InlineData(1)] [InlineData(86)] [InlineData(100)] [InlineData(101)] [InlineData(11)] [InlineData(57)] [InlineData(65)]
    public void ShortKnownBodiesThrow(int type) => Assert.Throws<InvalidDataException>(() => BlfMessageDecoder.TryDecode(Value((uint)type, new byte[1]), out _));
}
