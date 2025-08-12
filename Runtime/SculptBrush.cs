#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[System.Serializable]
public class SculptBrush : TerrainTool
{

    private Vector3 _mousePosition;
    private Vector3 _cellPosWorld;
    private Vector3 _totalTerrainSize;

    private Vector2Int _cellPos;
    private Vector2Int _chunkPos;

    private bool _mouseDown;
    private bool _flattenGeometry;

    private float _setHeight;
    private float _dragHeight;
    private float _hoveredCellHeight;
    private float _brushSize = 2;

    //cell world position, chunkPos
    private List<Vector3> _selectedCells;
    private List<Vector3> _smoothingCells;

    private Bounds _selectionBounds;

    private enum ToolState
    {
        None,
        SmoothingHeight,
        SelectingCells,
        SelectedCells,
        DraggingHeight
    }

    private ToolState _state = ToolState.None;

    private GUIStyle _style = new GUIStyle();

    public override void ToolSelected()
    {
        _setHeight = EditorPrefs.GetFloat("setHeight_SCULPTBRUSH", 0);
		_smoothingCells = new List<Vector3>();
        _selectedCells = new List<Vector3>();
    }


    public override void DrawHandles()
    {
        _style.normal.textColor = Color.black;

        Handles.color = Color.green;
        if (_state != ToolState.DraggingHeight)
        {
            Handles.DrawSolidDisc(_cellPosWorld + Vector3.up * _hoveredCellHeight,Vector3.up, _brushSize / 2);
        }
       
        foreach (var pos in _selectedCells)
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
                Handles.DrawSolidDisc(pos + Vector3.up * (cellHeight + _dragHeight), Vector3.up, t.cellSize.x / 2);
            }
        }

        //Draw wire around hovered chunk
        if (t.chunks.ContainsKey(_chunkPos))
        {
            Handles.color = Color.yellow;
            Vector3 chunkWorldPos = new Vector3(
                t.chunks[_chunkPos].transform.position.x + _totalTerrainSize.x / 2,
                0,
                t.chunks[_chunkPos].transform.position.z + _totalTerrainSize.z / 2
            );
            Handles.DrawWireCube(chunkWorldPos, _totalTerrainSize);
        }

        Handles.color = Color.black;
        //Draw text to indicate current state
        switch(_state)
        {
            
            case ToolState.SelectingCells:
                Handles.Label(_mousePosition + Vector3.up * 2, "Selecting Cells",_style);
                break;
            case ToolState.SelectedCells:
                Handles.Label(_mousePosition + Vector3.up * 2, "Selected Cells",_style);
                break;
            case ToolState.DraggingHeight:
                Handles.Label(_mousePosition + Vector3.up * 2, $"Dragging Height: {_dragHeight}",_style);
                break;
            case ToolState.SmoothingHeight:
                Handles.Label(_mousePosition + Vector3.up * 2, "Smoothing Height",_style);
                break;
        }
    }
    public override void OnMouseDown(int button = 0)
    {

        if (button == 0)
        {
            switch (_state)
            {
                case ToolState.None:       
                    _state = ToolState.SelectingCells;
                    break;
                case ToolState.SelectedCells:
                    _state = ToolState.DraggingHeight;
                    break;
                case ToolState.SmoothingHeight:
                    _mouseDown = true;
                    break;
            }
        }
    }
    public override void OnMouseDrag(Vector2 delta)
    {
        switch(_state)
        {
            case ToolState.DraggingHeight:
                _dragHeight += -delta.y * 0.1f;
                break;
            case ToolState.SmoothingHeight:
                if (!_mouseDown)
                    return;
                for (int y = -Mathf.FloorToInt(_brushSize / 2); y <= Mathf.FloorToInt(_brushSize / 2); y++)
                {
                    for (int x = -Mathf.FloorToInt(_brushSize / 2); x <= Mathf.FloorToInt(_brushSize / 2); x++)
                    {
                        Vector3 p = new Vector3(x, 0, y);
                        Vector3 mouseOffset = _mousePosition + p;
                        //Snap to cell size
                        Vector3 cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);

                        bool insideRadius = Vector3.Distance(_mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y), mouseOffset) <= _brushSize / 2;

                        if (insideRadius && !_smoothingCells.Contains(cellWorld))
                        {
                            _smoothingCells.Add(cellWorld);
                        }

                    }
                }

                foreach (var cell in _smoothingCells)
                {
                    var chunks = t.GetChunksAtWorldPosition(cell);
                    if (chunks.Count == 0)
                        continue;
                    var c = chunks[0];
                    var localCell = new Vector2Int(
                        Mathf.FloorToInt((cell.x - c.transform.position.x) / t.cellSize.x),
                        Mathf.FloorToInt((cell.z - c.transform.position.z) / t.cellSize.y)
                    );
                    t.SmoothHeights(_smoothingCells);
                    
                }
                _smoothingCells.Clear();
                break;
                
        }
    }


    public override void OnMouseUp(int button = 0)
    {
        if(button != 0)
            return;
        Debug.Log("Current state: " + _state);
        switch (_state)
        {
            case ToolState.SelectingCells:
                if (_selectedCells.Count > 0)
                    _state = ToolState.SelectedCells;
                else
                    _state = ToolState.None;
                break;
            case ToolState.DraggingHeight:
                t.DrawHeights(_selectedCells, _dragHeight, false);
                _dragHeight = 0;
                _state = ToolState.None;
                break;
            case ToolState.SmoothingHeight:
                _state = ToolState.None;
                break;
        }

        _mouseDown = false;
    }

    private GUIContent _flattenLabel = new GUIContent("Flatten Geometry","Averages the height of manipulated geometry so it'll all be at the same height");
    public override void OnInspectorGUI()
    {
        _flattenGeometry = EditorGUILayout.Toggle(_flattenLabel, _flattenGeometry);
        _setHeight = EditorGUILayout.FloatField("Set Height", _setHeight);

        //Save setHeight
        EditorPrefs.SetFloat("setHeight_SCULPTBRUSH", _setHeight);
    }
    public override void Update()
    {
        if(_selectedCells == null)
            _selectedCells = new List<Vector3>();

        _totalTerrainSize = new Vector3(
            (t.dimensions.x-1) * t.cellSize.x,
            0,
            (t.dimensions.z-1) * t.cellSize.y
        );
        if (_state == ToolState.None && _selectedCells.Count > 0)
        {
            _selectedCells.Clear();
        }

        var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        var groundPlane = new Plane(Vector3.up, t.transform.position);
        groundPlane.Raycast(ray, out var distance);
        var hit = Physics.Raycast(ray, out var hitInfo,100, 1 << t.gameObject.layer);
        _mousePosition = hit ? new Vector3(hitInfo.point.x,0,hitInfo.point.z) : ray.GetPoint(distance);

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.LeftShift)
        {
            Debug.Log("Setting height");
            _state = ToolState.SmoothingHeight;
        }
        else if (Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.LeftShift)
        {
            _state = ToolState.None;
        }

        if (Event.current.type == EventType.ScrollWheel)
        {
            _brushSize += -Event.current.delta.y * 0.1f;
            _brushSize = Mathf.Clamp(_brushSize, t.cellSize.magnitude, 100);
            //Eat the event to prevent zooming in the scene view
            Event.current.Use();
        }


        _chunkPos = new Vector2Int(
            Mathf.FloorToInt(_mousePosition.x / _totalTerrainSize.x),
            Mathf.FloorToInt(_mousePosition.z / _totalTerrainSize.z)
        );

        if(_state == ToolState.SelectingCells)
        {
            for (int y = -Mathf.FloorToInt(_brushSize / 2); y <= Mathf.FloorToInt(_brushSize / 2); y++)
            {
                for (int x = -Mathf.FloorToInt(_brushSize / 2); x <= Mathf.FloorToInt(_brushSize / 2); x++)
                {
                    Vector3 p = new Vector3(x, 0, y);
                    Vector3 mouseOffset = _mousePosition + p;
                    //Snap to cell size
                    Vector3 cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);

                    bool insideRadius = Vector3.Distance(_mousePosition.Snap(t.cellSize.x,1,t.cellSize.y), mouseOffset) <= _brushSize / 2;

                    if (insideRadius && !_selectedCells.Contains(cellWorld))
                    {
                        _selectedCells.Add(cellWorld);
                    }
                }
            }
        }


        _cellPosWorld = _mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y);

        //Get cell pos (0 - dimensions.x, 0 - dimensions.z)
        if (t.chunks.ContainsKey(_chunkPos))
        {
            _cellPos = new Vector2Int(
                Mathf.FloorToInt((_cellPosWorld.x - t.chunks[_chunkPos].transform.position.x) / t.cellSize.x),
                Mathf.FloorToInt((_cellPosWorld.z - t.chunks[_chunkPos].transform.position.z) / t.cellSize.y)
            );
            if (_state== ToolState.SelectingCells || _state == ToolState.None)
            {
                _hoveredCellHeight = t.chunks[_chunkPos].heightMap[t.chunks[_chunkPos].GetIndex(_cellPos.y, _cellPos.x)];
            }
        }
    }
}
#endif
