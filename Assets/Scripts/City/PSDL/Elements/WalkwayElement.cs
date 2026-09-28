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
    public class WalkwayElement : SDLElementBase, IRoad, IGeometricSDLElement, ISDLElement, ICloneable
    {
        public List<Vertex> Vertices = new List<Vertex>();

        //IGeometricSDLElement
        public Vertex[] GetVertices() => Vertices.ToArray();
        public Vertex GetVertex(int index) => Vertices[index];
        public void SetVertex(int index, Vertex vertex) => Vertices[index] = vertex;
        public int GetVertexCount() => Vertices.Count;
        public void RemoveVertexAt(int idx) => throw new NotImplementedException();
        public void AddVertex() => throw new NotImplementedException();
        public void InsertVertex(int idx, Vertex vtx) => throw new NotImplementedException();
        public void InsertVertex(int idx) => throw new NotImplementedException();

        //IRoad
        public int RowCount => Vertices.Count / RowBreadth;
        public int RowBreadth => 2;

        public void AddRow(Vertex[] vertices)
        {
            if (vertices.Length != 2)
                throw new Exception("Wrong amount of vertices for a row.");
            AddRow(vertices[0], vertices[1]);
        }

        public void SetRow(int rowId, Vertex[] vertices)
        {
            if (vertices.Length != RowBreadth)
                throw new Exception("Wrong amount of vertices for a row.");
            SetRow(rowId, vertices[0], vertices[1]);
        }

        public Vertex[] GetRow(int rowId)
        {
            int baseIndex = rowId * 2;
            return new[] { Vertices[baseIndex], Vertices[baseIndex + 1] };
        }

        public Vertex GetRowCenterPoint(int rowId)
        {
            var row = GetRow(rowId);
            return new Vertex((row[0].x + row[1].x) / 2, (row[0].y + row[1].y) / 2, (row[0].z + row[1].z) / 2);
        }

        public void SetTexture(RoadTextureType type, TextureRef texture)
        {
            if (type == RoadTextureType.Surface)
            {
                Textures[0] = texture;
            }
        }

        public TextureRef GetTexture(RoadTextureType type)
        {
            return (type == RoadTextureType.Surface) ? Textures[0] : 0;
        }

        public Vertex[] GetSidewalkBoundary(int rowNum)
        {
            var row = GetRow(rowNum);
            return new Vertex[] { row[0], row[0], row[1], row[1] };
        }

        public void DeleteSidewalk(SidewalkRemovalMode mode)
        {
            // nothing to do here
        }

        //interface
        public ElementType Type => ElementType.Walkway;
        public int Subtype
        {
            get
            {
                var segmentCount = Vertices.Count / 2;
                return (segmentCount > Constants.MaxSubtype) ? 0 : segmentCount;
            }
        }
        public int RequiredTextureCount => 1;

        public void Read(BinaryReader reader, int subtype, PSDLFile parent)
        {
            var numSections = (ushort)subtype;
            if (numSections == 0)
                numSections = reader.ReadUInt16();

            for (var i = 0; i < numSections; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var vertexIndex = reader.ReadUInt16();
                    Vertices.Add(parent.Vertices[vertexIndex]);
                }
            }
        }

        public void Save(BinaryWriter writer, PSDLFile parent)
        {
            //write count if applicable
            var subtype = Subtype;
            if (subtype == 0)
            {
                writer.Write((ushort)(Vertices.Count / 2));
            }

            //write indices
            for (var i = 0; i < Vertices.Count; i++)
            {
                writer.Write((ushort)parent.GetVertexIndex(Vertices[i]));
            }
        }

        //API
        public void AddRow(Vertex leftSide, Vertex rightSide)
        {
            Vertices.AddRange(new []{leftSide, rightSide});
        }

        public void SetRow(int rowId, Vertex leftSide, Vertex rightSide)
        {
            int baseIndex = rowId * RowBreadth;
            Vertices[baseIndex] = leftSide;
            Vertices[baseIndex + 1] = rightSide;
        }


        //Clone interface
        public object Clone()
        {
            var cloneWalkway = new WalkwayElement()
            {
                Textures = new[] { this.Textures[0] },
            };

            for (int i = 0; i < Vertices.Count; i++)
            {
                cloneWalkway.Vertices.Add(Vertices[i].Clone());
            }

            return cloneWalkway;
        }

        //Constructors
        public WalkwayElement(TextureRef texture, IEnumerable<Vertex> vertices)
        {
            Textures = new[] { texture };
            Vertices.AddRange(vertices);
        }

        public WalkwayElement()
        {
            //But nobody came
        }
    }
}
