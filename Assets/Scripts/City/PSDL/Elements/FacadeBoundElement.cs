using System;
using System.IO;

namespace PSDL.Elements
{
    public class FacadeBoundElement : SDLElementBase,  IGeometricSDLElement, ISDLElement, ICloneable
    {
        public Vertex[] Vertices = new Vertex[2];

        //IGeometricSDLElement
        public Vertex[] GetVertices() => Vertices;
        public Vertex GetVertex(int index) => Vertices[index];
        public void SetVertex(int index, Vertex vertex) => Vertices[index] = vertex;
        public int GetVertexCount() => Vertices.Length;
        public void RemoveVertexAt(int idx) => throw new NotImplementedException();
        public void AddVertex() => throw new NotImplementedException();
        public void InsertVertex(int idx, Vertex vtx) => throw new NotImplementedException();
        public void InsertVertex(int idx) => throw new NotImplementedException();

        public float LightAngle
        {
            get => (float) m_SunAngle * 5.625f;
            set => m_SunAngle = Math.Min((ushort)63, (ushort)((value % 360f) / 5.625));
        }
        public int SunAngleIndex
        {
            get => m_SunAngle;
            set => m_SunAngle = (ushort)value;
        }

        private ushort m_SunAngle;
        public float Height;

        //interface
        public ElementType Type => ElementType.FacadeBound;
        public int Subtype => 4;
        public int RequiredTextureCount => 0;

        public void Read(BinaryReader reader, int subtype, PSDLFile parent)
        {
            m_SunAngle = reader.ReadUInt16();
            Height = parent.Floats[reader.ReadUInt16()];
            Vertices[0] = parent.Vertices[reader.ReadUInt16()];
            Vertices[1] = parent.Vertices[reader.ReadUInt16()];
        }

        public void Save(BinaryWriter writer, PSDLFile parent)
        {
            writer.Write(m_SunAngle);
            writer.Write((ushort)parent.GetFloatIndex(Height));
            writer.Write((ushort)parent.GetVertexIndex(Vertices[0]));
            writer.Write((ushort)parent.GetVertexIndex(Vertices[1]));
        }

        //API
        public void RecalculateAngle()
        {
            float xDiff = Vertices[1].x - Vertices[0].x;
            float yDiff = Vertices[1].z - Vertices[0].z;
            double angle =  ((Math.Atan2(yDiff, xDiff) * (180 / Math.PI)) + 180);
            LightAngle = (float)angle;
        }

        //Clone interface
        public object Clone()
        {
            return new FacadeBoundElement(this.LightAngle, this.Height, this.Vertices[0].Clone(), this.Vertices[1].Clone());
        }

        //Constructors
        public FacadeBoundElement(float lightAngle, float height, Vertex leftVertex, Vertex rightVertex)
        {
            LightAngle = lightAngle;
            Height = height;
            Vertices[0] = leftVertex;
            Vertices[1] = rightVertex;
        }

        public FacadeBoundElement()
        {
            //But nobody came
        }
    }
}

