using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[System.Serializable]
public struct DetailObject
{
    public Matrix4x4 trs;
    public Vector3 normal;
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
    public float frequency;
    public float amplitude;
    public float lacunarity;
    public float persistence;
    public int octaves;
    public Vector2 offset;
    public int seed;
    public NoiseMixMode mixMode;
}
[ExecuteInEditMode]
public class MarchingSquaresTerrain : MonoBehaviour
{

    ComputeBuffer detailBuffer;
    ComputeBuffer argsBuffer;
    [SerializeField]
    List<DetailObject> allDetail;
    uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    MaterialPropertyBlock mpb;

    

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

    private void Awake()
    {

        args = new uint[5] { detailMesh.GetIndexCount(0), (uint)allDetail.Count, detailMesh.GetIndexStart(0), detailMesh.GetBaseVertex(0), 0 };
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
            detailBuffer.SetData(allDetail.ToArray());
        }
    }

    void InitializeBuffers()
    {
        mpb = new MaterialPropertyBlock();
        argsBuffer?.Release();
        detailBuffer?.Release();
        argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        args[1] = (uint)allDetail.Count;
        argsBuffer.SetData(args);
    }



    // Taken from:
    // http://extremelearning.com.au/unreasonable-effectiveness-of-quasirandom-sequences/
    // https://www.shadertoy.com/view/4dtBWH
    private Vector2 Nth_weyl(Vector2 p0, float n)
    {
        Vector2 res = p0 + n * new Vector2(0.754877669f, 0.569840296f);
        res.x %= 1;
        res.y %= 1;
        return res;
    }

    public void UpdateDetailBuffer()
    {
        if (mpb == null)
            mpb = new MaterialPropertyBlock();
        if (allDetail.Count == 0)
            return;
        if (detailBuffer == null)
            detailBuffer = new ComputeBuffer(1000000, Marshal.SizeOf(typeof(DetailObject)));

        //Append new data to the buffer
        detailBuffer.SetData(
            allDetail.ToArray(),
            0, 
            0, 
            allDetail.Count
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

    public void AddDetail(float size,float normalOffset,Vector3 detailPos,MarchingSquaresChunk chunk)
    {
        //Return if the detail is out of bounds
        if (!totalTerrainSize.Contains(detailPos))
            return;

        bool canPlace = true;
        Parallel.ForEach(allDetail, (d) =>
        {
            Vector3 dPosXZ = new Vector3(detailPos.x, 0, detailPos.z);
            
            Vector3 detailPosXZ = new Vector3(d.trs.GetPosition().x, 0, d.trs.GetPosition().z);
            if (Vector3.Distance(detailPosXZ, dPosXZ) <= currentDetailDensity)
            {
                canPlace = false;
                return;
            }
        });


        if (!canPlace)
            return;

        //Round detailPos to the nearest cell
        Vector2Int cellPos = new Vector2Int(
            Mathf.FloorToInt((detailPos.x - chunk.transform.position.x) / cellSize.x),
            Mathf.FloorToInt((detailPos.z - chunk.transform.position.z) / cellSize.y)
        );

        float height = chunk.heightMap[chunk.GetIndex(cellPos.y, cellPos.x)];

        detailPos.y = height + normalOffset;

        Matrix4x4 trs = Matrix4x4.TRS(detailPos, Quaternion.identity, Vector3.one * size);
        DetailObject detailObject = new DetailObject()
        {
            trs = trs,
            normal = Vector3.up,
            normalOffset = normalOffset//Unused in shader
        };

        


        allDetail.Add(detailObject);
        UpdateDetailHeight();
    }


    public void AddNewChunk(int chunkX, int chunkY)
    {
        var chunkCoords = new Vector2Int(chunkX, chunkY);
        var newChunk = new GameObject("Chunk " + chunkCoords);

        MarchingSquaresChunk chunk = newChunk.AddComponent<MarchingSquaresChunk>();
        newChunk.AddComponent<MeshFilter>();
        MeshRenderer mr = newChunk.AddComponent<MeshRenderer>();
        newChunk.AddComponent<MeshCollider>();
        newChunk.layer = gameObject.layer;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        newChunk.isStatic = true;

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
    }

    public void GenerateTerrain()
    {
        foreach (var chunk in chunks)
        {
            chunk.Value.GenerateHeightmap(
                noiseSettings
            );
        }

        UpdateDetailHeight();

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



        UpdateDetailHeight();
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

    
    public void SetColor(Vector2Int chunk, int cx, int cz, Vector4 color)
    {
        MarchingSquaresChunk c = chunks[chunk];
        c.DrawColor(cx, cz, color);

        var chunkLeft = chunks.TryGetValue(new Vector2Int(chunk.x - 1, chunk.y), out var leftChunk) && cx == 0;
        if (chunkLeft)
        {
            if (leftChunk.colorMap[leftChunk.GetIndex(dimensions.x - 1, (int)cz)] != color)
            {
                leftChunk.DrawColor(dimensions.x - 1, cz, color);
            }
        }
        var chunkRight = chunks.TryGetValue(new Vector2Int(chunk.x + 1, chunk.y), out var rightChunk) && cx == dimensions.x - 1;
        if (chunkRight)
        {
            if (rightChunk.colorMap[rightChunk.GetIndex(0, (int)cz)] != color)
            {
                rightChunk.DrawColor(0, cz, color);
            }
        }

        var chunkUp = chunks.TryGetValue(new Vector2Int(chunk.x, chunk.y + 1), out var upChunk) && cz == dimensions.z - 1;
        if (chunkUp)
        {
            if (upChunk.colorMap[upChunk.GetIndex((int)cx, 0)] != color)
            {
                upChunk.DrawColor(cx, 0, color);
            }
        }

        var chunkDown = chunks.TryGetValue(new Vector2Int(chunk.x, chunk.y - 1), out var downChunk) && cz == 0;
        if (chunkDown)
        {
            if (downChunk.colorMap[downChunk.GetIndex((int)cx, dimensions.z - 1)] != color)
            {
                downChunk.DrawColor(cx, dimensions.z - 1, color);
            }
        }

        var chunkUpright = chunks.TryGetValue(new Vector2Int(chunk.x + 1, chunk.y + 1), out var upRightChunk) && cx == dimensions.x - 1 && cz == dimensions.z - 1;
        if (chunkUpright)
        {
            if (upRightChunk.colorMap[upRightChunk.GetIndex(0, 0)] != color)
            {
                upRightChunk.DrawColor(0, 0, color);
            }
        }

        var chunkUpleft = chunks.TryGetValue(new Vector2Int(chunk.x - 1, chunk.y + 1), out var upLeftChunk) && cx == 0 && cz == dimensions.z - 1;
        if (chunkUpleft)
        {
            if (upLeftChunk.colorMap[upLeftChunk.GetIndex(dimensions.z - 1, 0)] != color)
            {
                upLeftChunk.DrawColor(dimensions.z - 1, 0, color);
            }
        }

        var chunkDownright = chunks.TryGetValue(new Vector2Int(chunk.x + 1, chunk.y - 1), out var downRightChunk) && cx == dimensions.x - 1 && cz == 0;
        if (chunkDownright)
        {
            if (downRightChunk.colorMap[downRightChunk.GetIndex(0, dimensions.x - 1)] != color)
            {
                downRightChunk.DrawColor(0, dimensions.x - 1, color);
            }
        }

        var chunkDownleft = chunks.TryGetValue(new Vector2Int(chunk.x - 1, chunk.y - 1), out var downLeftChunk) && cx == 0 && cz == 0;
        if (chunkDownleft)
        {
            if (downLeftChunk.colorMap[downLeftChunk.GetIndex(dimensions.z - 1, dimensions.x - 1)] != color)
            {
                downLeftChunk.DrawColor(dimensions.z - 1, dimensions.x - 1, color);
            }
        }

    }
   
    void UpdateDetailHeight()
    {
        List<DetailObject> newDetailList = new List<DetailObject>();

        /*
         * This is the best way I could easily ensure the height of detail matches height of terrain
         * We use Unity's job system to raycast downwards from all detail objects to the terrain, 
         * and set the height of the detail object to the hit point.If the hit point is null, 
         * we don't add the detail object to the new list.
         */

        var results = new NativeArray<RaycastHit>(allDetail.Count, Allocator.TempJob);
        var commands = new NativeArray<RaycastCommand>(allDetail.Count, Allocator.TempJob);

        for (int i = 0; i < allDetail.Count; i++)
        {
            DetailObject d = allDetail[i];
            Vector3 pos = new Vector3(
                d.trs.GetColumn(3).x,
                1000,
                d.trs.GetColumn(3).z
            );
            commands[i] = new RaycastCommand(
                pos,
                Vector3.down*2000,
                QueryParameters.Default
            );
        }

        JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 1,default(JobHandle));

        handle.Complete();

        int index = 0;
        foreach (var hit in results) { 
            if (hit.collider != null)
            {
                DetailObject d = allDetail[index];
                Vector3 pos = hit.point;
                Vector3 size = d.trs.lossyScale;
                Matrix4x4 trs = Matrix4x4.TRS(pos + Vector3.up * d.normalOffset, Quaternion.identity, size);
                newDetailList.Add(new DetailObject()
                {
                    trs = trs,
                    normal = hit.normal,
                    normalOffset = d.normalOffset //Unused in shader. 
                });
            }
            index++;
        }

        results.Dispose();
        commands.Dispose();

        allDetail = newDetailList;

        UpdateDetailBuffer();
    }

    public void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(totalTerrainSize.center, totalTerrainSize.size);
    }

    public void ClearDetails()
    {
        allDetail.Clear();
        UpdateDetailHeight();
    }

    private void LateUpdate()
    {
        if (argsBuffer == null || detailBuffer == null) {
            InitializeBuffers();
            UpdateDetailHeight();
            return;
        }



        args[0] = detailMesh.GetIndexCount(0);
        args[1] = (uint)allDetail.Count;
        args[2] = detailMesh.GetIndexStart(0);
        args[3] = detailMesh.GetBaseVertex(0);


        argsBuffer.SetData(args);

        Graphics.DrawMeshInstancedIndirect(
            detailMesh,0,detailMaterial,
            new Bounds(Vector3.zero,new Vector3(1000,1000,1000)),
            bufferWithArgs:argsBuffer,argsOffset:0, properties: mpb
        );
    }

    public void UpdateDensity()
    {
        currentDetailDensity = detailDensity;
        allDetail.Clear();
        UpdateDetailBuffer();
    }

    internal void RemoveDetail(float brushSize, Vector3 mousePosition)
    {

        //Get all details that are not inside the brush's radius
        var inRange = from d in allDetail.AsParallel()
                      where Vector3.Distance(d.trs.GetPosition(), mousePosition) > brushSize/2
                      select d;

        allDetail = inRange.ToList();

        UpdateDetailBuffer();
    }

    

    internal void DrawHeights(List<Vector2Int> cellPositions, MarchingSquaresChunk chunk,float dragHeight, bool setHeight = false)
    {
        chunk.DrawHeights(cellPositions, dragHeight, setHeight);
        UpdateDetailHeight();
    }

    internal void SmoothHeights(List<Vector2Int> localCells, MarchingSquaresChunk c, float setHeight, bool v)
    {
        c.SmoothHeights(localCells, setHeight, v);
        UpdateDetailHeight();
    }
}
