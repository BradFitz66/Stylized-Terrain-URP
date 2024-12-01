using System;
using System.Collections.Generic;
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

public class MarchingSquaresTerrain : MonoBehaviour
{
    

    public Vector3Int dimensions = new Vector3Int(10, 10, 10);
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

    }
    public void RemoveChunk(Vector2Int coords)
    {
        if (chunks.ContainsKey(coords))
        {
            if (chunks[coords] != null)
                DestroyImmediate(chunks[coords].gameObject);
            chunks.Remove(coords);
        }
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

    
    public void SetColor(Vector2Int chunk, float cx, float cz, Color color)
    {
        MarchingSquaresChunk c = chunks[chunk];
        c.drawColor((int)cx, (int)cz, color);

        var chunkLeft = chunks.TryGetValue(new Vector2Int(chunk.x - 1, chunk.y), out var leftChunk) && cx == 0;
        if (chunkLeft)
        {
            if (leftChunk.colorMap[leftChunk.getIndex(dimensions.x - 1, (int)cz)] != color)
            {
                SetColor(leftChunk.chunkPosition, dimensions.x - 1, cz, color);
            }
        }
        var chunkRight = chunks.TryGetValue(new Vector2Int(chunk.x + 1, chunk.y), out var rightChunk) && cx == dimensions.x - 1;
        if (chunkRight)
        {
            if (rightChunk.colorMap[rightChunk.getIndex(0, (int)cz)] != color)
            {
                SetColor(rightChunk.chunkPosition, 0, cz, color);
            }
        }

        var chunkUp = chunks.TryGetValue(new Vector2Int(chunk.x, chunk.y + 1), out var upChunk) && cz == dimensions.z - 1;
        if (chunkUp)
        {
            if (upChunk.colorMap[upChunk.getIndex((int)cx, 0)] != color)
            {
                SetColor(upChunk.chunkPosition, cx, 0, color);
            }
        }

        var chunkDown = chunks.TryGetValue(new Vector2Int(chunk.x, chunk.y - 1), out var downChunk) && cz == 0;
        if (chunkDown)
        {
            if (downChunk.colorMap[downChunk.getIndex((int)cx, dimensions.z - 1)] != color)
            {
                SetColor(downChunk.chunkPosition, cx, dimensions.z - 1, color);
            }
        }

    }
    public void SetHeight(Vector2Int chunk, float cx, float cz, float height)
    {

        MarchingSquaresChunk c = chunks[chunk];
        c.drawHeight((int)cx, (int)cz, height);

        //Do we have a chunk to the left and are we setting a cell on the left edge?
        var chunkLeft = chunks.TryGetValue(new Vector2Int(chunk.x - 1, chunk.y), out var leftChunk) && cx == 0;
        if (chunkLeft)
        {
            if (leftChunk.heightMap[leftChunk.getIndex((int)cz, dimensions.x - 1)] != height)
            {
                SetHeight(leftChunk.chunkPosition, dimensions.x - 1, cz, height);
            }
        }

        var chunkRight = chunks.TryGetValue(new Vector2Int(chunk.x + 1, chunk.y), out var rightChunk) && cx == dimensions.x - 1;
        if (chunkRight)
        {
            if (rightChunk.heightMap[rightChunk.getIndex((int)cz, 0)] != height)
            {
                SetHeight(rightChunk.chunkPosition, 0, cz, height);
            }
        }

        var chunkUp = chunks.TryGetValue(new Vector2Int(chunk.x, chunk.y + 1), out var upChunk) && cz == dimensions.z - 1;
        if (chunkUp)
        {
            if (upChunk.heightMap[upChunk.getIndex(0, (int)cx)] != height)
            {
                SetHeight(upChunk.chunkPosition, cx, 0, height);
            }
        }


        var chunkDown = chunks.TryGetValue(new Vector2Int(chunk.x, chunk.y - 1), out var downChunk) && cz == 0;
        if (chunkDown)
        {
            if (downChunk.heightMap[downChunk.getIndex(dimensions.z - 1, (int)cx)] != height)
            {
                SetHeight(downChunk.chunkPosition, cx, dimensions.z - 1, height);
            }
        }
    }
}
