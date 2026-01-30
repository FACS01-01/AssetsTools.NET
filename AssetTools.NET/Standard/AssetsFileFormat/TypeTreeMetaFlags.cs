using System;

namespace AssetsTools.NET
{
    // https://github.com/Unity-Technologies/UnityDataTools/blob/8500857f5dec6e4ee1189451a4e49f1e4b3b24c8/UnityFileSystem/DllWrapper.cs#L133-L139
    [Flags]
    public enum TypeTreeMetaFlags
    {
        None = 0,
        AlignBytes = 1 << 14,
        AnyChildUsesAlignBytes = 1 << 15,
    }
}
