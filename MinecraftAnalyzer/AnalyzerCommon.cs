using fNbt;
using System.Windows.Media.Media3D;

namespace MinecraftAnalyzer
{
    class AnalyzerCommon
    {
    }

    public struct ChunkCoordinate
    {
        public ChunkCoordinate(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X;
        public int Z;
    }

    public struct SubChunkCoordinate
    {
        public SubChunkCoordinate(int x, sbyte y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public SubChunkCoordinate(ChunkCoordinate chunk, byte y)
        {
            X = chunk.X;
            Y = (sbyte)y;
            Z = chunk.Z;
        }

        /// <summary>
        /// Allows for negitive y values introduced in 1.18 (which wraps around due to unsigned byte)
        /// </summary>
        public SubChunkCoordinate(ChunkCoordinate chunk, byte y, int minY)
        {
            sbyte adjustedY = (sbyte)y;

            //if y is supposed to be negitive, get the difference from 255 (+1 because 0-based) to convert to sbyte
            if (minY < 0 && y >= byte.MaxValue + minY) { adjustedY = (sbyte)(((byte.MaxValue - y) + 1) * -1); }

            X = chunk.X;
            Y = adjustedY;
            Z = chunk.Z;
        }

        public int X;
        /// <summary>
        /// <see langword="sbyte"/> to allow for negitive Y in 1.18 (all values should still be in range, because max Y in 1.18 is 19 and prior it was 15)
        /// </summary>
        public sbyte Y;
        public int Z;
    }

    public class BlockInfo
    {
        public BlockInfo() { }

        public BlockInfo(NbtTag state, Point3D coords)
        {
            State = state;
            Coordinates = coords;
            nameTag = (State["name"] ?? State["Name"]).Name;
        }

        private string nameTag;
        public string Name { get { return State[nameTag].StringValue; } }

        public NbtTag State { get; }
        public Point3D Coordinates { get; }
    }
}
