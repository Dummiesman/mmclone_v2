using UnityEngine;
using System.IO;

public struct MatrixFile 
{
    public Vector3 BoundsMin;
    public Vector3 BoundsMax;
    public Vector3 Pivot;
    public Vector3 Origin;
	
    public static MatrixFile Zero
    {
        get
        {
            return new MatrixFile()
            {
                BoundsMin = Vector3.zero,
                BoundsMax = Vector3.zero,
                Pivot = Vector3.zero,
                Origin = Vector3.zero
            };
        }
    }

    public void Load(Stream matrixStream)
    {
        //can we read it?
        if (matrixStream == null)
        {
            return;
        }

        //read mtx
        using (var r = new BinaryReader(matrixStream, System.Text.Encoding.Default, true))
        {
            ReadBinary(r);
        }
    }

    public MatrixFile RemoveOffset()
    {
        return new MatrixFile()
        {
            Origin = Vector3.zero,
            Pivot = this.Pivot,
            BoundsMin = this.BoundsMin,
            BoundsMax = this.BoundsMax
        };
    }

    public MatrixFile FlipXZ()
    {
        return new MatrixFile()
        {
            Origin = new Vector3(-Origin.x, Origin.y, -Origin.z),
            Pivot = new Vector3(-Pivot.x, Pivot.y, Pivot.z),
            BoundsMin = BoundsMin,
            BoundsMax = BoundsMax
        };
    }

    public void ReadBinary(BinaryReader reader)
    {
        BoundsMin = reader.ReadVector3().ConvertCoordinateSpace();
        BoundsMax = reader.ReadVector3().ConvertCoordinateSpace();
        Pivot = reader.ReadVector3().ConvertCoordinateSpace();
        Origin = reader.ReadVector3().ConvertCoordinateSpace();
    }
}
