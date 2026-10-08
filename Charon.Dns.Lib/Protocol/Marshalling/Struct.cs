using System;
using System.Buffers;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Charon.Dns.Lib.Protocol.Marshalling
{
    public static class Struct
    {
        // Every real caller passes a struct of 4-20 bytes (Header/Tail/Head/Options). The fallback
        // only matters for a hypothetical external caller passing an unusually large T.
        private const int MaxStackAllocSize = 256;

        private static void ConvertEndian<T>(Span<byte> data)
        {
            var type = typeof(T);
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            EndianAttribute endian = null;

            if (type.GetTypeInfo().IsDefined(typeof(EndianAttribute), false))
            {
                endian = (EndianAttribute)type.GetTypeInfo().GetCustomAttributes(typeof(EndianAttribute), false).First();
            }

            foreach (var field in fields)
            {
                if (endian == null && !field.IsDefined(typeof(EndianAttribute), false))
                {
                    continue;
                }

                var offset = Marshal.OffsetOf<T>(field.Name).ToInt32();
#pragma warning disable 618
                var length = Marshal.SizeOf(field.FieldType);
#pragma warning restore 618
                endian = endian ?? (EndianAttribute)field.GetCustomAttributes(typeof(EndianAttribute), false).First();

                if (endian.Endianness == Endianness.Big && BitConverter.IsLittleEndian ||
                        endian.Endianness == Endianness.Little && !BitConverter.IsLittleEndian)
                {
                    data.Slice(offset, length).Reverse();
                }
            }
        }

        public static T GetStruct<T>(byte[] data) where T : struct
        {
            return GetStruct<T>(data.AsSpan());
        }

        public static T GetStruct<T>(byte[] data, int offset, int length) where T : struct
        {
            return GetStruct<T>(new ReadOnlySpan<byte>(data, offset, length));
        }

        public static T GetStruct<T>(ReadOnlySpan<byte> data) where T : struct
        {
            var size = Unsafe.SizeOf<T>();
            if (data.Length < size)
            {
                throw new ArgumentException("Data too short", nameof(data));
            }

            if (size <= MaxStackAllocSize)
            {
                Span<byte> buffer = stackalloc byte[size];
                data[..size].CopyTo(buffer);
                ConvertEndian<T>(buffer);
                return MemoryMarshal.Read<T>(buffer);
            }

            var rented = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                var buffer = rented.AsSpan(0, size);
                data[..size].CopyTo(buffer);
                ConvertEndian<T>(buffer);
                return MemoryMarshal.Read<T>(buffer);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public static byte[] GetBytes<T>(T obj) where T : struct
        {
            var data = new byte[Unsafe.SizeOf<T>()];
            GetBytes(obj, data);
            return data;
        }

        public static void GetBytes<T>(T obj, Span<byte> destination) where T : struct
        {
            var size = Unsafe.SizeOf<T>();
            if (destination.Length < size)
            {
                throw new ArgumentException("Destination too small", nameof(destination));
            }

            MemoryMarshal.Write(destination, in obj);
            ConvertEndian<T>(destination[..size]);
        }
    }
}
