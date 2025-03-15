using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using System.Linq;
using Unity.Burst;
using System.Runtime.CompilerServices;
using UnityEditor;
using Unity.Collections.LowLevel.Unsafe;

//Job to generate the mesh for this chunk
[System.Serializable]
[BurstCompile(CompileSynchronously = true)]
struct GenerateChunkJob : IJobParallelFor
{

    [ReadOnly]
    [DeallocateOnJobCompletion]
    public NativeArray<float> heightMap;

    public int3 terrainSize;
    public float2 cellSize;
    public float mergeThreshold;

    bool floorMode;

    public bool higherPolyFloors;

    [NativeDisableParallelForRestriction]
    public NativeList<float3> vertices;
    [NativeDisableParallelForRestriction]
    public NativeList<float3> normals;
    [NativeDisableParallelForRestriction]
    public NativeList<float2> uvs;
    [NativeDisableParallelForRestriction]
    public NativeList<int> triangles;
    [NativeDisableParallelForRestriction]
    public NativeList<float4> colors;



    [NativeDisableParallelForRestriction]
    [DeallocateOnJobCompletion]
    public NativeArray<bool> cellEdges;

    [NativeDisableParallelForRestriction]
    [DeallocateOnJobCompletion]
    public NativeArray<float> pointHeights;

    [ReadOnly]
    [DeallocateOnJobCompletion]
    public NativeArray<float4> colorMap;

    int r;

    float ay;
    float by;
    float cy;
    float dy;

    bool ab;
    bool ac;
    bool bd;
    bool cd;

    int2 cellCoords;

    public int GetIndex(int x, int z)
    {
        //Check if within bounds
        if (x < 0 || x >= terrainSize.x || z < 0 || z >= terrainSize.z)
        {
            return 0;
        }

        return x + z * terrainSize.x;
    }

    int2 GetCoordinates(int index)
    {
        return new int2(index / terrainSize.x, index % terrainSize.x);
    }

    public void Execute(int index)
    {
        cellCoords = GetCoordinates(index);

        if (cellCoords.x > terrainSize.x - 2 || cellCoords.y > terrainSize.z - 2)
        {
            return;
        }


        int ayIndex = index;
        int byIndex = GetIndex(cellCoords.y, cellCoords.x + 1);
        int cyIndex = GetIndex(cellCoords.y + 1, cellCoords.x);
        int dyIndex = GetIndex(cellCoords.y + 1, cellCoords.x + 1);

        r = 0;

        ay = heightMap[ayIndex];
        by = heightMap[byIndex];
        cy = heightMap[cyIndex];
        dy = heightMap[dyIndex];

        ab = Mathf.Abs(ay - by) < mergeThreshold; // Top Edge
        ac = Mathf.Abs(ay - cy) < mergeThreshold; // Bottom Edge
        bd = Mathf.Abs(by - dy) < mergeThreshold; // Right Edge
        cd = Mathf.Abs(cy - dy) < mergeThreshold; // Left Edge

        //Case 0
        if (ab && ac && bd && cd)
        {
            AddFullFloor();
            return;
        }

        cellEdges[0] = ab;
        cellEdges[1] = bd;
        cellEdges[2] = cd;
        cellEdges[3] = ac;
        pointHeights[0] = ay;
        pointHeights[1] = by;
        pointHeights[2] = dy;
        pointHeights[3] = cy;



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
            if (IsHigher(ay, by) && IsHigher(ay, cy) && bd && cd)
            {
                AddOuterCorner(true, true);
            }

            //Case 2
            else if (IsHigher(ay, cy) && IsHigher(by, dy) && ab && cd)
            {
                AddEdge(true, true);
            }

            //Case 3
            else if (IsHigher(ay, by) && IsHigher(ay, cy) && IsHigher(by, dy) && cd)
            {
                AddEdge(true, true, 0.5f, 1);
                AddOuterCorner(false, true, true, by);
            }
            //Case 4
            else if (IsHigher(by, ay) && IsHigher(ay, cy) && IsHigher(by, dy) && cd)
            {
                AddEdge(true, true, 0, 0.5f);
                RotateCell(1);
                AddOuterCorner(false, true, true, cy);
            }

            //Case5
            else if (IsLower(ay, by) && IsLower(ay, cy) && IsLower(dy, by) && IsLower(dy, cy) && IsMerged(by, cy))
            {
                AddInnerCorner(true, false);
                AddDiagonalFloor(by, cy, true, true);
                RotateCell(2);
                AddInnerCorner(true, false);
            }
            //Case 5.5
            else if (IsLower(ay, by) && IsLower(ay, cy) && IsLower(dy, by) && IsLower(dy, cy) && IsHigher(by, cy))
            {
                AddInnerCorner(true, false, true);
                AddDiagonalFloor(cy, cy, true, true);

                RotateCell(2);
                AddInnerCorner(true, false, true);

                RotateCell(-1);
                AddOuterCorner(false, true);
            }
            //Case 6
            else if (IsLower(ay, by) && IsLower(ay, cy) && bd && cd)
            {
                AddInnerCorner(true, true);
            }

            //Case 7
            else if (IsLower(ay, by) && IsLower(ay, cy) && IsHigher(dy, by) && IsHigher(dy, cy) && IsMerged(by, cy))
            {

                AddInnerCorner(true, false);
                AddDiagonalFloor(by, cy, true, false);
                RotateCell(2);
                AddOuterCorner(false, true);
            }
            //Case 8
            else if (IsLower(ay, by) && IsLower(ay, cy) && IsLower(dy, cy) && bd)
            {
                AddInnerCorner(true, false, true);

                StartFloor();
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
                StartWall();
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
                StartFloor();
                AddFace(
                    AddPoint(0, cy, 1),
                    AddPoint(0, cy, 0.5f, 0, 1),
                    AddPoint(0.5f, cy, 1, 0, 1)
                );
            }
            //Case 9
            else if (IsLower(ay, by) && IsLower(ay, cy) && IsLower(dy, by) && cd)
            {

                AddInnerCorner(true, false, true);

                StartFloor();
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
                StartWall();
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

                StartFloor();
                AddFace(
                    AddPoint(1, by, 0),
                    AddPoint(1, by, 0.5f, 0, 1),
                    AddPoint(0.5f, by, 0, 0, 1)
                );
            }
            //Case 10
            else if (IsLower(ay, by) && IsLower(ay, cy) && IsHigher(dy, cy) && bd)
            {
                AddInnerCorner(true, false, true, true, false);

                RotateCell(1);
                AddEdge(false, true);
            }
            //Case 11
            else if (IsLower(ay, by) && IsLower(ay, cy) && IsHigher(dy, by) && cd)
            {
                AddInnerCorner(true, false, true, false, true);

                RotateCell(2);
                AddEdge(false, true);
            }
            //Case 12
            else if (IsLower(ay, by) && IsLower(by, dy) && IsLower(dy, cy) && IsHigher(cy, ay))
            {
                AddInnerCorner(true, false, true, false, true);

                RotateCell(2);
                AddEdge(false, true, 0, 0.5f);

                RotateCell(1);
                AddOuterCorner(false, true, true, cy);
            }
            //Case 13
            else if (IsLower(ay, cy) && IsLower(cy, dy) && IsLower(dy, by) && IsHigher(by, ay))
            {
                AddInnerCorner(true, false, true, true, false);

                RotateCell(1);
                AddEdge(false, true, 0.5f, 1);

                AddOuterCorner(false, true, true, by);
            }
            //Case 14
            else if (IsLower(ay, by) && IsLower(by, cy) && IsLower(cy, dy))
            {
                AddInnerCorner(true, false, true, false, true);

                RotateCell(2);
                AddEdge(false, true, 0.5f, 1);

                AddOuterCorner(false, true, true, by);
            }
            //Case 15
            else if (IsLower(ay, cy) && IsLower(cy, by) && IsLower(by, dy))
            {
                AddInnerCorner(true, false, true, true, false);

                RotateCell(1);
                AddEdge(false, true, 0, 0.5f);

                RotateCell(1);
                AddOuterCorner(false, true, true, cy);
            }
            //Case 16
            else if (ab && bd && cd && IsHigher(ay, cy))
            {
                float edgeBy = (by + dy) / 2;
                float edgeDy = (by + dy) / 2;

                StartFloor();

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

                StartWall();
                AddFace(
                    AddPoint(0, cy, 0.5f, 0, 0),
                    AddPoint(0, ay, 0.5f, 0, 1),
                    AddPoint(1, edgeDy, 0.5f, 1, 0)
                );

                StartFloor();
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
            else if (ab && ac && cd && IsHigher(by, dy))
            {
                var edgeAy = (ay + cy) / 2;
                var edgeCy = (ay + cy) / 2;

                StartFloor();
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

                StartWall();
                AddFace(
                    AddPoint(1, by, 0.5f, 1, 1),
                    AddPoint(1, dy, 0.5f, 1, 0),
                    AddPoint(0, edgeAy, 0.5f, 0, 0)
                );

                StartFloor();
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
            Debug.Log("Case not found");
            return;
        }
    }

    void RotateCell(int rotations)
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
        StartFloor();
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
            StartFloor();
            AddFace(
                AddPoint(0, ay, 0, 0, 0),
                AddPoint(0.5f, ay, 0, 0, 1),
                AddPoint(0, ay, 0.5f, 0, 1)
            );
        }

        StartWall();
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
            StartFloor();
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
            StartFloor();
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

        StartWall();
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
            StartFloor();
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
            StartFloor();
            AddFace(
                AddPoint(0, ay, 0),
                AddPoint(0.5f, ay, 0, 1, 0),
                AddPoint(0, ay, 0.5f, 1, 0)
            );
        }
        StartWall();
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
        StartFloor();
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

        StartFloor();
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



    Vector3 AddPoint(float x, float y, float z, float uvX = 0, float uvY = 0, bool diagMidpoint = false)
    {
        for (int i = 0; i < r; i++)
        {
            var temp = x;
            x = 1 - z;
            z = temp;
        }

        var uv = floorMode ? new Vector2(uvX, uvY) : new Vector2(1, 1);

        float4 color = new float4(1, 1, 1, 1);
        if (diagMidpoint)
        {
            int idx1 = GetIndex(cellCoords.x, cellCoords.y);
            int idx2 = GetIndex(cellCoords.x + 1, cellCoords.y);
            int idx3 = GetIndex(cellCoords.x, cellCoords.y + 1);
            int idx4 = GetIndex(cellCoords.x + 1, cellCoords.y + 1);

            var adColor = math.lerp(colorMap[idx1], colorMap[idx4], .5f);

            var bcColor = math.lerp(colorMap[idx2], colorMap[idx3], .5f);
            color = new float4(
                Mathf.Min(adColor.x, bcColor.x),
                Mathf.Min(adColor.y, bcColor.y),
                Mathf.Min(adColor.z, bcColor.z),
                Mathf.Min(adColor.w, bcColor.w)
            );

            if (adColor.x > 0.99 || bcColor.x > 0.99)
                color.x = 1;
            if (adColor.y > 0.99 || bcColor.y > 0.99)
                color.y = 1;
            if (adColor.z > 0.99 || bcColor.z > 0.99)
                color.z = 1;
            if (adColor.w > 0.99 || bcColor.w > 0.99)
                color.w = 1;
        }
        else
        {
            int idx1 = GetIndex(cellCoords.x, cellCoords.y);
            int idx2 = GetIndex(cellCoords.x + 1, cellCoords.y);
            int idx3 = GetIndex(cellCoords.x, cellCoords.y + 1);
            int idx4 = GetIndex(cellCoords.x + 1, cellCoords.y + 1);

            var abColor = math.lerp(
                colorMap[idx1],
                colorMap[idx2],
                x
            );
            var cdColor = math.lerp(
                colorMap[idx3],
                colorMap[idx4],
                x
            );
            color = math.lerp(abColor, cdColor, z);
        }

        colors.Add(new float4(color.x,color.y,color.z,color.w));
        float3 vert = new float3(
            (cellCoords.x + x) * cellSize.x,
            y,
            (cellCoords.y + z) * cellSize.y
        );
        uvs.Add(uv);
        return vert;
    }

    void AddFace(float3 v0, float3 v1, float3 v2)
    {
        var vertexCount = vertices.Length;
        vertices.Add(v0);
        vertices.Add(v1);
        vertices.Add(v2);

        triangles.Add(vertexCount + 2);
        triangles.Add(vertexCount + 1);
        triangles.Add(vertexCount);

        float3 normal = -math.normalize(math.cross(v1 - v0, v2 - v0));
        
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
    }

    bool IsHigher(float a, float b)
    {
        return a - b > mergeThreshold;
    }

    bool IsLower(float a, float b)
    {
        return a - b < - mergeThreshold;
    }

    bool IsMerged(float a, float b)
    {
        return Mathf.Abs(a - b) < mergeThreshold;
    }


    void StartFloor()
    {
        floorMode = true;
    }
    void StartWall()
    {
        floorMode = false;
    }
}



[ExecuteInEditMode]
public class MarchingSquaresChunk : MonoBehaviour
{
    public Mesh mesh;

    //Mesh data
    NativeArray<float3> vertices;
    NativeArray<float4> colors;
    NativeArray<int>    triangles;
    NativeArray<float2> uvs;
    NativeArray<float3> normals;

    public List<float3> vertCache;
    public List<float3> normCache;
    public List<int>    triCache;

    public MarchingSquaresTerrain terrain;
    public Vector2Int chunkPosition;

    public bool higherPolyFloors = true;

    public float[] heightMap;
    public float4[] colorMap;

    public bool IsDirty = false;

    public List<MarchingSquaresChunk> neighboringChunks = new List<MarchingSquaresChunk>();


    public void InitializeTerrain(bool shouldRegenerate = true)
    {
        mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        InitializeColorMap();
        InitializeHeightMap();
        RegenerateMesh();
    }

    void InitializeColorMap()
    {
        colorMap = new float4[terrain.dimensions.z * terrain.dimensions.x];
        for(int z = 0; z < terrain.dimensions.z; z++)
        {
            for (int x = 0; x < terrain.dimensions.x; x++)
            {
                colorMap[GetIndex(x,z)] = new float4(1, 0, 0, 0);
            }
        }
    }

    void InitializeHeightMap()
    {
        heightMap = new float[terrain.dimensions.z * terrain.dimensions.x];
    }

    public int GetIndex(int x, int z)
    {
        //Check if within bounds
        if (x < 0 || x >= terrain.dimensions.x || z < 0 || z >= terrain.dimensions.z)
        {
            print("Out of bounds");
            return 0;
        }

        return x + z * terrain.dimensions.x;
    }

    public void RegenerateMesh()
    {
        mesh.Clear();

        GenerateTerrainCells();

        MeshRenderer r = gameObject.GetComponent<MeshRenderer>();
        MeshFilter mf = gameObject.GetComponent<MeshFilter>();

        r.material = terrain.terrainMaterial;
        mf.sharedMesh = mesh;

        MeshCollider mc = gameObject.GetComponent<MeshCollider>();
        mc.sharedMesh = mf.sharedMesh;

        IsDirty = false;
    }

    private void OnDestroy()
    {
    }

    public void GenerateTerrainCells()
    {

        

        GenerateChunkJob job = new GenerateChunkJob()
        {
            //Data
            heightMap    = new NativeArray<float>(heightMap, Allocator.TempJob),
            colorMap     = new NativeArray<float4>(colorMap, Allocator.TempJob),

            //Mesh data
            vertices     = new NativeList<float3>(0,Allocator.Persistent),
            colors       = new NativeList<float4>(0, Allocator.Persistent),
            uvs          = new NativeList<float2>(0, Allocator.Persistent),
            triangles    = new NativeList<int>(0, Allocator.Persistent),
            normals      = new NativeList<float3>(0, Allocator.Persistent),

            cellEdges    = new NativeArray<bool>(new bool[4]{ false, false, false, false},Allocator.Persistent),
            pointHeights = new NativeArray<float>(new float[4] { 0, 0, 0, 0 }, Allocator.Persistent),
            
            //Config
            higherPolyFloors = false,
            cellSize         = terrain.cellSize,
            terrainSize      = new int3(terrain.dimensions.x, 0, terrain.dimensions.z),
            mergeThreshold   = terrain.mergeThreshold,
        };

        
        
        int totalLoop = (terrain.dimensions.x) * (terrain.dimensions.z);

        JobHandle handle = job.Schedule(totalLoop, terrain.dimensions.x * terrain.dimensions.z);

        handle.Complete();

        vertices  = job.vertices.AsArray();
        colors    = job.colors.AsArray();
        triangles = job.triangles.AsArray();
        uvs       = job.uvs.AsArray();
        normals   = job.normals.AsArray();

        //memcpy vertices into vertCache
        vertCache = new List<float3>(vertices.Length);
        normCache = new List<float3>(normals.Length);
        triCache = new List<int>(triangles.Length);
        for (int i = 0; i < vertices.Length; i++)
            vertCache.Add(vertices[i]);

        for (int i = 0; i < normals.Length; i++)
            normCache.Add(normals[i]);

        for (int i = 0; i < triangles.Length; i++)
            triCache.Add(triangles[i]);


        mesh.SetVertices<float3>(vertices);
        mesh.SetColors<float4>(colors);
        mesh.SetIndices(triangles, MeshTopology.Triangles, 0);
        mesh.SetUVs<float2>(0, uvs);
        mesh.Optimize();
        mesh.RecalculateNormals(45);

        job.vertices.Dispose();
        job.colors.Dispose();
        job.triangles.Dispose();
        job.uvs.Dispose();
        job.normals.Dispose();

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

                switch (ns.mixMode)
                {
                    case NoiseMixMode.Add:
                        heightMap[GetIndex(z, x)] += noiseValue;
                        break;
                    case NoiseMixMode.Subtract:
                        heightMap[GetIndex(z, x)] -= noiseValue;
                        break;
                    case NoiseMixMode.Multiply:
                        heightMap[GetIndex(z, x)] *= noiseValue;
                        break;
                    case NoiseMixMode.Replace:
                        heightMap[GetIndex(z, x)] = noiseValue;
                        break;
                }
            }
        }
        RegenerateMesh();
    }
    public bool inBounds(int x, int z)
    {
        return x >= 0 && x < terrain.dimensions.x && z >= 0 && z < terrain.dimensions.z;
    }

    public void DrawHeight(int x, int z, float y,bool setHeight = false)
    {
        //Within bounds?
        if (!inBounds(z, x))
            return;

        heightMap[GetIndex(z, x)] = setHeight ? y : heightMap[GetIndex(z, x)] + y;
        IsDirty = true;
    }


    public void DrawHeights(List<Vector2Int> positions, float height, bool setHeight, bool smooth)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            if (!inBounds(positions[i].y, positions[i].x))
                continue;
            if (smooth)
            {
                heightMap[GetIndex(positions[i].y, positions[i].x)] = Mathf.Lerp(heightMap[GetIndex(positions[i].y, positions[i].x)], height, 0.5f);
            }
            else
            {
                if (setHeight)
                    heightMap[GetIndex(positions[i].y, positions[i].x)] = height;
                else
                    heightMap[GetIndex(positions[i].y, positions[i].x)] += height;
            }
        }

        IsDirty = true;
    }

    public void DrawColor(int x, int z, Color color)
    {
        if (!inBounds(x, z))
            return;
        colorMap[GetIndex(x, z)] = color.ToFloat4();
        IsDirty = true;
    }

    internal void DrawColors(List<Vector2Int> value, Color color)
    {
        for (int i = 0; i < value.Count; i++)
        {
            if (!inBounds((int)value[i].x, (int)value[i].y))
                continue;
            colorMap[GetIndex((int)value[i].x, (int)value[i].y)] = color.ToFloat4();
        }
        IsDirty = true;
    }
}
