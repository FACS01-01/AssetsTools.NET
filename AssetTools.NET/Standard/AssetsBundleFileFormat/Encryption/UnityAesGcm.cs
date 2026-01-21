using AssetsTools.NET.Standard.Codecs;
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
        private const int TagSize = 16;

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
            if (maxCompressedSize > int.MaxValue - IVSize - sizeof(int) - TagSize)
                throw new ArgumentOutOfRangeException(nameof(input), "Not enough space to safely perform CompressAndEncrypt.");

            byte[] compressed = ArrayPool<byte>.Shared.Rent(maxCompressedSize);
            try
            {
                var len = CodecUtilities.CompressLZ4(input, compressed.AsSpan(0, maxCompressedSize), CompressionType.LZ4);

                var result = GC.AllocateUninitializedArray<byte>(IVSize + sizeof(int) + len + TagSize);

                Span<byte> iv = result.AsSpan(0, IVSize);
                RandomNumberGenerator.Fill(iv);

                var lenWrite = BitConverter.IsLittleEndian ?
                    len : BinaryPrimitives.ReverseEndianness(len); // always write little-endian
                Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(result.AsSpan(IVSize, sizeof(int))), lenWrite);

                var ciphertext = result.AsSpan(IVSize + sizeof(int), len);

                var tag = result.AsSpan(IVSize + sizeof(int) + len, TagSize);
                
                var plainCompressed = compressed.AsSpan(0, len);

                _aes.Encrypt(iv, plainCompressed, ciphertext, tag);

                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(compressed);
            }
        }

        public override void DecryptAndDecompress(ReadOnlySpan<byte> input, Span<byte> output, int blockIdx)
        {
            var iv = input[..IVSize];

            int len = BitConverter.ToInt32(input.Slice(IVSize, sizeof(int)));
            if (!BitConverter.IsLittleEndian) // always read little-endian
                len = BinaryPrimitives.ReverseEndianness(len);

            var ciphertext = input.Slice(IVSize + sizeof(int), len);

            var tag = input.Slice(IVSize + sizeof(int) + len, TagSize);

            byte[] plain = ArrayPool<byte>.Shared.Rent(len);
            try
            {
                var plainCompressed = plain.AsSpan(0, len);

                _aes.Decrypt(iv, ciphertext, tag, plainCompressed);

                CodecUtilities.DecompressLZ4(plainCompressed, output);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(plain);
            }
        }
    }
}
