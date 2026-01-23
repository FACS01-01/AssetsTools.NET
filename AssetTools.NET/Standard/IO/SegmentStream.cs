using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.Collections.Generic;
using System.IO;

namespace AssetsTools.NET
{
    public class SegmentStream : Stream, StreamExtensions.IStreamCopyToExactly, StreamExtensions.IStreamTryGetBuffer
    {
        public SegmentStream(Stream baseStream, long baseOffset, long length = -1, bool canWrite = true, bool leaveOpen = true)
        {
            baseStream.ThrowIfCantSeek();

            if (baseOffset < 0 || baseOffset > baseStream.Length)
                throw new ArgumentOutOfRangeException(nameof(baseOffset));

            if (length >= 0 && length > baseStream.Length - baseOffset)
                throw new ArgumentOutOfRangeException(nameof(length));

            CloseBaseOnDispose = !leaveOpen;
            _canWrite = canWrite;
            _length = length;

            if (baseStream is SegmentStream baseSegmentStream) // prevent/optimize SegmentStream nesting
            {
                _baseStream = baseSegmentStream.BaseStream; // rebase
                BaseOffset = baseSegmentStream.BaseOffset + baseOffset;
                _canWrite = _canWrite && baseSegmentStream._canWrite;
                if (baseSegmentStream.IsLengthRestricted && length < 0)
                    _length = baseSegmentStream.Length - baseOffset;
                if (CloseBaseOnDispose)
                    CloseBaseOnDispose = baseSegmentStream.CloseBaseOnDispose;
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
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _baseStream;
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
                ObjectDisposedException.ThrowIf(!CanSeek, this);
                return _position;
            }
            set
            {
                ObjectDisposedException.ThrowIf(!CanSeek, this);
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
                ObjectDisposedException.ThrowIf(!CanSeek, this);
                return _length >= 0 ? _length : _baseStream.Length - BaseOffset;
            }
        }
        public bool IsLengthRestricted => _length >= 0;

        public readonly bool CloseBaseOnDispose;

        public override void Flush()
        {
            ObjectDisposedException.ThrowIf(!CanSeek, this);
            _baseStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(!CanRead, this);
            StreamExtensions.ThrowIfInvalidBufferSegment(buffer, offset, count);

            if (count == 0)
                return 0;
            long remaining = Length - _position;
            if (remaining <= 0)
                return 0;

            _baseStream.Position = BaseOffset + _position;

            int minCount = count <= remaining ? count : (int)remaining;
            count = _baseStream.Read(buffer, offset, minCount);
            
            _position += count;
            return count;
        }
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(!CanRead, this);

            if (buffer.Length == 0)
                return 0;
            long remaining = Length - _position;
            if (remaining <= 0)
                return 0;

            _baseStream.Position = BaseOffset + _position;

            int count = buffer.Length < remaining ?
                _baseStream.Read(buffer) :
                _baseStream.Read(buffer[..(int)remaining]);

            _position += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long originPos = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => _position,
                SeekOrigin.End => Length,
                _ => throw new ArgumentException("Invalid Seek origin request.")
            };

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
            ObjectDisposedException.ThrowIf(!CanWrite, this);
            StreamExtensions.ThrowIfInvalidBufferSegment(buffer, offset, count);
            
            if (count == 0)
                return;

            ThrowIfWriteOverflow(count, nameof(count));

            _baseStream.Position = BaseOffset + _position;
            _baseStream.Write(buffer, offset, count);
            _position += count;
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(!CanWrite, this);

            var bufferLength = buffer.Length;
            if (bufferLength == 0)
                return;

            ThrowIfWriteOverflow(bufferLength, nameof(buffer));

            _baseStream.Position = BaseOffset + _position;
            _baseStream.Write(buffer);
            _position += bufferLength;
        }

        public override bool CanRead => !_disposed && _baseStream.CanRead;

        public override bool CanSeek => !_disposed && _baseStream.CanSeek;

        private readonly bool _canWrite;
        public override bool CanWrite => !_disposed && _canWrite && _baseStream.CanWrite;

        private bool _disposed;
        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (CloseBaseOnDispose)
                    _baseStream.Close();
                _baseStream = null;
            }

            _disposed = true;
        }
        public void DisposeLeavingBaseOpen()
        {
            if (_disposed)
                return;

            _baseStream = null;

            _disposed = true;
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            // verify this Stream is not disposed, destination is writable, and bufferSize is valid
            ObjectDisposedException.ThrowIf(!CanRead, this);
            destination.ThrowIfCantWrite();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize, nameof(bufferSize));

            // if there's no data left to copy, return
            var length = Length;
            if (_position >= length)
                return;

            // verify base resources are readable
            //   done with ThrowIf(!CanRead) above

            // init base resources position
            _baseStream.Position = BaseOffset + _position;

            // delegate copy based of length
            var copySize = length - _position;
            if (IsLengthRestricted && _length != length)
                StreamExtensions.CopyToExactly(_baseStream, destination, copySize, bufferSize);
            else
            {
                if (copySize < bufferSize) // fix buffer size if too big
                    bufferSize = (int)copySize;
                _baseStream.CopyTo(destination, bufferSize);
            }

            // update this Stream position
            _position = length;
        }

        /// <inheritdoc cref="CopyToExactly(Stream, long, int)"/>
        public void CopyToExactly(Stream destination, long copySize) =>
            CopyToExactly(destination, copySize, StreamExtensions.GetCopyBufferSize(this));

        public void CopyToExactly(Stream destination, long copySize, int bufferSize)
        {
            // verify this Stream is not disposed, destination is writable, and bufferSize is valid
            ObjectDisposedException.ThrowIf(!CanRead, this);
            destination.ThrowIfCantWrite();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize, nameof(bufferSize));

            // verify copySize is valid
            ArgumentOutOfRangeException.ThrowIfNegative(copySize, nameof(copySize));

            // if copy request if of size 0, return
            if (copySize == 0)
                return;

            // verify base resources are readable
            //   done with ThrowIf(!CanRead) above

            // verify enough data is available to copy
            var criticalPos = Length - copySize;
            ArgumentOutOfRangeException.ThrowIfGreaterThan(_position, criticalPos, nameof(copySize));

            // init base resources position
            _baseStream.Position = BaseOffset + _position;

            // delegate copy based of position
            if (_position == criticalPos)
            {
                if (copySize < bufferSize) // fix buffer size if too big
                    bufferSize = (int)copySize;
                _baseStream.CopyTo(destination, bufferSize);
            }
            else
                StreamExtensions.CopyToExactly(_baseStream, destination, copySize, bufferSize);

            // update this Stream position
            _position += copySize;
        }

        public override int ReadByte()
        {
            ObjectDisposedException.ThrowIf(!CanRead, this);

            if (_position >= Length)
                return -1;

            _baseStream.Position = BaseOffset + _position;
            var b = _baseStream.ReadByte();
            if (b != -1)
                _position += sizeof(byte);

            return b;
        }

        public override void WriteByte(byte value)
        {
            ObjectDisposedException.ThrowIf(!CanWrite, this);
            ThrowIfWriteOverflow(sizeof(byte), nameof(value));

            _baseStream.Position = BaseOffset + _position;
            _baseStream.WriteByte(value);
            _position += sizeof(byte);
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

        private void ThrowIfWriteOverflow(int count, in string _nameof)
        {
            if (_length >= 0 && count > _length - _position)
                throw new IOException($"Can't perform write operation, data stream too long.");
        }

        /// <summary>
        /// Merges adjacent compatible <see cref="SegmentStream"/> instances within <paramref name="streams"/> into single,
        /// larger <see cref="SegmentStream"/> objects.
        /// </summary>
        /// <param name="streams">The list of streams to process.</param>
        public static void JoinSegmentStreams(List<Stream> streams)
        {
            if (streams.Count < 2)
                return;

            for (int i = 0; i < streams.Count - 1; i++)
            {
                if (streams[i] is not SegmentStream seg || seg._disposed)
                    continue;

                Stream baseStream = seg._baseStream;
                long nextOffset = seg.BaseOffset + seg.Length;
                if (nextOffset < 0) // crazy overflow
                    continue;
                bool canWrite = seg.CanWrite;
                bool willCloseBase = seg.CloseBaseOnDispose;
                int i_next = i + 1;
                int j = i_next;
                for (; j < streams.Count; j++)
                {
                    if (streams[j] is not SegmentStream nextSeg ||
                        nextSeg._disposed ||
                        nextSeg._baseStream != baseStream ||
                        nextSeg._canWrite != canWrite ||
                        nextSeg.BaseOffset != nextOffset ||
                        long.MaxValue - nextSeg.Length < nextOffset)
                        break;
                    nextOffset += nextSeg.Length;
                    if (willCloseBase)
                        willCloseBase = nextSeg.CloseBaseOnDispose;
                    nextSeg.DisposeLeavingBaseOpen();
                }
                j -= i_next;
                if (j == 0)
                    continue;

                long newSegLength = nextOffset - seg.BaseOffset;
                SegmentStream newSeg = new(baseStream, seg.BaseOffset, newSegLength, canWrite, !willCloseBase);

                seg.DisposeLeavingBaseOpen();
                streams[i] = newSeg;

                while (j != 0)
                {
                    streams.RemoveAt(i_next);
                    j--;
                }
            }
        }
    }
}
