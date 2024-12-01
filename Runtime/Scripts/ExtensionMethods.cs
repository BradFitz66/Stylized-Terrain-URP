using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

}