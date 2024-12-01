using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;

[System.Serializable]
public class TerrainBrush : TerrainTool
{

    Vector3 mousePosition;
    Vector3 cellPosWorld;
    Vector3 totalTerrainSize;

    Vector2 viewportMousePosition;

    Vector2Int cellPos;
    Vector2Int chunkPos;

    bool mouseDown = false;
    bool flattenGeometry=false;

    float dragHeight = 0;
    float hoveredCellHeight = 0;

    float brushSize = 1;

    //cell world position, chunkPos
    Dictionary<Vector3, Vector2Int> selectedCells = new Dictionary<Vector3, Vector2Int>();

    Bounds selectionBounds;

    enum ToolState
    {
        None,
        SelectingCells,
        SelectedCells,
        DraggingHeight
    }

    ToolState state = ToolState.None;
    public override void DrawHandles()
    {

        Handles.color = Color.green;
        if (state != ToolState.DraggingHeight)
        {
            Handles.DrawSolidDisc(cellPosWorld + Vector3.up * hoveredCellHeight,Vector3.up, brushSize / 2);
        }
       
        foreach (var cell in selectedCells)
        {
            if (!t.chunks.ContainsKey(cell.Value))
            {
                selectedCells.Clear();
                state = ToolState.None;
                break;
            }
            MarchingSquaresChunk c = t.chunks[cell.Value];
            Vector2Int localCellPos = new Vector2Int(
                Mathf.FloorToInt((cell.Key.x - c.transform.position.x) / t.cellSize.x),
                Mathf.FloorToInt((cell.Key.z - c.transform.position.z) / t.cellSize.y)
            );
            Handles.color = new Color(0, 1, 0, .5f);

            float cellHeight = c.heightMap[c.getIndex(localCellPos.y, localCellPos.x)];
            Handles.DrawSolidDisc(cell.Key + Vector3.up * (cellHeight+dragHeight), Vector3.up, t.cellSize.x/2);
        }

        //Draw wire around hovered chunk
        if (t.chunks.ContainsKey(chunkPos))
        {
            Handles.color = Color.yellow;
            Vector3 chunkWorldPos = new Vector3(
                t.chunks[chunkPos].transform.position.x + totalTerrainSize.x / 2,
                0,
                t.chunks[chunkPos].transform.position.z + totalTerrainSize.z / 2
            );
            Handles.DrawWireCube(chunkWorldPos, totalTerrainSize);
        }

    }
    public override void OnMouseDown(int button = 0)
    {
        if (button == 0)
        {
            switch (state)
            {
                case ToolState.None:
                    state = ToolState.SelectingCells;
                    break;
                case ToolState.SelectedCells:
                    state = ToolState.DraggingHeight;
                    break;
            }
        }
    }
    public override void OnMouseDrag(Vector2 delta)
    {
        switch(state)
        {
            case ToolState.DraggingHeight:
                dragHeight += -delta.y * 0.1f;
                break;
        }
    }


    public override void OnMouseUp(int button = 0)
    {
        if(button != 0)
            return;

        switch (state)
        {
            case ToolState.SelectingCells:
                if (selectedCells.Count > 0)
                    state = ToolState.SelectedCells;
                else
                    state = ToolState.None;
                break;
            case ToolState.DraggingHeight:
                //Remove any selectedCells with the same world position
                float avgHeight = 0;
                foreach (var cell in selectedCells)
                {
                    MarchingSquaresChunk c = t.chunks[cell.Value];
                    Vector2Int localCellPos = new Vector2Int(
                        Mathf.FloorToInt((cell.Key.x - c.transform.position.x) / t.cellSize.x),
                        Mathf.FloorToInt((cell.Key.z - c.transform.position.z) / t.cellSize.y)
                    );
                    
                    float curHeight = c.heightMap[c.getIndex(localCellPos.y, localCellPos.x)];
                    if (flattenGeometry)
                    {
                        avgHeight += curHeight;
                    }

                    if (!flattenGeometry)
                    {
                        t.SetHeight(cell.Value, localCellPos.x, localCellPos.y, curHeight + dragHeight);
                    }
                }

                if (flattenGeometry)
                {
                    avgHeight /= selectedCells.Count;
                    foreach (var cell in selectedCells)
                    {
                        MarchingSquaresChunk c = t.chunks[cell.Value];
                        //Convert world position to chunk cell position
                        Vector2Int localCellPos = new Vector2Int(
                            Mathf.FloorToInt((cell.Key.x - c.transform.position.x) / t.cellSize.x),
                            Mathf.FloorToInt((cell.Key.z - c.transform.position.z) / t.cellSize.y)
                        );
                        t.SetHeight(cell.Value, localCellPos.x, localCellPos.y, avgHeight+dragHeight);
                    }
                }

                dragHeight = 0;
                state = ToolState.None;
                break;

        }

        mouseDown = false;
    }

    GUIContent flattenLabel = new GUIContent("Flatten Geometry","Averages the height of manipulated geometry so it'll all be at the same height");
    public override void OnInspectorGUI()
    {
        flattenGeometry = EditorGUILayout.Toggle(flattenLabel, flattenGeometry);
    }

    public override void Update()
    {
        totalTerrainSize = new Vector3(
            (t.dimensions.x-1) * t.cellSize.x,
            0,
            (t.dimensions.z-1) * t.cellSize.y
        );
        if (state == ToolState.None && selectedCells.Count > 0)
        {
            selectedCells.Clear();
        }

        viewportMousePosition = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, t.transform.position);
        groundPlane.Raycast(ray, out float distance);
        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, 1 << t.gameObject.layer);
        mousePosition = hit ? new Vector3(hitInfo.point.x,0,hitInfo.point.z) : ray.GetPoint(distance);

        if (Event.current.type == EventType.ScrollWheel)
        {
            brushSize += -Event.current.delta.y * 0.1f;
            brushSize = Mathf.Clamp(brushSize, 1, 100);
            //Eat the event to prevent zooming in the scene view
            Event.current.Use();
        }

        chunkPos = new Vector2Int(
            Mathf.FloorToInt(mousePosition.x / totalTerrainSize.x),
            Mathf.FloorToInt(mousePosition.z / totalTerrainSize.z)
        );

        if(state == ToolState.SelectingCells)
        {
            for (int y = -Mathf.FloorToInt(brushSize / 2); y <= Mathf.FloorToInt(brushSize / 2); y++)
            {
                for (int x = -Mathf.FloorToInt(brushSize / 2); x <= Mathf.FloorToInt(brushSize / 2); x++)
                {
                    Vector3 p = new Vector3(x, 0, y);
                    Vector3 mouseOffset = mousePosition + p;
                    //Snap to cell size
                    Vector3 cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);

                    //Get chunk position at mouseOffset
                    Vector2Int chunk = new Vector2Int(
                        Mathf.FloorToInt(mouseOffset.x / totalTerrainSize.x),
                        Mathf.FloorToInt(mouseOffset.z / totalTerrainSize.z)
                    );

                    if (t.chunks.ContainsKey(chunk) && !selectedCells.ContainsKey(cellWorld))
                    {
                        selectedCells[cellWorld] = chunk;
                    }
                }
            }
        }

        cellPosWorld = mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y);

        //Get cell pos (0 - dimensions.x, 0 - dimensions.z)
        if (t.chunks.ContainsKey(chunkPos))
        {
            cellPos = new Vector2Int(
                Mathf.FloorToInt((cellPosWorld.x - t.chunks[chunkPos].transform.position.x) / t.cellSize.x),
                Mathf.FloorToInt((cellPosWorld.z - t.chunks[chunkPos].transform.position.z) / t.cellSize.y)
            );
            if (state== ToolState.SelectingCells || state == ToolState.None)
            {
                hoveredCellHeight = t.chunks[chunkPos].heightMap[t.chunks[chunkPos].getIndex(cellPos.y, cellPos.x)];
            }
        }
    }
}
