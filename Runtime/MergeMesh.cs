using System.Collections.Generic;
using Codice.Client.BaseCommands;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

//Taken from https://github.com/Unity-Technologies/MeshApiExamples/blob/master/Assets/CreateMeshFromAllSceneMeshes/CreateMeshFromWholeScene.cs

public class MergeMesh
{

    public static Mesh CombineInstanceMeshes(MeshFilter[] meshFilters, Matrix4x4[] transforms)
    {
        var jobs = new ProcessMeshDataJob();
        jobs.CreateInputArrays(meshFilters.Length);
        var input = new List<Mesh>(meshFilters.Length);
    
        
        
        var vertexStart = 0;
        var indexStart = 0;
        var meshCount = 0;
        for (var i = 0; i < meshFilters.Length; ++i)
        {
            var mesh = meshFilters[i].sharedMesh;
            if (!mesh)
                continue;
            input.Add(mesh);
            jobs.vertexStart[meshCount] = vertexStart;
            jobs.indexStart[meshCount] = indexStart;
            jobs.xform[meshCount] = transforms[i];
            vertexStart += mesh.vertexCount;
            indexStart += (int)mesh.GetIndexCount(0);
            jobs.bounds[meshCount] = new float3x2(new float3(Mathf.Infinity), new float3(Mathf.NegativeInfinity));
            ++meshCount;
        }
        jobs.meshData = Mesh.AcquireReadOnlyMeshData(input);
        
        // Create and initialize writable data for the output mesh
        var outputMeshData = Mesh.AllocateWritableMeshData(1);
        jobs.outputMesh = outputMeshData[0];
        jobs.outputMesh.SetIndexBufferParams(indexStart, IndexFormat.UInt32);
        jobs.outputMesh.SetVertexBufferParams(vertexStart,
            new VertexAttributeDescriptor(VertexAttribute.Position, stream: 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal,    stream: 1)
            // new VertexAttributeDescriptor(VertexAttribute.Color,     stream: 1),
            // new VertexAttributeDescriptor(VertexAttribute.TexCoord0, stream: 1)
        );

        // Launch mesh processing jobs
        var handle = jobs.Schedule(meshCount, 4);

        var newMesh = new Mesh();
        newMesh.name = "CombinedMesh";
        var sm = new SubMeshDescriptor(0, indexStart, MeshTopology.Triangles);
        sm.firstVertex = 0;
        sm.vertexCount = vertexStart;

        handle.Complete();
        

        var bounds = new float3x2(new float3(Mathf.Infinity), new float3(Mathf.NegativeInfinity));
        for (var i = 0; i < meshCount; ++i)
        {
            var b = jobs.bounds[i];
            bounds.c0 = math.min(bounds.c0, b.c0);
            bounds.c1 = math.max(bounds.c1, b.c1);
        }
        sm.bounds = new Bounds((bounds.c0+bounds.c1)*0.5f, bounds.c1-bounds.c0);
        jobs.outputMesh.subMeshCount = 1;
        jobs.outputMesh.SetSubMesh(0, sm, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
        Mesh.ApplyAndDisposeWritableMeshData(outputMeshData, new[]{newMesh}, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
        newMesh.bounds = sm.bounds;
        
        return newMesh;
    }
}

[BurstCompile]
struct ProcessMeshDataJob : IJobParallelFor
{
    [ReadOnly] public Mesh.MeshDataArray meshData;
    public Mesh.MeshData outputMesh;
    [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> vertexStart;
    [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> indexStart;
    [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<float4x4> xform;
    public NativeArray<float3x2> bounds;

    [NativeDisableContainerSafetyRestriction] NativeArray<float3> tempVertices;
    [NativeDisableContainerSafetyRestriction] NativeArray<float3> tempNormals;
    // [NativeDisableContainerSafetyRestriction] NativeArray<float4> tempColor;
    // [NativeDisableContainerSafetyRestriction] NativeArray<float2> tempUV;

    public void CreateInputArrays(int meshCount)
    {
        vertexStart = new NativeArray<int>(meshCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        indexStart = new NativeArray<int>(meshCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        xform = new NativeArray<float4x4>(meshCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        bounds = new NativeArray<float3x2>(meshCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
    }

    public void Execute(int index)
    {
        var data = meshData[index];
        var vCount = data.vertexCount;
        var mat = xform[index];
        var vStart = vertexStart[index];

        // Allocate temporary arrays for input mesh vertices/normals
        if (!tempVertices.IsCreated || tempVertices.Length < vCount)
        {
            if (tempVertices.IsCreated) tempVertices.Dispose();
            tempVertices = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        }
        if (!tempNormals.IsCreated || tempNormals.Length < vCount)
        {
            if (tempNormals.IsCreated) tempNormals.Dispose();
            tempNormals = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        }

        // if (!tempColor.IsCreated || tempColor.Length < vCount)
        // {
        //     if (tempColor.IsCreated) tempColor.Dispose();
        //     tempColor = new NativeArray<float4>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        // }
        // if (!tempUV.IsCreated || tempUV.Length < vCount)
        // {
        //     if (tempUV.IsCreated) tempUV.Dispose();
        //     tempUV = new NativeArray<float2>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        // }
        
        // Read input mesh vertices/normals into temporary arrays -- this will
        // do any necessary format conversions into float3 data
        data.GetVertices(tempVertices.Reinterpret<Vector3>());
        data.GetNormals(tempNormals.Reinterpret<Vector3>());
        // data.GetColors(tempColor.Reinterpret<Color>());
        // data.GetUVs(0, tempUV.Reinterpret<Vector2>());

        var outputVerts = outputMesh.GetVertexData<Vector3>();
        var outputNormals = outputMesh.GetVertexData<Vector3>(stream:1);
        var outputColor = outputMesh.GetVertexData<Color>(stream:1);
        var outputUV = outputMesh.GetVertexData<Vector2>(stream:1);

        // Transform input mesh vertices/normals, write into destination mesh,
        // compute transformed mesh bounds.
        var b = bounds[index];
        for (var i = 0; i < vCount; ++i)
        {
            var pos = tempVertices[i];
            pos = math.mul(mat, new float4(pos, 1)).xyz;
            outputVerts[i+vStart] = pos;
            var nor = tempNormals[i];
            nor = math.normalize(math.mul(mat, new float4(nor, 0)).xyz);
            outputNormals[i+vStart] = nor;
            // var col = tempColor[i];
            // outputColor[i+vStart] = new Color(col.x, col.y, col.z, col.w);
            // var uv = tempUV[i];
            // outputUV[i+vStart] = uv;
            b.c0 = math.min(b.c0, pos);
            b.c1 = math.max(b.c1, pos);
        }
        bounds[index] = b;

        // Write input mesh indices into destination index buffer
        var tStart = indexStart[index];
        var tCount = data.GetSubMesh(0).indexCount;
        var outputTris = outputMesh.GetIndexData<int>();
        if (data.indexFormat == IndexFormat.UInt16)
        {
            var tris = data.GetIndexData<ushort>();
            for (var i = 0; i < tCount; ++i)
                outputTris[i + tStart] = vStart + tris[i];
        }
        else
        {
            var tris = data.GetIndexData<int>();
            for (var i = 0; i < tCount; ++i)
                outputTris[i + tStart] = vStart + tris[i];
        }
    }
}
