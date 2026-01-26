using AssetsTools.NET.Standard.Codecs;
using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AssetsTools.NET
{
    public abstract class UnityCryptoBase
    {
        private const string DefaultSignature = "#$unity3dchina!@";
        private static readonly byte[] DefaultInfoBytes = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xA6, 0xB1, 0xDE, 0x48, 0x9E, 0x2B, 0x53, 0x5C];
        private static readonly byte[] DefaultInfoKey = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];
        private static readonly byte[] DefaultSignatureBytes = Encoding.UTF8.GetBytes(DefaultSignature);
        private static readonly byte[] DefaultSignatureKey = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];

        public uint Value;
        public readonly byte[] InfoBytes = new byte[16];
        public readonly byte[] InfoKey = new byte[16];
        public byte DummyByte1;
        public readonly byte[] SignatureBytes = new byte[16];
        public readonly byte[] SignatureKey = new byte[16];
        public byte DummyByte2;

        private IDisposable? _cryptoEngine;
        private string _hexKey = string.Empty;
        private bool _initBytes = false;

        protected UnityCryptoBase(AssetsFileReader reader)
        {
            Value = reader.ReadUInt32();
            reader.ReadExactly(InfoBytes);
            reader.ReadExactly(InfoKey);
            DummyByte1 = reader.ReadByte();
            reader.ReadExactly(SignatureBytes);
            reader.ReadExactly(SignatureKey);
            DummyByte2 = reader.ReadByte();

            _initBytes = true;
        }

        protected UnityCryptoBase(UnityCryptoBase toCopy)
        {
            Value = toCopy.Value;
            toCopy.InfoBytes.CopyTo(InfoBytes);
            toCopy.InfoKey.CopyTo(InfoKey);
            DummyByte1 = toCopy.DummyByte1;
            toCopy.SignatureBytes.CopyTo(SignatureBytes);
            toCopy.SignatureKey.CopyTo(SignatureKey);
            DummyByte2 = toCopy.DummyByte2;

            _initBytes = toCopy._initBytes;
        }

        protected UnityCryptoBase() { }

        ~UnityCryptoBase()
        {
            _cryptoEngine?.Dispose();
        }

        /// <summary>
        /// Checks if the cryptographic engine is available for use.
        /// </summary>
        public bool IsUsable => _cryptoEngine != null;

        /// <summary>
        /// Gets the hexadecimal representation of the key.
        /// </summary>
        public string GetKey() => _hexKey;

        /// <summary>
        /// Sets the cryptographic key using the specified hexadecimal string.
        /// </summary>
        public void SetKey(string hexString)
        {
            _cryptoEngine?.Dispose();
            _cryptoEngine = null;
            var oldKey = _hexKey;
            try
            {
                SetKeyMayThrow(hexString);
            }
            catch
            {
                _cryptoEngine?.Dispose();
                _cryptoEngine = null;
                _hexKey = oldKey;
                throw;
            }
        }

        /// <summary>
        /// Attempts to set the cryptographic key using the specified hexadecimal string.
        /// </summary>
        /// <remarks>
        /// If the operation fails, the previous key is restored and any associated cryptographic resources are reset.
        /// </remarks>
        /// <returns><see langword="true"/> if the key was set successfully; otherwise, <see langword="false"/>.</returns>
        public bool TrySetKey(string hexString)
        {
            _cryptoEngine?.Dispose();
            _cryptoEngine = null;
            var oldKey = _hexKey;
            try
            {
                SetKeyMayThrow(hexString);
                return true;
            }
            catch
            {
                _cryptoEngine?.Dispose();
                _cryptoEngine = null;
                _hexKey = oldKey;
                return false;
            }
        }

        private void SetKeyMayThrow(string hexString)
        {
            VerifyHexKey(hexString); // check if valid length
            var key = Convert.FromHexString(hexString); // checks if valid hex
            _cryptoEngine = CreateCryptoEngine(key);
            _hexKey = hexString;
            if (!_initBytes)
            {
                XorDefaultKey();
                _initBytes = true;
            }
            InitCryptoEngine();
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

        protected void ApplyDefaultBytes()
        {
            SetDefaultBytes();
            _initBytes = false;
        }

        /// <summary>
        /// Resets all related byte arrays and associated fields to their default values.
        /// </summary>
        protected virtual void SetDefaultBytes()
        {
            Value = 0;
            DefaultInfoBytes.CopyTo(InfoBytes);
            DefaultInfoKey.CopyTo(InfoKey);
            DummyByte1 = 0;
            DefaultSignatureBytes.CopyTo(SignatureBytes);
            DefaultSignatureKey.CopyTo(SignatureKey);
            DummyByte2 = 0;
        }

        /// <summary>
        /// Validates that the specified string has the correct length, and if not, <see langword="throw"/>.
        /// </summary>
        protected abstract void VerifyHexKey(string hexString);

        /// <summary>
        /// Creates a new cryptographic engine instance using the specified key.
        /// </summary>
        /// <param name="key">The byte array representing the provided hex string key.</param>
        /// <remarks>
        /// If you need to initialize any additional resources after creating the engine, override <see cref="InitCryptoEngine"/>.
        /// </remarks>
        protected abstract IDisposable CreateCryptoEngine(byte[] key);

        /// <summary>
        /// Initializes the cryptographic engine and its resources for use by the current instance.
        /// </summary>
        /// <remarks>
        /// Always called after <see cref="CreateCryptoEngine"/>.
        /// </remarks>
        protected virtual void InitCryptoEngine() { }

        /// <summary>
        /// Determines whether encryption can be applied without requiring compression.
        /// </summary>
        public abstract bool SupportsEncryptionWithoutCompression();

        /// <summary>
        /// Calculates the number of bytes required to store encrypted data for a given plaintext size.
        /// </summary>
        protected abstract int CalculateEncryptionSize(int plainSize);

        /// <summary>
        /// Compresses and encrypts a block of data from the specified stream using the given compression type,
        /// writing the resulting cipher data to the provided output stream.
        /// </summary>
        /// <param name="plainData">The input stream containing plain data to be processed.</param>
        /// <param name="plainSize">The amount, in bytes, of plain data to read from the input stream.</param>
        /// <param name="compressionType">The type of compression to apply to the data.</param>
        /// <param name="compressedCipherStream">The output stream to which the compressed and encrypted data will be written.</param>
        /// <param name="blockIdx">The zero-based index of the data block being processed.</param>
        public virtual int CompressAndEncrypt(Stream plainData, long plainSize, CompressionType compressionType,
            Stream compressedCipherStream, int blockIdx)
        {
            if (!IsUsable)
                throw new InvalidOperationException("Crypto engine is not initialized. Set a valid key before using.");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(plainSize, int.MaxValue, nameof(plainSize));
            int _plainSize = (int)plainSize;

            switch (compressionType)
            {
                case CompressionType.None:
                    if (!SupportsEncryptionWithoutCompression())
                        throw new NotSupportedException($"{GetType()} doesn't support encryption without compression.");
                    return Encrypt(plainData, _plainSize, compressedCipherStream, blockIdx);
                case CompressionType.LZ4:
                case CompressionType.LZ4HC:
                    return CompressAndEncrypt(plainData, _plainSize, compressedCipherStream, blockIdx);
                default:
                    throw new NotSupportedException($"Unsupported compression type for {GetType()}.");
            }
        }

        protected virtual int Encrypt(Stream plainData, int plainSize, Stream cipherStream, int blockIdx)
        {
            if (plainData.TryReadBuffer(plainSize, out ReadOnlySpan<byte> plainSpan))
            {
                return Encrypt(plainSpan, cipherStream, blockIdx);
            }

            var buffer = ArrayPool<byte>.Shared.Rent(plainSize);
            try
            {
                var plainSpan2 = buffer.AsSpan(0, plainSize);
                plainData.ReadExactly(plainSpan2);
                return Encrypt(plainSpan2, cipherStream, blockIdx);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        protected virtual int Encrypt(ReadOnlySpan<byte> plainData, Stream cipherStream, int blockIdx)
        {
            var cipherSize = CalculateEncryptionSize(plainData.Length);
            if (cipherStream.TryGetRemainingBuffer(out var seg) && seg.Count >= cipherSize)
            {
                Encrypt(plainData, seg, blockIdx);
                cipherStream.Position += cipherSize;
                return cipherSize;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(cipherSize);
            try
            {
                Span<byte> cipherSpan = buffer.AsSpan(0, cipherSize);
                Encrypt(plainData, cipherSpan, blockIdx);
                cipherStream.Write(cipherSpan);
                return cipherSize;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Encrypts the specified input data block.
        /// </summary>
        /// <param name="plainData">The input data to be encrypted.</param>
        /// <param name="cipherSpan">The output span to receive the encrypted data.</param>
        /// <param name="blockIdx">The zero-based index of the input data block.</param>
        protected abstract int Encrypt(ReadOnlySpan<byte> plainData, Span<byte> cipherSpan, int blockIdx);

        protected virtual int CompressAndEncrypt(Stream plainData, int plainSize, Stream compressedCipherStream, int blockIdx)
        {
            if (plainData.TryReadBuffer(plainSize, out ReadOnlySpan<byte> plainSpan))
            {
                return CompressAndEncrypt(plainSpan, compressedCipherStream, blockIdx);
            }

            var buffer = ArrayPool<byte>.Shared.Rent(plainSize);
            try
            {
                var plainSpan2 = buffer.AsSpan(0, plainSize);
                plainData.ReadExactly(plainSpan2);
                return CompressAndEncrypt(plainSpan2, compressedCipherStream, blockIdx);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Compresses and encrypts the specified input data block.
        /// </summary>
        /// <param name="plainSpan">The input data to be compressed and then encrypted.</param>
        /// <param name="compressedCipherStream">The output span to receive the compressed and encrypted data.</param>
        /// <param name="blockIdx">The zero-based index of the input data block.</param>
        protected abstract int CompressAndEncrypt(ReadOnlySpan<byte> plainSpan, Stream compressedCipherStream, int blockIdx);
        
        /// <summary>
        /// Decrypts and decompresses a block of data from the specified stream using the given compression type,
        /// writing the resulting plain data to the provided output stream.
        /// </summary>
        /// <param name="compressedCipherData">The input stream containing the encrypted and compressed data to be processed.</param>
        /// <param name="compressedCipherSize">The size, in bytes, of the encrypted and compressed data to read from the input stream.</param>
        /// <param name="plainSize">The expected size, in bytes, of the resulting plain (decrypted and decompressed) data.</param>
        /// <param name="compressionType">The type of compression applied to the data.</param>
        /// <param name="plainStream">The output stream to which the decrypted and decompressed data will be written.</param>
        /// <param name="blockIdx">The zero-based index of the data block being processed.</param>
        public virtual void DecryptAndDecompress(Stream compressedCipherData, long compressedCipherSize, long plainSize,
            CompressionType compressionType, Stream plainStream, int blockIdx)
        {
            if (!IsUsable)
                throw new InvalidOperationException("Crypto engine is not initialized. Set a valid key before using.");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(compressedCipherSize, int.MaxValue, nameof(compressedCipherSize));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(plainSize, int.MaxValue, nameof(plainSize));
            int _compressedCipherSize = (int)compressedCipherSize;
            int _plainSize = (int)plainSize;

            switch (compressionType)
            {
                case CompressionType.None:
                    if (!SupportsEncryptionWithoutCompression())
                        throw new NotSupportedException($"{GetType()} doesn't support encryption without compression.");
                    Decrypt(compressedCipherData, _compressedCipherSize, _plainSize, plainStream, blockIdx);
                    break;
                case CompressionType.LZ4:
                case CompressionType.LZ4HC:
                    DecryptAndDecompress(compressedCipherData, _compressedCipherSize, _plainSize, plainStream, blockIdx);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported compression type for {GetType()}.");
            }
        }

        protected virtual void Decrypt(Stream cipherData, int cipherSize, int plainSize, Stream plainStream, int blockIdx)
        {
            if (plainStream.TryGetRemainingBuffer(out var seg) && seg.Count >= plainSize)
            {
                Decrypt(cipherData, cipherSize, seg[..plainSize], blockIdx);
                plainStream.Position += plainSize;
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(plainSize);
            try
            {
                Span<byte> decompressSpan = buffer.AsSpan(0, plainSize);
                Decrypt(cipherData, cipherSize, decompressSpan, blockIdx);
                plainStream.Write(decompressSpan);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        protected virtual void Decrypt(Stream cipherData, int cipherSize, Span<byte> plainSpan, int blockIdx)
        {
            if (cipherData.TryReadBuffer(cipherSize, out ReadOnlySpan<byte> cipherSpan))
            {
                Decrypt(cipherSpan, plainSpan, blockIdx);
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(cipherSize);
            try
            {
                var cipherSpan2 = buffer.AsSpan(0, cipherSize);
                cipherData.ReadExactly(cipherSpan2);
                Decrypt(cipherSpan2, plainSpan, blockIdx);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Decrypts the specified input data block.
        /// </summary>
        /// <param name="cipherSpan">The input data to be decrypted.</param>
        /// <param name="plainSpan">The output span to receive the decrypted data.</param>
        /// <param name="blockIdx">The zero-based index of the input data block.</param>
        protected abstract void Decrypt(ReadOnlySpan<byte> cipherSpan, Span<byte> plainSpan, int blockIdx);

        protected virtual void DecryptAndDecompress(Stream compressedCipherData, int compressedCipherSize, int plainSize, Stream plainStream, int blockIdx)
        {
            if (plainStream.TryGetRemainingBuffer(out var seg) && seg.Count >= plainSize)
            {
                DecryptAndDecompress(compressedCipherData, compressedCipherSize, seg[..plainSize], blockIdx);
                plainStream.Position += plainSize;
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(plainSize);
            try
            {
                Span<byte> decompressSpan = buffer.AsSpan(0, plainSize);
                DecryptAndDecompress(compressedCipherData, compressedCipherSize, decompressSpan, blockIdx);
                plainStream.Write(decompressSpan);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        protected virtual void DecryptAndDecompress(Stream compressedCipherData, int compressedCipherSize, Span<byte> plainSpan, int blockIdx)
        {
            if (compressedCipherData.TryReadBuffer(compressedCipherSize, out ReadOnlySpan<byte> compressedCipherSpan))
            {
                DecryptAndDecompress(compressedCipherSpan, plainSpan, blockIdx);
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(compressedCipherSize);
            try
            {
                var compressedCipherSpan2 = buffer.AsSpan(0, compressedCipherSize);
                compressedCipherData.ReadExactly(compressedCipherSpan2);
                DecryptAndDecompress(compressedCipherSpan2, plainSpan, blockIdx);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Decrypts and decompresses the specified input data block.
        /// </summary>
        /// <param name="compressedCipherSpan">The input data to be decrypted and then decompressed.</param>
        /// <param name="plainSpan">The output span to receive the decrypted and decompressed data.</param>
        /// <param name="blockIdx">The zero-based index of the input data block.</param>
        protected abstract void DecryptAndDecompress(ReadOnlySpan<byte> compressedCipherSpan, Span<byte> plainSpan, int blockIdx);

        /// <summary>
        /// Instantiate a crypto engine of the specified type, ready to work for the read bundle with known key.
        /// </summary>
        /// <param name="cryptoType"></param>
        /// <param name="reader"></param>
        /// <param name="key"></param>
        public static UnityCryptoBase Create(Type cryptoType, AssetsFileReader reader, string key)
        {
            ThrowIfNotCryptoType(cryptoType);

            var instance = (UnityCryptoBase)Activator.CreateInstance(cryptoType, reader);
            instance.SetKey(key);
            return instance;
        }

        /// <summary>
        /// Instantiate a crypto engine of the specified type, ready to work for the read bundle. Needs to set key later.
        /// </summary>
        /// <param name="cryptoType"></param>
        /// <param name="reader"></param>
        public static UnityCryptoBase Create(Type cryptoType, AssetsFileReader reader)
        {
            ThrowIfNotCryptoType(cryptoType);

            var instance = (UnityCryptoBase)Activator.CreateInstance(cryptoType, reader);
            return instance;
        }

        /// <summary>
        /// Instantiate a crypto engine of the specified type, with default byte data to work with any provided key.
        /// </summary>
        /// <param name="cryptoType"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static UnityCryptoBase Create(Type cryptoType, string key)
        {
            ThrowIfNotCryptoType(cryptoType);

            var instance = (UnityCryptoBase)Activator.CreateInstance(cryptoType);
            instance.ApplyDefaultBytes();
            instance.SetKey(key);
            return instance;
        }

        /// <summary>
        /// Instantiate a crypto engine of the specified type, with default byte data to work with any provided key.
        /// </summary>
        /// <param name="cryptoType"></param>
        /// <returns></returns>
        public static UnityCryptoBase Create(Type cryptoType)
        {
            ThrowIfNotCryptoType(cryptoType);

            var instance = (UnityCryptoBase)Activator.CreateInstance(cryptoType);
            instance.ApplyDefaultBytes();
            return instance;
        }

        /// <summary>
        /// Instantiate a crypto engine of the specified type, copying the byte data of an existing crypto engine.
        /// </summary>
        /// <param name="cryptoType"></param>
        /// <param name="toCopy"></param>
        /// <returns></returns>
        public static UnityCryptoBase Create(Type cryptoType, UnityCryptoBase toCopy)
        {
            ThrowIfNotCryptoType(cryptoType);

            var instance = (UnityCryptoBase)Activator.CreateInstance(cryptoType, toCopy);
            return instance;
        }

        private static void ThrowIfNotCryptoType(Type cryptoType)
        {
            if (!typeof(UnityCryptoBase).IsAssignableFrom(cryptoType))
                throw new ArgumentException("Invalid crypto Type.");
        }
    }
}
