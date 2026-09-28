using System;
using System.Numerics;

namespace PSDL
{
    public static class Constants
    {
        public const int MaxSubtype = 7;
        public const int MaxType = 12;
    }
    
    [Flags]
    public enum RoomFlags : byte
    {
        None = 0,
        Unknown = 1,
        Subterranean = 2,
        Standard = 4,
        Road = 8,
        Intersection = 16,
        SpecialBound = 32,
        Warp = 64,
        Instance = 128,
        All = 255
    }

    public enum ElementType
    {
        Road,
        SidewalkStrip,
        Walkway,
        Sliver,
        Crosswalk,
        CulledTriangleFan,
        TriangleFan,
        FacadeBound,
        DividedRoad,
        Tunnel,
        Texture,
        Facade,
        RoofTriangleFan
    }

    public class Vertex
    {
        public float x;
        public float y;
        public float z;

        public Vertex(float _x, float _y, float _z)
        {
            x = _x;
            y = _y;
            z = _z;
        }

        public Vertex(Vector3 vector)
        {
            x = vector.X;
            y = vector.Y;
            z = vector.Z;
        }

        public static explicit operator Vector3(Vertex vert)
        {
            return new Vector3(vert.x, vert.y, vert.z);
        }

        static float Lerp(float firstFloat, float secondFloat, float by)
        {
            return firstFloat * (1 - by) + secondFloat * by;
        }

        public Vertex Lerp(Vertex other, float t)
        {
            return new Vertex(Lerp(x, other.x, t), Lerp(y, other.y, t), Lerp(z, other.z, t));
        }

        public Vertex Clone()
        {
            return new Vertex(x, y, z);
        }

        public override string ToString()
        {
            return "(x:" +  x + ", y:" + y + ", z:" + z + ")";
        }

        public override int GetHashCode()
        {
            return this.x.GetHashCode() ^ this.y.GetHashCode() << 2 ^ this.z.GetHashCode() >> 2;
        }

        public override bool Equals(object obj)
        {
            if (obj is Vertex vertex)
            {
                if (vertex.x.Equals(x) && vertex.y.Equals(y) && vertex.z.Equals(z))
                {
                    return true;
                }
            }
            return false;
        }

        public float Distance(Vertex b)
        {
            return (b - this).Magnitude();
        }

        public float FlatDistance(Vertex b)
        {
            float xDist = (b.x - this.x);
            float yDist = (b.z - this.z);
            return (float)Math.Sqrt(xDist * xDist + yDist * yDist);
        }

        public float Magnitude()
        {
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        public static Vertex operator -(Vertex v1, Vertex v2)
        {
            return new Vertex(v1.x - v2.x, v1.y - v2.y, v1.z - v2.z);
        }

        public static Vertex operator +(Vertex v1, Vertex v2)
        {
            return new Vertex(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
        }

        public static Vertex operator *(Vertex v1, Vertex v2)
        {
            return new Vertex(v1.x * v2.x, v1.y * v2.y, v1.z * v2.z);
        }

        public static Vertex operator *(Vertex vtx, float amount)
        {
            return new Vertex(vtx.x * amount, vtx.y * amount, vtx.z * amount);
        }

        public static Vertex operator /(Vertex v1, Vertex v2)
        {
            return new Vertex(v1.x / v2.x, v1.y / v2.y, v1.z / v2.z);
        }

        public static Vertex operator /(Vertex vtx, float amount)
        {
            return new Vertex(vtx.x / amount, vtx.y / amount, vtx.z / amount);
        }
    }

    public class PerimeterPoint
    {
        public Vertex Vertex;
        public Room ConnectedRoom;

        public PerimeterPoint(Vertex vtx, Room connection)
        {
            ConnectedRoom = connection;
            Vertex = vtx;
        }
    }
}
