using AssetsTools.NET.Standard.Codecs;
using System;
using System.IO;

namespace AssetsTools.NET.Standard.IO.Extensions
{
    public static class StreamExtensions
    {
        public static Stream CopyToNew(this Stream source, long copySize, BackingStreamType bst)
        {
            ThrowIfCantRead(source);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copySize, nameof(copySize));

            switch (bst)
            {
                case BackingStreamType.MemoryStream:
                    ThrowIfSizeBiggerThanMemStream(copySize);
                    byte[] copyData = GC.AllocateUninitializedArray<byte>((int)copySize);
                    source.ReadExactly(copyData);
                    return NewExposedMemoryStream(copyData);
                case BackingStreamType.FileStream:
                    int bufferSize = copySize > MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE ?
                        MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE :
                        (copySize > MemorySizes.DEFAULT_FILESTREAM_BUFFER_SIZE ?
                        (int)copySize : MemorySizes.DEFAULT_FILESTREAM_BUFFER_SIZE);
                    FileStream new_fs = NewTempFileStream(bufferSize);
                    CopyToExactly(source, new_fs, copySize);
                    new_fs.Position = 0;
                    return new_fs;
                default:
                    throw new BackingStreamTypeNotImplementedException(bst);
            }
        }

        /// <inheritdoc cref="IStreamCopyToExactly.CopyToExactly(Stream, long, int)"/>
        public static void CopyToExactly(Stream source, Stream destination, long copySize)
        {
            ThrowIfCantRead(source);
            ThrowIfCantWrite(destination);
            ArgumentOutOfRangeException.ThrowIfNegative(copySize, nameof(copySize));

            if (copySize == 0)
                return;

            var currentPos = source.Position;
            var criticalPos = source.Length - copySize;
            ArgumentOutOfRangeException.ThrowIfGreaterThan(currentPos, criticalPos, nameof(copySize));

            var bufferSize = GetCopyBufferSize(source);
            if (currentPos == criticalPos)
            {
                if (copySize < bufferSize)
                    bufferSize = (int)copySize;
                source.CopyTo(destination, bufferSize);
            }
            else
                CopyToExactly(source, destination, copySize, bufferSize);
        }

        /// <inheritdoc cref="IStreamCopyToExactly.CopyToExactly(Stream, long, int)"/>
        internal static void CopyToExactly(Stream source, Stream destination, long copySize, int bufferSize) //verify args before calling
        {
            if (copySize < bufferSize)
                bufferSize = (int)copySize;

            switch (source)
            {
                case MemoryStream ms:
                    CopyMSToExactly(ms, destination, copySize, bufferSize);
                    return;
                case IStreamCopyToExactly scte:
                    scte.CopyToExactly(destination, copySize, bufferSize);
                    return;
                default:
                    CopyToExactlyGeneric(source, destination, copySize, bufferSize);
                    return;
            }
        }

        private static void CopyMSToExactly(MemoryStream source, Stream destination, long copySize, int bufferSize)
        {
            if (source.TryGetBuffer(out var buffer))
            {
                buffer = buffer.Slice((int)source.Position, (int)copySize);
                source.Position += copySize;
                destination.Write(buffer);
                return;
            }

            CopyToExactlyGeneric(source, destination, copySize, bufferSize);
        }

        private static void CopyToExactlyGeneric(Stream source, Stream destination, long copySize, int bufferSize)
        {
            TempBuffer.RunBufferedAction(bufferSize, (source, destination, copySize), static (state, buffer) =>
            {
                var (source, destination, copySize) = state;
                var bufferSize = buffer.Length;

                while (copySize > 0)
                {
                    int toRead = copySize > bufferSize ? bufferSize : (int)copySize;
                    int read = source.Read(buffer[..toRead]);
                    if (read == 0)
                        throw new IOException($"Unexpected End Of Stream during copy exact, {copySize} bytes missing to read.");
                    destination.Write(buffer[..read]);
                    copySize -= read;
                }
            });
        }

        /// <summary>
        /// Try get the <paramref name="stream"/>'s internal buffer, starting at its current Position,
        /// and advancing it by <paramref name="accessSize"/>.
        /// </summary>
        public static bool TryReadBuffer(this Stream stream, int accessSize, out ReadOnlySpan<byte> buffer)
        {
            ThrowIfCantRead(stream);
            ArgumentOutOfRangeException.ThrowIfNegative(accessSize, nameof(accessSize));

            if (accessSize == 0)
            {
                buffer = ReadOnlySpan<byte>.Empty;
                return true;
            }
            return TryReadBuffer_Core(stream, accessSize, out buffer);
        }

        public static bool TryGetRemainingBuffer(this Stream stream, out ArraySegment<byte> arrSeg)
        {
            ThrowIfCantRead(stream);
            long len = stream.Length;
            long pos = stream.Position;
            if (pos >= len)
            {
                arrSeg = ArraySegment<byte>.Empty;
                return true;
            }
            if (len > int.MaxValue) // too large to expose buffer
            {
                arrSeg = default;
                return false;
            }
            if (!TryGetInternalBuffer_Core(stream, out var buffer))
            {
                arrSeg = default;
                return false;
            }
            arrSeg = buffer[(int)pos..];
            return true;
        }

        internal static bool TryGetInternalBuffer_Core(this Stream stream, out ArraySegment<byte> arrSeg)
        {
            switch (stream)
            {
                case MemoryStream ms:
                    return ms.TryGetBuffer(out arrSeg);
                case IStreamTryGetBuffer ss:
                    return ss.TryGetBuffer(out arrSeg);
                default:
                    arrSeg = default;
                    return false;
            }
        }

        internal static bool TryReadBuffer_Core(this Stream stream, int accessSize, out ReadOnlySpan<byte> buffer)
        {
            long pos = stream.Position;
            ArgumentOutOfRangeException.ThrowIfGreaterThan(pos, stream.Length - accessSize, nameof(accessSize));

            if (!stream.TryGetInternalBuffer_Core(out var arrSeg))
            {
                buffer = null;
                return false;
            }

            buffer = arrSeg.AsSpan((int)pos, accessSize);
            stream.Position += accessSize;
            return true;
        }

        public static Span<byte> WriteExactly(this Span<byte> target, Stream stream)
        {
            ThrowIfCantRead(stream);

            return WriteExactly_Core(target, stream);
        }

        internal static Span<byte> WriteExactly_Core(this Span<byte> target, Stream stream)
        {
            stream.ReadExactly(target);
            return target;
        }

        public static MemoryStream NewExposedMemoryStream(int bufferSize, bool writable = true)
        {
            byte[] buffer = new byte[bufferSize];
            return new MemoryStream(buffer, 0, buffer.Length, writable, true);
        }

        public static MemoryStream NewExposedMemoryStream(this ArraySegment<byte> buffer, bool writable = true)
        {
            if (buffer.Array != null)
                return new MemoryStream(buffer.Array, buffer.Offset, buffer.Count, writable, true);
            throw new ArgumentException("The ArraySegment<byte> didn't contain a byte[] anymore.", nameof(buffer));
        }

        public static FileStream NewTempFileStream(long bufferSize = MemorySizes.DEFAULT_FILESTREAM_BUFFER_SIZE)
        {
            if (bufferSize > MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE)
                bufferSize = MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE;
            else if (bufferSize < MemorySizes.DEFAULT_FILESTREAM_BUFFER_SIZE)
                bufferSize = MemorySizes.DEFAULT_FILESTREAM_BUFFER_SIZE;

            string tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            return new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, (int)bufferSize,
                FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        }

        public static Stream GetTempStream(long memSize)
        {
            if (memSize < MemorySizes.TEMP_MEMORY_OPERATION_MAX_SIZE)
                return NewExposedMemoryStream((int)memSize);
            return NewTempFileStream();
        }

        internal static void ThrowIfSizeBiggerThanMemStream(long size)
        {
            if (size > int.MaxValue)
                throw new ArgumentOutOfRangeException($"Required size ({size}) greater than MemoryStream max size.");
        }

        internal static void ThrowIfCantSeek(this Stream stream)
        {
            if (!stream.CanSeek) // also throws on null reference
                throw new NotSupportedException("Stream is not seekable.");
        }

        internal static void ThrowIfCantRead(this Stream stream)
        {
            if (!stream.CanRead) // also throws on null reference
            {
                if (stream.CanWrite)
                    throw new NotSupportedException("Stream is write-only.");
                throw new ObjectDisposedException(nameof(stream), "Stream is disposed.");
            }
        }

        internal static void ThrowIfCantWrite(this Stream stream)
        {
            if (!stream.CanWrite) // also throws on null reference
            {
                if (stream.CanRead)
                    throw new NotSupportedException("Destination stream is read-only.");
                throw new ObjectDisposedException(nameof(stream), "Stream is disposed.");
            }
        }

        internal static void ThrowIfInvalidBufferSegment(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer, nameof(buffer));
            ArgumentOutOfRangeException.ThrowIfNegative(offset, nameof(offset));
            ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(count));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, buffer.Length - count, nameof(count));
        }

        public interface IStreamCopyToExactly
        {
            /// <summary>
            /// Copies <paramref name="copySize"/> number of bytes, starting at the current <see cref="Stream.Position"/>,
            /// to the specified <paramref name="destination"/>.
            /// </summary>
            /// <param name="destination">The stream to which the data will be copied.</param>
            /// <param name="copySize">The exact number of bytes to copy.</param>
            /// <param name="bufferSize">The size of the buffer to use during the copy operation.</param>
            void CopyToExactly(Stream destination, long copySize, int bufferSize);
        }
        public interface IStreamTryGetBuffer
        {
            /// <summary>
            /// Returns the internal buffer of the base <see cref="MemoryStream"/>, if available,
            /// sliced to this <see cref="Stream"/>'s range.
            /// </summary>
            /// <returns> <see langword="true"/> if the buffer is exposable; otherwise, <see langword="false"/>.</returns>
            bool TryGetBuffer(out ArraySegment<byte> buffer);
        }

        internal static int GetCopyBufferSize(Stream stream) // from .NET Foundation Stream.cs
        {
            int bufferSize = MemorySizes.OPTIMAL_BUFFER_SIZE;

            if (stream.CanSeek)
            {
                long length = stream.Length;
                long position = stream.Position;
                if (length > position)
                {
                    long remaining = length - position;
                    if (remaining > 0 && remaining < bufferSize)
                        bufferSize = (int)remaining;
                }
                else
                    bufferSize = 1;
            }

            return bufferSize;
        }
    }
}
