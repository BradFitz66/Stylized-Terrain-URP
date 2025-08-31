/* 
 * The following code was taken from: http://schemingdeveloper.com
 *
 * Visit our game studio website: http://stopthegnomes.com
 *
 * License: You may use this code however you see fit, as long as you include this notice
 *          without any modifications.
 *
 *          You may not publish a paid asset on Unity store if its main function is based on
 *          the following code, but you may publish a paid asset that uses this code.
 *
 *          If you intend to use this in a Unity store asset or a commercial project, it would
 *          be appreciated, but not required, if you let me know with a link to the asset. If I
 *          don't get back to you just go ahead and use it anyway!
 */

using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

public static class NormalSolver
{
    static ProfilerMarker _collectTrianglesMarker = new ProfilerMarker("Collect Triangles");
    static ProfilerMarker _processVerticesMarker = new ProfilerMarker("Process Vertices");
    /// <summary>
    ///     Recalculate the normals of a mesh based on an angle threshold. This takes
    ///     into account distinct vertices that have the same position.
    /// </summary>
    /// <param name="mesh"></param>
    /// <param name="angle">
    ///     The smoothing angle. Note that triangles that already share
    ///     the same vertex will be smooth regardless of the angle! 
    /// </param>
    [BurstCompile(CompileSynchronously = true)]
    public static void RecalculateNormals(this Mesh mesh, float angle)
    {
        var cosineThreshold = math.cos(angle * Mathf.Deg2Rad);

        var vertices = mesh.vertices;
        var normals = new NativeArray<float3>(vertices.Length,Allocator.Temp);
        var triNormals = new float3[mesh.subMeshCount][];
        var dictionary = new NativeHashMap<VertexKey, NativeList<VertexEntry>>(vertices.Length, Allocator.Temp);
        
        _collectTrianglesMarker.Begin();
        for (var subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; ++subMeshIndex)
        {

            var triangles = mesh.GetTriangles(subMeshIndex);

            triNormals[subMeshIndex] = new float3[triangles.Length / 3];

            for (var i = 0; i < triangles.Length; i += 3)
            {
                int i1 = triangles[i];
                int i2 = triangles[i + 1];
                int i3 = triangles[i + 2];

                // Calculate the normal of the triangle
                float3 p1 = vertices[i2] - vertices[i1];
                float3 p2 = vertices[i3] - vertices[i1];
                float3 normal = math.normalize(math.cross(p1, p2));
                int triIndex = i / 3;
                triNormals[subMeshIndex][triIndex] = normal;

                VertexKey key;

                if (!dictionary.TryGetValue(key = new VertexKey(vertices[i1]), out var entry))
                {
                    entry = new NativeList<VertexEntry>(4,Allocator.Temp);
                    dictionary.Add(key, entry);
                }
                entry.Add(new VertexEntry(subMeshIndex, triIndex, i1));

                if (!dictionary.TryGetValue(key = new VertexKey(vertices[i2]), out var entry2))
                {
                    entry2 = new NativeList<VertexEntry>(Allocator.Temp);
                    dictionary.Add(key, entry2);
                }
                entry2.Add(new VertexEntry(subMeshIndex, triIndex, i2));

                if (!dictionary.TryGetValue(key = new VertexKey(vertices[i3]), out var entry3))
                {
                    entry3 = new NativeList<VertexEntry>(Allocator.Temp);
                    dictionary.Add(key, entry3);
                }
                entry3.Add(new VertexEntry(subMeshIndex, triIndex, i3));
            }
        }
        _collectTrianglesMarker.End();

        _processVerticesMarker.Begin();
        // Each entry in the dictionary represents a unique vertex position
        var valueArray = dictionary.GetValueArray(Allocator.Temp);
        for (var i = 0; i < valueArray.Length; ++i)
        {
            var vertList = valueArray[i];
            for (var j = 0; j < vertList.Length; ++j)
            {
                var sum = new float3();
                var lhsEntry = vertList[j];

                for (var k = 0; k < vertList.Length; ++k)
                {
                    var rhsEntry = vertList[k];

                    if (lhsEntry.VertexIndex == rhsEntry.VertexIndex)
                    {
                        sum += triNormals[rhsEntry.MeshIndex][rhsEntry.TriangleIndex];
                    }
                    else
                    {
                        // The dot product is the cosine of the angle between the two triangles.
                        // A larger cosine means a smaller angle.
                        var dot = math.dot(
                            triNormals[lhsEntry.MeshIndex][lhsEntry.TriangleIndex],
                            triNormals[rhsEntry.MeshIndex][rhsEntry.TriangleIndex]);
                        if (dot >= cosineThreshold)
                        {
                            sum += triNormals[rhsEntry.MeshIndex][rhsEntry.TriangleIndex];
                        }
                    }
                }

                normals[lhsEntry.VertexIndex] = math.normalize(sum);
            }
        }
        _processVerticesMarker.End();
        mesh.SetNormals<float3>(normals);
        
        valueArray.Dispose();
        normals.Dispose();
        dictionary.Dispose();
    }

    private struct VertexKey : System.IEquatable<VertexKey>
    {
        private readonly long _x;
        private readonly long _y;
        private readonly long _z;

        // Change this if you require a different precision.
        private const int Tolerance = 100000;

        // Magic FNV values. Do not change these.
        private const long FNV32Init = 0x811c9dc5;
        private const long FNV32Prime = 0x01000193;

        public VertexKey(Vector3 position)
        {
            _x = (long)(Mathf.Round(position.x * Tolerance));
            _y = (long)(Mathf.Round(position.y * Tolerance));
            _z = (long)(Mathf.Round(position.z * Tolerance));
        }

        public bool Equals(VertexKey other)
        {
            return _x == other._x && _y == other._y && _z == other._z;
        }

        public override bool Equals(object obj)
        {
            var key = (VertexKey)obj;
            return _x == key._x && _y == key._y && _z == key._z;
        }

        public override int GetHashCode()
        {
            long rv = FNV32Init;
            rv ^= _x;
            rv *= FNV32Prime;
            rv ^= _y;
            rv *= FNV32Prime;
            rv ^= _z;
            rv *= FNV32Prime;

            return rv.GetHashCode();
        }
    }

    private struct VertexEntry
    {
        public int MeshIndex;
        public int TriangleIndex;
        public int VertexIndex;

        public VertexEntry(int meshIndex, int triIndex, int vertIndex)
        {
            MeshIndex = meshIndex;
            TriangleIndex = triIndex;
            VertexIndex = vertIndex;
        }
    }
}