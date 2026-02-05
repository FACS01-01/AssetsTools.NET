using AssetsTools.NET.Standard.Codecs;
using System.Collections.Generic;
using System.IO;

namespace AssetsTools.NET.Extra
{
    public class BundleFileInstance
    {
        public string path;
        public string name;
        public AssetBundleFile file;
        /// <summary>
        /// Original compression type. If this bundle is decompressed, you might
        /// use this value to compress it back to its original compression type.
        /// </summary>
        public CompressionType originalCompression;
        /// <summary>
        /// List of loaded assets files for this bundle.
        /// </summary>
        /// <remarks>
        /// This list does not contain <i>every</i> assets file for the bundle,
        /// instead only the ones that have been loaded so far.
        /// </remarks>
        public List<AssetsFileInstance> loadedAssetsFiles;

        public Stream BundleStream => file.Reader.BaseStream;
        public Stream DataStream => file.DataReader.BaseStream;

        public BundleFileInstance(Stream stream, string filePath, bool unpackIfPacked = true)
        {
            path = Path.GetFullPath(filePath);
            name = Path.GetFileName(path);

            file = new AssetBundleFile(stream, true, unpackIfPacked);

            originalCompression = file.GetCompressionType();

            loadedAssetsFiles = new List<AssetsFileInstance>();
        }

        public BundleFileInstance(FileStream stream, bool unpackIfPacked = true)
            : this(stream, stream.Name, unpackIfPacked)
        {
        }

        public BundleFileInstance(string filePath, bool unpackIfPacked = true)
        {
            path = Path.GetFullPath(filePath);
            name = Path.GetFileName(path);

            file = new AssetBundleFile(path, unpackIfPacked);

            originalCompression = file.GetCompressionType();

            loadedAssetsFiles = new List<AssetsFileInstance>();
        }
    }
}
