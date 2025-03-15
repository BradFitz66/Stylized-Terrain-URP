using LibNoise.Operator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
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

    Mesh m;
    List<Vector3> normals;
    int[] triangles;
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

        Mesh.MeshDataArray meshDataArray;

        ProfilerMarker pm = new ProfilerMarker("UpdateDetailHeight.ResultLoop");
        pm.Begin();
        foreach (var hit in results) { 
            if (hit.collider != null)
            {
                if (hit.transform.GetComponent<MeshFilter>() == null)
                    continue;

                MarchingSquaresChunk chunk = hit.transform.GetComponent<MarchingSquaresChunk>();
                if (chunk == null)
                    continue;

                if(chunk.vertCache == null || chunk.normCache == null || chunk.triCache == null)
                    continue;

                DetailObject d = allDetail[index];
                Vector3 pos = hit.point;
                Vector3 size = d.trs.lossyScale;
                Matrix4x4 trs = Matrix4x4.TRS(pos + Vector3.up * d.normalOffset, Quaternion.identity, size);

                Vector3 n0 = chunk.normCache[chunk.triCache[hit.triangleIndex * 3 + 0]];
                Vector3 n1 = chunk.normCache[chunk.triCache[hit.triangleIndex * 3 + 1]];
                Vector3 n2 = chunk.normCache[chunk.triCache[hit.triangleIndex * 3 + 2]];

                Vector3 baryCenter = hit.barycentricCoordinate;

                Vector3 interpolatedNormal = n0 * baryCenter.x + n1 * baryCenter.y + n2 * baryCenter.z;
                interpolatedNormal = interpolatedNormal.normalized;

                Transform hitTransform = hit.collider.transform;
                interpolatedNormal = hitTransform.TransformDirection(interpolatedNormal);

                newDetailList.Add(new DetailObject()
                {
                    trs = trs,
                    normal = interpolatedNormal,
                    normalOffset = d.normalOffset //Unused in shader. 
                });
                //gotNormals.Dispose();
                
            }
            index++;
        }
        pm.End();


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

    void UpdateDirtyChunks()
    {
        foreach (var chunk in chunks)
        {
            if (chunk.Value.IsDirty)
            {
                chunk.Value.RegenerateMesh();
            }
        }
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
        UpdateDetailHeight();
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
        UpdateDetailHeight();
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
        UpdateDetailHeight(); //Update the detail objects
    }
}
