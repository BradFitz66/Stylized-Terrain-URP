using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using static UnityEngine.BoundsInt;

public static class ExtensionMethods
{
    public static Quaternion ClampRotation(this Quaternion q, Vector3 bounds)
    {
        q.x /= q.w;
        q.y /= q.w;
        q.z /= q.w;
        q.w = 1.0f;

        float angleX = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.x);
        angleX = Mathf.Clamp(angleX, -bounds.x, bounds.x);
        q.x = Mathf.Tan(0.5f * Mathf.Deg2Rad * angleX);

        float angleY = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.y);
        angleY = Mathf.Clamp(angleY, -bounds.y, bounds.y);
        q.y = Mathf.Tan(0.5f * Mathf.Deg2Rad * angleY);

        float angleZ = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.z);
        angleZ = Mathf.Clamp(angleZ, -bounds.z, bounds.z);
        q.z = Mathf.Tan(0.5f * Mathf.Deg2Rad * angleZ);

        return q.normalized;
    }

    public static Vector3 Snap(this Vector3 v, float snapValue)
    {
        return new Vector3(
            Mathf.Round(v.x / snapValue) * snapValue,
            Mathf.Round(v.y / snapValue) * snapValue,
            Mathf.Round(v.z / snapValue) * snapValue
        );
    }

    public static Vector3 Snap(this Vector3 v, float snapX, float snapY, float snapZ)
    {
        return new Vector3(
            Mathf.Round(v.x / snapX) * snapX,
            Mathf.Round(v.y / snapY) * snapY,
            Mathf.Round(v.z / snapZ) * snapZ
        );
    }


    public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
    public static void AddRange(this IList list, IEnumerable items)
    {
        foreach (var item in items)
        {
            list.Add(item);
        }
    }

    //Iterator for bounds (loop over each edge)
    public static PositionEnumerator allPositionsWithin(this Bounds bounds)
    {
        //Convert to BoundsInt
        BoundsInt b = new BoundsInt(
            Vector3Int.FloorToInt(bounds.min),
            Vector3Int.FloorToInt(bounds.size)
        );
        return b.allPositionsWithin;
    }

    //Cast color to float4
    public static float4 ToFloat4(this Color c)
    {
        return new float4(c.r, c.g, c.b, c.a);
    }

    //Cast float4 to color
    public static Color ToColor(this float4 f)
    {
        return new Color(f.x, f.y, f.z, f.w);
    }

    public static bool equal(this float4 a, float4 b)
    {
        return math.all(math.abs(a - b) < 0.0001f);
    }



    public static unsafe NativeArray<T> ToArray<T>(this in UnsafeList<T> list) where T : unmanaged
    {
        NativeArray<T> result = new NativeArray<T>(list.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        UnsafeUtility.MemCpy(result.GetUnsafePtr(), list.Ptr, (long)list.Length * sizeof(T));
        return result;
    }

}