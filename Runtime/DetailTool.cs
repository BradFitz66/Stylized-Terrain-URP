#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.Serialization;

enum Mode
{
    Add,
    Remove
}


[System.Serializable]
public class DetailTool : TerrainTool
{

    private Vector3 _mousePosition;
    private Vector3 _totalTerrainSize;

    private Vector2Int _chunkPos;

    private bool _mouseDown;

    private Mode _mode = Mode.Add;

    public float hoveredCellHeight = 0;
    [FormerlySerializedAs("brushSize")] private float _brushSize = 2;
    public float normalOffset = 0.5f;
    public float size = 1;

    //cell world position, chunkPos
    private Dictionary<Vector3, Vector2Int> _selectedCells = new Dictionary<Vector3, Vector2Int>();
    
    public override void DrawHandles()
    {

        Handles.color = Color.green;
        Handles.DrawSolidDisc(_mousePosition + Vector3.up * hoveredCellHeight, Vector3.up, _brushSize / 2);
        Handles.color = Color.white;
        Handles.DrawSolidDisc(mouseOffset + Vector3.up * (hoveredCellHeight+.01f), Vector3.up, 1);
        

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

    }
    public override void OnMouseDown(int button = 0)
    {
        if (button != 0)
            return;

        _mouseDown = true;
    }
    public override void OnMouseDrag(Vector2 delta)
    {

    }


    public override void OnMouseUp(int button = 0)
    {
        if (button != 0)
            return;
        _selectedCells.Clear();
        _mouseDown = false;
    }

    //Load data from EditorPrefs on selection
    public override void ToolSelected()
    {
        normalOffset = EditorPrefs.GetFloat("NormalOffset", 0.5f);
        _brushSize = EditorPrefs.GetFloat("BrushSize", 2);
        size = EditorPrefs.GetFloat("Size", 1);
    }

    public override void ToolDeselected()
    {
        EditorPrefs.SetFloat("BrushSize", _brushSize);
        EditorPrefs.SetFloat("NormalOffset", normalOffset);
        EditorPrefs.SetFloat("Size", size);

    }
    
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        normalOffset = EditorGUILayout.FloatField("Normal Offset", normalOffset);
        size = EditorGUILayout.FloatField("Size", size);

        //Draw toolbar for mode selection
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        _mode = (Mode)GUILayout.Toolbar((int)_mode, new string[] { "Add", "Remove" });
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        //Save editor values
        EditorPrefs.SetFloat("NormalOffset", normalOffset);
        EditorPrefs.SetFloat("Size", size);

        SerializedT.ApplyModifiedProperties();
    }

    private Vector3 mouseOffset;
    public override void Update()
    {
        _totalTerrainSize = new Vector3(
            (t.dimensions.x - 1) * t.cellSize.x,
            0,
            (t.dimensions.z - 1) * t.cellSize.y
        );

        var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        var groundPlane = new Plane(Vector3.up, t.transform.position);
        groundPlane.Raycast(ray, out var distance);
        var hit = Physics.Raycast(ray, out var hitInfo, 100, 1 << t.gameObject.layer);
        _mousePosition = hit ? new Vector3(hitInfo.point.x, hitInfo.point.y, hitInfo.point.z) : ray.GetPoint(distance);

        if (Event.current.type == EventType.ScrollWheel)
        {
            _brushSize += -Event.current.delta.y * 0.1f;
            _brushSize = Mathf.Clamp(_brushSize, 2, 100);
            //Eat the event to prevent zooming in the scene view
            Event.current.Use();
        }

        _chunkPos = new Vector2Int(
            Mathf.FloorToInt(_mousePosition.x / _totalTerrainSize.x),
            Mathf.FloorToInt(_mousePosition.z / _totalTerrainSize.z)
        );



        if (_mouseDown)
        {
            if (_mode == Mode.Add)
            {
                // for (int y = -Mathf.FloorToInt(_brushSize / 2); y <= Mathf.FloorToInt(_brushSize / 2); y++)
                // {
                //     for (int x = -Mathf.FloorToInt(_brushSize / 2); x <= Mathf.FloorToInt(_brushSize / 2); x++)
                //     {
                //         Vector3 p = new Vector3(x, 0, y);
                //         Vector3 mouseOffset = _mousePosition + p;
                //         Vector3 cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);
                //
                //
                //         bool insideRadius = Vector3.Distance(_mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y), cellWorld) <= _brushSize / 2;
                //
                //         List<MarchingSquaresChunk> chunks = t.GetChunksAtWorldPosition(cellWorld);
                //
                //         if (!_selectedCells.Contains(cellWorld) && insideRadius && chunks.Count > 0)
                //         {
                //             _selectedCells.Add(cellWorld);
                //         }
                //     }
                // }

                for (var y = -Mathf.FloorToInt(_brushSize / 2); y <= Mathf.FloorToInt(_brushSize / 2); y++)
                {
                    for (var x = -Mathf.FloorToInt(_brushSize / 2); x <= Mathf.FloorToInt(_brushSize / 2); x++)
                    {
                        var p = new Vector3(x, 0, y);
                        mouseOffset = _mousePosition + p/2;

                        //Get chunk position at mouseOffset
                        var chunk = new Vector2Int(
                            Mathf.FloorToInt(mouseOffset.x / _totalTerrainSize.x),
                            Mathf.FloorToInt(mouseOffset.z / _totalTerrainSize.z)
                        );

                        var insideRadius = Vector3.Distance(_mousePosition, mouseOffset) <= _brushSize / 2;
                        if (!t.chunks.ContainsKey(chunk) || !insideRadius)
                        {
                            return;
                        }
                        
                        var c = t.chunks[chunk];
                        t.AddDetail(size, normalOffset, mouseOffset, c);
                        
                    }
                }
            }
            else
            {
                t.RemoveDetail(_brushSize, _mousePosition);
            }

        }
    }
}
#endif