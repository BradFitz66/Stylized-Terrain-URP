using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using NativeTrees;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using NUnit.Framework;
using System.Threading.Tasks;

[System.Serializable]
public struct DetailObject
{
    public float4x4 trs;
    public float3 normal;
    public float normalOffset;
}

public struct TerrainLayer
{
    public Texture2D cliffTexture;
    public Texture2D groundTexture;
}

public enum NoiseMixMode
{
    Replace,
    Add,
    Multiply,
    Subtract,
}

[Serializable]
public struct NoiseSettings
{
    public float scale;
    public double frequency;
    public double amplitude;
    public double lacunarity;
    public double persistence;
    public int octaves;
    public Vector2 offset;
    public int seed;
    public NoiseMixMode mixMode;
}

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[ExecuteInEditMode]
public class MarchingSquaresTerrain : MonoBehaviour
{

    ComputeBuffer detailBuffer;
    ComputeBuffer argsBuffer;
    
    //List<DetailObject> allDetail;

    

    uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    MaterialPropertyBlock mpb;

    NativeQuadtree<DetailObject> detailQuadtree;


    public Vector3Int dimensions = new Vector3Int(10, 10, 10);
    public Bounds totalTerrainSize
    {
        get {
            //Get the total size of the terrain with all chunks
            Bounds b = new Bounds(transform.position,Vector3.zero);
            foreach (var chunk in chunks)
            {
                if (chunk.Value == null) continue;
                if (b.size == Vector3.zero)
                    b.center = chunk.Value.transform.position;
                Bounds cB = chunk.Value.GetComponent<MeshFilter>().sharedMesh.bounds;
                cB.center += chunk.Value.transform.position;
                b.Encapsulate(cB);
            }
            b.size += Vector3.up;
            return b;
        }
    }
    public Vector2 cellSize = new Vector2(2, 2);
    public Material terrainMaterial;

    public float mergeThreshold = 0.6f;
    public float heightBanding = 0.25f;

    public List<Vector2Int> chunkKeys = new List<Vector2Int>();
    public List<MarchingSquaresChunk> chunkValues = new List<MarchingSquaresChunk>();

    public SerializedDictionary<Vector2Int, MarchingSquaresChunk> chunks = new SerializedDictionary<Vector2Int, MarchingSquaresChunk>();
    public SerializedDictionary<Vector2Int, List<DetailObject>> detailChunks = new SerializedDictionary<Vector2Int, List<DetailObject>>();

    public Texture2D[] groundTextures = new Texture2D[4];
    public NoiseSettings noiseSettings;

    public int selectedTool = 0;
    public TerrainTool[] tools;
    public TerrainTool currentTool;
    public TerrainTool lastTool;

    public Mesh detailMesh;
    public Material detailMaterial;

    public float detailDensity = 0.1f; //Minimum distance between details
    public float currentDetailDensity = 0.1f;

    public float cloudDensity = 0.1f;
    public Vector2 cloudSpeed = Vector2.zero;
    public float cloudScale = 0.1f;
    public float cloudBrightness;
    public float cloudVerticalSpeed;


    public int detailCount => detailChunks.Values.SelectMany(x => x).Count();
    public DetailObject[] detailData => detailChunks.Values.SelectMany(x => x).ToArray();

    Vector3[] vertexCache;
    Vector3[] normalCache;
    int[] triangleCache;

    private void Awake()
    {
        InitializeBuffers();
        UpdateDetailBuffer();
    }

    private void OnValidate()
    {
        UpdateClouds();

    }

    private void OnEnable()
    {
        InitializeBuffers();
        UpdateDetailBuffer();
    }

    void InitializeBuffers()
    {
        argsBuffer?.Release();
        detailBuffer?.Release();
        args = new uint[5] { detailMesh.GetIndexCount(0), (uint)detailCount, detailMesh.GetIndexStart(0), detailMesh.GetBaseVertex(0), 0 };
        if (argsBuffer == null)
        {
            argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
            argsBuffer.SetData(args);
        }
        if (mpb == null)
        {
            mpb = new MaterialPropertyBlock();//
        }
        if (detailBuffer == null)
        {
            detailBuffer = new ComputeBuffer(1000000, Marshal.SizeOf(typeof(DetailObject))); //Preallocate 1million details
            detailBuffer.SetData(detailData);
        }

        mpb = new MaterialPropertyBlock();
        argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        args[1] = (uint)detailCount;
        argsBuffer.SetData(args);
    }

    public void UpdateDetailBuffer()
    {
        
        args[1] = (uint)detailCount;
        argsBuffer.SetData(args);

        if (mpb == null)
            mpb = new MaterialPropertyBlock();
        if (detailCount == 0)
            return;
        if (detailBuffer == null)
            detailBuffer = new ComputeBuffer(1000000, Marshal.SizeOf(typeof(DetailObject)));

        print(detailData + " " + detailCount);

        //Append new data to the buffer
        detailBuffer.SetData(
            detailData,
            0,
            0,
            detailCount
        );

        mpb?.SetBuffer("_TerrainDetail", detailBuffer);
    }
    
    //Cleanup
    private void OnDestroy()
    {
        if (argsBuffer != null)
            argsBuffer.Release();
        if (detailBuffer != null)
            detailBuffer.Release();
    }
    public void AddDetail(float size, float normalOffset, float3 detailPos, MarchingSquaresChunk chunk)
    {

        //Return if the detail is out of bounds
        if (!totalTerrainSize.Contains(detailPos))
            return;

        var chunks = GetChunksAtWorldPosition(detailPos);
        if (chunks.Count == 0)
            return;

        MarchingSquaresChunk c = chunks[0];

        bool canPlace = true;
        foreach (var otherChunk in chunks)
        {
            if (!detailChunks.ContainsKey(otherChunk.chunkPosition))
                detailChunks.Add(otherChunk.chunkPosition, new List<DetailObject>());
            Parallel.ForEach(detailChunks[otherChunk.chunkPosition], (d) =>
            {
                if (math.distance(new float3(d.trs.c3.x, detailPos.y, d.trs.c3.z), detailPos) < currentDetailDensity)
                {
                    print(currentDetailDensity);
                    canPlace = false;
                }
            });
        }

        if (!canPlace)
            return;

        //Round detailPos to the nearest cell
        Vector2Int cellPos = new Vector2Int(
            Mathf.FloorToInt((detailPos.x - c.transform.position.x) / cellSize.x),
            Mathf.FloorToInt((detailPos.z - c.transform.position.z) / cellSize.y)
        );

        float height = chunk.heightMap[chunk.GetIndex(cellPos.y, cellPos.x)];

        detailPos.y = height + normalOffset;

        Matrix4x4 trs = Matrix4x4.TRS(detailPos, Quaternion.identity, Vector3.one * size);
        DetailObject detailObject = new DetailObject()
        {
            trs = trs,
            normal = Vector3.up,
            normalOffset = normalOffset,
        };

        detailChunks[c.chunkPosition].Add(detailObject);

        //allDetail.Add(detailObject);
        UpdateDetailHeight(c);
    }


    public void AddNewChunk(int chunkX, int chunkY)
    {
        var chunkCoords = new Vector2Int(chunkX, chunkY);
        var newChunk = new GameObject("Chunk " + chunkCoords);

        MarchingSquaresChunk chunk = newChunk.AddComponent<MarchingSquaresChunk>();
        newChunk.AddComponent<MeshFilter>();
        newChunk.AddComponent<MeshCollider>();
        newChunk.AddComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        newChunk.layer = gameObject.layer;

        AddChunk(chunkCoords, chunk);

        var chunkLeft = chunks.TryGetValue(new Vector2Int(chunkX - 1, chunkY), out var leftChunk);
        if (chunkLeft)
        {
            for (int z = 0; z < dimensions.z; z++)
            {
                int idx1 = chunk.GetIndex(z, 0);
                int idx2 = leftChunk.GetIndex(z, dimensions.x - 1);
                chunk.heightMap[idx1] = leftChunk.heightMap[idx2];

            }

            //Add neighbor
            leftChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(leftChunk);
        }
        var chunkRight = chunks.TryGetValue(new Vector2Int(chunkX + 1, chunkY), out var rightChunk);
        if (chunkRight)
        {
            for (int z = 0; z < dimensions.z; z++)
            {
                int idx1 = chunk.GetIndex(z, dimensions.x - 1);
                int idx2 = rightChunk.GetIndex(z, 0);
                chunk.heightMap[idx1] = rightChunk.heightMap[idx2];
            }
            rightChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(rightChunk);
        }
        var chunkUp = chunks.TryGetValue(new Vector2Int(chunkX, chunkY + 1), out var upChunk);
        if (chunkUp)
        {
            for (int x = 0; x < dimensions.x; x++)
            {
                int idx1 = chunk.GetIndex(dimensions.z - 1, x);
                int idx2 = upChunk.GetIndex(0, x);
                chunk.heightMap[idx1] = upChunk.heightMap[idx2];
            }
            upChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(upChunk);
        }
        var chunkDown = chunks.TryGetValue(new Vector2Int(chunkX, chunkY - 1), out var downChunk);
        if (chunkDown)
        {
            for (int x = 0; x < dimensions.x; x++)
            {
                int idx1 = chunk.GetIndex(0, x);
                int idx2 = downChunk.GetIndex(dimensions.z - 1, x);
                chunk.heightMap[idx1] = downChunk.heightMap[idx2];
            }
            downChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(downChunk);
        }
        var chunkUpright = chunks.TryGetValue(new Vector2Int(chunkX + 1, chunkY + 1), out var upRightChunk);
        if (chunkUpright)
        {
            int idx1 = chunk.GetIndex(0, dimensions.x - 1);
            int idx2 = upRightChunk.GetIndex(dimensions.z - 1, 0);
            chunk.heightMap[idx1] = upRightChunk.heightMap[idx2];
            upRightChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(upRightChunk);
        }
        var chunkUpleft = chunks.TryGetValue(new Vector2Int(chunkX - 1, chunkY + 1), out var upLeftChunk);
        if (chunkUpleft)
        {
            int idx1 = chunk.GetIndex(dimensions.z - 1, 0);
            int idx2 = upLeftChunk.GetIndex(0, dimensions.x - 1);
            chunk.heightMap[idx1] = upLeftChunk.heightMap[idx2];
            upLeftChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(upLeftChunk);
        }
        var chunkDownright = chunks.TryGetValue(new Vector2Int(chunkX + 1, chunkY - 1), out var downRightChunk);
        if (chunkDownright)
        {
            int idx1 = chunk.GetIndex(dimensions.z - 1, dimensions.x - 1);
            int idx2 = downRightChunk.GetIndex(0, 0);
            chunk.heightMap[idx1] = downRightChunk.heightMap[idx2];
            downRightChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(downRightChunk);
        }
        var chunkDownleft = chunks.TryGetValue(new Vector2Int(chunkX - 1, chunkY - 1), out var downLeftChunk);
        if (chunkDownleft)
        {
            int idx1 = chunk.GetIndex(0, 0);
            int idx2 = downLeftChunk.GetIndex(dimensions.z - 1, dimensions.x - 1);
            chunk.heightMap[idx1] = downLeftChunk.heightMap[idx2];
            downLeftChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(downLeftChunk);
        }



        chunk.RegenerateMesh();
        MergeChunks();
    }

    public void GenerateTerrain()
    {
        foreach (var chunk in chunks)
        {
            chunk.Value.GenerateHeightmap(
                noiseSettings
            );
        }

        MergeChunks();
    }
    public void RemoveChunk(Vector2Int coords)
    {
        if (chunks.ContainsKey(coords))
        {
            if (chunks[coords] != null)
                DestroyImmediate(chunks[coords].gameObject);

            //Loop through all chunks and remove the chunk from their neighbor list
            foreach (var chunk in chunks)
            {
                if (chunk.Value.neighboringChunks.Contains(chunks[coords]))
                {
                    chunk.Value.neighboringChunks.Remove(chunks[coords]);
                }
            }

            chunks.Remove(coords);
        }

        if (detailChunks.ContainsKey(coords))
        {
            detailChunks.Remove(coords);
            UpdateDetailBuffer();
        }

        MergeChunks();
    }

    void AddChunk(Vector2Int coords, MarchingSquaresChunk chunk, bool regenMesh = true)
    {
        if (chunks.ContainsKey(coords))
        {
            DestroyImmediate(chunks[coords].gameObject);
            chunks.Remove(coords);
        }

        chunks.Add(coords, chunk);
        chunk.terrain = this;
        chunk.chunkPosition = coords;

        chunk.transform.position = new Vector3(
            (coords.x * ((dimensions.x-1) * cellSize.x)),
            0,
            (coords.y * ((dimensions.z-1) * cellSize.y))
        );

        chunk.transform.parent = transform;
        chunk.InitializeTerrain();
    }
    void UpdateDetailHeight(MarchingSquaresChunk chunk)
    {
        Vector2Int chunkPos = chunk.chunkPosition;
        if (!detailChunks.ContainsKey(chunkPos))
            return;

        int count = detailChunks[chunkPos].Count;

        var results = new NativeArray<RaycastHit>(count, Allocator.TempJob);
        var commands = new NativeArray<RaycastCommand>(count, Allocator.TempJob);

        List<DetailObject> newDetailList = new List<DetailObject>();

        /*
         * This is the best way I could easily ensure the height of detail matches height of terrain
         * We use Unity's job system to raycast downwards from all detail objects to the terrain, 
         * and set the height of the detail object to the hit point.If the hit point is null, 
         * we don't add the detail object to the new list.
         */
        for (int i = 0; i < count; i++)
        {
            DetailObject d = detailChunks[chunkPos][i];
            Vector3 pos = new Vector3(
                d.trs.c3.x,
                1000,
                d.trs.c3.z
            );
            commands[i] = new RaycastCommand(
                pos,
                Vector3.down * 2000,
                QueryParameters.Default
            );
        }


        JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 1,default(JobHandle));

        handle.Complete();


        int index = 0;

        

        ProfilerMarker pm = new ProfilerMarker("UpdateDetailHeight.ResultLoop");
        pm.Begin();
        foreach (var hit in results)
        {
            if (chunk == null || chunk.vertCache == null || chunk.normCache == null || chunk.triCache == null)
            {
                continue;
            }

            DetailObject d = detailChunks[chunkPos][index];
            Vector3 pos = hit.point;
            //Get lossyScale from float4x4
            float3 size = new Vector3(
                math.length(d.trs.c0),
                math.length(d.trs.c1),
                math.length(d.trs.c2)
            );
            float3 n0 = chunk.normCache[chunk.triCache[hit.triangleIndex * 3 + 0]];
            float3 n1 = chunk.normCache[chunk.triCache[hit.triangleIndex * 3 + 1]];
            float3 n2 = chunk.normCache[chunk.triCache[hit.triangleIndex * 3 + 2]];

            float3 baryCenter = hit.barycentricCoordinate;

            float3 interpolatedNormal = math.normalize(n0 * baryCenter.x + n1 * baryCenter.y + n2 * baryCenter.z);

            float4x4 trs = float4x4.TRS(pos + Vector3.up * d.normalOffset, Quaternion.identity, size);
            interpolatedNormal = hit.collider.transform.TransformDirection(interpolatedNormal);

            newDetailList.Add(new DetailObject()
            {
                trs = trs,
                normal = interpolatedNormal,
                normalOffset = d.normalOffset
            });

            
        index++;
        }
        pm.End();


        results.Dispose();
        commands.Dispose();

        detailChunks[chunkPos] = newDetailList;

        UpdateDetailBuffer();
    }

    public void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(totalTerrainSize.center, totalTerrainSize.size);
    }

    public void ClearDetails()
    {
        detailChunks.Clear();
    }

    private void LateUpdate()
    {
        if (argsBuffer == null || detailBuffer == null) {
            InitializeBuffers();
            return;
        }


        Graphics.DrawMeshInstancedIndirect(
            detailMesh,0,detailMaterial,
            new Bounds(Vector3.zero,new Vector3(1000,1000,1000)),
            bufferWithArgs:argsBuffer,argsOffset:0, properties: mpb
        );
    }

    public void UpdateDensity()
    {
        currentDetailDensity = detailDensity;
        detailChunks.Clear();
        UpdateDetailBuffer();
    }

    internal void RemoveDetail(float brushSize, Vector3 mousePosition)
    {

        var chunks = GetChunksAtWorldPosition(mousePosition);
        foreach (var chunk in chunks)
        {
            if (!detailChunks.ContainsKey(chunk.chunkPosition))
                continue;

            float4 mousePos = new float4(mousePosition.x, 0, mousePosition.z,0);

            var inRange = from d in detailChunks[chunk.chunkPosition].AsParallel()
                          where math.distance(new float4(d.trs.c3.x,mousePos.y,d.trs.c3.z,0), mousePos) > brushSize / 2
                          select d;

            detailChunks[chunk.chunkPosition] = inRange.ToList();
        }


        UpdateDetailBuffer();
    }

    void UpdateDirtyChunks()
    {
        foreach (var chunk in chunks)
        {
            if (chunk.Value.IsDirty)
            {
                chunk.Value.RegenerateMesh();
            }
            UpdateDetailHeight(chunk.Value);
        }

        MergeChunks();
    }

    void MergeChunks()
    {
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
        CombineInstance[] combine = new CombineInstance[meshFilters.Length];


        int i = 0;
        while (i < meshFilters.Length)
        {


            if (meshFilters[i].gameObject != gameObject)
            {
                combine[i].mesh = meshFilters[i].sharedMesh;
                combine[i].transform = meshFilters[i].transform.localToWorldMatrix;
                meshFilters[i].gameObject.GetComponent<MeshRenderer>().enabled = false;
            }

            i++;
        }

        Mesh mesh = new Mesh();
        mesh.CombineMeshes(combine);
        mesh.RecalculateNormals(45);

        transform.GetComponent<MeshFilter>().sharedMesh = mesh;
        transform.GetComponent<MeshRenderer>().sharedMaterial = terrainMaterial;

        vertexCache = mesh.vertices;
        normalCache = mesh.normals;
        triangleCache = mesh.triangles;

    }




    internal void DrawHeights(List<Vector3> worldCellPositions,float dragHeight, bool setHeight = false, bool smooth=false)
    {
        foreach (Vector3 worldCell in worldCellPositions)
        {
            List<MarchingSquaresChunk> chunks = GetChunksAtWorldPosition(worldCell);
            foreach(MarchingSquaresChunk chunk in chunks)
            {
                var localPos = new Vector2Int(
                    Mathf.FloorToInt((worldCell.x - chunk.transform.position.x) / cellSize.x),
                    Mathf.FloorToInt((worldCell.z - chunk.transform.position.z) / cellSize.y)
                );
                chunk.DrawHeight(localPos.x, localPos.y, dragHeight, setHeight);
            }
        }

        UpdateDirtyChunks();
    }

    internal void DrawColors(List<Vector3> worldCellPositions,Vector3 paintPos,float brushSize, Color color,bool fallOff)
    {
        AnimationCurve fallOffCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        foreach (Vector3 worldCell in worldCellPositions)
        {
            List<MarchingSquaresChunk> chunks = GetChunksAtWorldPosition(worldCell);
            foreach (MarchingSquaresChunk chunk in chunks)
            {
                var localPos = new Vector2Int(
                    Mathf.FloorToInt((worldCell.x - chunk.transform.position.x) / cellSize.x),
                    Mathf.FloorToInt((worldCell.z - chunk.transform.position.z) / cellSize.y)
                );
                float dist = Vector3.Distance(worldCell, paintPos);
                var t = (brushSize/2 - dist) / brushSize/2;
                Color color1 = color;
                Color color2 = GetColor(worldCell);

                chunk.DrawColor(localPos.x, localPos.y, fallOff ? Color.Lerp(color1,color2,fallOffCurve.Evaluate(t)) : color1);
            }
        }
        UpdateDirtyChunks();
    }


    /// <summary>
    /// Get the chunks at a given cell's world position
    /// </summary>
    /// <param name="worldPosition"></param>
    internal List<MarchingSquaresChunk> GetChunksAtWorldPosition(Vector3 worldCellPosition)
    {
        List<MarchingSquaresChunk> chunksAtPosition = new List<MarchingSquaresChunk>();
        //Loop through every chunk and check if the world position is inside the chunk
        foreach (var chunk in chunks)
        {
            Vector2Int localCellPos = new Vector2Int(
                Mathf.FloorToInt((worldCellPosition.x - chunk.Value.transform.position.x) / cellSize.x),
                Mathf.FloorToInt((worldCellPosition.z - chunk.Value.transform.position.z) / cellSize.y)
            );
            bool inBounds = !(localCellPos.x < 0 || localCellPos.x >= dimensions.x || localCellPos.y < 0 || localCellPos.y >= dimensions.z);
            if (inBounds && !chunksAtPosition.Contains(chunk.Value))
            {
                chunksAtPosition.Add(chunk.Value);
            }
        }

        chunksAtPosition = chunksAtPosition.Distinct().ToList();

        return chunksAtPosition;
    }
    internal Color GetColor(Vector3 worldPos)
    {
        Color color = Color.white;
        List<MarchingSquaresChunk> chunks = GetChunksAtWorldPosition(worldPos);
        foreach (MarchingSquaresChunk chunk in chunks)
        {
            //Get the local cell position
            Vector2Int localCellPos = new Vector2Int(
                Mathf.FloorToInt((worldPos.x - chunk.transform.position.x) / cellSize.x),
                Mathf.FloorToInt((worldPos.z - chunk.transform.position.z) / cellSize.y)
            );
            float4 col = chunk.colorMap[chunk.GetIndex(localCellPos.x, localCellPos.y)];
            color = new Color(
                col.x,
                col.y,
                col.z,
                col.w
            );
        }
        return color;
    }

    internal float GetHeight(Vector3 worldPos, MarchingSquaresChunk c=null)
    {
        //worldPos can actually belong to multiple chunks at once (maximum of 4 if it's in a corner of a chunk)

        float height = 0;
        //The size of one chunk
        Vector3 totalChunkSize= new Vector3(
            (dimensions.x - 1) * cellSize.x,
            0,
            (dimensions.z - 1) * cellSize.y
        );

        List<MarchingSquaresChunk> chunks = GetChunksAtWorldPosition(worldPos);
        foreach (MarchingSquaresChunk chunk in chunks)
        {
            if (c != null)
            {
                if (chunk != c)
                    continue;
            }
            //Get the local cell position
            Vector2Int localCellPos = new Vector2Int(
                Mathf.FloorToInt((worldPos.x - chunk.transform.position.x) / cellSize.x),
                Mathf.FloorToInt((worldPos.z - chunk.transform.position.z) / cellSize.y)
            );
            height = chunk.heightMap[chunk.GetIndex(localCellPos.y, localCellPos.x)];
        }


        return height;
    }

    internal void SetHeight(Vector3 worldPos)
    {
        List<MarchingSquaresChunk> chunks = GetChunksAtWorldPosition(worldPos);
        foreach (MarchingSquaresChunk chunk in chunks)
        {
            //Get the local cell position
            Vector2Int localCellPos = new Vector2Int(
                Mathf.FloorToInt((worldPos.x - chunk.transform.position.x) / cellSize.x),
                Mathf.FloorToInt((worldPos.z - chunk.transform.position.z) / cellSize.y)
            );
            chunk.DrawHeight(localCellPos.x, localCellPos.y, worldPos.y, true);
        }
        UpdateDirtyChunks();
    }

    internal void SmoothHeights(List<Vector3> cells)
    {

        //Store the height values of all cells
        Dictionary<Vector3, List<float>> heights = new Dictionary<Vector3, List<float>>();

        foreach (Vector3 cell in cells)
        {
            List<MarchingSquaresChunk> chunks = GetChunksAtWorldPosition(cell);
            foreach (MarchingSquaresChunk chunk in chunks)
            {
                //Get world position neighbors, this way we can correctly smooth between chunks
                for (int z = -1; z <= 1; z++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        Vector3 neighbor = new Vector3(
                            cell.x + x * cellSize.x,
                            0,
                            cell.z + z * cellSize.y
                        );
                        foreach (MarchingSquaresChunk nChunk in GetChunksAtWorldPosition(neighbor))
                        {
                            Vector2Int localCellPos = new Vector2Int(
                                Mathf.FloorToInt((neighbor.x - nChunk.transform.position.x) / cellSize.x),
                                Mathf.FloorToInt((neighbor.z - nChunk.transform.position.z) / cellSize.y)
                            );
                            float height = nChunk.heightMap[nChunk.GetIndex(localCellPos.y, localCellPos.x)];
                            if (!heights.ContainsKey(cell))
                                heights.Add(cell, new List<float>());
                            else 
                                heights[cell].Add(height);
                        }
                    }
                }
            }
        }

        //Average the heights
        foreach (var cell in heights)
        {
            List<MarchingSquaresChunk> chunks = GetChunksAtWorldPosition(cell.Key);
            foreach (MarchingSquaresChunk chunk in chunks)
            {
                //Get the local cell position
                Vector2Int localCellPos = new Vector2Int(
                    Mathf.FloorToInt((cell.Key.x - chunk.transform.position.x) / cellSize.x),
                    Mathf.FloorToInt((cell.Key.z - chunk.transform.position.z) / cellSize.y)
                );
                List<float> h = cell.Value;
                //Smooth heights
                float curHeight = chunk.heightMap[chunk.GetIndex(localCellPos.y, localCellPos.x)];

                chunk.DrawHeight(
                    localCellPos.x, 
                    localCellPos.y,
                    Mathf.Lerp(curHeight, h.Average(), 0.15f), 
                    true
                );
            }
        }


        UpdateDirtyChunks(); //Regenerate the meshes of all modified chunks
    }

    Texture2D gradientTextureCache = null;
    public void UpdateClouds()
    {
        terrainMaterial.SetFloat("_CloudDensity", cloudDensity);
        terrainMaterial.SetFloat("_CloudSpeedX", cloudSpeed.x);
        terrainMaterial.SetFloat("_CloudSpeedY", cloudSpeed.y);
        terrainMaterial.SetFloat("_CloudScale", cloudScale);
        terrainMaterial.SetFloat("_CloudBrightness", cloudBrightness);
        terrainMaterial.SetFloat("_CloudVerticalSpeed", cloudVerticalSpeed);

        if (detailMaterial)
        {
            detailMaterial.SetFloat("_CloudDensity", cloudDensity);
            detailMaterial.SetFloat("_CloudSpeedX", cloudSpeed.x);
            detailMaterial.SetFloat("_CloudSpeedY", cloudSpeed.y);
            detailMaterial.SetFloat("_CloudScale", cloudScale);
            detailMaterial.SetFloat("_CloudBrightness", cloudBrightness);
            detailMaterial.SetFloat("_CloudVerticalSpeed", cloudVerticalSpeed);
        }
    }
}
