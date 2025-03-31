using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;


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

[Serializable]
public struct CloudSettings
{
    public float density;
    public Vector3 speed;
    public float scale;
    public float brightness;

    //Operator for checking equality
    public static bool operator ==(CloudSettings a, CloudSettings b)
    {
        return a.GetHashCode() == b.GetHashCode();
    }

    public static bool operator !=(CloudSettings a, CloudSettings b)
    {
        return a.GetHashCode() != b.GetHashCode();
    }

    //Hash function
    public override int GetHashCode()
    {
        return density.GetHashCode() ^ speed.GetHashCode() ^ scale.GetHashCode() ^ brightness.GetHashCode();
    }
}



[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
[ExecuteInEditMode]
public class MarchingSquaresTerrain : MonoBehaviour
{

    ComputeBuffer detailBuffer;
    ComputeBuffer argsBuffer;
    ComputeBuffer voteBuffer;
    ComputeBuffer scanBuffer;
    ComputeBuffer groupSumArrayBuffer;
    ComputeBuffer scannedGroupSumBuffer;
    ComputeBuffer resultBuffer;

    private int numThreadGroups;
    private int numVoteThreadGroups;
    private int numGroupScanThreadGroups;


    //List<DetailObject> allDetail;



    uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    MaterialPropertyBlock mpb;


    public Vector3Int dimensions = new Vector3Int(10, 10, 10);
    public Bounds totalTerrainSize
    {
        get
        {
            Bounds cB = new Bounds();
            if (GetComponent<MeshFilter>().sharedMesh != null) {
                cB = GetComponent<MeshFilter>().sharedMesh.bounds;
            }
            return cB;
        }
    }
    public Vector2 cellSize = new Vector2(2, 2);
    public Material terrainMaterial;

    public float mergeThreshold = 0.6f;
    public float heightBanding = 0.25f;

    public List<Vector2Int> chunkKeys = new();
    public List<MarchingSquaresChunk> chunkValues = new();

    [HideInInspector]
    public SerializedDictionary<Vector2Int, MarchingSquaresChunk> chunks = new();

    public InstancingData instancingData;


    public Texture2D[] groundTextures = new Texture2D[4];
    public NoiseSettings noiseSettings;

    public int selectedTool = 0;
    public TerrainTool[] tools;
    public TerrainTool currentTool;
    public TerrainTool lastTool;

    public int id = -100;

    public Mesh detailMesh;
    public Material detailMaterial;

    public float detailDensity = 0.1f; //Minimum distance between details
    public float currentDetailDensity = 0.1f;

    Camera camera;

    bool useCulling = false;

    public string GUID = System.Guid.NewGuid().ToString();

    ComputeShader shader;


    int[] triCache;
    Vector3[] normCache;

    public CloudSettings cloudSettings = new CloudSettings()
    {
        density = 0.1f,
        speed = new Vector3(4f, 4f, 0.005f),
        scale = 0.05f,
        brightness = 0f
    };
#region MonoBehaviour Functions
    private void Awake()
    {
        CreateOrLoadInstanceData();
        useCulling = !(Application.isEditor && !Application.isPlaying);
        UpdateDetailBuffer();
    }

    private void OnValidate()
    {
    }

    private void OnEnable()
    {

        CreateOrLoadInstanceData();

        useCulling = !(Application.isEditor && !Application.isPlaying);
        shader = Resources.Load<ComputeShader>("Shaders/Culling");

        UpdateDetailBuffer();

        if (resultBuffer == null || voteBuffer == null || scanBuffer == null || groupSumArrayBuffer == null || scannedGroupSumBuffer == null)
        {
            voteBuffer = new ComputeBuffer(30000, sizeof(uint), ComputeBufferType.Append);
            scanBuffer = new ComputeBuffer(30000, sizeof(uint), ComputeBufferType.Append);
            groupSumArrayBuffer = new ComputeBuffer(30000, sizeof(uint), ComputeBufferType.Append);
            scannedGroupSumBuffer = new ComputeBuffer(30000, sizeof(uint), ComputeBufferType.Append);
            resultBuffer = new ComputeBuffer(30000, sizeof(uint), ComputeBufferType.Append);
        }
    }

    //Cleanup
    private void OnDestroy()
    {
        if (argsBuffer != null)
            argsBuffer.Release();
        if (detailBuffer != null)
            detailBuffer.Release();
    }

    public void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(totalTerrainSize.center, totalTerrainSize.size);
    }

    CloudSettings lastCloudSettings;
    private void Update()
    {
        if (argsBuffer == null || args == null || detailBuffer == null)
        {
            InitializeBuffers();
            return;
        }

        if (camera == null)
        {
            if (Application.isPlaying)
                camera = Camera.main;
        }

        if (lastCloudSettings != cloudSettings)
        {
            lastCloudSettings = cloudSettings;
            UpdateClouds();
        }

        if (camera != null && useCulling)
            Dispatch(transform.position, camera.transform.position, camera.projectionMatrix * camera.worldToCameraMatrix);

        Graphics.DrawMeshInstancedIndirect(
            detailMesh, 0, detailMaterial,
            new Bounds(Vector3.zero, new Vector3(10000, 10000, 10000)),
            bufferWithArgs: argsBuffer, argsOffset: 0, properties: mpb
        );
    }
#endregion
#region Detail Functions
    public void GenerateGrass(float size, float normalOffset)
    {
        //Sanity checking to ensure instancingData is never null when doing stuff related to detail.
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }
        ClearDetails();
        NativeList<float2> results = new NativeList<float2>(Allocator.TempJob);
        float2 bottomLeft = new float2(totalTerrainSize.min.x, totalTerrainSize.min.z);
        float2 topRight = new float2(totalTerrainSize.max.x, totalTerrainSize.max.z);
        float minimumDistance = detailDensity;
        PoissonJob job = new PoissonJob(
            results,
            bottomLeft,
            topRight,
            minimumDistance
        );
        JobHandle handle = job.Schedule();
        handle.Complete();

        foreach (float2 pos in results)
        {
            var chunks = GetChunksAtWorldPosition(new Vector3(pos.x, 0, pos.y));
            foreach (var chunk in chunks)
            {
                if (!instancingData.detailChunks.ContainsKey(chunk.chunkPosition))
                    instancingData.detailChunks.Add(chunk.chunkPosition, new List<DetailObject>());
                Matrix4x4 trs = Matrix4x4.TRS(new Vector3(pos.x, 0, pos.y), Quaternion.identity, Vector3.one * size);
                DetailObject detailObject = new DetailObject()
                {
                    trs = trs,
                    normal = Vector3.up,
                    normalOffset = normalOffset,
                };
                instancingData.detailChunks[chunk.chunkPosition].Add(detailObject);
            }
        }

        foreach (var chunk in chunks)
        {
            UpdateDetailHeight(chunk.Value);
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(instancingData);
#endif

        results.Dispose();

    }

    void InitializeBuffers()
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }
        argsBuffer?.Release();
        detailBuffer?.Release();
        args = new uint[5] { detailMesh.GetIndexCount(0), (uint)instancingData.GetDetailCount(), detailMesh.GetIndexStart(0), detailMesh.GetBaseVertex(0), 0 };

        if (mpb == null)
        {
            mpb = new MaterialPropertyBlock();//
        }
        if (detailBuffer == null || voteBuffer == null || scanBuffer == null || groupSumArrayBuffer == null || scannedGroupSumBuffer == null || resultBuffer == null)
        {
            var data = instancingData.GetDetailData();
            detailBuffer = new ComputeBuffer(300000, Marshal.SizeOf<DetailObject>()); //Preallocate 1million details
            voteBuffer = new ComputeBuffer(300000, 4, ComputeBufferType.Append);
            scanBuffer = new ComputeBuffer(300000, 4, ComputeBufferType.Append);
            groupSumArrayBuffer = new ComputeBuffer(300000, 4, ComputeBufferType.Append);
            scannedGroupSumBuffer = new ComputeBuffer(300000, 4, ComputeBufferType.Append);
            resultBuffer = new ComputeBuffer(300000, Marshal.SizeOf<DetailObject>(), ComputeBufferType.Append);
            detailBuffer.SetData(
                data,
                0,
                0,
                data.Length
            );

            mpb?.SetBuffer("_TerrainDetail", useCulling ? resultBuffer : detailBuffer);
        }

        mpb = new MaterialPropertyBlock();

        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);
    }

    public void UpdateDetailBuffer()
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }
        var data = instancingData.GetDetailData();

        if (argsBuffer == null)
            InitializeBuffers();

        args[1] = (uint)data.Length;
        argsBuffer.SetData(args);

        if (mpb == null)
            mpb = new MaterialPropertyBlock();

        if (data.Length == 0)
            return;

        print("Updating detail buffer");

        if (detailBuffer == null)
            detailBuffer = new ComputeBuffer(300000, Marshal.SizeOf(typeof(DetailObject)));

        numThreadGroups = Mathf.CeilToInt(data.Length / 128.0f);
        if (numThreadGroups > 128)
        {
            int powerOfTwo = 128;
            while (powerOfTwo < numThreadGroups)
                powerOfTwo *= 2;

            numThreadGroups = powerOfTwo;
        }
        else
        {
            while (128 % numThreadGroups != 0)
                numThreadGroups++;
        }

        numVoteThreadGroups = Mathf.CeilToInt(data.Length / 128.0f);
        numGroupScanThreadGroups = Mathf.CeilToInt(data.Length / 1024.0f);

        print("Updating detail buffer");

        detailBuffer.SetData(
            data,
            0,
            0,
            data.Length
        );

        mpb?.SetBuffer("_TerrainDetail", useCulling ? resultBuffer : detailBuffer);
    }
    public void AddDetail(float size, float normalOffset, float3 detailPos, MarchingSquaresChunk chunk, bool distanceCheck = true)
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }

        //Return if the detail is out of bounds
        if (!totalTerrainSize.Contains(detailPos))
        {
            return;
        }

        var chunks = GetChunksAtWorldPosition(detailPos);
        if (chunks.Count == 0)
            return;

        MarchingSquaresChunk c = chunks[0];

        bool canPlace = true;
        foreach (var otherChunk in chunks)
        {
            if (!instancingData.detailChunks.ContainsKey(otherChunk.chunkPosition))
                instancingData.detailChunks.Add(otherChunk.chunkPosition, new List<DetailObject>());
            if (distanceCheck)
            {
                Parallel.ForEach(instancingData.detailChunks[otherChunk.chunkPosition], (d) =>
                {
                    if (math.distance(new float3(d.trs.c3.x, detailPos.y, d.trs.c3.z), detailPos) < currentDetailDensity)
                    {
                        canPlace = false;
                    }
                });
            }
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

        if (!instancingData.detailChunks.ContainsKey(c.chunkPosition))
            instancingData.detailChunks.Add(c.chunkPosition, new List<DetailObject>());

        instancingData.detailChunks[c.chunkPosition].Add(detailObject);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(instancingData);
#endif

        UpdateDetailHeight(c);
    }

    void UpdateDetailHeight(MarchingSquaresChunk chunk)
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }
        Vector2Int chunkPos = chunk.chunkPosition;
        if (!instancingData.detailChunks.ContainsKey(chunkPos))
            return;

        int count = instancingData.detailChunks[chunkPos].Count;

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
            DetailObject d = instancingData.detailChunks[chunkPos][i];
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


        JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 1, default(JobHandle));

        handle.Complete();


        int index = 0;



        foreach (var hit in results)
        {
            if (chunk == null)
            {
                index++;
                continue;
            }
            if (normCache == null || triCache == null)
            {
                normCache = GetComponent<MeshFilter>().sharedMesh.normals;
                triCache = GetComponent<MeshFilter>().sharedMesh.triangles;
            }

            DetailObject d = instancingData.detailChunks[chunkPos][index];
            Vector3 pos = hit.point;
            //Get lossyScale from float4x4
            float3 size = new Vector3(
                math.length(d.trs.c0),
                math.length(d.trs.c1),
                math.length(d.trs.c2)
            );

            int triIdx1 = hit.triangleIndex * 3;
            int triIdx2 = hit.triangleIndex * 3 + 1;
            int triIdx3 = hit.triangleIndex * 3 + 2;
            if (triIdx1 < 0 || triIdx2 < 0 || triIdx3 < 0 || triIdx1 >= triCache.Length || triIdx2 >= triCache.Length || triIdx3 >= triCache.Length)
            {
                index++;
                continue;
            }
            int tri1 = triCache[triIdx1];
            int tri2 = triCache[triIdx2];
            int tri3 = triCache[triIdx3];


            Vector3 n0 = normCache[tri1];
            Vector3 n1 = normCache[tri2];
            Vector3 n2 = normCache[tri3];

            Vector3 baryCenter = hit.barycentricCoordinate;
            Vector3 interpolatedNormal = n0 * baryCenter.x + n1 * baryCenter.y + n2 * baryCenter.z;

            interpolatedNormal = interpolatedNormal.normalized;
            interpolatedNormal = hit.transform.TransformDirection(interpolatedNormal);
            float4x4 trs = float4x4.TRS(pos + Vector3.up * d.normalOffset, Quaternion.identity, size);

            newDetailList.Add(new DetailObject()
            {
                trs = trs,
                normal = interpolatedNormal,
                normalOffset = d.normalOffset
            });


            index++;
        }


        results.Dispose();
        commands.Dispose();

        instancingData.detailChunks[chunkPos] = newDetailList;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(instancingData);
#endif

        UpdateDetailBuffer();
    }
    public void UpdateDensity()
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }
        currentDetailDensity = detailDensity;
        instancingData.detailChunks.Clear();
        UpdateDetailBuffer();
    }

    public void ClearDetails()
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }
        instancingData.detailChunks.Clear();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(instancingData);
#endif
        UpdateDetailBuffer();
    }
    void Dispatch(Vector3 position, Vector3 cameraPos, Matrix4x4 VP)
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }
        args[1] = 0;
        argsBuffer.SetData(args);

        if (shader == null)
            shader = Resources.Load<ComputeShader>("Shaders/Culling");

        if (numGroupScanThreadGroups == 0 || numThreadGroups == 0 || numVoteThreadGroups == 0)
            return;

        shader.SetVector("_ObjectPosition", position);
        shader.SetMatrix("MATRIX_VP", VP);
        shader.SetBuffer(0, "_GrassDataBuffer", detailBuffer);
        shader.SetBuffer(0, "_VoteBuffer", voteBuffer);
        shader.SetVector("_CameraPosition", cameraPos);
        shader.SetFloat("_Distance", 5000);
        shader.Dispatch(0, numVoteThreadGroups, 1, 1);

        shader.SetBuffer(1, "_VoteBuffer", voteBuffer);
        shader.SetBuffer(1, "_ScanBuffer", scanBuffer);
        shader.SetBuffer(1, "_GroupSumArray", groupSumArrayBuffer);
        shader.Dispatch(1, numThreadGroups, 1, 1);

        ////////Scan groups
        shader.SetInt("_NumOfGroups", numThreadGroups);
        shader.SetBuffer(2, "_GroupSumArrayIn", groupSumArrayBuffer);
        shader.SetBuffer(2, "_GroupSumArrayOut", scannedGroupSumBuffer);
        shader.Dispatch(2, numGroupScanThreadGroups, 1, 1);

        //////////Compact
        shader.SetBuffer(3, "_GrassDataBuffer", detailBuffer);
        shader.SetBuffer(3, "_VoteBuffer", voteBuffer);
        shader.SetBuffer(3, "_ScanBuffer", scanBuffer);
        shader.SetBuffer(3, "_ArgsBuffer", argsBuffer);
        shader.SetBuffer(3, "_CulledGrassOutputBuffer", resultBuffer);
        shader.SetBuffer(3, "_GroupSumArray", scannedGroupSumBuffer);
        shader.Dispatch(3, numThreadGroups, 1, 1);
    }

    internal void RemoveDetail(float brushSize, Vector3 mousePosition)
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }
        var chunks = GetChunksAtWorldPosition(mousePosition);
        foreach (var chunk in chunks)
        {
            if (!instancingData.detailChunks.ContainsKey(chunk.chunkPosition))
                continue;

            float4 mousePos = new float4(mousePosition.x, 0, mousePosition.z, 0);

            var inRange = from d in instancingData.detailChunks[chunk.chunkPosition].AsParallel()
                          where math.distance(new float4(d.trs.c3.x, mousePos.y, d.trs.c3.z, 0), mousePos) > brushSize / 2
                          select d;

            instancingData.detailChunks[chunk.chunkPosition] = inRange.ToList();
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(instancingData);
#endif

        UpdateDetailBuffer();
    }
    #endregion



#region Chunk Functions
    public void AddNewChunk(int chunkX, int chunkY)
    {
        var chunkCoords = new Vector2Int(chunkX, chunkY);
        var newChunk = new GameObject("Chunk " + chunkCoords);

        MarchingSquaresChunk chunk = newChunk.AddComponent<MarchingSquaresChunk>();
        newChunk.AddComponent<MeshFilter>();
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
        UpdateDirtyChunks();
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
            (coords.x * ((dimensions.x - 1) * cellSize.x)),
            0,
            (coords.y * ((dimensions.z - 1) * cellSize.y))
        );

        chunk.transform.parent = transform;
        chunk.InitializeTerrain();
    }

    void UpdateDirtyChunks(bool updateHeight = true)
    {
        List<MarchingSquaresChunk> toUpdate = new List<MarchingSquaresChunk>();
        foreach (var chunk in chunks)
        {
            if (chunk.Value.IsDirty)
            {
                chunk.Value.RegenerateMesh();
                toUpdate.Add(chunk.Value);
            }
        }

        MergeChunks();
        if (updateHeight)
        {
            foreach (var chunk in toUpdate)
            {
                print("1");
                UpdateDetailHeight(chunk);
            }
        }
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
            }

            i++;
        }

        Mesh mesh = new Mesh();
        mesh.CombineMeshes(combine);
        mesh.RecalculateNormals(45);

        transform.GetComponent<MeshFilter>().sharedMesh = mesh;
        transform.GetComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
        transform.GetComponent<MeshCollider>().sharedMesh = mesh;

        triCache = mesh.triangles;
        normCache = mesh.normals;
    }
    internal void DrawHeights(List<Vector3> worldCellPositions, float dragHeight, bool setHeight = false, bool smooth = false)
    {
        print("Drawing heights");
        foreach (Vector3 worldCell in worldCellPositions)
        {
            List<MarchingSquaresChunk> chunks = GetChunksAtWorldPosition(worldCell);
            foreach (MarchingSquaresChunk chunk in chunks)
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

    internal void DrawColors(List<Vector3> worldCellPositions, Vector3 paintPos, float brushSize, Color color, bool fallOff)
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
                var t = (brushSize / 2 - dist) / brushSize / 2;
                Color color1 = color;
                Color color2 = GetColor(worldCell);

                chunk.DrawColor(localPos.x, localPos.y, fallOff ? Color.Lerp(color1, color2, fallOffCurve.Evaluate(t)) : color1);
            }
        }
        UpdateDirtyChunks(false);
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

    internal float GetHeight(Vector3 worldPos, MarchingSquaresChunk c = null)
    {
        //worldPos can actually belong to multiple chunks at once (maximum of 4 if it's in a corner of a chunk)

        float height = 0;
        //The size of one chunk
        Vector3 totalChunkSize = new Vector3(
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
    #endregion


#region Misc Functions
    public void UpdateClouds()
    {

        Shader.SetGlobalFloat("_CloudDensity", cloudSettings.density);
        Shader.SetGlobalFloat("_CloudSpeedX", cloudSettings.speed.x);
        Shader.SetGlobalFloat("_CloudSpeedY", cloudSettings.speed.y);
        Shader.SetGlobalFloat("_CloudScale", cloudSettings.scale);
        Shader.SetGlobalFloat("_CloudBrightness", cloudSettings.brightness);
        Shader.SetGlobalFloat("_CloudVerticalSpeed", cloudSettings.speed.z);
    }
    void CreateOrLoadInstanceData()
    {
        if (instancingData == null)
        {
            if (Resources.Load<InstancingData>("InstancingData" + GUID) != null)
            {
                print("Loading instancing data");
                instancingData = Resources.Load<InstancingData>("InstancingData" + GUID);
                UpdateDetailBuffer();
            }
#if UNITY_EDITOR
            else
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<InstancingData>(), "Assets/Resources/InstancingData" + GUID + ".asset");
            }
#endif
            return;
        }
    }
#endregion
}