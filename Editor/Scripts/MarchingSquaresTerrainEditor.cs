using UnityEngine;
using UnityEditor;
using UnityEngine.Serialization;


[CustomEditor(typeof(MarchingSquaresTerrain))]
public class MarchingSquaresTerrainEditor : Editor
{

    [FormerlySerializedAs("_currentHandle")] [SerializeField] private int currentHandle;



    MarchingSquaresTerrain t;
    SerializedProperty _terrainDimensions;
    private SerializedProperty _cellSize;
    private SerializedProperty _mergeThreshold;
    private SerializedProperty _tools;
    private SerializedProperty _selectedToolIndex;
    private SerializedProperty _terrainMaterial;
    private SerializedProperty _noiseSettings;
    private SerializedProperty _heightBanding;
    private SerializedProperty _detailDensity;
    private SerializedProperty _cloudSettings;
    private SerializedProperty _instancingData;
    private SerializedProperty _heightMapTexture;

    private SerializedProperty _lastTool;
    private SerializedProperty _currentTool;

    private TerrainTool _lastToolInstance;
    private TerrainTool _currentToolInstance;

    private bool _showGenerateGrassSettings;
    public void OnEnable()
    {
        t = (MarchingSquaresTerrain)target;
        _terrainDimensions = serializedObject.FindProperty("dimensions");
        _mergeThreshold = serializedObject.FindProperty("mergeThreshold");
        _cellSize = serializedObject.FindProperty("cellSize");
        _tools = serializedObject.FindProperty("tools");
        _selectedToolIndex = serializedObject.FindProperty("selectedTool");
        _terrainMaterial = serializedObject.FindProperty("terrainMaterial");
        _lastTool = serializedObject.FindProperty("lastTool");
        _currentTool = serializedObject.FindProperty("currentTool");
        _noiseSettings = serializedObject.FindProperty("noiseSettings");
        _heightBanding = serializedObject.FindProperty("heightBanding");
        _detailDensity = serializedObject.FindProperty("detailDensity");
        _cloudSettings = serializedObject.FindProperty("cloudSettings");
        _instancingData = serializedObject.FindProperty("instancingData");
        _heightMapTexture = serializedObject.FindProperty("heightMap");

        


        if (_tools.arraySize != 4)
        {
            _tools.arraySize = 4;
        }
        if (_tools.GetArrayElementAtIndex(0).objectReferenceValue == null)
            _tools.GetArrayElementAtIndex(0).objectReferenceValue = CreateInstance<ChunkBrush>();

        if (_tools.GetArrayElementAtIndex(1).objectReferenceValue == null)
            _tools.GetArrayElementAtIndex(1).objectReferenceValue = CreateInstance<SculptBrush>();

        if (_tools.GetArrayElementAtIndex(2).objectReferenceValue == null)
            _tools.GetArrayElementAtIndex(2).objectReferenceValue = CreateInstance<TextureBrush>();

        if (_tools.GetArrayElementAtIndex(3).objectReferenceValue == null || _tools.GetArrayElementAtIndex(3).objectReferenceValue.GetType() != typeof(DetailTool))
            _tools.GetArrayElementAtIndex(3).objectReferenceValue = CreateInstance<DetailTool>();


        serializedObject.ApplyModifiedProperties();
        if (_currentTool.objectReferenceValue != null)
        {
            _currentToolInstance = (TerrainTool)_currentTool.objectReferenceValue;
            _currentToolInstance.ToolSelected();
        }
        if (_terrainMaterial.objectReferenceValue == null)
        {
            //Check current path
            string path = AssetDatabase.GetAssetPath(t);
            Debug.Log(path);
        }
    }


    public override void OnInspectorGUI()
    {
        t = (MarchingSquaresTerrain)target;
        EditorGUILayout.PropertyField(_instancingData);

        EditorGUILayout.PropertyField(_terrainDimensions);
        EditorGUILayout.PropertyField(_cellSize);
        EditorGUILayout.PropertyField(_mergeThreshold);
        EditorGUILayout.PropertyField(_terrainMaterial);
        EditorGUILayout.PropertyField(_noiseSettings, true);
        if (_noiseSettings.isExpanded)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("If heightmap is set, noise settings will be ignored other than scale (scale will be used as a height multiplier)");
            EditorGUILayout.PropertyField(_heightMapTexture);
            
            if (GUILayout.Button("Generate terrain"))
            {
                t.GenerateTerrain();
            }
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.PropertyField(_cloudSettings);

        _showGenerateGrassSettings = EditorGUILayout.Foldout(_showGenerateGrassSettings, new GUIContent("Grass settings"));
        
        if (_showGenerateGrassSettings)
        {
            if (GUILayout.Button("Generate grass"))
            {
                var detailTool = _tools.GetArrayElementAtIndex(3).objectReferenceValue as DetailTool;
                t.GenerateGrass(detailTool.size, detailTool.normalOffset);
            }
        }
        EditorGUILayout.PropertyField(_heightBanding);
        EditorGUILayout.PropertyField(_detailDensity);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Cloud settings", EditorStyles.boldLabel);



        if (GUILayout.Button("Clear details"))
        {
            t.ClearDetails();
        }
        if (GUILayout.Button("Apply detail density"))
        {
            //Popup window telling user that this will erase all detail
            if (EditorUtility.DisplayDialog("Apply detail density", "This will erase all details on the terrain", "Ok", "Cancel"))
            {
                t.UpdateDensity();
            }
        }


        //Space
        EditorGUILayout.Space();

        //Toolbar 
        _selectedToolIndex.intValue = GUILayout.Toolbar(_selectedToolIndex.intValue, new string[] { "Chunk brush", "Sculpt brush", "Texture brush", "Detail brush" });
        var selectingTool = (TerrainTool)_tools.GetArrayElementAtIndex(_selectedToolIndex.intValue).objectReferenceValue;
        if (selectingTool != null)
        {
            selectingTool.t = t;
            selectingTool.SerializedT = serializedObject;
            if (_currentToolInstance != selectingTool)
            {
                if (_currentToolInstance != null && _lastToolInstance != _currentToolInstance)
                {
                    _lastToolInstance = _currentToolInstance;
                    _lastToolInstance.ToolDeselected();
                }
                _currentToolInstance = selectingTool;
                _currentToolInstance.ToolSelected();
            }
        }
        EditorGUILayout.Separator();
        if (_currentToolInstance)
        {
            _currentToolInstance.OnInspectorGUI();
        }

        serializedObject.ApplyModifiedProperties();
        //serializedObject.Update();
    }



    public void OnSceneGUI()
    {

        if (t == null)
            return;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        Event current = Event.current;
        //Get editor mouse position
        currentHandle = (GUIUtility.hotControl != 0) ? GUIUtility.hotControl : currentHandle;
        Tools.current = Tool.None;

        if (_currentToolInstance != null)
        {
            _currentToolInstance.Update();
            _currentToolInstance.DrawHandles();
            HandleEvent(_currentToolInstance);
        }
    }

    void HandleEvent(TerrainTool tool)
    {
        var current = Event.current;
        switch (current.type)
        {
            case EventType.MouseDown:
                tool.OnMouseDown(current.button);
                break;
            case EventType.MouseDrag:
                tool.OnMouseDrag(current.delta);
                break;
            case EventType.MouseUp:
                tool.OnMouseUp(current.button);
                break;
        }
    }

    private void OnDisable()
    {
        Tools.current = Tool.Move;
        if (_currentToolInstance != null)
        {
            _currentToolInstance.ToolDeselected();
        }
    }
}

