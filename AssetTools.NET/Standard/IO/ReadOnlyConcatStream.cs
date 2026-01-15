using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace AssetsTools.NET.IO
{
    public class ReadOnlyConcatStream : Stream, IList<Stream>, StreamExtensions.IStreamCopyToExactly
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
                if (_position == value)
                    return;
                _position = value;
                UpdateCurrentStreamIdx();
            }
        }

        /// <summary>
        /// The index of the stream that contains the current <see cref="Position"/>.
        /// </summary>
        /// <remarks>
        /// If <see cref="Position"/> is at the end of the concatenated streams,
        /// this will be equal to <see cref="Streams"/>.Count.
        /// </remarks>
        public int CurrentStreamIdx { get; private set; }

        /// <summary>
        /// Refreshes the <see cref="ReadOnlyConcatStream"/>'s internal state.
        /// Use in case underlying streams have changed their lengths, and you won't set a new <see cref="Position"/>.
        /// </summary>
        /// <remarks>
        /// It should be okey not to update if only streams after <see cref="CurrentStreamIdx"/> have changed.
        /// </remarks>
        public void Update()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            UpdateCurrentStreamIdx();
        }

        private void UpdateCurrentStreamIdx()
        {
            long accumulatedLength = 0;
            int streamsCount = _streams.Count;
            for (CurrentStreamIdx = 0; CurrentStreamIdx < streamsCount; CurrentStreamIdx++)
            {
                accumulatedLength += _streams[CurrentStreamIdx].Length;
                if (_position < accumulatedLength)
                    return;
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
        private long ReverseCumulativeLength(int fromIdx)
        {
            long total = 0;
            for (int i = _streams.Count - 1; i >= fromIdx; i--)
                total += _streams[i].Length;
            return total;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StreamExtensions.ThrowIfInvalidBufferSegment(buffer, offset, count);

            if (count == 0)
                return 0;
            var streamsCount = _streams.Count;
            if (CurrentStreamIdx == streamsCount) // no data left
                return 0;

            Stream currentStream = _streams[CurrentStreamIdx];
            currentStream.ThrowIfCantRead();
            var currentStreamPos = _position - CumulativeLength(CurrentStreamIdx);
            bool isNextStream = true;
            int totalRead = 0;
            do
            {
                while (CurrentStreamIdx != streamsCount && currentStreamPos >= currentStream.Length)
                {
                    CurrentStreamIdx++;
                    currentStream = _streams[CurrentStreamIdx];
                    currentStream.ThrowIfCantRead();
                    currentStreamPos = 0;
                    isNextStream = true;
                }
                if (CurrentStreamIdx == streamsCount) // no data left
                    return totalRead;

                if (isNextStream)
                {
                    isNextStream = false;
                    currentStream.Position = currentStreamPos;
                }

                int read = currentStream.Read(buffer, offset + totalRead, count - totalRead);
                _position += read;
                currentStreamPos += read;
                totalRead += read;

                if (currentStreamPos >= currentStream.Length)
                {
                    CurrentStreamIdx++;
                    currentStream = _streams[CurrentStreamIdx];
                    currentStream.ThrowIfCantRead();
                    currentStreamPos = 0;
                    isNextStream = true;
                }
                else if (read == 0)
                    throw new IOException($"Stream #{CurrentStreamIdx + 1} returned 0 bytes read before reaching its end.");
            }
            while (count > totalRead);

            return totalRead;
        }

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var count = buffer.Length;
            if (count == 0)
                return 0;
            var streamsCount = _streams.Count;
            if (CurrentStreamIdx == streamsCount) // no data left
                return 0;

            Stream currentStream = _streams[CurrentStreamIdx];
            currentStream.ThrowIfCantRead();
            var currentStreamPos = _position - CumulativeLength(CurrentStreamIdx);
            bool isNextStream = true;
            int totalRead = 0;
            do
            {
                while (CurrentStreamIdx != streamsCount && currentStreamPos >= currentStream.Length) // advance to next stream if needed
                {
                    CurrentStreamIdx++;
                    currentStream = _streams[CurrentStreamIdx];
                    currentStream.ThrowIfCantRead();
                    currentStreamPos = 0;
                    isNextStream = true;
                }
                if (CurrentStreamIdx == streamsCount) // no data left
                    return totalRead;

                if (isNextStream) // only force stream position if we switched streams
                {
                    isNextStream = false;
                    currentStream.Position = currentStreamPos;
                }

                buffer = buffer[totalRead..];
                int read = currentStream.Read(buffer);
                _position += read;
                currentStreamPos += read;
                totalRead += read;

                if (currentStreamPos >= currentStream.Length) // sync _lastStreamIdx to current _position
                {
                    CurrentStreamIdx++;
                    currentStream = _streams[CurrentStreamIdx];
                    currentStream.ThrowIfCantRead();
                    currentStreamPos = 0;
                    isNextStream = true;
                }
                else if (read == 0)
                    throw new IOException($"Stream #{CurrentStreamIdx + 1} returned 0 bytes read before reaching its end.");
            }
            while (count > totalRead);

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
            ArgumentOutOfRangeException.ThrowIfNegative(finalPos, nameof(offset)); // negative and overflow position

            Position = finalPos;
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
                        stream?.Close();

                _streams.Clear();
                _streams = null;
            }

            _disposed = true;
        }
        public readonly bool CloseStreamsOnDispose;

        public override void CopyTo(Stream destination, int bufferSize)
        {
            // verify this Stream is not disposed, destination is writable, and bufferSize is valid
            ObjectDisposedException.ThrowIf(_disposed, this);
            destination.ThrowIfCantWrite();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize, nameof(bufferSize));

            // if there's no data left to copy, return
            var streamsCount = _streams.Count;
            if (CurrentStreamIdx == streamsCount)
                return;

            // loop through base streams
            long currentStreamPos = _position - CumulativeLength(CurrentStreamIdx);
            for (; CurrentStreamIdx < streamsCount; CurrentStreamIdx++)
            {
                var currentStream = _streams[CurrentStreamIdx];

                // verify base resources are readable
                currentStream.ThrowIfCantRead();

                // init base resources position
                currentStream.Position = currentStreamPos;

                // delegate copy
                currentStream.CopyTo(destination, bufferSize);

                // update this Stream position
                _position -= currentStreamPos;
                _position += currentStream.Length;
                currentStreamPos = 0;
            }
        }

        /// <inheritdoc cref="CopyToExactly(Stream, long, int)"/>
        public void CopyToExactly(Stream destination, long copySize) =>
            CopyToExactly(destination, copySize, StreamExtensions.GetCopyBufferSize(this));
        public void CopyToExactly(Stream destination, long copySize, int bufferSize)
        {
            // verify this Stream is not disposed, destination is writable, and bufferSize is valid
            ObjectDisposedException.ThrowIf(_disposed, this);
            destination.ThrowIfCantWrite();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize, nameof(bufferSize));

            // verify copySize is valid
            ArgumentOutOfRangeException.ThrowIfNegative(copySize, nameof(copySize));

            // if copy request if of size 0, return
            if (copySize == 0)
                return;

            // verify enough data is available to copy
            var streamsCount = _streams.Count;
            var firstLengths = CumulativeLength(CurrentStreamIdx);
            var lastLengths = ReverseCumulativeLength(CurrentStreamIdx);
            var criticalPos = firstLengths + lastLengths - copySize;
            ArgumentOutOfRangeException.ThrowIfGreaterThan(_position, criticalPos, nameof(copySize));

            // loop through base streams
            long currentStreamPos = _position - firstLengths;
            while (CurrentStreamIdx < streamsCount && copySize > 0)
            {
                var currentStream = _streams[CurrentStreamIdx];

                // verify base resources are readable
                currentStream.ThrowIfCantRead();

                var currentStreamLength = currentStream.Length;
                if (currentStreamLength > 0)
                {
                    // init base resources position
                    currentStream.Position = currentStreamPos;

                    // delegate copy based of remaining copySize
                    var posMaxAdvance = currentStreamLength - currentStreamPos;
                    if (posMaxAdvance <= copySize)
                    {
                        if (copySize < bufferSize) // fix buffer size if too big
                            bufferSize = (int)copySize;
                        currentStream.CopyTo(destination, bufferSize);
                    }
                    else
                    {
                        posMaxAdvance = copySize;
                        StreamExtensions.CopyToExactly(currentStream, destination, copySize, bufferSize);
                    }

                    // update this Stream position
                    _position += posMaxAdvance;
                    copySize -= posMaxAdvance;
                }

                currentStreamPos = 0;
                CurrentStreamIdx++;
            }
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
                ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _streams.Count, nameof(index));
                value.ThrowIfCantRead();

                if (index == CurrentStreamIdx)
                {
                    _position = CumulativeLength(index);
                }
                else if (index < CurrentStreamIdx)
                {
                    _position -= _streams[index].Length;
                    _position += value.Length;
                }

                _streams[index] = value;
            }
        }
        public void Add(Stream item)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            item.ThrowIfCantRead();
            var update = CurrentStreamIdx == _streams.Count;
            _streams.Add(item);
            if (update)
                UpdateCurrentStreamIdx();
        }
        public void Clear()
        {
            _streams?.Clear();
            _position = 0;
            CurrentStreamIdx = 0;
        }
        public bool Contains(Stream item)
        {
            if (_disposed)
                return false;
            return _streams.Contains(item);
        }
        public void CopyTo(Stream[] array, int arrayIndex)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _streams.CopyTo(array, arrayIndex);
        }
        public int IndexOf(Stream item)
        {
            if (_disposed)
                return -1;
            return _streams.IndexOf(item);
        }
        public void Insert(int index, Stream item)
        {
            var streamsCount = _streams.Count;
            if (index == streamsCount)
            {
                Add(item);
                return;
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, streamsCount, nameof(index));
            item.ThrowIfCantRead();

            if (index <= CurrentStreamIdx)
            {
                _position += item.Length;
                CurrentStreamIdx++;
            }

            _streams.Insert(index, item);
        }
        public bool Remove(Stream item)
        {
            if (_disposed)
                return false;
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
            ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _streams.Count, nameof(index));
            
            if (index == CurrentStreamIdx)
            {
                _position = CumulativeLength(index);
            }
            else if (index < CurrentStreamIdx)
            {
                _position -= _streams[index].Length;
                CurrentStreamIdx--;
            }

            _streams.RemoveAt(index);
        }
        public IEnumerator<Stream> GetEnumerator() => Streams.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Streams.GetEnumerator();
    }
}
