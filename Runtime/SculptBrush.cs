#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using Unity.Profiling;

[System.Serializable]
public class SculptBrush : TerrainTool
{
    static ProfilerMarker _drawHandlesMarker = new ProfilerMarker("Sculpt Brush Draw Handles");
    static ProfilerMarker _updateMarker = new ProfilerMarker("Sculpt Brush Update");
    static ProfilerMarker _onMouseDragMarker = new ProfilerMarker("Sculpt Brush On Mouse Drag");

    private Vector3 _mousePosition;
    private Vector3 _cellPosWorld;
    private Vector3 _totalTerrainSize;
    private Vector3 _slopePoint1;
    private Vector3 _slopePoint2;

    private Vector2Int _cellPos;
    private Vector2Int _chunkPos;

    private bool _mouseDown;
    private bool _flattenGeometry;

    private float _setHeight;
    private float _dragHeight;
    private float _hoveredCellHeight;
    private float _brushSize = 2;
    private float _selectedHeight;
    private float _selectedHeightThreshold = 0.1f;
    private bool _selectedHeightOnly;

    
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
        DraggingHeight,
        SelectingSlopePoint1,
        SelectingSlopePoint2
    }

    private ToolState _state = ToolState.None;

    private GUIStyle _style = new GUIStyle();

    public override void ToolSelected()
    {
        _setHeight = EditorPrefs.GetFloat("setHeight_SCULPTBRUSH", 0);
		_smoothingCells = new List<Vector3>();
        _selectedCells = new List<Vector3>();
        InitializeInstancingInfo();

    }
    
    void InitializeInstancingInfo()
    {
        HandleMaterial = new Material(Shader.Find("Custom/TerrainHandlesInstanced"));
        HandleMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        HandleBuffer = new ComputeBuffer(MaxHandleCount, Marshal.SizeOf(typeof(BrushHandleInstance))); 

        RenderParameters = new RenderParams(HandleMaterial);
        RenderParameters.worldBounds = new Bounds(Vector3.zero, Vector3.one * 10000);
        RenderParameters.matProps = new MaterialPropertyBlock();
    }

    public override void ToolDeselected()
    {
        
        HandleBuffer?.Dispose();
        
    }


    public override void DrawHandles()
    {
        if (HandleMaterial == null || HandleBuffer == null || HandleMesh == null)
        {
            
            InitializeInstancingInfo();
            return;
        }

        _style.normal.textColor = Color.black;
        Handles.color = Color.green;
        if (_state != ToolState.DraggingHeight)
        {
            Handles.DrawSolidDisc(_cellPosWorld + Vector3.up * _hoveredCellHeight,Vector3.up, _brushSize / 2);
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
            case ToolState.SelectingSlopePoint1:
                Handles.Label(_mousePosition + Vector3.up * 2, "Selecting Slope Point 1",_style);
                break;
            case ToolState.SelectingSlopePoint2:
                Handles.Label(_mousePosition + Vector3.up * 2, "Selecting Slope Point 2",_style);
                Handles.color = Color.green;
                Handles.DrawLine(_slopePoint1 + Vector3.up * 0.1f, _cellPosWorld);
                Handles.color = Color.black;
                break;
        }

        Graphics.RenderMeshPrimitives(RenderParameters, HandleMesh, 0, _selectedCells.Count);
    }
    public override void OnMouseDown(int button = 0)
    {

        if (button == 0)
        {
            switch (_state)
            {
                case ToolState.None:       
                    _state = ToolState.SelectingCells;
                    _selectedHeight = _hoveredCellHeight;
                    break;
                case ToolState.SelectingSlopePoint1:
                    _slopePoint1 = _cellPosWorld;
                    _state = ToolState.SelectingSlopePoint2;
                    break;
                case ToolState.SelectingSlopePoint2:
                    _slopePoint2 = _cellPosWorld;
                    _state = ToolState.None;
                    GenerateSlope();
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

    private void GenerateSlope()
    {
        
        
        float height1 = t.GetHeightAtWorldPosition(_slopePoint1);
        float height2 = t.GetHeightAtWorldPosition(_slopePoint2);
        
        Vector3 direction = (_slopePoint2 - _slopePoint1).normalized;
        float distance = Vector3.Distance(_slopePoint1, _slopePoint2);
        float heightDifference = height2 - height1;
        int steps = Mathf.CeilToInt(distance / (t.mergeThreshold/2));
        Vector3 step = direction * (distance / steps);
        float heightStep = heightDifference / steps;
        
        List<Vector3> slopeCells = new List<Vector3>();
        List<float> slopeHeights = new List<float>();
        for (int i = 0; i <= steps; i++)
        {
            Vector3 pos = _slopePoint1 + step * i;
            float height = height1 + heightStep * i;
            for (int y = -Mathf.FloorToInt(_brushSize / 2); y <= Mathf.FloorToInt(_brushSize / 2); y++)
            {
                for (int x = -Mathf.FloorToInt(_brushSize / 2); x <= Mathf.FloorToInt(_brushSize / 2); x++)
                {
                    Vector3 p = new Vector3(x, 0, y);
                    Vector3 mouseOffset = pos + p;

                    Vector3 cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);

                    bool insideRadius = Vector3.Distance(pos.Snap(t.cellSize.x, 1, t.cellSize.y), mouseOffset) <= _brushSize / 2;

                    if (insideRadius && !slopeCells.Contains(cellWorld))
                    {
                        slopeCells.Add(cellWorld);
                        slopeHeights.Add(height);
                    }
                }
            }
        }
        t.DrawHeights(slopeCells, slopeHeights, true, 0);
        

    }

    static ProfilerMarker _smoothHeightCollect = new ProfilerMarker("Sculpt Brush Smooth Height Collect Cells");
    public override void OnMouseDrag(Vector2 delta)
    {
        _onMouseDragMarker.Begin();
        switch(_state)
        {
            case ToolState.DraggingHeight:
                _dragHeight += -delta.y * 0.1f;
                RenderParameters.matProps.SetFloat("_HeightOffset", _dragHeight);
                break;
            case ToolState.SmoothingHeight:
                if (!_mouseDown)
                    return;
                _smoothHeightCollect.Begin();
                for (int y = -Mathf.FloorToInt(_brushSize / 2); y <= Mathf.FloorToInt(_brushSize / 2); y++)
                {
                    for (int x = -Mathf.FloorToInt(_brushSize / 2); x <= Mathf.FloorToInt(_brushSize / 2); x++)
                    {
                        Vector3 p = new Vector3(x, 0, y);
                        Vector3 mouseOffset = _mousePosition + p;
                        Vector3 cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);
                        
                        bool insideRadius = Vector3.Distance(_mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y), mouseOffset) <= _brushSize / 2;
                        if (insideRadius && !_smoothingCells.Contains(cellWorld))
                        {
                            _smoothingCells.Add(cellWorld);
                        }
                    }
                }
                _smoothHeightCollect.End();

                t.SmoothHeights(_smoothingCells);
                _smoothingCells.Clear();
                break;
            case ToolState.SelectingCells:
                for (int y = -Mathf.FloorToInt(_brushSize / 2); y <= Mathf.FloorToInt(_brushSize / 2); y++)
                {
                    for (int x = -Mathf.FloorToInt(_brushSize / 2); x <= Mathf.FloorToInt(_brushSize / 2); x++)
                    {
                        Vector3 p = new Vector3(x, 0, y);
                        Vector3 mouseOffset = _mousePosition + p;
                        //Snap to cell size
                        Vector3 cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);
                        float height = t.GetHeightAtWorldPosition(cellWorld);
                        bool insideRadius = Vector3.Distance(_mousePosition.Snap(t.cellSize.x,1,t.cellSize.y), mouseOffset) <= _brushSize / 2;
                        
                        int chunksAtPos = t.GetAmountOfChunksAtWorldPosition(cellWorld);
                        
                        if (insideRadius && !_selectedCells.Contains(cellWorld) && chunksAtPos>0)
                        {

                            if (_selectedHeightOnly && Mathf.Abs(height - _selectedHeight) > _selectedHeightThreshold)
                                continue;
                            _selectedCells.Add(cellWorld);
                            HandleInstances.Add(new BrushHandleInstance()
                            {
                                matrix = float4x4.TRS(cellWorld + Vector3.up * (height + 0.5f), quaternion.Euler(math.radians(90),0,0), new float3(t.cellSize.x*.75f, t.cellSize.y*.75f, 1.0f))
                            });
                            HandleBuffer?.SetData(HandleInstances);
                            RenderParameters.matProps.SetBuffer("_TerrainHandles", HandleBuffer);
                            
                        }
                    }
                }
                break;

        }
        _onMouseDragMarker.End();
    }


    public override void OnMouseUp(int button = 0)
    {
        if(button != 0)
            return;
        switch (_state)
        {
            case ToolState.SelectingCells:
                if (_selectedCells.Count > 0)
                    _state = ToolState.SelectedCells;
                else
                    _state = ToolState.None;
                
                break;
            case ToolState.DraggingHeight:
                t.DrawHeights(_selectedCells, _dragHeight, _flattenGeometry, _flattenGeometry, _selectedHeight);
                _dragHeight = 0;
                _state = ToolState.None;
                break;
            case ToolState.SmoothingHeight:
                _state = ToolState.None;
                break;
        }

        _mouseDown = false;
    }

    private GUIContent _flattenLabel = new GUIContent("Flatten Geometry","Averages the final height of selected cells to ensure they're flat");
    private GUIContent _selectedHeightThresholdLabel = new GUIContent("Selected Height Threshold", "The threshold for selecting heights. If the difference between the first cell that was clicked on and the height of the cell currently being selected is greater than this value, the cell will not be selected.");
    private GUIContent _selectedHeightOnlyLabel = new GUIContent("Selected Height Only", "Only select cells with a similar height to the height of the cell that was first clicked on.");
    public override void OnInspectorGUI()
    {
        
        base.OnInspectorGUI();
        
        _flattenGeometry = EditorGUILayout.Toggle(_flattenLabel, _flattenGeometry);
        _selectedHeightOnly = EditorGUILayout.Toggle(_selectedHeightOnlyLabel, _selectedHeightOnly);
        if (_selectedHeightOnly)
        {
            EditorGUI.indentLevel++;
            _selectedHeightThreshold = EditorGUILayout.FloatField(_selectedHeightThresholdLabel, _selectedHeightThreshold);
            EditorGUI.indentLevel--;
        }
        _setHeight = EditorGUILayout.FloatField("Set Height", _setHeight);

        //Save setHeight
        EditorPrefs.SetFloat("setHeight_SCULPTBRUSH", _setHeight);
    }
    public override void Update()
    {
        _updateMarker.Begin();
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
            HandleInstances.Clear();
            HandleBuffer = new ComputeBuffer(MaxHandleCount, Marshal.SizeOf(typeof(BrushHandleInstance))); 
            RenderParameters.matProps.SetBuffer("_TerrainHandles", HandleBuffer);
            RenderParameters.matProps.SetFloat("_HeightOffset", 0);

        }

        var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        var groundPlane = new Plane(Vector3.up, t.transform.position);
        groundPlane.Raycast(ray, out var distance);
        var hit = Physics.Raycast(ray, out var hitInfo,1000, 1 << t.gameObject.layer);
        _mousePosition = hit ? new Vector3(hitInfo.point.x,0,hitInfo.point.z) : ray.GetPoint(distance);

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.LeftShift)
        {
            _state = ToolState.SmoothingHeight;
        }
        else if (Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.LeftShift)
        {
            _state = ToolState.None;
        }
        
        if(Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.LeftControl && _state == ToolState.None)
        {
            _state = ToolState.SelectingSlopePoint1;
            _selectedCells.Clear();
            _dragHeight = 0;
            _mouseDown = false;
        }
        else if (Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.LeftControl && _state == ToolState.SelectingSlopePoint1)
        {
            _state = ToolState.None;
            _slopePoint1 = Vector3.zero;
            _slopePoint2 = Vector3.zero;
        }

        if (Event.current.type == EventType.ScrollWheel)
        {
            _brushSize += -Event.current.delta.y * 0.1f;
            _brushSize = Mathf.Clamp(_brushSize, 1, 100);
            //Eat the event to prevent zooming in the scene view
            Event.current.Use();
        }


        _chunkPos = new Vector2Int(
            Mathf.FloorToInt(_mousePosition.x / _totalTerrainSize.x),
            Mathf.FloorToInt(_mousePosition.z / _totalTerrainSize.z)
        );

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
        _updateMarker.End();
    }
    
}
#endif
