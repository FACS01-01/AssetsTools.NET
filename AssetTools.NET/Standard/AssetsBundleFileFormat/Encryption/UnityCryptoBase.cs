using System;
using System.Security.Cryptography;
using System.Text;

namespace AssetsTools.NET
{
    public abstract class UnityCryptoBase
    {
        private const string Signature = "#$unity3dchina!@";

        public uint Value { get; set; }
        public byte[] InfoBytes { get; protected set; }
        public byte[] InfoKey { get; protected set; }
        public byte DummyByte1 { get; set; }
        public byte[] SignatureBytes { get; protected set; }
        public byte[] SignatureKey { get; protected set; }
        public byte DummyByte2 { get; set; }

        private IDisposable? _cryptoEngine;
        private string _hexKey = string.Empty;
        private bool _defaultInit = true;

        public UnityCryptoBase(AssetsFileReader reader, string hexKey) : this(reader)
        {
            SetKey(hexKey);
        }

        public UnityCryptoBase(AssetsFileReader reader)
        {
            Value = reader.ReadUInt32();
            InfoBytes = reader.ReadBytes(16);
            InfoKey = reader.ReadBytes(16);
            DummyByte1 = reader.ReadByte();
            SignatureBytes = reader.ReadBytes(16);
            SignatureKey = reader.ReadBytes(16);
            DummyByte2 = reader.ReadByte();

            _defaultInit = false;
        }

        public UnityCryptoBase(string hexKey) : this()
        {
            SetKey(hexKey);
        }

        public UnityCryptoBase()
        {
            SetDefaultBytes();
        }

        public UnityCryptoBase(UnityCryptoBase toCopy)
        {
            Value = toCopy.Value;
            InfoBytes = (byte[])toCopy.InfoBytes.Clone();
            InfoKey = (byte[])toCopy.InfoKey.Clone();
            DummyByte1 = toCopy.DummyByte1;
            SignatureBytes = (byte[])toCopy.SignatureBytes.Clone();
            SignatureKey = (byte[])toCopy.SignatureKey.Clone();
            DummyByte2 = toCopy.DummyByte2;

            _defaultInit = toCopy._defaultInit;

            var key = toCopy.GetKey();
            if (!string.IsNullOrEmpty(key))
                SetKey(key);
        }

        public void SetKey(string hexString)
        {
            if (_cryptoEngine != null)
            {
                _cryptoEngine.Dispose();
                _cryptoEngine = null;
            }

            VerifyHexKey(hexString); // check if valid length
            var key = Convert.FromHexString(hexString); // checks if valid hex
            _cryptoEngine = CreateCryptoEngine(key);
            _hexKey = hexString;
            if (_defaultInit)
            {
                XorDefaultKey();
                _defaultInit = false;
            }
            InitCryptoEngine();
        }

        public string GetKey() => _hexKey;

        ~UnityCryptoBase()
        {
            _cryptoEngine?.Dispose();
        }

        private void XorDefaultKey()
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Key = Convert.FromHexString(_hexKey);
            using var encryptor = aes.CreateEncryptor();

            XorWithKey(InfoKey, InfoBytes);
            XorWithKey(SignatureKey, SignatureBytes);

            void XorWithKey(byte[] key, byte[] data)
            {
                key = encryptor.TransformFinalBlock(key, 0, key.Length);
                for (int i = 0; i < 0x10; i++)
                    data[i] ^= key[i];
            }
        }

        protected virtual void SetDefaultBytes()
        {
            Value = 0;
            InfoBytes = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xA6, 0xB1, 0xDE, 0x48, 0x9E, 0x2B, 0x53, 0x5C];
            InfoKey = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];
            DummyByte1 = 0;
            SignatureBytes = Encoding.UTF8.GetBytes(Signature);
            SignatureKey = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];
            DummyByte2 = 0;
        }

        protected virtual void InitCryptoEngine() { }

        protected abstract void VerifyHexKey(string hexString);

        protected abstract IDisposable CreateCryptoEngine(byte[] key);

        public abstract byte[] CompressAndEncrypt(ReadOnlySpan<byte> input, int blockIdx);

        public abstract void DecryptAndDecompress(ReadOnlySpan<byte> input, Span<byte> output, int blockIdx);
    }
}
