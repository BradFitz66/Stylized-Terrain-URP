using UnityEditor;
using UnityEngine;

[System.Serializable]
public class TerrainTool : ScriptableObject
{
    public MarchingSquaresTerrain t;
    public SerializedObject serializedT;
    public virtual void Update() { }

    public virtual void OnMouseDown(int button = 0) { }

    public virtual void OnMouseUp(int button = 0) { }

    public virtual void OnMouseDrag(Vector2 delta) { }

    public virtual void DrawHandles() { }

    

    public virtual void OnInspectorGUI() 
    {
    }

    public virtual void ToolSelected()
    {
    }

    public virtual void ToolDeselected()
    {
    }
}
