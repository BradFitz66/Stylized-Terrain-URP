#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine.Pool;
using UnityEngine.Profiling;
using UnityEngine.Serialization;

[System.Serializable]
public class TextureBrush : TerrainTool
{
    private static ProfilerMarker _collectCellsMarker = new ProfilerMarker("Collect Cells _ Texture Brush");
    private static ProfilerMarker _drawColorsMarker = new ProfilerMarker("Draw Colors _ Texture Brush");
    private static ProfilerMarker _collectCellsInnerMarker = new ProfilerMarker("Collect Cells Inner _ Texture Brush");
    [FormerlySerializedAs("Layers")] [SerializeField]
    private Texture2D[] layers;
    private GUIContent[] _textureContent;

    private bool _mouseDown;
    private bool _selectedHeightOnly;
    private float _selectedHeight = 0;
    private bool _fallOff;
    private int _selectedTexture;
    private float _brushSize = 2;
    private List<Vector3> _selectedCells;
    private Color _color = Color.white;
    private GUIStyle _labelStyle;
    private AnimationCurve _falloffCurve = AnimationCurve.Linear(0,1,1,0);
    private Vector3 _mousePosition;
    private bool _samplingHeight;
    private readonly GUIStyle _style = new GUIStyle();


    public override void DrawHandles()
    {
        if (!HandleMaterial || HandleBuffer == null || !HandleBuffer.IsValid() || !HandleMesh)
        {
            InitializeInstancingInfo();
            return;
        }

        _falloffCurve = AnimationCurve.Linear(0, 1, 1, 0);
        _labelStyle.normal.textColor = Color.black;

        _collectCellsMarker.Begin();
        var brushSizeHalf = Mathf.FloorToInt(_brushSize / 2);
        for (var y = -brushSizeHalf; y <= brushSizeHalf; y++)
        {
            for (var x = -brushSizeHalf; x <= brushSizeHalf; x++)
            {
                var p = new Vector3(x, 0, y);
                var mouseOffset = _mousePosition + p;
                var cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);
                var insideRadius = Vector3.Distance(_mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y), cellWorld) <= _brushSize/2;

                if (!insideRadius)
                    continue;
                
                
                var withinBounds = t.TotalTerrainSize.Contains(cellWorld);
                
                if (!withinBounds && !_selectedCells.Contains(cellWorld))
                    continue;
                
                var dist = Vector3.Distance(cellWorld, _mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y));
                var falloff = _fallOff 
                    ? _falloffCurve.Evaluate(((_brushSize / 2) - dist) / (_brushSize / 2)) 
                    : 0;
                
                var height = t.GetHeightAtWorldPosition(cellWorld);
                if(_selectedHeightOnly && Mathf.Approximately(height, _selectedHeight) == false)
                    continue;
                
                var newInstance = new BrushHandleInstance()
                {
                    matrix = float4x4.TRS(
                        cellWorld + (Vector3.up * (height + .5f)),
                        Quaternion.Euler(90, 0, 0),
                        Vector3.Lerp(Vector3.one * 0.1f, new Vector3(t.cellSize.x * .75f, t.cellSize.y * .75f, 1),
                            1 - falloff)
                    ),
                    heightOffset = height
                };
                
                //Make sure HandleInstances doesn't already contain this instance
                // if(HandleInstances.Contains(newInstance))
                //     continue;

                HandleInstances.Add(newInstance);
                _selectedCells.Add(cellWorld);
            }
        }
        
        HandleBuffer?.SetData(HandleInstances, 0, 0, HandleInstances.Count);
        RenderParameters.matProps?.SetBuffer("_TerrainHandles", HandleBuffer);
        

        _collectCellsMarker.End();
        
        _drawColorsMarker.Begin();
        if (_mouseDown)
        {
            t.DrawColors(_selectedCells, _mousePosition, _brushSize, _color, _fallOff);
        }
        _drawColorsMarker.End();

        if(HandleInstances.Count>0)
            Graphics.RenderMeshPrimitives(RenderParameters, HandleMesh, 0,_selectedCells.Count);

        _selectedCells?.Clear();
        HandleInstances?.Clear();
        if(_samplingHeight)
            Handles.Label(_mousePosition + Vector3.up * 2, "Sampling height",_style);
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

    public override void ToolSelected()
    {
        _selectedCells = new List<Vector3>();
        _labelStyle = new GUIStyle();
        if (layers == null || layers.Length < 4)
            layers = new Texture2D[4];

        _textureContent = new GUIContent[4]
        {
            new GUIContent("R"),
            new GUIContent("G"),
            new GUIContent("B"),
            new GUIContent("A")
        };

    }

    void UpdateMaterialLayers(Texture2D[] l)
    {
        var mr = t.GetComponentsInChildren<MeshRenderer>();
        foreach (var r in mr)
        {
            
            var mat = r.sharedMaterial;
            
            mat?.SetTexture("_Ground1", layers[0]);
            mat?.SetTexture("_Ground2", layers[1]);
            mat?.SetTexture("_Ground3", layers[2]);
            mat?.SetTexture("_Ground4", layers[3]);
        }
    }

    public override void ToolDeselected()
    {

    }

    

    public override void OnMouseDown(int button = 0)
    {
        if (button == 0 && !_samplingHeight)
            _mouseDown = true;
        else if(button == 0 && _samplingHeight)
        {
            _selectedHeight = t.GetHeightAtWorldPosition(_mousePosition);
            _samplingHeight = false;
        }
    }
    public override void OnMouseUp(int button = 0)
    {
        if (button == 0)
            _mouseDown = false;
    }
    public override void OnMouseDrag(Vector2 delta)
    {

    }
    public override void Update()
    {
        if (!HandleMaterial || HandleBuffer == null || !HandleMesh)
        {
            
            InitializeInstancingInfo();
            return;
        }

        if (Event.current.type == EventType.ScrollWheel)
        {
            _brushSize += -Event.current.delta.y * 0.1f;
            _brushSize = Mathf.Clamp(_brushSize, t.cellSize.magnitude, 100);
            //Eat the event to prevent zooming in the scene view
            Event.current.Use();
        }

        switch (_selectedTexture)
        {
            case 0:
                _color = new Color(1, 0, 0, 0);
                break;
            case 1:
                _color = new Color(0, 1, 0, 0);
                break;
            case 2:
                _color = new Color(0, 0, 1, 0);
                break;
            case 3:
                _color = new Color(0, 0, 0, 1);
                break;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, t.transform.position);
        groundPlane.Raycast(ray, out float distance);
        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, 1000);
        _mousePosition = hit ? new Vector3(hitInfo.point.x, 0, hitInfo.point.z) : ray.GetPoint(distance);
        



        if(Event.current.type == EventType.KeyDown)
        {
            if (Event.current.keyCode == KeyCode.LeftShift)
            {
                _fallOff = true;
            }
            if (Event.current.keyCode == KeyCode.LeftControl)
            {
                _samplingHeight = true;
            }
        }
        else if (Event.current.type == EventType.KeyUp)
        {
            if (Event.current.keyCode == KeyCode.LeftShift)
            {
                _fallOff = false;
            }
            if (Event.current.keyCode == KeyCode.LeftControl)
            {
                _samplingHeight = false;
            }
        }
    }



    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        EditorGUILayout.LabelField("Brush Size: " + _brushSize);
        EditorGUILayout.LabelField("Hold shift to enable falloff");
        EditorGUILayout.LabelField("Hold ctrl and click to sample terrain height for paint selected height option");

        EditorGUILayout.Space();
        //selectedTexture = GUILayout.Toolbar(selectedTexture, textureContent,GUILayout.MaxHeight(32),GUILayout.MaxWidth(48*4));
        EditorGUILayout.BeginHorizontal();

        //Set width of horizontal layout 
        for (int i = 0; i < layers.Length; i++)
        {
            EditorGUILayout.BeginVertical();
            Texture2D tex = (Texture2D)EditorGUILayout.ObjectField("", layers[i], typeof(Texture2D), false, GUILayout.MaxWidth(64));
            if (GUILayout.Toggle(_selectedTexture == i, _textureContent[i]))
                _selectedTexture = i;

            EditorGUILayout.EndVertical();
            if (tex != layers[i])
            {
                layers[i] = tex;
                UpdateMaterialLayers(layers);
            }
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        _selectedHeightOnly = EditorGUILayout.Toggle("Paint Selected Height Only", _selectedHeightOnly);
        if (_selectedHeightOnly)
        {
            _selectedHeight = EditorGUILayout.FloatField("Selected Height", _selectedHeight);
        }
    }
}
#endif
