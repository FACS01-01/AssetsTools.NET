using AssetsTools.NET.Standard.Codecs;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AssetsTools.NET
{
    public class AssetBundleHeader
    {
        public const string HeaderSignature = "UnityFS";
        public const string DefaultGeneration = "5.x.x";
        public const string DefaultEngine = "0.0.0";

        /// <summary>
        /// Magic appearing at the beginning of all bundles. Possible options are:
        /// UnityFS, UnityWeb, UnityRaw, UnityArchive
        /// </summary>
        public string Signature { get; set; } // this should be removed. it is always HeaderSignature for this class, or an exception is thrown
        /// <summary>
        /// Version of this file.
        /// </summary>
        public uint Version { get; set; }
        /// <summary>
        /// Generation version string. For Unity 5 bundles this is always "5.x.x"
        /// </summary>
        public string GenerationVersion { get; set; }
        /// <summary>
        /// Engine version. This is the specific version string being used. For example, "2019.4.2f1"
        /// </summary>
        public string EngineVersion
        {
            get => _engineVersion;
            set
            {
                if (!TryParseVersion(value))
                    throw new ArgumentException("Invalid engine version format.");
                _engineVersion = value;
                _useOldEncryptionMask = UseOldEncryptionMask();
            }
        }
        private string _engineVersion;

        private int EngineMajor;
        private int EngineMinor;
        private int EnginePatch;
        private bool _useOldEncryptionMask = true;

        /// <summary>
        /// Header for bundles with a UnityFS Signature.
        /// </summary>
        public AssetBundleFSHeader FileStreamHeader { get; set; }

        public UnityCryptoBase? CryptoHandler { get; set; }

        public static bool CanRead(BufferedBinaryReader reader)
        {
            var pos = reader.Position;
            var signature = reader.ReadNullTerminated();
            reader.Position = pos;

            return signature == HeaderSignature;
        }

        public AssetBundleHeader()
        {
            Signature = HeaderSignature;
            Version = 7;
            GenerationVersion = DefaultGeneration;
            _engineVersion = DefaultEngine;
            EngineMajor = EngineMinor = EnginePatch = 0;
            FileStreamHeader = new();
            CryptoHandler = null;
        }

        public AssetBundleHeader(BufferedBinaryReader reader)
        {
            reader.BigEndian = true;

            Signature = reader.ReadNullTerminated();
            Version = reader.ReadUInt32();
            GenerationVersion = reader.ReadNullTerminated();
            EngineVersion = reader.ReadNullTerminated();

            FileStreamHeader = new AssetBundleFSHeader(reader);

            if (IsEncrypted())
                CryptoHandler = UnityCryptoBase.DefaultCreate(reader);

            if (ShouldAlignAfterHeader())
                reader.Align16();
        }

        public void Write(AssetsFileWriter writer)
        {
            writer.BigEndian = true;

            writer.WriteNullTerminated(Signature);
            writer.Write(Version);
            writer.WriteNullTerminated(GenerationVersion);
            writer.WriteNullTerminated(EngineVersion);

            if (IsEncrypted() && CryptoHandler == null)
                FileStreamHeader.Flags &= ~GetEncryptionMask();
            FileStreamHeader.Write(writer);

            if (CryptoHandler != null && IsEncrypted())
                CryptoHandler.WriteHeader(writer);

            if (ShouldAlignAfterHeader())
                writer.Align16();
        }
        
        public long HeaderByteSize()
        {
            long size = Signature.Length + 1
                + 4 //Version
                + GenerationVersion.Length + 1
                + EngineVersion.Length + 1
                + (8 + 4 + 4 + 4) // AssetBundleFSHeader
                + (IsEncrypted() ? CryptoHandler.GetHeaderSize() : 0); // UnityCryptoBase
            if (ShouldAlignAfterHeader())
                size = (size + 15) & ~15; // align16
            return size;
        }

        public long GetBundleInfoOffset()
        {
            if (FileStreamHeader.Flags.HasFlag(AssetBundleFSHeaderFlags.BlockAndDirAtEnd))
                return FileStreamHeader.TotalFileSize - FileStreamHeader.CompressedSize;
            else
                return HeaderByteSize();
        }

        public long GetFileDataOffset()
        {
            long ret = HeaderByteSize();
            
            if (!FileStreamHeader.Flags.HasFlag(AssetBundleFSHeaderFlags.BlockAndDirAtEnd))
                ret += FileStreamHeader.CompressedSize;
            if (FileStreamHeader.Flags.HasFlag(AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart))
                ret = (ret + 15) & ~15;

            return ret;
        }

        public CompressionType GetCompressionType() =>
            (CompressionType)(FileStreamHeader.Flags & AssetBundleFSHeaderFlags.CompressionMask);

        public AssetBundleFSHeaderFlags GetEncryptionFlag()
        {
            return _useOldEncryptionMask ?
                AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart :
                AssetBundleFSHeaderFlags.UnityCNEncryption; // todo. when to use UnityCNEncryptionNew
        }

        public AssetBundleFSHeaderFlags GetEncryptionMask()
        {
            return _useOldEncryptionMask ?
                AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart :
                AssetBundleFSHeaderFlags.UnityCNEncryption | AssetBundleFSHeaderFlags.UnityCNEncryptionNew;
        }

        public bool IsEncrypted() => (FileStreamHeader.Flags & GetEncryptionMask()) != AssetBundleFSHeaderFlags.None;

        private bool ShouldAlignAfterHeader()
        {
            if (Version >= 7)
                return true;
            if (EngineMajor == 2019 && EngineMinor == 4 && EnginePatch >= 30)
                return true;

            return false;
        }

        private bool TryParseVersion(string newEngineVersion)
        {
            if (string.IsNullOrEmpty(newEngineVersion))
            {
                EngineMajor = EngineMinor = EnginePatch = -1;
                return true;
            }

            ReadOnlySpan<char> s = newEngineVersion;
            var verLen = s.Length;
            int i = 0;

            int start = i;
            while (i < verLen && char.IsDigit(s[i]))
                i++;
            if (i == start || i >= verLen || s[i] != '.' ||
                !int.TryParse(s[start..i], out EngineMajor))
                goto parseFail;
            i++;

            start = i;
            while (i < verLen && char.IsDigit(s[i]))
                i++;
            if (i == start || i >= s.Length || s[i] != '.' ||
                !int.TryParse(s[start..i], out EngineMinor))
                goto parseFail;
            i++;

            start = i;
            while (i < verLen && char.IsDigit(s[i]))
                i++;
            if (i == start || !int.TryParse(s[start..i], out EnginePatch))
                goto parseFail;

            return true;

        parseFail:
            EngineMajor = EngineMinor = EnginePatch = -1;
            return false;
        }

        private bool UseOldEncryptionMask()
        {
            if (EngineMajor < 2020)
                return true; //2020 and earlier

            if (EngineMajor > 2022 || EngineMinor != 3)
                return false;

            if (EngineMajor == 2020)
            {
                if (EnginePatch <= 34)
                    return true; //2020.3.34 and earlier
            }
            else if (EngineMajor == 2021)
            {
                if (EnginePatch <= 2)
                    return true; //2021.3.2 and earlier
            }
            else if (EngineMajor == 2022)
            {
                if (EnginePatch <= 1)
                    return true; //2022.3.1 and earlier
            }

            return false;

            /*
            return version < "2020" || //2020 and earlier
                   (version >= "2020.3" && version <= "2020.3.34") || //2020.3.34 and earlier
                   (version >= "2021.3" && version <= "2021.3.2") || //2021.3.2 and earlier
                   (version >= "2022.3" && version <= "2022.3.1"); //2022.3.1 and earlier
            */
        }

        public void SetEngineVersion(int major, int minor, int patch, string? extra = null)
        {
            if (major < 0 || minor < 0 || patch < 0 ||
                (!string.IsNullOrEmpty(extra) && char.IsDigit(extra[0])))
                throw new ArgumentException("Invalid engine version format.");
            if (string.IsNullOrEmpty(extra))
                extra = string.Empty;
            EngineMajor = major;
            EngineMinor = minor;
            EnginePatch = patch;
            _engineVersion = major + '.' + minor + '.' + patch + extra;
            _useOldEncryptionMask = UseOldEncryptionMask();
        }
    }
}
