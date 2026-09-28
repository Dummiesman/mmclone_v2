using System;
using System.IO;

#if PSDLLIB_RUNTIME
using TextureRef = System.Int32;
#else
using TextureRef = System.String;
#endif

namespace PSDL.Elements
{
    public class TunnelElement : SDLElementBase, ISDLElement, ICloneable
    {
        [Flags]
        public enum TunnelFlags : ushort
        {
            LeftSide = 1,
            RightSide = 2,
            IsWall = 4,
            FlatCeiling = 8,
            ClosedStartLeft = 16,
            ClosedEndLeft = 32,
            ClosedStartRight = 64,
            ClosedEndRight = 128,
            CurvedCeiling = 256,
            OffsetStartLeft = 512,
            OffsetEndLeft = 1024,
            OffsetStartRight = 2048,
            OffsetEndRight = 4096,
            CurvedSides = 8192,
            Culled = 16384
        }

        //subtypes are the element's size in 16-bit words:
        //  0x2 = flags + height                                        (old betas)
        //  0x3 = flags + height + unknown
        //  0x9 = flags + height + junction data                        (old betas)
        //  0xA = flags + height + unknown + junction data
        public const int SubtypeShort = 0x2;
        public const int SubtypeStandard = 0x3;
        public const int SubtypeJunctionShort = 0x9;
        public const int SubtypeJunction = 0xA;

        public bool IsJunctionTunnel;

        /// <summary>
        /// Whether this element carries the trailing (seemingly unused) short.
        /// False only for legacy beta PSDLs, which use subtypes 0x2 / 0x9.
        /// </summary>
        public bool HasUnknown = true;

        public TunnelFlags Flags;

        /// <summary>
        /// Height of the tunnel. Maximum 255 meters.
        /// </summary>
        public float Height;

        /// <summary>
        /// Appears to be unused by the game. Only present on subtypes 0x3 and 0xA.
        /// </summary>
        public ushort Unknown;

        //junction-only data
        /// <summary>
        /// Index of the vertex the junction ceiling is generated from. Zero based, as stored in the file.
        /// </summary>
        public ushort CeilingOriginVertex;

        /// <summary>
        /// One bit per junction edge: set means a wall is generated on that edge.
        /// </summary>
        public uint WallBits;

        /// <summary>
        /// One bit per junction edge: inverts the first edge of the generated wall.
        /// </summary>
        public uint WallInvertFirstEdge;

        /// <summary>
        /// One bit per junction edge: inverts the second edge of the generated wall.
        /// </summary>
        public uint WallInvertSecondEdge;

        //properties
        /// <summary>
        /// Wall width is equivalent of Height * (1 / 3). It's automatically determined, and cannot be set.
        /// </summary>
        public float WallWidth => Height * (1f / 3f);

        /// <summary>
        /// Wall underside depth is equivalent of Height * (1 / 4). It's automatically determined
        /// </summary>
        public float WallUndersideDepth => Height * (1f / 4f);

        //interface
        public ElementType Type => ElementType.Tunnel;

        public int Subtype
        {
            get
            {
                if (IsJunctionTunnel)
                    return HasUnknown ? SubtypeJunction : SubtypeJunctionShort;
                return HasUnknown ? SubtypeStandard : SubtypeShort;
            }
        }

        public int RequiredTextureCount => 6;

        //bit helpers
        public bool GetWallVisible(int edgeIndex) => GetBit(WallBits, edgeIndex);
        public void SetWallVisible(int edgeIndex, bool value) => WallBits = SetBit(WallBits, edgeIndex, value);

        public bool GetWallFirstEdgeInverted(int edgeIndex) => GetBit(WallInvertFirstEdge, edgeIndex);
        public void SetWallFirstEdgeInverted(int edgeIndex, bool value) => WallInvertFirstEdge = SetBit(WallInvertFirstEdge, edgeIndex, value);

        public bool GetWallSecondEdgeInverted(int edgeIndex) => GetBit(WallInvertSecondEdge, edgeIndex);
        public void SetWallSecondEdgeInverted(int edgeIndex, bool value) => WallInvertSecondEdge = SetBit(WallInvertSecondEdge, edgeIndex, value);

        private static bool GetBit(uint bits, int index)
        {
            if (index < 0 || index > 31)
                throw new ArgumentOutOfRangeException(nameof(index), "Junction edge index must be between 0 and 31.");
            return (bits & (1u << index)) != 0;
        }

        private static uint SetBit(uint bits, int index, bool value)
        {
            if (index < 0 || index > 31)
                throw new ArgumentOutOfRangeException(nameof(index), "Junction edge index must be between 0 and 31.");
            return value ? bits | (1u << index) : bits & ~(1u << index);
        }

        public void Read(BinaryReader reader, int subtype, PSDLFile parent)
        {
            if (subtype == 0)
                subtype = reader.ReadUInt16();

            if (subtype != SubtypeShort && subtype != SubtypeStandard &&
                subtype != SubtypeJunctionShort && subtype != SubtypeJunction)
            {
                throw new InvalidDataException($"Unexpected tunnel subtype 0x{subtype:X}.");
            }

            IsJunctionTunnel = (subtype == SubtypeJunction || subtype == SubtypeJunctionShort);
            HasUnknown = (subtype == SubtypeStandard || subtype == SubtypeJunction);

            Flags = (TunnelFlags)reader.ReadUInt16();
            Height = reader.ReadUInt16() / 256f;

            //not present on the really old (beta) subtypes
            if (HasUnknown)
                Unknown = reader.ReadUInt16();

            if (IsJunctionTunnel)
            {
                CeilingOriginVertex = reader.ReadUInt16();
                WallBits = reader.ReadUInt32();
                WallInvertFirstEdge = reader.ReadUInt32();
                WallInvertSecondEdge = reader.ReadUInt32();
            }
        }

        public void Save(BinaryWriter writer, PSDLFile parent)
        {
            writer.Write((ushort)Flags);

            var scaledHeight = Math.Round(Height * 256f);
            if (scaledHeight < 0) scaledHeight = 0;
            if (scaledHeight > ushort.MaxValue) scaledHeight = ushort.MaxValue;
            writer.Write((ushort)scaledHeight);

            if (HasUnknown)
                writer.Write(Unknown);

            if (IsJunctionTunnel)
            {
                writer.Write(CeilingOriginVertex);
                writer.Write(WallBits);
                writer.Write(WallInvertFirstEdge);
                writer.Write(WallInvertSecondEdge);
            }
        }

        //Clone interface
        public object Clone()
        {
            var cloneTunnel = new TunnelElement();

            //clone texture array
            cloneTunnel.Textures = new TextureRef[Textures.Length];
            Textures.CopyTo(cloneTunnel.Textures, 0);

            //clone properties
            cloneTunnel.Flags = Flags;
            cloneTunnel.Height = Height;
            cloneTunnel.IsJunctionTunnel = IsJunctionTunnel;
            cloneTunnel.HasUnknown = HasUnknown;
            cloneTunnel.Unknown = Unknown;
            cloneTunnel.CeilingOriginVertex = CeilingOriginVertex;
            cloneTunnel.WallBits = WallBits;
            cloneTunnel.WallInvertFirstEdge = WallInvertFirstEdge;
            cloneTunnel.WallInvertSecondEdge = WallInvertSecondEdge;

            return cloneTunnel;
        }

        //Constructors
        public TunnelElement(TextureRef leftWallTexture, TextureRef rightWallTexture, TextureRef ceilingTexture,
            TextureRef outerRightTexture, TextureRef outerLeftTexture, TextureRef undersideTexture,
            TunnelFlags flags, float height)
        {
            Textures = new[] { leftWallTexture, rightWallTexture, ceilingTexture, outerRightTexture, outerLeftTexture, undersideTexture };
            Flags = flags;
            Height = height;
            IsJunctionTunnel = false;
        }

        public TunnelElement(TextureRef leftWallTexture, TextureRef rightWallTexture, TextureRef ceilingTexture,
            TextureRef outerRightTexture, TextureRef outerLeftTexture, TextureRef undersideTexture,
            TunnelFlags flags, float height, ushort ceilingOriginVertex, uint wallBits,
            uint wallInvertFirstEdge = 0, uint wallInvertSecondEdge = 0)
        {
            Textures = new[] { leftWallTexture, rightWallTexture, ceilingTexture, outerRightTexture, outerLeftTexture, undersideTexture };
            Flags = flags;
            Height = height;
            IsJunctionTunnel = true;
            CeilingOriginVertex = ceilingOriginVertex;
            WallBits = wallBits;
            WallInvertFirstEdge = wallInvertFirstEdge;
            WallInvertSecondEdge = wallInvertSecondEdge;
        }

        public TunnelElement()
        {
            //But nobody came
        }
    }
}