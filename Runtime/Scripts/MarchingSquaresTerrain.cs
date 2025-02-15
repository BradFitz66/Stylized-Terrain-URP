using LibNoise.Operator;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;


public struct TerrainLayer
{
    public Texture2D cliffTexture;
    public Texture2D groundTexture;
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
}
[ExecuteInEditMode]
public class MarchingSquaresTerrain : MonoBehaviour
{

    ComputeBuffer detailBuffer;
    ComputeBuffer argsBuffer;
    List<DetailObject> allDetail = new List<DetailObject>();
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
        args = new uint[5] { detailMesh.GetIndexCount(0), 0, detailMesh.GetIndexStart(0), detailMesh.GetBaseVertex(0), 0 };
        if (argsBuffer == null)
        {
            argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
            argsBuffer.SetData(args);
        }
        mpb = new MaterialPropertyBlock();
    }

    void InitializeBuffers()
    {
        mpb = new MaterialPropertyBlock();
        argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        args[1] = (uint)allDetail.Count;
        argsBuffer.SetData(args);
    }

    public void UpdateDetailBuffer()
    {
        if (mpb == null)
            mpb = new MaterialPropertyBlock();
        if (allDetail.Count == 0)
            return;

        detailBuffer?.Release();
        detailBuffer = new ComputeBuffer(allDetail.Count, Marshal.SizeOf<DetailObject>());
        
        detailBuffer.SetData(allDetail.ToArray());
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

    private void OnEnable()
    {
        UpdateDetailBuffer();
    }

    public void AddDetail(int x, int y, Vector3 detailPos,MarchingSquaresChunk chunk)
    {
        //Return if the detail is out of bounds
        if (!totalTerrainSize.Contains(detailPos))
            return;

        bool canPlace = true;
        Parallel.ForEach(allDetail, (d) =>
        {
            Vector3 dPosXZ = new Vector3(detailPos.x, 0, detailPos.z);
            Vector3 detailPosXZ = new Vector3(d.trs.GetColumn(3).x, 0, d.trs.GetColumn(3).z);
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

        float height = chunk.heightMap[chunk.getIndex(cellPos.y, cellPos.x)];

        detailPos.y = height;

        Matrix4x4 trs = Matrix4x4.TRS(detailPos, Quaternion.identity, Vector3.one * 10);
        DetailObject detailObject = new DetailObject()
        {
            trs = trs
        };

        

        //Ensure density

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
                int idx1 = chunk.getIndex(z, 0);
                int idx2 = leftChunk.getIndex(z, dimensions.x - 1);
                chunk.drawHeight(0, z, leftChunk.heightMap[idx2]);
            }
        }
        var chunkRight = chunks.TryGetValue(new Vector2Int(chunkX + 1, chunkY), out var rightChunk);
        if (chunkRight)
        {
            for (int z = 0; z < dimensions.z; z++)
            {
                int idx1 = chunk.getIndex(z, dimensions.x - 1);
                int idx2 = rightChunk.getIndex(z, 0);
                chunk.drawHeight(dimensions.x - 1, z, rightChunk.heightMap[idx2]);
            }
        }
        var chunkUp = chunks.TryGetValue(new Vector2Int(chunkX, chunkY + 1), out var upChunk);
        if (chunkUp)
        {
            for (int x = 0; x < dimensions.x; x++)
            {
                int idx1 = chunk.getIndex(dimensions.z - 1, x);
                int idx2 = upChunk.getIndex(0, x);
                chunk.drawHeight(x, dimensions.z - 1, upChunk.heightMap[idx2]);

            }
        }
        var chunkDown = chunks.TryGetValue(new Vector2Int(chunkX, chunkY - 1), out var downChunk);
        if (chunkDown)
        {
            for (int x = 0; x < dimensions.x; x++)
            {
                int idx1 = chunk.getIndex(0, x);
                int idx2 = downChunk.getIndex(dimensions.z - 1, x);
                chunk.drawHeight(x, 0, downChunk.heightMap[idx2]);
            }
        }



        chunk.regenerateMesh();
    }

    public void GenerateTerrain()
    {
        foreach (var chunk in chunks)
        {
            chunk.Value.GenerateHeightmap(
                noiseSettings
            );
        }

        UpdateDetailBuffer();

    }
    public void RemoveChunk(Vector2Int coords)
    {
        if (chunks.ContainsKey(coords))
        {
            if (chunks[coords] != null)
                DestroyImmediate(chunks[coords].gameObject);
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
        chunk.initializeTerrain();
    }

    
    public void SetColor(Vector2Int chunk, int cx, int cz, Color color)
    {
        MarchingSquaresChunk c = chunks[chunk];
        c.drawColor(cx, cz, color);

        var chunkLeft = chunks.TryGetValue(new Vector2Int(chunk.x - 1, chunk.y), out var leftChunk) && cx == 0;
        if (chunkLeft)
        {
            if (leftChunk.colorMap[leftChunk.getIndex(dimensions.x - 1, (int)cz)] != color)
            {
                leftChunk.drawColor(dimensions.x - 1, cz, color);
            }
        }
        var chunkRight = chunks.TryGetValue(new Vector2Int(chunk.x + 1, chunk.y), out var rightChunk) && cx == dimensions.x - 1;
        if (chunkRight)
        {
            if (rightChunk.colorMap[rightChunk.getIndex(0, (int)cz)] != color)
            {
                rightChunk.drawColor(0, cz, color);
            }
        }

        var chunkUp = chunks.TryGetValue(new Vector2Int(chunk.x, chunk.y + 1), out var upChunk) && cz == dimensions.z - 1;
        if (chunkUp)
        {
            if (upChunk.colorMap[upChunk.getIndex((int)cx, 0)] != color)
            {
                upChunk.drawColor(cx, 0, color);
            }
        }

        var chunkDown = chunks.TryGetValue(new Vector2Int(chunk.x, chunk.y - 1), out var downChunk) && cz == 0;
        if (chunkDown)
        {
            if (downChunk.colorMap[downChunk.getIndex((int)cx, dimensions.z - 1)] != color)
            {
                downChunk.drawColor(cx, dimensions.z - 1, color);
            }
        }

        var chunkUpright = chunks.TryGetValue(new Vector2Int(chunk.x + 1, chunk.y + 1), out var upRightChunk) && cx == dimensions.x - 1 && cz == dimensions.z - 1;
        if (chunkUpright)
        {
            if (upRightChunk.colorMap[upRightChunk.getIndex(0, 0)] != color)
            {
                upRightChunk.drawColor(0, 0, color);
            }
        }

        var chunkUpleft = chunks.TryGetValue(new Vector2Int(chunk.x - 1, chunk.y + 1), out var upLeftChunk) && cx == 0 && cz == dimensions.z - 1;
        if (chunkUpleft)
        {
            if (upLeftChunk.colorMap[upLeftChunk.getIndex(dimensions.z - 1, 0)] != color)
            {
                upLeftChunk.drawColor(dimensions.z - 1, 0, color);
            }
        }

        var chunkDownright = chunks.TryGetValue(new Vector2Int(chunk.x + 1, chunk.y - 1), out var downRightChunk) && cx == dimensions.x - 1 && cz == 0;
        if (chunkDownright)
        {
            if (downRightChunk.colorMap[downRightChunk.getIndex(0, dimensions.x - 1)] != color)
            {
                downRightChunk.drawColor(0, dimensions.x - 1, color);
            }
        }

        var chunkDownleft = chunks.TryGetValue(new Vector2Int(chunk.x - 1, chunk.y - 1), out var downLeftChunk) && cx == 0 && cz == 0;
        if (chunkDownleft)
        {
            if (downLeftChunk.colorMap[downLeftChunk.getIndex(dimensions.z - 1, dimensions.x - 1)] != color)
            {
                downLeftChunk.drawColor(dimensions.z - 1, dimensions.x - 1, color);
            }
        }

    }
    public void SetHeight(Vector2Int chunk, int cx, int cz, float height)
    {

        MarchingSquaresChunk c = chunks[chunk];
        c.drawHeight(cx, cz, height);


        #region Update Neighbors
        //Do we have a chunk to the left and are we setting a cell on the left edge?
        var chunkLeft = chunks.TryGetValue(new Vector2Int(chunk.x - 1, chunk.y), out var leftChunk) && cx == 0;
        if (chunkLeft)
        {
            if (leftChunk.heightMap[leftChunk.getIndex(cz, dimensions.x - 1)] != height)
            {
                leftChunk.drawHeight(dimensions.x - 1, cz, height);
            }
        }

        var chunkRight = chunks.TryGetValue(new Vector2Int(chunk.x + 1, chunk.y), out var rightChunk) && cx == dimensions.x - 1;
        if (chunkRight)
        {
            if (rightChunk.heightMap[rightChunk.getIndex(cz, 0)] != height)
            {
                rightChunk.drawHeight(0, cz, height);
            }
        }

        var chunkUp = chunks.TryGetValue(new Vector2Int(chunk.x, chunk.y + 1), out var upChunk) && cz == dimensions.z - 1;
        if (chunkUp)
        {
            if (upChunk.heightMap[upChunk.getIndex(0, cx)] != height)
            {
                upChunk.drawHeight(cx, 0, height);
            }
        }


        var chunkDown = chunks.TryGetValue(new Vector2Int(chunk.x, chunk.y - 1), out var downChunk) && cz == 0;
        if (chunkDown)
        {
            if (downChunk.heightMap[downChunk.getIndex(dimensions.z - 1, cx)] != height)
            {
                downChunk.drawHeight(cx, dimensions.z - 1, height);
            }
        }

        var chunkUpright = chunks.TryGetValue(new Vector2Int(chunk.x + 1, chunk.y + 1), out var upRightChunk) && cx == dimensions.x - 1 && cz == dimensions.z - 1;
        if (chunkUpright)
        {
            if (upRightChunk.heightMap[upRightChunk.getIndex(0, 0)] != height)
            {
                upRightChunk.drawHeight(0, 0, height);
            }
        }

        var chunkUpleft = chunks.TryGetValue(new Vector2Int(chunk.x - 1, chunk.y + 1), out var upLeftChunk) && cx == 0 && cz == dimensions.z - 1;
        if (chunkUpleft)
        {
            if (upLeftChunk.heightMap[upLeftChunk.getIndex(dimensions.z - 1, 0)] != height)
            {
                upLeftChunk.drawHeight(dimensions.z - 1, 0, height);
            }
        }

        var chunkDownright = chunks.TryGetValue(new Vector2Int(chunk.x + 1, chunk.y - 1), out var downRightChunk) && cx == dimensions.x - 1 && cz == 0;
        if (chunkDownright)
        {
            if (downRightChunk.heightMap[downRightChunk.getIndex(0, dimensions.x - 1)] != height)
            {
                downRightChunk.drawHeight(0, dimensions.x - 1, height);
            }
        }
        #endregion

        UpdateDetailHeight();

    }

    void UpdateDetailHeight()
    {
        List<DetailObject> newDetailList = new List<DetailObject>();

        //Use unity jobs system to do a raycast for each detail object
        var results = new NativeArray<RaycastHit>(allDetail.Count, Allocator.TempJob);
        var commands = new NativeArray<RaycastCommand>(allDetail.Count, Allocator.TempJob);

        //Prepare raycast commands
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

        //Execute raycasts
        JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 1,default(JobHandle));

        handle.Complete();
        foreach (var hit in results) { 
            if (hit.collider != null)
            {
                Vector3 pos = hit.point;
                Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * 10);
                newDetailList.Add(new DetailObject()
                {
                    trs = trs
                });
            }
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
        UpdateDetailBuffer();
    }
    
    private void LateUpdate()
    {
        if (argsBuffer == null || detailBuffer == null) {
            InitializeBuffers();
            UpdateDetailBuffer();
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
        print(args[1]);
    }

    public void UpdateDensity()
    {
        currentDetailDensity = detailDensity;
        allDetail.Clear();
        UpdateDetailBuffer();
    }
}
