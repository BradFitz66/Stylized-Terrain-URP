using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SerializedDictionary<TKey, TValue> : ISerializationCallbackReceiver
{
    [System.Serializable]
    public class KeyValue
    {
        public TKey key;
        public TValue value;
    }

    private Dictionary<TKey, TValue> m_Dict = new Dictionary<TKey, TValue>();
    public Dictionary<TKey, TValue> Dict => m_Dict;

    public List<TKey> Keys => new List<TKey>(m_Dict.Keys);
    public List<TValue> Values => new List<TValue>(m_Dict.Values);

    public bool ContainsKey(TKey kay)
    {
        return m_Dict.ContainsKey(kay);
    }

    public void Clear()
    {
        m_Dict.Clear();
        m_Data.Clear();
    }

    public void Add(TKey kay, TValue val)
    {
        m_Dict.Add(kay, val);
    }

    public void Remove(TKey key)
    {
        m_Dict.Remove(key);
    }

    public int Count => m_Dict.Count;

    //Iterator
    public Dictionary<TKey, TValue>.Enumerator GetEnumerator()
    {
        return m_Dict.GetEnumerator();
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        return m_Dict.TryGetValue(key, out value);
    }

    public TValue this[TKey index] { 
                                     get => m_Dict[index]; 
                                     set { if (m_Dict.ContainsKey(index)) m_Dict[index] = value; else m_Dict.Add(index, value); }
    }

    [SerializeField]
    private List<KeyValue> m_Data = new List<KeyValue>();

    public void OnAfterDeserialize()
    {
        m_Dict.Clear();
        foreach (var kv in m_Data)
            m_Dict.Add(kv.key, kv.value);
    }

    public void OnBeforeSerialize()
    {
        m_Data.Clear();
        foreach (var kv in m_Dict)
            m_Data.Add(new KeyValue { key = kv.Key, value = kv.Value });
    }

}
