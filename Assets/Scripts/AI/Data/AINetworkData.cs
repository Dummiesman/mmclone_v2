using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MM2.AI
{
    public class AINetworkData
    {
        public readonly List<Road> Roads = new List<Road>();
        public readonly List<Intersection> Intersections = new List<Intersection>();
        public readonly List<Road> Shortcuts = new List<Road>();

        public readonly List<HashSet<int>> PedestrianCullRoads = new List<HashSet<int>>();
        public readonly List<HashSet<int>> TrafficCullRoads = new List<HashSet<int>>();
        public readonly List<HashSet<int>> CombinedCullRoads = new List<HashSet<int>>();

        public Road GetRoad(int id)
        {
            if (id < 0) return null;
            if (id < Roads.Count) return id < Roads.Count ? Roads[id] : null;
            id -= Roads.Count;
            return id < Shortcuts.Count ? Shortcuts[id] : null;
        }

        public void WriteBinary(BinaryWriter writer)
        {
            writer.Write('C');
            writer.Write('A');
            writer.Write('I');
            writer.Write('1');

            writer.Write((ushort)Intersections.Count);
            writer.Write((ushort)Roads.Count);

            foreach (var road in Roads)
                road.WriteBinary(writer);
            foreach (var intersection in Intersections)
                intersection.WriteBinary(writer);

            // write culling lists
            writer.Write(TrafficCullRoads.Count + 1);

            // write far culling indices
            writer.Write((ushort)0); // first room has 0
            for (int i = 0; i < TrafficCullRoads.Count; i++)
            {
                var roadList = TrafficCullRoads[i];

                writer.Write((ushort)roadList.Count);
                foreach (var roadId in roadList)
                {
                    writer.Write((ushort)roadId);
                }
            }

            writer.Write((ushort)0); // first room has 0
            for (int i = 0; i < PedestrianCullRoads.Count; i++)
            {
                var roadList = PedestrianCullRoads[i];

                writer.Write((ushort)roadList.Count);
                foreach(var roadId in roadList)
                {
                    writer.Write((ushort)roadId);
                }
            }
        }

        public void ReadShortcutsBinary(BinaryReader reader)
        {
            string header = new string(reader.ReadChars(4));
            if (header != "CAI1")
                throw new Exception("AINetworkData.ReadShortcutsBinary failed. Wrong header.");

            // read shortcuts
            int numShortcuts = reader.ReadUInt16();
            for (int i = 0; i < numShortcuts; i++)
                Shortcuts.Add(Road.ReadShortcut(reader, Roads.Count + i));

            // init road turns
            foreach (var road in Shortcuts)
            {
                road.InitRoadTurns();
            }

            // map shortcuts
            foreach (var cut in Shortcuts)
            {
                Intersections[cut.LeftEndData.IntersectionID].Shortcuts.Add(cut);
                Intersections[cut.RightEndData.IntersectionID].Shortcuts.Add(cut);
            }
        }

        public void ReadBinary(BinaryReader reader)
        {
            string header = new string(reader.ReadChars(4));
            if (header != "CAI1")
                throw new Exception("AINetworkData.ReadBinary failed. Wrong header.");

            // read bai
            int numIntersections = reader.ReadUInt16();
            int numRoads = reader.ReadUInt16();

            for (int i = 0; i < numRoads; i++)
                Roads.Add(Road.ReadBinary(reader));
            for (int i = 0; i < numIntersections; i++)
                Intersections.Add(Intersection.ReadBinary(reader));

            // init road turns
            foreach(var road in Roads)
            {
                road.InitRoadTurns();
            }

            // culling
            int numCullRooms = reader.ReadInt32();
            for (int i = 0; i < numCullRooms; i++)
            {
                int numCullRoads = reader.ReadUInt16();
                var roadList = new HashSet<int>();
                for (int j = 0; j < numCullRoads; j++)
                {
                    roadList.Add(reader.ReadUInt16());
                }

                if (i > 0)
                    TrafficCullRoads.Add(roadList);
            }

            // small
            for (int i = 0; i < numCullRooms; i++)
            {
                int numCullRoads = reader.ReadUInt16();
                var roadList = new HashSet<int>();
                for (int j = 0; j < numCullRoads; j++)
                {
                    roadList.Add(reader.ReadUInt16());
                }

                if (i > 0)
                    PedestrianCullRoads.Add(roadList);
            }

            // merge
            for (int i = 0; i < numCullRooms - 1; i++)
            {
                var pedRoads = PedestrianCullRoads[i];
                var trafficRoads = TrafficCullRoads[i];

                var combined = new HashSet<int>();
                combined.UnionWith(pedRoads);
                combined.UnionWith(trafficRoads);

                CombinedCullRoads.Add(combined);
            }

            // map roads to intersections, and calculate angles
            foreach (var intersection in Intersections)
            {
                for (int i = 0; i < intersection.Roads.Count; i++)
                {
                    intersection.Roads[i] = Roads[intersection.Roads[i].Id];
                }
                intersection.ComputeAnglesAndRadius();
            }
        }
    }
}