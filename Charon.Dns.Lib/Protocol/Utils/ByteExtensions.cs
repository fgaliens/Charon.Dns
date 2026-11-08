namespace Charon.Dns.Lib.Protocol.Utils;

public static class ByteExtensions
{
    extension(byte b)
    {
        public byte GetBitValueAt(byte offset, byte length)
        {
            return (byte)((b >> offset) & ~(0xff << length));
        }

        public byte GetBitValueAt(byte offset)
        {
            return b.GetBitValueAt(offset, 1);
        }

        public byte SetBitValueAt(byte offset, byte length, byte value)
        {
            var mask = ~(0xff << length);
            value = (byte)(value & mask);

            return (byte)((value << offset) | (b & ~(mask << offset)));
        }

        public byte SetBitValueAt(byte offset, byte value)
        {
            return b.SetBitValueAt(offset, 1, value);
        }
    }
}
