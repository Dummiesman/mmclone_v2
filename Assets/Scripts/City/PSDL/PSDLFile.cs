using PSDL.Elements;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;

//textures are stored as names when authoring, and as indices into m_Textures at runtime
#if PSDLLIB_RUNTIME
using TextureRef = System.Int32;
#else
using TextureRef = System.String;
#endif

namespace PSDL
{
    public class PSDLFile
    {
        /// <summary>
        /// The file's texture name table. Always names, in both builds - at runtime the
        /// elements hold indices into this list rather than copies of the names.
        /// </summary>
        private List<string> m_Textures;

        /// <summary>
        /// The value stored in an element texture slot when the element has no texture.
        /// </summary>
#if PSDLLIB_RUNTIME
        public static readonly TextureRef NullTexture = -1;
#else
        public static readonly TextureRef NullTexture = null;
#endif
        public List<float> Floats;
        public List<Vertex> Vertices;
        public List<Room> Rooms;
        public List<AIRoad> AIRoads;

        /// <summary>
        /// References material indices in materials.mtl
        /// </summary>
        public Dictionary<string, byte> MaterialIndices;

        /// <summary>
        /// All elements in the SDL file. If you want to modify elements, modify them per room.
        /// </summary>
        public IEnumerable<ISDLElement> Elements => Rooms.SelectMany(x => x.Elements);

        private string m_FilePath;
        private bool m_ElementMapBuilt;
        private Dictionary<int, Type> _elementMap;

        public Vertex DimensionsMin { get; set; }
        public Vertex DimensionsMax { get; set; }
        public Vertex DimensionsCenter { get; set; }
        public float DimensionsRadius { get; set; }

        private int m_Version = 0;
        public int Version
        {
            get => m_Version;
            set
            {
                if (value > 1)
                    throw new Exception("Only PSD0 and PSD1 are supported");
                m_Version = value;
            }
        }

        /// <summary>
        /// Performance tweak. Makes CalculateBounds only use perimeter vertices.
        /// </summary>
        public static bool FasterCalculateBounds = true;

        //API for elements to use (maybe there's a better way?)
        private Dictionary<Vertex, int> m_VertexIndexMap = new Dictionary<Vertex, int>();
        private Dictionary<float, int> m_FloatMap = new Dictionary<float, int>();

        /// <summary>
        /// Only to be used by SDLElement.Save
        /// </summary>
        public int GetFloatIndex(float flt)
        {
            return m_FloatMap[flt];
        }

        /// <summary>
        /// Only to be used by SDLElement.Save
        /// </summary>
        public int GetVertexIndex(Vertex vtx)
        {
            return m_VertexIndexMap[vtx];
        }

        /// <summary>
        /// Only to be used by SDLElement.Save
        /// </summary>
        public int GetTextureIndex(TextureRef tex)
        {
#if PSDLLIB_RUNTIME
            //already an index
            return tex;
#else
            return m_Textures.IndexOf(tex);
#endif
        }

        /// <summary>
        /// Only to be used by SDLElement.Save
        /// </summary>
        public string GetTextureFromCache(int id)
        {
            //fix for j01
            if (id < 0)
                id = 0;

            //fix for out of range
            if (id >= m_Textures.Count)
                return null;

            return m_Textures[id];
        }

        //API
        public IEnumerable<T> FindAllElementsOfType<T>() where T : ISDLElement
        {
            foreach (var room in Rooms)
            {
                foreach (var element in room.Elements)
                {
                    if (element is T)
                        yield return (T)element;
                }
            }
        }

        private void RebuildElementMap()
        {
            m_ElementMapBuilt = true;
            _elementMap = new Dictionary<int, Type>();

            foreach (var mytype in Assembly.GetExecutingAssembly().GetTypes().Where(mytype => mytype.GetInterfaces().Contains(typeof(ISDLElement))))
            {
                var element = (ISDLElement)Activator.CreateInstance(mytype);
                _elementMap[(int)element.Type] = element.GetType();
            }
        }

        /// <summary>
        /// Turns a texture table index into whatever this build stores in a texture slot:
        /// the name when authoring, the index itself at runtime. Out of range yields NullTexture.
        /// </summary>
        public TextureRef GetTextureRef(int id)
        {
#if PSDLLIB_RUNTIME
            //fix for j01
            if (id < 0)
                id = 0;

            //fix for out of range
            if (id >= m_Textures.Count)
                return NullTexture;

            return id;
#else
            return GetTextureFromCache(id);
#endif
        }

        /// <summary>
        /// As GetTextureRange, but yields whatever this build stores in a texture slot.
        /// </summary>
        private TextureRef[] GetTextureRefRange(int start, int count)
        {
            var returnList = new TextureRef[count];
            for (var i = 0; i < count; i++)
            {
                var id = start + i;
                returnList[i] = (id >= m_Textures.Count) ? NullTexture : GetTextureRef(id);
            }
            return returnList;
        }

        /// <summary>
        /// Gets a range from m_Textures, will return null entries for anything out of range
        /// </summary>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private List<string> GetTextureRange(int start, int count)
        {
            List<string> returnList = new List<string>(count);
            for (int i = start; i < start + count; i++)
            {
                if (i >= m_Textures.Count)
                {
                    returnList.Add(null);
                }
                else
                {
                    returnList.Add(m_Textures[i]);
                }
            }
            return returnList;
        }

        private void Load(Stream stream)
        {
            if (!m_ElementMapBuilt)
                RebuildElementMap();

            //clear these, we'll be overwriting the data
            Vertices.Clear();
            Rooms.Clear();
            Floats.Clear();
            m_Textures.Clear();

            using (var r = new BinaryReader(stream, Encoding.ASCII))
            {
                //verify our magic is PSD0
                var magic = new string(r.ReadChars(4));
                if (magic == "PSD0")
                {
                    Version = 0;
                }
                else if (magic == "PSD1")
                {
                    Version = 1;
                }
                else
                {
                    throw new Exception("Incorrect magic, this may not be a PSDL file");
                }


                var targetSize = r.ReadUInt32();
                if (targetSize != 2)
                {
                    throw new Exception("Incorrect target size. Expected 2, got " + targetSize.ToString() + ".");
                }

                //read Vertices
                var numVertices = r.ReadUInt32();
                for (var i = 0; i < numVertices; i++)
                {
                    Vertices.Add(new Vertex(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                }

                //read Floats
                var numFloats = r.ReadUInt32();
                for (var i = 0; i < numFloats; i++)
                {
                    Floats.Add(r.ReadSingle());
                }

                //read Textures
                var numTextures = r.ReadUInt32() - 1;
                for (var i = 0; i < numTextures; i++)
                {
                    string textureName = string.Empty;
                    var textureLen = r.ReadByte();

                    if (textureLen > 0)
                    {
                        textureName = new string(r.ReadChars(textureLen - 1));
                        r.BaseStream.Seek(1, SeekOrigin.Current);
                    }

                    m_Textures.Add(textureName);

                    //introduced in version 1
                    if (Version == 1)
                        MaterialIndices[textureName] = r.ReadByte();
                }

                //read rooms
                var numRooms = r.ReadUInt32() - 1;
                var somethingWeird = r.ReadUInt32();

                uint currentTextureId = 0;

                var perimeterLinks = new Dictionary<PerimeterPoint, ushort>();

                for (var i = 0; i < numRooms; i++)
                {
                    var perimPoints = new List<PerimeterPoint>();
                    var roomElements = new List<ISDLElement>();

                    var numPerimeterPoints = r.ReadUInt32();
                    var numAttributes = r.ReadUInt32();

                    //Perimeter points
                    for (var j = 0; j < numPerimeterPoints; j++)
                    {
                        var perimVertIndex = r.ReadUInt16();
                        var perimBlockIndex = r.ReadUInt16();

                        perimPoints.Add(new PerimeterPoint(Vertices[perimVertIndex], null));

                        perimeterLinks[perimPoints[perimPoints.Count - 1]] = perimBlockIndex;
                    }

                    //attributes
                    var attributeStartAddress = r.BaseStream.Position;
                    while (r.BaseStream.Position < (attributeStartAddress + (numAttributes * 2)))
                    {
                        //extract attribute Type and subtype
                        var att = r.ReadUInt16();
                        int type = att >> 3 & 15;
                        int subtype = att & 7;

                        //new texture
                        if (type == 10)
                        {
                            //new texture
                            var textureId = (ushort)(r.ReadUInt16() + (256 * subtype) - 1);
                            currentTextureId = textureId;
                            continue;
                        }

                        //get the loader
                        ISDLElement loader = (ISDLElement)Activator.CreateInstance(_elementMap[type]);

                        //some Elements require a texture offset
                        var textureOffset = loader.TextureIndexOffset;

                        //read data, setup textures
                        loader.Read(r, subtype, this);

                        var requiredTextureCount = loader.RequiredTextureCount;
                        if (requiredTextureCount > 0)
                        {
                            if (currentTextureId != 65535)
                            {
                                loader.Textures = GetTextureRefRange((int)currentTextureId + textureOffset, requiredTextureCount);
                            }
                            else
                            {
                                //null texture
                                loader.Textures = new TextureRef[requiredTextureCount];
                                for (var j = 0; j < requiredTextureCount; j++)
                                    loader.Textures[j] = NullTexture;
                            }
                        }

                        //finally, add to room
                        roomElements.Add(loader);

                    }

                    //finally, store this room
                    Rooms.Add(new Room(roomElements, perimPoints));

                }


                //set Perimeter links
                for (var i = 0; i < Rooms.Count; i++)
                {
                    var room = Rooms[i];
                    foreach (var pp in room.Perimeter)
                    {
                        var ppIndex = perimeterLinks[pp];
                        pp.ConnectedRoom = (ppIndex > 0) ? Rooms[ppIndex - 1] : null;
                    }
                }

                //skip padding? byte, read Flags
                r.BaseStream.Seek(1, SeekOrigin.Current);
                for (var i = 0; i < numRooms; i++)
                {
                    Rooms[i].Flags = (RoomFlags)r.ReadByte();
                }

                //skip padding? byte, read proprule index
                r.BaseStream.Seek(1, SeekOrigin.Current);
                for (var i = 0; i < numRooms; i++)
                {
                    Rooms[i].PropRule = r.ReadByte();
                }

                //dimensions
                DimensionsMin = new Vertex(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                DimensionsMax = new Vertex(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                DimensionsCenter = new Vertex(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                DimensionsRadius = r.ReadSingle();

                //read ai roads (TODO: Figure out version 1 structure)
                var numAiRoads = Version == 0 ? r.ReadUInt32() : 0;
                for (var i = 0; i < numAiRoads; i++)
                {
                    //lots of unknown stuff
                    var flags = r.ReadUInt32();
                    var u3 = r.ReadByte();
                    var u4 = r.ReadByte();

                    //? some kind of distances
                    var floatData1 = new float[u3];
                    var floatData2 = new float[u4];
                    for (var j = 0; j < floatData1.Length; j++)
                    {
                        floatData1[j] = r.ReadSingle();
                    }
                    for (var j = 0; j < floatData2.Length; j++)
                    {
                        floatData2[j] = r.ReadSingle();
                    }


                    var flags3 = r.ReadUInt16();

                    //read road start Vertices
                    var scr = new Vertex[4];
                    for (var j = 0; j < 4; j++)
                    {
                        scr[j] = Vertices[r.ReadUInt16()];
                    }

                    //read road end Vertices
                    var ecr = new Vertex[4];
                    for (var j = 0; j < 4; j++)
                    {
                        ecr[j] = Vertices[r.ReadUInt16()];
                    }

                    var numRoomsInRoad = r.ReadByte();
                    var roadRooms = new Room[numRoomsInRoad];
                    for (var j = 0; j < numRoomsInRoad; j++)
                    {
                        //for some strange reason these are signed
                        var roomNumber = Math.Abs(r.ReadInt16());
                        roadRooms[j] = Rooms[roomNumber - 1];
                    }

                    AIRoads.Add(new AIRoad((AIRoad.PropulationFlags)flags, floatData1, floatData2, flags3, roadRooms));
                }

                //HACK: We don't check unconsumed bytes for version one, as we don't fully know the structure
                var unconsumedBytes = r.BaseStream.Length - r.BaseStream.Position;
                if (unconsumedBytes > 0 && Version == 0)
                    throw new Exception("Reader didn't consume all bytes. Something happened!!");
            }
        }

        //useful saving functions
        private static string TextureHashString(TextureRef[] textures)
        {
#if PSDLLIB_RUNTIME
            //an int slot can't be null, so the empty marker is NullTexture
            if (textures == null || textures.Length == 0 || textures[0] == NullTexture)
                return null;
#else
            if (textures?[0] == null)
                return null;
#endif

            var hash = string.Empty;
            foreach (var texture in textures)
            {
                hash += $"{texture}|";
            }
            return hash;
        }

        private ushort BuildAttributeHeader(bool last, int type, int subtype)
        {
            if (subtype > Constants.MaxSubtype)
                throw new Exception($"Tried to create subtype > MAX_SUBTYPE for Type {type}, got subtype {subtype}.");

            if (type > Constants.MaxType)
                throw new Exception($"Tried to create attribute header with Type > MAX_TYPE, got Type {type}");

            var iLast = (last) ? 1 : 0;
            ushort ret = (byte)(iLast << 7 | type << 3 | subtype);
            return ret;
        }


        //cache functions
        private void RefreshVertexCache()
        {
            Vertices.Clear();
            m_VertexIndexMap.Clear();
            foreach (var rm in Rooms)
            {
                foreach (var vtx in rm.GatherVertices())
                {
                    if (!m_VertexIndexMap.ContainsKey(vtx))
                    {
                        m_VertexIndexMap.Add(vtx, Vertices.Count);
                        Vertices.Add(vtx);
                    }
                }
            }
        }

        private Dictionary<string, int> GenerateTextureHashDictionary()
        {
#if PSDLLIB_RUNTIME
            //runtime elements already hold indices into m_Textures, so the table is passed
            //through untouched. rebuilding it from indices would be meaningless and would
            //break the round trip - all that's needed is hash -> base index, which each
            //element already knows.
            //
            //note this also means DividedRoadElement.DividerTexture keeps the value it was
            //read with, rather than being reassigned as it is in the authoring path.
            var textureHashDictionary = new Dictionary<string, int>();

            foreach (var el in Elements)
            {
                //no Textures, no hash
                if (el.RequiredTextureCount == 0)
                    continue;

                var hash = TextureHashString(el.Textures);

                //weird invisble Textures
                if (hash == null)
                    continue;

                textureHashDictionary[hash] = el.Textures[0];
            }

            return textureHashDictionary;
#else
            //build Textures cache. start with divided roads
            //since they have a special index for Textures
            //which is only a wee little byte
            m_Textures.Clear();

            var textureHashDictionary = new Dictionary<string, int>();

            foreach (var rm in Rooms)
            {
                foreach (var el in rm.Elements)
                {
                    //STEP 1 : CACHE DVIIDED ROAD DIVIDERS (We only have 1 byte)
                    if (el is DividedRoadElement)
                    {
                        var dr = (DividedRoadElement)el;
                        var hash = TextureHashString(dr.DividerTextures);

                        //weird invisble Textures
                        if (hash == null)
                            continue;

                        if (!textureHashDictionary.ContainsKey(hash))
                        {
                            textureHashDictionary[hash] = m_Textures.Count;
                            m_Textures.AddRange(dr.DividerTextures);
                        }
                        dr.DividerTexture = (byte)(textureHashDictionary[hash] + 1);
                    }

                    //STEP 2 : CACHE ROADS AND TUNNELS
                    if (el is DividedRoadElement || el is RoadElement || el is TunnelElement || el is WalkwayElement)
                    {
                        var hash = TextureHashString(el.Textures);

                        //weird invisble Textures
                        if (hash == null)
                            continue;

                        if (!textureHashDictionary.ContainsKey(hash))
                        {
                            textureHashDictionary[hash] = m_Textures.Count;
                            m_Textures.AddRange(el.Textures);
                        }
                    }
                }
            }

            //next build everything else
            foreach(var el in Elements) { 
                //no Textures, no hash
                if (el.RequiredTextureCount == 0)
                    continue;

                var hash = TextureHashString(el.Textures);

                //weird invisble Textures
                if (hash == null)
                    continue;

                if (!textureHashDictionary.ContainsKey(hash))
                {
                    textureHashDictionary[hash] = m_Textures.Count;
                    m_Textures.AddRange(el.Textures);
                }
            }

            return textureHashDictionary;
#endif
        }

        private void RefreshFloatCache()
        {
            //build floats cache
            Floats.Clear();
            m_FloatMap.Clear();

            foreach (var rm in Rooms)
            {
                foreach (var flt in rm.GatherFloats())
                {
                    if (!m_FloatMap.ContainsKey(flt))
                    {
                        m_FloatMap[flt] = Floats.Count;
                        Floats.Add(flt);
                    }
                }
            }
        }

        public void RecalculateBounds()
        {
            var average = new Vertex(0, 0, 0);
            DimensionsMin = new Vertex(0, 0, 0);
            DimensionsMax = new Vertex(0, 0, 0);
            DimensionsCenter = average;

            int count = 0;
            if (FasterCalculateBounds)
            {
                foreach (var room in Rooms)
                {
                    foreach (var vtx in room.Perimeter)
                    {
                        DimensionsMin.x = Math.Min(vtx.Vertex.x, DimensionsMin.x);
                        DimensionsMin.y = Math.Min(vtx.Vertex.y, DimensionsMin.y);
                        DimensionsMin.z = Math.Min(vtx.Vertex.z, DimensionsMin.z);
                        DimensionsMax.x = Math.Max(vtx.Vertex.x, DimensionsMax.x);
                        DimensionsMax.y = Math.Max(vtx.Vertex.y, DimensionsMax.y);
                        DimensionsMax.z = Math.Max(vtx.Vertex.z, DimensionsMax.z);
                        average += vtx.Vertex;
                        count++;
                    }
                }
            }
            else
            {
                foreach (var vtx in Vertices)
                {
                    DimensionsMin.x = Math.Min(vtx.x, DimensionsMin.x);
                    DimensionsMin.y = Math.Min(vtx.y, DimensionsMin.y);
                    DimensionsMin.z = Math.Min(vtx.z, DimensionsMin.z);
                    DimensionsMax.x = Math.Max(vtx.x, DimensionsMax.x);
                    DimensionsMax.y = Math.Max(vtx.y, DimensionsMax.y);
                    DimensionsMax.z = Math.Max(vtx.z, DimensionsMax.z);
                    average += vtx;
                    count++;
                }
            }

            average /= count;
            DimensionsCenter = average;

            float radius = DimensionsMax.x - DimensionsMin.x;
            if (DimensionsMax.y - DimensionsMin.y > radius)
            {
                radius = DimensionsMax.y - DimensionsMin.y;
            }
            else if (DimensionsMax.z - DimensionsMin.z > radius)
            {
                radius = DimensionsMax.z - DimensionsMin.z;
            }
            DimensionsRadius = radius / 2;
        }

        public int FindRoomId(float x, float z)
        {
            for (int i = 0; i < Rooms.Count; i++)
            {
                var room = Rooms[i];
                if (room.PointInRoom(x, z))
                {
                    return i;
                }
            }
            return -1;
        }

        public int FindRoomId(float x, float y, float z)
        {
            int bestMatchRoom = -1;
            float bestMatchYDiff = 9999.0f;

            for (int i = 0; i < Rooms.Count; i++)
            {
                var room = Rooms[i];
                if (room.PointInRoom(x, z))
                {
                    float closestPerimeterPoint = 9999.0f;
                    foreach (var point in room.Perimeter)
                    {
                        closestPerimeterPoint = Math.Min(closestPerimeterPoint, MathF.Abs(point.Vertex.y - y));
                    }

                    if (closestPerimeterPoint < bestMatchYDiff)
                    {
                        bestMatchRoom = i;
                        bestMatchYDiff = closestPerimeterPoint;
                    }
                }
            }
            return bestMatchRoom;
        }

        public void ReSave(bool overwrite = true)
        {
            if (m_FilePath == null)
                throw new Exception("Can't save PSDL file without being constructed from a file path. Use SaveAs instead.");
            if (overwrite)
                File.Delete(m_FilePath);
            SaveAs(m_FilePath);
        }



        public void SaveAs(string saveAsFile)
        {
            if (!m_ElementMapBuilt)
                RebuildElementMap();

            //initialize caches and get hash dict
            RefreshVertexCache();
            RefreshFloatCache();
            RecalculateBounds();
            var textureHashDictionary = GenerateTextureHashDictionary();

            //write file
            using (var w = new BinaryWriter(File.OpenWrite(saveAsFile)))
            {
                //header
                w.Write('P');
                w.Write('S');
                w.Write('D');
                w.Write(Version.ToString()[0]);

                w.Write((uint)2);//target size

                //Vertices
                w.Write((uint)Vertices.Count);
                foreach (var vtx in Vertices)
                {
                    w.Write(vtx.x);
                    w.Write(vtx.y);
                    w.Write(vtx.z);
                }

                //floats
                w.Write((uint)Floats.Count);
                foreach (var flt in Floats)
                {
                    w.Write(flt);
                }

                //Textures
                w.Write((uint)m_Textures.Count + 1);
                foreach (var texture in m_Textures)
                {
                    if (texture.Length > 0)
                    {
                        w.Write((byte)(texture.Length + 1)); //length incl. null terminator
                        for (var j = 0; j < texture.Length; j++)
                        {
                            w.Write(texture[j]);
                        }
                    }
                    w.Write((byte)0); //null terminator

                    //introduced in version 1
                    if (Version == 1)
                    {
                        MaterialIndices.TryGetValue(texture, out var materialIndex);
                        w.Write(materialIndex);
                    }
                }

                //rooms
                w.Write((uint)Rooms.Count + 1);
                w.Write((uint)0); //unknown stuff :/
                foreach (var room in Rooms)
                {
                    w.Write((uint)room.Perimeter.Count);

                    var attributeLengthPtr = w.BaseStream.Position;
                    w.Write((uint)0);

                    //write Perimeter
                    foreach (var pp in room.Perimeter)
                    {
                        w.Write((ushort)GetVertexIndex(pp.Vertex));

                        if (pp.ConnectedRoom == null)
                        {
                            w.Write((ushort)0);
                        }
                        else
                        {
                            w.Write((ushort)(Rooms.IndexOf(pp.ConnectedRoom) + 1));
                        }

                    }

                    //write elements
                    var lastTextureHash = "$$";
                    var attributeStartPtr = w.BaseStream.Position;

                    for (var j = 0; j < room.Elements.Count; j++)
                    {
                        var el = room.Elements[j];
                        var texHash = TextureHashString(el.Textures);

                        //need a new texture statement?
                        if (lastTextureHash != texHash && !(el is FacadeBoundElement))
                        {
                            if (texHash != null)
                            {
                                //generate a new texture
                                var textureIndex = textureHashDictionary[texHash] + 1 + -el.TextureIndexOffset;

                                int textureIndexByte = textureIndex % 256;
                                int textureIndexSubtype = (int)Math.Floor((float)textureIndex / 256);

                                w.Write(BuildAttributeHeader(false, 10, textureIndexSubtype));
                                w.Write((ushort)(textureIndexByte));
                            }
                            else
                            {
                                //null texture
                                w.Write(BuildAttributeHeader(false, 10, 0));
                                w.Write((ushort)(0));
                            }
                        }

                        //write the element
                        w.Write(BuildAttributeHeader((j == room.Elements.Count - 1), (int)el.Type, el.Subtype));

                        el.Save(w, this);
                    }

                    //finally, write attribute length
                    var attributeTotalSize = (uint)((w.BaseStream.Position - attributeStartPtr) / 2);

                    w.BaseStream.Seek(attributeLengthPtr, SeekOrigin.Begin);
                    w.Write(attributeTotalSize);
                    w.BaseStream.Seek(0, SeekOrigin.End);
                }

                //write padding(?) byte, and room Flags
                w.Write((byte)0);
                for (var i = 0; i < Rooms.Count; i++)
                {
                    w.Write((byte)Rooms[i].Flags);
                }

                //write padding(?) byte, and room proprules
                w.Write((byte)0xcd);
                for (var i = 0; i < Rooms.Count; i++)
                {
                    w.Write(Rooms[i].PropRule);
                }

                //write dimensions
                w.Write(DimensionsMin.x);
                w.Write(DimensionsMin.y);
                w.Write(DimensionsMin.z);

                w.Write(DimensionsMax.x);
                w.Write(DimensionsMax.y);
                w.Write(DimensionsMax.z);

                w.Write(DimensionsCenter.x);
                w.Write(DimensionsCenter.y);
                w.Write(DimensionsCenter.z);

                w.Write(DimensionsRadius);

                //verify ai roads
                var verifiedAiRoads = new List<AIRoad>();
                foreach (var aiRoad in AIRoads)
                {
                    var verified = aiRoad.StripInvalidRooms();
                    if (verified.Rooms.Count > 0)
                        verifiedAiRoads.Add(verified);
                }

                w.Write((uint)verifiedAiRoads.Count);
                for (var i = 0; i < verifiedAiRoads.Count; i++)
                {
                    var road = verifiedAiRoads[i];
                    w.Write((int)road.Flags);
                    w.Write((byte)road.Unknown3.Count);
                    w.Write((byte)road.Unknown4.Count);
                    foreach (var f in road.Unknown3)
                    {
                        w.Write(f);
                    }
                    foreach (var f in road.Unknown4)
                    {
                        w.Write(f);
                    }
                    w.Write(road.Unknown5);

                    //calculate crossroads
                    Room startRoom = road.Rooms[0];
                    Room endRoom = road.Rooms[road.Rooms.Count - 1];

                    var startCrossroads = startRoom.GetCrossroadVertices(CrossroadEnd.First);
                    var endCrossroads = endRoom.GetCrossroadVertices(CrossroadEnd.Last);
                    foreach (var vertex in startCrossroads)
                    {
                        w.Write((ushort)GetVertexIndex(vertex));
                    }
                    foreach (var vertex in endCrossroads)
                    {
                        w.Write((ushort)GetVertexIndex(vertex));
                    }


                    w.Write((byte)road.Rooms.Count);
                    foreach (var room in road.Rooms)
                    {
                        w.Write((short)(Rooms.IndexOf(room) + 1));
                    }
                }
            }
        }

        //Constructors
        public PSDLFile()
        {
            m_Textures = new List<string>();
            Floats = new List<float>();
            Vertices = new List<Vertex>();
            Rooms = new List<Room>();
            AIRoads = new List<AIRoad>();
            MaterialIndices = new Dictionary<string, byte>();
        }

        public PSDLFile(string psdlPath) : this()
        {
            m_FilePath = psdlPath;
            using (var stream = new FileStream(psdlPath, FileMode.Open))
            {
                Load(stream);
            }
        }

        public PSDLFile(Stream psdlStream) : this()
        {
            Load(psdlStream);
        }
    }
}