using AssetsTools.NET.Standard.IO;
using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.Buffers;
using System.IO;

namespace AssetsTools.NET
{
    public class SegmentStream : Stream
    {
        public SegmentStream(Stream baseStream, long baseOffset, long length = -1, bool canWrite = true, bool leaveOpen = true)
        {
            if (!baseStream.CanSeek)
                throw new ArgumentException("Base stream must be seekable.", nameof(baseStream));

            if (baseOffset < 0 || baseOffset > baseStream.Length)
                throw new ArgumentOutOfRangeException(nameof(baseOffset));

            if (length >= 0 && length > baseStream.Length - baseOffset)
                throw new ArgumentOutOfRangeException(nameof(length));

            _leaveOpen = leaveOpen;
            _canWrite = canWrite;
            _length = length;

            if (baseStream is SegmentStream baseSegmentStream) // prevent/optimize SegmentStream nesting
            {
                _baseStream = baseSegmentStream.BaseStream; // rebase
                BaseOffset = baseSegmentStream.BaseOffset + baseOffset;
                _canWrite = _canWrite && baseSegmentStream._canWrite;
                if (baseSegmentStream.IsLengthRestricted && length < 0)
                    _length = baseSegmentStream.Length - baseOffset;
                if (!_leaveOpen)
                    _leaveOpen = baseSegmentStream._leaveOpen;
            }
            else
            {
                _baseStream = baseStream;
                BaseOffset = baseOffset;
            }
        }

        private Stream? _baseStream;
        public Stream BaseStream
        {
            get
            {
                return _baseStream ?? throw new ObjectDisposedException(nameof(SegmentStream));
            }
        }

        public long BaseOffset
        {
            get;
        }

        private long _position;
        public override long Position
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _position;
            }
            set
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_position == value)
                    return;
                if (value > long.MaxValue - BaseOffset)
                    throw new ArgumentOutOfRangeException(nameof(value), "Out of range of stream max length.");
                if (value < 0)
                    throw new IOException("Can't position before the start of the stream.");
                _position = value;
            }
        }

        private readonly long _length;
        public override long Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _length >= 0 ? _length : BaseStream.Length - BaseOffset;
            }
        }
        public bool IsLengthRestricted => _length >= 0;

        private readonly bool _leaveOpen;
        public bool CloseBaseOnDispose => !_leaveOpen;

        public override void Flush()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            BaseStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long remaining = Length - Position;
            if (remaining <= 0)
                return 0;

            BaseStream.Position = BaseOffset + Position;

            int minCount = count <= remaining ? count : (int)remaining;
            count = BaseStream.Read(buffer, offset, minCount);
            
            Position += count;
            return count;
        }
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long remaining = Length - Position;
            if (remaining <= 0)
                return 0;

            BaseStream.Position = BaseOffset + Position;

            int count = buffer.Length < remaining ?
                BaseStream.Read(buffer) :
                BaseStream.Read(buffer[..(int)remaining]);

            Position += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long originPos = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => Position,
                SeekOrigin.End => Length,
                _ => throw new ArgumentException("Invalid Seek origin request.")
            };

            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, long.MaxValue - originPos, nameof(offset));
            long finalPos = originPos + offset;

            Position = finalPos;
            return finalPos;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfCantWrite();

            ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(count));
            if (count == 0)
                return;

            ThrowIfWriteOverflow(count, nameof(count));

            BaseStream.Position = BaseOffset + Position;
            BaseStream.Write(buffer, offset, count);
            Position += count;
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfCantWrite();

            var bufferLength = buffer.Length;
            if (bufferLength == 0)
                return;

            ThrowIfWriteOverflow(bufferLength, nameof(buffer));

            BaseStream.Position = BaseOffset + Position;
            BaseStream.Write(buffer);
            Position += bufferLength;
        }

        public override bool CanRead => !_disposed && BaseStream.CanRead;

        public override bool CanSeek => !_disposed;

        private readonly bool _canWrite;
        public override bool CanWrite => !_disposed && _canWrite && BaseStream.CanWrite;

        private bool _disposed;
        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (!_leaveOpen)
                    _baseStream.Close();
                if (_baseStream is not MemoryStream) // Don't set to null - allow TryGetBuffer to work
                    _baseStream = null;
            }

            _disposed = true;
        }

        private void ThrowIfCantWrite()
        {
            if (!CanWrite)
                throw new NotSupportedException("Can't write to this stream.");
        }
        private void ThrowIfWriteOverflow(int count, in string _nameof)
        {
            if (_length >= 0 && count > _length - Position)
                throw new IOException($"Can't perform write operation, data stream too long.");
        }

        public bool TryGetBuffer(out ArraySegment<byte> buffer)
        {
            if (_baseStream == null ||
                _baseStream is not MemoryStream ms ||
                !ms.TryGetBuffer(out ArraySegment<byte> baseBuffer))
            {
                buffer = null;
                return false;
            }

            buffer = baseBuffer.Slice((int)BaseOffset, (int)Length);
            return true;
        }

        public void CopyToStream(Stream destination)
        {
            long copySize = Length - Position;
            CopyToStream(destination, copySize);
        }

        public void CopyToStream(Stream destination, long copySize)
        {
            if (copySize == 0)
                return;
            ArgumentOutOfRangeException.ThrowIfNegative(copySize, nameof(copySize));

            BaseStream.Position = BaseOffset + Position;

            if (copySize < int.MaxValue && _baseStream is MemoryStream ms && ms.TryReadBuffer((int)copySize, out ReadOnlySpan<byte> buff))
            {
                destination.Write(buff);
                return;
            }

            int fitBufferSize = copySize > MemorySizes.OPTIMAL_BUFFER_SIZE ? MemorySizes.OPTIMAL_BUFFER_SIZE : (int)copySize;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(fitBufferSize);
            try
            {
                Span<byte> bufferSpan = buffer;
                while (copySize > 0)
                {
                    int toRead = copySize >= bufferSpan.Length ? bufferSpan.Length : (int)copySize;

                    int bytesRead = BaseStream.Read(bufferSpan[..toRead]);
                    if (bytesRead == 0)
                        throw new EndOfStreamException($"Unexpected End Of Stream during copy exact, {copySize} bytes left.");

                    destination.Write(bufferSpan[..bytesRead]);
                    copySize -= bytesRead;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
