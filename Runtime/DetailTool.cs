#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

enum Mode
{
    Add,
    Remove
}

[System.Serializable]
public class DetailTool : TerrainTool
{

    Vector3 mousePosition;
    Vector3 cellPosWorld;
    Vector3 totalTerrainSize;
    Vector3 viewportMousePosition;

    Vector2Int cellPos;
    Vector2Int chunkPos;

    bool mouseDown = false;

    Mode mode = Mode.Add;

    float detailDensity = 1f;
    float hoveredCellHeight = 0;
    float brushSize = 2;
    float normalOffset = 0.5f;
    float size = 1;

    //cell world position, chunkPos
    Dictionary<Vector3, Vector2Int> selectedCells = new Dictionary<Vector3, Vector2Int>();

    SerializedProperty detailMaterial;
    SerializedProperty detailMesh;

    public override void DrawHandles()
    {

        Handles.color = Color.green;
        Handles.DrawSolidDisc(mousePosition + Vector3.up * hoveredCellHeight, Vector3.up, brushSize / 2);


        //Draw wire around hovered chunk
        if (t.chunks.ContainsKey(chunkPos))
        {
            Handles.color = Color.yellow;
            Vector3 chunkWorldPos = new Vector3(
                t.chunks[chunkPos].transform.position.x + totalTerrainSize.x / 2,
                0,
                t.chunks[chunkPos].transform.position.z + totalTerrainSize.z / 2
            );
            Handles.DrawWireCube(chunkWorldPos, totalTerrainSize);
        }

    }
    public override void OnMouseDown(int button = 0)
    {
        if (button != 0)
            return;
        
        mouseDown = true;
    }
    public override void OnMouseDrag(Vector2 delta)
    {

    }


    public override void OnMouseUp(int button = 0)
    {
        if (button != 0)
            return;
        selectedCells.Clear();
        mouseDown = false;
    }

    //Load data from EditorPrefs on selection
    public override void ToolSelected()
    {
        detailDensity = EditorPrefs.GetFloat("DetailDensity", 1);
        normalOffset = EditorPrefs.GetFloat("NormalOffset", 0.5f);
    }

    GUIContent detailDensityLabel = new GUIContent("Detail Density", "Density(amount) of the added details");
    
    public override void OnInspectorGUI()
    {
        detailMesh = serializedT.FindProperty("detailMesh");
        detailMaterial = serializedT.FindProperty("detailMaterial");
        detailDensity = EditorGUILayout.FloatField(detailDensityLabel, detailDensity);
        normalOffset = EditorGUILayout.FloatField("Normal Offset", normalOffset);
        size = EditorGUILayout.FloatField("Size", size);
        detailMesh.objectReferenceValue = EditorGUILayout.ObjectField("Detail Mesh", detailMesh.objectReferenceValue, typeof(Mesh), false);
        detailMaterial.objectReferenceValue = EditorGUILayout.ObjectField("Detail Material", detailMaterial.objectReferenceValue, typeof(Material), false);

        //Draw toolbar for mode selection
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        mode = (Mode)GUILayout.Toolbar((int)mode, new string[] { "Add", "Remove" });
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        //Save editor values
        EditorPrefs.SetFloat("DetailDensity", detailDensity);
        EditorPrefs.SetFloat("NormalOffset", normalOffset);
        EditorPrefs.SetFloat("Size", size);

        serializedT.ApplyModifiedProperties();
    }

    public override void Update()
    {
        totalTerrainSize = new Vector3(
            (t.dimensions.x - 1) * t.cellSize.x,
            0,
            (t.dimensions.z - 1) * t.cellSize.y
        );

        viewportMousePosition = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, t.transform.position);
        groundPlane.Raycast(ray, out float distance);
        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, 100, 1 << t.gameObject.layer);
        mousePosition = hit ? new Vector3(hitInfo.point.x, hitInfo.point.y, hitInfo.point.z) : ray.GetPoint(distance);

        if (Event.current.type == EventType.ScrollWheel)
        {
            brushSize += -Event.current.delta.y * 0.1f;
            brushSize = Mathf.Clamp(brushSize, 2, 100);
            //Eat the event to prevent zooming in the scene view
            Event.current.Use();
        }

        chunkPos = new Vector2Int(
            Mathf.FloorToInt(mousePosition.x / totalTerrainSize.x),
            Mathf.FloorToInt(mousePosition.z / totalTerrainSize.z)
        );
        
       

        if (mouseDown)
        {
            if(mode == Mode.Add)
            {
                for (int y = -Mathf.FloorToInt(brushSize / 2); y <= Mathf.FloorToInt(brushSize / 2); y++)
                {
                    for (int x = -Mathf.FloorToInt(brushSize / 2); x <= Mathf.FloorToInt(brushSize / 2); x++)
                    {
                        Vector3 p = new Vector3(x, 0, y);
                        Vector3 mouseOffset = mousePosition + p;

                        //Get chunk position at mouseOffset
                        Vector2Int chunk = new Vector2Int(
                            Mathf.FloorToInt(mouseOffset.x / totalTerrainSize.x),
                            Mathf.FloorToInt(mouseOffset.z / totalTerrainSize.z)
                        );

                        bool insideRadius = Vector3.Distance(mousePosition, mouseOffset) <= brushSize / 2;

                        if (t.chunks.ContainsKey(chunk) && insideRadius)
                        {
                            MarchingSquaresChunk c = t.chunks[chunk];
                            t.AddDetail(size, normalOffset, mouseOffset, c);
                        }
                    }
                }
            }
            else
            {
                t.RemoveDetail(brushSize, mousePosition);
            }

        }

        cellPosWorld = mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y);

        //Get cell pos (0 - dimensions.x, 0 - dimensions.z)
        if (t.chunks.ContainsKey(chunkPos))
        {
            cellPos = new Vector2Int(
                Mathf.FloorToInt((cellPosWorld.x - t.chunks[chunkPos].transform.position.x) / t.cellSize.x),
                Mathf.FloorToInt((cellPosWorld.z - t.chunks[chunkPos].transform.position.z) / t.cellSize.y)
            );
        }
    }
}
#endif