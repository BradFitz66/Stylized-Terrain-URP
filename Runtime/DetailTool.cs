#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

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

    float detailDensity = 1f;
    float hoveredCellHeight = 0;
    float brushSize = 2;

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

    GUIContent detailDensityLabel = new GUIContent("Detail Density", "Density(amount) of the added details");
    
    public override void OnInspectorGUI()
    {
        detailMesh = serializedT.FindProperty("detailMesh");
        detailMaterial = serializedT.FindProperty("detailMaterial");
        detailDensity = EditorGUILayout.FloatField(detailDensityLabel, detailDensity);
        detailMesh.objectReferenceValue = EditorGUILayout.ObjectField("Detail Mesh", detailMesh.objectReferenceValue, typeof(Mesh), false);
        detailMaterial.objectReferenceValue = EditorGUILayout.ObjectField("Detail Material", detailMaterial.objectReferenceValue, typeof(Material), false);
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
        mousePosition = hit ? new Vector3(hitInfo.point.x, 0, hitInfo.point.z) : ray.GetPoint(distance);

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
            for (int y = -Mathf.FloorToInt(brushSize / 2); y <= Mathf.FloorToInt(brushSize / 2); y++)
            {
                for (int x = -Mathf.FloorToInt(brushSize / 2); x <= Mathf.FloorToInt(brushSize / 2); x++)
                {
                    Vector3 p = new Vector3(x, 0, y);
                    Vector3 mouseOffset = mousePosition + p;
                    //Snap to cell size
                    Vector3 cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);

                    //Get chunk position at mouseOffset
                    Vector2Int chunk = new Vector2Int(
                        Mathf.FloorToInt(mouseOffset.x / totalTerrainSize.x),
                        Mathf.FloorToInt(mouseOffset.z / totalTerrainSize.z)
                    );
                    float wX = (chunk.x * (t.dimensions.x - 1)) + x;
                    float wZ = (chunk.y * (t.dimensions.z - 1)) + y;

                    bool insideRadius = Vector3.Distance(mousePosition,mouseOffset) <= brushSize / 2;

                    if (t.chunks.ContainsKey(chunk) && insideRadius)
                    {
                        MarchingSquaresChunk c = t.chunks[chunk];
                        Vector2Int localCellPos = new Vector2Int(
                            Mathf.FloorToInt((cellWorld.x - c.transform.position.x) / t.cellSize.x),
                            Mathf.FloorToInt((cellWorld.z - c.transform.position.z) / t.cellSize.y)
                        );
                        t.AddDetail(localCellPos.x, localCellPos.y, mouseOffset + p,c);
                    }
                }
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