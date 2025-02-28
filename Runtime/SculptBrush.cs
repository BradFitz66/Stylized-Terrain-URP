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

    Vector2 viewportMousePosition;

    Vector2Int cellPos;
    Vector2Int chunkPos;

    bool mouseDown = false;
    bool flattenGeometry=false;

    float setHeight = 0;



    float dragHeight = 0;
    float hoveredCellHeight = 0;

    float brushSize = 2;

    //cell world position, chunkPos
    Dictionary<Vector2Int, List<Vector3>> selectedCells = new Dictionary<Vector2Int, List<Vector3>>();
    Dictionary<Vector2Int, List<Vector3>> smoothingCells = new Dictionary<Vector2Int, List<Vector3>>();

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
       
        foreach (var cell in selectedCells)
        {
            if (!t.chunks.ContainsKey(cell.Key))
            {
                selectedCells.Clear();
                state = ToolState.None;
                break;
            }
            MarchingSquaresChunk c = t.chunks[cell.Key];

            foreach(var pos in cell.Value)
            {
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

                        //Get chunk position at mouseOffset
                        Vector2Int chunk = new Vector2Int(
                            Mathf.FloorToInt(mouseOffset.x / totalTerrainSize.x),
                            Mathf.FloorToInt(mouseOffset.z / totalTerrainSize.z)
                        );

                        float wX = (chunk.x * (t.dimensions.x - 1)) + x;
                        float wZ = (chunk.y * (t.dimensions.z - 1)) + y;

                        bool insideRadius = Vector3.Distance(mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y), mouseOffset) <= brushSize / 2;

                        if (t.chunks.ContainsKey(chunk) && !smoothingCells.ContainsKey(chunk) && insideRadius)
                        {
                            smoothingCells.Add(chunk, new List<Vector3>());
                            smoothingCells[chunk].Add(cellWorld);
                        }
                        else if (t.chunks.ContainsKey(chunk) && smoothingCells.ContainsKey(chunk) && insideRadius)
                        {
                            if (!smoothingCells[chunk].Contains(cellWorld))
                                smoothingCells[chunk].Add(cellWorld);
                        }
                    }
                }
                foreach (var cell in smoothingCells)
                {
                    MarchingSquaresChunk c = t.chunks[cell.Key];
                    if (!flattenGeometry)
                    {
                        List<Vector2Int> localCells = cell.Value.ConvertAll(v => new Vector2Int(
                            Mathf.FloorToInt((v.x - c.transform.position.x) / t.cellSize.x),
                            Mathf.FloorToInt((v.z - c.transform.position.z) / t.cellSize.y)
                        ));
                        t.SmoothHeights(localCells, c, setHeight, true);
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
                foreach (var cell in selectedCells)
                {
                    MarchingSquaresChunk c = t.chunks[cell.Key];
                    if (!flattenGeometry)
                    {
                        List<Vector2Int> localCells = cell.Value.ConvertAll(v => new Vector2Int(
                            Mathf.FloorToInt((v.x - c.transform.position.x) / t.cellSize.x),
                            Mathf.FloorToInt((v.z - c.transform.position.z) / t.cellSize.y)
                        ));
                        t.DrawHeights(localCells,c,dragHeight, false);
                    }
                }

                dragHeight = 0;
                state = ToolState.None;
                break;
            case ToolState.SmoothingHeight:
                Debug.Log("Setting height");
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

                    //Get chunk position at mouseOffset
                    Vector2Int chunk = new Vector2Int(
                        Mathf.FloorToInt(mouseOffset.x / totalTerrainSize.x),
                        Mathf.FloorToInt(mouseOffset.z / totalTerrainSize.z)
                    );

                    float wX = (chunk.x * (t.dimensions.x - 1)) + x;
                    float wZ = (chunk.y * (t.dimensions.z - 1)) + y;

                    bool insideRadius = Vector3.Distance(mousePosition.Snap(t.cellSize.x,1,t.cellSize.y), mouseOffset) <= brushSize / 2;

                    if (t.chunks.ContainsKey(chunk) && !selectedCells.ContainsKey(chunk) && insideRadius)
                    {
                        selectedCells.Add(chunk, new List<Vector3>());
                        selectedCells[chunk].Add(cellWorld);
                    }
                    else if (t.chunks.ContainsKey(chunk) && selectedCells.ContainsKey(chunk) && insideRadius)
                    {
                        if (!selectedCells[chunk].Contains(cellWorld))
                            selectedCells[chunk].Add(cellWorld);
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