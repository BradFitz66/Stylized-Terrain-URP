using UnityEngine;
using System.Collections.Generic;
using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

[Serializable]
public struct Grass
{
    public Matrix4x4 trs;
}
[Serializable]
public struct InstanceData<T>
{
    [HideInInspector]
    public Matrix4x4 trs;
    public Vector3 normal;

}



[RequireComponent(typeof(MarchingSquaresTerrain))]
public class GrassInstancer : MonoBehaviour
{
    MarchingSquaresTerrain t;

    List<Grass> grass = new List<Grass>();

    public Mesh grassMesh;
    public Material grassMaterial;

    //ComputeBuffers
    ComputeBuffer argsBuffer;
    ComputeBuffer grassBuffer;


    private void Start()
    {
        t = GetComponent<MarchingSquaresTerrain>();
        GenerateGrassOnCells();
    }

    void GenerateGrassOnCells()
    {
        grass = new List<Grass>();
        //Loop through all chunks
        foreach (KeyValuePair<Vector2Int, MarchingSquaresChunk> chunk in t.chunks)
        {
            MarchingSquaresChunk c = chunk.Value;
            //Loop through all cells
            for (int z = 0; z < t.dimensions.z; z++)
            {
                for (int x = 0; x < t.dimensions.x; x++)
                {
                    float4 color = c.colorMap[c.GetIndex(x, z)];
                    if (color.equal(new float4(1f, 0, 0, 0f)))
                    {
                        Vector3 cellPos = c.transform.position + new Vector3(x, 0, z);
                    }
                }
            }
        }
        //Create compute buffers
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        //Get size of Grass struct using Marshal

        grassBuffer = new ComputeBuffer(grass.Count, Marshal.SizeOf(typeof(Grass)));
        grassBuffer.SetData(grass.ToArray());

        grassMaterial.SetBuffer("positionBuffer", grassBuffer);
    }

    void OnDestroy()
    {
        grassBuffer.Release();
        argsBuffer.Release();
    }
    uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private void Update()
    {
        if (argsBuffer == null) return;

        args[0] = grassMesh.GetIndexCount(0);
        args[1] = (uint)grass.Count;
        args[2] = (uint)grassMesh.GetIndexStart(0);
        args[3] = (uint)grassMesh.GetBaseVertex(0);
        args[4] = (uint)0;

        argsBuffer.SetData(args);

        //Draw grass
        Graphics.DrawMeshInstancedIndirect(
            grassMesh,
            0,
            grassMaterial,
            new Bounds(Vector3.zero, new Vector3(1000, 1000, 1000)),
            argsBuffer
        );
    }
}
