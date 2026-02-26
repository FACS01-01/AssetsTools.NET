
namespace AssetsTools.NET
{
    public class ClassPackageType
    {
        public int ClassId { get; set; }
        public ushort Name { get; set; }
        public ushort BaseName { get; set; }
        public ClassFileTypeFlags Flags { get; set; }
        public ushort EditorRootNode { get; set; }
        public ushort ReleaseRootNode { get; set; }

        /// <summary>
        /// Read the <see cref="ClassPackageType"/> with the provided reader and class ID.
        /// </summary>
        /// <param name="reader">The reader to use.</param>
        /// <param name="classId">The class ID to assign.</param>
        public void Read(BufferedBinaryReader reader, int classId)
        {
            ClassId = classId;

            Name = reader.ReadUInt16();
            BaseName = reader.ReadUInt16();

            Flags = (ClassFileTypeFlags)reader.ReadByte();

            EditorRootNode = ushort.MaxValue;
            if (Flags.HasFlag(ClassFileTypeFlags.HasEditorRootNode))
                EditorRootNode = reader.ReadUInt16();

            ReleaseRootNode = ushort.MaxValue;
            if (Flags.HasFlag(ClassFileTypeFlags.HasReleaseRootNode))
                ReleaseRootNode = reader.ReadUInt16();
        }

        /// <summary>
        /// Write the <see cref="ClassPackageType"/> with the provided writer.
        /// </summary>
        /// <param name="writer">The writer to use.</param>
        public void Write(AssetsFileWriter writer)
        {
            writer.Write(Name);
            writer.Write(BaseName);
            writer.Write((byte)Flags);

            if (Flags.HasFlag(ClassFileTypeFlags.HasEditorRootNode))
                writer.Write(EditorRootNode);

            if (Flags.HasFlag(ClassFileTypeFlags.HasReleaseRootNode))
                writer.Write(ReleaseRootNode);
        }
    }
}
