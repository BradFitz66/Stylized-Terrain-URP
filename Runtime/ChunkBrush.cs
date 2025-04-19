#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[System.Serializable]
public class ChunkBrush : TerrainTool
{
    private Vector3 _mousePosition;
    private Vector3 _snappedMousePosition;
    private Vector2Int _chunkPos;
    private Vector3 _totalTerrainSize;
    private bool _canPlace = false;

    public override void DrawHandles()
    {
        Handles.color = _canPlace ? Color.green : Color.red;
        Handles.DrawWireCube(_snappedMousePosition, _totalTerrainSize);
    }
    public override void OnMouseDown(int button = 0)
    {
        if (button != 0)
            return;

        if (_canPlace)
        {
            t.AddNewChunk(_chunkPos.x, _chunkPos.y);
        }
        else
        {
            if (t.chunks.ContainsKey(_chunkPos))
                t.RemoveChunk(_chunkPos);
        }
    }
    public override void OnMouseDrag(Vector2 delta)
    {
    }
    public override void OnMouseUp(int button = 0)
    {
    }
    public override void Update()
    {
        _totalTerrainSize = new Vector3(
            (t.dimensions.x-1) * t.cellSize.x,
            0,
            (t.dimensions.z-1) * t.cellSize.y
        );

        //Raycast to terrain
        _canPlace = false;
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, t.transform.position);
        groundPlane.Raycast(ray, out float distance);
        _mousePosition = ray.GetPoint(distance);

        _chunkPos = new Vector2Int(
            Mathf.FloorToInt(_mousePosition.x / _totalTerrainSize.x),
            Mathf.FloorToInt(_mousePosition.z / _totalTerrainSize.z)
        );

        //Convert chunk position to world position
        _snappedMousePosition = new Vector3(
            (_chunkPos.x * (t.dimensions.x - 1) * t.cellSize.x) + _totalTerrainSize.x / 2,
            0,
            (_chunkPos.y * (t.dimensions.z - 1) * t.cellSize.y) + _totalTerrainSize.z / 2
        );

        if (t.chunks.Count == 0)
        {
            _canPlace = true;
        }
        else
        {

            if (t.chunks.ContainsKey(_chunkPos))
            {
                _canPlace = false;
                return;
            }
            Vector2Int[] neighborPositions = new Vector2Int[]
            {
                new Vector2Int(_chunkPos.x - 1, _chunkPos.y),
                new Vector2Int(_chunkPos.x + 1, _chunkPos.y),
                new Vector2Int(_chunkPos.x, _chunkPos.y - 1),
                new Vector2Int(_chunkPos.x, _chunkPos.y + 1)
            };

            bool hasNeighbors = false;
            foreach (Vector2Int pos in neighborPositions)
            {
                if (t.chunks.ContainsKey(pos))
                {
                    hasNeighbors = true;
                    break;
                }
            }
            _canPlace = hasNeighbors;
        }

    }
}
#endif