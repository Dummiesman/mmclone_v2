using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public class ImageLoaderHelper
{
    /// <summary>
    /// A clusterfuck for creating arrays of Unity Color 
    /// </summary>
    /// <param name="fillArray"></param>
    /// <param name="pixelData"></param>
    /// <param name="bytesPerPixel"></param>
    /// <param name="bgra"></param>
    public static void FillPixelArray(Color32[] fillArray, byte[] pixelData, int bytesPerPixel, bool bgra = false)
    {
        //special case for TGA :(
        if (bgra)
        {
            if (bytesPerPixel == 4)
            {
                for (int i = 0; i < fillArray.Length; i++)
                {
                    int bi = i * bytesPerPixel;
                    fillArray[i] = new Color32(pixelData[bi + 2], pixelData[bi + 1], pixelData[bi], pixelData[bi + 3]);
                }
            }
            else
            {
                //24 bit BGR to Color32 (RGBA)
                //this is faster than safe code
                unsafe
                {
                    fixed (byte* p = &fillArray[0].r)
                    {
                        fixed (byte* d = &pixelData[0])
                        {
                            int pi = 0; //this keeps track of the index in fillArray
                            int bi = 0; //this keeps track of the index in pixelData
                            int len = fillArray.Length;
                            for (int i=0; i < len; i++)
                            {
                                p[pi++] = d[bi + 2];
                                p[pi++] = d[bi + 1];
                                p[pi++] = d[bi + 0];
                                pi++;
                                bi += 3;
                            }
                        }
                    }
                }
            }
        }
        else
        {            
            if (bytesPerPixel == 4)
            {
                //with RGBA, we can directly copy the memory!
                unsafe
                {
                    fixed (void* p = &fillArray[0].r)
                    {
                        fixed (void* d = &pixelData[0])
                        {
                            UnsafeUtility.MemCpy(p, d, pixelData.Length);
                        }
                    }
                }
            }
            else
            {
                //with RGB we can't! :(
                int bi = 0;
                for (int i = 0; i < fillArray.Length; i++)
                {
                    fillArray[i].r = pixelData[bi++];
                    fillArray[i].g = pixelData[bi++];
                    fillArray[i].b = pixelData[bi++];
                    fillArray[i].a = 255;
                }
            }
        }
    }
}
