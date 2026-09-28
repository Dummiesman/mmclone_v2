using System;
using System.IO;

#if PSDLLIB_RUNTIME
using TextureRef = System.Int32;
#else
using TextureRef = System.String;
#endif

namespace PSDL.Elements
{
    public class FacadeElement : SDLElementBase, IGeometricSDLElement, ISDLElement, ICloneable
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

        public float BottomHeight;
        public float TopHeight;
        public short UTiling;
        public short VTiling;

        //interface
        public ElementType Type => ElementType.Facade;
        public int Subtype => 6;
        public int RequiredTextureCount => 1;


        public void Read(BinaryReader reader, int subtype, PSDLFile parent)
        {
            BottomHeight = parent.Floats[reader.ReadUInt16()];
            TopHeight = parent.Floats[reader.ReadUInt16()];
            UTiling = reader.ReadInt16();
            VTiling = reader.ReadInt16();
            Vertices[0] = parent.Vertices[reader.ReadUInt16()];
            Vertices[1] = parent.Vertices[reader.ReadUInt16()];
        }

        public void Save(BinaryWriter writer, PSDLFile parent)
        {
            writer.Write((ushort)parent.GetFloatIndex(BottomHeight));
            writer.Write((ushort)parent.GetFloatIndex(TopHeight));
            writer.Write(UTiling);
            writer.Write(VTiling);
            writer.Write((ushort)parent.GetVertexIndex(Vertices[0]));
            writer.Write((ushort)parent.GetVertexIndex(Vertices[1]));
        }

        //API
        public FacadeBoundElement CreateBound()
        {
            var bound = new FacadeBoundElement(0, this.TopHeight, Vertices[0], Vertices[1]);
            bound.RecalculateAngle();
            return bound;
        }

        //Clone interface
        public object Clone()
        {
            return new FacadeElement(this.Textures[0],
                                     this.BottomHeight, 
                                     this.TopHeight, 
                                     this.UTiling, 
                                     this.VTiling, 
                                     this.Vertices[0].Clone(), 
                                     this.Vertices[1].Clone());
        }

        //Constructors
        public FacadeElement(TextureRef texture, float bottomHeight, float topHeight, short uTiling, short vTiling, Vertex leftVertex, Vertex rightVertex)
        {
            Textures = new [] { texture };
            BottomHeight = bottomHeight;
            TopHeight = topHeight;
            UTiling = uTiling;
            VTiling = vTiling;
            Vertices[0] = leftVertex;
            Vertices[1] = rightVertex;
        }

        public FacadeElement()
        {
            //But nobody came
        }
    }
}

