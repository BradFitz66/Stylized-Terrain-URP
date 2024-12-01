using UnityEngine;
using UnityEditor;

[System.Serializable]
public class ChunkBrush : TerrainTool
{
    Vector3 mousePosition;
    Vector3 snappedMousePosition;
    Vector2Int chunkPos;
    Vector3 totalTerrainSize;
    bool canPlace = false;

    public override void DrawHandles()
    {
        Handles.color = canPlace ? Color.green : Color.red;
        Handles.DrawWireCube(snappedMousePosition, totalTerrainSize);
    }
    public override void OnMouseDown(int button = 0)
    {
        if (button != 0)
            return;

        if (canPlace)
        {
            t.AddNewChunk(chunkPos.x, chunkPos.y);
        }
        else
        {
            if (t.chunks.ContainsKey(chunkPos))
                t.RemoveChunk(chunkPos);
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
        totalTerrainSize = new Vector3(
            (t.dimensions.x-1) * t.cellSize.x,
            0,
            (t.dimensions.z-1) * t.cellSize.y
        );

        //Raycast to terrain
        canPlace = false;
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, t.transform.position);
        groundPlane.Raycast(ray, out float distance);
        mousePosition = ray.GetPoint(distance);

        chunkPos = new Vector2Int(
            Mathf.FloorToInt(mousePosition.x / totalTerrainSize.x),
            Mathf.FloorToInt(mousePosition.z / totalTerrainSize.z)
        );

        //Convert chunk position to world position
        snappedMousePosition = new Vector3(
            (chunkPos.x * (t.dimensions.x - 1) * t.cellSize.x) + totalTerrainSize.x / 2,
            0,
            (chunkPos.y * (t.dimensions.z - 1) * t.cellSize.y) + totalTerrainSize.z / 2
        );

        if (t.chunks.Count == 0)
        {
            canPlace = true;
        }
        else
        {

            if (t.chunks.ContainsKey(chunkPos))
            {
                canPlace = false;
                return;
            }
            Vector2Int[] neighborPositions = new Vector2Int[]
            {
                new Vector2Int(chunkPos.x - 1, chunkPos.y),
                new Vector2Int(chunkPos.x + 1, chunkPos.y),
                new Vector2Int(chunkPos.x, chunkPos.y - 1),
                new Vector2Int(chunkPos.x, chunkPos.y + 1)
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
            canPlace = hasNeighbors;
        }

    }
}
