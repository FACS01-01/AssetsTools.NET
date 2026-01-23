using AssetsTools.NET.Standard.Codecs;
using AssetsTools.NET.Standard.IO;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AssetsTools.NET
{
    public sealed class UnityAesGcm : UnityCryptoBase
    {
        private const int IVSize = 12;
        private const int LengthSize = sizeof(int);
        private const int TagSize = 16;
        private const int NonPlainTextBytes = IVSize + LengthSize + TagSize;
        private const int MaxEncryptionStepSize = MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE / 2 + NonPlainTextBytes;

        private AesGcm _aes;

        protected override void VerifyHexKey(string hexString)
        {
            if (hexString.Length != 64)
                throw new ArgumentException("GCM key must be 64 hex chars.");
        }

        protected override IDisposable CreateCryptoEngine(byte[] key)
        {
            _aes = new AesGcm(key, TagSize);
            return _aes;
        }

        public override byte[] CompressAndEncrypt(ReadOnlySpan<byte> input, int blockIdx)
        {
            var maxCompressedSize = CodecUtilities.LZ4MaxCompressedSize(input.Length);
            if (maxCompressedSize > int.MaxValue - NonPlainTextBytes)
                throw new ArgumentOutOfRangeException(nameof(input), "Not enough space to safely perform CompressAndEncrypt.");

            byte[] compressed = ArrayPool<byte>.Shared.Rent(maxCompressedSize);
            try
            {
                var len = CodecUtilities.CompressLZ4(input, compressed.AsSpan(0, maxCompressedSize), CompressionType.LZ4);

                var result = GC.AllocateUninitializedArray<byte>(NonPlainTextBytes + len);

                Span<byte> iv = result.AsSpan(0, IVSize);
                RandomNumberGenerator.Fill(iv);

                var lenWrite = BitConverter.IsLittleEndian ?
                    len : BinaryPrimitives.ReverseEndianness(len); // always write little-endian
                Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(result.AsSpan(IVSize, LengthSize)), lenWrite);

                var ciphertext = result.AsSpan(IVSize + LengthSize, len);

                var tag = result.AsSpan(IVSize + LengthSize + len, TagSize);
                
                var plainCompressed = compressed.AsSpan(0, len);

                _aes.Encrypt(iv, plainCompressed, ciphertext, tag);

                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(compressed);
            }
        }

        protected override void Decrypt(ReadOnlySpan<byte> cipherSpan, Span<byte> plainSpan, int blockIdx)
        {
            var cipherSize = cipherSpan.Length;
            int cursor = 0;
            int plainCursor = 0;
            while (cursor < cipherSize)
            {
                int decipherStepSize = cipherSize - cursor;
                if (decipherStepSize > MaxEncryptionStepSize)
                    decipherStepSize = MaxEncryptionStepSize;
                var cipherStepSpan = cipherSpan.Slice(cursor, decipherStepSize);

                var iv = cipherStepSpan.Slice(0, IVSize);

                int len = BitConverter.ToInt32(cipherStepSpan.Slice(IVSize, LengthSize));
                if (!BitConverter.IsLittleEndian) // always read little-endian
                    len = BinaryPrimitives.ReverseEndianness(len);

                var ciphertext = cipherStepSpan.Slice(IVSize + LengthSize, len);

                var tag = cipherStepSpan.Slice(IVSize + LengthSize + len, TagSize);

                _aes.Decrypt(iv, ciphertext, tag, plainSpan.Slice(plainCursor, len));

                plainCursor += len;
                cursor += decipherStepSize;
            }
        }

        protected override void DecryptAndDecompress(ReadOnlySpan<byte> compressedCipherSpan, Span<byte> plainSpan, int blockIdx)
        {
            int cipherSize = compressedCipherSpan.Length;
            int decipherSteps = (cipherSize + MaxEncryptionStepSize - 1) / MaxEncryptionStepSize; // ceiling division of (cipherSize / MaxEncryptionStepSize)
            int decipherSize = cipherSize - decipherSteps * NonPlainTextBytes;

            byte[] compressedArr = ArrayPool<byte>.Shared.Rent(decipherSize);
            try
            {
                var compressedSpan = compressedArr.AsSpan(0, decipherSize);
                Decrypt(compressedCipherSpan, compressedSpan, blockIdx);
                CodecUtilities.DecompressLZ4(compressedSpan, plainSpan);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(compressedArr);
            }
        }
    }
}
