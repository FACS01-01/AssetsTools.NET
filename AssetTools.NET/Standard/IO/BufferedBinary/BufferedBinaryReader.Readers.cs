using AssetsTools.NET.Standard.IO;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace AssetsTools.NET
{
    public partial class BufferedBinaryReader : IDisposable
    {
        public Encoding StringEncoding;
        private bool _bigEndian = !BitConverter.IsLittleEndian;
        protected bool mustReverseEndianness = false;

        public bool BigEndian
        {
            get => _bigEndian;
            set
            {
                _bigEndian = value;
                mustReverseEndianness = value == BitConverter.IsLittleEndian;
            }
        }

        public void Align() => AlignTo(4);
        public void Align8() => AlignTo(8);
        public void Align16() => AlignTo(16);
        private void AlignTo(int alignment) // Alignment must be a power of 2!!!
        {
            long position = Position;
            long mask = alignment - 1L;
            long alignedPos = (position + mask) & ~mask;

            if (position != alignedPos)
                Position = alignedPos; // could end un not aligned if alignedPos > Length (enforces Position <= Length)
        }

        public virtual byte ReadByte() => InternalReadByte();

        public virtual sbyte ReadSByte() => (sbyte)InternalReadByte();

        public virtual bool ReadBoolean() => InternalReadByte() != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T ReadBuffer<T>(ReadOnlySpan<byte> buffer) where T : unmanaged
            => Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(buffer));

        public virtual short ReadInt16()
        {
            var buffer = InternalRead(sizeof(short));

            var castedVal = ReadBuffer<short>(buffer);
            if (mustReverseEndianness)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }

        public virtual ushort ReadUInt16()
        {
            var buffer = InternalRead(sizeof(ushort));

            var castedVal = ReadBuffer<ushort>(buffer);
            if (mustReverseEndianness)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }

        public virtual int ReadInt32()
        {
            var buffer = InternalRead(sizeof(int));

            var castedVal = ReadBuffer<int>(buffer);
            if (mustReverseEndianness)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }

        public virtual uint ReadUInt32()
        {
            var buffer = InternalRead(sizeof(uint));

            var castedVal = ReadBuffer<uint>(buffer);
            if (mustReverseEndianness)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }

        public virtual long ReadInt64()
        {
            var buffer = InternalRead(sizeof(long));

            var castedVal = ReadBuffer<long>(buffer);
            if (mustReverseEndianness)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }

        public virtual ulong ReadUInt64()
        {
            var buffer = InternalRead(sizeof(ulong));

            var castedVal = ReadBuffer<ulong>(buffer);
            if (mustReverseEndianness)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);
            
            return castedVal;
        }

        public virtual Half ReadHalf() => BitConverter.UInt16BitsToHalf(ReadUInt16());

        public virtual float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

        public virtual double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

        public virtual decimal ReadDecimal()
        {
            ReadOnlySpan<byte> buffer = InternalRead(16);

            int lo = ReadBuffer<int>(buffer.Slice(0, 4));
            int mid = ReadBuffer<int>(buffer.Slice(4, 4));
            int hi = ReadBuffer<int>(buffer.Slice(8, 4));
            int flags = ReadBuffer<int>(buffer.Slice(12, 4));

            if (mustReverseEndianness)
            {
                lo = BinaryPrimitives.ReverseEndianness(lo);
                mid = BinaryPrimitives.ReverseEndianness(mid);
                hi = BinaryPrimitives.ReverseEndianness(hi);
                flags = BinaryPrimitives.ReverseEndianness(flags);
            }

            bool isNegative = flags < 0; // (flags & int.MinValue) != 0;
            byte scale = (byte)((flags >> 16) & 0x7F);

            return new decimal(lo, mid, hi, isNegative, scale);
        }

        [SkipLocalsInit]
        public virtual int ReadInt24()
        {
            Span<byte> intBuffer = stackalloc byte[sizeof(int)];

            if (BitConverter.IsLittleEndian)
            {
                ReadExactly(intBuffer[..3]);
                intBuffer[3] = ((intBuffer[2] & 128) != 0) ? byte.MaxValue : byte.MinValue; // higher bit, sign extend
            }
            else
            {
                ReadExactly(intBuffer[1..]);
                intBuffer[0] = ((intBuffer[1] & 128) != 0) ? byte.MaxValue : byte.MinValue; // higher bit, sign extend
            }

            var castedVal = ReadBuffer<int>(intBuffer);
            if (mustReverseEndianness)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }

        [SkipLocalsInit]
        public virtual uint ReadUInt24()
        {
            Span<byte> intBuffer = stackalloc byte[sizeof(uint)];

            if (BitConverter.IsLittleEndian)
            {
                ReadExactly(intBuffer[..3]);
                intBuffer[3] = ((intBuffer[2] & 128) != 0) ? byte.MaxValue : byte.MinValue;
            }
            else
            {
                ReadExactly(intBuffer[1..]);
                intBuffer[0] = ((intBuffer[1] & 128) != 0) ? byte.MaxValue : byte.MinValue;
            }

            var castedVal = ReadBuffer<uint>(intBuffer);
            if (mustReverseEndianness)
                castedVal = BinaryPrimitives.ReverseEndianness(castedVal);

            return castedVal;
        }

        public int Read7BitEncodedInt()
        {
            uint result = 0;
            byte byteReadJustNow;
            const int MaxBytesWithoutOverflow = 4;
            for (int shift = 0; shift < MaxBytesWithoutOverflow * 7; shift += 7)
            {
                byteReadJustNow = ReadByte();
                result |= (byteReadJustNow & 0x7Fu) << shift;

                if (byteReadJustNow <= 0x7Fu)
                    return (int)result;
            }

            byteReadJustNow = ReadByte();
            if (byteReadJustNow > 0b_1111u)
                throw new FormatException("Incorrect format for 7-bit Int32");

            result |= (uint)byteReadJustNow << (MaxBytesWithoutOverflow * 7);
            return (int)result;
        }

        public long Read7BitEncodedInt64()
        {
            ulong result = 0;
            byte byteReadJustNow;
            const int MaxBytesWithoutOverflow = 9;
            for (int shift = 0; shift < MaxBytesWithoutOverflow * 7; shift += 7)
            {
                byteReadJustNow = ReadByte();
                result |= (byteReadJustNow & 0x7Ful) << shift;

                if (byteReadJustNow <= 0x7Fu)
                    return (long)result;
            }

            byteReadJustNow = ReadByte();
            if (byteReadJustNow > 0b_1u)
                throw new FormatException("Incorrect format for 7-bit Int64");

            result |= (ulong)byteReadJustNow << (MaxBytesWithoutOverflow * 7);
            return (long)result;
        }

        public virtual string ReadString()
            => ReadStringLength(Read7BitEncodedInt());
        public string ReadCountString()
            => ReadStringLength(ReadByte());
        public string ReadCountStringInt16()
            => ReadStringLength(ReadUInt16());
        public string ReadCountStringInt32()
            => ReadStringLength(ReadInt32());

        public string ReadStringLength(int len)
        {
            if (len < 0)
                throw new IOException($"Invalid string length: {len}");
            if (len == 0)
                return string.Empty;
            return GetEncodedString(InternalRead(len));
        }

        public string ReadNullTerminated()
        {
            ReadOnlySpan<byte> buffer;
            int idx;
            if (_isMemoryStream)
            {
                buffer = new(_ms.GetBuffer(), _origin + (int)_pos, (int)(_stream.Length - _pos));
                idx = buffer.IndexOf(byte.MinValue);
                switch (idx)
                {
                    case -1:
                        throw new IOException("Null terminator not found in the remaining stream buffer.");
                    case 0:
                        _pos++;
                        return string.Empty;
                    default:
                        _pos += idx + 1;
                        return GetEncodedString(buffer[..idx]);
                }
            }

            buffer = new(_buffer, _bufferPos, _bufferLen - _bufferPos);
            idx = buffer.IndexOf(byte.MinValue);
            switch (idx)
            {
                case -1:
                    break;
                case 0:
                    _bufferPos++;
                    return string.Empty;
                default:
                    _bufferPos += idx + 1;
                    return GetEncodedString(buffer[..idx]);
            }

            return ReadNullTerminatedSlow(buffer);
        }

        [SkipLocalsInit]
        private string ReadNullTerminatedSlow(ReadOnlySpan<byte> lastBuffer)
        {
            using var collector = new TempBuffer.ByteCollector(256);

            collector.Append(lastBuffer);
            _bufferPos = _bufferLen = 0;

            while (true)
            {
                int read = _stream.Read(_buffer, 0, _bufferMaxLength);
                if (read == 0)
                    throw new EndOfStreamException("Null terminator not found in the remaining stream.");
                _pos += read;

                lastBuffer = new(_buffer, 0, read);
                int idx = lastBuffer.IndexOf(byte.MinValue);
                switch (idx)
                {
                    case -1:
                        collector.Append(lastBuffer);
                        break;
                    case 0:
                        _bufferPos = 1;
                        _bufferLen = read;
                        return GetEncodedString(collector.AsSpan());
                    default:
                        collector.Append(lastBuffer[..idx]);
                        _bufferPos = idx + 1;
                        _bufferLen = read;
                        return GetEncodedString(collector.AsSpan());
                }
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetEncodedString(ReadOnlySpan<byte> bytes) => StringEncoding.GetString(bytes);

        public static string ReadNullTerminatedArray(byte[] bytes, uint pos)
            => ReadNullTerminatedArray(bytes, pos, Encoding.UTF8);

        public static string ReadNullTerminatedArray(byte[] bytes, uint pos, Encoding encoding)
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
                _ => encoding.GetString(span[..idx])
            };
        }
    }
}
