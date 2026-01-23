using AssetsTools.NET.Standard.Codecs;
using AssetsTools.NET.Standard.IO;
using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.Collections.Generic;
using System.IO;

namespace AssetsTools.NET.IO
{
    public class ReadOnlyBlockStream : Stream
    {
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

            var blockInfosLength = blockInfos.Length;
            cumulativeCompressedSizes = GC.AllocateUninitializedArray<long>(blockInfosLength);
            blockCompressions = GC.AllocateUninitializedArray<CompressionType>(blockInfosLength);
            cumulativeDecompressedSizes = GC.AllocateUninitializedArray<long>(blockInfosLength);

            uint firstDecompressedChunkSize = blockInfos[0].DecompressedSize;
            long cumulativeCompressedSize = 0;
            long cumulativeDecompressedSize = 0;

            for (int i = 0; i < blockInfosLength; i++)
            {
                var blockInfo = blockInfos[i];

                cumulativeCompressedSize += blockInfo.CompressedSize;
                if (cumulativeCompressedSize < 0)
                    throw new OverflowException("Cumulative compressed size overflowed.");
                cumulativeCompressedSizes[i] = cumulativeCompressedSize;

                cumulativeDecompressedSize += blockInfo.DecompressedSize;
                if (cumulativeDecompressedSize < 0)
                    throw new OverflowException("Cumulative decompressed size overflowed.");
                cumulativeDecompressedSizes[i] = cumulativeDecompressedSize;

                blockCompressions[i] = blockInfo.GetCompressionType();

                if (hasConstantDecompressedChunkSize &&
                    blockInfo.DecompressedSize != firstDecompressedChunkSize &&
                    i != blockInfosLength - 1)
                    hasConstantDecompressedChunkSize = false;
            }
            _length = cumulativeDecompressedSize;

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

        private readonly long BaseOffset; // offset in base compressed stream
        private readonly long _length; // length of decompressed blocks
        public override long Length // length of decompressed blocks
        {
            get
            {
                ObjectDisposedException.ThrowIf(!CanSeek, this);
                return _length;
            }
        }

        private long _position; // position over decompressed blocks
        /// <summary>
        /// The decompressed block index that contains the current <see cref="Position"/>.
        /// </summary>
        /// <remarks>
        /// If <see cref="Position"/> is at the end of the decompressed Blocks Stream,
        /// this will be equal to the block count.
        /// </remarks>
        private int currentBlockIdx;
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
                    currentBlockIdx = cumulativeCompressedSizes.Length;
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
            ArgumentOutOfRangeException.ThrowIfNegative(finalPos, nameof(offset)); // negative and overflow position

            Position = finalPos;
            return finalPos;
        }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count);


        //
        public override int Read(Span<byte> buffer);
        public override int ReadByte();
        public override void CopyTo(Stream destination, int bufferSize);
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

        //

        private Stream LoadBlock(int blockIdx)
        {
            if (cacheInMemory.TryGetValue(blockIdx, out var ms))
                return ms;
            if (cacheInFile.TryGetValue(blockIdx, out var ss))
                return ss;

            var blockOffset = blockIdx == 0 ? 0 : cumulativeCompressedSizes[blockIdx - 1];
            var compressedSize = cumulativeCompressedSizes[blockIdx] - blockOffset;
            var decompressedSize = hasConstantDecompressedChunkSize ? constantDecompressedChunkSize
                : cumulativeDecompressedSizes[blockIdx] - (blockIdx == 0 ? 0 : cumulativeDecompressedSizes[blockIdx - 1]);

            _baseStream.Position = BaseOffset + blockOffset;

            BackingStreamType backingStreamType = decompressedSize <= MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE ?
                BackingStreamType.MemoryStream : BackingStreamType.FileStream;

            if (backingStreamType == BackingStreamType.MemoryStream)
            {
                var newBuffer = GC.AllocateUninitializedArray<byte>((int)decompressedSize);
                var newms = newBuffer.NewExposedMemoryStream();

                if (decryptor != null)
                {
                    //
                    decryptor.DecryptAndDecompress();
                }
                else
                {
                    CodecUtilities.DecompressToStream(_baseStream, compressedSize, decompressedSize,
                        blockCompressions[blockIdx], newms);
                }

                cacheInMemory[blockIdx] = newms;
                return newms;
            }
            else
            {
                var tempCacheFileOffset = tempCacheFile.Seek(0, SeekOrigin.End);

                if (decryptor != null)
                {
                    //
                }
                else
                {
                    CodecUtilities.DecompressToStream(_baseStream, compressedSize, decompressedSize,
                        blockCompressions[blockIdx], tempCacheFile);
                }

                var newss = new SegmentStream(tempCacheFile, tempCacheFileOffset, decompressedSize);
                cacheInFile[blockIdx] = newss;
                return newss;
            }
        }



    }
}
