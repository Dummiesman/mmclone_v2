using System;
using System.IO;

#if PSDLLIB_RUNTIME
using TextureRef = System.Int32;
#else
using TextureRef = System.String;
#endif

namespace PSDL.Elements
{
    public class SliverElement : SDLElementBase, IGeometricSDLElement, ISDLElement, ICloneable
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


        public float Height;
        public float TextureScale;

        //interface
        public ElementType Type => ElementType.Sliver;
        public int Subtype => 4;
        public int RequiredTextureCount => 1;

        public void Read(BinaryReader reader, int subtype, PSDLFile parent)
        {
            Height = parent.Floats[reader.ReadUInt16()];
            TextureScale = parent.Floats[reader.ReadUInt16()];
            Vertices[0] = parent.Vertices[reader.ReadUInt16()];
            Vertices[1] = parent.Vertices[reader.ReadUInt16()];
        }

        public void Save(BinaryWriter writer, PSDLFile parent)
        {
            writer.Write((ushort)parent.GetFloatIndex(Height));
            writer.Write((ushort)parent.GetFloatIndex(TextureScale));
            writer.Write((ushort)parent.GetVertexIndex(Vertices[0]));
            writer.Write((ushort)parent.GetVertexIndex(Vertices[1]));
        }

        //API
        public void SetTextureTiling(int numTiles)
        {
            var v1 = new Vertex(Vertices[0].x, Height, Vertices[0].z);
            var v2 = new Vertex(Vertices[1].x, Height, Vertices[1].z);
            TextureScale = (float) numTiles / v1.Distance(v2);
        }

        //Clone interface
        public object Clone()
        {
            var cloneSliver = new SliverElement
            {
                Textures = new[] {this.Textures[0]},
                Height = this.Height,
                TextureScale = this.TextureScale
            };

            for (int i = 0; i < Vertices.Length; i++)
            {
                cloneSliver.Vertices[i] = this.Vertices[i].Clone();
            }

            return cloneSliver;
        }

        //Constructors
        public SliverElement(TextureRef texture, float height, float textureScale, Vertex leftVertex, Vertex rightVertex)
        {
            Textures = new [] { texture };
            Height = height;
            TextureScale = textureScale;
            Vertices[0] = leftVertex;
            Vertices[1] = rightVertex;
        }

        public SliverElement()
        {
            //But nobody came
        }
    }
}

