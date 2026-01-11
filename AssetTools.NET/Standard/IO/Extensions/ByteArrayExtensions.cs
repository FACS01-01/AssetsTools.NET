using System;
using System.IO;

namespace AssetsTools.NET.Standard.IO.Extensions
{
    public static class ByteArrayExtensions
    {
        public static MemoryStream NewExposedMemoryStream(this byte[] buffer, bool writable = true) =>
            new(buffer, 0, buffer.Length, writable, true);

        public static MemoryStream NewExposedMemoryStream(this byte[] buffer, int index, int count, bool writable = true) =>
            new(buffer, index, count, writable, true);

        public static byte[] ToUInt4Array(this byte[] source) => ToUInt4Array(source, 0, source.Length);

        public static byte[] ToUInt4Array(this byte[] source, int offset, int size)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, source.Length - size);
            var buffer = GC.AllocateUninitializedArray<byte>(size * 2);
            for (var i = 0; i < size; i++)
            {
                var idx = i * 2;
                buffer[idx] = (byte)(source[offset + i] >> 4);
                buffer[idx + 1] = (byte)(source[offset + i] & 0xF);
            }
            return buffer;
        }
    }
}
