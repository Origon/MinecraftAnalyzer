using fNbt;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace MinecraftAnalyzer
{
    public static class JavaAnalyzer
    {
        public static void ParseRegionIfExists(DirectoryInfo levelFolder, ChunkCoordinate regionCoordinates, Action<BlockInfo> blockAction, bool multiThread)
        {
            //Get the path to the "region" subfolder
            string path = levelFolder.FullName;
            if(!path.EndsWith(Path.DirectorySeparatorChar)) { path += Path.DirectorySeparatorChar; }
            path += (@"region" + Path.DirectorySeparatorChar);

            //Add the name of the region file.
            path += $@"r.{regionCoordinates.X}.{regionCoordinates.Z}.mca";

            if (File.Exists(path))
            {
                using (var f = File.Open(path, FileMode.Open))
                {

                    //The first 8KiB are a header listing which chunks are present,
                    //how many "sectors" into the file that particular chunk starts
                    //and how many "sectors" of data that chunk takes up.
                    //A "sector" is 4KiB.

                    //Start going reading through the header and getting a list of the offset every present chunk.
                    var chunksToParse = new List<UInt32>();

                    do
                    {
                        //Each chunk header entry starts with a 3 byte, unsigned integer specifying the chunks offset in the region file.
                        //Since we have no 24-bit integer types, use bit shifting to merge them into a 32-bit integer with the last byte empty.
                        //Multiply this number of sectors by 4096 bytes to offset in bytes for this chunk.
                        var b1 = (UInt32)f.ReadByte();
                        var b2 = (UInt32)f.ReadByte();
                        var b3 = (UInt32)f.ReadByte();
                        var offset = (b1 << 16 | b2 << 8 | b3) * 4096;

                        //The length of the chunk data (in sectors), is stored in a single byte.
                        //Multiply by 4096 to get the length in bytes.
                        var length = f.ReadByte() * 4096;

                        //If the chunk is generated and saved yet, offset and length will be 0.
                        if (offset != 0 && length != 0) { chunksToParse.Add(offset); }

                    //Stop once we reach the end of the header at byte 4096;
                    } while (f.Position != 4096);

                    List<Task> parseTasks = multiThread ? new List<Task>() : null;

                    foreach (var chunkOffset in chunksToParse)
                    {
                        //Move ahead to the start of this chunk's data.
                        //The first four bytes give the exact size of the chunk and the fifth is the compression type.
                        //This data is all infered by the NBT library from the data itself, so we can just skip it.
                        f.Seek(chunkOffset + 5, SeekOrigin.Begin);                           

                        var chunkNBT = new NbtFile();
                        chunkNBT.LoadFromStream(f, NbtCompression.AutoDetect);

                        if (multiThread)
                        {
                            parseTasks.Add(ParseChunkAsync(chunkNBT, blockAction));
                        }
                        else
                        {
                            ParseChunk(chunkNBT, blockAction);
                        }
                    }

                    if (multiThread) { Task.WaitAll(parseTasks.ToArray()); }
                }
            }
        }

        public static Task ParseRegionIfExistsAsync(DirectoryInfo levelFolder, ChunkCoordinate regionCoordinates, Action<BlockInfo> blockAction, bool multiThread)
        {
            return Task.Run(() => ParseRegionIfExists(levelFolder, regionCoordinates, blockAction, multiThread));
        }

        public static void ParseChunk(NbtFile chunk, Action<BlockInfo> blockAction)
        {
            //We can get the chunks absolute coordinates (not relative to the region) by reading these NBT tags.
            //Note: these are chunk coordinates, not block coordinates. Chunk 1,1 starts with block 16,16.
            var chunkCoordinates = new ChunkCoordinate(chunk.RootTag["Level"]["xPos"].IntValue, chunk.RootTag["Level"]["zPos"].IntValue);

            //Each chunk is sliced into "subchunks" or "sections" of 16x16x16 blocks.
            //These sections are stacked vertically to make the full chunk (16x256x16).
            //There are 16 subchunks in a chunk, but empty subchunks might be missing from the save file.
            var subChunks = (NbtList)chunk.RootTag["Level"]["Sections"];

            foreach (var subChunk in subChunks)
            {
                //The first tag in "sections" actually isn't a subchunk.
                //We only want subchunks, which can be identified by having the tag "BlockStates".
                if (!(subChunk as NbtCompound).Names.Contains("BlockStates")) { continue; }

                //First get the coordinates of the subchunk.
                //"Y" isn't the coordinate in terms of blocks, it's actually the subchunk index (from 0 through 15; bottom to top).
                var subChunkCoordinates = new SubChunkCoordinate(chunkCoordinates, ((NbtByte)subChunk["Y"]).Value);
                int blockNumber = 0;

                //Each subchunk has its own "palette": a list of all the blocks that are actually used in that subchunk.
                //Each block in the chunk data is recorded as an index used to look up the actual block in the palette.
                var palette = (NbtList)subChunk["Palette"];

                //Each block is stored as an index to the palette, but the length of that index is variable and depends on the the number of blocks in the palette.
                //More block types in the palette = a larger integer type needed to serve as an index.
                //The size (number of bits) of the index integer that was used to record the blocks will be the minimum required to store the largest index value, but will not be lower than 4.
                //To calculate this, we take the number of block types in the palette and subtract 1 to get the largest index value.
                //Then we use Ceiling(Log2()) to calculate the smallest integer size which can store that index value.
                //We use that value unless it is less than 4, in which case we use 4.
                int bitsPerBlock = (int)Math.Max(Math.Ceiling(Math.Log2(palette.Count - 1)), 4);

                //Groups of block indices are packed into 64-bit integers ("words").
                foreach (var l in ((NbtLongArray)subChunk["BlockStates"]).Value)
                {
                    var word = new BitArray(BitConverter.GetBytes(l));

                    //Current index for reading bits in the current word
                    int wordIndex = 0;

                    //Loop until there could be no more full block indexes in the current word
                    //(i.e. the next block index would take us past the end of the word).
                    //This leaves off the padding for bitsPerBlock which aren't factors of 32.
                    while (!(wordIndex + bitsPerBlock > word.Length))
                    {
                        //We need to convert each variable-sized block index into a fixed-sized integer so we can use it in code.
                        //Since there are a max of 4096 block indecies, there can't be more than 4096 palette options.
                        //This means 4095 is our largest possable index value. The smallest normal integer size that can fit this value is 16-bit.
                        //So for our buffer size we use 16 bits, which we will convert to a 16-bit, unsigned integer later.
                        var blockBuffer = new BitArray(16);

                        //Read however many bits constitutes a block index into the buffer.
                        //The rest of the buffer will stay 0, which is fine since this data is little-endian.
                        for (int bufferIndex = 0; bufferIndex < bitsPerBlock; bufferIndex++)
                        {
                            blockBuffer.Set(bufferIndex, word.Get(wordIndex));
                            wordIndex++;
                        }
                        
                        //Convert the buffer into a 16-bit integer
                        byte[] bytes = new byte[2];
                        blockBuffer.CopyTo(bytes, 0);
                        int paletteIndex = BitConverter.ToUInt16(bytes);

                        //Blocks are stored by Y then Z then X, so use the overall index to get the 3D coordinates using math.
                        //These initial coordinates will be relative to the subchunk, not the absolute coordinates of that block in the world.
                        int x = blockNumber % 16;
                        int z = (blockNumber / 16) % 16;
                        int y = (int)Math.Truncate(blockNumber / (double)(16 * 16));

                        //The initial coordinates will be relative to the chunk.
                        //We need to add them to the chunks coordinates to get the absolute position of the block in the world.
                        x += subChunkCoordinates.X;
                        z += subChunkCoordinates.Z;
                        //The Y coordinate is actually which slice of the chunk (0-15) is being parsed, not the real Y coordniate of where the slice starts.
                        //Since each slice is 16 blocks tall, we need to multiple by 16 before adding to get the correct Y of the block.
                        y = (subChunkCoordinates.Y * 16) + y;

                        var block = new BlockInfo(palette[paletteIndex], new Point3D(x, y, z));
                        blockAction.Invoke(block);

                        blockNumber++;
                    }
                }
            }
        }

        public static Task ParseChunkAsync(NbtFile chunk, Action<BlockInfo> blockAction)
        {
            return Task.Run(() => ParseChunkAsync(chunk, blockAction));
        }
    }
}
