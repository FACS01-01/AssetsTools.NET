using AssetsTools.NET.Standard.Codecs;
using AssetsTools.NET.Standard.IO;
using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace AssetsTools.NET
{
    public sealed class UnityCN : UnityCrypto<ICryptoTransform>
    {
        // for default header
        private const string Signature = "#$unity3dchina!@";
        private static readonly byte[] DefaultInfoBytes = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xA6, 0xB1, 0xDE, 0x48, 0x9E, 0x2B, 0x53, 0x5C];
        private static readonly byte[] DefaultInfoKey = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];
        private static readonly byte[] DefaultSignatureBytes = Encoding.UTF8.GetBytes(Signature);
        private static readonly byte[] DefaultSignatureKey = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];

        // for instance header
        public uint Value;
        public byte[] InfoBytes { get; private set; } = new byte[16];
        public byte[] InfoKey { get; private set; } = new byte[16];
        public byte DummyByte1;
        public byte[] SignatureBytes { get; private set; } = new byte[16];
        public byte[] SignatureKey { get; private set; } = new byte[16];
        public byte DummyByte2;
        private bool _initBytes = false;

        // for crypto engine
        private byte[] Index = new byte[16];
        private byte[] Sub = new byte[16];
        private bool _isIndexSpecial = false;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                InfoBytes = null;
                InfoKey = null;
                SignatureBytes = null;
                SignatureKey = null;
                Index = null;
                Sub = null;
            }
        }

        public override void SetDefaultHeader()
        {
            Value = 0;
            DefaultInfoBytes.CopyTo(InfoBytes);
            DefaultInfoKey.CopyTo(InfoKey);
            DummyByte1 = 0;
            DefaultSignatureBytes.CopyTo(SignatureBytes);
            DefaultSignatureKey.CopyTo(SignatureKey);
            DummyByte2 = 0;
        }

        public override void ReadHeaderFrom(BufferedBinaryReader reader)
        {
            reader.BigEndian = true;

            Value = reader.ReadUInt32();
            reader.ReadExactly(InfoBytes);
            reader.ReadExactly(InfoKey);
            DummyByte1 = reader.ReadByte();
            reader.ReadExactly(SignatureBytes);
            reader.ReadExactly(SignatureKey);
            DummyByte2 = reader.ReadByte();

            _initBytes = true;
        }

        public override void CopyHeaderFrom(UnityCryptoBase toCopy)
        {
            if (toCopy is UnityCN toCopyCN)
            {
                Value = toCopyCN.Value;
                toCopyCN.InfoBytes.CopyTo(InfoBytes);
                toCopyCN.InfoKey.CopyTo(InfoKey);
                DummyByte1 = toCopyCN.DummyByte1;
                toCopyCN.SignatureBytes.CopyTo(SignatureBytes);
                toCopyCN.SignatureKey.CopyTo(SignatureKey);
                DummyByte2 = toCopyCN.DummyByte2;

                _initBytes = toCopyCN._initBytes;
            }
            if (this.GetHeaderSize() > toCopy.GetHeaderSize())
                return; // or throw?
            //todo?
        }

        public override void WriteHeader(AssetsFileWriter writer)
        {
            writer.BigEndian = true;

            writer.Write(Value);
            writer.Write(InfoBytes);
            writer.Write(InfoKey);
            writer.Write(DummyByte1);
            writer.Write(SignatureBytes);
            writer.Write(SignatureKey);
            writer.Write(DummyByte2);
        }

        public override int GetHeaderSize() => 4 + 16 + 16 + 1 + 16 + 16 + 1;

        protected override void VerifyHexKey(string hexString)
        {
            if (hexString.Length != 16 && hexString.Length != 32)
                throw new ArgumentException("CN key must be 16 or 32 hex chars.");
        }

        protected override ICryptoTransform CreateCryptoEngine(byte[] key)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Key = key;
            return aes.CreateEncryptor();
        }

        protected override void InitCryptoEngine()
        {
            if (!_initBytes)
            {
                XorWithAes(InfoKey, InfoBytes);
                XorWithAes(SignatureKey, SignatureBytes);
                _initBytes = true;
            }

            InitializeTables();

            _isIndexSpecial = true;
            for (int i = 0; i < Index.Length; i++)
            {
                if (Index[i] != i)
                {
                    _isIndexSpecial = false;
                    break;
                }
            }
        }

        public override bool SupportsCompression(CompressionType compressionType)
        {
            return compressionType switch
            {
                CompressionType.LZ4 or CompressionType.LZ4HC => true,
                _ => false
            };
        }

        public override CompressionType MainCompressionType() => CompressionType.LZ4;

        public override int MaxPlainBlockSize() => MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE;

        protected override int CalculateEncryptionSize(int plainSize) => plainSize;

        protected override int Encrypt(Stream plainData, int plainSize, Stream cipherStream, int blockIdx)
            => throw new NotSupportedException("Encryption without compression is not supported by UnityCN.");
        protected override int Encrypt(ReadOnlySpan<byte> plainData, Span<byte> cipherSpan, int blockIdx)
            => throw new NotSupportedException("Encryption without compression is not supported by UnityCN.");

        protected override (int cipherSize, CompressionType revisedCompression) CompressAndEncrypt(ReadOnlySpan<byte> plainSpan, Stream compressedCipherStream, int blockIdx, CompressionType compressionType)
        {
            var maxCompressedSize = CodecUtilities.LZ4MaxCompressedSize(plainSpan.Length);
            var buffer = ArrayPool<byte>.Shared.Rent(maxCompressedSize);
            try
            {
                var compressSpan = buffer.AsSpan(0, maxCompressedSize);
                var compressedSize = CodecUtilities.CompressLZ4(plainSpan, compressSpan, compressionType);
                compressSpan = compressSpan[..compressedSize];
                EncryptBlock(compressSpan, blockIdx);
                compressedCipherStream.Write(compressSpan);
                return (compressedSize, compressionType);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        protected override void Decrypt(Stream cipherData, int cipherdSize, int plainSize, Stream plainStream, int blockIdx)
            => throw new NotSupportedException("Decryption without decompression is not supported by UnityCN.");
        protected override void Decrypt(ReadOnlySpan<byte> cipherSpan, Span<byte> plainSpan, int blockIdx)
            => throw new NotSupportedException("Decryption without decompression is not supported by UnityCN.");

        protected override void DecryptAndDecompress(ReadOnlySpan<byte> compressedCipherSpan, CompressionType compressionType, Span<byte> plainSpan, int blockIdx)
        {
            int s = 0, d = 0;

            while (s < compressedCipherSpan.Length)
            {
                int inner = blockIdx;
                byte token = DecryptByte(compressedCipherSpan, ref s, ref inner);

                int literal = token >> 4;
                int match = token & 0xF;

                if (literal == 0xF)
                    literal += ReadLength(compressedCipherSpan, ref s, ref inner);

                compressedCipherSpan.Slice(s, literal).CopyTo(plainSpan.Slice(d));
                s += literal;
                d += literal;

                if (s == compressedCipherSpan.Length && match == 0)
                    break;

                int offset =
                    DecryptByte(compressedCipherSpan, ref s, ref inner) |
                    (DecryptByte(compressedCipherSpan, ref s, ref inner) << 8);

                if (match == 0xF)
                    match += ReadLength(compressedCipherSpan, ref s, ref inner);

                match += 4;
                CopyMatch(plainSpan, ref d, offset, match);
                blockIdx++;
            }
        }

        private void InitializeTables()
        {
            byte[] info = (byte[])InfoBytes.Clone();
            byte[] infoKey = (byte[])InfoKey.Clone();
            byte[] sig = (byte[])SignatureBytes.Clone();
            byte[] sigKey = (byte[])SignatureKey.Clone();

            XorWithAes(sigKey, sig);
            var sigStr = Encoding.UTF8.GetString(sig);
            if (sigStr != Signature)
                throw new Exception($"Invalid Signature, Expected {Signature} but found {sigStr} instead.");

            XorWithAes(infoKey, info);

            Span<byte> buf = stackalloc byte[32]; // info.Length * 2
            for (int i = 0; i < 16; i++)
            {
                var idx = i * 2;
                buf[idx] = (byte)(info[i] >> 4);
                buf[idx + 1] = (byte)(info[i] & 0xF);
            }

            buf[..16].CopyTo(Index); // [..info.Length]

            for (int i = 0; i < 16; i++) // < info.Length
            {
                var idx = (i % 4 * 4) + (i / 4);
                Sub[idx] = buf[16 + i];
            }
        }

        private void XorWithAes(byte[] key, byte[] data)
        {
            key = CryptoEngine.TransformFinalBlock(key, 0, 16);
            for (int i = 0; i < 16; i++)
                data[i] ^= key[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte DecryptByte(ReadOnlySpan<byte> src, ref int pos, ref int index)
        {
            byte b = src[pos++];

            int mb =
                Sub[index & 3] +
                Sub[((index >> 2) & 3) + 4] +
                Sub[((index >> 4) & 3) + 8] +
                Sub[((index >> 6) & 3) + 12];

            index++;

            int low = b & 0xF;
            int high = b >> 4;
            if (_isIndexSpecial)
            {
                low = Index[low];
                high = Index[high];
            }

            low = (low - mb) & 0xF;
            high = (high - mb) & 0xF;
            return (byte)(low | (high << 4));
        }

        private static int ReadLength(ReadOnlySpan<byte> src, ref int pos, ref int index)
        {
            int len = 0;
            byte b;
            do
            {
                b = src[pos++];
                len += b;
            }
            while (b == 0xFF);
            return len;
        }

        private static void CopyMatch(Span<byte> dst, ref int d, int offset, int len)
        {
            int src = d - offset;
            while (len > offset)
            {
                dst.Slice(src, offset).CopyTo(dst.Slice(d, offset));
                d += offset;
                len -= offset;
            }
            dst.Slice(src, len).CopyTo(dst.Slice(d, len));
            d += len;
        }

        private void EncryptBlock(Span<byte> data, int index)
        {
            int offset = 0;
            while (offset < data.Length)
                offset += EncryptStep(data[offset..], ref index);
        }

        private int EncryptStep(Span<byte> data, ref int index)
        {
            int pos = 0;
            byte token = EncryptByte(data, ref pos, ref index);

            int literal = token >> 4;
            int match = token & 0xF;

            if (literal == 0xF)
                literal += ReadEncryptedLength(data, ref pos, ref index);

            pos += literal;

            if (pos < data.Length)
            {
                EncryptByte(data, ref pos, ref index);
                EncryptByte(data, ref pos, ref index);
                if (match == 0xF)
                    ReadEncryptedLength(data, ref pos, ref index);
            }

            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte EncryptByte(Span<byte> data, ref int pos, ref int index)
        {
            byte b = data[pos];
            int lo = b & 0xF;
            int hi = b >> 4;

            int mb =
                Sub[index & 3] +
                Sub[((index >> 2) & 3) + 4] +
                Sub[((index >> 4) & 3) + 8] +
                Sub[((index >> 6) & 3) + 12];

            index++;

            data[pos++] = (byte)(FindNibble(lo, mb) | (FindNibble(hi, mb) << 4));

            return b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindNibble(int target, int mb)
        {
            for (int i = 0; i < 16; i++)
                if (((Index[i] - mb) & 0xF) == target)
                    return i;
            return 0;
        }

        private int ReadEncryptedLength(Span<byte> data, ref int pos, ref int index)
        {
            int len = 0;
            byte b;
            do
            {
                b = EncryptByte(data, ref pos, ref index);
                len += b;
            }
            while (b == 0xFF);
            return len;
        }
    }
}
