using System.Collections.Generic;
using System.IO;

namespace AssetsTools.NET.Extra
{
    public static class BundleHelper
    {
        public static byte[] LoadAssetDataFromBundle(AssetBundleFile bundle, int index)
        {
            bundle.GetFileRange(index, out long offset, out long length);

            var reader = bundle.DataReader;
            reader.Position = offset;
            return reader.ReadBytes((int)length);
        }

        public static byte[] LoadAssetDataFromBundle(AssetBundleFile bundle, string name)
        {
            int index = bundle.GetFileIndex(name);
            if (index < 0)
                return null;

            return LoadAssetDataFromBundle(bundle, index);
        }

        public static AssetsFile LoadAssetFromBundle(AssetBundleFile bundle, int index)
        {
            bundle.GetFileRange(index, out long offset, out long length);
            Stream stream = new SegmentStream(bundle.DataReader.BaseStream, offset, length);
            BufferedBinaryReader reader = new(stream);
            AssetsFile file = new AssetsFile();
            file.Read(reader);
            return file;
        }

        public static AssetsFile LoadAssetFromBundle(AssetBundleFile bundle, string name)
        {
            int index = bundle.GetFileIndex(name);
            if (index < 0)
                return null;

            return LoadAssetFromBundle(bundle, index);
        }

        public static List<byte[]> LoadAllAssetsDataFromBundle(AssetBundleFile bundle)
        {
            List<byte[]> files = new List<byte[]>();
            int numFiles = bundle.BlockAndDirInfo.DirectoryInfos.Count;
            for (int i = 0; i < numFiles; i++)
            {
                if (bundle.IsAssetsFile(i))
                {
                    files.Add(LoadAssetDataFromBundle(bundle, i));
                }
            }
            return files;
        }

        public static List<AssetsFile> LoadAllAssetsFromBundle(AssetBundleFile bundle)
        {
            List<AssetsFile> files = new List<AssetsFile>();
            int numFiles = bundle.BlockAndDirInfo.DirectoryInfos.Count;
            for (int i = 0; i < numFiles; i++)
            {
                if (bundle.IsAssetsFile(i))
                {
                    files.Add(LoadAssetFromBundle(bundle, i));
                }
            }
            return files;
        }

        public static AssetBundleFile UnpackBundle(AssetBundleFile file, bool freeOriginalStream = true)
        {
            MemoryStream ms = new MemoryStream();
            file.Write(ms, Standard.Codecs.CompressionType.None, Standard.Codecs.CompressionType.None);

            ms.Position = 0;
            AssetBundleFile newFile = new AssetBundleFile(ms, false);

            if (freeOriginalStream)
            {
                file.Dispose();
            }
            return newFile;
        }

        public static AssetBundleFile UnpackBundleToStream(AssetBundleFile file, Stream stream, bool freeOriginalStream = true)
        {
            stream.Position = 0;
            file.Write(stream, Standard.Codecs.CompressionType.None, Standard.Codecs.CompressionType.None);

            stream.Position = 0;
            AssetBundleFile newFile = new AssetBundleFile(stream);

            if (freeOriginalStream)
            {
                file.Dispose();
            }
            return newFile;
        }

        public static AssetBundleDirectoryInfo GetDirInfo(AssetBundleFile bundle, int index)
        {
            List<AssetBundleDirectoryInfo> dirInf = bundle.BlockAndDirInfo.DirectoryInfos;
            return dirInf[index];
        }

        public static AssetBundleDirectoryInfo GetDirInfo(AssetBundleFile bundle, string name)
        {
            List<AssetBundleDirectoryInfo> dirInf = bundle.BlockAndDirInfo.DirectoryInfos;
            for (int i = 0; i < dirInf.Count; i++)
            {
                AssetBundleDirectoryInfo info = dirInf[i];
                if (info.Name == name)
                {
                    return info;
                }
            }
            return null;
        }
    }
}
