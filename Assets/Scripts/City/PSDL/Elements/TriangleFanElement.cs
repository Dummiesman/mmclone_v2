using System;
using System.Collections.Generic;
using System.IO;

#if PSDLLIB_RUNTIME
using TextureRef = System.Int32;
#else
using TextureRef = System.String;
#endif

namespace PSDL.Elements
{
    public class TriangleFanElement : SDLElementBase, IGeometricSDLElement, ISDLElement, ICloneable
    {
        public List<Vertex> Vertices = new List<Vertex>();

        //IGeometricSDLElement
        public Vertex[] GetVertices() => Vertices.ToArray();
        public Vertex GetVertex(int index) => Vertices[index];
        public void SetVertex(int index, Vertex vertex) => Vertices[index] = vertex;
        public int GetVertexCount() => Vertices.Count;
        public void RemoveVertexAt(int idx) => Vertices.RemoveAt(idx);
        public void AddVertex() => Vertices.Add(Vertices[Vertices.Count - 1].Clone());
        public void InsertVertex(int idx, Vertex vtx)
        {
            Vertices.Insert(idx, vtx);
        }
        public void InsertVertex(int idx)
        {
            Vertex source;
            if (idx == 0)
                source = Vertices[1];
            else if (idx == GetVertexCount())
                source = Vertices[Vertices.Count - 1];
            else
                source = Vertices[idx - 1];
            Vertices.Insert(idx, source.Clone());
        }

        //interface
        public virtual ElementType Type => ElementType.TriangleFan;
        public int Subtype
        {
            get
            {
                var vcount = Vertices.Count - 2;
                return (vcount > Constants.MaxSubtype) ? 0 : vcount;
            }
        }
        public int RequiredTextureCount => 1;

        public void Read(BinaryReader reader, int subtype, PSDLFile parent)
        {
            var numSections = (ushort)subtype;
            if (numSections == 0)
                numSections = reader.ReadUInt16();

            for (var i = 0; i < numSections + 2; i++)
            {
                var vertexIndex = reader.ReadUInt16();
                Vertices.Add(parent.Vertices[vertexIndex]);
            }
        }

        public void Save(BinaryWriter writer, PSDLFile parent)
        {
            //write count if applicable
            var subtype = Subtype;
            if (subtype == 0)
            {
                writer.Write((ushort)(Vertices.Count - 2));
            }

            //write indices
            for (var i = 0; i < Vertices.Count; i++)
            {
                writer.Write((ushort)parent.GetVertexIndex(Vertices[i]));
            }
        }

        //Clone interface
        public object Clone()
        {
            var cloneTriFan = new TriangleFanElement()
            {
                Textures = new[] { this.Textures[0] },
            };

            for (int i = 0; i < Vertices.Count; i++)
            {
                cloneTriFan.Vertices.Add(Vertices[i].Clone());
            }

            return cloneTriFan;
        }

        //Constructors
        public TriangleFanElement(TextureRef texture, IEnumerable<Vertex> vertices)
        {
            Textures = new [] { texture };
            Vertices.AddRange(vertices);
        }

        public TriangleFanElement()
        {
            //But nobody came
        }

    }
}
