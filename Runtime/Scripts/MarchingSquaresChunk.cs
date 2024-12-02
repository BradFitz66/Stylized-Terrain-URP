using System.Collections.Generic;
using UnityEngine;
[System.Serializable]
public struct cellGeometryData
{
    public List<Vector3> vertices;
    public List<Vector2> uvs;
    public List<Color> colors;
}

public class MarchingSquaresChunk : MonoBehaviour
{

    public Mesh mesh;
    public List<Vector3> vertices;
    public List<Color> colors;
    public List<int> triangles;
    public List<Vector2> uvs;



    public MarchingSquaresTerrain terrain;
    public Vector2Int chunkPosition;

    public bool higherPolyFloors = true;

    public Texture2D heightMapImage;
    public float[] heightMap;
    public bool[] needsUpdate;
    public Color[] colorMap;

    public int r;

    public Vector2Int cellCoords;
    public List<bool> cellEdges;
    public List<float> pointHeights;

    public float ay;
    public float by;
    public float cy;
    public float dy;

    public bool ab;
    public bool ac;
    public bool bd;
    public bool cd;


    bool floorMode;

    public SerializedDictionary<Vector2Int, cellGeometryData> cellGeometry = new SerializedDictionary<Vector2Int, cellGeometryData>();
    


    public void initializeTerrain(bool shouldRegenerate = true)
    {
        mesh = new Mesh();
        cellGeometry = new SerializedDictionary<Vector2Int, cellGeometryData>();

        colorMap = new Color[terrain.dimensions.z * terrain.dimensions.x];
        //Fill with red
        for (int z = 0; z < terrain.dimensions.z; z++)
        {
            for (int x = 0; x < terrain.dimensions.x; x++)
            {
                colorMap[getIndex(x, z)] = new Color(1, 0, 0, 0);
            }
        }
        heightMap = new float[terrain.dimensions.z * terrain.dimensions.x];
        needsUpdate = new bool[terrain.dimensions.z * terrain.dimensions.x];
        for (int z = 0; z < terrain.dimensions.z; z++)
        {
            for (int x = 0; x < terrain.dimensions.x; x++)
            {
                needsUpdate[getIndex(x, z)] = true;
            }
        }

        regenerateMesh();

    }

    public int getIndex(int x, int z)
    {
        //Check if within bounds
        if (x < 0 || x >= terrain.dimensions.x || z < 0 || z >= terrain.dimensions.z)
        {
            return 0;
        }

        return x + z * terrain.dimensions.x;
    }

    public void regenerateMesh()
    {
        mesh.Clear();
        vertices = new List<Vector3>();
        triangles = new List<int>();
        colors = new List<Color>();
        uvs = new List<Vector2>();

        generateTerrainCells();

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        Color[] vertexColors = new Color[mesh.vertices.Length];
        for (int i = 0; i < colors.Count; i++)
        {
            vertexColors[i] = colors[i];
        }
        mesh.colors = vertexColors;
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();

        MeshRenderer r = gameObject.GetComponent<MeshRenderer>();
        MeshFilter mf = gameObject.GetComponent<MeshFilter>();

        r.material = terrain.terrainMaterial;
        mf.sharedMesh = mesh;

        MeshCollider mc = gameObject.GetComponent<MeshCollider>();
        mc.sharedMesh = mf.sharedMesh;


    }

    public void generateTerrainCells()
    {
        if (cellGeometry == null)
        {
            cellGeometry = new SerializedDictionary<Vector2Int, cellGeometryData>();
        }

        for (int z = 0; z < terrain.dimensions.z - 1; z++)
        {
            for (int x = 0; x < terrain.dimensions.x - 1; x++)
            {
                cellCoords = new Vector2Int(x, z);

                if (!needsUpdate[getIndex(z, x)])
                {
                    List<Vector3> verts = cellGeometry[cellCoords].vertices;
                    List<Vector2> uv = cellGeometry[cellCoords].uvs;
                    List<Color> cols = cellGeometry[cellCoords].colors;
                    int vertIdx = vertices.Count;
                    for (int i = 0; i < verts.Count; i += 3)
                    {
                        AddFace(
                            verts[i],
                            verts[i + 1],
                            verts[i + 2],
                            false
                        );
                    }
                    for (int i = 0; i < cols.Count; i++)
                    {
                        colors.Add(cols[i]);
                    }
                    for (int i = 0; i < uv.Count; i++)
                    {
                        uvs.Add(uv[i]);
                    }

                    continue;
                }
                needsUpdate[getIndex(z, x)] = false;
                cellGeometry[cellCoords] = new cellGeometryData()
                {
                    vertices = new List<Vector3>(),
                    uvs = new List<Vector2>(),
                    colors = new List<Color>()
                };

                r = 0;

                ay = heightMap[getIndex(z, x)];
                by = heightMap[getIndex(z, x + 1)];
                cy = heightMap[getIndex(z + 1, x)];
                dy = heightMap[getIndex(z + 1, x + 1)];

                ab = Mathf.Abs(ay - by) < terrain.mergeThreshold; // Top Edge
                ac = Mathf.Abs(ay - cy) < terrain.mergeThreshold; // Bottom Edge
                bd = Mathf.Abs(by - dy) < terrain.mergeThreshold; // Right Edge
                cd = Mathf.Abs(cy - dy) < terrain.mergeThreshold; // Left Edge

                //Case 0
                if (ab && ac && bd && cd)
                {
                    AddFullFloor();
                    continue;
                }

                cellEdges = new List<bool> { ab, bd, cd, ac };
                pointHeights = new List<float> { ay, by, dy, cy };



                bool caseFound = false;

                for (int i = 0; i < 4; i++)
                {
                    r = i;

                    ab = cellEdges[r];
                    bd = cellEdges[(r + 1) % 4];
                    cd = cellEdges[(r + 2) % 4];
                    ac = cellEdges[(r + 3) % 4];

                    ay = pointHeights[r];
                    by = pointHeights[(r + 1) % 4];
                    dy = pointHeights[(r + 2) % 4];
                    cy = pointHeights[(r + 3) % 4];

                    caseFound = true;

                    //Case 1
                    if (isHigher(ay, by) && isHigher(ay, cy) && bd && cd)
                    {
                        AddOuterCorner(true, true);
                    }

                    //Case 2
                    else if (isHigher(ay, cy) && isHigher(by, dy) && ab && cd)
                    {
                        AddEdge(true, true);
                    }

                    //Case 3
                    else if (isHigher(ay, by) && isHigher(ay, cy) && isHigher(by, dy) && cd)
                    {
                        AddEdge(true, true, 0.5f, 1);
                        AddOuterCorner(false, true, true, by);
                    }
                    //Case 4
                    else if (isHigher(by, ay) && isHigher(ay, cy) && isHigher(by, dy) && cd)
                    {
                        AddEdge(true, true, 0, 0.5f);
                        rotateCell(1);
                        AddOuterCorner(false, true, true, cy);
                    }

                    //Case5
                    else if (isLower(ay, by) && isLower(ay, cy) && isLower(dy, by) && isLower(dy, cy) && isMerged(by, cy))
                    {
                        AddInnerCorner(true, false);
                        AddDiagonalFloor(by, cy, true, true);
                        rotateCell(2);
                        AddInnerCorner(true, false);
                    }
                    //Case 5.5
                    else if (isLower(ay, by) && isLower(ay, cy) && isLower(dy, by) && isLower(dy, cy) && isHigher(by, cy))
                    {
                        AddInnerCorner(true, false, true);
                        AddDiagonalFloor(cy, cy, true, true);

                        rotateCell(2);
                        AddInnerCorner(true, false, true);

                        rotateCell(-1);
                        AddOuterCorner(false, true);
                    }
                    //Case 6
                    else if (isLower(ay, by) && isLower(ay, cy) && bd && cd)
                    {
                        AddInnerCorner(true, true);
                    }

                    //Case 7
                    else if (isLower(ay, by) && isLower(ay, cy) && isHigher(dy, by) && isHigher(dy, cy) && isMerged(by, cy))
                    {

                        AddInnerCorner(true, false);
                        AddDiagonalFloor(by, cy, true, false);
                        rotateCell(2);
                        AddOuterCorner(false, true);
                    }
                    //Case 8
                    else if (isLower(ay, by) && isLower(ay, cy) && isLower(dy, cy) && bd)
                    {
                        AddInnerCorner(true, false, true);

                        startFloor();
                        AddFace(
                            AddPoint(1, dy, 1),
                            AddPoint(0.5f, dy, 1, 1, 0),
                            AddPoint(1, (by + dy) / 2, 0.5f)
                        );

                        AddFace(
                            AddPoint(1, by, 0),
                            AddPoint(1, (by + dy) / 2, 0.5f),
                            AddPoint(0.5f, by, 0, 0, 1)
                        );

                        AddFace(
                            AddPoint(0.5f, by, 0, 0, 1),
                            AddPoint(1, (by + dy) / 2, 0.5f),
                            AddPoint(0, by, 0.5f, 1, 1)
                        );

                        AddFace(
                            AddPoint(0.5f, dy, 1, 1, 0),
                            AddPoint(0, by, 0.5f, 1, 1),
                            AddPoint(1, (by + dy) / 2, 0.5f)
                        );
                        startWall();
                        AddFace(
                            AddPoint(0, by, 0.5f),
                            AddPoint(0.5f, dy, 1),
                            AddPoint(0, cy, 0.5f)
                        );
                        AddFace(
                            AddPoint(0.5f, cy, 1),
                            AddPoint(0, cy, 0.5f),
                            AddPoint(0.5f, dy, 1)
                        );
                        startFloor();
                        AddFace(
                            AddPoint(0, cy, 1),
                            AddPoint(0, cy, 0.5f, 0, 1),
                            AddPoint(0.5f, cy, 1, 0, 1)
                        );
                    }
                    //Case 9
                    else if (isLower(ay, by) && isLower(ay, cy) && isLower(dy, by) && cd)
                    {

                        AddInnerCorner(true, false, true);

                        startFloor();
                        //D Corner
                        AddFace(
                            AddPoint(1, dy, 1, 0, 0),
                            AddPoint(0.5f, (dy + cy) / 2, 1, 0, 0),
                            AddPoint(1, dy, 0.5f, 1, 0)
                        );
                        //C Corner
                        AddFace(
                            AddPoint(0, cy, 1, 0, 0),
                            AddPoint(0, cy, 0.5f, 0, 1),
                            AddPoint(0.5f, (dy + cy) / 2, 1)
                        );

                        //Center floors
                        AddFace(
                            AddPoint(0, cy, 0.5f, 0, 1),
                            AddPoint(0.5f, cy, 0, 1, 1),
                            AddPoint(0.5f, (dy + cy) / 2, 1, 0, 0)
                        );

                        AddFace(
                            AddPoint(1, dy, 0.5f, 1, 0f),
                            AddPoint(0.5f, (dy + cy) / 2, 1, 0, 0),
                            AddPoint(0.5f, cy, 0, 1, 1)
                        );

                        //Walls to upper corner
                        startWall();
                        AddFace(
                            AddPoint(0.5f, cy, 0),
                            AddPoint(0.5f, by, 0),
                            AddPoint(1, dy, 0.5f)
                        );

                        AddFace(
                            AddPoint(1, by, 0.5f),
                            AddPoint(1, dy, 0.5f),
                            AddPoint(0.5f, by, 0)
                        );

                        startFloor();
                        AddFace(
                            AddPoint(1, by, 0),
                            AddPoint(1, by, 0.5f, 0, 1),
                            AddPoint(0.5f, by, 0, 0, 1)
                        );
                    }
                    //Case 10
                    else if (isLower(ay, by) && isLower(ay, cy) && isHigher(dy, cy) && bd)
                    {
                        AddInnerCorner(true, false, true, true, false);

                        rotateCell(1);
                        AddEdge(false, true);
                    }
                    //Case 11
                    else if (isLower(ay, by) && isLower(ay, cy) && isHigher(dy, by) && cd)
                    {
                        AddInnerCorner(true, false, true, false, true);

                        rotateCell(2);
                        AddEdge(false, true);
                    }
                    //Case 12
                    else if (isLower(ay, by) && isLower(by, dy) && isLower(dy, cy) && isHigher(cy, ay))
                    {
                        AddInnerCorner(true, false, true, false, true);

                        rotateCell(2);
                        AddEdge(false, true, 0, 0.5f);

                        rotateCell(1);
                        AddOuterCorner(false, true, true, cy);
                    }
                    //Case 13
                    else if (isLower(ay, cy) && isLower(cy, dy) && isLower(dy, by) && isHigher(by, ay))
                    {
                        AddInnerCorner(true, false, true, true, false);

                        rotateCell(1);
                        AddEdge(false, true, 0.5f, 1);

                        AddOuterCorner(false, true, true, by);
                    }
                    //Case 14
                    else if (isLower(ay, by) && isLower(by, cy) && isLower(cy, dy))
                    {
                        AddInnerCorner(true, false, true, false, true);

                        rotateCell(2);
                        AddEdge(false, true, 0.5f, 1);

                        AddOuterCorner(false, true, true, by);
                    }
                    //Case 15
                    else if (isLower(ay, cy) && isLower(cy, by) && isLower(by, dy))
                    {
                        AddInnerCorner(true, false, true, true, false);

                        rotateCell(1);
                        AddEdge(false, true, 0, 0.5f);

                        rotateCell(1);
                        AddOuterCorner(false, true, true, cy);
                    }
                    //Case 16
                    else if (ab && bd && cd && isHigher(ay, cy))
                    {
                        float edgeBy = (by + dy) / 2;
                        float edgeDy = (by + dy) / 2;

                        startFloor();

                        AddFace(
                            AddPoint(0, ay, 0),
                            AddPoint(1, by, 0),
                            AddPoint(1, edgeBy, 0.5f)
                        );
                        AddFace(
                            AddPoint(1, edgeBy, 0.5f, 0, 1),
                            AddPoint(0, ay, 0.5f, 0, 1),
                            AddPoint(0, ay, 0)
                        );

                        startWall();
                        AddFace(
                            AddPoint(0, cy, 0.5f, 0, 0),
                            AddPoint(0, ay, 0.5f, 0, 1),
                            AddPoint(1, edgeDy, 0.5f, 1, 0)
                        );

                        startFloor();
                        AddFace(
                            AddPoint(0, cy, 0.5f, 1, 0),
                            AddPoint(1, edgeDy, 0.5f, 1, 0),
                            AddPoint(0, cy, 1)
                        );

                        AddFace(
                            AddPoint(1, dy, 1),
                            AddPoint(0, cy, 1),
                            AddPoint(1, edgeDy, 0.5f)
                        );
                    }
                    //Case 17
                    else if (ab && ac && cd && isHigher(by, dy))
                    {
                        var edgeAy = (ay + cy) / 2;
                        var edgeCy = (ay + cy) / 2;

                        startFloor();
                        AddFace(
                            AddPoint(0, ay, 0),
                            AddPoint(1, by, 0),
                            AddPoint(0, edgeAy, 0.5f)
                        );

                        AddFace(
                            AddPoint(1, by, 0.5f, 0, 1),
                            AddPoint(0, edgeAy, 0.5f, 0, 1),
                            AddPoint(1, by, 0)
                        );

                        startWall();
                        AddFace(
                            AddPoint(1, by, 0.5f, 1, 1),
                            AddPoint(1, dy, 0.5f, 1, 0),
                            AddPoint(0, edgeAy, 0.5f, 0, 0)
                        );

                        startFloor();
                        AddFace(
                            AddPoint(0, edgeCy, 0.5f, 1, 0),
                            AddPoint(1, dy, 0.5f, 1, 0),
                            AddPoint(1, dy, 1)
                        );
                        AddFace(
                            AddPoint(0, cy, 1),
                            AddPoint(0, edgeCy, 0.5f),
                            AddPoint(1, dy, 1)
                        );
                    }
                    else
                    {
                        caseFound = false;
                    }
                    if (caseFound)
                    {
                        break;
                    }
                }
                if (!caseFound)
                {
                    continue;
                }
            }
        }
    }


    void rotateCell(int rotations)
    {
        r = (r + 4 + rotations) % 4;

        ab = cellEdges[r];
        bd = cellEdges[(r + 1) % 4];
        cd = cellEdges[(r + 2) % 4];
        ac = cellEdges[(r + 3) % 4];


        ay = pointHeights[r];
        by = pointHeights[(r + 1) % 4];
        dy = pointHeights[(r + 2) % 4];
        cy = pointHeights[(r + 3) % 4];
    }

    void AddFullFloor()
    {
        startFloor();
        if (higherPolyFloors)
        {
            var ey = (ay + by + cy + dy) / 4;
            AddFace(
                AddPoint(0, ay, 0),
                AddPoint(1, by, 0),
                AddPoint(0.5f, ey, 0.5f, 0, 0, true)
            );

            AddFace(
                AddPoint(1, by, 0),
                AddPoint(1, dy, 1),
                AddPoint(0.5f, ey, 0.5f, 0, 0, true)
            );

            AddFace(
                AddPoint(1, dy, 1),
                AddPoint(0, cy, 1),
                AddPoint(0.5f, ey, 0.5f, 0, 0, true)
            );

            AddFace(
                AddPoint(0, cy, 1),
                AddPoint(0, ay, 0),
                AddPoint(0.5f, ey, 0.5f, 0, 0, true)
            );
        }
        else
        {
            AddFace(

                AddPoint(0, ay, 0),
                AddPoint(1, by, 0),
                AddPoint(0, cy, 1)
            );

            AddFace(
                AddPoint(1, dy, 1),
                AddPoint(0, cy, 1),
                AddPoint(1, by, 0)
            );
        }
    }

    void AddOuterCorner(bool floorBelow = true, bool floorAbove = true, bool flattenBottom = false, float bottomHeight = -1)
    {


        float edgeBy = flattenBottom ? bottomHeight : by;
        float edgeCy = flattenBottom ? bottomHeight : cy;

        if (floorAbove)
        {
            startFloor();
            AddFace(
                AddPoint(0, ay, 0, 0, 0),
                AddPoint(0.5f, ay, 0, 0, 1),
                AddPoint(0, ay, 0.5f, 0, 1)
            );
        }

        startWall();
        AddFace(
            AddPoint(0, edgeCy, 0.5f, 0, 0),
            AddPoint(0, ay, 0.5f, 0, 1),
            AddPoint(0.5f, edgeBy, 0, 1, 0)
        );

        AddFace(
            AddPoint(0.5f, ay, 0, 1, 1),
            AddPoint(0.5f, edgeBy, 0, 1, 0),
            AddPoint(0, ay, 0.5f, 0, 1)
        );

        if (floorBelow)
        {
            startFloor();
            AddFace(
                AddPoint(1, dy, 1),
                AddPoint(0, cy, 1),
                AddPoint(1, by, 0)
            );

            AddFace(
                AddPoint(0, cy, 1),
                AddPoint(0, cy, 0.5f, 1, 0),
                AddPoint(0.5f, by, 0, 1, 0)
            );

            AddFace(
                AddPoint(1, by, 0),
                AddPoint(0, cy, 1),
                AddPoint(0.5f, by, 0, 1, 0)
            );
        }

    }


    void AddEdge(bool floorBelow, bool floorAbove, float aX = 0, float bX = 1)
    {

        var edgeAy = ab ? ay : Mathf.Min(ay, by);
        var edgeBy = ab ? by : Mathf.Min(ay, by);
        var edgeCy = cd ? cy : Mathf.Max(cy, dy);
        var edgeDy = cd ? dy : Mathf.Max(cy, dy);

        if (floorAbove)
        {
            startFloor();
            AddFace(
                AddPoint(
                    aX,
                    edgeAy,
                    0,
                    aX > 0 ? 1 : 0,
                    0
                ),
                AddPoint(
                    bX,
                    edgeBy,
                    0,
                    bX < 1 ? 1 : 0,
                    0
                ),
                AddPoint(
                    0,
                    edgeAy,
                    0.5f,
                    bX < 1 ? -1 : (aX > 0 ? 1 : 0),
                    1
                )
            );

            AddFace(
                AddPoint(
                    1,
                    edgeBy,
                    0.5f,
                    aX > 0 ? -1 : (bX < 1 ? 1 : 0),
                    1
                ),
                AddPoint(
                    0,
                    edgeAy,
                    0.5f,
                    bX < 1 ? -1 : (aX > 0 ? 1 : 0),
                    1
                ),
                AddPoint(
                    bX,
                    edgeBy,
                    0,
                    bX < 1 ? 1 : 0,
                    0
                )
            );
        }

        startWall();
        AddFace(
            AddPoint(0, edgeCy, 0.5f, 0, 0),
            AddPoint(0, edgeAy, 0.5f, 0, 1),
            AddPoint(1, edgeDy, 0.5f, 1, 0)
        );

        AddFace(
            AddPoint(1, edgeBy, 0.5f, 1, 1),
            AddPoint(1, edgeDy, 0.5f, 1, 0),
            AddPoint(0, edgeAy, 0.5f, 0, 1)
        );
        if (floorBelow)
        {
            startFloor();
            AddFace(
                AddPoint(0, cy, 0.5f, 1, 0),
                AddPoint(1, dy, 0.5f, 1, 0),
                AddPoint(0, cy, 1)
            );
            AddFace(
                AddPoint(1, dy, 1),
                AddPoint(0, cy, 1),
                AddPoint(1, dy, 0.5f, 1, 0)
            );
        }
    }

    void AddInnerCorner(bool lowerFloor = true, bool fullUpperFloor = true, bool flatten = false, bool bdFloor = false, bool cdFloor = false)
    {


        var cornerBy = flatten ? Mathf.Min(by, cy) : by;
        var cornerCy = flatten ? Mathf.Min(by, cy) : cy;

        if (lowerFloor)
        {
            startFloor();
            AddFace(
                AddPoint(0, ay, 0),
                AddPoint(0.5f, ay, 0, 1, 0),
                AddPoint(0, ay, 0.5f, 1, 0)
            );
        }
        startWall();
        AddFace(
            AddPoint(0, ay, 0.5f, 1, 0),
            AddPoint(0.5f, ay, 0, 0, 0),
            AddPoint(0, cornerCy, 0.5f, 1, 1)
        );

        AddFace(
            AddPoint(0.5f, cornerBy, 0, 0, 1),
            AddPoint(0, cornerCy, 0.5f, 1, 1),
            AddPoint(0.5f, ay, 0, 0, 0)
        );
        startFloor();
        if (fullUpperFloor)
        {
            AddFace(
                AddPoint(1, dy, 1),
                AddPoint(0, cornerCy, 1),
                AddPoint(1, cornerBy, 0)
            );
            AddFace(
                AddPoint(0, cornerCy, 1),
                AddPoint(0, cornerCy, 0.5f, 0, 1),
                AddPoint(0.5f, cornerBy, 0, 0, 1)
            );
            AddFace(
                AddPoint(1, cornerBy, 0),
                AddPoint(0, cornerCy, 1),
                AddPoint(0.5f, cornerBy, 0, 0, 1)
            );
        }
        if (cdFloor)
        {
            AddFace(
                AddPoint(1, by, 0, 0, 0),
                AddPoint(0, by, 0.5f, 1, 1),
                AddPoint(0.5f, by, 0, 0, 1)
            );

            AddFace(
                AddPoint(1, by, 0, 0, 0),
                AddPoint(1, by, 0.5f, 1, -1),
                AddPoint(0, by, 0.5f, 1, 1)
            );
        }

        if (bdFloor)
        {
            AddFace(
                AddPoint(0, cy, 0.5f, 0, 1),
                AddPoint(0.5f, cy, 0, 1, 1),
                AddPoint(0, cy, 1, 0, 0)
            );
            AddFace(
                AddPoint(0.5f, cy, 1, 1, -1),
                AddPoint(0, cy, 1, 0, 0),
                AddPoint(0.5f, cy, 0, 1, 1)
            );
        }
    }

    void AddDiagonalFloor(float bY, float cY, bool aCliff, bool dCliff)
    {

        startFloor();
        AddFace(
            AddPoint(1, bY, 0),
            AddPoint(0, cY, 1),
            AddPoint(
                0.5f,
                bY,
                0,
                aCliff ? 0 : 1,
                aCliff ? 1 : 0
            )
        );

        AddFace(
            AddPoint(0, cY, 1),
            AddPoint(
                0,
                cY,
                0.5f,
                aCliff ? 0 : 1,
                aCliff ? 1 : 0
            ),
            AddPoint(
                0.5f,
                bY,
                0,
                aCliff ? 0 : 1,
                aCliff ? 1 : 0
            )
        );

        AddFace(
            AddPoint(1, bY, 0),
            AddPoint(
                1,
                bY,
                0.5f,
                dCliff ? 0 : 1,
                dCliff ? 1 : 0
            ),
            AddPoint(0, cY, 1)
        );
        AddFace(
            AddPoint(0, cY, 1),
            AddPoint(
                1,
                bY,
                0.5f,
                dCliff ? 0 : 1,
                dCliff ? 1 : 0
            ),
            AddPoint(
                0.5f,
                cY,
                1,
                dCliff ? 0 : 1,
                dCliff ? 1 : 0
            )
        );
    }

    public void GenerateHeightmap(NoiseSettings ns)
    {
        for (int z = 0; z < terrain.dimensions.z; z++)
        {
            for (int x = 0; x < terrain.dimensions.x; x++)
            {
                LibNoise.Generator.Perlin perlin = new LibNoise.Generator.Perlin(
                    ns.frequency,
                    ns.lacunarity,
                    ns.persistence,
                    ns.octaves,
                    ns.seed,
                    LibNoise.QualityMode.High
                );
                var point = new Vector3(x, 0, z);
                //Convert local coordinates (0 - terrain.dimensions) to world coordinates (x - terrain.dimensions * cellSize)
                float wX = (chunkPosition.x * (terrain.dimensions.x - 1)) + x;
                float wZ = (chunkPosition.y * (terrain.dimensions.z - 1)) + z;

                float noiseValue = (float)perlin.GetValue((wX * ns.scale) + ns.offset.x, (wZ * ns.scale) + ns.offset.y, 0);
                //Quantize noiseValue based on terrain.heightBanding
                if (noiseValue != 0) 
                    noiseValue = Mathf.Round(noiseValue * terrain.heightBanding) / terrain.heightBanding;


                heightMap[getIndex(z, x)] = noiseValue;
            }
        }
        regenerateAllCells();
        regenerateMesh();
    }

    public void drawHeight(int x, int z, float y)
    {
        heightMap[getIndex(z, x)] = y;
        notifyNeedsUpdate(z, x);
        notifyNeedsUpdate(z, x - 1);
        notifyNeedsUpdate(z - 1, x);
        notifyNeedsUpdate(z - 1, x - 1);
        regenerateMesh();
    }

    public void drawColor(int x, int z, Color color)
    {
        colorMap[getIndex(x, z)] = color;
        notifyNeedsUpdate(z, x);
        notifyNeedsUpdate(z, x - 1);
        notifyNeedsUpdate(z - 1, x);
        notifyNeedsUpdate(z - 1, x - 1);
        regenerateMesh();
    }

    void notifyNeedsUpdate(int z, int x)
    {
        //Return if out of bounds
        if (x < 0 || x >= terrain.dimensions.x || z < 0 || z >= terrain.dimensions.z)
            return;

        needsUpdate[getIndex(z, x)] = true;
    }

    void regenerateAllCells()
    {
        for (int z = 0; z < terrain.dimensions.z - 1; z++)
        {
            for (int x = 0; x < terrain.dimensions.x - 1; x++)
            {
                needsUpdate[getIndex(z, x)] = true;
            }
        }
    }

    Vector3 AddPoint(float x, float y, float z, float uvX = 0, float uvY = 0, bool diagMidpoint = false)
    {
        for (int i = 0; i < r; i++)
        {
            var temp = x;
            x = 1 - z;
            z = temp;
        }

        var uv = floorMode ? new Vector2(uvX, uvY) : new Vector2(1, 1);

        Color color = new Color(1, 1, 1, 1);
        if (diagMidpoint)
        {
            int idx1 = getIndex(cellCoords.x, cellCoords.y);
            int idx2 = getIndex(cellCoords.x + 1, cellCoords.y);
            int idx3 = getIndex(cellCoords.x, cellCoords.y + 1);
            int idx4 = getIndex(cellCoords.x + 1, cellCoords.y + 1);

            var adColor = Color.Lerp(colorMap[idx1], colorMap[idx4], .5f);

            var bcColor = Color.Lerp(colorMap[idx2], colorMap[idx3], .5f);
            color = new Color(
                Mathf.Min(adColor.r, bcColor.r),
                Mathf.Min(adColor.g, bcColor.g),
                Mathf.Min(adColor.b, bcColor.b),
                Mathf.Min(adColor.a, bcColor.a)
            );

            if (adColor.r > 0.99 || bcColor.r > 0.99)
                color.r = 1;
            if (adColor.g > 0.99 || bcColor.g > 0.99)
                color.g = 1;
            if (adColor.b > 0.99 || bcColor.b > 0.99)
                color.b = 1;
            if (adColor.a > 0.99 || bcColor.a > 0.99)
                color.a = 1;
        }
        else
        {
            int idx = getIndex(cellCoords.x, cellCoords.y);
            int idx2 = getIndex(cellCoords.x + 1, cellCoords.y);
            int idx3 = getIndex(cellCoords.x, cellCoords.y + 1);
            int idx4 = getIndex(cellCoords.x + 1, cellCoords.y + 1);

            var abColor = Color.Lerp(
                colorMap[idx],
                colorMap[idx2],
                x
            );
            var cdColor = Color.Lerp(
                colorMap[idx3],
                colorMap[idx4],
                x
            );
            color = Color.Lerp(abColor, cdColor, z);
        }

        colors.Add(color);
        Vector3 vert = new Vector3(
            (cellCoords.x + x) * terrain.cellSize.x,
            y,
            (cellCoords.y + z) * terrain.cellSize.y
        );
        uvs.Add(uv);
        cellGeometry[cellCoords].uvs.Add(uv);
        cellGeometry[cellCoords].colors.Add(color);

        return vert;
    }

    void AddFace(Vector3 v0, Vector3 v1, Vector3 v2, bool cache = true)
    {
        int vertexIdx = vertices.Count;

        vertices.Add(v0);
        vertices.Add(v1);
        vertices.Add(v2);
        triangles.Add(vertexIdx + 2);
        triangles.Add(vertexIdx + 1);
        triangles.Add(vertexIdx);

        if (cache)
        {
            int cachedVertexIdx = cellGeometry[cellCoords].vertices.Count;
            cellGeometry[cellCoords].vertices.Add(v0);
            cellGeometry[cellCoords].vertices.Add(v1);
            cellGeometry[cellCoords].vertices.Add(v2);
        }


    }

    bool isHigher(float a, float b)
    {
        return a - b > terrain.mergeThreshold;
    }

    bool isLower(float a, float b)
    {
        return a - b < -terrain.mergeThreshold;
    }

    bool isMerged(float a, float b)
    {
        return Mathf.Abs(a-b) < terrain.mergeThreshold;
    }


    void startFloor()
    {
        floorMode = true;
    }
    void startWall()
    {
        floorMode = false;
    }

}
