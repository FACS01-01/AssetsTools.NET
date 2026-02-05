using System;

namespace AssetsTools.NET
{
    /// <summary>
    /// Abstraction over <see cref="UnityCryptoBase"/> to better layout new crypto engines.
    /// </summary>
    /// <typeparam name="TCryptoEngine">The type for this internal crypto engine.</typeparam>
    public abstract class UnityCrypto<TCryptoEngine> : UnityCryptoBase where TCryptoEngine : IDisposable
    {
        private TCryptoEngine? _cryptoEngine = default;
        /// <summary>
        /// Engine created by <see cref="CreateCryptoEngine"/>.
        /// </summary>
        protected TCryptoEngine? CryptoEngine => _cryptoEngine;
        private string _hexKey = string.Empty;

        protected override void Dispose(bool disposing)
        {
            DisposeCryptoEngine();
            if (disposing)
            {
                _hexKey = null;
            }
        }

        private void DisposeCryptoEngine()
        {
            _cryptoEngine?.Dispose();
            _cryptoEngine = default;
        }

        public sealed override bool IsUsable() => _cryptoEngine != null;

        public sealed override string GetKey() => _hexKey;

        public sealed override void SetKey(string hexString)
        {
            var oldKey = _hexKey;
            var oldCryptoEngine = _cryptoEngine;
            try
            {
                VerifyHexKey(hexString); // check if valid length
                var key = Convert.FromHexString(hexString); // checks if valid hex
                _cryptoEngine = CreateCryptoEngine(key);
                _hexKey = hexString;
                InitCryptoEngine();
                oldCryptoEngine?.Dispose();
            }
            catch
            {
                _hexKey = oldKey;
                _cryptoEngine = oldCryptoEngine;
                throw;
            }
        }

        /// <summary>
        /// Validates that the specified string has the correct length, and if not, must <see langword="throw"/>.
        /// </summary>
        protected abstract void VerifyHexKey(string hexString);

        /// <summary>
        /// Creates a new cryptographic engine instance using the specified key.
        /// </summary>
        /// <param name="key">The byte array representing the provided hex string key.</param>
        /// <remarks>
        /// If you need to initialize any additional resources after
        /// creating the engine with a provided key, override <see cref="InitCryptoEngine"/>.
        /// </remarks>
        protected abstract TCryptoEngine CreateCryptoEngine(byte[] key);

        /// <summary>
        /// Initializes the cryptographic engine and its resources for use by the current instance.
        /// </summary>
        /// <remarks>
        /// Always called after <see cref="CreateCryptoEngine"/>.
        /// </remarks>
        protected virtual void InitCryptoEngine() { }
    }
}
