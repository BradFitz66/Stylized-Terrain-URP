#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[System.Serializable]
public class SculptBrush : TerrainTool
{

    Vector3 mousePosition;
    Vector3 cellPosWorld;
    Vector3 totalTerrainSize;

    Vector2Int cellPos;
    Vector2Int chunkPos;

    bool mouseDown = false;
    bool flattenGeometry=false;

    float setHeight = 0;
    float dragHeight = 0;
    float hoveredCellHeight = 0;
    float brushSize = 2;

    //cell world position, chunkPos
    List<Vector3> selectedCells = new List<Vector3>();
    List<Vector3> smoothingCells = new List<Vector3>();

    Bounds selectionBounds;

    enum ToolState
    {
        None,
        SmoothingHeight,
        SelectingCells,
        SelectedCells,
        DraggingHeight
    }

    ToolState state = ToolState.None;


    public override void ToolSelected()
    {
        setHeight = EditorPrefs.GetFloat("setHeight_SCULPTBRUSH", 0);
    }

    public override void DrawHandles()
    {

        Handles.color = Color.green;
        if (state != ToolState.DraggingHeight)
        {
            Handles.DrawSolidDisc(cellPosWorld + Vector3.up * hoveredCellHeight,Vector3.up, brushSize / 2);
        }
       
        foreach (var pos in selectedCells)
        {

            List<MarchingSquaresChunk> chunks = t.GetChunksAtWorldPosition(pos);
            if (chunks.Count > 0)
            {
                //We don't really need to be accurate with this, so just get the first chunk that contains the pos
                MarchingSquaresChunk c = chunks[0];
                Vector2Int localCell = new Vector2Int(
                    Mathf.FloorToInt((pos.x - c.transform.position.x) / t.cellSize.x),
                    Mathf.FloorToInt((pos.z - c.transform.position.z) / t.cellSize.y)
                );

                Handles.color = new Color(0, 1, 0, .5f);

                float cellHeight = c.heightMap[c.GetIndex(localCell.y, localCell.x)];
                Handles.DrawSolidDisc(pos + Vector3.up * (cellHeight + dragHeight), Vector3.up, t.cellSize.x / 2);
            }
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

        //Draw text to indicate current state
        switch(state)
        {
            case ToolState.SelectingCells:
                Handles.Label(mousePosition + Vector3.up * 2, "Selecting Cells");
                break;
            case ToolState.SelectedCells:
                Handles.Label(mousePosition + Vector3.up * 2, "Selected Cells");
                break;
            case ToolState.DraggingHeight:
                Handles.Label(mousePosition + Vector3.up * 2, $"Dragging Height: {dragHeight}");
                break;
            case ToolState.SmoothingHeight:
                Handles.Label(mousePosition + Vector3.up * 2, "Smoothing Height");
                break;
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
                case ToolState.SmoothingHeight:
                    mouseDown = true;
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
            case ToolState.SmoothingHeight:
                if (!mouseDown)
                    return;
                for (int y = -Mathf.FloorToInt(brushSize / 2); y <= Mathf.FloorToInt(brushSize / 2); y++)
                {
                    for (int x = -Mathf.FloorToInt(brushSize / 2); x <= Mathf.FloorToInt(brushSize / 2); x++)
                    {
                        Vector3 p = new Vector3(x, 0, y);
                        Vector3 mouseOffset = mousePosition + p;
                        //Snap to cell size
                        Vector3 cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);

                        bool insideRadius = Vector3.Distance(mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y), mouseOffset) <= brushSize / 2;

                        if (insideRadius && !smoothingCells.Contains(cellWorld))
                        {
                            smoothingCells.Add(cellWorld);
                        }

                    }
                }

                foreach (var cell in smoothingCells)
                {
                    List<MarchingSquaresChunk> chunks = t.GetChunksAtWorldPosition(cell);
                    if (chunks.Count > 0)
                    {
                        MarchingSquaresChunk c = chunks[0];
                        Vector2Int localCell = new Vector2Int(
                            Mathf.FloorToInt((cell.x - c.transform.position.x) / t.cellSize.x),
                            Mathf.FloorToInt((cell.z - c.transform.position.z) / t.cellSize.y)
                        );
                        t.SmoothHeights(smoothingCells);
                    }
                }
                smoothingCells.Clear();
                break;
                
        }
    }


    public override void OnMouseUp(int button = 0)
    {
        if(button != 0)
            return;
        Debug.Log("Current state: " + state);
        switch (state)
        {
            case ToolState.SelectingCells:
                if (selectedCells.Count > 0)
                    state = ToolState.SelectedCells;
                else
                    state = ToolState.None;
                break;
            case ToolState.DraggingHeight:
                t.DrawHeights(selectedCells, dragHeight, false);
                dragHeight = 0;
                state = ToolState.None;
                break;
            case ToolState.SmoothingHeight:
                state = ToolState.None;
                break;
        }

        mouseDown = false;
    }

    GUIContent flattenLabel = new GUIContent("Flatten Geometry","Averages the height of manipulated geometry so it'll all be at the same height");
    public override void OnInspectorGUI()
    {
        flattenGeometry = EditorGUILayout.Toggle(flattenLabel, flattenGeometry);
        setHeight = EditorGUILayout.FloatField("Set Height", setHeight);

        //Save setHeight
        EditorPrefs.SetFloat("setHeight_SCULPTBRUSH", setHeight);
    }

    public override void Update()
    {
        if(selectedCells == null)
            selectedCells = new List<Vector3>();

        totalTerrainSize = new Vector3(
            (t.dimensions.x-1) * t.cellSize.x,
            0,
            (t.dimensions.z-1) * t.cellSize.y
        );
        if (state == ToolState.None && selectedCells.Count > 0)
        {
            selectedCells.Clear();
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, t.transform.position);
        groundPlane.Raycast(ray, out float distance);
        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo,100, 1 << t.gameObject.layer);
        mousePosition = hit ? new Vector3(hitInfo.point.x,0,hitInfo.point.z) : ray.GetPoint(distance);

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.LeftShift)
        {
            Debug.Log("Setting height");
            state = ToolState.SmoothingHeight;
        }
        else if (Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.LeftShift)
        {
            state = ToolState.None;
        }

        if (Event.current.type == EventType.ScrollWheel)
        {
            brushSize += -Event.current.delta.y * 0.1f;
            brushSize = Mathf.Clamp(brushSize, 2, 100);
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

                    bool insideRadius = Vector3.Distance(mousePosition.Snap(t.cellSize.x,1,t.cellSize.y), mouseOffset) <= brushSize / 2;

                    if (insideRadius && !selectedCells.Contains(cellWorld))
                    {
                        selectedCells.Add(cellWorld);
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
                hoveredCellHeight = t.chunks[chunkPos].heightMap[t.chunks[chunkPos].GetIndex(cellPos.y, cellPos.x)];
            }
        }
    }
}
#endif