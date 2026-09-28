using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MM2.AI
{
    public class ShortcutIntersection
    {
        public int Id;
        public ushort Unk1;
        public ushort Unk2;
        public ushort Unk3;
        public int Unk4;

        public Vector3 Vec1;
        public Vector3 Vec2;

        public void WriteBinary(BinaryWriter writer)
        {
            writer.Write(Id);
            writer.Write(Unk1);
            writer.Write(Unk2);
            writer.Write(Unk3);
            writer.Write(Unk4);

            writer.WriteVector3Flipped(Vec1);
            writer.WriteVector3Flipped(Vec2);
        }

        public static ShortcutIntersection ReadBinary(BinaryReader reader)
        {
            var inter = new ShortcutIntersection();

            //read
            inter.Id = reader.ReadInt32();
            inter.Unk1 = reader.ReadUInt16();
            inter.Unk2 = reader.ReadUInt16();
            inter.Unk3 = reader.ReadUInt16();
            inter.Unk4 = reader.ReadInt32();

            inter.Vec1 = reader.ReadVector3Flipped();
            inter.Vec2 = reader.ReadVector3Flipped();

            return inter;
        }
    }

    public class Intersection
    {
        public int Id;
        public int Room;
        public Vector3 Center;

        public readonly List<Road> Roads = new List<Road>();
        public readonly List<Road> Shortcuts = new List<Road>();
        public List<List<float>> RoadAngles = new List<List<float>>();
        public float EstimatedRadius = 0f;

        public void ComputeAnglesAndRadius()
        {
            //clear existing 
            RoadAngles.Clear();
            
            //calculate road angles
            for(int i=0; i < Roads.Count; i++)
            {
                //init turn list
                var turnList = new List<float>();
                RoadAngles.Add(turnList);

                //
                var srcRoad = Roads[i];
                int mySectionCount = srcRoad.NumSections;
                bool isMyFirstSectionClosest = Vector3.Distance(srcRoad.SectionCenterCurve.Points[0].Position, Center)
                                              < Vector3.Distance(srcRoad.SectionCenterCurve.Points[mySectionCount - 1].Position, Center);
                Vector3 myRoadEnd = isMyFirstSectionClosest ? srcRoad.SectionCenterCurve.Points[0].Position
                                    : srcRoad.SectionCenterCurve.Points[mySectionCount - 1].Position;
                Vector3 myRoadEndDir = isMyFirstSectionClosest ? (myRoadEnd - srcRoad.SectionCenterCurve.Points[1].Position).normalized
                                                               : (myRoadEnd - srcRoad.SectionCenterCurve.Points[mySectionCount - 2].Position).normalized;

                for (int j=0; j < Roads.Count; j++)
                {
                    //this won't happen anyways, turn angle from our road to our road
                    if(j == i)
                    {
                        turnList.Add(180f);
                        continue;
                    }

                    var otherRoad = Roads[j];
                    int otherSectionCount = otherRoad.NumSections;
                    bool isOtherFirstSectionClosest = Vector3.Distance(otherRoad.SectionCenterCurve.Points[0].Position, Center)
                                                    < Vector3.Distance(otherRoad.SectionCenterCurve.Points[otherSectionCount - 1].Position, Center);
                    Vector3 otherRoadEnd = isOtherFirstSectionClosest ? otherRoad.SectionCenterCurve.Points[0].Position
                                                                      : otherRoad.SectionCenterCurve.Points[otherSectionCount - 1].Position;
                    Vector3 otherRoadEndDir = isOtherFirstSectionClosest ? (otherRoadEnd - otherRoad.SectionCenterCurve.Points[1].Position).normalized
                                                                   : (otherRoadEnd - otherRoad.SectionCenterCurve.Points[otherSectionCount - 2].Position).normalized;

                    float ang = Vector3.SignedAngle(myRoadEndDir, -otherRoadEndDir, Vector3.up);
                    turnList.Add(ang);
                }
            }

            //get "radius"
            float estimatedSqrRadius = 0f;
            for (int i = 0; i < Roads.Count; i++)
            {
                var srcRoad = Roads[i];
                int mySectionCount = srcRoad.NumSections;
                bool isMyFirstSectionClosest = Vector3.Distance(srcRoad.SectionCenterCurve.Points[0].Position, Center)
                                              < Vector3.Distance(srcRoad.SectionCenterCurve.Points[mySectionCount - 1].Position, Center);
                Vector3 myRoadEnd = isMyFirstSectionClosest ? srcRoad.SectionCenterCurve.Points[0].Position
                                    : srcRoad.SectionCenterCurve.Points[mySectionCount - 1].Position;

                float sqrRadius = (myRoadEnd - Center).sqrMagnitude;
                if(sqrRadius > estimatedSqrRadius)
                {
                    estimatedSqrRadius = sqrRadius;
                }
            }
            EstimatedRadius = Mathf.Sqrt(estimatedSqrRadius) * 1.3f; //sqrt the result. Multiply it to encompass the entire intersection
                                                                     // because by default it will just barely touch the largest road
        }

        public bool IsPositionWithinIntersection(Vector3 position)
        {
            return Vector3.Distance(position, Center) <= EstimatedRadius;
        }

        /*
        public IEnumerable<Intersection> GetNeighbours(AINetwork network)
        {
            foreach(var road in this.Roads)
            {
                int endInts = road.LeftEndData.IntersectionID;
                int startInts = road.RightEndData.IntersectionID;

                if(startInts == this.Id)
                {
                    yield return network.Intersections[endInts];
                }
                else
                {
                    yield return network.Intersections[startInts];
                }
            }

            foreach (var cut in this.Shortcuts)
            {
                int endInts = cut.EndIntersection.Id;
                int startInts = cut.StartIntersection.Id;

                if (startInts == this.Id)
                {
                    yield return network.Intersections[endInts];
                }
                else
                {
                    yield return network.Intersections[startInts];
                }
            }
        }
        */

        public override int GetHashCode()
        {
            return Center.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var other = obj as Intersection;
            if (other == null)
                return false;

            return other.Id == this.Id && 
                   other.Room == this.Room && 
                   other.Center.Equals(this.Center) && 
                   other.Roads.SequenceEqual(this.Roads) && 
                   other.Shortcuts.SequenceEqual(this.Shortcuts);
        }

        public void WriteBinary(BinaryWriter writer)
        {
            writer.Write((ushort)Id);
            writer.Write((ushort)(Room));
            writer.WriteVector3Flipped(Center);

            writer.Write((ushort)Roads.Count);
            for (int i = 0; i < Roads.Count; i++)
                writer.Write(Roads[i].Id);
        }

        public static Intersection ReadBinary(BinaryReader reader)
        {
            var inter = new Intersection();

            //read
            inter.Id = reader.ReadUInt16();
            inter.Room = reader.ReadUInt16();
            inter.Center = reader.ReadVector3Flipped();

            int numRoads = reader.ReadUInt16();
            inter.Roads.Clear();
            for (int i = 0; i < numRoads; i++)
                inter.Roads.Add(new Road() { Id = reader.ReadInt32() }); //TEMP, OVERWRITTEN

            return inter;
        }
    }
}