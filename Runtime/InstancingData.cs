using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[PreferBinarySerialization]
public class InstancingData : ScriptableObject
{
    public int GetDetailCount()
    {
        return detailChunks.Values.SelectMany(x => x).Count();
    }

    public DetailObject[] GetDetailData()
    {
        return detailChunks.Values.SelectMany(x => x).ToArray();
    }

    public SerializedDictionary<Vector2Int, List<DetailObject>> detailChunks = new();


}
