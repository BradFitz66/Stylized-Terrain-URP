/*
 
MIT License

Copyright (c) 2020 Kitbashery

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.


Created by Jorge Reyna Tamez @ https://www.fiverr.com/wkepro
Created for Kitbashery @ https://www.kitbashery.com

*/

using System.Collections.Generic;
using UnityEngine;


namespace Kitbashery.MeshCombiner
{
    public static class MeshCombiner
    {
        /// <summary>
        /// Combines the meshes of mesh filter.
        /// </summary>
        /// <param name="filters"></param>
        /// <param name="weld">Should mesh vertices be welded?</param>
        /// <param name="gap">Padding between packed UV isalnds.</param>
        /// <returns></returns>
        public static Mesh Combine(MeshFilter[] filters, bool weld, float gap)
        {

            if (filters.Length == 0)
            {
                Debug.LogError("Mesh list is empty.");
                return null;
            }

            // the root transform is used as base to translate local space to world space and back.
            Transform root = filters[0].transform;

            // Get extended data from each meh.
            int meshCount = filters.Length;
            MeshExtended[] extended = new MeshExtended[meshCount];
            for (int m = 0; m < meshCount; m++)
            {
                MeshFilter filter = filters[m];
                extended[m] = new MeshExtended();
                // The Mesh Extender take care of translating vertex positions to world space and back.
                // also calculates the required box size for the UVs
                extended[m].Prepare(filter.sharedMesh, weld, root, filter.GetComponent<Transform>(), MeshExtended.SizeMethod.WorldScale);
            }
            
            // Prepare a new mesh.
            Mesh combined = new Mesh();
            List<Vector3> combinedVertices = new List<Vector3>();
            List<Vector2> combinedUVs = new List<Vector2>();
            List<Vector3> combinedNormals = new List<Vector3>();
            List<int> combinedTris = new List<int>();
            int triOffset = 0;

            // once welded and with the UVs packed we can just add items to lists.
            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {

                MeshExtended extmesh = extended[meshIndex];
                Vector3[] vertices = extmesh.Vertices;
                Vector2[] uvs = extmesh.UVs;
                Vector3[] normals = extmesh.Normals;
                int[] triangles = extmesh.Triangles;
                int vertexCount = vertices.Length;
                int triCount = triangles.Length;

                for (int v = 0; v < vertexCount; v++)
                {
                    combinedVertices.Add(vertices[v]);
                    combinedUVs.Add(uvs[v]);
                    combinedNormals.Add(normals[v]);
                }

                for (int t = 0; t < triCount; t++)
                {
                    combinedTris.Add(triangles[t] + triOffset);
                }
                triOffset = combinedVertices.Count;

            }

            // with all the UVs merged we can make the final adjustments.
            UVPacker.AdjustSpace(combinedUVs);

            // and finally create the new mesh.
            combined.SetVertices(combinedVertices);
            combined.SetUVs(0, combinedUVs);
            combined.SetNormals(combinedNormals);
            combined.SetTriangles(combinedTris, 0);
            combined.RecalculateTangents();

            return combined;
        }
    }
}