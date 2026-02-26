using AssetsTools.NET.Standard.Codecs;
using AssetsTools.NET.Standard.IO;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AssetsTools.NET
{
    public sealed class UnityAesGcm : UnityCrypto<AesGcm>
    {
        private const int IVSize = 12;
        private const int LengthSize = sizeof(int);
        private const int TagSize = 16;
        private const int NonPlainTextBytes = IVSize + LengthSize + TagSize;
        private const int MaxEncryptionStepSize = MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE / 2;
        private const int MaxDecryptionStepSize = MaxEncryptionStepSize + NonPlainTextBytes;

        public override void SetDefaultHeader() { }

        public override void ReadHeaderFrom(BufferedBinaryReader reader) { }

        public override void CopyHeaderFrom(UnityCryptoBase toCopy) { }

        public override void WriteHeader(AssetsFileWriter writer) { }

        public override int GetHeaderSize() => 0;

        protected override void VerifyHexKey(string hexString)
        {
            if (hexString.Length != 64)
                throw new ArgumentException("GCM key must be 64 hex chars.");
        }

        protected override AesGcm CreateCryptoEngine(byte[] key) => new AesGcm(key, TagSize);

        public override bool SupportsCompression(CompressionType compressionType)
        {
            return compressionType switch
            {
                CompressionType.None or CompressionType.LZ4 or CompressionType.LZ4HC => true,
                _ => false
            };
        }

        public override CompressionType MainCompressionType() => CompressionType.LZ4;

        public override int MaxPlainBlockSize() => MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE;

        private static int CeilingDiv(int a, int b)
        {
            (int quo, int rem) = Math.DivRem(a, b);
            if (rem != 0)
                quo++;
            return quo;
        }

        protected override int CalculateEncryptionSize(int plainSize)
            => plainSize + CeilingDiv(plainSize, MaxEncryptionStepSize) * NonPlainTextBytes;

        private int CalculateDecryptionSize(int cipherSize)
            => cipherSize - CeilingDiv(cipherSize, MaxDecryptionStepSize) * NonPlainTextBytes;

        protected override int Encrypt(ReadOnlySpan<byte> plainData, Span<byte> cipherSpan, int blockIdx)
        {
            var plainSize = plainData.Length;
            int cursor = 0;
            int cipherCursor = 0;
            while (cursor < plainSize)
            {
                int cipherStepSize = plainSize - cursor;
                if (cipherStepSize > MaxEncryptionStepSize)
                    cipherStepSize = MaxEncryptionStepSize;

                EncryptStep(plainData.Slice(cursor, cipherStepSize), cipherSpan.Slice(cipherCursor, cipherStepSize + NonPlainTextBytes));
                
                cipherCursor += cipherStepSize + NonPlainTextBytes;
                cursor += cipherStepSize;
            }
            return cipherCursor; // must equal CalculateEncryptionSize(plainSize)
        }

        private void EncryptStep(ReadOnlySpan<byte> plainData, Span<byte> cipherSpan)
        {
            var len = plainData.Length;

            Span<byte> iv = cipherSpan[..IVSize];
            RandomNumberGenerator.Fill(iv);

            var lenWrite = BitConverter.IsLittleEndian ?
                len : BinaryPrimitives.ReverseEndianness(len); // always write little-endian
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(cipherSpan.Slice(IVSize, LengthSize)), lenWrite);

            var ciphertext = cipherSpan.Slice(IVSize + LengthSize, len);

            var tag = cipherSpan.Slice(IVSize + LengthSize + len, TagSize);

            CryptoEngine.Encrypt(iv, plainData, ciphertext, tag);
        }

        [SkipLocalsInit]
        protected override (int cipherSize, CompressionType revisedCompression) CompressAndEncrypt(ReadOnlySpan<byte> plainSpan, Stream compressedCipherStream, int blockIdx, CompressionType compressionType)
        {
            var maxCompressedSize = CodecUtilities.LZ4MaxCompressedSize(plainSpan.Length);
            var buffer = ArrayPool<byte>.Shared.Rent(maxCompressedSize + NonPlainTextBytes);
            try
            {
                var compressSpan = buffer.AsSpan(IVSize + LengthSize, maxCompressedSize + TagSize);
                var compressedSize = CodecUtilities.CompressLZ4(plainSpan, compressSpan, compressionType);

                Span<byte> backupBytes = stackalloc byte[TagSize];

                int cursor = 0;
                while (cursor < compressedSize)
                {
                    int cipherStepSize = compressedSize - cursor;
                    bool needsBackup = cipherStepSize > MaxEncryptionStepSize;
                    if (needsBackup)
                        cipherStepSize = MaxEncryptionStepSize;

                    var plainSlice = compressSpan.Slice(cursor, cipherStepSize);
                    if (needsBackup)
                        compressSpan.Slice(cursor + cipherStepSize, TagSize).CopyTo(backupBytes);

                    var compressedCipherSpan = buffer.AsSpan(cursor, cipherStepSize + NonPlainTextBytes);

                    EncryptStep(plainSlice, compressedCipherSpan);
                    compressedCipherStream.Write(compressedCipherSpan);

                    cursor += cipherStepSize;

                    if (needsBackup)
                        backupBytes.CopyTo(compressSpan.Slice(cursor, TagSize));
                }

                return (CalculateEncryptionSize(compressedSize), compressionType);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
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
                if (decipherStepSize > MaxDecryptionStepSize)
                    decipherStepSize = MaxDecryptionStepSize;
                var cipherStepSpan = cipherSpan.Slice(cursor, decipherStepSize);

                var iv = cipherStepSpan[..IVSize];

                int len = Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(cipherStepSpan.Slice(IVSize, LengthSize)));
                if (!BitConverter.IsLittleEndian) // always read little-endian
                    len = BinaryPrimitives.ReverseEndianness(len);

                var ciphertext = cipherStepSpan.Slice(IVSize + LengthSize, len);

                var tag = cipherStepSpan.Slice(IVSize + LengthSize + len, TagSize);

                CryptoEngine.Decrypt(iv, ciphertext, tag, plainSpan.Slice(plainCursor, len));

                plainCursor += len;
                cursor += decipherStepSize;
            }
        }

        protected override void DecryptAndDecompress(ReadOnlySpan<byte> compressedCipherSpan, CompressionType compressionType, Span<byte> plainSpan, int blockIdx)
        {
            int cipherSize = compressedCipherSpan.Length;
            int decipherSize = CalculateDecryptionSize(cipherSize);

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
