using System;

namespace AssetsTools.NET
{
    [Flags]
    public enum AssetBundleFSHeaderFlags
    {
        None = 0x00,
        CompressionMask = HasDirectoryInfo - 1,
        HasDirectoryInfo = 0x40,
        BlockAndDirAtEnd = 0x80,
        OldWebPluginCompatibility = 0x100,
        BlockInfoNeedPaddingAtStart = 0x200,
        UnityCNEncryption = 0x400,
        UnityCNEncryptionNew = 0x1000
    }
}
