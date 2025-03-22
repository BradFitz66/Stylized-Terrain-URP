using UnityEngine;
using UnityEditor;


[CustomEditor(typeof(MarchingSquaresTerrain))]
public class MarchingSquaresTerrainEditor : Editor
{

    [SerializeField]
    int _currentHandle;



    MarchingSquaresTerrain t;
    SerializedProperty terrainDimensions;
    SerializedProperty cellSize;
    SerializedProperty mergeThreshold;
    SerializedProperty tools;
    SerializedProperty selectedToolIndex;
    SerializedProperty terrainMaterial;
    SerializedProperty noiseSettings;
    SerializedProperty heightBanding;
    SerializedProperty detailDensity;
    SerializedProperty cloudSize;
    SerializedProperty cloudDensity;
    SerializedProperty cloudSpeed;
    SerializedProperty cloudBrightness;
    SerializedProperty cloudVerticalSpeed;

    SerializedProperty lastTool;
    SerializedProperty currentTool;

    TerrainTool lastToolInstance;
    TerrainTool currentToolInstance;


    public void OnEnable()
    {
        t = (MarchingSquaresTerrain)target;
        terrainDimensions = serializedObject.FindProperty("dimensions");
        mergeThreshold = serializedObject.FindProperty("mergeThreshold");
        cellSize = serializedObject.FindProperty("cellSize");
        tools = serializedObject.FindProperty("tools");
        selectedToolIndex = serializedObject.FindProperty("selectedTool");
        terrainMaterial = serializedObject.FindProperty("terrainMaterial");
        lastTool = serializedObject.FindProperty("lastTool");
        currentTool = serializedObject.FindProperty("currentTool");
        noiseSettings = serializedObject.FindProperty("noiseSettings");
        heightBanding = serializedObject.FindProperty("heightBanding");
        detailDensity = serializedObject.FindProperty("detailDensity");
        cloudSize = serializedObject.FindProperty("cloudScale");
        cloudDensity = serializedObject.FindProperty("cloudDensity");
        cloudSpeed = serializedObject.FindProperty("cloudSpeed");
        cloudBrightness = serializedObject.FindProperty("cloudBrightness");
        cloudVerticalSpeed = serializedObject.FindProperty("cloudVerticalSpeed");
        


        if (tools.arraySize != 4)
        {
            tools.arraySize = 4;
        }
        if (tools.GetArrayElementAtIndex(0).objectReferenceValue == null)
            tools.GetArrayElementAtIndex(0).objectReferenceValue = CreateInstance<ChunkBrush>();

        if (tools.GetArrayElementAtIndex(1).objectReferenceValue == null)
            tools.GetArrayElementAtIndex(1).objectReferenceValue = CreateInstance<SculptBrush>();

        if (tools.GetArrayElementAtIndex(2).objectReferenceValue == null)
            tools.GetArrayElementAtIndex(2).objectReferenceValue = CreateInstance<TextureBrush>();

        if (tools.GetArrayElementAtIndex(3).objectReferenceValue == null || tools.GetArrayElementAtIndex(3).objectReferenceValue.GetType() != typeof(DetailTool))
            tools.GetArrayElementAtIndex(3).objectReferenceValue = CreateInstance<DetailTool>();


        serializedObject.ApplyModifiedProperties();
        if (currentTool.objectReferenceValue != null)
        {
            currentToolInstance = (TerrainTool)currentTool.objectReferenceValue;
            currentToolInstance.ToolSelected();
        }
        if (terrainMaterial.objectReferenceValue == null)
        {
            //Check current path
            string path = AssetDatabase.GetAssetPath(t);
            Debug.Log(path);
        }
    }


    public override void OnInspectorGUI()
    {
        t = (MarchingSquaresTerrain)target;
        //Create toolbar for the terrain tools
        //Draw terrain dimensions fields
        serializedObject.Update();
        EditorGUILayout.PropertyField(terrainDimensions);
        EditorGUILayout.PropertyField(cellSize);
        EditorGUILayout.PropertyField(mergeThreshold);
        EditorGUILayout.PropertyField(terrainMaterial);
        EditorGUILayout.PropertyField(noiseSettings);
        EditorGUILayout.PropertyField(heightBanding);
        EditorGUILayout.PropertyField(detailDensity);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Cloud settings", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.PropertyField(cloudSize);
        EditorGUILayout.PropertyField(cloudDensity);
        EditorGUILayout.PropertyField(cloudSpeed);
        EditorGUILayout.PropertyField(cloudBrightness);
        EditorGUILayout.PropertyField(cloudVerticalSpeed);

        if (EditorGUI.EndChangeCheck())
        {
            //t.UpdateClouds();
        }

        if (GUILayout.Button("Generate terrain"))
        {
            t.GenerateTerrain();
        }
        if(GUILayout.Button("Clear details"))
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
        selectedToolIndex.intValue = GUILayout.Toolbar(selectedToolIndex.intValue, new string[] { "Chunk brush", "Sculpt brush", "Texture brush", "Detail brush" });
        TerrainTool selectingTool = (TerrainTool)tools.GetArrayElementAtIndex(selectedToolIndex.intValue).objectReferenceValue;
        if (selectingTool != null)
        {
            selectingTool.t = t;
            selectingTool.serializedT = serializedObject;
            if (currentToolInstance != selectingTool)
            {
                if (currentToolInstance != null && lastToolInstance != currentToolInstance)
                {
                    lastToolInstance = currentToolInstance;
                    lastToolInstance.ToolDeselected();
                }
                currentToolInstance = selectingTool;
                currentToolInstance.ToolSelected();
            }
        }
        EditorGUILayout.Separator();
        if (currentToolInstance)
        {
            currentToolInstance.OnInspectorGUI();
        }

        serializedObject.ApplyModifiedProperties();
    }



    public void OnSceneGUI()
    {

        if (t == null)
            return;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        Event current = Event.current;
        //Get editor mouse position
        _currentHandle = (EditorGUIUtility.hotControl != 0) ? EditorGUIUtility.hotControl : _currentHandle;
        Tools.current = Tool.None;

        if (currentToolInstance != null)
        {
            currentToolInstance.Update();
            currentToolInstance.DrawHandles();
            HandleEvent(currentToolInstance);
        }
    }

    void HandleEvent(TerrainTool tool)
    {
        Event current = Event.current;
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
        if (currentToolInstance != null)
        {
            currentToolInstance.ToolDeselected();
        }
    }
}

