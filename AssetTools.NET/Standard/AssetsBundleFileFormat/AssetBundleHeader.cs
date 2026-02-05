using AssetsTools.NET.Standard.Codecs;
using System;
using System.Text.RegularExpressions;

namespace AssetsTools.NET
{
    public class AssetBundleHeader
    {
        public const string HeaderSignature = "UnityFS";
        public const string DefaultGeneration = "5.x.x";
        public const string DefaultEngine = "0.0.0";
        private static readonly Regex EngineVersionRegex =
            new(@"^(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?<extra>.*)", RegexOptions.Compiled);

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
        public string EngineVersion { get; set; }

        /// <summary>
        /// Header for bundles with a UnityFS Signature.
        /// </summary>
        public AssetBundleFSHeader FileStreamHeader { get; set; }

        public UnityCryptoBase? CryptoHandler { get; set; }

        public static bool CanRead(AssetsFileReader reader)
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
            EngineVersion = DefaultEngine;
            FileStreamHeader = new();
            CryptoHandler = null;
        }
        public AssetBundleHeader(AssetsFileReader reader)
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

        private (uint Major, uint Minor, uint Patch) GetEngineVersionNumbers()
        {
            Match match = EngineVersionRegex.Match(EngineVersion);
            if (!match.Success)
                throw new FormatException($"Engine version string '{EngineVersion}' is not in the correct format.");
            uint major = uint.Parse(match.Groups["major"].Value);
            uint minor = match.Groups["minor"].Success ? uint.Parse(match.Groups["minor"].Value) : 0;
            uint patch = match.Groups["patch"].Success ? uint.Parse(match.Groups["patch"].Value) : 0;
            return (major, minor, patch);
        }

        private bool UseOldEncryptionMask()
        {
            (var maj, var min, var pat) = GetEngineVersionNumbers();
            if (maj < 2020)
                return true; //2020 and earlier

            if (maj > 2022 || min != 3)
                return false;

            if (maj == 2020)
            {
                if (pat <= 34)
                    return true; //2020.3.34 and earlier
            }
            else if (maj == 2021)
            {
                if (pat <= 2)
                    return true; //2021.3.2 and earlier
            }
            else if (maj == 2022)
            {
                if (pat <= 1)
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

        public AssetBundleFSHeaderFlags GetEncryptionFlag()
        {
            return UseOldEncryptionMask() ?
                AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart :
                AssetBundleFSHeaderFlags.UnityCNEncryption; // todo. when to use UnityCNEncryptionNew
        }

        public AssetBundleFSHeaderFlags GetEncryptionMask()
        {
            return UseOldEncryptionMask() ?
                AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart :
                AssetBundleFSHeaderFlags.UnityCNEncryption | AssetBundleFSHeaderFlags.UnityCNEncryptionNew;
        }

        public bool IsEncrypted() => (FileStreamHeader.Flags & GetEncryptionMask()) != AssetBundleFSHeaderFlags.None;

        private bool ShouldAlignAfterHeader()
        {
            if (Version >= 7)
                return true;
            var verNums = GetEngineVersionNumbers();
            if (verNums.Major == 2019 && verNums.Minor == 4 && verNums.Patch >= 30)
                return true;

            return false;
        }
    }
}
