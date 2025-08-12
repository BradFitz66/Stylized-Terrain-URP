#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.Serialization;

[System.Serializable]
public class TextureBrush : TerrainTool
{
    private Vector3 _mousePosition;

    [FormerlySerializedAs("Layers")] [SerializeField]
    private Texture2D[] layers;

    private GUIContent[] _textureContent;

    private bool _mouseDown;
    private bool _fallOff;

    private int _selectedTexture;

    private float _brushSize = 2;

    private List<Vector3> _selectedCells;

    public Color color = Color.white;

    private GUIStyle _labelStyle;

    private AnimationCurve _falloffCurve = AnimationCurve.Linear(0,1,1,0);
    public override void DrawHandles()
    {
        _falloffCurve = AnimationCurve.Linear(0, 1, 1, 0);
        _labelStyle.normal.textColor = Color.black;
        Handles.color = Color.green;
        foreach (var cell in _selectedCells)
        {
            var marchingSquaresChunks = t.GetChunksAtWorldPosition(cell);

            if (marchingSquaresChunks.Count == 0)
                break;

            var dist = Vector3.Distance(cell, _mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y));
            var falloff = _fallOff 
                            ? _falloffCurve.Evaluate(((_brushSize / 2) - dist) / (_brushSize / 2)) 
                            : 0;

            var c = marchingSquaresChunks[0];
            var localCell = new Vector2Int(
                Mathf.FloorToInt((cell.x - c.transform.position.x) / t.cellSize.x),
                Mathf.FloorToInt((cell.z - c.transform.position.z) / t.cellSize.y)
            );

            Handles.DrawSolidDisc(cell + Vector3.up * c.heightMap[c.GetIndex(localCell.y, localCell.x)], Vector3.up, Mathf.Lerp(t.cellSize.x / 2, 0, falloff));
        }

        _selectedCells.Clear();
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
        foreach (MarchingSquaresChunk chunk in t.chunks.Values)
        {
            foreach (Material mat in chunk.GetComponent<MeshRenderer>().sharedMaterials)
            {
                mat.SetTexture("_Ground1", layers[0]);
                mat.SetTexture("_Ground2", layers[1]);
                mat.SetTexture("_Ground3", layers[2]);
                mat.SetTexture("_Ground4", layers[3]);
            }
        }

    }

    public override void ToolDeselected()
    {

    }

    

    public override void OnMouseDown(int button = 0)
    {
        if (button == 0)
            _mouseDown = true;
    }
    public override void OnMouseUp(int button = 0)
    {
        if (button == 0)
            _mouseDown = false;
    }
    public override void Update()
    {
        _selectedCells?.Clear();

        if (Event.current.type == EventType.ScrollWheel)
        {
            _brushSize += -Event.current.delta.y * 0.1f;
            _brushSize = Mathf.Clamp(_brushSize, t.cellSize.Length, 100);
            //Eat the event to prevent zooming in the scene view
            Event.current.Use();
        }

        switch (_selectedTexture)
        {
            case 0:
                color = new Color(1, 0, 0, 0);
                break;
            case 1:
                color = new Color(0, 1, 0, 0);
                break;
            case 2:
                color = new Color(0, 0, 1, 0);
                break;
            case 3:
                color = new Color(0, 0, 0, 1);
                break;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, t.transform.position);
        groundPlane.Raycast(ray, out float distance);
        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, 1000, 1 << t.gameObject.layer);
        _mousePosition = hit ? new Vector3(hitInfo.point.x, 0, hitInfo.point.z) : ray.GetPoint(distance);

        for (int y = -Mathf.FloorToInt(_brushSize / 2); y <= Mathf.FloorToInt(_brushSize / 2); y++)
        {
            for (int x = -Mathf.FloorToInt(_brushSize / 2); x <= Mathf.FloorToInt(_brushSize / 2); x++)
            {
                Vector3 p = new Vector3(x, 0, y);
                Vector3 mouseOffset = _mousePosition + p;
                Vector3 cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);


                bool insideRadius = Vector3.Distance(_mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y), cellWorld) <= _brushSize / 2;

                List<MarchingSquaresChunk> chunks = t.GetChunksAtWorldPosition(cellWorld);

                if (!_selectedCells.Contains(cellWorld) && insideRadius && chunks.Count > 0)
                {
                    _selectedCells.Add(cellWorld);
                }
            }
        }

        if (_mouseDown)
        {
            t.DrawColors(_selectedCells, _mousePosition, _brushSize, color, _fallOff);
        }

        if(Event.current.type == EventType.KeyDown)
        {
            if (Event.current.keyCode == KeyCode.LeftShift)
            {
                _fallOff = true;
            }
        }
        else if (Event.current.type == EventType.KeyUp)
        {
            if (Event.current.keyCode == KeyCode.LeftShift)
            {
                _fallOff = false;
            }
        }
    }



    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField("Brush Size: " + _brushSize);
        EditorGUILayout.LabelField("Hold shift to enable falloff");

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
    }
}
#endif
