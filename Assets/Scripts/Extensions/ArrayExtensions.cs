using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ArrayExtensions 
{
    public static BinaryReader CreateReader(this byte[] array)
    {
        return new BinaryReader(new MemoryStream(array));
    }

    private static int WrapIndex(int boundary, int index)
    {
        if (boundary == 0) return -1;
        return index >= 0 ? index % boundary : boundary - (Mathf.Abs(index) % boundary);
    }

	public static int WrapIndex(this Array array, int index)
	{
	    return WrapIndex(array.Length, index);
	}

    public static int WrapIndex<T>(this List<T> list, int index)
    {
        return WrapIndex(list.Count, index);
    }
}
