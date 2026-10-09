using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static PathSet;

namespace MM2.AI
{
    public class RoadEnd
    {
        public int IntersectionID;
        public VehicleRule VehRule;
        public int IntersectionRoadIndex;
        public Vector3 TrafficLightOrigin;
        public Vector3 TrafficLightOrientation;

        public void WriteBinary(BinaryWriter writer)
        {
            if(IntersectionID < 0)
            {
                writer.Write(0xCDCDCDCD);
            }
            else
            {
                writer.Write(IntersectionID);
            }

            writer.Write((ushort)0xcdcd);
            writer.Write((int)VehRule);
            writer.Write(IntersectionRoadIndex);
            writer.WriteVector3Flipped(TrafficLightOrigin);
            writer.WriteVector3Flipped(TrafficLightOrientation);
        }

        public static RoadEnd ReadBinary(BinaryReader reader)
        {
            var end = new RoadEnd();

            // read data
            end.IntersectionID = reader.ReadInt32();
            if(end.IntersectionID == unchecked((int)0xCDCDCDCD))
            {
                end.IntersectionID = -1;
            }

            reader.BaseStream.Seek(2, SeekOrigin.Current); // unknown value
            end.VehRule = (VehicleRule)reader.ReadInt32();
            end.IntersectionRoadIndex = reader.ReadInt32();
            end.TrafficLightOrigin = reader.ReadVector3Flipped();
            end.TrafficLightOrientation = reader.ReadVector3Flipped();

            return end;
        }
    }

    public class RoadData
    {
        public int numLanes;
        public int numTrams;
        public int numTrains;
        public bool hasSidewalk;

        // / <summary>
        // / This appears to be
        // / NUMLANES count: Center distances
        // / SIDEWALK count: Sidewalk center distances
        // / ONE COUNT: halfWidth repeated, but negative?
        // / NUMLANES count * 2: Center distances, repeated twice for each lane?
        // / ONE COUNT: halfWidth repeated?
        // / </summary>
        public float[] UnknownFloats;

        public float[] UnknownFloats2;
        public float[,] LaneDistances;
        public float[] OuterEdgeDistances;

        public List<HermiteCurve> VehicleCurves;
        public List<HermiteCurve> TramCurves;
        public List<HermiteCurve> TrainCurves;

        public Vector3[] SidewalkVertices;
        public Vector3[] SidewalkInnerVertices;
        public Vector3[] SidewalkOuterVertices;
        public AmbientTypeFlags AiTypeFlags;

        public HermiteCurve GetCurve(RailType type, int rail)
        {
            switch(type)
            {
                case RailType.Vehicle: return VehicleCurves[rail];
                case RailType.Tram: return TramCurves[rail];
                case RailType.Subway: return TrainCurves[rail];
            }
            return null;
        }

        public Vector3 GetVertex(RailType type, int lane, int sectionIndex)
        {
            switch (type)
            {
                case RailType.Pedestrian:
                    return GetSidewalkVertex(sectionIndex);
                case RailType.Subway:
                    return GetTrainVertex(lane, sectionIndex);
                case RailType.Vehicle:
                    return GetVehicleVertex(lane, sectionIndex);
                case RailType.Tram:
                    return GetTramVertex(lane, sectionIndex);
            }
            return Vector3.zero;
        }

        public int GetTotalRailCount()
        {
            return (hasSidewalk ? 1 : 0) + numTrains + numTrams + numLanes;
        }

        public int GetRailCount(RailType type)
        {
            switch (type)
            {
                case RailType.Pedestrian:
                    return hasSidewalk ? 1 : 0;
                case RailType.Subway:
                    return numTrains;
                case RailType.Tram:
                    return numTrams;
                case RailType.Vehicle:
                    return numLanes;
            }
            return 0;
        }

        public Vector3 GetVehicleVertex(int lane, int sectionIndex)
        {
            return VehicleCurves[lane].Points[sectionIndex].Position;
        }

        public void SetVehicleVertex(int lane, int sectionIndex, Vector3 vertex)
        {
            var pt = VehicleCurves[lane].Points[sectionIndex];
            pt.Position = vertex;
            VehicleCurves[lane].Points[sectionIndex] = pt;
        }

        public Vector3 GetTramVertex(int lane, int sectionIndex)
        {
            return TramCurves[lane].Points[sectionIndex].Position;
        }

        public void SetTramVertex(int lane, int sectionIndex, Vector3 vertex)
        {
            var pt = TramCurves[lane].Points[sectionIndex];
            pt.Position = vertex;
            TramCurves[lane].Points[sectionIndex] = pt;
        }

        public Vector3 GetTrainVertex(int lane, int sectionIndex)
        {
            return TrainCurves[lane].Points[sectionIndex].Position;
        }

        public void SetTrainVertex(int lane, int sectionIndex, Vector3 vertex)
        {
            var pt = TrainCurves[lane].Points[sectionIndex];
            pt.Position = vertex;
            TrainCurves[lane].Points[sectionIndex] = pt;
        }

        public Vector3 GetSidewalkVertex(int sectionIndex)
        {
            return SidewalkVertices[sectionIndex];
        }

        public void SetSidewalkVertex(int sectionIndex, Vector3 vertex)
        {
            SidewalkVertices[sectionIndex] = vertex;
        }

        /*public bool IsSidewalkReversed(Road parentRoad)
        {
            var ainet = SDLCity.Instance.AINetwork;
            var endIntersectionA = ainet.Intersections[parentRoad.RightEndData.IntersectionID];
            var endIntersectionB = ainet.Intersections[parentRoad.LeftEndData.IntersectionID];

            if(this == parentRoad.RightData)
            {
                float distToEnd = (endIntersectionA.Center - SidewalkVertices[0]).sqrMagnitude;
                float distToStart= (endIntersectionB.Center - SidewalkVertices[0]).sqrMagnitude;
                return distToEnd < distToStart;
            }
            else
            {
                float distToEnd = (endIntersectionB.Center - SidewalkVertices[0]).sqrMagnitude;
                float distToStart= (endIntersectionA.Center - SidewalkVertices[0]).sqrMagnitude;
                return distToEnd < distToStart;
            }
        }*/

        public void WriteBinary(BinaryWriter writer, int numSections)
        {
            writer.Write((ushort)numLanes);
            writer.Write((ushort)numTrams);
            writer.Write((ushort)numTrains);
            writer.Write((ushort)(hasSidewalk ? 1 : 0));

            // reverse to do bitflags
            ushort typeFlags = (ushort)AiTypeFlags;
            typeFlags = (ushort)(3 - typeFlags);

            writer.Write(typeFlags);

            // the fun part
            for (int i = 0; i < numLanes; i++)
            {
                for (int j = 0; j < numSections; j++)
                {
                    writer.Write(LaneDistances[i, j]);
                }
            }

            for (int i = 0; i < OuterEdgeDistances.Length; i++)
                writer.Write(OuterEdgeDistances[i]);

            // unknown
            for (int i = 0; i < UnknownFloats.Length; i++)
                writer.Write(UnknownFloats[i]);

            // unknown
            for (int i = 0; i < UnknownFloats2.Length; i++)
                writer.Write(UnknownFloats2[i]);

            // write vehicle verts
            for (int i = 0; i < numLanes; i++)
            {
                for (int j = 0; j < numSections; j++)
                    writer.WriteVector3Flipped(VehicleCurves[i].Points[j].Position);
            }

            // write sidewalk center verts
            for (int i = 0; i < numSections; i++)
                writer.WriteVector3Flipped(SidewalkVertices[i]);

            // write tram vertices
            for (int i = 0; i < numTrams; i++)
            {
                for (int j = 0; j < numSections; j++)
                    writer.WriteVector3Flipped(TramCurves[i].Points[j].Position);
            }

            // write train vertices
            for (int i = 0; i < numTrains; i++)
            {
                for (int j = 0; j < numSections; j++)
                    writer.WriteVector3Flipped(TrainCurves[i].Points[j].Position);
            }

            // write sidewalk inner verts
            for (int i = 0; i < numSections; i++)
                writer.WriteVector3Flipped(SidewalkInnerVertices[i]);

            // write sidewalk outer verts
            for (int i = 0; i < numSections; i++)
                writer.WriteVector3Flipped(SidewalkOuterVertices[i]);
        }

        public static RoadData ReadBinary(BinaryReader reader, Road parent)
        {
            var data = new RoadData();

            // read header
            data.numLanes = reader.ReadUInt16();
            data.numTrams = reader.ReadUInt16();
            data.numTrains = reader.ReadUInt16();
            data.hasSidewalk = (reader.ReadUInt16() > 0);

            // swap 3 and 0 to do bitflags
            int ambientType = reader.ReadUInt16();
            ambientType = 3 - ambientType;

            // init data
            data.UnknownFloats = new float[10];
            data.UnknownFloats2 = new float[data.numLanes];
            data.LaneDistances = new float[data.numLanes, parent.NumSections];
            data.OuterEdgeDistances = new float[parent.NumSections + (data.hasSidewalk ? 1 : 0)];
            data.VehicleCurves = new List<HermiteCurve>();
            data.TramCurves = new List<HermiteCurve>();
            data.TrainCurves = new List<HermiteCurve>();
            data.SidewalkVertices = new Vector3[parent.NumSections];
            data.SidewalkInnerVertices = new Vector3[parent.NumSections];
            data.SidewalkOuterVertices = new Vector3[parent.NumSections];
            data.AiTypeFlags = (AmbientTypeFlags)ambientType;

            // read data
            // read lane distances
            for (int i = 0; i < data.numLanes; i++)
            {
                for (int j = 0; j < parent.NumSections; j++)
                    data.LaneDistances[i, j] = reader.ReadSingle();
            }

            // read outer edge distances
            for (int i = 0; i < data.OuterEdgeDistances.Length; i++)
                data.OuterEdgeDistances[i] = reader.ReadSingle();

            // read unk data
            for (int i = 0; i < data.UnknownFloats.Length; i++)
            {
                data.UnknownFloats[i] = reader.ReadSingle();
            }

            // unknown
            for (int i = 0; i < data.numLanes; i++)
            {
                data.UnknownFloats2[i] = reader.ReadSingle();
            }

            // read lane vertices
            for (int i = 0; i < data.numLanes; i++)
            {
                List<HermitePoint> lanePoints = new List<HermitePoint>();
                for (int j = 0; j < parent.NumSections; j++)
                    lanePoints.Add(new HermitePoint() { Position = reader.ReadVector3Flipped() });
                data.VehicleCurves.Add(new HermiteCurve(lanePoints));
            }

            // read sidewalk center verts
            for (int i = 0; i < parent.NumSections; i++)
                data.SidewalkVertices[i] = reader.ReadVector3Flipped();

            // read tram vertices
            for (int i = 0; i < data.numTrams; i++)
            {
                List<HermitePoint> lanePoints = new List<HermitePoint>();
                for (int j = 0; j < parent.NumSections; j++)
                    lanePoints.Add(new HermitePoint() { Position = reader.ReadVector3Flipped() });
                data.TramCurves.Add(new HermiteCurve(lanePoints));
            }

            // read train vertices
            for (int i = 0; i < data.numTrains; i++)
            {
                List<HermitePoint> lanePoints = new List<HermitePoint>();
                for (int j = 0; j < parent.NumSections; j++)
                    lanePoints.Add(new HermitePoint() { Position = reader.ReadVector3Flipped() });
                data.TrainCurves.Add(new HermiteCurve(lanePoints));
            }

            // read sidewalk inner verts
            for (int i = 0; i < parent.NumSections; i++)
                data.SidewalkInnerVertices[i] = reader.ReadVector3Flipped();

            // read sidewalk outer verts
            for (int i = 0; i < parent.NumSections; i++)
                data.SidewalkOuterVertices[i] = reader.ReadVector3Flipped();

            return data;
        }

        public static RoadData ReadShortcutBinary(BinaryReader reader, int numSections)
        {
            var data = new RoadData();

            //  read header
            int numLanes = reader.ReadUInt16();
            int numTrams = reader.ReadUInt16();
            int numTrains = reader.ReadUInt16();
            int numSidewalks = reader.ReadUInt16();
            reader.ReadUInt16(); // ambient type, the game overwrites this with 3 after loading

            int numRails = numLanes + numSidewalks;

            //  the game only allocates/reads ONE rail for trams/trains regardless of the count
            data.numLanes = numLanes;
            data.numTrams = numTrams > 0 ? 1 : 0;
            data.numTrains = numTrains > 0 ? 1 : 0;
            data.hasSidewalk = numSidewalks > 0;
            data.AiTypeFlags = (AmbientTypeFlags)(3 - 3); // raw 3 after the game's override

            //  init data
            data.LaneDistances = new float[numLanes, numSections];
            data.UnknownFloats = new float[10];
            data.UnknownFloats2 = new float[numLanes];
            data.OuterEdgeDistances = new float[numSections + (data.hasSidewalk ? 1 : 0)];
            data.VehicleCurves = new List<HermiteCurve>();
            data.TramCurves = new List<HermiteCurve>();
            data.TrainCurves = new List<HermiteCurve>();
            data.SidewalkVertices = new Vector3[numSections];
            data.SidewalkInnerVertices = new Vector3[numSections];
            data.SidewalkOuterVertices = new Vector3[numSections];

            //  rail distances: count * (lanes + sidewalks), lanes first
            var sidewalkDistances = new float[numSections];
            for (int rail = 0; rail < numRails; rail++)
            {
                for (int j = 0; j < numSections; j++)
                {
                    float d = reader.ReadSingle();
                    if (rail < numLanes)
                        data.LaneDistances[rail, j] = d;
                    else if (rail == numLanes)
                        sidewalkDistances[j] = d; // extra sidewalks (if any) are dropped
                }
            }

            //  per-rail outer distances
            var railOuter = new float[numRails];
            for (int i = 0; i < numRails; i++)
                railOuter[i] = reader.ReadSingle();

            //  unknown 40 byte block (lUnkDat / rUnk2)
            for (int i = 0; i < data.UnknownFloats.Length; i++)
                data.UnknownFloats[i] = reader.ReadSingle();

            //  map shortcut-only arrays onto the regular format's arrays so WriteBinary stays valid
            //  NOTE: this mapping is a best guess
            for (int i = 0; i < numLanes; i++)
                data.UnknownFloats2[i] = railOuter[i];
            if (data.hasSidewalk)
            {
                for (int j = 0; j < numSections; j++)
                    data.OuterEdgeDistances[j] = sidewalkDistances[j];
                data.OuterEdgeDistances[numSections] = railOuter[numLanes];
            }

            //  rail vertices: count * (lanes + sidewalks), lanes then sidewalk centers
            for (int rail = 0; rail < numRails; rail++)
            {
                var lanePoints = rail < numLanes ? new List<HermitePoint>(numSections) : null;
                for (int j = 0; j < numSections; j++)
                {
                    var v = reader.ReadVector3Flipped();
                    if (lanePoints != null)
                        lanePoints.Add(new HermitePoint() { Position = v });
                    else if (rail == numLanes)
                        data.SidewalkVertices[j] = v;
                }
                if (lanePoints != null)
                    data.VehicleCurves.Add(new HermiteCurve(lanePoints));
            }

            //  tram / train: a single rail each, only present if the count is nonzero
            if (data.numTrams > 0)
                data.TramCurves.Add(ReadShortcutCurve(reader, numSections));
            if (data.numTrains > 0)
                data.TrainCurves.Add(ReadShortcutCurve(reader, numSections));

            //  sidewalk boundary verts
            for (int i = 0; i < numSections; i++)
                data.SidewalkInnerVertices[i] = reader.ReadVector3Flipped();
            for (int i = 0; i < numSections; i++)
                data.SidewalkOuterVertices[i] = reader.ReadVector3Flipped();

            return data;
        }

        private static HermiteCurve ReadShortcutCurve(BinaryReader reader, int numSections)
        {
            var points = new List<HermitePoint>(numSections);
            for (int j = 0; j < numSections; j++)
                points.Add(new HermitePoint() { Position = reader.ReadVector3Flipped() });
            return new HermiteCurve(points);
        }

        public void CopyTangents(Road road, bool mirror)
        {
            void CopyTo(IEnumerable<HermiteCurve> curves)
            {
                foreach (var curve in curves)
                {
                    for (int i = 0; i < road.SectionCenterCurve.Points.Count; i++)
                    {
                        int index = mirror
                            ? road.SectionCenterCurve.Points.Count - 1 - i
                            : i;

                        var point = curve.Points[i];
                        point.Tangent = road.SectionCenterCurve.Points[index].Tangent;

                        if (mirror)
                            point.Tangent *= -1;

                        curve.Points[i] = point;
                    }
                }
            }

            CopyTo(VehicleCurves);
            CopyTo(TramCurves);
            CopyTo(TrainCurves);
        }
    }

    public class Road
    {
        const float SharpTurnAngle = 0.7f;
        const float ShortSegment = 10f;

        private int numSections;
        public int NumSections
        {
            get { return numSections; }
        }

        public float Length =>this.NumSections > 0 ? SectionCenterDistances[numSections - 1] : 0f;

        public int TotalLanes => (LeftData.numLanes + RightData.numLanes);
        public int HighestLaneCount => Mathf.Max(LeftData.numLanes, RightData.numLanes);
        public int LowestLaneCount => Mathf.Min(LeftData.numLanes, RightData.numLanes);

        public int Id;
        public PathFlags Flags;
        public int[] RoomRefs;
        public float HalfWidth;
        public float SpeedLimit;

        public RoadData LeftData;
        public RoadData RightData;

        public float[] SectionCenterDistances;
        public HermiteCurve SectionCenterCurve;
        public Vector3[] SectionXOrientations;
        public Vector3[] SectionYOrientations;
        public Vector3[] SectionZOrientations;

        public RoadEnd RightEndData;
        public RoadEnd LeftEndData;

        public List<RoadTurn> RoadTurns { get; private set; } = new List<RoadTurn>();

        public float SharpTurnDir(int turn, RoadSide side)
        {
            return side != 0 ? RoadTurns[turn].Dir : -RoadTurns[turn].Dir;
        }

        /// <summary>
        /// Distance along the road at a side-local section index. SectionCenterDistances is stored in
        /// right-side order; left side data runs the other way (see GetSidedInfoAtPoint), so mirror it.
        /// </summary>
        public float GetSectionDistance(RoadSide side, int sectionIndex)
        {
            if (side == RoadSide.Left)
                return Length - SectionCenterDistances[numSections - 1 - sectionIndex];
            return SectionCenterDistances[sectionIndex];
        }

        public Vector3 GetEndVertex()
        {
            return SectionCenterCurve.Points[numSections - 1].Position;
        }

        public Vector3 GetStartVertex()
        {
            return SectionCenterCurve.Points[0].Position;
        }

        public void GetInfoAtPoint(Vector3 point, out int railIndex, out int sectionIndex, out RoadSide side)
        {
            var testPointF = new Vector2(point.x, point.z);

            // defaults
            railIndex = -1;
            sectionIndex = -1;
            side = RoadSide.Invalid;

            for (int i = NumSections - 1; i >= 0; i--)
            {
                var sectionCtr = new Vector2(SectionCenterCurve.Points[i].Position.x, SectionCenterCurve.Points[i].Position.z);
                Vector2 dirToPoint = new Vector2(testPointF.x - sectionCtr.x, testPointF.y - sectionCtr.y);

                var sectionForward = SectionZOrientations[i].ToVec2XZ();
                var sectionXOri = SectionXOrientations[i].ToVec2XZ();
                var sectionZOri = SectionZOrientations[i].ToVec2XZ();

                // within front/back?
                float forwardDot = -Vector2.Dot(dirToPoint, sectionZOri);
                if (forwardDot >= 0)
                {
                    // last section, and we're ahead. not on this road
                    if (i == numSections - 1)
                        break;

                    // check if we're within left/right boundaries
                    float sideDot = Vector2.Dot(dirToPoint, sectionXOri);
                    float sideDotAbs = Mathf.Abs(sideDot);

                    if (sideDot >= -HalfWidth && sideDot <= HalfWidth)
                    {
                        // set section
                        sectionIndex = i;

                        // set side
                        if (IsOnewayRoad())
                        {
                            if (RightData.numLanes > 0)
                            {
                                side = RoadSide.Right;
                            }
                            else if(LeftData.numLanes > 0)
                            {
                                side = RoadSide.Left;
                            }
                        }
                        else
                        {
                            side = (sideDot > 0) ? RoadSide.Left : RoadSide.Right;
                        }


                        // find lane
                        var data = side > 0 ? RightData : LeftData;
                        for (int j = 0; j < data.numLanes ; j++)
                        {
                            int floatIdx = (data.numLanes + 1) + (j * 2);
                            // int floatIdx = 1 + (j * 2);
                            if (floatIdx >= data.UnknownFloats.Length)
                                break;

                            if (IsOnewayRoad() && false)
                            {
                                if ((sideDotAbs + HalfWidth) < data.UnknownFloats[floatIdx])
                                {
                                    railIndex = j;
                                    break;
                                }
                            }
                            else
                            {
                                if (sideDotAbs < data.UnknownFloats[floatIdx])
                                {
                                    railIndex = j;
                                    break;
                                }
                            }
                        }

                        // done
                        break; 
                    }
                }
            }
        }
        
        public void GetSidedInfoAtPoint(Vector3 point, out int railIndex, out int sectionIndex, out RoadSide side)
        {
            GetInfoAtPoint(point, out railIndex, out sectionIndex, out side);
            if(side == 0)
                sectionIndex = numSections - sectionIndex - 2;
        }

        // / <summary>
        // / Find out what side of the road an arbitrary point is at
        // / (will return 0 if this point is not within the road!)
        // / </summary>
        // / <returns>The point road side.</returns>
        // / <param name="point">Point.</param>
        public RoadSide GetPointRoadSide(Vector3 point)
        {
            GetInfoAtPoint(point, out int railIndex, out int sectionIndex, out var side);
            return side;
        }

        // / <summary>
        // / Find out what section this point is in
        // / </summary>
        // / <returns>The point section index.</returns>
        // / <param name="point">Point.</param>
        public int GetPointSectionIndex(Vector3 point)
        {
            GetInfoAtPoint(point, out int railIndex, out int sectionIndex, out var side);
            return sectionIndex;
        }

        // / <summary>
        // / Find out what section this point is in
        // / </summary>
        // / <returns>The point section index.</returns>
        // / <param name="point">Point.</param>
        public int GetPointSectionIndexSided(Vector3 point)
        {
            GetSidedInfoAtPoint(point, out int railIndex, out int sectionIndex, out var side);
            return sectionIndex;
        }

        public bool IsPointOnRoad(Vector3 point)
        {
            return GetPointSectionIndex(point) >= 0;
        }

        public RoadPosition IsPosOnRoad(Vector3 pos, float margin, out float lateral)
        {
            lateral = 0f;

            //  the exe reads uninitialized locals in this case; bail instead
            if (numSections <= 0)
                return RoadPosition.OffRoad;

            //  lateral offset table: [2n] = curb (outer edge of last lane), [2n+1] = sidewalk outer edge
            var table = RightData.UnknownFloats;
            int n = RightData.numLanes;
            float roadEdge = table[Mathf.Min(2 * n, table.Length - 1)] - margin;
            float sidewalkEdge = table[Mathf.Min(2 * n + 1, table.Length - 1)] - margin;

            //  NOTE: the exe never indexes SectionOriY, so it always uses section 0's up vector
            Vector3 up = SectionYOrientations[0];

            //  walk sections from the start; stop at the first one whose cross-plane the point is in front of.
            //  falls through to the last section if none match (LABEL_9 in the decomp)
            int section = numSections - 1;
            for (int i = 0; i < numSections; i++)
            {
                Vector3 o = SectionCenterCurve.Points[i].Position;

                //  direction from section center to right sidewalk outer vertex, crossed with up = along-road axis.
                //  exe normalizes toEdge first, but only the sign of the dot is used, so it's skipped here
                Vector3 toEdge = RightData.SidewalkOuterVertices[i] - o;
                Vector3 along = Vector3.Cross(toEdge, up);

                float dx = pos.x - o.x;
                float dz = pos.z - o.z;

                //  negated because the import mirrors one axis, which flips the sign of a triple product
                if (-(dx * along.x + dz * along.z) > 0f)
                {
                    section = i;
                    break;
                }
            }

            {
                Vector3 o = SectionCenterCurve.Points[section].Position;
                Vector3 ox = SectionXOrientations[section];
                float dx = pos.x - o.x;
                float dz = pos.z - o.z;
                lateral = -(dz * ox.z + dx * ox.x);
            }

            if (lateral < roadEdge && lateral > -roadEdge)
                return RoadPosition.OnRoad;
            if (lateral < sidewalkEdge && lateral > -sidewalkEdge)
                return RoadPosition.OnSidewalk;
            return RoadPosition.OffRoad;
        }

        public Vector3 GetCenter()
        {
            // uneven sections are already perfect
            if (numSections % 2 > 0)
            {
                return SectionCenterCurve.Points[Mathf.CeilToInt((float)numSections / 2f)].Position;
            }

            // else, interpolate
            if (numSections == 2)
                return Vector3.Lerp(SectionCenterCurve.Points[0].Position, SectionCenterCurve.Points[1].Position, 0.5f);
            if (numSections == 1)
                return SectionCenterCurve.Points[0].Position;

            Vector3 section1 = SectionCenterCurve.Points[numSections / 2].Position;
            Vector3 section2 = SectionCenterCurve.Points[(numSections / 2) + 1].Position;
            return Vector3.Lerp(section1, section2, 0.5f);
        }

        public void CalculateHalfWidth()
        {
            float width = 0f;

            // loop through sections and find the largest width
            for(int i=0; i < numSections; i++)
            {
                var sectionCtr =  SectionCenterCurve.Points[i].Position;
                var sectionCtrF = new Vector2(sectionCtr.x, sectionCtr.z);

                var sectionXO = SectionXOrientations[i];
                var sectionXOF = new Vector2(sectionXO.x, sectionXO.z);

                var testPoint = LeftData.SidewalkOuterVertices[i];
                var testPointF = new Vector2(testPoint.x, testPoint.z);

                float xSum = testPointF.x - sectionCtrF.x;
                float zSum = testPointF.y - sectionCtrF.y;
                Vector2 xzSum = new Vector2(xSum, zSum);

                float xzDot = Mathf.Abs(Vector2.Dot(xzSum, sectionXOF));

                if(xzDot > width)
                {
                    width = xzDot;
                }

            }

            this.HalfWidth = width;
        }

   
        public float CalculateLaneOuterBoundaryDist(int side, int lane)
        {
            float width = 0f;
            lane++; // add one since this is outer boundary

            // loop through sections and find the largest width
            for (int i = 0; i < numSections; i++)
            {
                var sectionCtr = SectionCenterCurve.Points[i].Position;
                var sectionCtrF = new Vector2(sectionCtr.x, sectionCtr.z);

                var sectionXO = SectionXOrientations[i];
                var sectionXOF = new Vector2(sectionXO.x, sectionXO.z);

                Vector3 testPoint;
                if(side == 1 && lane == RightData.numLanes)
                {
                    testPoint = RightData.GetSidewalkVertex(i);
                }else if(side == 0 && lane == LeftData.numLanes)
                {
                    testPoint = LeftData.GetSidewalkVertex(i);
                }
                else
                {
                    testPoint = (side == 1) ? RightData.GetVehicleVertex(lane, i) : LeftData.GetVehicleVertex(lane, i);
                }
                var testPointF = new Vector2(testPoint.x, testPoint.z);

                float xSum = testPointF.x - sectionCtrF.x;
                float zSum = testPointF.y - sectionCtrF.y;
                Vector2 xzSum = new Vector2(xSum, zSum);

                float xzDot = Mathf.Abs(Vector2.Dot(xzSum, sectionXOF));

                if (xzDot > width)
                {
                    width = xzDot;
                }

            }

            return width;
        }

        public float CenterLength(int a, int b)
        {
            return Mathf.Abs(SectionCenterDistances[b] - SectionCenterDistances[a]);
        }

        public bool IsOnewayRoad()
        {
            int ll = LeftData.numLanes;
            int rl = RightData.numLanes;

            // can't determine
            if (ll == 0 && rl == 0) // both zero
            {
                return false;
            }else if(ll == 0 || rl == 0) // one side has no lanes
            {
                return true;
            }
            else // both sides have lanes
            {
                return false;
            }
        }

        public void ReverseDirection()
        {
            //  Original only does anything if the left side has lanes
            if (LeftData.numLanes == 0)
                return;

            var newRightCurves = BuildReversedLanes(LeftData);
            var newLeftCurves = BuildReversedLanes(RightData);

            RightData.VehicleCurves = newRightCurves;
            LeftData.VehicleCurves = newLeftCurves;

            //  In the game this is implicit via shared buffer sizing; here we must keep counts consistent
            (LeftData.numLanes, RightData.numLanes) = (RightData.numLanes, LeftData.numLanes);

            //  Per-lane arrays whose length is tied to numLanes must follow the lanes,
            //  otherwise WriteBinary will produce a file ReadBinary can't parse
            (LeftData.UnknownFloats2, RightData.UnknownFloats2) = (RightData.UnknownFloats2, LeftData.UnknownFloats2);

            RecalculateLaneDistances(LeftData);
            RecalculateLaneDistances(RightData);

            //  Tangents were copied from the centerline at load; redo it for the new curves
            LeftData.CopyTangents(this, true);
            RightData.CopyTangents(this, false);
        }

        private List<HermiteCurve> BuildReversedLanes(RoadData src)
        {
            var result = new List<HermiteCurve>(src.numLanes);
            for (int dstLane = 0; dstLane < src.numLanes; dstLane++)
            {
                int srcLane = src.numLanes - 1 - dstLane;
                var srcPoints = src.VehicleCurves[srcLane].Points;

                var points = new List<HermitePoint>(numSections);
                for (int j = 0; j < numSections; j++)
                    points.Add(new HermitePoint { Position = srcPoints[numSections - 1 - j].Position });

                result.Add(new HermiteCurve(points));
            }
            return result;
        }

        private void RecalculateLaneDistances(RoadData data)
        {
            data.LaneDistances = new float[data.numLanes, numSections];
            for (int lane = 0; lane < data.numLanes; lane++)
            {
                var pts = data.VehicleCurves[lane].Points;
                float dist = 0f;
                data.LaneDistances[lane, 0] = 0f;
                for (int j = 1; j < numSections; j++)
                {
                    dist += Vector3.Distance(pts[j].Position, pts[j - 1].Position); //  full 3D, same as the decomp
                    data.LaneDistances[lane, j] = dist;
                }
            }
        }

        public int RoadVertice(Vector3 pos, int side)
        {
            int best = numSections;
            float bestLateral = 9999f;
            float prevLength = 0f;

            for (int v = 0; v < numSections; v++)
            {
                Vector3 o = SectionCenterCurve.Points[v].Position;
                float dx = pos.x - o.x;
                float dz = pos.z - o.z;

                Vector3 oz = SectionZOrientations[v];
                float along = dz * oz.z + dx * oz.x;

                //  skip sections the point is already clear of
                bool past = v != 0 && CenterLength(0, v) - prevLength + 2f <= along;

                if (along > -0.1f && !past)
                {
                    Vector3 ox = SectionXOrientations[v];
                    float lateral = Mathf.Abs(dz * ox.z + dx * ox.x);
                    if (lateral < bestLateral)
                    {
                        bestLateral = lateral;
                        best = v;
                        if (lateral < HalfWidth + 4f) break;   //  close enough, stop looking
                    }
                }

                if (v != 0) prevLength = CenterLength(0, v - 1);
            }

            //  nothing within the road's width: accept the last vertex if we're beyond it
            if (HalfWidth + 4f < bestLateral)
            {
                int last = numSections - 1;
                Vector3 o = SectionCenterCurve.Points[last].Position;
                float dx = pos.x - o.x;
                float dz = pos.z - o.z;
                Vector3 oz = SectionZOrientations[last];
                Vector3 ox = SectionXOrientations[last];

                if (dz * oz.z + dx * oz.x < 0f && Mathf.Abs(dz * ox.z + dx * ox.x) < bestLateral)
                    best = numSections;
            }

            return side == 1 ? best : numSections - best;
        }

        public int RoadVertice(Vector3 pos, int side, int startVert)
        {
            if (startVert >= numSections) return RoadVertice(pos, side);

            int raw = startVert;
            int mirrored = numSections - startVert - 1;

            while (true)
            {
                int v = side == 1 ? raw : mirrored;
                Vector3 o = SectionCenterCurve.Points[v].Position;
                float dx = pos.x - o.x;
                float dz = pos.z - o.z;
                Vector3 oz = SectionZOrientations[v];

                float along = side == 1 ? dz * oz.z + dx * oz.x : -oz.z * dz - oz.x * dx;
                if (along > 4f) return startVert;

                raw++;
                mirrored--;
                if (++startVert >= numSections) return RoadVertice(pos, side);
            }
        }

        public Vector3 Origin(int i) => SectionCenterCurve.Points[i].Position;
        public Vector3 OriX(int i) => SectionXOrientations[i];
        public Vector3 OriZ(int i) => SectionZOrientations[i];

        public bool IsOneWay()
        {
            int lc0 = LeftData.numLanes;
            int lc1 = RightData.numLanes;
            return (lc0 == 0 && lc1 != 0) || (lc1 == 0 && lc0 != 0);
        }

        Vector3 ApexFromEdges(int v, bool left)
        {
            Vector3 a = InsideEdge(v - 1, left, 1.5f);
            Vector3 da = -SectionZOrientations[v - 1];
            Vector3 b = InsideEdge(v + 1, left, 1.5f);
            Vector3 db = SectionZOrientations[v + 1];

            float t = (b.z * da.x - a.z * da.x - b.x * da.z + a.x * da.z) / (da.z * db.x - da.x * db.z);
            float y = left ? LeftData.SidewalkInnerVertices[v].y : RightData.SidewalkInnerVertices[v].y;

            return new Vector3(b.x + db.x * t, y, b.z + db.z * t);
        }

        Vector3 ApexSimple(int v, bool left) => InsideEdge(v, left, 1f);
        float LaneEdge(int index) =>
        RightData.UnknownFloats[Mathf.Clamp(index, 0, RightData.UnknownFloats.Length - 1)];
        Vector3 InsideEdge(int v, bool left, float amount) => left
        ? LeftData.SidewalkInnerVertices[v] - SectionXOrientations[v] * amount
        : RightData.SidewalkInnerVertices[v] + SectionXOrientations[v] * amount;

        public void InitRoadTurns()
        {
            RoadTurns.Clear();

            for (int v = 1; v < numSections - 1; v++)
            {
                float angle = VertexAngle(v);
                bool sharp = angle < -SharpTurnAngle || angle > SharpTurnAngle;
                bool shortSeg = CenterLength(v, v + 1) < ShortSegment && v < numSections - 2;

                if (!sharp && !shortSeg) continue;

                int start = v;

                if (!sharp || shortSeg)
                {
                    //  fold in the next vertex before deciding
                    angle += VertexAngle(v + 1);
                    v++;
                    if (angle > -SharpTurnAngle && angle < SharpTurnAngle) continue;

                    RoadTurns.Add(new RoadTurn
                    {
                        Vertex = start,
                        Angle = angle,
                        Dir = angle < 0f ? -1f : 1f,
                        Intersection = ApexFromEdges(v, angle < 0f),
                    });
                }
                else
                {
                    RoadTurns.Add(new RoadTurn
                    {
                        Vertex = start,
                        Angle = angle,
                        Dir = angle < 0f ? -1f : 1f,
                        Intersection = ApexSimple(v, angle < 0f),
                    });
                }
            }
        }

        public void CalcRoadTurns(Vector3 from, int side)
        {
            if (RoadTurns.Count == 0) return;

            float laneEdge = LaneEdge(2 * RightData.numLanes);
            float maxInset = laneEdge + laneEdge - 1.5f;

            foreach (var t in RoadTurns)
            {
                int v = t.Vertex;
                float half = (3.14f - Mathf.Abs(t.Angle)) * 0.5f;

                //  how far in from the apex the car sits, along the road's lateral axis.
                //  NOTE: the exe reads the apex's Y field for the Z term here; assumed a bug.
                float dx = from.x - t.Intersection.x;
                float dz = from.z - t.Intersection.z;

                Vector3 ox = SectionXOrientations[v];
                Vector3 oz = SectionZOrientations[v];

                float lateral;
                if (side != 0)
                {
                    lateral = dz * ox.z + dx * ox.x;
                }
                else
                {
                    int off = 1;
                    if (v + 2 < numSections)
                    {
                        Vector3 seg = SectionCenterCurve.Points[v + 1].Position - SectionCenterCurve.Points[v].Position;
                        float a = Mathf.Atan2(-ox.x * seg.x - ox.z * seg.z, -oz.x * seg.x - oz.z * seg.z);
                        if (a != t.Angle) off = 2;   //  exact float compare, as in the original
                    }
                    Vector3 axis = SectionXOrientations[v + off];
                    lateral = -axis.x * dx - axis.z * dz;
                }

                float inset = Mathf.Clamp(lateral * t.Dir, 3f, maxInset);

                t.Radius = inset / (1f - Mathf.Sin(half));
                t.Setback = Mathf.Cos(half) * t.Radius;
                t.Center = t.Intersection + oz * t.Setback - ox * ((t.Radius - inset) * t.Dir);

                Vector3 start = ox * (t.Radius * t.Dir);
                start.y = 0f;
                t.StartDir = start.normalized;

                float arc = t.Angle * t.Dir;
                Vector3 end = ox * (Mathf.Cos(arc) * t.Radius * t.Dir) - oz * (Mathf.Sin(arc) * t.Radius);
                end.y = 0f;
                t.EndDir = end.normalized;
            }
        }

        public int IsSharpTurn(int vertex, int side)
        {
            int raw = side != 0 ? vertex : numSections - vertex - 1;
            for (int i = 0; i < RoadTurns.Count; i++)
                if (RoadTurns[i].Vertex == raw) return i;
            return -1;
        }

        float VertexAngle(int v)
        {
            Vector3 seg = SectionCenterCurve.Points[v + 1].Position - SectionCenterCurve.Points[v].Position;
            Vector3 ox = SectionXOrientations[v];
            Vector3 oz = SectionZOrientations[v];
            return Mathf.Atan2(-(ox.x * seg.x + ox.z * seg.z), -(oz.x * seg.x + oz.z * seg.z));
        }

        public void WriteBinary(BinaryWriter writer)
        {
            writer.Write((ushort)Id);
            writer.Write((ushort)numSections);

            writer.Write((short)Flags);

            writer.Write((ushort)RoomRefs.Length);
            for (int i = 0; i < RoomRefs.Length; i++)
                writer.Write((ushort)(RoomRefs[i]));

            writer.Write(HalfWidth);
            writer.Write(SpeedLimit);

            LeftData.WriteBinary(writer, numSections);
            RightData.WriteBinary(writer, numSections);

            // write section data
            for (int i = 0; i < numSections; i++)
                writer.Write(SectionCenterDistances[i]);
            for (int i = 0; i < numSections; i++)
                writer.WriteVector3Flipped(SectionCenterCurve.Points[i].Position);
            for (int i = 0; i < numSections; i++)
                writer.WriteVector3Flipped(SectionXOrientations[i]);
            for (int i = 0; i < numSections; i++)
                writer.WriteVector3Flipped(SectionYOrientations[i]);
            for (int i = 0; i < numSections; i++)
                writer.WriteVector3Flipped(SectionZOrientations[i]);
            for (int i = 0; i < numSections; i++)
                writer.WriteVector3Flipped(SectionCenterCurve.Points[i].Tangent);

            // write RoadEnd's
            LeftEndData.WriteBinary(writer);
            RightEndData.WriteBinary(writer);
        }

        public static Road ReadBinary(BinaryReader reader)
        {
            var road = new Road();

            // read road header
            road.Id = reader.ReadUInt16();
            road.numSections = reader.ReadUInt16();

            road.Flags = (PathFlags)reader.ReadInt16();

            road.RoomRefs = new int[reader.ReadUInt16()]; // init room list
            for (int i = 0; i < road.RoomRefs.Length; i++)
                road.RoomRefs[i] = reader.ReadUInt16();

            road.HalfWidth = reader.ReadSingle();
            road.SpeedLimit = reader.ReadSingle();

            // read road data
            road.LeftData = RoadData.ReadBinary(reader, road);
            road.RightData = RoadData.ReadBinary(reader, road);

            // read section data
            road.ReadSectionData(reader);

            // set tangents
            road.LeftData.CopyTangents(road, true);
            road.RightData.CopyTangents(road, false);

            // recalculate halfwidth if zero (some mod maps)
            if (road.HalfWidth <= Mathf.Epsilon)
                road.CalculateHalfWidth();

            // read RoadEnd's
            road.LeftEndData = RoadEnd.ReadBinary(reader);
            road.RightEndData = RoadEnd.ReadBinary(reader);

            return road;
        }

        public static Road ReadShortcut(BinaryReader reader, int id)
        {
            var road = new Road();

            // read road header
            reader.ReadUInt16(); // stored id, the game discards it and uses the passed index
            road.Id = id;
            road.numSections = reader.ReadUInt16();
            road.Flags = (PathFlags)reader.ReadInt16();

            road.RoomRefs = new int[reader.ReadUInt16()];
            for (int i = 0; i < road.RoomRefs.Length; i++)
                road.RoomRefs[i] = reader.ReadUInt16();

            road.HalfWidth = reader.ReadSingle();
            road.SpeedLimit = reader.ReadSingle();

            // read road data
            road.LeftData = RoadData.ReadShortcutBinary(reader, road.numSections);
            road.RightData = RoadData.ReadShortcutBinary(reader, road.numSections);

            // section data, same layout as the regular format
            road.ReadSectionData(reader);

            // set tangents
            road.LeftData.CopyTangents(road, true);
            road.RightData.CopyTangents(road, false);

            if (road.HalfWidth <= Mathf.Epsilon)
                road.CalculateHalfWidth();

            // road ends: the decomp splits these fields differently per side, but both are
            // 4+2+2+2+4+12+12 = 38 bytes, identical to RoadEnd's layout, so reuse it
            road.LeftEndData = RoadEnd.ReadBinary(reader);
            road.RightEndData = RoadEnd.ReadBinary(reader);

            return road;
        }

        private void ReadSectionData(BinaryReader reader)
        {
            SectionCenterCurve = new HermiteCurve();
            SectionCenterDistances = new float[numSections];
            SectionXOrientations = new Vector3[numSections];
            SectionYOrientations = new Vector3[numSections];
            SectionZOrientations = new Vector3[numSections];

            for (int i = 0; i < numSections; i++)
                SectionCenterDistances[i] = reader.ReadSingle();
            for (int i = 0; i < numSections; i++)
                SectionCenterCurve.Points.Add(new HermitePoint() { Position = reader.ReadVector3Flipped() });
            for (int i = 0; i < numSections; i++)
                SectionXOrientations[i] = reader.ReadVector3Flipped();
            for (int i = 0; i < numSections; i++)
                SectionYOrientations[i] = reader.ReadVector3Flipped();
            for (int i = 0; i < numSections; i++)
                SectionZOrientations[i] = reader.ReadVector3Flipped();
            for (int i = 0; i < numSections; i++)
            {
                var point = SectionCenterCurve.Points[i];
                point.Tangent = reader.ReadVector3Flipped();
                SectionCenterCurve.Points[i] = point;
            }
        }

        public void DrawGizmos()
        {
            DrawRoadDataGizmos(RightData, Color.cyan);
            DrawRoadDataGizmos(LeftData, Color.red);
            DrawSectionGizmos();
        }

        private void DrawSectionGizmos(float axisLength = 2f, float pointRadius = 0.2f)
        {
            if (SectionCenterCurve == null)
                return;

            var prevColor = Gizmos.color;
            int count = Mathf.Min(numSections, SectionCenterCurve.Points.Count);

            for (int i = 0; i < count; i++)
            {
                Vector3 origin = SectionCenterCurve.Points[i].Position;

                // center point
                Gizmos.color = Color.white;
                Gizmos.DrawSphere(origin, pointRadius);

                // X axis
                if (SectionXOrientations != null && i < SectionXOrientations.Length)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(origin, origin + SectionXOrientations[i].normalized * axisLength);
                }

                // Y axis
                if (SectionYOrientations != null && i < SectionYOrientations.Length)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(origin, origin + SectionYOrientations[i].normalized * axisLength);
                }

                // Z axis
                if (SectionZOrientations != null && i < SectionZOrientations.Length)
                {
                    Gizmos.color = Color.blue;
                    Gizmos.DrawLine(origin, origin + SectionZOrientations[i].normalized * axisLength);
                }
            }

            Gizmos.color = prevColor;
        }

        private void DrawRoadDataGizmos(RoadData data, Color vehicleColor)
        {
            if (data == null)
                return;

            var prevColor = Gizmos.color;

            Gizmos.color = vehicleColor;
            DrawCurveGizmos(data.VehicleCurves);

            Gizmos.color = Color.magenta;
            DrawCurveGizmos(data.TrainCurves);

            Gizmos.color = Color.blue;
            DrawCurveGizmos(data.TramCurves);

            Gizmos.color = prevColor;

#if UNITY_EDITOR
            // sidewalk overlay (Gizmos can't fill polygons, so use Handles)
            var inner = data.SidewalkInnerVertices;
            var outer = data.SidewalkOuterVertices;
            if (data.hasSidewalk && inner != null && outer != null)
            {
                var prevHandlesColor = UnityEditor.Handles.color;
                var prevHandlesMatrix = UnityEditor.Handles.matrix;
                UnityEditor.Handles.matrix = Gizmos.matrix;
                UnityEditor.Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.35f);

                int count = Mathf.Min(numSections, inner.Length, outer.Length);
                var quad = new Vector3[4];
                for (int i = 0; i < count - 1; i++)
                {
                    quad[0] = inner[i];
                    quad[1] = outer[i];
                    quad[2] = outer[i + 1];
                    quad[3] = inner[i + 1];
                    UnityEditor.Handles.DrawAAConvexPolygon(quad);
                }

                UnityEditor.Handles.color = prevHandlesColor;
                UnityEditor.Handles.matrix = prevHandlesMatrix;
            }
#endif
        }

        private static void DrawCurveGizmos(List<HermiteCurve> curves)
        {
            if (curves == null)
                return;

            foreach (var curve in curves)
            {
                var pts = curve.Points;
                for (int i = 0; i < pts.Count - 1; i++)
                    Gizmos.DrawLine(pts[i].Position, pts[i + 1].Position);
            }
        }
    }
}
