using AssetsTools.NET.Standard.Codecs;
using System;
using System.Buffers;
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
                    int bufferSize = copySize < 0 ?
                        MemorySizes.DEFAULT_FILESTREAM_BUFFER_SIZE : (copySize > MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE ?
                        MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE : (int)copySize);
                    FileStream new_fs = NewTempFileStream(bufferSize);
                    source.CopyToExactly(new_fs, copySize);
                    new_fs.Position = 0;
                    return new_fs;
                default:
                    throw new BackingStreamTypeNotImplementedException(bst);
            }
        }

        /// <summary>
        /// Copies all remaining bytes from the current position to the destination stream.
        /// </summary>
        /// <param name="source">The source stream from which bytes are read.</param>
        /// <param name="destination">The destination stream to which bytes are written.</param>
        public static void CopyToExactly(this Stream source, Stream destination)
        {
            ThrowIfCantRead(source);
            long copySize = source.Length - source.Position;
            ArgumentOutOfRangeException.ThrowIfNegative(copySize, nameof(copySize));

            CopyToExactly_Core(source, destination, copySize);
        }

        /// <summary>
        /// Copies a specified number of bytes from the current position to the provided destination stream.
        /// </summary>
        /// <param name="source">The stream to read bytes from.</param>
        /// <param name="destination">The stream to which bytes are written.</param>
        /// <param name="copySize">The exact number of bytes to copy.</param>
        public static void CopyToExactly(this Stream source, Stream destination, long copySize)
        {
            ThrowIfCantRead(source);
            ArgumentOutOfRangeException.ThrowIfNegative(copySize, nameof(copySize));

            CopyToExactly_Core(source, destination, copySize);
        }

        private static void CopyToExactly_Core(Stream source, Stream destination, long copySize)
        {
            ThrowIfCantWrite(destination);

            if (copySize == 0)
                return;

            if (source.TryReadBuffer_Core(copySize, out ReadOnlySpan<byte> buff))
            {
                //var pos = destination.Position;
                destination.Write(buff); // it does throw IOException
                //copySize -= destination.Position - pos;
                //if (copySize != 0)
                //    throw new IOException($"Unexpected End Of Stream during copy exact, {copySize} bytes left.");
                return;
            }

            if (source is SegmentStream ss)
            {
                ss.CopyToStream_Core(destination, copySize); // small optimization for SegmentStream
                return;
            }

            CopyToFSExactly_Core(source, destination, copySize);
        }

        /// <summary>
        /// Version of <see cref="CopyToExactly(Stream, Stream, long)"/> for source FileStreams and non-seekable source streams.
        /// </summary>
        /// <param name="source">The stream to read bytes from.</param>
        /// <param name="destination">The stream to which bytes are written.</param>
        /// <param name="copySize">The exact number of bytes to copy.</param>
        internal static void CopyToFSExactly(Stream source, Stream destination, long copySize)
        {
            ThrowIfCantRead(source);
            ThrowIfCantWrite(destination);
            ArgumentOutOfRangeException.ThrowIfNegative(copySize, nameof(copySize));

            if (copySize == 0)
                return;

            CopyToFSExactly_Core(source, destination, copySize);
        }

        private static void CopyToFSExactly_Core(Stream source, Stream destination, long copySize)
        {
            int fitBufferSize = copySize > MemorySizes.OPTIMAL_BUFFER_SIZE ? MemorySizes.OPTIMAL_BUFFER_SIZE : (int)copySize;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(fitBufferSize);
            try
            {
                Span<byte> bufferSpan = buffer;
                while (copySize > 0)
                {
                    int toRead = copySize >= bufferSpan.Length ? bufferSpan.Length : (int)copySize;

                    int bytesRead = source.Read(bufferSpan[..toRead]);
                    if (bytesRead == 0)
                        throw new IOException($"Unexpected End Of Stream during copy exact, {copySize} bytes missing to read.");

                    destination.Write(bufferSpan[..bytesRead]);
                    copySize -= bytesRead;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Try get the MemoryStream's internal buffer, starting at its current Position, and advancing it by <paramref name="accessSize"/>.
        /// </summary>
        public static bool TryReadBuffer(this Stream stream, long accessSize, out ReadOnlySpan<byte> buffer)
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

        private static bool TryReadBuffer_Core(this Stream stream, long accessSize, out ReadOnlySpan<byte> buffer)
        {
            ArraySegment<byte> arrSeg;
            long pos;

            switch (stream)
            {
                case MemoryStream ms:
                    if (!ms.TryGetBuffer(out arrSeg))
                    {
                        buffer = null;
                        return false;
                    }
                    pos = ms.Position;
                    break;
                case SegmentStream ss:
                    if (!ss.TryGetBuffer(out arrSeg))
                    {
                        buffer = null;
                        return false;
                    }
                    pos = ss.Position;
                    break;
                default:
                    buffer = null;
                    return false;
            }

            ArgumentOutOfRangeException.ThrowIfGreaterThan(pos, arrSeg.Count - accessSize, nameof(accessSize));

            buffer = arrSeg.AsSpan((int)pos, (int)accessSize);
            stream.Position += accessSize;
            return true;
        }

        public static Span<byte> WriteExactly(this Span<byte> target, Stream stream)
        {
            ThrowIfCantRead(stream);

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

        internal static void ThrowIfCantWrite(this Stream destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite)
            {
                if (destination.CanRead)
                    throw new NotSupportedException("Destination stream is read-only.");
                throw new ObjectDisposedException("Destination stream is disposed.");
            }
        }

        internal static void ThrowIfCantRead(this Stream source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!source.CanRead)
            {
                if (source.CanWrite)
                    throw new NotSupportedException("Source stream is write-only.");
                throw new ObjectDisposedException("Source stream is disposed.");
            }
        }
    }
}
