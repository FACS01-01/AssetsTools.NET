using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace AssetsTools.NET.IO
{
    internal class ReadOnlyConcatStream : Stream, IList<Stream>
    {
        public ReadOnlyConcatStream(bool leaveOpen = true) : this(3, leaveOpen) { }

        public ReadOnlyConcatStream(int capacity, bool leaveOpen = true)
        {
            _streams = new(capacity);
            CloseStreamsOnDispose = !leaveOpen;
        }

        private List<Stream>? _streams;
        private List<Stream> Streams
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _streams;
            }
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
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _position = value;
            }
        }

        private long CumulativeLength(int upToCount)
        {
            long total = 0;
            for (int i = 0; i < upToCount; i++)
                total += _streams[i].Length;
            return total;
        }
        public override long Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return CumulativeLength(_streams.Count);
            }
        }

        public readonly bool CloseStreamsOnDispose;

        private bool HasDataLeft()
        {
            if (_streams.Count == 0)
            {
                _position = 0;
                return false;
            }
            if (_position >= Length)
                return false;

            return true;
        }

        private bool TryGetFirstStream(out int beginStreamIndex, out Stream? beginStream)
        {
            long accumulatedLength = 0;
            int streamsCount = _streams.Count;
            for (beginStreamIndex = 0; beginStreamIndex < streamsCount; beginStreamIndex++)
            {
                var currLength = _streams[beginStreamIndex].Length;
                accumulatedLength += currLength;
                if (_position < accumulatedLength)
                {
                    accumulatedLength -= currLength;
                    beginStream = _streams[beginStreamIndex];
                    beginStream.Position = _position - accumulatedLength;
                    return true;
                }
            }

            beginStream = null;
            beginStreamIndex = -1;
            return false;
        }

        private bool TryGetNextStream(ref int beginStreamIndex, out Stream? beginStream)
        {
            if (beginStreamIndex < _streams.Count - 1)
            {
                beginStreamIndex++;
                beginStream = _streams[beginStreamIndex];
                beginStream.Position = 0;
                return true;
            }
            beginStream = null;
            return false;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!HasDataLeft() ||
                !TryGetFirstStream(out int beginStreamIndex, out Stream? beginStream))
                return 0;

            int read = beginStream.Read(buffer, offset, count);
            if (read == 0)
                return 0;

            int totalRead = read;
            _position += read;

            while (read < count)
            {
                if (beginStream.Position >= beginStream.Length &&
                    !TryGetNextStream(ref beginStreamIndex, out beginStream))
                    break;

                offset += read;
                count -= read;

                read = beginStream.Read(buffer, offset, count);

                if (read == 0)
                    break;

                totalRead += read;
                _position += read;
            }

            return totalRead;
        }

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!HasDataLeft() ||
                !TryGetFirstStream(out int beginStreamIndex, out Stream? beginStream))
                return 0;

            int read = beginStream.Read(buffer);
            if (read == 0)
                return 0;

            int totalRead = read;
            int totalToRead = buffer.Length;
            _position += read;

            while (totalRead < totalToRead)
            {
                if (beginStream.Position >= beginStream.Length &&
                    !TryGetNextStream(ref beginStreamIndex, out beginStream))
                    break;

                buffer = buffer[read..];
                read = beginStream.Read(buffer);

                if (read == 0)
                    break;

                totalRead += read;
                _position += read;
            }

            return totalRead;
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
            ArgumentOutOfRangeException.ThrowIfNegative(finalPos, nameof(offset)); // neg pos and overflows

            _position = finalPos;
            return finalPos;
        }

        public override void Flush()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _streams.ForEach(s => s.Flush());
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;
        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotImplementedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        private bool _disposed;
        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (CloseStreamsOnDispose)
                    foreach (var stream in _streams)
                        stream.Close();

                _streams.Clear();
                _streams = null;
            }

            _disposed = true;
        }



        public int Count
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _streams.Count;
            }
        }
        public bool IsReadOnly => false;
        public Stream this[int index]
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _streams[index];
            }
            set
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                value.ThrowIfCantSeek();
                _streams[index] = value; // should fix Position?
            }
        }
        public void Add(Stream item)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            item.ThrowIfCantSeek();
            _streams.Add(item);
        }
        public void Clear()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _streams.Clear();
            _position = 0;
        }
        public bool Contains(Stream item)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _streams.Contains(item);
        }
        public void CopyTo(Stream[] array, int arrayIndex)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _streams.CopyTo(array, arrayIndex);
        }
        public int IndexOf(Stream item)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _streams.IndexOf(item);
        }
        public void Insert(int index, Stream item)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            item.ThrowIfCantSeek();
            _streams.Insert(index, item); //should fix Position?
        }
        public bool Remove(Stream item)
        {
            int index = IndexOf(item);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }
            return false;
        }
        public void RemoveAt(int index)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfLessThan(index, 0, nameof(index));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _streams.Count);
            
            var safeLength = CumulativeLength(index);
            if (_position > safeLength)
            {
                var item = _streams[index];
                if (_position < safeLength + item.Length)
                    _position = safeLength;
                else
                    _position -= item.Length;
            }

            _streams.RemoveAt(index);
        }
        public IEnumerator<Stream> GetEnumerator() => Streams.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Streams.GetEnumerator();
    }
}
