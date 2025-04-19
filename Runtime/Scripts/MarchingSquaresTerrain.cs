using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Kitbashery.MeshCombiner;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

// ReSharper disable All


[System.Serializable]
public struct DetailObject
{
    public float4x4 trs;
    public float3 normal;
    public float normalOffset;
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
public struct CloudSettings : IEquatable<CloudSettings>
{
    public float density;
    public Vector3 speed;
    public float scale;
    public float brightness;
    
    
    public bool Equals(CloudSettings other)
    {
        return other.GetHashCode() == GetHashCode();
    }

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

    private ComputeShader _shader;
    
    private ComputeBuffer _detailBuffer;
    private ComputeBuffer _argsBuffer;
    private ComputeBuffer _voteBuffer;
    private ComputeBuffer _scanBuffer;
    private ComputeBuffer _groupSumArrayBuffer;
    private ComputeBuffer _scannedGroupSumBuffer;
    private ComputeBuffer _resultBuffer;
    private int _numThreadGroups;
    private int _numVoteThreadGroups;
    private int _numGroupScanThreadGroups;
    private Camera _camera;
    private bool _useCulling;
    private uint[] _args = new uint[] { 0, 0, 0, 0, 0 };

    private int[] _triCache;
    private Vector3[] _normCache;
    
    private CloudSettings _lastCloudSettings;

    private MaterialPropertyBlock _mpb;
    
    public Vector3Int dimensions = new Vector3Int(10, 10, 10);
    public Bounds TotalTerrainSize
    {
        get
        {
            var cB = new Bounds();
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
    public Texture2D heightMap;

    public int selectedTool;
    public TerrainTool[] tools;
    public TerrainTool currentTool;
    public TerrainTool lastTool;

    public int id = -100;

    public Mesh detailMesh;
    public Material detailMaterial;

    public float detailDensity = 0.1f; //Minimum distance between details
    public float currentDetailDensity = 0.1f;

    [FormerlySerializedAs("GUID")] public string guid;




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
        _useCulling = !(Application.isEditor && !Application.isPlaying);
        UpdateDetailBuffer();
    }

    private void OnEnable()
    {

        CreateOrLoadInstanceData();

        _useCulling = !(Application.isEditor && !Application.isPlaying);
        _shader = Resources.Load<ComputeShader>("Shaders/Culling");

        UpdateDetailBuffer();

        if (_resultBuffer == null || _voteBuffer == null || _scanBuffer == null || _groupSumArrayBuffer == null ||
            _scannedGroupSumBuffer == null)
            return;
        
        _voteBuffer = new ComputeBuffer(30000, sizeof(uint), ComputeBufferType.Append);
        _scanBuffer = new ComputeBuffer(30000, sizeof(uint), ComputeBufferType.Append);
        _groupSumArrayBuffer = new ComputeBuffer(30000, sizeof(uint), ComputeBufferType.Append);
        _scannedGroupSumBuffer = new ComputeBuffer(30000, sizeof(uint), ComputeBufferType.Append);
        _resultBuffer = new ComputeBuffer(30000, sizeof(uint), ComputeBufferType.Append);
    }

    //Cleanup
    private void OnDestroy()
    {
        if (_argsBuffer != null)
            _argsBuffer.Release();
        if (_detailBuffer != null)
            _detailBuffer.Release();
    }

    public void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(TotalTerrainSize.center, TotalTerrainSize.size);
    }

    private void Update()
    {
        if (_argsBuffer == null || _args == null || _detailBuffer == null)
        {
            InitializeBuffers();
            return;
        }

        if (_camera == null)
        {
            if (Application.isPlaying)
                _camera = Camera.main;
        }

        if (!_lastCloudSettings.Equals(cloudSettings))
        {
            _lastCloudSettings = cloudSettings;
            UpdateClouds();
        }

        if (_camera != null && _useCulling)
            Dispatch(transform.position, _camera.transform.position, _camera.projectionMatrix * _camera.worldToCameraMatrix);

        Graphics.DrawMeshInstancedIndirect(
            detailMesh, 0, detailMaterial,
            new Bounds(Vector3.zero, new Vector3(10000, 10000, 10000)),
            bufferWithArgs: _argsBuffer, argsOffset: 0, properties: _mpb
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
        float2 bottomLeft = new float2(TotalTerrainSize.min.x, TotalTerrainSize.min.z);
        float2 topRight = new float2(TotalTerrainSize.max.x, TotalTerrainSize.max.z);
        float minimumDistance = detailDensity;
        PoissonJob job = new PoissonJob(
            results,
            bottomLeft,
            topRight,
            minimumDistance
        );
        var handle = job.Schedule();
        handle.Complete();

        foreach (var pos in results)
        {
            var chunksAtWorldPosition = GetChunksAtWorldPosition(new Vector3(pos.x, 0, pos.y));
            foreach (var chunk in chunksAtWorldPosition)
            {
                if (!instancingData.detailChunks.ContainsKey(chunk.chunkPosition))
                    instancingData.detailChunks.Add(chunk.chunkPosition, new List<DetailObject>());
                var trs = Matrix4x4.TRS(new Vector3(pos.x, 0, pos.y), Quaternion.identity, Vector3.one * size);
                var detailObject = new DetailObject()
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
        EditorUtility.SetDirty(instancingData);
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
        _argsBuffer?.Release();
        _detailBuffer?.Release();
        _args = new uint[5] { detailMesh.GetIndexCount(0), (uint)instancingData.GetDetailCount(), detailMesh.GetIndexStart(0), detailMesh.GetBaseVertex(0), 0 };

        _mpb ??= new MaterialPropertyBlock();
        
        if (_detailBuffer == null || _voteBuffer == null || _scanBuffer == null || _groupSumArrayBuffer == null || _scannedGroupSumBuffer == null || _resultBuffer == null)
        {
            var data = instancingData.GetDetailData();
            _detailBuffer = new ComputeBuffer(300000, Marshal.SizeOf<DetailObject>()); //Preallocate 1million details
            _voteBuffer = new ComputeBuffer(300000, 4, ComputeBufferType.Append);
            _scanBuffer = new ComputeBuffer(300000, 4, ComputeBufferType.Append);
            _groupSumArrayBuffer = new ComputeBuffer(300000, 4, ComputeBufferType.Append);
            _scannedGroupSumBuffer = new ComputeBuffer(300000, 4, ComputeBufferType.Append);
            _resultBuffer = new ComputeBuffer(300000, Marshal.SizeOf<DetailObject>(), ComputeBufferType.Append);
            _detailBuffer.SetData(
                data,
                0,
                0,
                data.Length
            );

            _mpb?.SetBuffer("_TerrainDetail", _useCulling ? _resultBuffer : _detailBuffer);
        }

        _mpb = new MaterialPropertyBlock();

        _argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(_args);
    }

    public void UpdateDetailBuffer()
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }
        var data = instancingData.GetDetailData();

        if (_argsBuffer == null)
            InitializeBuffers();

        _args[1] = (uint)data.Length;
        _argsBuffer.SetData(_args);

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        if (data.Length == 0)
            return;

        print("Updating detail buffer");

        if (_detailBuffer == null)
            _detailBuffer = new ComputeBuffer(300000, Marshal.SizeOf(typeof(DetailObject)));

        _numThreadGroups = Mathf.CeilToInt(data.Length / 128.0f);
        if (_numThreadGroups > 128)
        {
            int powerOfTwo = 128;
            while (powerOfTwo < _numThreadGroups)
                powerOfTwo *= 2;

            _numThreadGroups = powerOfTwo;
        }
        else
        {
            while (128 % _numThreadGroups != 0)
                _numThreadGroups++;
        }

        _numVoteThreadGroups = Mathf.CeilToInt(data.Length / 128.0f);
        _numGroupScanThreadGroups = Mathf.CeilToInt(data.Length / 1024.0f);

        print("Updating detail buffer");

        _detailBuffer.SetData(
            data,
            0,
            0,
            data.Length
        );

        _mpb?.SetBuffer("_TerrainDetail", _useCulling ? _resultBuffer : _detailBuffer);
    }
    public void AddDetail(float size, float normalOffset, float3 detailPos, MarchingSquaresChunk chunk, bool distanceCheck = true)
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }

        //Return if the detail is out of bounds
        if (!TotalTerrainSize.Contains(detailPos))
        {
            return;
        }

        var chunksAtWorldPosition = GetChunksAtWorldPosition(detailPos);
        if (chunksAtWorldPosition.Count == 0)
            return;

        MarchingSquaresChunk c = chunksAtWorldPosition[0];

        bool canPlace = true;
        foreach (var otherChunk in chunksAtWorldPosition)
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
        EditorUtility.SetDirty(instancingData);
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


        var handle = RaycastCommand.ScheduleBatch(commands, results, 1, default(JobHandle));

        handle.Complete();


        var index = 0;



        foreach (var hit in results)
        {
            if (chunk == null)
            {
                index++;
                continue;
            }
            if (_normCache == null || _triCache == null)
            {
                _normCache = GetComponent<MeshFilter>().sharedMesh.normals;
                _triCache = GetComponent<MeshFilter>().sharedMesh.triangles;
            }

            DetailObject d = instancingData.detailChunks[chunkPos][index];
            Vector3 pos = hit.point;
            //Get lossyScale from float4x4
            float3 size = new Vector3(
                math.length(d.trs.c0),
                math.length(d.trs.c1),
                math.length(d.trs.c2)
            );

            var triIdx1 = hit.triangleIndex * 3;
            var triIdx2 = hit.triangleIndex * 3 + 1;
            var triIdx3 = hit.triangleIndex * 3 + 2;
            if (triIdx1 < 0 || triIdx2 < 0 || triIdx3 < 0 || triIdx1 >= _triCache.Length || triIdx2 >= _triCache.Length || triIdx3 >= _triCache.Length)
            {
                index++;
                continue;
            }
            int tri1 = _triCache[triIdx1];
            int tri2 = _triCache[triIdx2];
            int tri3 = _triCache[triIdx3];


            Vector3 n0 = _normCache[tri1];
            Vector3 n1 = _normCache[tri2];
            Vector3 n2 = _normCache[tri3];

            var baryCenter = hit.barycentricCoordinate;
            var interpolatedNormal = n0 * baryCenter.x + n1 * baryCenter.y + n2 * baryCenter.z;

            interpolatedNormal = interpolatedNormal.normalized;
            interpolatedNormal = hit.transform.TransformDirection(interpolatedNormal);
            var trs = float4x4.TRS(pos + Vector3.up * d.normalOffset, Quaternion.identity, size);

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
        EditorUtility.SetDirty(instancingData);
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
    private string _GroupSumArrayInID  = "_GroupSumArrayIn";
    private string _GroupSumArrayOutID = "_GroupSumArrayOut";
    private string _GrassDataBufferID = "_GrassDataBuffer";
    private string _voteBufferID = "_VoteBuffer";
    private string _scanBufferID = "_ScanBuffer";
    private string _argsBufferID = "_ArgsBuffer";
    private string _matrixVPID = "_MatrixVP";
    private string _cameraPositionID = "_CameraPosition";
    private string _distanceID = "_Distance";
    private string _objectPositionID = "_ObjectPosition";
    void Dispatch(Vector3 position, Vector3 cameraPos, Matrix4x4 VP)
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }
        _args[1] = 0;
        _argsBuffer.SetData(_args);

        if (_shader == null)
            _shader = Resources.Load<ComputeShader>("Shaders/Culling");

        if (_numGroupScanThreadGroups == 0 || _numThreadGroups == 0 || _numVoteThreadGroups == 0)
            return;

        _shader.SetVector("_ObjectPosition", position);
        _shader.SetMatrix("_MatrixVP", VP);
        _shader.SetBuffer(0, "_GrassDataBuffer", _detailBuffer);
        _shader.SetBuffer(0, "_VoteBuffer", _voteBuffer);
        _shader.SetVector("_CameraPosition", cameraPos);
        _shader.SetFloat("_Distance", 5000);
        _shader.Dispatch(0, _numVoteThreadGroups, 1, 1);

        _shader.SetBuffer(1, "_VoteBuffer", _voteBuffer);
        _shader.SetBuffer(1, "_ScanBuffer", _scanBuffer);
        _shader.SetBuffer(1, "_GroupSumArray", _groupSumArrayBuffer);
        _shader.Dispatch(1, _numThreadGroups, 1, 1);

        ////////Scan groups
        _shader.SetInt("_NumOfGroups", _numThreadGroups);
        _shader.SetBuffer(2, "_GroupSumArrayIn", _groupSumArrayBuffer);
        _shader.SetBuffer(2, "_GroupSumArrayOut", _scannedGroupSumBuffer);
        _shader.Dispatch(2, _numGroupScanThreadGroups, 1, 1);

        
        //////////Compact
        _shader.SetBuffer(3, "_GrassDataBuffer", _detailBuffer);
        _shader.SetBuffer(3, "_VoteBuffer", _voteBuffer);
        _shader.SetBuffer(3, "_ScanBuffer", _scanBuffer);
        _shader.SetBuffer(3, "_ArgsBuffer", _argsBuffer);
        _shader.SetBuffer(3, "_CulledGrassOutputBuffer", _resultBuffer);
        _shader.SetBuffer(3, "_GroupSumArray", _scannedGroupSumBuffer);
        _shader.Dispatch(3, _numThreadGroups, 1, 1);
    }

    internal void RemoveDetail(float brushSize, Vector3 mousePosition)
    {
        if (instancingData == null)
        {
            CreateOrLoadInstanceData();
            return;
        }
        var chunksAtWorldPosition = GetChunksAtWorldPosition(mousePosition);
        foreach (var chunk  in chunksAtWorldPosition)
        {
            if (!instancingData.detailChunks.ContainsKey(chunk.chunkPosition))
                continue;

            var mousePos = new float4(mousePosition.x, 0, mousePosition.z, 0);

            var inRange = from d in instancingData.detailChunks[chunk.chunkPosition].AsParallel()
                          where math.distance(new float4(d.trs.c3.x, mousePos.y, d.trs.c3.z, 0), mousePos) > brushSize / 2
                          select d;

            instancingData.detailChunks[chunk.chunkPosition] = inRange.ToList();
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(instancingData);
#endif

        UpdateDetailBuffer();
    }
    #endregion
#region Chunk Functions
    public void AddNewChunk(int chunkX, int chunkY)
    {
        var chunkCoords = new Vector2Int(chunkX, chunkY);
        var newChunk = new GameObject("Chunk " + chunkCoords);

        var chunk = newChunk.AddComponent<MarchingSquaresChunk>();
        newChunk.AddComponent<MeshFilter>();
        newChunk.layer = gameObject.layer;

        AddChunk(chunkCoords, chunk);

        var chunkLeft = chunks.TryGetValue(new Vector2Int(chunkX - 1, chunkY), out var leftChunk);
        if (chunkLeft)
        {
            for (var z = 0; z < dimensions.z; z++)
            {
                var idx1 = chunk.GetIndex(z, 0);
                var idx2 = leftChunk.GetIndex(z, dimensions.x - 1);
                chunk.heightMap[idx1] = leftChunk.heightMap[idx2];

            }

            //Add neighbor
            leftChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(leftChunk);
        }
        var chunkRight = chunks.TryGetValue(new Vector2Int(chunkX + 1, chunkY), out var rightChunk);
        if (chunkRight)
        {
            for (var z = 0; z < dimensions.z; z++)
            {
                var idx1 = chunk.GetIndex(z, dimensions.x - 1);
                var idx2 = rightChunk.GetIndex(z, 0);
                chunk.heightMap[idx1] = rightChunk.heightMap[idx2];
            }
            rightChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(rightChunk);
        }
        var chunkUp = chunks.TryGetValue(new Vector2Int(chunkX, chunkY + 1), out var upChunk);
        if (chunkUp)
        {
            for (var x = 0; x < dimensions.x; x++)
            {
                var idx1 = chunk.GetIndex(dimensions.z - 1, x);
                var idx2 = upChunk.GetIndex(0, x);
                chunk.heightMap[idx1] = upChunk.heightMap[idx2];
            }
            upChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(upChunk);
        }
        var chunkDown = chunks.TryGetValue(new Vector2Int(chunkX, chunkY - 1), out var downChunk);
        if (chunkDown)
        {
            for (var x = 0; x < dimensions.x; x++)
            {
                var idx1 = chunk.GetIndex(0, x);
                var idx2 = downChunk.GetIndex(dimensions.z - 1, x);
                chunk.heightMap[idx1] = downChunk.heightMap[idx2];
            }
            downChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(downChunk);
        }
        var chunkUpright = chunks.TryGetValue(new Vector2Int(chunkX + 1, chunkY + 1), out var upRightChunk);
        if (chunkUpright)
        {
            var idx1 = chunk.GetIndex(0, dimensions.x - 1);
            var idx2 = upRightChunk.GetIndex(dimensions.z - 1, 0);
            chunk.heightMap[idx1] = upRightChunk.heightMap[idx2];
            upRightChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(upRightChunk);
        }
        var chunkUpleft = chunks.TryGetValue(new Vector2Int(chunkX - 1, chunkY + 1), out var upLeftChunk);
        if (chunkUpleft)
        {
            var idx1 = chunk.GetIndex(dimensions.z - 1, 0);
            var idx2 = upLeftChunk.GetIndex(0, dimensions.x - 1);
            chunk.heightMap[idx1] = upLeftChunk.heightMap[idx2];
            upLeftChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(upLeftChunk);
        }
        var chunkDownright = chunks.TryGetValue(new Vector2Int(chunkX + 1, chunkY - 1), out var downRightChunk);
        if (chunkDownright)
        {
            var idx1 = chunk.GetIndex(dimensions.z - 1, dimensions.x - 1);
            var idx2 = downRightChunk.GetIndex(0, 0);
            chunk.heightMap[idx1] = downRightChunk.heightMap[idx2];
            downRightChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(downRightChunk);
        }
        var chunkDownleft = chunks.TryGetValue(new Vector2Int(chunkX - 1, chunkY - 1), out var downLeftChunk);
        if (chunkDownleft)
        {
            var idx1 = chunk.GetIndex(0, 0);
            var idx2 = downLeftChunk.GetIndex(dimensions.z - 1, dimensions.x - 1);
            chunk.heightMap[idx1] = downLeftChunk.heightMap[idx2];
            downLeftChunk.neighboringChunks.Add(chunk);
            chunk.neighboringChunks.Add(downLeftChunk);
        }



        chunk.RegenerateMesh();
        MergeChunks();
    }

    public void GenerateTerrain()
    {
        if (heightMap == null)
        {
            foreach (var chunk in chunks)
            {

                chunk.Value.GenerateHeightmap(
                    noiseSettings
                );
            }
        }
        else
        {
            //Loop over all chunks
            foreach (var chunk in chunks)
            {
                //Loop through chunk's heightmap
                for (var x = 0; x < dimensions.x; x++)
                {
                    for (var z = 0; z < dimensions.z; z++)
                    {
                        var idx = chunk.Value.GetIndex(z, x);
                        
                        //Convert x and z to world position
                        var worldPos = new Vector3(
                            chunk.Value.transform.position.x + x * cellSize.x,
                            0,
                            chunk.Value.transform.position.z + z * cellSize.y
                        );
                        //Convert world position to heightmap position
                        var heightMapPos = new Vector2(
                            Mathf.InverseLerp(TotalTerrainSize.min.x, TotalTerrainSize.max.x, worldPos.x),
                            Mathf.InverseLerp(TotalTerrainSize.min.z, TotalTerrainSize.max.z, worldPos.z)
                        );
                        //Get the height from the heightmap
                        var height = heightMap.GetPixelBilinear(heightMapPos.x, heightMapPos.y).r;
                        
                        chunk.Value.heightMap[idx] = height * noiseSettings.scale;
                    }
                }
                chunk.Value.isDirty = true;
            }
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
            if (chunk.Value.isDirty)
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

    // ReSharper disable Unity.PerformanceAnalysis
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

        transform.GetComponent<MeshFilter>().sharedMesh = mesh;
        transform.GetComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
        transform.GetComponent<MeshCollider>().sharedMesh = mesh;

        _triCache = mesh.triangles;
        _normCache = mesh.normals;
    }
    internal void DrawHeights(List<Vector3> worldCellPositions, float dragHeight, bool setHeight = false, bool smooth = false)
    {
        print("Drawing heights");
        foreach (Vector3 worldCell in worldCellPositions)
        {
            var chunksAtWorldPosition = GetChunksAtWorldPosition(worldCell);
            foreach (var chunk in chunksAtWorldPosition)
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
        var fallOffCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        foreach (var worldCell in worldCellPositions)
        {
            var chunksAtWorldPosition = GetChunksAtWorldPosition(worldCell);
            foreach (var chunk in chunksAtWorldPosition)
            {
                var localPos = new Vector2Int(
                    Mathf.FloorToInt((worldCell.x - chunk.transform.position.x) / cellSize.x),
                    Mathf.FloorToInt((worldCell.z - chunk.transform.position.z) / cellSize.y)
                );
                var dist = Vector3.Distance(worldCell, paintPos);
                var t = (brushSize / 2 - dist) / brushSize / 2;
                var color2 = GetColor(worldCell);

                chunk.DrawColor(localPos.x, localPos.y, fallOff ? Color.Lerp(color, color2, fallOffCurve.Evaluate(t)) : color);
            }
        }
        UpdateDirtyChunks(false);
    }


    /// <summary>
    /// Get the chunks at a given cell's world position
    /// </summary>
    /// <param name="worldCellPosition"></param>
    internal List<MarchingSquaresChunk> GetChunksAtWorldPosition(Vector3 worldCellPosition)
    {
        var chunksAtPosition = new List<MarchingSquaresChunk>();
        //Loop through every chunk and check if the world position is inside the chunk
        foreach (var chunk in chunks)
        {
            var localCellPos = new Vector2Int(
                Mathf.FloorToInt((worldCellPosition.x - chunk.Value.transform.position.x) / cellSize.x),
                Mathf.FloorToInt((worldCellPosition.z - chunk.Value.transform.position.z) / cellSize.y)
            );
            var inBounds = !(localCellPos.x < 0 || localCellPos.x >= dimensions.x || localCellPos.y < 0 || localCellPos.y >= dimensions.z);
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
        var color = Color.white;
        var chunksAtWorldPosition = GetChunksAtWorldPosition(worldPos);
        foreach (var chunk in chunksAtWorldPosition)
        {
            //Get the local cell position
            var localCellPos = new Vector2Int(
                Mathf.FloorToInt((worldPos.x - chunk.transform.position.x) / cellSize.x),
                Mathf.FloorToInt((worldPos.z - chunk.transform.position.z) / cellSize.y)
            );
            var col = chunk.colorMap[chunk.GetIndex(localCellPos.x, localCellPos.y)];
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
        var totalChunkSize = new Vector3(
            (dimensions.x - 1) * cellSize.x,
            0,
            (dimensions.z - 1) * cellSize.y
        );

        var chunksAtWorldPosition = GetChunksAtWorldPosition(worldPos);
        foreach (var chunk in chunksAtWorldPosition)
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

        var heights = new Dictionary<Vector3, List<float>>();

        foreach (var cell in cells)
        {
            var chunksAtWorldPosition = GetChunksAtWorldPosition(cell);
            foreach (var chunk in chunksAtWorldPosition)
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
                        foreach (var nChunk in GetChunksAtWorldPosition(neighbor))
                        {
                            var localCellPos = new Vector2Int(
                                Mathf.FloorToInt((neighbor.x - nChunk.transform.position.x) / cellSize.x),
                                Mathf.FloorToInt((neighbor.z - nChunk.transform.position.z) / cellSize.y)
                            );
                            var height = nChunk.heightMap[nChunk.GetIndex(localCellPos.y, localCellPos.x)];
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
            var chunksAtWorldPosition = GetChunksAtWorldPosition(cell.Key);
            foreach (var chunk in chunksAtWorldPosition)
            {
                //Get the local cell position
                var localCellPos = new Vector2Int(
                    Mathf.FloorToInt((cell.Key.x - chunk.transform.position.x) / cellSize.x),
                    Mathf.FloorToInt((cell.Key.z - chunk.transform.position.z) / cellSize.y)
                );
                var h = cell.Value;
                //Smooth heights
                var curHeight = chunk.heightMap[chunk.GetIndex(localCellPos.y, localCellPos.x)];

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
            return;
        if (Resources.Load<InstancingData>("InstancingData" + guid) != null)
        {
            print("Loading instancing data");
            instancingData = Resources.Load<InstancingData>("InstancingData" + guid);
            UpdateDetailBuffer();
        }
#if UNITY_EDITOR
        else
        {
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<InstancingData>(), "Assets/Resources/InstancingData" + guid + ".asset");
        }
#endif
    }
#endregion
}