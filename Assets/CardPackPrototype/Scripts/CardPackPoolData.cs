using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CardPackPoolEntry
{
    public bool Enabled = true;
    public CardPackData Pack;
    [Min(0f)] public float Weight = 1f;
}

[CreateAssetMenu(fileName = "CardPackPool", menuName = "Card Pack/Pack Pool")]
public sealed class CardPackPoolData : ScriptableObject
{
    public List<CardPackPoolEntry> Packs = new List<CardPackPoolEntry>();

    public float TotalWeight
    {
        get
        {
            float total = 0f;
            if (Packs == null) return total;
            for (int i = 0; i < Packs.Count; i++)
            {
                CardPackPoolEntry entry = Packs[i];
                if (entry != null && entry.Enabled && entry.Pack != null && entry.Weight > 0f)
                    total += entry.Weight;
            }
            return total;
        }
    }

    public CardPackData DrawRandomPack()
    {
        float total = TotalWeight;
        if (total <= 0f) return null;
        float roll = UnityEngine.Random.value * total;
        float accumulated = 0f;
        CardPackData lastValidPack = null;
        for (int i = 0; i < Packs.Count; i++)
        {
            CardPackPoolEntry entry = Packs[i];
            if (entry == null || !entry.Enabled || entry.Pack == null || entry.Weight <= 0f) continue;
            accumulated += entry.Weight;
            lastValidPack = entry.Pack;
            if (roll < accumulated) return entry.Pack;
        }
        return lastValidPack;
    }

    public float GetProbability(CardPackPoolEntry entry)
    {
        if (entry == null || !entry.Enabled || entry.Pack == null || entry.Weight <= 0f) return 0f;
        float total = TotalWeight;
        return total > 0f ? entry.Weight / total : 0f;
    }

    private void OnValidate()
    {
        if (Packs == null) return;
        for (int i = 0; i < Packs.Count; i++)
            if (Packs[i] != null) Packs[i].Weight = Mathf.Max(0f, Packs[i].Weight);
    }
}