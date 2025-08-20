using System;
using UnityEditor;
using UnityEngine;

[System.Serializable]
public class TerrainTool : ScriptableObject
{
    public MarchingSquaresTerrain t;

    public SerializedObject SerializedT;
    public virtual void Update() { }

    public virtual void OnMouseDown(int button = 0) { }

    public virtual void OnMouseUp(int button = 0) { }

    public virtual void OnMouseDrag(Vector2 delta) { }

    public virtual void DrawHandles() { }

    

    public virtual void OnInspectorGUI() 
    {
        if (t == null)
            return;
        
        EditorGUILayout.LabelField("----- Tool settings -----");
    }

    public virtual void ToolSelected()
    {
    }

    public virtual void ToolDeselected()
    {
    }
}
