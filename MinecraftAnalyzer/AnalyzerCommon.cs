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
        public SubChunkCoordinate(int x, byte y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public SubChunkCoordinate(ChunkCoordinate chunk, byte y)
        {
            X = chunk.X;
            Y = y;
            Z = chunk.Z;
        }

        public int X;
        public byte Y;
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
