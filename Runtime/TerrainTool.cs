using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

[System.Serializable]
public class TerrainTool : ScriptableObject
{
    
    protected struct BrushHandleInstance
    {
        public float4x4 matrix;
        public float heightOffset;
    }
    
    public MarchingSquaresTerrain t;
    public SerializedObject SerializedT;
    
    protected const int MaxHandleCount = 40960; //Max instances we can draw at once

    protected Material HandleMaterial;
    protected Mesh HandleMesh;
    protected RenderParams RenderParameters;
    protected ComputeBuffer HandleBuffer;
    protected List<BrushHandleInstance> HandleInstances = new List<BrushHandleInstance>();

    
    
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
