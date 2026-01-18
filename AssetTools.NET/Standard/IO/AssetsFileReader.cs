using AssetsTools.NET.Standard.IO;
using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace AssetsTools.NET
{
    public class AssetsFileReader : BinaryReader
    {
        public bool BigEndian { get; set; } = !BitConverter.IsLittleEndian;

        public AssetsFileReader(string filePath, bool leaveOpen = false)
            : base(File.OpenRead(filePath), Encoding.UTF8, leaveOpen)
        {
        }

        public AssetsFileReader(Stream stream, bool leaveOpen = false)
            : base(stream, Encoding.UTF8, leaveOpen)
        {
        }

        [SkipLocalsInit] // don't zero-init stackallocs
        public override short ReadInt16()
        {
            BaseStream.ThrowIfCantRead();

            ReadOnlySpan<byte> buffer = BaseStream.TryReadBuffer_Core(sizeof(short), out ReadOnlySpan<byte> buff) ?
                buff :
                (stackalloc byte[sizeof(short)]).WriteExactly_Core(BaseStream);

            var castedVal = MemoryMarshal.Read<short>(buffer);
            if (BigEndian == BitConverter.IsLittleEndian) // reverse if endianness differs
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }
        [SkipLocalsInit]
        public override ushort ReadUInt16()
        {
            BaseStream.ThrowIfCantRead();

            ReadOnlySpan<byte> buffer = BaseStream.TryReadBuffer_Core(sizeof(ushort), out ReadOnlySpan<byte> buff) ?
                buff :
                (stackalloc byte[sizeof(ushort)]).WriteExactly_Core(BaseStream);

            var castedVal = MemoryMarshal.Read<ushort>(buffer);
            if (BigEndian == BitConverter.IsLittleEndian)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }
        [SkipLocalsInit]
        public int ReadInt24()
        {
            BaseStream.ThrowIfCantRead();

            Span<byte> intBuffer = stackalloc byte[sizeof(int)];
            if (BitConverter.IsLittleEndian)
            {
                if (BaseStream.TryReadBuffer_Core(3, out ReadOnlySpan<byte> buff))
                    buff.CopyTo(intBuffer);
                else intBuffer[..3].WriteExactly_Core(BaseStream);
                intBuffer[3] = ((intBuffer[2] & 128) != 0) ? byte.MaxValue : byte.MinValue; // higher bit, sign extend
            }
            else
            {
                if (BaseStream.TryReadBuffer_Core(3, out ReadOnlySpan<byte> buff))
                    buff.CopyTo(intBuffer[1..]);
                else intBuffer[1..].WriteExactly_Core(BaseStream);
                intBuffer[0] = ((intBuffer[1] & 128) != 0) ? byte.MaxValue : byte.MinValue; // higher bit, sign extend
            }

            var castedVal = MemoryMarshal.Read<int>(intBuffer);
            if (BigEndian == BitConverter.IsLittleEndian)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }
        [SkipLocalsInit]
        public uint ReadUInt24()
        {
            BaseStream.ThrowIfCantRead();

            Span<byte> intBuffer = stackalloc byte[sizeof(uint)];
            if (BitConverter.IsLittleEndian)
            {
                if (BaseStream.TryReadBuffer_Core(3, out ReadOnlySpan<byte> buff))
                    buff.CopyTo(intBuffer);
                else intBuffer[..3].WriteExactly_Core(BaseStream);
                intBuffer[3] = ((intBuffer[2] & 128) != 0) ? byte.MaxValue : byte.MinValue;
            }
            else
            {
                if (BaseStream.TryReadBuffer_Core(3, out ReadOnlySpan<byte> buff))
                    buff.CopyTo(intBuffer[1..]);
                else intBuffer[1..].WriteExactly_Core(BaseStream);
                intBuffer[0] = ((intBuffer[1] & 128) != 0) ? byte.MaxValue : byte.MinValue;
            }

            var castedVal = MemoryMarshal.Read<uint>(intBuffer);
            if (BigEndian == BitConverter.IsLittleEndian)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }
        [SkipLocalsInit]
        public override int ReadInt32()
        {
            BaseStream.ThrowIfCantRead();

            ReadOnlySpan<byte> buffer = BaseStream.TryReadBuffer_Core(sizeof(int), out ReadOnlySpan<byte> buff) ?
                buff :
                (stackalloc byte[sizeof(int)]).WriteExactly_Core(BaseStream);

            var castedVal = MemoryMarshal.Read<int>(buffer);
            if (BigEndian == BitConverter.IsLittleEndian)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }
        [SkipLocalsInit]
        public override uint ReadUInt32()
        {
            BaseStream.ThrowIfCantRead();

            ReadOnlySpan<byte> buffer = BaseStream.TryReadBuffer_Core(sizeof(uint), out ReadOnlySpan<byte> buff) ?
                buff :
                (stackalloc byte[sizeof(uint)]).WriteExactly_Core(BaseStream);

            var castedVal = MemoryMarshal.Read<uint>(buffer);
            if (BigEndian == BitConverter.IsLittleEndian)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }
        [SkipLocalsInit]
        public override long ReadInt64()
        {
            BaseStream.ThrowIfCantRead();

            ReadOnlySpan<byte> buffer = BaseStream.TryReadBuffer_Core(sizeof(long), out ReadOnlySpan<byte> buff) ?
                buff :
                (stackalloc byte[sizeof(long)]).WriteExactly_Core(BaseStream);

            var castedVal = MemoryMarshal.Read<long>(buffer);
            if (BigEndian == BitConverter.IsLittleEndian)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }
        [SkipLocalsInit]
        public override ulong ReadUInt64()
        {
            BaseStream.ThrowIfCantRead();

            ReadOnlySpan<byte> buffer = BaseStream.TryReadBuffer_Core(sizeof(ulong), out ReadOnlySpan<byte> buff) ?
                buff :
                (stackalloc byte[sizeof(ulong)]).WriteExactly_Core(BaseStream);

            var castedVal = MemoryMarshal.Read<ulong>(buffer);
            if (BigEndian == BitConverter.IsLittleEndian)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }

        public void Align() => AlignTo(4);
        public void Align8() => AlignTo(8);
        public void Align16() => AlignTo(16);
        private void AlignTo(int alignment) // Alignment must be a power of 2!!!
        {
            BaseStream.ThrowIfCantSeek();

            long position = BaseStream.Position;
            long mask = alignment - 1L;
            long aligned = (position + mask) & ~mask;

            if (position != aligned)
                BaseStream.Position = aligned;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetUTF8String(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);
        public string ReadStringLength(int len)
        {
            if (BaseStream.TryReadBuffer(len, out var buffer))
                return GetUTF8String(buffer);

            return TempBuffer.RunBufferedAction(len, BaseStream, static (stream, tempBuffer) =>
            {
                stream.ReadExactly(tempBuffer);
                return GetUTF8String(tempBuffer);
            });
        }
        public string ReadNullTerminated()
        {
            if (BaseStream.TryGetRemainingBuffer(out var buffer))
            {
                ReadOnlySpan<byte> bufferSpan = buffer;
                int idx = bufferSpan.IndexOf(byte.MinValue);
                if (idx == -1)
                    throw new IOException("Null terminator not found in the remaining stream buffer.");
                BaseStream.Position += idx + 1;
                return GetUTF8String(buffer.Slice(0, idx));
            }

            return ReadNullTerminatedSlow();
        }
        [SkipLocalsInit]
        private string ReadNullTerminatedSlow()
        {
            using var collector = new TempBuffer.ByteCollector(256);
            Span<byte> readBuffer = stackalloc byte[64];

            while (true)
            {
                int read = BaseStream.Read(readBuffer);
                if (read == 0)
                    throw new IOException("Null terminator not found in the remaining stream.");

                int nullIdx = readBuffer[..read].IndexOf(byte.MinValue);
                if (nullIdx >= 0)
                {
                    collector.Append(readBuffer[..nullIdx]);
                    BaseStream.Position -= read - (nullIdx + 1);
                    break;
                }

                collector.Append(readBuffer[..read]);
            }

            return GetUTF8String(collector.AsSpan());
        }
        public static string ReadNullTerminatedArray(byte[] bytes, uint pos)
        {
            ArgumentNullException.ThrowIfNull(bytes, nameof(bytes));
            uint bytesLength = (uint)bytes.Length;
            if (bytesLength == 0)
                throw new IOException("Null terminator not found in the (empty) array.");

            //ArgumentOutOfRangeException.ThrowIfNegative(pos); // in case pos was int
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pos, bytesLength, nameof(pos));

            if (bytes[(int)pos] == byte.MinValue)
                return string.Empty;

            ReadOnlySpan<byte> span = bytes.AsSpan((int)pos);
            int idx = span.IndexOf(byte.MinValue);
            return idx switch
            {
                -1 => throw new IOException("Null terminator not found in the array."),
                //0 => string.Empty,
                _ => GetUTF8String(span[..idx])
            };
        }
        public string ReadCountString()
        {
            byte length = ReadByte();
            return ReadStringLength(length);
        }
        public string ReadCountStringInt16()
        {
            ushort length = ReadUInt16();
            return ReadStringLength(length);
        }
        public string ReadCountStringInt32()
        {
            int length = ReadInt32();
            return ReadStringLength(length);
        }
        public long Position
        {
            get { return BaseStream.Position; }
            set { BaseStream.Position = value; }
        }
    }
}
