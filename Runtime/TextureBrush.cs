#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[System.Serializable]
public class TextureBrush : TerrainTool
{
    Vector3 mousePosition;
    Vector3 cellPosWorld;
    Vector3 totalTerrainSize;

    Vector2 viewportMousePosition;

    Vector2Int chunkPos;
    Vector2Int cellPos;

    [SerializeField]
    Texture2D[] Layers;

    GUIContent[] textureContent;


    bool mouseDown = false;

    int selectedTexture = 0;
    float height = 0;
    float brushSize = 2;

    //cell world position, chunkPos
    Dictionary<Vector2Int, List<Vector3>> selectedCells = new Dictionary<Vector2Int, List<Vector3>>();
    public Color color = Color.white;

    public override void DrawHandles()
    {
        Handles.color = Color.green;

        foreach (var cell in selectedCells)
        {
            if (!t.chunks.ContainsKey(cell.Key))
            {
                selectedCells.Clear();
                break;
            }
            MarchingSquaresChunk c = t.chunks[cell.Key];

            //for (int i = 0; i < cell.Value.Count; i++)
            //{
            //    Vector3 p = cell.Value[i];
            //    Handles.DrawSolidDisc(p + Vector3.up * c.heightMap[c.GetIndex(cell.Key.y, cell.Key.x)], Vector3.up, brushSize / 2);
            //}

            Handles.color = Color.yellow;
            Vector3 chunkWorldPos = new Vector3(
                c.transform.position.x + totalTerrainSize.x / 2,
                0,
                c.transform.position.z + totalTerrainSize.z / 2
            );
            Handles.DrawWireCube(chunkWorldPos, totalTerrainSize);

        }
    }


    public override void ToolSelected()
    {
        if (Layers == null || Layers.Length < 4 )
        {
            Layers = new Texture2D[4];
        }
        textureContent = new GUIContent[4]
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
                mat.SetTexture("_Ground1", Layers[0]);
                mat.SetTexture("_Ground2", Layers[1]);
                mat.SetTexture("_Ground3", Layers[2]);
                mat.SetTexture("_Ground4", Layers[3]);
            }
        }

    }

    public override void ToolDeselected()
    {
        //Loop over all materials on chunks and set the keyword _OVERLAYVERTEXCOLORS to false
        foreach (var chunk in t.chunks.Values)
        {
            foreach (var mat in chunk.GetComponent<MeshRenderer>().sharedMaterials)
            {
                mat.DisableKeyword("_OVERLAYVERTEXCOLORS");
            }
        }

    }



    public override void OnMouseDown(int button = 0)
    {
        if (button == 0)
            mouseDown = true;
    }
    public override void OnMouseDrag(Vector2 delta)
    {
        if (!mouseDown)
            return;
        if (t.chunks.ContainsKey(chunkPos))
        {

        }
    }
    public override void OnMouseUp(int button = 0)
    {
        if (button == 0)
            mouseDown = false;
    }
    public override void Update()
    {
        selectedCells.Clear();
        totalTerrainSize = new Vector3(
            (t.dimensions.x - 1) * t.cellSize.x,
            0,
            (t.dimensions.z - 1) * t.cellSize.y
        );

        if (Event.current.type == EventType.ScrollWheel)
        {
            brushSize += -Event.current.delta.y * 0.1f;
            brushSize = Mathf.Clamp(brushSize, 1, 100);
            //Eat the event to prevent zooming in the scene view
            Event.current.Use();
        }

        switch (selectedTexture)
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

        viewportMousePosition = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, t.transform.position);
        groundPlane.Raycast(ray, out float distance);
        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, 1000, 1 << t.gameObject.layer);
        if (!hit)
        {
            Debug.Log("No hit");
        }
        mousePosition = hit ? new Vector3(hitInfo.point.x, 0, hitInfo.point.z) : ray.GetPoint(distance);

        for (int y = -Mathf.FloorToInt(brushSize / 2); y <= Mathf.FloorToInt(brushSize / 2); y++)
        {
            for (int x = -Mathf.FloorToInt(brushSize / 2); x <= Mathf.FloorToInt(brushSize / 2); x++)
            {
                Vector3 p = new Vector3(x,0, y);
                Vector3 mouseOffset = mousePosition + p;
                //Snap to cell size

                //Get chunk position at mouseOffset
                Vector2Int chunk = new Vector2Int(
                    Mathf.FloorToInt(mouseOffset.x / totalTerrainSize.x),
                    Mathf.FloorToInt(mouseOffset.z / totalTerrainSize.z)
                );
                float wX = (chunk.x * (t.dimensions.x - 1)) + x;
                float wZ = (chunk.y * (t.dimensions.z - 1)) + y;
                Vector3 cellWorld = mouseOffset.Snap(t.cellSize.x, 1, t.cellSize.y);


                bool insideRadius = Vector3.Distance(mousePosition.Snap(t.cellSize.x,1,t.cellSize.y),cellWorld) <= brushSize / 2;

                if (t.chunks.ContainsKey(chunk) && insideRadius)
                {
                    if (!selectedCells.ContainsKey(chunk))
                    {
                        selectedCells.Add(chunk, new List<Vector3>());
                        selectedCells[chunk].Add(cellWorld);
                    }
                    else
                    {
                        if (!selectedCells[chunk].Contains(cellWorld))
                            selectedCells[chunk].Add(cellWorld);
                    }
                }
            }
        }

        if (mouseDown)
        {
            foreach (var cell in selectedCells)
            {
                if (!t.chunks.ContainsKey(cell.Key))
                {
                    selectedCells.Clear();
                    break;
                }
                MarchingSquaresChunk c = t.chunks[cell.Key];
                List<Vector2Int> localCells = cell.Value.ConvertAll(v => new Vector2Int(
                    Mathf.FloorToInt((v.x - c.transform.position.x) / t.cellSize.x),
                    Mathf.FloorToInt((v.z - c.transform.position.z) / t.cellSize.y)
                ));
                c.DrawColors(localCells, color);
            }
        }

        chunkPos = new Vector2Int(
            Mathf.FloorToInt(mousePosition.x / totalTerrainSize.x),
            Mathf.FloorToInt(mousePosition.z / totalTerrainSize.z)
        );

        cellPosWorld = mousePosition.Snap(t.cellSize.x, 1, t.cellSize.y);

        if (t.chunks.ContainsKey(chunkPos))
        {
            cellPos = new Vector2Int(
                Mathf.FloorToInt((cellPosWorld.x - t.chunks[chunkPos].transform.position.x) / t.cellSize.x),
                Mathf.FloorToInt((cellPosWorld.z - t.chunks[chunkPos].transform.position.z) / t.cellSize.y)
            );
            height = t.chunks[chunkPos].heightMap[t.chunks[chunkPos].GetIndex(cellPos.y, cellPos.x)];
        }

    }



    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField("Brush Size: " + brushSize);

        EditorGUILayout.Space();
        //selectedTexture = GUILayout.Toolbar(selectedTexture, textureContent,GUILayout.MaxHeight(32),GUILayout.MaxWidth(48*4));
        EditorGUILayout.BeginHorizontal();

        //Set width of horizontal layout 
        for (int i = 0; i < Layers.Length; i++)
        {
            EditorGUILayout.BeginVertical();
            Texture2D tex = (Texture2D)EditorGUILayout.ObjectField("", Layers[i], typeof(Texture2D), false,GUILayout.MaxWidth(64));
            if (GUILayout.Toggle(selectedTexture == i, textureContent[i]))
                selectedTexture = i;
            
            EditorGUILayout.EndVertical();
            if (tex != Layers[i])
            {
                Layers[i] = tex;
                UpdateMaterialLayers(Layers);
            }
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }
}
#endif