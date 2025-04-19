using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[System.Serializable]
public class SerializedDictionary<TKey, TValue> : ISerializationCallbackReceiver
{
    [System.Serializable]
    public class KeyValue
    {
        public TKey key;
        public TValue value;
    }

    public Dictionary<TKey, TValue> Dict { get; } = new Dictionary<TKey, TValue>();

    public List<TKey> Keys => new List<TKey>(Dict.Keys);
    public List<TValue> Values => new List<TValue>(Dict.Values);

    public bool ContainsKey(TKey kay)
    {
        return Dict.ContainsKey(kay);
    }

    public void Clear()
    {
        Dict.Clear();
        mData.Clear();
    }

    public void Add(TKey kay, TValue val)
    {
        Dict.Add(kay, val);
    }

    public void Remove(TKey key)
    {
        Dict.Remove(key);
    }

    public int Count => Dict.Count;

    //Iterator
    public Dictionary<TKey, TValue>.Enumerator GetEnumerator()
    {
        return Dict.GetEnumerator();
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        return Dict.TryGetValue(key, out value);
    }

    public TValue this[TKey index] { 
                                     get => Dict[index]; 
                                     set { if (Dict.ContainsKey(index)) Dict[index] = value; else Dict.Add(index, value); }
    }

    [FormerlySerializedAs("m_Data")] [SerializeField]
    private List<KeyValue> mData = new List<KeyValue>();

    public void OnAfterDeserialize()
    {
        Dict.Clear();
        foreach (var kv in mData)
            Dict.Add(kv.key, kv.value);
    }

    public void OnBeforeSerialize()
    {
        mData.Clear();
        foreach (var kv in Dict)
            mData.Add(new KeyValue { key = kv.Key, value = kv.Value });
    }

}
