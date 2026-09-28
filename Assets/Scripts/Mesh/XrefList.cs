using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class XRef
{
    public string PackageFile;
    public Vector3 xAxis;
    public Vector3 yAxis;
    public Vector3 zAxis;
    public Vector3 Origin;

    public void Read(BinaryReader reader)
    {
        xAxis = reader.ReadVector3().ConvertCoordinateSpace();
        yAxis = reader.ReadVector3().ConvertCoordinateSpace();
        zAxis = reader.ReadVector3().ConvertCoordinateSpace();
        Origin = reader.ReadVector3().ConvertCoordinateSpace();

        var name = reader.ReadFixedString(32);
        PackageFile = name;
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(-xAxis.x);
        writer.Write(xAxis.y);
        writer.Write(xAxis.z);

        writer.Write(-yAxis.x);
        writer.Write(yAxis.y);
        writer.Write(yAxis.z);

        writer.Write(-zAxis.x);
        writer.Write(zAxis.y);
        writer.Write(zAxis.z);

        writer.Write(-Origin.x);
        writer.Write(Origin.y);
        writer.Write(Origin.z);

        writer.WriteFixedString(PackageFile, 32);
    }
}

public class XrefList
{
    public readonly List<XRef> Xrefs = new List<XRef>();

    public void Read(BinaryReader reader)
    {
        Xrefs.Clear();

        int numXrefs = reader.ReadInt32();
        Xrefs.Capacity = numXrefs;

        for (int i = 0; i < numXrefs; i++)
        {
            var xref = new XRef();
            xref.Read(reader);
            Xrefs.Add(xref);
        }
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Xrefs.Count);

        foreach (var xref in Xrefs)
        {
            xref.Write(writer);
        }
    }
}