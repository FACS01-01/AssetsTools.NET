using AssetsTools.NET.Standard.Codecs;
using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace AssetsTools.NET
{
    public sealed class UnityCN : UnityCryptoBase
    {
        private const string Signature = "#$unity3dchina!@";

        private ICryptoTransform _aes;
        private readonly byte[] Index = new byte[16];
        private readonly byte[] Sub = new byte[16];
        private bool _isIndexSpecial = false;

        protected override void VerifyHexKey(string hexString)
        {
            if (hexString.Length != 16 && hexString.Length != 32)
                throw new ArgumentException("CN key must be 16 or 32 hex chars");
        }

        protected override IDisposable CreateCryptoEngine(byte[] key)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Key = key;
            _aes = aes.CreateEncryptor();
            return _aes;
        }

        protected override void InitCryptoEngine()
        {
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

        public override byte[] CompressAndEncrypt(ReadOnlySpan<byte> input, int blockIdx)
        {
            var compressedData = CodecUtilities.CompressLZ4ToArray(input, CompressionType.LZ4);
            EncryptBlock(compressedData, blockIdx);
            return compressedData;
        }

        public override void DecryptAndDecompress(ReadOnlySpan<byte> input, Span<byte> output, int blockIdx)
        {
            int s = 0, d = 0;

            while (s < input.Length)
            {
                int inner = blockIdx;
                byte token = DecryptByte(input, ref s, ref inner);

                int literal = token >> 4;
                int match = token & 0xF;

                if (literal == 0xF)
                    literal += ReadLength(input, ref s, ref inner);

                input.Slice(s, literal).CopyTo(output.Slice(d));
                s += literal;
                d += literal;

                if (s == input.Length && match == 0)
                    break;

                int offset =
                    DecryptByte(input, ref s, ref inner) |
                    (DecryptByte(input, ref s, ref inner) << 8);

                if (match == 0xF)
                    match += ReadLength(input, ref s, ref inner);

                match += 4;
                CopyMatch(output, ref d, offset, match);
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
            key = _aes.TransformFinalBlock(key, 0, 16);
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
