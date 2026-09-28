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
    public class DividedRoadElement : SDLElementBase, IRoad, IGeometricSDLElement, ISDLElement, ICloneable
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

        public ushort Value;
        public DividerType DividerType;
        public DividerFlags DividerFlags;

        /// <summary>
        /// Only used internally, do not set this directly
        /// </summary>
        internal byte DividerTexture;

        public TextureRef[] DividerTextures = new TextureRef[4];

        //IRoad
        public int RowCount => Vertices.Count / RowBreadth;
        public int RowBreadth => 6;

        public Vertex[] GetSidewalkBoundary(int rowNum)
        {
            var row = GetRow(rowNum);
            return new Vertex[] { row[0], row[1], row[4], row[5] };
        }

        public TextureRef GetTexture(RoadTextureType type)
        {
            return Textures[(int)type];
        }

        public void SetTexture(RoadTextureType type, TextureRef texture)
        {
            Textures[(int)type] = texture;
        }

        public void DeleteSidewalk(SidewalkRemovalMode mode)
        {
            //mm2 says if vert[0] == vert[1] then don't draw the sidewalk
            for (int i = 0; i < RowCount; i++)
            {
                var row = GetRow(i);
                if (mode == SidewalkRemovalMode.MoveRoadOutwards)
                {
                    row[1] = row[0];
                    row[4] = row[5];
                }
                else
                {
                    row[0] = row[1];
                    row[5] = row[4];
                }
                SetRow(i, row);
            }
        }

        public void AddRow(Vertex[] vertices)
        {
            if (vertices.Length != 6)
                throw new Exception("Wrong amount of vertices for a row.");
            AddRow(vertices[0], vertices[1], vertices[2], vertices[3], vertices[4], vertices[5]);
        }

        public void SetRow(int rowId, Vertex[] vertices)
        {
            if (vertices.Length != 6)
                throw new Exception("Wrong amount of vertices for a row.");
            SetRow(rowId, vertices[0], vertices[1], vertices[2], vertices[3], vertices[4], vertices[5]);
        }

        public Vertex[] GetRow(int rowId)
        {
            int baseIndex = rowId * 6;
            return new[] { Vertices[baseIndex], Vertices[baseIndex + 1], Vertices[baseIndex + 2], Vertices[baseIndex + 3], Vertices[baseIndex + 4], Vertices[baseIndex + 5] };
        }

        public Vertex GetRowCenterPoint(int rowId)
        {
            var row = GetRow(rowId);
            return (row[0] + row[5]) / 2f;
        }

        //interface
        public ElementType Type => ElementType.DividedRoad;
        public int Subtype
        {
            get
            {
                var segmentCount = Vertices.Count / 6;
                return (segmentCount > Constants.MaxSubtype) ? 0 : segmentCount;
            }
        }
        public int RequiredTextureCount => 3;

        public void Read(BinaryReader reader, int subtype, PSDLFile parent)
        {
            var numSections = (ushort)subtype;
            if (numSections == 0)
                numSections = reader.ReadUInt16();

            var flagType = reader.ReadByte();
            DividerFlags = (DividerFlags)(flagType >> 2);
            DividerType = (DividerType)(flagType & 3);

            DividerTexture = reader.ReadByte();
#if !PSDLLIB_RUNTIME
            DividerTextures = new[]
            {
                parent.GetTextureFromCache(DividerTexture - 1), parent.GetTextureFromCache(DividerTexture),
                parent.GetTextureFromCache(DividerTexture + 1), parent.GetTextureFromCache(DividerTexture + 2)
            };
#else
            DividerTextures = new[]
            {
                DividerTexture - 1, DividerTexture, DividerTexture + 1, DividerTexture + 2
            };
#endif

            Value = reader.ReadUInt16();

            for (var i = 0; i < numSections * 6; i++)
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
                writer.Write((ushort)(Vertices.Count / 6));
            }

            writer.Write((byte)(((byte)DividerFlags << 2) | (byte)DividerType));
            writer.Write(DividerTexture);
            writer.Write(Value);

            //write indices
            for (var i = 0; i < Vertices.Count; i++)
            {
                writer.Write((ushort)parent.GetVertexIndex(Vertices[i]));
            }
        }

        //API
        public TextureRef GetDividerTexture(DividerTextureType type)
        {
            switch (type)
            {
                case DividerTextureType.Top:
                    if (DividerType == DividerType.Wedged || DividerType == DividerType.Elevated)
                    {
                        return DividerTextures[2];
                    }
                    else
                    {
                        return DividerTextures[1];
                    }
                case DividerTextureType.Side:
                    if (DividerType == DividerType.Wedged)
                    {
                        return DividerTextures[1];
                    }
                    else if (DividerType == DividerType.Elevated)
                    {
                        return DividerTextures[0];
                    }
                    break;
                case DividerTextureType.SideStrips:
                    if (DividerType == DividerType.Elevated)
                        return DividerTextures[1];
                    break;
                case DividerTextureType.Cap:
                    return DividerTextures[3];
            }
            return -1;
        }

        public void SetDividerTexture(DividerTextureType type, TextureRef texture)
        {
            switch (type)
            {
                case DividerTextureType.Top:
                    if (DividerType == DividerType.Wedged || DividerType == DividerType.Elevated)
                    {
                        DividerTextures[2] = texture;
                    }
                    else
                    {
                        DividerTextures[1] = texture;
                    }
                    break;
                case DividerTextureType.Side:
                    if (DividerType == DividerType.Wedged)
                    {
                        DividerTextures[1] = texture;
                    }
                    else if (DividerType == DividerType.Elevated)
                    {
                        DividerTextures[0] = texture;
                    }
                    break;
                case DividerTextureType.SideStrips:
                    if (DividerType == DividerType.Elevated)
                        DividerTextures[1] = texture;
                    break;
                case DividerTextureType.Cap:
                    DividerTextures[3] = texture;
                    break;
            }
        }

        public void AddRow(Vertex sidewalkOuterLeft, Vertex sidewalkInnerLeft, Vertex dividerLeft, Vertex dividerRight, Vertex sidewalkInnerRight, Vertex sidewalkOuterRight )
        {
            Vertices.AddRange(new[] { sidewalkOuterLeft, sidewalkInnerLeft, dividerLeft, dividerRight, sidewalkInnerRight, sidewalkOuterRight });
        }

        public void SetRow(int rowId, Vertex sidewalkOuterLeft, Vertex sidewalkInnerLeft, Vertex sidewalkInnerRight, Vertex dividerLeft, Vertex dividerRight,
            Vertex sidewalkOuterRight)
        {
            int baseIndex = rowId * 6;
            Vertices[baseIndex] = sidewalkOuterLeft;
            Vertices[baseIndex + 1] = sidewalkInnerLeft;
            Vertices[baseIndex + 2] = dividerLeft;
            Vertices[baseIndex + 3] = dividerRight;
            Vertices[baseIndex + 4] = sidewalkInnerRight;
            Vertices[baseIndex + 5] = sidewalkOuterRight;
        }


        //Clone interface
        public object Clone()
        {
            var cloneRoad = new DividedRoadElement()
            {
                DividerFlags = this.DividerFlags,
                Value = this.Value,
                DividerType = this.DividerType
            };

            //clone arrays
            Textures = new TextureRef[this.Textures.Length];
            DividerTextures.CopyTo(cloneRoad.DividerTextures, 0);
            Textures.CopyTo(cloneRoad.Textures, 0);

            for (int i = 0; i < this.Vertices.Count; i++)
                cloneRoad.Vertices.Add(this.Vertices[i].Clone());

            return cloneRoad;
        }


        public DividedRoadElement(TextureRef roadTexture, TextureRef sidewalkTexture, TextureRef LODTexture)
        {
            Textures = new[] { roadTexture, sidewalkTexture, LODTexture };
        }

        public DividedRoadElement()
        {      
        }
    }
}
