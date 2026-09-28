using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PathSet
{   
    public class Path
    {
        public string Name;
        public float Spacing = 0;
        public PathType Type = PathType.Lines;
        public int Flags = 0;
        public List<Vector3> Points = new List<Vector3>();

        private float _length = 0f;
        public float Length
        {
            get
            {
                if (_length == 0f) RecalculateLength();
                return _length;
            }
        }

        public enum PathType
        {
            Points,
            Lines,
            LineStrip
        }


        public void RecalculateLength()
        {
            _length = 0f;
            for (int i = 0; i < Points.Count - 1; i++)
                _length += Vector3.Distance(Points[i], Points[i + 1]);
        }

        /// <summary>
        /// "rotates" a triangle strip path
        /// </summary>
        public void StripReorient()
        {
            if (Points.Count <= 3)
                return;
            Points.Reverse();
        }

        public void Enumerate(Action<Vector3, Quaternion> cb)
        {
            switch (Type)
            {
                case PathType.Points:
                    for (int i = 0; i < Points.Count; i++)
                    {
                        cb(Points[i], Quaternion.identity);
                    }
                    break;

                case PathType.Lines:
                    for (int i = 0; i + 1 < Points.Count; i += 2)
                    {
                        Vector3 origin = Points[i];

                        Vector3 target = Points[i + 1];
                        target.y = origin.y;

                        Vector3 direction = -(target - origin).normalized;

                        Quaternion rotation = direction.sqrMagnitude > 0f
                            ? Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, -90f, 0f)
                            : Quaternion.identity;

                        cb(origin, rotation);
                    }
                    break;

                case PathType.LineStrip:
                    if (Points.Count < 2)
                        return;

                    if (Spacing <= 0f)
                    {
                        Debug.LogWarning($"[Path] LineStrip has Spacing {Spacing}; skipping.");
                        return;
                    }


                    for (int i = 0; i < Points.Count - 1; i++)
                    {
                        float segmentDistance =
                            Vector3.Distance(Points[i], Points[i + 1]);

                        if (segmentDistance < Spacing)
                            continue;

                        float increment =
                            segmentDistance / Mathf.Floor(segmentDistance / Spacing);

                        float remainingProgress = segmentDistance;

                        Vector3 lineDir =
                            (Points[i + 1] - Points[i]).normalized;

                        Vector3 lineDirForward =
                            (Points[i + 1].RotateAroundY(Points[i], -90f) -
                             Points[i]).normalized;

                        Vector3 lineDirUp =
                            Vector3.Cross(lineDir, lineDirForward);

                        Quaternion rotation =
                            Quaternion.LookRotation(lineDirForward, lineDirUp);

                        Vector3 euler = rotation.eulerAngles;
                        euler.x = 0f;
                        rotation = Quaternion.Euler(euler);

                        do
                        {
                            Vector3 position = Vector3.Lerp(
                                Points[i + 1],
                                Points[i],
                                remainingProgress / segmentDistance);

                            cb(position, rotation);

                            remainingProgress -= increment;
                        }
                        while (remainingProgress >= Spacing);
                    }
                    break;
            }
        }

        internal void StripFlip()
        {
            for (int i = 0; i < Points.Count - 1; i+= 2)
            {
                Vector3 p1 = Points[i];
                Vector3 p2 = Points[i+1];
                Points[i] = p2;
                Points[i + 1] = p1;
            }
        }

        public void StripScale(float scaleFieldValue)
        {
            for (int i = 0; i < Points.Count - 1; i += 2)
            {
                Vector3 p1 = Points[i];
                Vector3 p2 = Points[i + 1];

                Vector3 p2p1dir = (p2 - p1).normalized;
                Vector3 p1p2dir = (p1 - p2).normalized;

                p1 += (p1p2dir * scaleFieldValue);
                p2 += (p2p1dir * scaleFieldValue);

                Points[i] = p1;
                Points[i + 1] = p2;
            }
        }

        public void WriteBinary(BinaryWriter w)
        {
            //write name
            for (int i = 0; i < 32; i++)
            {
                w.Write((i >= Name.Length || i == 31) ? '\x00' : Name[i]);
            }

            w.Write(Points.Count);
            w.Write(Flags);
            for (int i = 0; i < Points.Count; i++)
            {
                w.Write(0); // upper 8 bits may be flags, lower 24 bits may be room id, not used in game
                w.Write(-Points[i].x);
                w.Write(Points[i].y);
                w.Write(Points[i].z);
            }

            w.Write((byte) Type);
            w.Write((byte)(Spacing * 4f));
            w.Write((ushort)0);
        }

        public void ReadBinary(BinaryReader r)
        {
            string nameNullTerminated = new string(r.ReadChars(32));
            Name = nameNullTerminated.Substring(0, nameNullTerminated.IndexOf('\x00'));
            
            int numPoints = r.ReadInt32();
            Flags = r.ReadInt32();
            for (int i = 0; i < numPoints; i++)
            {
                uint unkVal = r.ReadUInt32();
                Points.Add(r.ReadVector3().ConvertCoordinateSpace());
            }

            Type = (PathType)r.ReadByte();
            Spacing = (float)r.ReadByte() * 0.25f;
            if (Spacing == 0.0f) Spacing = 5.0f;

            r.ReadUInt16(); //unused?
        }

        public void Reverse()
        {
            Points.Reverse();
        }

        public Path Flatten()
        {
            var path = new Path()
            {
                Type = this.Type,
                Name = this.Name,
                Spacing = this.Spacing,
                Flags = this.Flags
            };
            foreach (var pt in Points)
            {
                path.Points.Add(new Vector3(pt.x, 0, pt.z));
            }
            return path;
        }

        public void Offset(Vector3 offset)
        {
            for (int i = 0; i < Points.Count; i++)
                Points[i] += offset;
        }

        public Path() { }

        public Path(string name)
        {
            this.Name = name;
        }
    }

    public List<Path> Paths = new List<Path>();

    public void WriteBinary(BinaryWriter w)
    {
        w.Write(826823760); // PTH1

        w.Write(Paths.Count);
        w.Write(Paths.Count - 1);

        for (int i = 0; i < Paths.Count; i++)
        {
            Paths[i].WriteBinary(w);
        }
    }

    public void WriteBinary(Stream stream)
    {
        using (var w = new BinaryWriter(stream, System.Text.Encoding.Default, true))
        {
            WriteBinary(w);
        }
    }

    public void ReadBinary(BinaryReader r)
    {
        int header = r.ReadInt32();
        if (header != 826823760)
            throw new Exception("Pathset magic is incorrect (" + header + ")");

        int numPaths = r.ReadInt32();
        r.BaseStream.Seek(4, SeekOrigin.Current);

        Paths.Clear();

        for (int i = 0; i < numPaths; i++)
        {
            var path = new Path();
            path.ReadBinary(r);
            Paths.Add(path);
        }
    }

    public void ReadBinary(Stream stream)
    {
        using (var r = new BinaryReader(stream, System.Text.Encoding.Default, true))
        {
            ReadBinary(r);
        }
    }
}
