using System;
using System.Collections.Generic;

namespace AssetsTools.NET
{
    public class AssetBundleBlockAndDirInfo
    {
        /// <summary>
        /// Hash of this entry.
        /// </summary>
        public Hash128 Hash { get; set; }
        /// <summary>
        /// List of blocks in this bundle.
        /// Do not modify this array, it's needed to read the existing file correctly.
        /// </summary>
        public AssetBundleBlockInfo[] BlockInfos { get; set; }
        /// <summary>
        /// List of file infos in this bundle.
        /// You can add new infos or make changes to existing ones and they will be
        /// updated on write.
        /// </summary>
        public List<AssetBundleDirectoryInfo> DirectoryInfos { get; set; }

        public AssetBundleBlockAndDirInfo()
        {
            Hash = new Hash128();
            BlockInfos = Array.Empty<AssetBundleBlockInfo>();
            DirectoryInfos = new(0);
        }
        public AssetBundleBlockAndDirInfo(AssetsFileReader reader, bool hasDirInfo)
        {
            reader.BigEndian = true;

            Hash = new Hash128(reader.ReadBytes(16));
            int blockCount = reader.ReadInt32();
            BlockInfos = new AssetBundleBlockInfo[blockCount];
            for (int i = 0; i < blockCount; i++)
            {
                var blockInfo = new AssetBundleBlockInfo
                {
                    DecompressedSize = reader.ReadUInt32(),
                    CompressedSize = reader.ReadUInt32(),
                    Flags = reader.ReadUInt16()
                };
                BlockInfos[i] = blockInfo;
            }

            if (!hasDirInfo)
            {
                DirectoryInfos = new(0);
                return;
            }

            int directoryCount = reader.ReadInt32();
            DirectoryInfos = new List<AssetBundleDirectoryInfo>(directoryCount);
            for (int i = 0; i < directoryCount; i++)
            {
                var dirInfo = new AssetBundleDirectoryInfo
                {
                    Offset = reader.ReadInt64(),
                    DecompressedSize = reader.ReadInt64(),
                    Flags = (ArchiveNodeFlags)reader.ReadUInt32(), //test ReadInt32()
                    Name = reader.ReadNullTerminated()
                };
                DirectoryInfos.Add(dirInfo);
            }
        }

        public void Write(AssetsFileWriter writer, bool hasDirInfo)
        {
            writer.BigEndian = true;

            if (Hash.data == null)
            {
                writer.Write(0L);
                writer.Write(0L);
            }
            else
            {
                writer.Write(Hash.data);
            }

            int blockCount = BlockInfos.Length;
            writer.Write(blockCount);
            for (int i = 0; i < blockCount; i++)
            {
                writer.Write(BlockInfos[i].DecompressedSize);
                writer.Write(BlockInfos[i].CompressedSize);
                writer.Write(BlockInfos[i].Flags);
            }

            if (!hasDirInfo)
                return;

            int directoryCount = DirectoryInfos.Count;
            writer.Write(directoryCount);
            for (int i = 0; i < directoryCount; i++)
            {
                writer.Write(DirectoryInfos[i].Offset);
                writer.Write(DirectoryInfos[i].DecompressedSize);
                writer.Write((uint)DirectoryInfos[i].Flags); // test cast to int
                writer.WriteNullTerminated(DirectoryInfos[i].Name);
            }
        }

        public uint UncompressedInfoSize(bool hasDirInfo)
        {
            uint val = (uint)(16 + 4 + BlockInfos.Length * (4 + 4 + 2));
            if (hasDirInfo)
            {
                val += (uint)(4 + DirectoryInfos.Count * (8 + 8 + 4));
                foreach (var dirInfo in DirectoryInfos)
                    val += (uint)(dirInfo.Name.Length + 1);
            }
            return val;
        }
    }
}
