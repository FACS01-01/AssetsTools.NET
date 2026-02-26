using AssetsTools.NET.IO;
using AssetsTools.NET.Standard.Codecs;
using AssetsTools.NET.Standard.IO;
using AssetsTools.NET.Standard.IO.Extensions;
using System;
using System.Collections.Generic;
using System.IO;

namespace AssetsTools.NET
{
    public class AssetBundleFile : IDisposable
    {
        /// <summary>
        /// Bundle header. Contains bundle engine version.
        /// </summary>
        public AssetBundleHeader Header { get; set; }
        /// <summary>
        /// List of compression blocks and file info (file names, address in file, etc.)
        /// </summary>
        public AssetBundleBlockAndDirInfo BlockAndDirInfo { get; set; }
        /// <summary>
        /// Reader for data block of bundle
        /// </summary>
        public BufferedBinaryReader DataReader { get; set; }
        /// <summary>
        /// Is data reader reading compressed or encrypted data?
        /// </summary>
        public bool DataIsEncoded { get; set; }

        public BufferedBinaryReader Reader;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            Reader?.Dispose();
            DataReader?.Dispose();
            Reader = null;
            DataReader = null;
            if (disposing)
            {
                if (Header != null)
                {
                    Header.Signature = null;
                    Header.GenerationVersion = null;
                    Header.EngineVersion = null;
                    Header.CryptoHandler = null;
                    Header = null;
                }
                if (BlockAndDirInfo != null)
                {
                    BlockAndDirInfo.BlockInfos = null;
                    foreach (var dirInfo in BlockAndDirInfo.DirectoryInfos)
                        dirInfo.Replacer = null; //Replacer should have Dispose?
                    BlockAndDirInfo.DirectoryInfos.Clear();
                    BlockAndDirInfo.DirectoryInfos = null;
                    BlockAndDirInfo = null;
                }
            }
        }

        /// <summary>
        /// Create a new <see cref="AssetBundleFile"/> with default values.
        /// </summary>
        public AssetBundleFile()
        {
            Reader = new(Stream.Null);
            Header = new();
            BlockAndDirInfo = new();
            DataReader = new(Stream.Null);
            DataIsEncoded = false;
        }

        public AssetBundleFile(string filePath, bool unpackIfPacked = false) : this(File.OpenRead(filePath), false, unpackIfPacked) { }

        /// <summary>
        /// Create the <see cref="AssetBundleFile"/> with the provided stream.
        /// </summary>
        /// <param name="source">The stream to use. Will be read from Position 0 onwards.</param>
        /// <param name="leaveStreamOpen">Whether to leave the source stream open after closing this bundle.</param>
        /// <param name="unpackIfPacked">Whether to unpack the Data Block immediately.</param>
        public AssetBundleFile(Stream source, bool leaveStreamOpen = true, bool unpackIfPacked = false)
        {
            Reader = new(source, leaveOpen: leaveStreamOpen) { BigEndian = true };
            Reader.Position = 0;

            Header = new AssetBundleHeader(Reader);

            if (Header.FileStreamHeader.Flags.HasFlag(AssetBundleFSHeaderFlags.BlockAndDirAtEnd))
                Reader.Position = Header.FileStreamHeader.TotalFileSize - Header.FileStreamHeader.CompressedSize;

            var hasDirInfo = Header.FileStreamHeader.Flags.HasFlag(AssetBundleFSHeaderFlags.HasDirectoryInfo);
            if (Header.GetCompressionType() == CompressionType.None)
            {
                BlockAndDirInfo = new(Reader, hasDirInfo);
            }
            else
            {
                var compressedSize = Header.FileStreamHeader.CompressedSize;
                var decompressedSize = Header.FileStreamHeader.DecompressedSize;
                var blocksInfoStream = CodecUtilities.DecompressToNew(Reader.BaseStream, compressedSize, decompressedSize,
                    Header.GetCompressionType(), BackingStreamType.MemoryStream, false);

                using (var memReader = new BufferedBinaryReader(blocksInfoStream))
                {
                    memReader.Position = 0;
                    BlockAndDirInfo = new(memReader, hasDirInfo);
                }
            }

            if (Header.CryptoHandler == null && GetCompressionType() == CompressionType.None)
            {
                SegmentStream dataStream = new SegmentStream(Reader.BaseStream, Header.GetFileDataOffset(), leaveOpen: leaveStreamOpen);
                DataReader = new(dataStream);
                DataIsEncoded = false;
            }
            else
            {
                var BlockStream = new ReadOnlyBlockStream(Reader.BaseStream, Header.GetFileDataOffset(), BlockAndDirInfo.BlockInfos, Header.CryptoHandler);
                if (unpackIfPacked)
                {
                    var tempFs = StreamExtensions.NewTempFileStream();
                    BlockStream.DumpInto(tempFs);
                    BlockStream.Dispose();
                    DataReader = new(tempFs);
                    DataIsEncoded = false;
                }
                else
                {
                    DataReader = new(BlockStream);
                    DataIsEncoded = true;
                }
            }
        }

        public void Write(string newFilePath, CompressionType metadataCompression = CompressionType.LZ4HC,
            CompressionType dataCompression = CompressionType.None, UnityCryptoBase? encryptor = null)
        {
            newFilePath = Path.GetFullPath(newFilePath);
            if (File.Exists(newFilePath))
                throw new NotSupportedException($"File already exists at path: {newFilePath}");

            var newFile_Dir = Path.GetDirectoryName(newFilePath);
            if (!Directory.Exists(newFile_Dir))
                throw new DirectoryNotFoundException($"Directory for new file doesn't exist: {newFile_Dir}");

            using var newFileStream = File.Create(newFilePath);
            Write(newFileStream, metadataCompression, dataCompression, encryptor);
        }

        /// <summary>
        /// Write the <see cref="AssetBundleFile"/> to the provided stream.
        /// </summary>
        /// todo params descriptions
        public void Write(Stream destination, CompressionType metadataCompression = CompressionType.LZ4HC,
            CompressionType dataCompression = CompressionType.None, UnityCryptoBase? encryptor = null)
        {
            destination.ThrowIfCantWrite();
            if (Reader.BaseStream == destination)
                throw new ArgumentException("Can't write to the same base stream.", nameof(destination));
            if (encryptor != null)
            {
                if (!encryptor.IsUsable())
                    throw new ArgumentException("Provided encryptor is not ready for use.", nameof(encryptor));
                if (!encryptor.SupportsCompression(dataCompression))
                    dataCompression = encryptor.MainCompressionType();
            }

            var NewBundleHeader = new AssetBundleHeader()
            {
                Signature = Header.Signature,
                Version = Header.Version,
                GenerationVersion = Header.GenerationVersion,
                EngineVersion = Header.EngineVersion,
                CryptoHandler = encryptor,
            };

            NewBundleHeader.FileStreamHeader.Flags = Header.FileStreamHeader.Flags
                & ~(AssetBundleFSHeaderFlags.CompressionMask | Header.GetEncryptionMask());
            NewBundleHeader.FileStreamHeader.Flags |= (AssetBundleFSHeaderFlags)metadataCompression;
            if (encryptor != null)
                NewBundleHeader.FileStreamHeader.Flags |= Header.GetEncryptionFlag();

            var NewBundleInf = new AssetBundleBlockAndDirInfo();

            int newDirInfoCount = 0;
            foreach (AssetBundleDirectoryInfo dirInfo in BlockAndDirInfo.DirectoryInfos)
            {
                if (dirInfo.ReplacerType == ContentReplacerType.Remove)
                    continue;
                newDirInfoCount++;
            }

            NewBundleInf.DirectoryInfos = new(newDirInfoCount);
            long newDirOffset = 0;
            using var concatStream = new ReadOnlyConcatStream();
            foreach (AssetBundleDirectoryInfo dirInfo in BlockAndDirInfo.DirectoryInfos)
            {
                if (dirInfo.ReplacerType == ContentReplacerType.Remove)
                    continue;

                Stream newDirData;
                if (dirInfo.ReplacerType == ContentReplacerType.AddOrModify)
                {
                    if (dirInfo.Replacer.HasPreview())
                        newDirData = dirInfo.Replacer.GetPreviewStream();
                    else
                    {
                        var newMS = new MemoryStream();
                        dirInfo.Replacer.Write(new AssetsFileWriter(newMS, true), false);
                        newDirData = newMS;
                    }
                }
                else
                {
                    newDirData = new SegmentStream(DataReader.BaseStream, dirInfo.Offset, dirInfo.DecompressedSize, false, true);
                }
                long newDecompSize = newDirData.Length;
                concatStream.Add(newDirData);
                NewBundleInf.DirectoryInfos.Add(new AssetBundleDirectoryInfo()
                {
                    Offset = newDirOffset,
                    DecompressedSize = newDecompSize,
                    Flags = dirInfo.Flags,
                    Name = dirInfo.Name
                });

                newDirOffset += newDecompSize;
            }
            // here newDirOffset equals newDirs total decompressed size

            long MaxUncompressedBlockSize = encryptor != null ? encryptor.MaxPlainBlockSize() :
                dataCompression switch
                { CompressionType.None or CompressionType.LZMA => MemorySizes.LZMA_BLOCK_MAX_DECOMPRESSION_SIZE,
                    _ => MemorySizes.LZ4_BLOCK_MAX_DECOMPRESSION_SIZE};
            (long quo, long rem) = Math.DivRem(newDirOffset, MaxUncompressedBlockSize);
            if (rem != 0)
                quo++;
            int blockInfoCount = checked((int)quo);
            NewBundleInf.BlockInfos = new AssetBundleBlockInfo[blockInfoCount];

            using FileStream tempFS = StreamExtensions.NewTempFileStream(sequentialScan: true);
            using var writer = new AssetsFileWriter(destination, true);
            writer.Position = NewBundleHeader.HeaderByteSize();

            var writeDataBlockDirectly =
                NewBundleHeader.FileStreamHeader.Flags.HasFlag(AssetBundleFSHeaderFlags.BlockAndDirAtEnd);

            Stream forDataBlock = writeDataBlockDirectly ? destination : tempFS;
            if (writeDataBlockDirectly && NewBundleHeader.FileStreamHeader.Flags.HasFlag(AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart))
                writer.Align16();

            long compressedBlockDataSize = 0;
            for (int i = 0; i < blockInfoCount; i++)
            {
                long i_decompressedSize = newDirOffset < MaxUncompressedBlockSize ? newDirOffset : MaxUncompressedBlockSize;
                long i_compressedSize;
                ushort i_flags;
                if (encryptor != null)
                {
                    (i_compressedSize, var finalCompressionType) =
                        encryptor.CompressAndEncrypt(concatStream, i_decompressedSize, dataCompression, forDataBlock, i);
                    i_flags = (ushort)finalCompressionType;//never streamed(0x40)
                }
                else
                {
                    i_compressedSize = CodecUtilities.CompressToStream(concatStream, i_decompressedSize, dataCompression, forDataBlock);
                    i_flags = dataCompression switch
                    { CompressionType.None or CompressionType.LZMA => (ushort)(dataCompression + 0x40),
                        _ => (ushort)dataCompression};
                }

                NewBundleInf.BlockInfos[i] = new AssetBundleBlockInfo()
                {
                    DecompressedSize = (uint)i_decompressedSize,
                    CompressedSize = (uint)i_compressedSize,
                    Flags = i_flags
                };

                newDirOffset -= i_decompressedSize;
                compressedBlockDataSize += i_compressedSize;
            }

            var saveDirInfos = NewBundleHeader.FileStreamHeader.Flags.HasFlag(AssetBundleFSHeaderFlags.HasDirectoryInfo);
            uint blockInfoDecompressedSize = NewBundleInf.UncompressedInfoSize(saveDirInfos);
            uint blockInfoCompressedSize = blockInfoDecompressedSize;
            if (metadataCompression == CompressionType.None)
            {
                NewBundleInf.Write(writer, saveDirInfos);
            }
            else
            {
                long tempFileOffset = writeDataBlockDirectly ? 0 : compressedBlockDataSize;
                NewBundleInf.Write(new(tempFS,true), saveDirInfos);
                tempFS.Position = tempFileOffset;
                blockInfoCompressedSize = (uint)CodecUtilities.CompressToStream(tempFS, metadataCompression, destination);
            }

            if (!writeDataBlockDirectly)
            {
                if (NewBundleHeader.FileStreamHeader.Flags.HasFlag(AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart))
                    writer.Align16();
                StreamExtensions.CopyToExactly(tempFS, destination, compressedBlockDataSize);
            }

            NewBundleHeader.FileStreamHeader.DecompressedSize = blockInfoDecompressedSize;
            NewBundleHeader.FileStreamHeader.CompressedSize = blockInfoCompressedSize;
            NewBundleHeader.FileStreamHeader.TotalFileSize = destination.Length;
            writer.Position = 0;
            NewBundleHeader.Write(writer);
        }

        /// <summary>
        /// Unpack and write the uncompressed <see cref="AssetBundleFile"/> with the provided writer. <br/>
        /// You must write to a new file or stream when calling this method.
        /// </summary>
        /// <param name="writer">The writer to use.</param>
        public void Unpack(AssetsFileWriter writer)
        {
            /*if (Header == null)
                new Exception("Header must be loaded! (Did you forget to call bundle.Read?)");

            AssetBundleFSHeader fsHeader = Header.FileStreamHeader;
            AssetsFileReader reader = DataReader;

            AssetBundleBlockInfo[] blockInfos = BlockAndDirInfo.BlockInfos;
            List<AssetBundleDirectoryInfo> directoryInfos = BlockAndDirInfo.DirectoryInfos;

            AssetBundleHeader newBundleHeader = new AssetBundleHeader()
            {
                Signature = Header.Signature,
                Version = Header.Version,
                GenerationVersion = Header.GenerationVersion,
                EngineVersion = Header.EngineVersion,
                FileStreamHeader = new AssetBundleFSHeader
                {
                    TotalFileSize = 0,
                    CompressedSize = fsHeader.DecompressedSize,
                    DecompressedSize = fsHeader.DecompressedSize,
                    Flags = AssetBundleFSHeaderFlags.HasDirectoryInfo |
                        (fsHeader.Flags & AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart)
                }
            };

            long fileSize = newBundleHeader.GetFileDataOffset();
            for (int i = 0; i < blockInfos.Length; i++)
            {
                fileSize += blockInfos[i].DecompressedSize;
            }
            newBundleHeader.FileStreamHeader.TotalFileSize = fileSize;

            AssetBundleBlockAndDirInfo newBundleInf = new AssetBundleBlockAndDirInfo()
            {
                Hash = new Hash128(),
                BlockInfos = new AssetBundleBlockInfo[blockInfos.Length],
                DirectoryInfos = new List<AssetBundleDirectoryInfo>(directoryInfos.Count)
            };

            // todo: we should just use one block here
            for (int i = 0; i < blockInfos.Length; i++)
            {
                newBundleInf.BlockInfos[i] = new AssetBundleBlockInfo()
                {
                    CompressedSize = blockInfos[i].DecompressedSize,
                    DecompressedSize = blockInfos[i].DecompressedSize,
                    // Set compression to none
                    Flags = (ushort)(blockInfos[i].Flags & (~0x3f))
                };
            }

            for (int i = 0; i < directoryInfos.Count; i++)
            {
                newBundleInf.DirectoryInfos.Add(new AssetBundleDirectoryInfo()
                {
                    Offset = directoryInfos[i].Offset,
                    DecompressedSize = directoryInfos[i].DecompressedSize,
                    Flags = directoryInfos[i].Flags,
                    Name = directoryInfos[i].Name
                });
            }

            newBundleHeader.Write(writer);
            if (newBundleHeader.Version >= 7)
            {
                writer.Align16();
            }
            newBundleInf.Write(writer);
            if ((newBundleHeader.FileStreamHeader.Flags & AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart) != 0)
            {
                writer.Align16();
            }

            reader.Position = 0;

            if (DataIsEncoded)
            {
                for (int i = 0; i < newBundleInf.BlockInfos.Length; i++)
                {
                    AssetBundleBlockInfo info = blockInfos[i];
                    CodecUtilities.DecompressToStream(reader.BaseStream, info.CompressedSize, info.DecompressedSize, info.GetCompressionType(), writer.BaseStream);
                }
            }
            else
            {
                for (int i = 0; i < newBundleInf.BlockInfos.Length; i++)
                {
                    AssetBundleBlockInfo info = blockInfos[i];
                    StreamExtensions.CopyToExactly(reader.BaseStream, writer.BaseStream, info.DecompressedSize);
                }
            }*/
        }

        /// <summary>
        /// Pack and write the compressed <see cref="AssetBundleFile"/> with the provided writer. <br/>
        /// You must write to a new file or stream when calling this method.
        /// </summary>
        /// <param name="writer">The writer to use.</param>
        /// <param name="compType">The compression type to use. LZ4 compresses worse but faster, LZMA compresses better but slower.</param>
        /// <param name="blockDirAtEnd">Put block and directory list at end? This skips creating temporary files, but is not officially used.</param>
        /// <param name="progress">Optional callback for compression progress.</param>
        public void Pack(AssetsFileWriter writer, CompressionType compType,
            bool blockDirAtEnd = true, IAssetBundleCompressProgress progress = null)
        {
            /*if (DataIsEncoded)
                throw new Exception("Bundles must be decompressed before writing.");

            Reader.Position = 0;
            writer.Position = 0;

            AssetBundleFSHeader newFsHeader = new AssetBundleFSHeader
            {
                TotalFileSize = 0,
                CompressedSize = 0,
                DecompressedSize = 0,
                Flags = ((AssetBundleFSHeaderFlags)CompressionType.LZ4HC) | AssetBundleFSHeaderFlags.HasDirectoryInfo |
                    (blockDirAtEnd ? AssetBundleFSHeaderFlags.BlockAndDirAtEnd : AssetBundleFSHeaderFlags.None)
            };

            AssetBundleHeader newHeader = new AssetBundleHeader()
            {
                Signature = Header.Signature,
                Version = Header.Version,
                GenerationVersion = Header.GenerationVersion,
                EngineVersion = Header.EngineVersion,
                FileStreamHeader = newFsHeader
            };

            AssetBundleBlockAndDirInfo newBlockAndDirList = new AssetBundleBlockAndDirInfo()
            {
                Hash = new Hash128(),
                BlockInfos = null,
                DirectoryInfos = BlockAndDirInfo.DirectoryInfos
            };

            // write header now and overwrite it later
            long startPos = writer.Position;

            newHeader.Write(writer);
            if (newHeader.Version >= 7)
                writer.Align16();

            int headerSize = (int)(writer.Position - startPos);

            long totalCompressedSize = 0;
            List<AssetBundleBlockInfo> newBlocks = new List<AssetBundleBlockInfo>();
            List<Stream> newStreams = new List<Stream>(); // used if blockDirAtEnd == false

            Stream bundleDataStream = DataReader.BaseStream;
            bundleDataStream.Position = 0;

            int fileDataLength = (int)bundleDataStream.Length;

            switch (compType)
            {
                case CompressionType.LZMA:
                {
                    // write to one large lzma block
                    Stream writeStream;
                    if (blockDirAtEnd)
                        writeStream = writer.BaseStream;
                    else
                        writeStream = StreamExtensions.NewTempFileStream();

                    var lzmaProgress = new AssetBundleLZMAProgress(progress, bundleDataStream.Length);

                    uint writeStreamLength = (uint)CodecUtilities.CompressLZMA(bundleDataStream, writeStream, lzmaProgress);

                    AssetBundleBlockInfo blockInfo = new AssetBundleBlockInfo()
                    {
                        CompressedSize = writeStreamLength,
                        DecompressedSize = (uint)fileDataLength,
                        Flags = 0x41
                    };

                    totalCompressedSize += blockInfo.CompressedSize;
                    newBlocks.Add(blockInfo);

                    if (!blockDirAtEnd)
                        newStreams.Add(writeStream);

                    if (progress != null)
                    {
                        progress.SetProgress(1.0f);
                    }

                    break;
                }
                case CompressionType.LZ4:
                case CompressionType.LZ4HC:
                {
                    // compress into 0x20000 blocks
                    BinaryReader bundleDataReader = new BinaryReader(bundleDataStream);

                    Stream writeStream;
                    if (blockDirAtEnd)
                        writeStream = writer.BaseStream;
                    else
                        writeStream = StreamExtensions.NewTempFileStream(0x20000);

                    byte[] uncompressedBlock = bundleDataReader.ReadBytes(0x20000);
                    while (uncompressedBlock.Length != 0)
                    {
                        byte[] compressedBlock = CodecUtilities.CompressLZ4ToArray(uncompressedBlock, compType);

                        if (progress != null)
                        {
                            progress.SetProgress((float)bundleDataReader.BaseStream.Position / bundleDataReader.BaseStream.Length);
                        }

                        if (compressedBlock.Length > uncompressedBlock.Length)
                        {
                            writeStream.Write(uncompressedBlock, 0, uncompressedBlock.Length);

                            AssetBundleBlockInfo blockInfo = new AssetBundleBlockInfo()
                            {
                                CompressedSize = (uint)uncompressedBlock.Length,
                                DecompressedSize = (uint)uncompressedBlock.Length,
                                Flags = 0x00
                            };

                            totalCompressedSize += blockInfo.CompressedSize;

                            newBlocks.Add(blockInfo);
                        }
                        else
                        {
                            writeStream.Write(compressedBlock, 0, compressedBlock.Length);

                            AssetBundleBlockInfo blockInfo = new AssetBundleBlockInfo()
                            {
                                CompressedSize = (uint)compressedBlock.Length,
                                DecompressedSize = (uint)uncompressedBlock.Length,
                                Flags = 0x03
                            };

                            totalCompressedSize += blockInfo.CompressedSize;

                            newBlocks.Add(blockInfo);
                        }

                        uncompressedBlock = bundleDataReader.ReadBytes(0x20000);
                    }

                    if (!blockDirAtEnd)
                        newStreams.Add(writeStream);

                    if (progress != null)
                    {
                        progress.SetProgress(1.0f);
                    }

                    break;
                }
                case CompressionType.None:
                {
                    AssetBundleBlockInfo blockInfo = new AssetBundleBlockInfo()
                    {
                        CompressedSize = (uint)fileDataLength,
                        DecompressedSize = (uint)fileDataLength,
                        Flags = 0x00
                    };

                    totalCompressedSize += blockInfo.CompressedSize;

                    newBlocks.Add(blockInfo);

                    if (blockDirAtEnd)
                        bundleDataStream.CopyTo(writer.BaseStream);
                    else
                        newStreams.Add(bundleDataStream);

                    break;
                }
            }

            newBlockAndDirList.BlockInfos = newBlocks.ToArray();

            byte[] bundleInfoBytes;
            using (MemoryStream memStream = new MemoryStream())
            {
                AssetsFileWriter infoWriter = new AssetsFileWriter(memStream);
                infoWriter.BigEndian = writer.BigEndian;
                newBlockAndDirList.Write(infoWriter);
                bundleInfoBytes = memStream.ToArray();
            }

            // listing is usually lz4 even if the data blocks are lzma
            byte[] bundleInfoBytesCom = CodecUtilities.CompressLZ4ToArray(bundleInfoBytes, compType);

            long totalFileSize = headerSize + bundleInfoBytesCom.Length + totalCompressedSize;
            newFsHeader.TotalFileSize = totalFileSize;
            newFsHeader.DecompressedSize = (uint)bundleInfoBytes.Length;
            newFsHeader.CompressedSize = (uint)bundleInfoBytesCom.Length;

            if (!blockDirAtEnd)
            {
                writer.Write(bundleInfoBytesCom);
                foreach (Stream newStream in newStreams)
                {
                    newStream.Position = 0;
                    newStream.CopyTo(writer.BaseStream);
                    newStream.Close();
                }
            }
            else
            {
                writer.Write(bundleInfoBytesCom);
            }

            writer.Position = 0;
            newHeader.Write(writer);
            if (newHeader.Version >= 7)
                writer.Align16();*/
        }

        /// <summary>
        /// Returns the main compression type the bundle uses (the first uncompressed block type).
        /// </summary>
        /// <returns>The compression type</returns>
        public CompressionType GetCompressionType()
        {
            AssetBundleBlockInfo[] blockInfos = BlockAndDirInfo.BlockInfos;
            for (int i = 0; i < blockInfos.Length; i++)
            {
                var compressionType = blockInfos[i].GetCompressionType();
                switch (compressionType)
                {
                    case CompressionType.None:
                        continue;
                    case CompressionType.LZ4HC:
                        compressionType = CompressionType.LZ4;
                        goto default;
                    default:
                        return compressionType;
                }
            }

            return CompressionType.None;
        }

        /// <summary>
        /// Is the file at the index an <see cref="AssetsFile"/>?
        /// Note: this checks by reading the first bit of the file instead of reading the directory flag.
        /// </summary>
        /// <param name="index">Index of the file in the directory info list.</param>
        /// <returns>True if the file at the index is an <see cref="AssetsFile"/>.</returns>
        public bool IsAssetsFile(int index)
        {
            GetFileRange(index, out long offset, out long length);
            return AssetsFile.IsAssetsFile(DataReader, offset, length);
        }

        /// <summary>
        /// Returns the index of the file in the directory list with the given name.
        /// </summary>
        /// <param name="name">The name to search for.</param>
        /// <returns>The index of the file in the directory list or -1 if no file is found.</returns>
        public int GetFileIndex(string name)
        {
            for (int i = 0; i < BlockAndDirInfo.DirectoryInfos.Count; i++)
            {
                if (BlockAndDirInfo.DirectoryInfos[i].Name == name)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Returns the name of the file at the index in the directory list.
        /// </summary>
        /// <param name="index">The index to look at.</param>
        /// <returns>The name of the file in the directory list or null if the index is out of bounds.</returns>
        public string GetFileName(int index)
        {
            if (index < 0 || index >= BlockAndDirInfo.DirectoryInfos.Count)
                return null;

            return BlockAndDirInfo.DirectoryInfos[index].Name;
        }

        /// <summary>
        /// Returns the file range of a file.
        /// Use <see cref="DataReader"/> instead of <see cref="Reader"/> to read data.
        /// </summary>
        /// <param name="index">The index to look at.</param>
        /// <param name="offset">The offset in the data stream, or -1 if the index is out of bounds.</param>
        /// <param name="length">The length of the file, or 0 if the index is out of bounds.</param>
        public void GetFileRange(int index, out long offset, out long length)
        {
            if (index < 0 || index >= BlockAndDirInfo.DirectoryInfos.Count)
            {
                offset = -1;
                length = 0;
                return;
            }

            AssetBundleDirectoryInfo entry = BlockAndDirInfo.DirectoryInfos[index];
            offset = entry.Offset;
            length = entry.DecompressedSize;
        }

        /// <summary>
        /// Returns a list of file names in the bundle.
        /// </summary>
        /// <returns>The file names in the bundle.</returns>
        public List<string> GetAllFileNames()
        {
            List<AssetBundleDirectoryInfo> dirInfos = BlockAndDirInfo.DirectoryInfos;
            List<string> names = new List<string>(dirInfos.Count);
            foreach (AssetBundleDirectoryInfo dirInfo in dirInfos)
                names.Add(dirInfo.Name);

            return names;
        }
    }
}
