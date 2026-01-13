
namespace AssetsTools.NET.Standard.IO
{
    public static class MemorySizes
    {
        /// <summary>
        /// 256 B
        /// </summary>
        public const int STACKALLOC_MAX_SIZE = 256; // personal choice
        /// <summary>
        /// 10 MB
        /// </summary>
        public const int TEMP_MEMORY_OPERATION_MAX_SIZE = 0xA00000; // personal choice
        /// <summary>
        /// 4 KB
        /// </summary>
        public const int DEFAULT_FILESTREAM_BUFFER_SIZE = 4096; // from FileStream.DefaultBufferSize
        /// <summary>
        /// 80 KB
        /// </summary>
        public const int OPTIMAL_BUFFER_SIZE = 81920; // from Stream.GetCopyBufferSize()
        /// <summary>
        /// 128 KB
        /// </summary>
        public const int LZ4_BLOCK_MAX_DECOMPRESSION_SIZE = 0x20000; // from tests
        /// <summary>
        /// Almost 4 GB. Used for LZMA and None compressions.
        /// </summary>
        public const uint LZMA_BLOCK_MAX_DECOMPRESSION_SIZE = 0xFAE147AD; // from tests
    }
}
