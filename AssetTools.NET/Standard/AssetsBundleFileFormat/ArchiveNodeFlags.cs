using System;

namespace AssetsTools.NET
{
    // https://github.com/Unity-Technologies/UnityDataTools/blob/8500857f5dec6e4ee1189451a4e49f1e4b3b24c8/UnityFileSystem/DllWrapper.cs#L83-L90
    [Flags]
    public enum ArchiveNodeFlags
    {
        None = 0,
        Directory = 1 << 0,
        Deleted = 1 << 1,
        SerializedFile = 1 << 2,
    }
}
