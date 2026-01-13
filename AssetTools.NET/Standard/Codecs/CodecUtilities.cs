using AssetsTools.NET.Standard.IO.Extensions;
using K4os.Compression.LZ4;
using SevenZip.Compression.LZMA;
using System;
using System.Buffers;
using System.IO;

namespace AssetsTools.NET.Standard.Codecs
{
    public static class CodecUtilities
    {
        public static Stream DecompressToNew(Stream compressedData, uint compressedSize, uint decompressedSize,
        CompressionType compressionType, BackingStreamType streamType, bool copyStreamIfUncompressed = true)
        {
            switch (compressionType)
            {
                case CompressionType.None:
                    if (copyStreamIfUncompressed)
                        return compressedData.CopyToNew(compressedSize, streamType);
                    return new SegmentStream(compressedData, compressedData.Position, compressedSize, false);
                case CompressionType.LZMA:
                    return DecompressLZMAToNew(compressedData, compressedSize, decompressedSize, streamType);
                case CompressionType.LZ4:
                case CompressionType.LZ4HC:
                    StreamExtensions.ThrowIfSizeBiggerThanMemStream(compressedSize);
                    StreamExtensions.ThrowIfSizeBiggerThanMemStream(decompressedSize);
                    return DecompressLZ4ToNew(compressedData, (int)compressedSize, (int)decompressedSize, streamType);
                default:
                    throw new CodecNotImplementedException(compressionType);
            }
        }

        public static void DecompressToStream(Stream compressedData, uint compressedSize, uint decompressedSize,
        CompressionType compressionType, Stream decompressStream)
        {
            switch (compressionType)
            {
                case CompressionType.None:
                    StreamExtensions.CopyToExactly(compressedData, decompressStream, compressedSize);
                    return;
                case CompressionType.LZMA:
                    DecompressLZMA(compressedData, decompressStream, compressedSize, decompressedSize);
                    return;
                case CompressionType.LZ4:
                case CompressionType.LZ4HC:
                    DecompressLZ4(compressedData, decompressStream, (int)compressedSize, (int)decompressedSize);
                    return;
                default:
                    throw new CodecNotImplementedException(compressionType);
            }
        }

        public static Stream DecompressLZ4ToNew(Stream compressedData, int compressedSize, int decompressedSize, BackingStreamType streamType)
        {
            switch (streamType)
            {
                case BackingStreamType.MemoryStream:
                    byte[] decompressArray = GC.AllocateUninitializedArray<byte>(decompressedSize);
                    DecompressLZ4(compressedData, compressedSize, decompressArray);
                    return decompressArray.NewExposedMemoryStream();
                case BackingStreamType.FileStream:
                    FileStream fs = StreamExtensions.NewTempFileStream(decompressedSize);
                    DecompressLZ4(compressedData, fs, compressedSize, decompressedSize);
                    fs.Position = 0;
                    return fs;
                default:
                    throw new BackingStreamTypeNotImplementedException(streamType);
            }
        }

        public static void DecompressLZ4(Stream compressedData, Stream decompressStream, int compressedSize, int decompressedSize)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(decompressedSize);
            try
            {
                Span<byte> decompressSpan = buffer.AsSpan(0, decompressedSize);
                DecompressLZ4(compressedData, compressedSize, decompressSpan);
                decompressStream.Write(decompressSpan);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static void DecompressLZ4(Stream compressedData, int compressedSize, Span<byte> decompressSpan)
        {
            if (compressedData.TryReadBuffer(compressedSize, out ReadOnlySpan<byte> compressedSpan))
            {
                DecompressLZ4(compressedSpan, decompressSpan);
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(compressedSize);
            try
            {
                var compressedSpan2 = buffer.AsSpan(0, compressedSize);
                compressedData.ReadExactly(compressedSpan2);
                DecompressLZ4(compressedSpan2, decompressSpan);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static void DecompressLZ4(ReadOnlySpan<byte> compressedData, Span<byte> decompressSpan)
        {
            var size = LZ4Codec.Decode(compressedData, decompressSpan);
            if (size != decompressSpan.Length)
                throw new Exception($"Decompressed size mismatch, expected {decompressSpan.Length}, got {size}");
        }

        public static Stream DecompressLZMAToNew(Stream compressedData, long compressedSize, long decompressedSize, BackingStreamType streamType)
        {
            Stream decompressStream;
            switch (streamType)
            {
                case BackingStreamType.MemoryStream:
                    StreamExtensions.ThrowIfSizeBiggerThanMemStream(decompressedSize);
                    decompressStream = StreamExtensions.NewExposedMemoryStream((int)decompressedSize);
                    break;
                case BackingStreamType.FileStream:
                    decompressStream = StreamExtensions.NewTempFileStream(decompressedSize);
                    break;
                default:
                    throw new BackingStreamTypeNotImplementedException(streamType);
            }

            DecompressLZMA(compressedData, decompressStream, compressedSize, decompressedSize);
            decompressStream.Position = 0;
            return decompressStream;
        }

        public static void DecompressLZMA(Stream compressedData, Stream decompressStream, long compressedSize, long decompressedSize)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(compressedSize, 5, nameof(compressedSize));
            if (compressedData.Length - compressedData.Position < compressedSize)
                throw new Exception($"Remaining data in {nameof(compressedData)} is less than {nameof(compressedSize)}.");

            var properties = new byte[5];
            compressedData.ReadExactly(properties);
            var decoder = new Decoder();
            decoder.SetDecoderProperties(properties);
            decoder.Code(compressedData, decompressStream, compressedSize - 5, decompressedSize, null);
        }

        public static Stream CompressToNew(Stream decompressedData, CompressionType compressionType, BackingStreamType streamType, bool copyStreamIfUncompressed = true)
        {
            long decompressedSize = decompressedData.Length - decompressedData.Position;
            return CompressToNew(decompressedData, decompressedSize, compressionType, streamType, copyStreamIfUncompressed);
        }

        public static Stream CompressToNew(Stream decompressedData, long decompressedSize, CompressionType compressionType, BackingStreamType streamType, bool copyStreamIfUncompressed = true)
        {
            switch (compressionType)
            {
                case CompressionType.None:
                    if (copyStreamIfUncompressed)
                        return decompressedData.CopyToNew(decompressedSize, streamType);
                    return new SegmentStream(decompressedData, decompressedData.Position, decompressedSize, false);
                case CompressionType.LZMA:
                    return CompressLZMAToNew(decompressedData, decompressedSize, streamType);
                case CompressionType.LZ4:
                case CompressionType.LZ4HC:
                    StreamExtensions.ThrowIfSizeBiggerThanMemStream(decompressedSize);
                    return CompressLZ4ToNew(decompressedData, (int)decompressedSize, compressionType, streamType);
                default:
                    throw new CodecNotImplementedException(compressionType);
            }
        }

        public static void CompressToStream(Stream decompressedData, CompressionType compressionType, Stream compressStream)
        {
            long decompressedSize = decompressedData.Length - decompressedData.Position;
            CompressToStream(decompressedData, decompressedSize, compressionType, compressStream);
        }

        public static void CompressToStream(Stream decompressedData, long decompressedSize, CompressionType compressionType, Stream compressStream)
        {
            switch (compressionType)
            {
                case CompressionType.None:
                    StreamExtensions.CopyToExactly(decompressedData, compressStream, decompressedSize);
                    break;
                case CompressionType.LZMA:
                    CompressLZMA(decompressedData, decompressedSize, compressStream);
                    break;
                case CompressionType.LZ4:
                case CompressionType.LZ4HC:
                    StreamExtensions.ThrowIfSizeBiggerThanMemStream(decompressedSize);
                    CompressLZ4(decompressedData, (int)decompressedSize, compressStream, compressionType);
                    break;
                default:
                    throw new CodecNotImplementedException(compressionType);
            }
        }

        public static Stream CompressLZ4ToNew(Stream decompressedData, int decompressedSize, CompressionType compressionLevel, BackingStreamType streamType)
        {
            switch (streamType)
            {
                case BackingStreamType.MemoryStream:
                    byte[] compressLZ4 = CompressLZ4ToArray(decompressedData, decompressedSize, compressionLevel);
                    return compressLZ4.NewExposedMemoryStream();
                case BackingStreamType.FileStream:
                    FileStream fs = StreamExtensions.NewTempFileStream(LZ4Codec.MaximumOutputSize(decompressedSize));
                    CompressLZ4(decompressedData, decompressedSize, fs, compressionLevel);
                    fs.Position = 0;
                    return fs;
                default:
                    throw new BackingStreamTypeNotImplementedException(streamType);
            }
        }

        public static byte[] CompressLZ4ToArray(Stream decompressedData, int decompressedSize, CompressionType compressionLevel)
        {
            if (decompressedData.TryReadBuffer(decompressedSize, out ReadOnlySpan<byte> decompressedSpan))
                return CompressLZ4ToArray(decompressedSpan, compressionLevel);

            var buffer = ArrayPool<byte>.Shared.Rent(decompressedSize);
            try
            {
                var decompressedSpan2 = buffer.AsSpan(0, decompressedSize);
                decompressedData.ReadExactly(decompressedSpan2);
                return CompressLZ4ToArray(decompressedSpan2, compressionLevel);
            }
            finally
            { 
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static byte[] CompressLZ4ToArray(ReadOnlySpan<byte> decompressedData, CompressionType compressionLevel)
        {
            int maxCompressedSize = LZ4Codec.MaximumOutputSize(decompressedData.Length);

            var buffer = ArrayPool<byte>.Shared.Rent(maxCompressedSize);
            try
            {
                Span<byte> maxCompressSpan = buffer;
                int compressedSize = CompressLZ4(decompressedData, maxCompressSpan, compressionLevel);
                byte[] finalCompressArray = GC.AllocateUninitializedArray<byte>(compressedSize);
                Span<byte> compressSpan = maxCompressSpan[..compressedSize];
                compressSpan.CopyTo(finalCompressArray);
                return finalCompressArray;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static bool TryCompressLZ4(Stream decompressedData, int decompressedSize, CompressionType compressionLevel, out byte[] result)
        {
            if (decompressedData.TryReadBuffer(decompressedSize, out ReadOnlySpan<byte> decompressedSpan))
                return TryCompressLZ4(decompressedSpan, compressionLevel, out result);

            var buffer = ArrayPool<byte>.Shared.Rent(decompressedSize);
            try
            {
                var decompressedSpan2 = buffer.AsSpan(0, decompressedSize);
                decompressedData.ReadExactly(decompressedSpan2);
                return TryCompressLZ4(decompressedSpan2, compressionLevel, out result);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static bool TryCompressLZ4(ReadOnlySpan<byte> decompressedData, CompressionType compressionLevel, out byte[] result)
        {
            int maxCompressedSize = LZ4Codec.MaximumOutputSize(decompressedData.Length);

            var buffer = ArrayPool<byte>.Shared.Rent(maxCompressedSize);
            try
            {
                Span<byte> maxCompressSpan = buffer;
                int compressedSize = CompressLZ4(decompressedData, maxCompressSpan, compressionLevel);

                if (compressedSize < decompressedData.Length)
                {
                    result = GC.AllocateUninitializedArray<byte>(compressedSize);
                    Span<byte> compressSpan = maxCompressSpan[..compressedSize];
                    compressSpan.CopyTo(result);
                    return true;
                }

                result = GC.AllocateUninitializedArray<byte>(decompressedData.Length);
                decompressedData.CopyTo(result);
                return false;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static int CompressLZ4(Stream decompressedData, int decompressedSize, Stream compressStream, CompressionType compressionLevel)
        {
            if (decompressedData.TryReadBuffer(decompressedSize, out ReadOnlySpan<byte> decompressedSpan))
                return CompressLZ4(decompressedSpan, compressStream, compressionLevel);

            var buffer = ArrayPool<byte>.Shared.Rent(decompressedSize);
            try
            {
                var decompressedSpan2 = buffer.AsSpan(0, decompressedSize);
                decompressedData.ReadExactly(decompressedSpan2);
                return CompressLZ4(decompressedSpan2, compressStream, compressionLevel);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static int CompressLZ4(ReadOnlySpan<byte> decompressedData, Stream compressStream, CompressionType compressionLevel)
        {
            int maxCompressedSize = LZ4Codec.MaximumOutputSize(decompressedData.Length);

            var buffer = ArrayPool<byte>.Shared.Rent(maxCompressedSize);
            try
            {
                Span<byte> maxCompressSpan = buffer;
                int compressedSize = CompressLZ4(decompressedData, maxCompressSpan, compressionLevel);
                compressStream.Write(maxCompressSpan[..compressedSize]);
                return compressedSize;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static int CompressLZ4(ReadOnlySpan<byte> decompressedData, Span<byte> compressSpan, CompressionType compressionLevel)
        {
            LZ4Level comprLvl = compressionLevel == CompressionType.LZ4HC ? LZ4Level.L12_MAX : LZ4Level.L00_FAST;
            int compressedSize = LZ4Codec.Encode(decompressedData, compressSpan, comprLvl);
            if (compressedSize < 0)
                throw new Exception($"{nameof(compressSpan)} size too small to hold encoded {nameof(decompressedData)}.");
            return compressedSize;
        }

        public static Stream CompressLZMAToNew(Stream decompressedData, long decompressedSize, BackingStreamType streamType)
        {
            switch (streamType)
            {
                case BackingStreamType.MemoryStream:
                    StreamExtensions.ThrowIfSizeBiggerThanMemStream(decompressedSize);
                    byte[] compressLZMA = CompressLZMA(decompressedData, (int)decompressedSize);
                    return compressLZMA.NewExposedMemoryStream();
                case BackingStreamType.FileStream:
                    var compressStream = StreamExtensions.NewTempFileStream();
                    CompressLZMA(decompressedData, decompressedSize, compressStream);
                    compressStream.Position = 0;
                    return compressStream;
                default:
                    throw new BackingStreamTypeNotImplementedException(streamType);
            }
        }

        public static byte[] CompressLZMA(Stream decompressedData, int decompressedSize)
        {
            var BadCompressionMargin = decompressedSize / 20;
            var buffer = ArrayPool<byte>.Shared.Rent(decompressedSize + BadCompressionMargin);
            try
            {
                using MemoryStream compressStream = buffer.NewExposedMemoryStream();
                int compressedSize = (int)CompressLZMA(decompressedData, decompressedSize, compressStream);
                byte[] compressArray = GC.AllocateUninitializedArray<byte>(compressedSize);
                buffer.AsSpan(0, compressedSize).CopyTo(compressArray);
                return compressArray;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static long CompressLZMA(Stream decompressedData, Stream compressStream, SevenZip.ICodeProgress? progress = null) =>
            CompressLZMA(decompressedData, -1L, compressStream, progress);

        public static long CompressLZMA(Stream decompressedData, long decompressedSize, Stream compressStream, SevenZip.ICodeProgress? progress = null)
        {
            var encoder = new Encoder();
            long compressedSize = compressStream.Position;
            encoder.WriteCoderProperties(compressStream);
            encoder.Code(decompressedData, compressStream, decompressedSize, -1L, progress);
            compressedSize = compressStream.Position - compressedSize;
            return compressedSize;
        }
    }

    public enum CompressionType : byte
    {
        None = 0x00,
        LZMA = 0x01,
        LZ4 = 0x02,
        LZ4HC = 0x03
    }

    public enum BackingStreamType : byte
    {
        MemoryStream,
        FileStream
    }

    internal class CodecNotImplementedException(CompressionType compressionType) :
        NotImplementedException($"Codec not implemented: {compressionType}");
    internal class BackingStreamTypeNotImplementedException(BackingStreamType streamType) :
        NotImplementedException($"BackingStreamType not implemented: {streamType}");
}
