using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiNET.LevelDB;
using MiNET.LevelDB.Utils;
using NUnit.Framework;
using fNbt;
using System.Diagnostics;
using System.Windows.Media.Media3D;
using System.Threading;

namespace MinecraftAnalyzer
{
    public static class BedrockAnalyzer
    {
        public static void ParseChunkIfExists(Database db, ChunkCoordinate coordinates, Action<BlockInfo> blockAction)
        {
            for (byte y = 0; y < 16; y++)
            {
                ParseSubChunkIfExists(db, new SubChunkCoordinate(coordinates, y), blockAction);
            }
        }

        public static Task ParseChunkIfExistsAsync(Database db, ChunkCoordinate coordinates, Action<BlockInfo> blockAction)
        {
            return Task.Run(() =>
            {
                var subChunkTasks = new List<Task>();

                for (byte y = 0; y < 16; y++)
                {
                    subChunkTasks.Add(ParseSubChunkIfExistsAsync(db, new SubChunkCoordinate(coordinates, y), blockAction));
                }

                Task.WaitAll(subChunkTasks.ToArray());
            });
        }

        public static void ParseSubChunkIfExists(Database db, SubChunkCoordinate coordinates, Action<BlockInfo> blockAction)
        {
            var chunkDataKey = BitConverter.GetBytes(coordinates.X).Concat(BitConverter.GetBytes(coordinates.Z)).Concat(new byte[] { 0x2f, (byte)coordinates.Y }).ToArray();
            Monitor.Enter(db);
            var chunkData = db.Get(chunkDataKey);
            Monitor.Exit(db);
            if (chunkData != null) { ParseSubChunkData(chunkData, coordinates, blockAction); }
        }

        public static Task ParseSubChunkIfExistsAsync(Database db, SubChunkCoordinate coordinates, Action<BlockInfo> blockAction)
        {
            return Task.Run(() => ParseSubChunkIfExists(db, coordinates, blockAction));
        }

        public static void ParseSubChunkData(byte[] data, SubChunkCoordinate coordinates, Action<BlockInfo> blockAction)
        {
            //Format details found at
            //https://minecraft.gamepedia.com/Bedrock_Edition_level_format

            var reader = new SpanReader(data);

            //Make sure we're using the version this code was designed for
            var version = reader.ReadByte();
            Assert.AreEqual(8, version);

            var storageSize = reader.ReadByte();
            for (int i = 0; i < storageSize; i++)
            {
                //Number of bits used to store the palette index of a single block
                var bitsPerBlock = reader.ReadByte() >> 1;
                //Number of blocks that fit into a single "word" (a 32-bit, unsigned integer)
                int blocksPerWord = (int)Math.Floor(32 / (double)bitsPerBlock);
                //Number of words we expect to find
                int numberOfWords = (int)Math.Ceiling(4096 / (double)blocksPerWord);
                //Read as many bytes as that many words would take up (each word being 4 bytes)
                var blockData = reader.Read(numberOfWords * 4);

                //The number of different types of blocks stored in the palette for this subchunk
                int paletteSize = reader.ReadInt32();

                //Read each palette item into a list
                var palette = new List<NbtFile>();
                for (int j = 0; j < paletteSize; j++)
                {
                    NbtFile file = new NbtFile();
                    file.BigEndian = false;
                    var buffer = ((ReadOnlySpan<byte>)data).Slice(reader.Position).ToArray();

                    int numberOfBytesRead = (int)file.LoadFromStream(new MemoryStream(buffer), NbtCompression.None);
                    reader.Position += numberOfBytesRead;
                    palette.Add(file);
                    Assert.NotZero(numberOfBytesRead);
                }

                //We'll need to read each block index into a byte array buffer so we can convert it to an int and use it to look up a value in the palette list.
                //Valid values for bitsPerBlock are 1, 2, 3, 4, 5, 6, 8 and 16.
                //So for 16 we use 16 bits (2 bytes) and for anything else we use 8 bits (1 byte), since we know all other valid values are one byte or less.
                int blockBufferSize = bitsPerBlock == 16 ? 16 : 8;

                var dataReader = new SpanReader(blockData);
                int blockNumber = 0;

                //Loop until we reach either the expected number of words.
                for (int wordNumber = 0; wordNumber < numberOfWords; wordNumber++)
                {
                    //Read the next word (4 bytes; a single 32 bit integer)
                    var word = new BitArray(dataReader.Read(4).ToArray());

                    //Current index for reading bits in the current word
                    int wordIndex = 0;

                    //Loop until there could be no more full block indexes in the current word
                    //(i.e. the next block index would take us past the end of the word),
                    //or until the maximum number of blocks in a subchunk (16*16*16=4096) is reached.
                    //This leaves off the padding for bitsPerBlock which aren't factors of 32.
                    while (!(wordIndex + bitsPerBlock > word.Length) && blockNumber < 4096)
                    {
                        var blockBuffer = new BitArray(blockBufferSize);

                        //Read however many bits constitutes a block id into the buffer.
                        //The rest of the buffer will stay 0, which is perfect since everything is little-endian.
                        for (int bufferIndex = 0; bufferIndex < bitsPerBlock; bufferIndex++)
                        {
                            blockBuffer.Set(bufferIndex, word.Get(wordIndex));
                            wordIndex++;
                        }

                        int paletteIndex = -1;

                        //For an 8-bit buffer, we copy to a byte and cast to int
                        if (blockBufferSize == 8)
                        {
                            byte[] bytes = new byte[1];
                            blockBuffer.CopyTo(bytes, 0);
                            paletteIndex = bytes[0];
                        }
                        //For a 16-bit buffer, we copy to two bytes, interpret it as a 16-bit int and then cast to int.
                        else if (blockBufferSize == 16)
                        {
                            byte[] bytes = new byte[2];
                            blockBuffer.CopyTo(bytes, 0);
                            paletteIndex = BitConverter.ToUInt16(bytes);
                        }

                        //Blocks are stored by Y then Z then X, so use the overall index to get the 3D coordinates using math.
                        //These initial coordinates will be relative to the subchunk, not the absolute coordinates of that block in the world.
                        int y = blockNumber % 16;
                        int z = (blockNumber / 16) % 16;
                        int x = (int)Math.Truncate(blockNumber / (double)(16 * 16));

                        //The initial coordinates will be relative to the chunk.
                        //We need to add them to the chunks coordinates to get the absolute position of the block in the world.
                        x += coordinates.X;
                        z += coordinates.Z;
                        //The Y coordinate is actually which slice of the chunk (0-15) is being parsed, not the real Y coordniate of where the slice starts.
                        //Since each slice is 16 blocks tall, we need to multiple by 16 before adding to get the correct Y of the block.
                        y = (coordinates.Y * 16) + y;

                        var block = new BlockInfo(palette[paletteIndex].RootTag, new Point3D(x, y, z));
                        blockAction.Invoke(block);

                        blockNumber++;
                    }
                }
            }
        }

        public static Task ParseSubChunkDataAsync(byte[] data, SubChunkCoordinate coordinates, Action<BlockInfo> blockAction)
        {
            return Task.Run(() => ParseSubChunkData(data, coordinates, blockAction));
        }
    }
}
