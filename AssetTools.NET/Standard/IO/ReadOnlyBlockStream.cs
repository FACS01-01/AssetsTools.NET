using AssetsTools.NET.Standard.Codecs;
using AssetsTools.NET.Standard.IO;
using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.Collections.Generic;
using System.IO;

namespace AssetsTools.NET.IO
{
    /// <summary>
    /// Provides a read-only stream that exposes decompressed data from a sequence of compressed AssetBundle blocks,
    /// supporting random access and efficient block-level caching.
    /// </summary>
    public class ReadOnlyBlockStream : Stream
    {
        /// <inheritdoc cref="MemoryCacheMaxSize"/>
        private static int _memoryCacheMaxSize = 0x3200000; // 50MB
        private static int MaxDecompressedBlockSizeToCacheInMemory = MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE;
        /// <summary>
        /// Max amount of decompressed block bytes to keep loaded on cached MemoryStreams.
        /// </summary>
        /// <remarks>
        /// When there is no enough free cache to load a new block, all cached blocks in memory will be dumped to a temporary <see cref="FileStream"/>.
        /// </remarks>
        public static int MemoryCacheMaxSize
        {
            get => _memoryCacheMaxSize;
            set
            {
                if (_memoryCacheMaxSize == value)
                    return;
                _memoryCacheMaxSize = value;
                MaxDecompressedBlockSizeToCacheInMemory = _memoryCacheMaxSize <= MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE ?
                    _memoryCacheMaxSize :
                    MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE;
            }
        }
        private int cacheSize = 0;

        private readonly long[] cumulativeCompressedSizes;
        private readonly long[] cumulativeDecompressedSizes;
        private readonly CompressionType[] blockCompressions;
        private readonly bool hasConstantDecompressedChunkSize = true;
        private readonly long constantDecompressedChunkSize = -1;
        private UnityCryptoBase decryptor;
        private Dictionary<int, MemoryStream> cacheInMemory;
        private Dictionary<int, SegmentStream> cacheInFile;
        private FileStream tempCacheFile;

        public ReadOnlyBlockStream(Stream baseStream, long baseOffset, AssetBundleBlockInfo[] blockInfos,
            UnityCryptoBase decryptor = null)
        {
            baseStream.ThrowIfCantSeek();

            var baseStreamLength = baseStream.Length;
            if (baseOffset < 0 || baseOffset > baseStreamLength)
                throw new ArgumentOutOfRangeException(nameof(baseOffset));

            ArgumentNullException.ThrowIfNull(blockInfos, nameof(blockInfos));
            if (blockInfos.Length == 0)
                throw new ArgumentException("Block infos array cannot be empty.", nameof(blockInfos));

            _baseStream = baseStream;
            BaseOffset = baseOffset;
            this.decryptor = decryptor;
            cacheInMemory = new();
            cacheInFile = new();
            tempCacheFile = StreamExtensions.NewTempFileStream(MemorySizes.OPTIMAL_BUFFER_SIZE);

            BlockCount = blockInfos.Length;
            cumulativeCompressedSizes = GC.AllocateUninitializedArray<long>(BlockCount);
            blockCompressions = GC.AllocateUninitializedArray<CompressionType>(BlockCount);
            cumulativeDecompressedSizes = GC.AllocateUninitializedArray<long>(BlockCount);

            uint firstDecompressedChunkSize = 0;
            foreach (var bi in blockInfos)
            {
                firstDecompressedChunkSize = bi.DecompressedSize;
                if (firstDecompressedChunkSize != 0)
                    break;
            }
            if (firstDecompressedChunkSize == 0)
                throw new ArgumentException("All blocks have 0 decompressed size.", nameof(blockInfos));

            long cumulativeCompressedSize = 0;
            long cumulativeDecompressedSize = 0;

            int blocksWith0DecompressedSize = 0;
            for (int i = 0; i < BlockCount; i++)
            {
                var blockInfo = blockInfos[i + blocksWith0DecompressedSize];

                cumulativeCompressedSize += blockInfo.CompressedSize;
                if (cumulativeCompressedSize < 0)
                    throw new OverflowException("Cumulative compressed size overflowed.");

                cumulativeDecompressedSize += blockInfo.DecompressedSize;
                if (cumulativeDecompressedSize < 0)
                    throw new OverflowException("Cumulative decompressed size overflowed.");

                if (blockInfo.DecompressedSize == 0)
                {
                    blocksWith0DecompressedSize++;
                    BlockCount--;
                    i--;
                }
                else
                {
                    cumulativeCompressedSizes[i] = cumulativeCompressedSize;
                    cumulativeDecompressedSizes[i] = cumulativeDecompressedSize;

                    blockCompressions[i] = blockInfo.GetCompressionType();

                    if (hasConstantDecompressedChunkSize &&
                        blockInfo.DecompressedSize != firstDecompressedChunkSize &&
                        i != BlockCount - 1)
                        hasConstantDecompressedChunkSize = false;
                }
            }
            _length = cumulativeDecompressedSize;

            if (blocksWith0DecompressedSize != 0)
            {
                var old_cumulativeCompressedSizes = cumulativeCompressedSizes;
                var old_blockCompressions = blockCompressions;

                cumulativeCompressedSizes = GC.AllocateUninitializedArray<long>(BlockCount);
                blockCompressions = GC.AllocateUninitializedArray<CompressionType>(BlockCount);

                Array.Copy(old_cumulativeCompressedSizes, cumulativeCompressedSizes, BlockCount);
                Array.Copy(old_blockCompressions, blockCompressions, BlockCount);

                if (!hasConstantDecompressedChunkSize)
                {
                    var old_cumulativeDecompressedSizes = cumulativeDecompressedSizes;
                    cumulativeDecompressedSizes = GC.AllocateUninitializedArray<long>(BlockCount);
                    Array.Copy(old_cumulativeDecompressedSizes, cumulativeDecompressedSizes, BlockCount);
                }
            }
            if (hasConstantDecompressedChunkSize)
            {
                constantDecompressedChunkSize = firstDecompressedChunkSize;
                cumulativeDecompressedSizes = [];
            }
        }

        private Stream? _baseStream;
        /*public Stream BaseStream
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _baseStream;
            }
        }*/

        /// <summary>
        /// Offset from where to start reading in <see cref="_baseStream"/> (base compressed stream).
        /// </summary>
        private readonly long BaseOffset;
        /// <summary>
        /// Total length of decompressed blocks.
        /// </summary>
        private readonly long _length;
        public override long Length // length of decompressed blocks
        {
            get
            {
                ObjectDisposedException.ThrowIf(!CanSeek, this);
                return _length;
            }
        }

        /// <summary>
        /// Position over decompressed blocks.
        /// </summary>
        private long _position;
        /// <summary>
        /// The decompressed block index that contains the current <see cref="Position"/>.
        /// </summary>
        /// <remarks>
        /// If <see cref="Position"/> is at the end of the decompressed Blocks Stream,
        /// this will be equal to <see cref="BlockCount"/>.
        /// </remarks>
        private int currentBlockIdx;
        private readonly int BlockCount; 
        public override long Position // position over decompressed blocks
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
                if (value < 0)
                    throw new IOException("Can't position before the start of the stream.");
                _position = value;
                UpdateCurrentBlockIdx();
            }
        }
        private void UpdateCurrentBlockIdx()
        {
            if (hasConstantDecompressedChunkSize)
            {
                if (_position >= _length)
                    currentBlockIdx = BlockCount;
                else
                    currentBlockIdx = (int)(_position / constantDecompressedChunkSize);
                return;
            }

            var streamIdx = cumulativeDecompressedSizes.BinarySearch(_position);
            streamIdx = streamIdx < 0 ? ~streamIdx : streamIdx + 1;
            currentBlockIdx = streamIdx;
        }

        public override bool CanRead => !_disposed && _baseStream.CanRead;
        public override bool CanWrite => false;
        public override bool CanSeek => !_disposed && _baseStream.CanSeek;
        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long originPos = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => _position,
                SeekOrigin.End => _length,
                _ => throw new ArgumentException("Invalid Seek origin request.")
            };

            long finalPos = originPos + offset;
            Position = finalPos;
            return finalPos;
        }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StreamExtensions.ThrowIfInvalidBufferSegment(buffer, offset, count);

            if (count == 0)
                return 0;
            if (currentBlockIdx == BlockCount) // no data left
                return 0;

            var currentStream = LoadBlock(currentBlockIdx);
            currentStream.Position = _position - CumulativeLength(currentBlockIdx);

            int totalRead = 0;
            do
            {
                int read = currentStream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                    throw new IOException($"Block #{currentBlockIdx + 1}/{BlockCount} returned 0 bytes read before reaching its end.");

                _position += read;
                totalRead += read;

                if (_position >= CumulativeLength(currentBlockIdx + 1))
                {
                    currentBlockIdx++;
                    if (currentBlockIdx == BlockCount)
                        return totalRead;
                    if (count > totalRead)
                    {
                        currentStream = LoadBlock(currentBlockIdx);
                        currentStream.Position = 0;
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
            if (currentBlockIdx == BlockCount) // no data left
                return 0;

            var currentStream = LoadBlock(currentBlockIdx);
            currentStream.Position = _position - CumulativeLength(currentBlockIdx);

            int totalRead = 0;
            int read = 0;
            do
            {
                buffer = buffer[read..];
                read = currentStream.Read(buffer);
                if (read == 0)
                    throw new IOException($"Block #{currentBlockIdx + 1}/{BlockCount} returned 0 bytes read before reaching its end.");

                _position += read;
                totalRead += read;

                if (_position >= CumulativeLength(currentBlockIdx + 1))
                {
                    currentBlockIdx++;
                    if (currentBlockIdx == BlockCount)
                        return totalRead;
                    if (count > totalRead)
                    {
                        currentStream = LoadBlock(currentBlockIdx);
                        currentStream.Position = 0;
                    }
                }
            }
            while (count > totalRead);

            return totalRead;
        }

        public override int ReadByte()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (currentBlockIdx == BlockCount) // no data left
                return -1;

            var currentStream = LoadBlock(currentBlockIdx);
            currentStream.Position = _position - CumulativeLength(currentBlockIdx);

            var b = currentStream.ReadByte();
            if (b == -1)
                throw new IOException($"Block #{currentBlockIdx + 1}/{BlockCount} returned 0 bytes read before reaching its end.");

            _position += sizeof(byte);
            if (_position >= CumulativeLength(currentBlockIdx + 1))
                currentBlockIdx++;

            return b;
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            destination.ThrowIfCantWrite();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize, nameof(bufferSize));

            if (currentBlockIdx == BlockCount) // no data left
                return;

            long currentStreamPos = _position - CumulativeLength(currentBlockIdx);
            for (; currentBlockIdx < BlockCount; currentBlockIdx++)
            {
                var currentStream = LoadBlock(currentBlockIdx);

                var nextAcumLength = CumulativeLength(currentBlockIdx + 1);
                if (_position < nextAcumLength)
                {
                    currentStream.Position = currentStreamPos;
                    currentStream.CopyTo(destination, bufferSize);
                }

                _position = nextAcumLength;
                currentStreamPos = 0;
            }
        }

        private bool _disposed;
        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _baseStream = null; // don't Dispose the base stream

                foreach (var ms in cacheInMemory.Values)
                    ms.Dispose();
                cacheInMemory.Clear();
                cacheInMemory = null;

                foreach (var ss in cacheInFile.Values)
                    ss.Dispose();
                cacheInFile.Clear();
                cacheInFile = null;

                tempCacheFile.Dispose();
                tempCacheFile = null;

                decryptor = null;
            }

            _disposed = true;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
        public override void WriteByte(byte value) => throw new NotSupportedException();

        private Stream LoadBlock(int blockIdx)
        {
            if (cacheInMemory.TryGetValue(blockIdx, out var ms))
                return ms;
            if (cacheInFile.TryGetValue(blockIdx, out var ss))
                return ss;

            var decompressedSize = hasConstantDecompressedChunkSize ? constantDecompressedChunkSize :
                blockIdx == 0 ? cumulativeDecompressedSizes[0] :
                cumulativeDecompressedSizes[blockIdx] - cumulativeDecompressedSizes[blockIdx - 1];


            BackingStreamType backingStreamType = decompressedSize <= MaxDecompressedBlockSizeToCacheInMemory ?
                BackingStreamType.MemoryStream : BackingStreamType.FileStream;

            if (backingStreamType == BackingStreamType.MemoryStream)
                return CacheNewBlockInMemory(blockIdx, (int)decompressedSize);

            var tempCacheFileOffset = tempCacheFile.Seek(0, SeekOrigin.End);
            ProcessBlockInto(blockIdx, decompressedSize, tempCacheFile);
            var newss = new SegmentStream(tempCacheFile, tempCacheFileOffset, decompressedSize);
            cacheInFile[blockIdx] = newss;
            return newss;
        }

        private MemoryStream CacheNewBlockInMemory(int blockIdx, int decompressedSize)
        {
            if (_memoryCacheMaxSize - cacheSize < decompressedSize)
                DumpMemoryCacheToTempFile();

            var newBuffer = GC.AllocateUninitializedArray<byte>(decompressedSize);
            var newms = newBuffer.NewExposedMemoryStream();

            ProcessBlockInto(blockIdx, decompressedSize, newms);

            cacheInMemory[blockIdx] = newms;
            cacheSize += decompressedSize;
            return newms;
        }

        private void DumpMemoryCacheToTempFile()
        {
            var tempCacheFileOffset = tempCacheFile.Seek(0, SeekOrigin.End);
            foreach (var memCache in cacheInMemory)
            {
                var ms = memCache.Value;
                ms.Position = 0;
                var memSize = ms.Length;
                ms.CopyTo(tempCacheFile);
                var newss = new SegmentStream(tempCacheFile, tempCacheFileOffset, memSize);
                cacheInFile[memCache.Key] = newss;
                tempCacheFileOffset += memSize;
                ms.Dispose();
            }
            cacheInMemory.Clear();
            cacheSize = 0;
        }

        private void ProcessBlockInto(int blockIdx, Stream destination)
        {
            long decompressedSize = hasConstantDecompressedChunkSize ? constantDecompressedChunkSize :
                blockIdx == 0 ? cumulativeDecompressedSizes[0] :
                cumulativeDecompressedSizes[blockIdx] - cumulativeDecompressedSizes[blockIdx - 1];
            ProcessBlockInto(blockIdx, decompressedSize, destination);
        }
        private void ProcessBlockInto(int blockIdx, long decompressedSize, Stream destination)
        {
            var blockOffset = blockIdx == 0 ? 0 : cumulativeCompressedSizes[blockIdx - 1];
            var compressedSize = cumulativeCompressedSizes[blockIdx] - blockOffset;

            _baseStream.Position = BaseOffset + blockOffset;

            if (decryptor != null)
            {
                decryptor.DecryptAndDecompress(_baseStream, compressedSize, decompressedSize,
                    blockCompressions[blockIdx], destination, blockIdx);
            }
            else
            {
                CodecUtilities.DecompressToStream(_baseStream, compressedSize, decompressedSize,
                    blockCompressions[blockIdx], destination);
            }
        }

        private long CumulativeLength(int upToBlockCount)
        {
            if (upToBlockCount <= 0)
                return 0;
            return hasConstantDecompressedChunkSize ?
                constantDecompressedChunkSize * upToBlockCount :
                (currentBlockIdx == 0 ? 0 : cumulativeDecompressedSizes[upToBlockCount - 1]);
        }
    }
}
