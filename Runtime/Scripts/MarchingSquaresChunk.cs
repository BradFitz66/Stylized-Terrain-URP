using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Burst;
using UnityEditor;
using UnityEngine.Profiling;
using UnityEngine.Serialization;

[BurstCompile(CompileSynchronously = true)]
internal struct GenerateChunkJob : IJobParallelFor
{

    [ReadOnly]
    [DeallocateOnJobCompletion]
    public NativeArray<float> HeightMap;

    public int3 TerrainSize;
    public float2 CellSize;
    public float MergeThreshold;

    private bool _floorMode;

    public bool HigherPolyFloors;

    [NativeDisableParallelForRestriction]
    public NativeList<float3> Vertices;
    [NativeDisableParallelForRestriction]
    public NativeList<float3> Normals;
    [NativeDisableParallelForRestriction]
    public NativeList<float2> Uvs;
    [NativeDisableParallelForRestriction]
    public NativeList<int> Triangles;
    [NativeDisableParallelForRestriction]
    public NativeList<float4> Colors;



    [NativeDisableParallelForRestriction]
    [DeallocateOnJobCompletion]
    public NativeArray<bool> CellEdges;

    [NativeDisableParallelForRestriction]
    [DeallocateOnJobCompletion]
    public NativeArray<float> PointHeights;

    [ReadOnly]
    [DeallocateOnJobCompletion]
    public NativeArray<float4> ColorMap;

    int _r;

    private float _ay;
    private float _by;
    private float _cy;
    private float _dy;

    private bool _ab;
    private bool _ac;
    private bool _bd;
    private bool _cd;

    private int2 _cellCoords;
    public int GetIndex(int x, int z)
    {
        //Check if within bounds
        if (x < 0 || x >= TerrainSize.x || z < 0 || z >= TerrainSize.z)
        {
            return 0;
        }

        return x + z * TerrainSize.x;
    }

    int2 GetCoordinates(int index)
    {
        return new int2(index / TerrainSize.x, index % TerrainSize.x);
    }

    public void Execute(int index)
    {
        _cellCoords = GetCoordinates(index);

        if (_cellCoords.x > TerrainSize.x - 2 || _cellCoords.y > TerrainSize.z - 2)
        {
            return;
        }


        int ayIndex = index;
        int byIndex = GetIndex(_cellCoords.y, _cellCoords.x + 1);
        int cyIndex = GetIndex(_cellCoords.y + 1, _cellCoords.x);
        int dyIndex = GetIndex(_cellCoords.y + 1, _cellCoords.x + 1);

        _r = 0;

        _ay = HeightMap[ayIndex];
        _by = HeightMap[byIndex];
        _cy = HeightMap[cyIndex];
        _dy = HeightMap[dyIndex];

        _ab = Mathf.Abs(_ay - _by) < MergeThreshold; // Top Edge
        _ac = Mathf.Abs(_ay - _cy) < MergeThreshold; // Bottom Edge
        _bd = Mathf.Abs(_by - _dy) < MergeThreshold; // Right Edge
        _cd = Mathf.Abs(_cy - _dy) < MergeThreshold; // Left Edge

        //Case 0
        if (_ab && _ac && _bd && _cd)
        {
            AddFullFloor();
            return;
        }

        CellEdges[0] = _ab;
        CellEdges[1] = _bd;
        CellEdges[2] = _cd;
        CellEdges[3] = _ac;
        PointHeights[0] = _ay;
        PointHeights[1] = _by;
        PointHeights[2] = _dy;
        PointHeights[3] = _cy;



        bool caseFound = false;

        for (int i = 0; i < 4; i++)
        {
            _r = i;

            _ab = CellEdges[_r];
            _bd = CellEdges[(_r + 1) % 4];
            _cd = CellEdges[(_r + 2) % 4];
            _ac = CellEdges[(_r + 3) % 4];

            _ay = PointHeights[_r];
            _by = PointHeights[(_r + 1) % 4];
            _dy = PointHeights[(_r + 2) % 4];
            _cy = PointHeights[(_r + 3) % 4];

            caseFound = true;

            //Case 1
            if (IsHigher(_ay, _by) && IsHigher(_ay, _cy) && _bd && _cd)
            {
                AddOuterCorner(true, true);
            }

            //Case 2
            else if (IsHigher(_ay, _cy) && IsHigher(_by, _dy) && _ab && _cd)
            {
                AddEdge(true, true);
            }

            //Case 3
            else if (IsHigher(_ay, _by) && IsHigher(_ay, _cy) && IsHigher(_by, _dy) && _cd)
            {
                AddEdge(true, true, 0.5f, 1);
                AddOuterCorner(false, true, true, _by);
            }
            //Case 4
            else if (IsHigher(_by, _ay) && IsHigher(_ay, _cy) && IsHigher(_by, _dy) && _cd)
            {
                AddEdge(true, true, 0, 0.5f);
                RotateCell(1);
                AddOuterCorner(false, true, true, _cy);
            }

            //Case5
            else if (IsLower(_ay, _by) && IsLower(_ay, _cy) && IsLower(_dy, _by) && IsLower(_dy, _cy) && IsMerged(_by, _cy))
            {
                AddInnerCorner(true, false);
                AddDiagonalFloor(_by, _cy, true, true);
                RotateCell(2);
                AddInnerCorner(true, false);
            }
            //Case 5.5
            else if (IsLower(_ay, _by) && IsLower(_ay, _cy) && IsLower(_dy, _by) && IsLower(_dy, _cy) && IsHigher(_by, _cy))
            {
                AddInnerCorner(true, false, true);
                AddDiagonalFloor(_cy, _cy, true, true);

                RotateCell(2);
                AddInnerCorner(true, false, true);

                RotateCell(-1);
                AddOuterCorner(false, true);
            }
            //Case 6
            else if (IsLower(_ay, _by) && IsLower(_ay, _cy) && _bd && _cd)
            {
                AddInnerCorner(true, true);
            }

            //Case 7
            else if (IsLower(_ay, _by) && IsLower(_ay, _cy) && IsHigher(_dy, _by) && IsHigher(_dy, _cy) && IsMerged(_by, _cy))
            {

                AddInnerCorner(true, false);
                AddDiagonalFloor(_by, _cy, true, false);
                RotateCell(2);
                AddOuterCorner(false, true);
            }
            //Case 8
            else if (IsLower(_ay, _by) && IsLower(_ay, _cy) && IsLower(_dy, _cy) && _bd)
            {
                AddInnerCorner(true, false, true);

                StartFloor();
                AddFace(
                    AddPoint(1, _dy, 1),
                    AddPoint(0.5f, _dy, 1, 1, 0),
                    AddPoint(1, (_by + _dy) / 2, 0.5f)
                );

                AddFace(
                    AddPoint(1, _by, 0),
                    AddPoint(1, (_by + _dy) / 2, 0.5f),
                    AddPoint(0.5f, _by, 0, 0, 1)
                );

                AddFace(
                    AddPoint(0.5f, _by, 0, 0, 1),
                    AddPoint(1, (_by + _dy) / 2, 0.5f),
                    AddPoint(0, _by, 0.5f, 1, 1)
                );

                AddFace(
                    AddPoint(0.5f, _dy, 1, 1, 0),
                    AddPoint(0, _by, 0.5f, 1, 1),
                    AddPoint(1, (_by + _dy) / 2, 0.5f)
                );
                StartWall();
                AddFace(
                    AddPoint(0, _by, 0.5f),
                    AddPoint(0.5f, _dy, 1),
                    AddPoint(0, _cy, 0.5f)
                );
                AddFace(
                    AddPoint(0.5f, _cy, 1),
                    AddPoint(0, _cy, 0.5f),
                    AddPoint(0.5f, _dy, 1)
                );
                StartFloor();
                AddFace(
                    AddPoint(0, _cy, 1),
                    AddPoint(0, _cy, 0.5f, 0, 1),
                    AddPoint(0.5f, _cy, 1, 0, 1)
                );
            }
            //Case 9
            else if (IsLower(_ay, _by) && IsLower(_ay, _cy) && IsLower(_dy, _by) && _cd)
            {

                AddInnerCorner(true, false, true);

                StartFloor();
                //D Corner
                AddFace(
                    AddPoint(1, _dy, 1, 0, 0),
                    AddPoint(0.5f, (_dy + _cy) / 2, 1, 0, 0),
                    AddPoint(1, _dy, 0.5f, 1, 0)
                );
                //C Corner
                AddFace(
                    AddPoint(0, _cy, 1, 0, 0),
                    AddPoint(0, _cy, 0.5f, 0, 1),
                    AddPoint(0.5f, (_dy + _cy) / 2, 1)
                );

                //Center floors
                AddFace(
                    AddPoint(0, _cy, 0.5f, 0, 1),
                    AddPoint(0.5f, _cy, 0, 1, 1),
                    AddPoint(0.5f, (_dy + _cy) / 2, 1, 0, 0)
                );

                AddFace(
                    AddPoint(1, _dy, 0.5f, 1, 0f),
                    AddPoint(0.5f, (_dy + _cy) / 2, 1, 0, 0),
                    AddPoint(0.5f, _cy, 0, 1, 1)
                );

                //Walls to upper corner
                StartWall();
                AddFace(
                    AddPoint(0.5f, _cy, 0),
                    AddPoint(0.5f, _by, 0),
                    AddPoint(1, _dy, 0.5f)
                );

                AddFace(
                    AddPoint(1, _by, 0.5f),
                    AddPoint(1, _dy, 0.5f),
                    AddPoint(0.5f, _by, 0)
                );

                StartFloor();
                AddFace(
                    AddPoint(1, _by, 0),
                    AddPoint(1, _by, 0.5f, 0, 1),
                    AddPoint(0.5f, _by, 0, 0, 1)
                );
            }
            //Case 10
            else if (IsLower(_ay, _by) && IsLower(_ay, _cy) && IsHigher(_dy, _cy) && _bd)
            {
                AddInnerCorner(true, false, true, true, false);

                RotateCell(1);
                AddEdge(false, true);
            }
            //Case 11
            else if (IsLower(_ay, _by) && IsLower(_ay, _cy) && IsHigher(_dy, _by) && _cd)
            {
                AddInnerCorner(true, false, true, false, true);

                RotateCell(2);
                AddEdge(false, true);
            }
            //Case 12
            else if (IsLower(_ay, _by) && IsLower(_by, _dy) && IsLower(_dy, _cy) && IsHigher(_cy, _ay))
            {
                AddInnerCorner(true, false, true, false, true);

                RotateCell(2);
                AddEdge(false, true, 0, 0.5f);

                RotateCell(1);
                AddOuterCorner(false, true, true, _cy);
            }
            //Case 13
            else if (IsLower(_ay, _cy) && IsLower(_cy, _dy) && IsLower(_dy, _by) && IsHigher(_by, _ay))
            {
                AddInnerCorner(true, false, true, true, false);

                RotateCell(1);
                AddEdge(false, true, 0.5f, 1);

                AddOuterCorner(false, true, true, _by);
            }
            //Case 14
            else if (IsLower(_ay, _by) && IsLower(_by, _cy) && IsLower(_cy, _dy))
            {
                AddInnerCorner(true, false, true, false, true);

                RotateCell(2);
                AddEdge(false, true, 0.5f, 1);

                AddOuterCorner(false, true, true, _by);
            }
            //Case 15
            else if (IsLower(_ay, _cy) && IsLower(_cy, _by) && IsLower(_by, _dy))
            {
                AddInnerCorner(true, false, true, true, false);

                RotateCell(1);
                AddEdge(false, true, 0, 0.5f);

                RotateCell(1);
                AddOuterCorner(false, true, true, _cy);
            }
            //Case 16
            else if (_ab && _bd && _cd && IsHigher(_ay, _cy))
            {
                float edgeBy = (_by + _dy) / 2;
                float edgeDy = (_by + _dy) / 2;

                StartFloor();

                AddFace(
                    AddPoint(0, _ay, 0),
                    AddPoint(1, _by, 0),
                    AddPoint(1, edgeBy, 0.5f)
                );
                AddFace(
                    AddPoint(1, edgeBy, 0.5f, 0, 1),
                    AddPoint(0, _ay, 0.5f, 0, 1),
                    AddPoint(0, _ay, 0)
                );

                StartWall();
                AddFace(
                    AddPoint(0, _cy, 0.5f, 0, 0),
                    AddPoint(0, _ay, 0.5f, 0, 1),
                    AddPoint(1, edgeDy, 0.5f, 1, 0)
                );

                StartFloor();
                AddFace(
                    AddPoint(0, _cy, 0.5f, 1, 0),
                    AddPoint(1, edgeDy, 0.5f, 1, 0),
                    AddPoint(0, _cy, 1)
                );

                AddFace(
                    AddPoint(1, _dy, 1),
                    AddPoint(0, _cy, 1),
                    AddPoint(1, edgeDy, 0.5f)
                );
            }
            //Case 17
            else if (_ab && _ac && _cd && IsHigher(_by, _dy))
            {
                var edgeAy = (_ay + _cy) / 2;
                var edgeCy = (_ay + _cy) / 2;

                StartFloor();
                AddFace(
                    AddPoint(0, _ay, 0),
                    AddPoint(1, _by, 0),
                    AddPoint(0, edgeAy, 0.5f)
                );

                AddFace(
                    AddPoint(1, _by, 0.5f, 0, 1),
                    AddPoint(0, edgeAy, 0.5f, 0, 1),
                    AddPoint(1, _by, 0)
                );

                StartWall();
                AddFace(
                    AddPoint(1, _by, 0.5f, 1, 1),
                    AddPoint(1, _dy, 0.5f, 1, 0),
                    AddPoint(0, edgeAy, 0.5f, 0, 0)
                );

                StartFloor();
                AddFace(
                    AddPoint(0, edgeCy, 0.5f, 1, 0),
                    AddPoint(1, _dy, 0.5f, 1, 0),
                    AddPoint(1, _dy, 1)
                );
                AddFace(
                    AddPoint(0, _cy, 1),
                    AddPoint(0, edgeCy, 0.5f),
                    AddPoint(1, _dy, 1)
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

        if (caseFound) return;
        Debug.Log($"Case not found");
        return;
    }

    private void RotateCell(int rotations)
    {
        _r = (_r + 4 + rotations) % 4;

        _ab = CellEdges[_r];
        _bd = CellEdges[(_r + 1) % 4];
        _cd = CellEdges[(_r + 2) % 4];
        _ac = CellEdges[(_r + 3) % 4];


        _ay = PointHeights[_r];
        _by = PointHeights[(_r + 1) % 4];
        _dy = PointHeights[(_r + 2) % 4];
        _cy = PointHeights[(_r + 3) % 4];
    }


    private void AddFullFloor()
    {
        StartFloor();
        if (HigherPolyFloors)
        {
            var ey = (_ay + _by + _cy + _dy) / 4;
            AddFace(
                AddPoint(0, _ay, 0),
                AddPoint(1, _by, 0),
                AddPoint(0.5f, ey, 0.5f, 0, 0, true)
            );

            AddFace(
                AddPoint(1, _by, 0),
                AddPoint(1, _dy, 1),
                AddPoint(0.5f, ey, 0.5f, 0, 0, true)
            );

            AddFace(
                AddPoint(1, _dy, 1),
                AddPoint(0, _cy, 1),
                AddPoint(0.5f, ey, 0.5f, 0, 0, true)
            );

            AddFace(
                AddPoint(0, _cy, 1),
                AddPoint(0, _ay, 0),
                AddPoint(0.5f, ey, 0.5f, 0, 0, true)
            );
        }
        else
        {
            AddFace(

                AddPoint(0, _ay, 0),
                AddPoint(1, _by, 0),
                AddPoint(0, _cy, 1)
            );

            AddFace(
                AddPoint(1, _dy, 1),
                AddPoint(0, _cy, 1),
                AddPoint(1, _by, 0)
            );
        }
    }

    private void AddOuterCorner(bool floorBelow = true, bool floorAbove = true, bool flattenBottom = false, float bottomHeight = -1)
    {


        float edgeBy = flattenBottom ? bottomHeight : _by;
        float edgeCy = flattenBottom ? bottomHeight : _cy;

        if (floorAbove)
        {
            StartFloor();
            AddFace(
                AddPoint(0, _ay, 0, 0, 0),
                AddPoint(0.5f, _ay, 0, 0, 1),
                AddPoint(0, _ay, 0.5f, 0, 1)
            );
        }

        StartWall();
        AddFace(
            AddPoint(0, edgeCy, 0.5f, 0, 0),
            AddPoint(0, _ay, 0.5f, 0, 1),
            AddPoint(0.5f, edgeBy, 0, 1, 0)
        );

        AddFace(
            AddPoint(0.5f, _ay, 0, 1, 1),
            AddPoint(0.5f, edgeBy, 0, 1, 0),
            AddPoint(0, _ay, 0.5f, 0, 1)
        );

        if (floorBelow)
        {
            StartFloor();
            AddFace(
                AddPoint(1, _dy, 1),
                AddPoint(0, _cy, 1),
                AddPoint(1, _by, 0)
            );

            AddFace(
                AddPoint(0, _cy, 1),
                AddPoint(0, _cy, 0.5f, 1, 0),
                AddPoint(0.5f, _by, 0, 1, 0)
            );

            AddFace(
                AddPoint(1, _by, 0),
                AddPoint(0, _cy, 1),
                AddPoint(0.5f, _by, 0, 1, 0)
            );
        }

    }


    private void AddEdge(bool floorBelow, bool floorAbove, float aX = 0, float bX = 1)
    {

        var edgeAy = _ab ? _ay : Mathf.Min(_ay, _by);
        var edgeBy = _ab ? _by : Mathf.Min(_ay, _by);
        var edgeCy = _cd ? _cy : Mathf.Max(_cy, _dy);
        var edgeDy = _cd ? _dy : Mathf.Max(_cy, _dy);

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
                AddPoint(0, _cy, 0.5f, 1, 0),
                AddPoint(1, _dy, 0.5f, 1, 0),
                AddPoint(0, _cy, 1)
            );
            AddFace(
                AddPoint(1, _dy, 1),
                AddPoint(0, _cy, 1),
                AddPoint(1, _dy, 0.5f, 1, 0)
            );
        }
    }

    private void AddInnerCorner(bool lowerFloor = true, bool fullUpperFloor = true, bool flatten = false, bool bdFloor = false, bool cdFloor = false)
    {


        var cornerBy = flatten ? Mathf.Min(_by, _cy) : _by;
        var cornerCy = flatten ? Mathf.Min(_by, _cy) : _cy;

        if (lowerFloor)
        {
            StartFloor();
            AddFace(
                AddPoint(0, _ay, 0),
                AddPoint(0.5f, _ay, 0, 1, 0),
                AddPoint(0, _ay, 0.5f, 1, 0)
            );
        }
        StartWall();
        AddFace(
            AddPoint(0, _ay, 0.5f, 1, 0),
            AddPoint(0.5f, _ay, 0, 0, 0),
            AddPoint(0, cornerCy, 0.5f, 1, 1)
        );

        AddFace(
            AddPoint(0.5f, cornerBy, 0, 0, 1),
            AddPoint(0, cornerCy, 0.5f, 1, 1),
            AddPoint(0.5f, _ay, 0, 0, 0)
        );
        StartFloor();
        if (fullUpperFloor)
        {
            AddFace(
                AddPoint(1, _dy, 1),
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
                AddPoint(1, _by, 0, 0, 0),
                AddPoint(0, _by, 0.5f, 1, 1),
                AddPoint(0.5f, _by, 0, 0, 1)
            );

            AddFace(
                AddPoint(1, _by, 0, 0, 0),
                AddPoint(1, _by, 0.5f, 1, -1),
                AddPoint(0, _by, 0.5f, 1, 1)
            );
        }

        if (bdFloor)
        {
            AddFace(
                AddPoint(0, _cy, 0.5f, 0, 1),
                AddPoint(0.5f, _cy, 0, 1, 1),
                AddPoint(0, _cy, 1, 0, 0)
            );
            AddFace(
                AddPoint(0.5f, _cy, 1, 1, -1),
                AddPoint(0, _cy, 1, 0, 0),
                AddPoint(0.5f, _cy, 0, 1, 1)
            );
        }
    }

    private void AddDiagonalFloor(float bY, float cY, bool aCliff, bool dCliff)
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



    private float3 AddPoint(float x, float y, float z, float uvX = 0, float uvY = 0, bool diagMidpoint = false)
    {
        for (int i = 0; i < _r; i++)
        {
            var temp = x;
            x = 1 - z;
            z = temp;
        }

        var uv = _floorMode ? new Vector2(uvX, uvY) : new Vector2(1, 1);

        float4 color = new float4(1, 1, 1, 1);
        if (diagMidpoint)
        {
            var adColor = math.lerp(
                ColorMap[_cellCoords.y * TerrainSize.x + _cellCoords.x],
                ColorMap[(_cellCoords.y + 1) * TerrainSize.x + _cellCoords.x + 1],
                .5f
            );
            var bcColor = math.lerp(
                ColorMap[_cellCoords.y * TerrainSize.x + _cellCoords.x + 1],
                ColorMap[(_cellCoords.y + 1) * TerrainSize.x + _cellCoords.x],
                .5f
            );

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

            var abColor = math.lerp(
                ColorMap[_cellCoords.y * TerrainSize.x + _cellCoords.x],
                ColorMap[_cellCoords.y * TerrainSize.x + _cellCoords.x + 1],
                x
            );
            var cdColor = math.lerp(
                ColorMap[(_cellCoords.y + 1) * TerrainSize.x + _cellCoords.x],
                ColorMap[(_cellCoords.y + 1) * TerrainSize.x + _cellCoords.x + 1],
                x
            );
            color = math.lerp(abColor, cdColor, z);
        }

        Colors.Add(new float4(color.x, color.y, color.z, color.w));
        float3 vert = new float3(
            (_cellCoords.x + x) * CellSize.x,
            y,
            (_cellCoords.y + z) * CellSize.y
        );
        Uvs.Add(uv);
        return vert;
    }

    private void AddFace(float3 v0, float3 v1, float3 v2)
    {
        var vertexCount = Vertices.Length;
        Vertices.Add(v0);
        Vertices.Add(v1);
        Vertices.Add(v2);

        Triangles.Add(vertexCount + 2);
        Triangles.Add(vertexCount + 1);
        Triangles.Add(vertexCount);

        float3 normal = -math.normalize(math.cross(v1 - v0, v2 - v0));
        float angle = math.degrees(math.acos(normal.y));
        
        Normals.Add(normal);
        Normals.Add(normal);
        Normals.Add(normal);
    }

    private bool IsHigher(float a, float b)
    {
        return a - b > MergeThreshold;
    }

    private bool IsLower(float a, float b)
    {
        return a - b < -MergeThreshold;
    }

    private bool IsMerged(float a, float b)
    {
        return Mathf.Abs(a - b) < MergeThreshold;
    }


    private void StartFloor()
    {
        _floorMode = true;
    }
    private void StartWall()
    {
        _floorMode = false;
    }
}



[ExecuteInEditMode]
public class MarchingSquaresChunk : MonoBehaviour
{
    public Mesh mesh;

    //Mesh data
    private NativeArray<float3> _vertices;
    private NativeArray<float4> _colors;
    private NativeArray<int> _triangles;
    private NativeArray<float2> _uvs;
    private NativeArray<float3> _normals;

    public List<float3> vertCache;
    public List<float3> normCache;
    public List<int> triCache;
    public List<Color> colorCache;

    public MarchingSquaresTerrain terrain;
    public Vector2Int chunkPosition;
    
    public float[] heightMap;
    public float4[] colorMap;

    public bool isDirty;

    public List<MarchingSquaresChunk> neighboringChunks = new List<MarchingSquaresChunk>();


    public void InitializeTerrain(bool shouldRegenerate = true)
    {
        mesh = new Mesh
        {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
        InitializeColorMap();
        InitializeHeightMap();
        RegenerateMesh();
    }

    private void InitializeColorMap()
    {
        colorMap = new float4[terrain.dimensions.z * terrain.dimensions.x];
        for (int z = 0; z < terrain.dimensions.z; z++)
        {
            for (int x = 0; x < terrain.dimensions.x; x++)
            {
                colorMap[GetIndex(x, z)] = new float4(1, 0, 0, 0);
            }
        }
    }

    private void InitializeHeightMap()
    {
        heightMap = new float[terrain.dimensions.z * terrain.dimensions.x];
    }

    public int GetIndex(int x, int z)
    {
        //Check if within bounds
        if (x >= 0 && x < terrain.dimensions.x && z >= 0 && z < terrain.dimensions.z)
            return x + z * terrain.dimensions.x;
        
        return 0;
    }

    public void RegenerateMesh()
    {
        mesh.Clear();

        GenerateTerrainCells();

        var mf = gameObject.GetComponent<MeshFilter>();
        var mc = gameObject.GetComponent<MeshCollider>();
        
        mesh.RecalculateNormals(45f);
        mf.sharedMesh = mesh;
        mc.sharedMesh = mesh;
        isDirty = false;
    }

    private void OnDestroy()
    {
    }

    public void GenerateTerrainCells()
    {
		//Mark this area on the profiler
        Profiler.BeginSample("Chunk Generation");
        GenerateChunkJob job = new GenerateChunkJob()
        {
            //Data
            HeightMap = new NativeArray<float>(heightMap, Allocator.TempJob),
            ColorMap = new NativeArray<float4>(colorMap, Allocator.TempJob),

            //Mesh data
            Vertices = new NativeList<float3>(0, Allocator.Persistent),
            Colors = new NativeList<float4>(  0, Allocator.Persistent),
            Uvs = new NativeList<float2>(     0, Allocator.Persistent),
            Triangles = new NativeList<int>(  0, Allocator.Persistent),
            Normals = new NativeList<float3>( 0, Allocator.Persistent),

            CellEdges = new NativeArray<bool>(new bool[4] { false, false, false, false }, Allocator.Persistent),
            PointHeights = new NativeArray<float>(new float[4] { 0, 0, 0, 0 }, Allocator.Persistent),

            //Config
            HigherPolyFloors = true,
            CellSize = terrain.cellSize,
            TerrainSize = new int3(terrain.dimensions.x, 0, terrain.dimensions.z),
            MergeThreshold = terrain.mergeThreshold,
        };



        var totalLoop = (terrain.dimensions.x) * (terrain.dimensions.z);

        var handle = job.Schedule(totalLoop, terrain.dimensions.x * terrain.dimensions.z);

        handle.Complete();
        Profiler.EndSample();
        
        Profiler.BeginSample("Chunk Mesh Creation");
        _vertices = job.Vertices.AsArray();
        _colors = job.Colors.AsArray();
        _triangles = job.Triangles.AsArray();
        _uvs = job.Uvs.AsArray();
        _normals = job.Normals.AsArray();

        //memcpy vertices into vertCache
        vertCache =  new List<float3>(_vertices.Length);
        normCache =  new List<float3>(_normals.Length);
        triCache =   new List<int>(_triangles.Length);
        colorCache = new List<Color>(_colors.Length);
        foreach (var t in _vertices)
            vertCache.Add(t);

        foreach (var t in _normals)
            normCache.Add(t);

        foreach (var t in _triangles)
            triCache.Add(t);
        
        foreach (var t in _colors)
            colorCache.Add(new Color(t.x, t.y, t.z, t.w));


        mesh.SetVertices<float3>(_vertices);
        mesh.SetNormals<float3>(_normals);
        mesh.SetColors<float4>(_colors);
        mesh.SetIndices(_triangles, MeshTopology.Triangles, 0);
        mesh.SetUVs<float2>(0, _uvs);

        job.Vertices.Dispose();
        job.Colors.Dispose();
        job.Triangles.Dispose();
        job.Uvs.Dispose();
        job.Normals.Dispose();
        
        Profiler.EndSample();

    }

    public void GenerateHeightmap(NoiseSettings ns,Texture2D heightmap = null)
    {
        for (var z = 0; z < terrain.dimensions.z; z++)
        {
            for (var x = 0; x < terrain.dimensions.x; x++)
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

                var noiseValue =
                    (float)perlin.GetValue((wX * ns.scale) + ns.offset.x, (wZ * ns.scale) + ns.offset.y, 0);
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

        isDirty = true;
    }

    private bool InBounds(int x, int z)
    {
        return x >= 0 && x < terrain.dimensions.x && z >= 0 && z < terrain.dimensions.z;
    }

    public void DrawHeight(int x, int z, float y, bool setHeight = false)
    {
        //Within bounds?
        if (!InBounds(z, x))
            return;
        heightMap[GetIndex(z, x)] = setHeight ? y : heightMap[GetIndex(z, x)] + y;
        isDirty = true;
    }


    public void DrawHeights(List<Vector2Int> positions, float height, bool setHeight, bool smooth)
    {
        Profiler.BeginSample("Draw Heights");
        for (var i = 0; i < positions.Count; i++)
        {
            if (!InBounds(positions[i].y, positions[i].x))
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
        Profiler.EndSample();
        isDirty = true;
    }

    public void DrawColor(int x, int z, Color color)
    {
        if (!InBounds(x, z))
            return;
        colorMap[GetIndex(x, z)] = color.ToFloat4();
        isDirty = true;
    }

    internal void DrawColors(List<Vector2Int> value, Color color)
    {
        for (var i = 0; i < value.Count; i++)
        {
            if (!InBounds((int)value[i].x, (int)value[i].y))
                continue;
            colorMap[GetIndex((int)value[i].x, (int)value[i].y)] = color.ToFloat4();
        }
        isDirty = true;
    }
}
