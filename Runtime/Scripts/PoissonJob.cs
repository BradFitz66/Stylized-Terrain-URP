using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;
[BurstCompile]
public struct PoissonJob : IJob, IDisposable
{
    #region Types

    private struct GridItem
    {
        #region Fields

        public bool HasValue;

        public float2 Value;

        #endregion
    }

    #endregion

    #region Fields

    // Because two dimensional grid.
    private const float INVERT_ROOT_TWO = 0.70710678118f;

    private const int DEFAULT_ITERATION_PER_POINT = 30;

    private readonly float2 _bottomLeft;

    private readonly float2 _topRight;

    private readonly Rect _area;

    private readonly float _minimumDistance;

    private readonly int _iterationPerPoint;

    private readonly float _cellSize;

    private readonly int2 _gridSize;

    [WriteOnly]
    private NativeList<float2> _results;

    private NativeArray<GridItem> _grid;

    private NativeList<float2> _activePoints;

    private Random _rng;

    #endregion

    #region Constructors

    public PoissonJob(
        in NativeList<float2> results,
        in float2 bottomLeft,
        in float2 topRight,
        in float minimumDistance,
        in int iterationPerPoint = DEFAULT_ITERATION_PER_POINT,
        in uint? seed = default)
    {
        var dimension = topRight - bottomLeft;
        var cell = minimumDistance * INVERT_ROOT_TWO;

        _bottomLeft = bottomLeft;
        _topRight = topRight;
        _area = new Rect(new float2(bottomLeft.x, bottomLeft.y), new float2(dimension.x, dimension.y));
        _minimumDistance = minimumDistance;
        _iterationPerPoint = iterationPerPoint;
        _cellSize = cell;
        _gridSize = new int2((int)math.ceil(dimension.x / cell), (int)math.ceil(dimension.y / cell));
        _results = results;

        var gridCapacity = (_gridSize.x + 1) * (_gridSize.y + 1);
        _grid = new NativeArray<GridItem>(gridCapacity, Allocator.TempJob);
        _activePoints = new NativeList<float2>(Allocator.TempJob);
        _rng = new Random(seed ?? (uint)DateTime.Now.Ticks);
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public void Dispose()
    {
        _grid.Dispose();
        _activePoints.Dispose();
    }

    private float2 GetRandPosInCircle(in float fieldMin, in float fieldMax)
    {
        var theta = _rng.NextFloat(0f, math.PI * 2f);
        var radius = math.sqrt(_rng.NextFloat(fieldMin * fieldMin, fieldMax * fieldMax));

        return new float2(radius * math.cos(theta), radius * math.sin(theta));
    }

    private int2 GetGridCellForValue(in float2 point)
    {
        return new int2(
            (int)math.floor((point.x - _bottomLeft.x) / _cellSize),
            (int)math.floor((point.y - _bottomLeft.y) / _cellSize));
    }

    private bool GetNextPoint(in float2 point)
    {
        var found = false;
        var p = GetRandPosInCircle(_minimumDistance, 2f * _minimumDistance) + point;

        if (!_area.Contains(p))
        {
            return false;
        }

        var minimum = _minimumDistance * _minimumDistance;
        var index = GetGridCellForValue(p);
        var drop = false;

        var around = (int)math.ceil(_minimumDistance / _cellSize);
        var fieldMin = new int2(math.max(0, index.x - around), math.max(0, index.y - around));
        var fieldMax = new int2(
            math.min(_gridSize.x, index.x + around),
            math.min(_gridSize.y, index.y + around));

        for (var x = fieldMin.x; x <= fieldMax.x && !drop; x++)
        {
            for (var y = fieldMin.y; y <= fieldMax.y && !drop; y++)
            {
                var cell = new int2(x, y);
                var item = GetItemFromGrid(cell);
                if (item.HasValue && math.lengthsq(item.Value - p) <= minimum)
                {
                    drop = true;
                }
            }
        }

        if (!drop)
        {
            found = true;

            _results.Add(p);
            _activePoints.Add(p);
            SetValueInGrid(index, p);
        }

        return found;
    }

    private GridItem GetItemFromGrid(in int2 cell)
    {
        var index = cell.y * _gridSize.x + cell.x;
        return _grid[index];
    }

    private void SetValueInGrid(in int2 cell, in float2 value)
    {
        var index = cell.y * _gridSize.x + cell.x;
        var gridItem = _grid[index];
        gridItem.Value = value;
        gridItem.HasValue = true;
        _grid[index] = gridItem;
    }

    private void GetFirstPoint()
    {
        var first = new float2(
            _rng.NextFloat(_bottomLeft.x, _topRight.x),
            _rng.NextFloat(_bottomLeft.y, _topRight.y));

        var index = GetGridCellForValue(first);

        SetValueInGrid(index, first);
        _results.Add(first);
        _activePoints.Add(first);
    }

    /// <inheritdoc />
    void IJob.Execute()
    {
        GetFirstPoint();

        do
        {
            var index = _rng.NextInt(0, _activePoints.Length);
            var point = _activePoints[index];
            var found = false;
            for (var k = 0; k < _iterationPerPoint; k++)
            {
                found |= GetNextPoint(point);
            }

            if (!found)
            {
                _activePoints.RemoveAt(index);
            }
        }
        while (_activePoints.Length > 0);
    }

    #endregion
}