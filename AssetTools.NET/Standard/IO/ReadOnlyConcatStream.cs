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
            _cumulativeStreamLengths = new(capacity);
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
                if (_position == value)
                    return;
                if (value < 0)
                    throw new IOException("Can't position before the start of the stream.");
                _position = value;
                UpdateCurrentStreamIdx();
            }
        }

        /// <summary>
        /// The stream's index that contains the current <see cref="Position"/>.
        /// </summary>
        /// <remarks>
        /// If <see cref="Position"/> is at the end of the concatenated streams,
        /// this will be equal to <see cref="Streams"/>.Count.
        /// </remarks>
        public int CurrentStreamIdx { get; private set; }

        /// <summary>
        /// Refreshes the <see cref="ReadOnlyConcatStream"/>'s internal state.
        /// Use in case underlying streams have changed their lengths.
        /// </summary>
        public void Update()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            UpdateCumulativeLengths();
            UpdateCurrentStreamIdx();
        }

        private void UpdateCumulativeLengths()
        {
            _cumulativeStreamLengths.Clear();
            long acumLength = 0;
            var streamsCount = _streams.Count;
            for (int i = 0; i < streamsCount; i++)
            {
                var stream = _streams[i];
                acumLength += stream.Length;
                if (acumLength < 0)
                    throw new OverflowException($"The accumulated length at index {i} is overflowing.");
                _cumulativeStreamLengths.Add(acumLength);
            }
        }

        private void UpdateCurrentStreamIdx()
        {
            var streamIdx = _cumulativeStreamLengths.BinarySearch(_position);
            streamIdx = streamIdx < 0 ? ~streamIdx : streamIdx + 1;
            CurrentStreamIdx = streamIdx;
        }

        private List<long>? _cumulativeStreamLengths;
        public override long Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return CumulativeLength(_streams.Count);
            }
        }
        private long CumulativeLength(int upToCount)
        {
            upToCount--;
            if (upToCount < 0)
                return 0;
            return _cumulativeStreamLengths[upToCount];
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
            //var currentStreamPos = _position - CumulativeLength(CurrentStreamIdx);
            //bool isNextStream = true;
            currentStream.Position = _position - CumulativeLength(CurrentStreamIdx);
            int totalRead = 0;
            do
            {
                /*
                while (CurrentStreamIdx != streamsCount && _position >= CumulativeLength(CurrentStreamIdx + 1))
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
                */ // maybe not needed because _position and CurrentStreamIdx are always in sync outside of this method, and skipping 0 length streams

                int read = currentStream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                    throw new IOException($"Stream #{CurrentStreamIdx + 1} returned 0 bytes read before reaching its end.");

                _position += read;
                totalRead += read;

                while (_position >= CumulativeLength(CurrentStreamIdx + 1))
                {
                    CurrentStreamIdx++;
                    if (CurrentStreamIdx == streamsCount)
                        return totalRead;
                    if (count > totalRead)
                    {
                        currentStream = _streams[CurrentStreamIdx];
                        currentStream.ThrowIfCantRead();
                        //currentStreamPos = 0;
                        //isNextStream = true;
                        currentStream.Position = 0;// currentStreamPos;
                    }
                }
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
            //var currentStreamPos = _position - CumulativeLength(CurrentStreamIdx);
            currentStream.Position = _position - CumulativeLength(CurrentStreamIdx);
            //bool isNextStream = true;
            int totalRead = 0;
            int read = 0;
            do
            {
                /*
                while (CurrentStreamIdx != streamsCount && _position >= CumulativeLength(CurrentStreamIdx + 1)) // advance to next stream if needed
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
                */ // maybe not needed because _position and CurrentStreamIdx are always in sync outside of this method, and skipping 0 length streams
                buffer = buffer[read..];
                read = currentStream.Read(buffer);
                if (read == 0)
                    throw new IOException($"Stream #{CurrentStreamIdx + 1} returned 0 bytes read before reaching its end.");

                _position += read;
                totalRead += read;

                while (_position >= CumulativeLength(CurrentStreamIdx + 1)) // sync _lastStreamIdx to current _position
                {
                    CurrentStreamIdx++;
                    if (CurrentStreamIdx == streamsCount)
                        return totalRead;
                    if (count > totalRead)
                    {
                        currentStream = _streams[CurrentStreamIdx];
                        currentStream.ThrowIfCantRead();
                        //currentStreamPos = 0;
                        //isNextStream = true;
                        currentStream.Position = 0;
                    }
                    
                }
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
            Position = finalPos;
            return finalPos;
        }

        public override void Flush() { }
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

            if (CloseStreamsOnDispose)
                foreach (var stream in _streams)
                    stream?.Close();
            _streams?.Clear();
            _streams = null;

            _cumulativeStreamLengths?.Clear();
            _cumulativeStreamLengths = null;

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

                // verify if there's data left to copy in current stream
                var nextAcumLength = CumulativeLength(CurrentStreamIdx + 1);
                if (_position < nextAcumLength)
                {
                    // init base resources position
                    currentStream.Position = currentStreamPos;
                    // delegate copy
                    currentStream.CopyTo(destination, bufferSize);
                }

                // update this Stream position
                _position = nextAcumLength;
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

            // if there's no data left to copy, return
            var streamsCount = _streams.Count;
            var criticalPos = CumulativeLength(streamsCount) - copySize;
            if (CurrentStreamIdx == streamsCount || _position > criticalPos)
                throw new ArgumentOutOfRangeException(nameof(copySize), "Not enough data left to copy.");

            // loop through base streams
            long currentStreamPos = _position - CumulativeLength(CurrentStreamIdx);
            var nextAcumLength = CumulativeLength(CurrentStreamIdx + 1);
            while (CurrentStreamIdx < streamsCount && copySize > 0)
            {
                var currentStream = _streams[CurrentStreamIdx];

                // verify base resources are readable
                currentStream.ThrowIfCantRead();

                if (_position < nextAcumLength)
                {
                    // init base resources position
                    currentStream.Position = currentStreamPos;

                    // delegate copy based of remaining copySize
                    var posMaxAdvance = nextAcumLength - _position;
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

                while (_position >= nextAcumLength)
                {
                    CurrentStreamIdx++;
                    if (CurrentStreamIdx == streamsCount)
                        return;
                    nextAcumLength = CumulativeLength(CurrentStreamIdx + 1);
                }
                currentStreamPos = 0;
            }
        }

        public override int ReadByte()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var streamsCount = _streams.Count;
            if (CurrentStreamIdx == streamsCount) // no data left
                return -1;

            Stream currentStream = _streams[CurrentStreamIdx];
            currentStream.ThrowIfCantRead();
            var currentStreamPos = _position - CumulativeLength(CurrentStreamIdx);
            /*
            while (CurrentStreamIdx != streamsCount && _position >= CumulativeLength(CurrentStreamIdx + 1)) // advance to next stream if needed
            {
                CurrentStreamIdx++;
                currentStream = _streams[CurrentStreamIdx];
                currentStream.ThrowIfCantRead();
                currentStreamPos = 0;
            }
            if (CurrentStreamIdx == streamsCount)
                return -1;
            */
            currentStream.Position = currentStreamPos;
            var b = currentStream.ReadByte();
            if (b == -1)
                throw new IOException($"Stream #{CurrentStreamIdx + 1} returned 0 bytes read before reaching its end.");

            _position += sizeof(byte);

            while (_position >= CumulativeLength(CurrentStreamIdx + 1))
            {
                CurrentStreamIdx++;
                if (CurrentStreamIdx == streamsCount)
                    break;
            }

            return b;
        }

        public override void WriteByte(byte value) => throw new NotImplementedException();

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
                var streamsCount = _streams.Count;
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, streamsCount, nameof(index));
                value.ThrowIfCantSeek();

                var oldStreamLength = CumulativeLength(index + 1) - CumulativeLength(index);
                var newStreamLength = value.Length;

                ArgumentOutOfRangeException.ThrowIfGreaterThan( // check for total Length overflow
                    _cumulativeStreamLengths[streamsCount - 1] - oldStreamLength, long.MaxValue - newStreamLength, nameof(value));

                var deltaLength = newStreamLength - oldStreamLength;
                for (int i = index; i < streamsCount; i++)
                    _cumulativeStreamLengths[i] += deltaLength;

                if (index <= CurrentStreamIdx)
                    _position = index == CurrentStreamIdx ? CumulativeLength(index) : _position + deltaLength;

                if (newStreamLength == 0)
                {
                    while (CurrentStreamIdx < streamsCount && _position >= CumulativeLength(CurrentStreamIdx + 1))
                        CurrentStreamIdx++;
                }

                _streams[index] = value;
            }
        }
        public void Add(Stream item)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            item.ThrowIfCantSeek();

            var oldStreamsCount = _streams.Count;
            var newCumulativeLength = oldStreamsCount == 0 ? 0 : _cumulativeStreamLengths[^1];
            newCumulativeLength += item.Length;
            if (newCumulativeLength < 0)
                throw new OverflowException("Cumulative length overflowed.");

            _streams.Add(item);
            _cumulativeStreamLengths.Add(newCumulativeLength);
            if (CurrentStreamIdx == oldStreamsCount && _position >= newCumulativeLength)
                CurrentStreamIdx++;
        }
        public void Clear()
        {
            _streams?.Clear();
            _cumulativeStreamLengths?.Clear();
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
            var oldStreamsCount = _streams.Count;
            if (index == oldStreamsCount)
            {
                Add(item);
                return;
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, oldStreamsCount, nameof(index));
            item.ThrowIfCantSeek();

            var itemLength = item.Length;
            var oldCumulativeLength = _cumulativeStreamLengths[^1];
            ArgumentOutOfRangeException.ThrowIfGreaterThan(oldCumulativeLength, long.MaxValue - itemLength, nameof(item));

            _cumulativeStreamLengths.Add(oldCumulativeLength + itemLength);
            for (int i = oldStreamsCount - 1; i >= index; i--)
                _cumulativeStreamLengths[i + 1] = _cumulativeStreamLengths[i] + itemLength;
            _cumulativeStreamLengths[index] = (index == 0 ? 0 : _cumulativeStreamLengths[index - 1]) + itemLength;

            if (index <= CurrentStreamIdx)
            {
                _position += itemLength;
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
            
            var itemLength = CumulativeLength(index + 1) - CumulativeLength(index);

            if (index == CurrentStreamIdx)
                _position = CumulativeLength(index);
            else if (index < CurrentStreamIdx)
            {
                _position -= itemLength;
                CurrentStreamIdx--;
            }

            var streamsCount = _streams.Count;
            for (int i = index + 1; i < streamsCount; i++)
                _cumulativeStreamLengths[i - 1] = _cumulativeStreamLengths[i] - itemLength;

            _streams.RemoveAt(index);
            streamsCount--;
            _cumulativeStreamLengths.RemoveAt(streamsCount);

            while (CurrentStreamIdx < streamsCount && _position >= CumulativeLength(CurrentStreamIdx + 1))
                CurrentStreamIdx++;
        }
        public IEnumerator<Stream> GetEnumerator() => Streams.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Streams.GetEnumerator();
    }
}
