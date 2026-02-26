using AssetsTools.NET.Standard.Codecs;
using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.Buffers;
using System.IO;

namespace AssetsTools.NET
{
    /// <summary>
    /// Base class for Unity Encryption.
    /// </summary>
    public abstract class UnityCryptoBase : IDisposable
    {
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected abstract void Dispose(bool disposing);

        /// <summary>
        /// Resets crypto header to its default values.
        /// </summary>
        public abstract void SetDefaultHeader();

        /// <summary>
        /// Read crypto header from a <see cref="BufferedBinaryReader"/>.
        /// </summary>
        public abstract void ReadHeaderFrom(BufferedBinaryReader reader);

        /// <summary>
        /// Copy crypto header from an existing <see cref="UnityCryptoBase"/> instance.
        /// </summary>
        public abstract void CopyHeaderFrom(UnityCryptoBase toCopy);

        /// <summary>
        /// Write crypto header into a <see cref="AssetsFileWriter"/>.
        /// </summary>
        public abstract void WriteHeader(AssetsFileWriter writer);

        /// <summary>
        /// Get the amount of bytes used by this crypto header.
        /// </summary>
        public abstract int GetHeaderSize();

        /// <summary>
        /// Checks if the crypto engine is operational.
        /// </summary>
        public abstract bool IsUsable();

        /// <summary>
        /// Gets the hexadecimal representation of the key.
        /// </summary>
        public abstract string GetKey();

        /// <summary>
        /// Sets the cryptographic key using the specified hexadecimal string.
        /// </summary>
        public abstract void SetKey(string hexString);

        /// <summary>
        /// Attempts to set the cryptographic key using the provided hexadecimal string.
        /// </summary>
        /// <returns><see langword="true"/> if the key was set successfully; otherwise, <see langword="false"/>.</returns>
        public bool TrySetKey(string hexString)
        {
            try
            {
                SetKey(hexString);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Determines whether encryption can be applied to plaintext with the provided compression.
        /// </summary>
        public abstract bool SupportsCompression(CompressionType compressionType);

        /// <summary>
        /// Returns the preferred compression type for this crypto engine (should be constant).
        /// </summary>
        public abstract CompressionType MainCompressionType();

        /// <summary>
        /// Maximum plaintext's block size that can be encrypted at once.
        /// </summary>
        public abstract int MaxPlainBlockSize();

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
        public virtual (int cipherSize, CompressionType revisedCompression) CompressAndEncrypt(Stream plainData, long plainSize, CompressionType compressionType,
            Stream compressedCipherStream, int blockIdx)
        {
            if (!IsUsable())
                throw new InvalidOperationException("Crypto engine is not initialized. Set a valid key before using.");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(plainSize, int.MaxValue, nameof(plainSize));
            if (!SupportsCompression(compressionType))
                throw new NotSupportedException($"Unsupported compression type for {GetType()}.");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(plainSize, MaxPlainBlockSize(), nameof(plainSize));

            int _plainSize = (int)plainSize;

            switch (compressionType)
            {
                case CompressionType.None:
                    return (Encrypt(plainData, _plainSize, compressedCipherStream, blockIdx), CompressionType.None);
                default:
                    return CompressAndEncrypt(plainData, _plainSize, compressedCipherStream, blockIdx, compressionType);
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

        protected virtual (int cipherSize, CompressionType revisedCompression) CompressAndEncrypt(Stream plainData, int plainSize, Stream compressedCipherStream, int blockIdx, CompressionType compressionType)
        {
            if (plainData.TryReadBuffer(plainSize, out ReadOnlySpan<byte> plainSpan))
            {
                return CompressAndEncrypt(plainSpan, compressedCipherStream, blockIdx, compressionType);
            }

            var buffer = ArrayPool<byte>.Shared.Rent(plainSize);
            try
            {
                var plainSpan2 = buffer.AsSpan(0, plainSize);
                plainData.ReadExactly(plainSpan2);
                return CompressAndEncrypt(plainSpan2, compressedCipherStream, blockIdx, compressionType);
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
        /// <param name="compressionType">The type of compression to apply to the data.</param>
        protected abstract (int cipherSize, CompressionType revisedCompression) CompressAndEncrypt(ReadOnlySpan<byte> plainSpan, Stream compressedCipherStream, int blockIdx, CompressionType compressionType);

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
            if (!IsUsable())
                throw new InvalidOperationException("Crypto engine is not initialized. Set a valid key before using.");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(compressedCipherSize, int.MaxValue, nameof(compressedCipherSize));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(plainSize, int.MaxValue, nameof(plainSize));
            if (!SupportsCompression(compressionType))
                throw new NotSupportedException($"Unsupported compression type for {GetType()}.");

            int _compressedCipherSize = (int)compressedCipherSize;
            int _plainSize = (int)plainSize;

            if (compressionType == CompressionType.None)
                Decrypt(compressedCipherData, _compressedCipherSize, _plainSize, plainStream, blockIdx);
            else
                DecryptAndDecompress(compressedCipherData, _compressedCipherSize, _plainSize, compressionType, plainStream, blockIdx);
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

        protected virtual void DecryptAndDecompress(Stream compressedCipherData, int compressedCipherSize, int plainSize, CompressionType compressionType, Stream plainStream, int blockIdx)
        {
            if (plainStream.TryGetRemainingBuffer(out var seg) && seg.Count >= plainSize)
            {
                DecryptAndDecompress(compressedCipherData, compressedCipherSize, compressionType, seg[..plainSize], blockIdx);
                plainStream.Position += plainSize;
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(plainSize);
            try
            {
                Span<byte> decompressSpan = buffer.AsSpan(0, plainSize);
                DecryptAndDecompress(compressedCipherData, compressedCipherSize, compressionType, decompressSpan, blockIdx);
                plainStream.Write(decompressSpan);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        protected virtual void DecryptAndDecompress(Stream compressedCipherData, int compressedCipherSize, CompressionType compressionType, Span<byte> plainSpan, int blockIdx)
        {
            if (compressedCipherData.TryReadBuffer(compressedCipherSize, out ReadOnlySpan<byte> compressedCipherSpan))
            {
                DecryptAndDecompress(compressedCipherSpan, compressionType, plainSpan, blockIdx);
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(compressedCipherSize);
            try
            {
                var compressedCipherSpan2 = buffer.AsSpan(0, compressedCipherSize);
                compressedCipherData.ReadExactly(compressedCipherSpan2);
                DecryptAndDecompress(compressedCipherSpan2, compressionType, plainSpan, blockIdx);
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
        /// <param name="compressionType">The type of compression applied to the data.</param>
        /// <param name="plainSpan">The output span to receive the decrypted and decompressed data.</param>
        /// <param name="blockIdx">The zero-based index of the input data block.</param>
        protected abstract void DecryptAndDecompress(ReadOnlySpan<byte> compressedCipherSpan, CompressionType compressionType, Span<byte> plainSpan, int blockIdx);

        /// <summary>
        /// Creates a new instance of a <see cref="UnityCryptoBase"/>-derived object,
        /// optionally initializing it from a reader and/or setting a custom key.
        /// </summary>
        /// <param name="reader">An optional <see cref="BufferedBinaryReader"/> to initialize the crypto engine's header.
        /// If null, <see cref="SetDefaultHeader"/> is used.</param>
        /// <param name="key">An optional key to set for the crypto engine.</param>
        public static T Create<T>(BufferedBinaryReader? reader = null, string? key = null) where T : UnityCryptoBase, new()
        {
            T newCrypto = new();

            if (reader != null)
                newCrypto.ReadHeaderFrom(reader);
            else
                newCrypto.SetDefaultHeader();

            if (key != null)
                newCrypto.SetKey(key);

            return newCrypto;
        }

        /// <summary>
        /// Creates a new instance of a <see cref="UnityCryptoBase"/>-derived object,
        /// copying the header from an existing instance.
        /// </summary>
        /// <param name="toCopy">The crypto object from which to copy header data.</param>
        public static T Create<T>(UnityCryptoBase toCopy) where T : UnityCryptoBase, new()
        {
            ArgumentNullException.ThrowIfNull(toCopy, nameof(toCopy));

            T newCrypto = new();

            newCrypto.CopyHeaderFrom(toCopy);

            return newCrypto;
        }

        private static Func<BufferedBinaryReader?, string?, UnityCryptoBase> DelegableCreate =
            static (reader, key) => Create<UnityAesGcm>(reader, key);

        /// <summary>
        /// The <see cref="UnityCryptoBase"/>-derived type being used for <see cref="DefaultCreate"/>.
        /// </summary>
        public static Type CurrentDefaultCreateType { get; private set; } = typeof(UnityAesGcm);

        /// <summary>
        /// Set the <see cref="UnityCryptoBase"/>-derived type to be used for <see cref="DefaultCreate"/>.
        /// </summary>
        public static void SetDefaultCreate<T>() where T : UnityCryptoBase, new()
        {
            DelegableCreate = static (reader, key) => Create<T>(reader, key);
            CurrentDefaultCreateType = typeof(T);
        }

        /// <summary>
        /// Check if the provided type is equal to <see cref="CurrentDefaultCreateType"/>.
        /// </summary>
        public static bool IsDefaultCreateType<T>() where T : UnityCryptoBase
            => IsDefaultCreateType(typeof(T));

        /// <inheritdoc cref="IsDefaultCreateType{T}"/>
        public static bool IsDefaultCreateType(Type T)
            => T == CurrentDefaultCreateType;

        /// <summary>
        /// Creates a new instance of <see cref="CurrentDefaultCreateType"/>,
        /// optionally initializing it from a reader and/or setting a custom key.
        /// </summary>
        /// <param name="reader">An optional <see cref="BufferedBinaryReader"/> to initialize the crypto engine's header.
        /// If null, <see cref="SetDefaultHeader"/> is used.</param>
        /// <param name="key">An optional key to set for the crypto engine.</param>
        public static UnityCryptoBase DefaultCreate(BufferedBinaryReader? reader = null, string? key = null)
            => DelegableCreate(reader, key);
    }
}