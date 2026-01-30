using System;
using System.Collections.Generic;
using System.Text;

namespace AssetsTools.NET
{
    public class ClassPackageTypeNode // related to: TypeTreeNode (https://github.com/Unity-Technologies/UnityDataTools/blob/8500857f5dec6e4ee1189451a4e49f1e4b3b24c8/UnityFileSystem/TypeTreeNode.cs#L9)
    {
        public ushort TypeName { get; set; }
        public ushort FieldName { get; set; }
        public int ByteSize { get; set; }
        public ushort Version { get; set; }
        public byte TypeFlags { get; set; }
        public TypeTreeMetaFlags MetaFlag { get; set; }
        public ushort[] SubNodes { get; set; }

        /// <summary>
        /// Read the <see cref="ClassPackageTypeNode"/> with the provided reader.
        /// </summary>
        /// <param name="reader">The reader to use.</param>
        public void Read(AssetsFileReader reader)
        {
            TypeName = reader.ReadUInt16();
            FieldName = reader.ReadUInt16();
            ByteSize = reader.ReadInt32();
            Version = reader.ReadUInt16();
            TypeFlags = reader.ReadByte();
            MetaFlag = (TypeTreeMetaFlags)reader.ReadUInt32(); // test ReadInt32

            ushort subNodeCount = reader.ReadUInt16();
            SubNodes = new ushort[subNodeCount];
            for (int i = 0; i < subNodeCount; i++)
            {
                SubNodes[i] = reader.ReadUInt16();
            }
        }

        /// <summary>
        /// Write the <see cref="ClassPackageTypeNode"/> with the provided writer.
        /// </summary>
        /// <param name="writer">The writer to use.</param>
        public void Write(AssetsFileWriter writer)
        {
            writer.Write(TypeName);
            writer.Write(FieldName);
            writer.Write(ByteSize);
            writer.Write(Version);
            writer.Write(TypeFlags);
            writer.Write((uint)MetaFlag); // test cast to int

            writer.Write(SubNodes.Length);
            for (int i = 0; i < SubNodes.Length; i++)
            {
                writer.Write(SubNodes[i]);
            }
        }
    }
}
