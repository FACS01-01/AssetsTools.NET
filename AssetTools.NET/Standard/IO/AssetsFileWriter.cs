using AssetsTools.NET.Standard.IO;
using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace AssetsTools.NET
{
    public class AssetsFileWriter : BinaryWriter
    {
        private bool _bigEndian = !BitConverter.IsLittleEndian;
        private bool mustReverseEndianness = false;
        public bool BigEndian
        {
            get => _bigEndian;
            set
            {
                _bigEndian = value;
                mustReverseEndianness = value == BitConverter.IsLittleEndian;
            }
        }

        public AssetsFileWriter(string filePath, bool leaveOpen = false)
            : base(File.Open(filePath, FileMode.Create, FileAccess.Write), Encoding.UTF8, leaveOpen)
        {
        }
        
        public AssetsFileWriter(Stream stream, bool leaveOpen = false)
            : base(stream, Encoding.UTF8, leaveOpen)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ReadOnlySpan<byte> GetBuffer<T>(scoped ref T val) where T : unmanaged
        {
            void* p = Unsafe.AsPointer(ref val);
            return new ReadOnlySpan<byte>(p, sizeof(T));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void WriteNoAlloc<T>(T val) where T : unmanaged
        {
            BaseStream.Write(GetBuffer(ref val));
            //bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref val, 1));
            //BaseStream.Write(bytes);
        }

        public override void Write(short val)
        {
            BaseStream.ThrowIfCantWrite();

            if (mustReverseEndianness) // reverse if endianness differs
                val = BinaryPrimitives.ReverseEndianness(val);

            WriteNoAlloc(val);
        }
        public override void Write(ushort val)
        {
            BaseStream.ThrowIfCantWrite();

            if (mustReverseEndianness)
                val = BinaryPrimitives.ReverseEndianness(val);

            WriteNoAlloc(val);
        }
        public override void Write(int val)
        {
            BaseStream.ThrowIfCantWrite();

            if (mustReverseEndianness)
                val = BinaryPrimitives.ReverseEndianness(val);

            WriteNoAlloc(val);
        }
        public override void Write(uint val)
        {
            BaseStream.ThrowIfCantWrite();

            if (mustReverseEndianness)
                val = BinaryPrimitives.ReverseEndianness(val);

            WriteNoAlloc(val);
        }
        public override void Write(long val)
        {
            BaseStream.ThrowIfCantWrite();

            if (mustReverseEndianness)
                val = BinaryPrimitives.ReverseEndianness(val);

            WriteNoAlloc(val);
        }
        public override void Write(ulong val)
        {
            BaseStream.ThrowIfCantWrite();

            if (mustReverseEndianness)
                val = BinaryPrimitives.ReverseEndianness(val);

            WriteNoAlloc(val);
        }
        public void WriteRawString(string val)
        {
            BaseStream.ThrowIfCantWrite();
            int byteCount = Encoding.UTF8.GetByteCount(val);

            TempBuffer.RunBufferedAction(byteCount, (val, BaseStream), static (state, span) =>
            {
                int bytesWritten = Encoding.UTF8.GetBytes(state.val, span);
                state.BaseStream.Write(span[..bytesWritten]);
            });
        }
        public void WriteUInt24(uint val)
        {
            BaseStream.ThrowIfCantWrite();

            if (mustReverseEndianness)
                val = BinaryPrimitives.ReverseEndianness(val);
            var bytes = GetBuffer(ref val);

            bytes = BitConverter.IsLittleEndian ? bytes[..3] : bytes.Slice(1, 3);

            BaseStream.Write(bytes);
        }
        public void WriteInt24(int val)
        {
            BaseStream.ThrowIfCantWrite();

            if (mustReverseEndianness)
                val = BinaryPrimitives.ReverseEndianness(val);
            var bytes = GetBuffer(ref val);

            bytes = BitConverter.IsLittleEndian ? bytes[..3] : bytes.Slice(1, 3);

            BaseStream.Write(bytes);
        }
        public void Align() => AlignTo(4);
        public void Align8() => AlignTo(8);
        public void Align16() => AlignTo(16);
        private static readonly byte[] ZeroPadding = new byte[16];
        private void AlignTo(int alignment) // Alignment must be a power of 2!!! up to 16
        {
            BaseStream.ThrowIfCantWrite();

            long pos = BaseStream.Position;
            long mask = alignment - 1L;
            int pad = (int)((-pos) & mask);

            switch (pad)
            {
                case 0:
                    return;
                case 1:
                    BaseStream.WriteByte(byte.MinValue);
                    return;
                default:
                    BaseStream.Write(ZeroPadding, 0, pad);
                    return;
            }
        }
        public void WriteNullTerminated(string text)
        {
            WriteRawString(text);
            BaseStream.WriteByte(byte.MinValue);
        }
        public void WriteCountString(string text)
        {
            var byteCount = Encoding.UTF8.GetByteCount(text);
            if (byteCount > byte.MaxValue)
                new Exception("String is longer than 255! Use the Int32 variant instead!");
            BaseStream.WriteByte((byte)byteCount);
            WriteRawString(text);
        }
        public void WriteCountStringInt16(string text)
        {
            var byteCount = Encoding.UTF8.GetByteCount(text);
            if (byteCount > ushort.MaxValue)
                new Exception("String is longer than 65535! Use the Int32 variant instead!");
            Write((ushort)byteCount);
            WriteRawString(text);
        }
        public void WriteCountStringInt32(string text)
        {
            Write(Encoding.UTF8.GetByteCount(text));
            WriteRawString(text);
        }
        public long Position
        {
            get { return BaseStream.Position; }
            set { BaseStream.Position = value; }
        }
    }
}
