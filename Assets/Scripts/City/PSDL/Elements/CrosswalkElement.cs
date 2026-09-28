using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#if PSDLLIB_RUNTIME
using TextureRef = System.Int32;
#else
using TextureRef = System.String;
#endif

namespace PSDL.Elements
{
    public class CrosswalkElement : SDLElementBase, IGeometricSDLElement, ISDLElement, ICloneable
    {
        public Vertex[] Vertices = new Vertex[4];

        //IGeometricSDLElement
        public Vertex[] GetVertices() => Vertices;
        public Vertex GetVertex(int index) => Vertices[index];
        public void SetVertex(int index, Vertex vertex) => Vertices[index] = vertex;
        public int GetVertexCount() => Vertices.Length;
        public void RemoveVertexAt(int idx) => throw new NotImplementedException();
        public void AddVertex() => throw new NotImplementedException();
        public void InsertVertex(int idx, Vertex vtx) => throw new NotImplementedException();
        public void InsertVertex(int idx) => throw new NotImplementedException();

        //interface
        public ElementType Type => ElementType.Crosswalk;
        public int Subtype => 4;
        public int RequiredTextureCount => 1;
        public override int TextureIndexOffset => 2;


        public void Read(BinaryReader reader, int subtype, PSDLFile parent)
        {
            for (var i = 0; i < 4; i++)
            {
                Vertices[i] = parent.Vertices[reader.ReadUInt16()];
            }
        }

        public void Save(BinaryWriter writer, PSDLFile parent)
        {
            for (var i = 0; i < 4; i++)
            {
                writer.Write((ushort)parent.GetVertexIndex(Vertices[i]));
            }
        }

        //Clone interface
        public object Clone()
        {
            var cloneCrosswalk = new CrosswalkElement
            {
                Textures = new[] {this.Textures[0]},
                Vertices = new Vertex[4]
            };

            for (int i = 0; i < Vertices.Length; i++)
            {
                cloneCrosswalk.Vertices[i] = Vertices[i].Clone();
            }

            return cloneCrosswalk;
        }

        //Constructors
        public CrosswalkElement(TextureRef texture, IEnumerable<Vertex> vertices)
        {
            Textures = new [] { texture };
            Vertices = vertices.ToArray();
        }

        public CrosswalkElement()
        {
            //But nobody came
        }
    }
}

