using AssetsTools.NET.Standard.IO;
using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace AssetsTools.NET
{
    /// <summary>
    /// Reads primitive data types as binary values in a specific encoding.
    /// </summary>
    public partial class BufferedBinaryReader : IDisposable
    {
        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        protected bool _disposed = false;

        private readonly bool _isMemoryStream = false;
        private readonly MemoryStream _ms = null;
        private readonly int _origin = 0;

        private readonly byte[] _buffer = null;
        /// <summary>
        /// Length of <see cref="_buffer"/>.
        /// </summary>
        private readonly int _bufferMaxLength = 0;
        /// <summary>
        /// Position in buffer from where to start reading.
        /// </summary>
        private int _bufferPos = 0;
        /// <summary>
        /// Length of last buffer saved.
        /// </summary>
        private int _bufferLen = 0;
        /// <summary>
        /// Last <see cref="_stream"/> Position after buffering.
        /// </summary>
        private long _pos;

        public BufferedBinaryReader(string filePath,
            int bufferSize = MemorySizes.DEFAULT_FILESTREAM_BUFFER_SIZE, bool leaveOpen = false)
            : this(filePath, Encoding.UTF8, bufferSize, leaveOpen)
        {
        }

        public BufferedBinaryReader(string filePath, Encoding encoding,
            int bufferSize = MemorySizes.DEFAULT_FILESTREAM_BUFFER_SIZE, bool leaveOpen = false)
            : this(File.OpenRead(filePath), encoding, bufferSize, leaveOpen)
        {
        }

        public BufferedBinaryReader(Stream input,
            int bufferSize = MemorySizes.DEFAULT_FILESTREAM_BUFFER_SIZE, bool leaveOpen = false)
            : this(input, Encoding.UTF8, bufferSize, leaveOpen)
        {
        }

        public BufferedBinaryReader(Stream input, Encoding encoding,
            int bufferSize = MemorySizes.DEFAULT_FILESTREAM_BUFFER_SIZE, bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(encoding);
            input.ThrowIfCantRead();
            if (bufferSize < 32)
                bufferSize = 32; // bufferSize should be at least max(sizeof(T)) for all supported T's

            _stream = input;
            StringEncoding = encoding;
            _leaveOpen = leaveOpen;

            switch (input)
            {
                case MemoryStream ms:
                    if (ms.TryGetBuffer(out var buffer))
                    {
                        _isMemoryStream = true;
                        _ms = ms;
                        _origin = buffer.Offset;
                    }
                    break;
                case SegmentStream ss:
                    if (ss.BaseStream is MemoryStream ss_ms &&
                        ss_ms.TryGetBuffer(out var ss_buffer))
                    {
                        _isMemoryStream = true;
                        _ms = ss_ms;
                        _origin = ss_buffer.Offset + (int)ss.BaseOffset;
                    }
                    break;
            }

            if (!_isMemoryStream)
            {
                _bufferMaxLength = bufferSize;
                _buffer = GC.AllocateUninitializedArray<byte>(bufferSize);
            }

            _pos = input.Position;
        }

        public Stream BaseStream => _stream;
        
        public long Position
        {
            get
            {
                ThrowIfDisposed();
                return _pos - _bufferLen + _bufferPos;
            }
            set
            {
                ThrowIfDisposed();
                _stream.ThrowIfCantSeek();

                var pos = _pos - _bufferLen + _bufferPos;
                if (pos == value)
                    return;
                ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(value));

                if (value > _stream.Length)
                    value = _stream.Length;

                if (_isMemoryStream)
                {
                    _pos = value;
                    _stream.Position = _pos;
                    return;
                }

                var delta = value - pos;
                if (delta > 0)
                {
                    var remaining = _bufferLen - _bufferPos;
                    if (remaining >= delta)
                    {
                        _bufferPos += (int)delta;
                    }
                    else
                    {
                        _pos = value;
                        _bufferPos = _bufferLen = 0;
                    }
                }
                else
                {
                    delta = -delta;
                    if (_bufferPos >= delta)
                    {
                        _bufferPos -= (int)delta;
                    }
                    else
                    {
                        _pos = value;
                        _bufferPos = _bufferLen = 0;
                    }
                }
                _stream.Position = _pos;
            }
        }

        public long Length
        {
            get
            {
                ThrowIfDisposed();
                return _stream.Length;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed) // share same _disposed token
            {
                DisposeCore(disposing); // overrides should begin calling base.DisposeCore(bool)
                _disposed = true;
            }
        }

        protected virtual void DisposeCore(bool disposing)
        {
            if (!_leaveOpen)
            {
                _stream.Dispose();
            }
        }

        public virtual int Read(byte[] buffer, int index, int count)
        {
            ThrowIfDisposed();
            _stream.ThrowIfCantRead();
            StreamExtensions.ThrowIfInvalidBufferSegment(buffer, index, count);
            if (count == 0)
                return 0;

            int remaining;
            int read;

            if (_isMemoryStream)
            {
                remaining = (int)(_stream.Length - _pos);
                if (remaining == 0)
                    return 0;

                var thisBuffer = _ms.GetBuffer();
                var begin = _origin + (int)_pos;
                read = count < remaining ? count : remaining;
                Buffer.BlockCopy(thisBuffer, begin, buffer, index, read);
                _pos += read;
                return read;
            }

            if (TryEnsureBufferInternal(count, false))
            {
                Buffer.BlockCopy(_buffer, _bufferPos, buffer, index, count);
                _bufferPos += count;
                return count;
            }

            remaining = _bufferLen - _bufferPos;
            if (remaining > 0)
            {
                Buffer.BlockCopy(_buffer, _bufferPos, buffer, index, remaining);
                _bufferPos = _bufferLen;
                read = remaining;
            }
            else
                read = 0;
            if (count > _bufferMaxLength)
            {
                _bufferPos = _bufferLen = 0; // this loop won't save any buffer, buffer must be restarted empty
                while (read < count)
                {
                    int read2 = _stream.Read(buffer, index + read, count - read);
                    if (read2 == 0)
                        return read;
                    _pos += read2;
                    read += read2;
                }
            }
            return read;
        }

        public virtual int Read(Span<byte> buffer)
        {
            ThrowIfDisposed();
            _stream.ThrowIfCantRead();
            var count = buffer.Length;
            if (count == 0)
                return 0;

            int remaining;
            int read;

            if (_isMemoryStream)
            {
                remaining = (int)(_stream.Length - _pos);
                if (remaining == 0)
                    return 0;

                var thisBuffer = _ms.GetBuffer();
                var begin = _origin + (int)_pos;
                read = count < remaining ? count : remaining;
                new ReadOnlySpan<byte>(thisBuffer, begin, read).CopyTo(buffer);
                _pos += read;
                return read;
            }

            if (TryEnsureBufferInternal(count, false))
            {
                new ReadOnlySpan<byte>(_buffer, _bufferPos, count).CopyTo(buffer);
                _bufferPos += count;
                return count;
            }

            remaining = _bufferLen - _bufferPos;
            if (remaining > 0)
            {
                new ReadOnlySpan<byte>(_buffer, _bufferPos, remaining).CopyTo(buffer);
                _bufferPos = _bufferLen;
                read = remaining;
            }
            else
                read = 0;
            if (count > _bufferMaxLength)
            {
                _bufferPos = _bufferLen = 0; // this loop won't save any buffer, buffer must be restarted empty
                while (read < count)
                {
                    int read2 = _stream.Read(buffer[read..]);
                    if (read2 == 0)
                        return read;
                    _pos += read2;
                    read += read2;
                }
            }
            return read;
        }

        public virtual byte[] ReadBytes(int count)
        {
            ThrowIfDisposed();
            _stream.ThrowIfCantRead();
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            
            if (count == 0)
                return Array.Empty<byte>();

            byte[] result = GC.AllocateUninitializedArray<byte>(count);
            int numRead = Read(result, 0, count);

            if (numRead != count)
                result = result[..numRead];

            return result;
        }

        /// <summary>
        /// Reads bytes from the current stream and advances the position within the stream until the <paramref name="buffer" /> is filled.
        /// </summary>
        /// <remarks>
        /// When <paramref name="buffer"/> is empty, this read operation will be completed without waiting for available data in the stream.
        /// </remarks>
        /// <param name="buffer">A region of memory. When this method returns, the contents of this region are replaced by the bytes read from the current stream.</param>
        /// <exception cref="ObjectDisposedException">The stream is closed.</exception>
        /// <exception cref="IOException">An I/O error occurred.</exception>
        /// <exception cref="EndOfStreamException">The end of the stream is reached before filling the <paramref name="buffer" />.</exception>
        public virtual void ReadExactly(Span<byte> buffer)
        {
            var read = Read(buffer);
            if (read != buffer.Length)
                throw new EndOfStreamException();
        }

        public bool TryEnsureBuffer(int count)
        {
            ThrowIfDisposed();
            if (_isMemoryStream)
            {
                if (count < 0)
                    count = 0;
                return _pos <= _stream.Length - count;
            }
            if (count < 0)
                count = _bufferMaxLength - _bufferLen;
            if (count == 0)
                return true;
            return TryEnsureBufferInternal(count, false);
        }

        private bool TryEnsureBufferInternal(int count, bool throwEOS = true)
        {
            var remaining = _bufferLen - _bufferPos;
            if (remaining >= count)
                return true;

            if (count > _bufferMaxLength)
                return false;

            if (_bufferPos > 0)
            {
                if (remaining != 0)
                    Buffer.BlockCopy(_buffer, _bufferPos, _buffer, 0, remaining);
                _bufferLen = remaining;
                _bufferPos = 0;
            }

            while (_bufferLen < count)
            {
                int read = _stream.Read(_buffer, _bufferLen, _bufferMaxLength - _bufferLen);
                if (read == 0)
                {
                    if (throwEOS)
                        throw new EndOfStreamException();
                    return false;
                }
                _bufferLen += read;
                _pos += read;
            }

            return true;
        }

        protected virtual ReadOnlySpan<byte> InternalRead(int readSize)
        {
            ThrowIfDisposed();
            _stream.ThrowIfCantRead();

            if (_isMemoryStream)
                return InternalReadMS(readSize);

            return InternalReadBuffer(readSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<byte> InternalReadMS(int readSize)
        {
            var newPos = _pos + readSize;

            if (newPos > _stream.Length)
                throw new EndOfStreamException();

            var span = new ReadOnlySpan<byte>(_ms.GetBuffer(), _origin + (int)_pos, readSize);
            _pos = newPos;
            return span;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<byte> InternalReadBuffer(int readSize)
        {
            if (TryEnsureBufferInternal(readSize))
            {
                var ros = new ReadOnlySpan<byte>(_buffer, _bufferPos, readSize);
                _bufferPos += readSize;
                return ros;
            }
            // readSize is > _bufferMaxLength
            var tempBuffer = GC.AllocateUninitializedArray<byte>(readSize);
            var remaining = _bufferLen - _bufferPos;
            Buffer.BlockCopy(_buffer, _bufferPos, tempBuffer, 0, remaining);
            _bufferPos = _bufferLen = 0; // this operation won't save any buffer, buffer must be restarted empty
            var extraRead = readSize - remaining;
            _stream.ReadExactly(tempBuffer, remaining, extraRead);
            _pos += extraRead;
            return new ReadOnlySpan<byte>(tempBuffer, 0, readSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual byte InternalReadByte()
        {
            ThrowIfDisposed();
            _stream.ThrowIfCantRead();

            if (_isMemoryStream)
            {
                if (_pos >= _stream.Length)
                    throw new EndOfStreamException();
                var b = _ms.GetBuffer()[_origin + (int)_pos];
                _pos++;
                return b;
            }

            TryEnsureBufferInternal(1);
            return _buffer[_bufferPos++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
