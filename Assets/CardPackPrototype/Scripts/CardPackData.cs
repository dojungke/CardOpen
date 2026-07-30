using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CardPackEntry
{
    public CardData Card;
    [Range(1, 6)] public int Number = 1;
    public CardColor Color = CardColor.Green;
    [Range(0f, 100f), Tooltip("Relative draw weight within the eligible card list.")]
    public float InclusionRate = 100f;

    public int DisplayNumber { get { return Mathf.Clamp(Number, 1, 6); } }
    public string AttributeAssetKey { get { return Color.ToString(); } }
}

[CreateAssetMenu(fileName = "CardPack", menuName = "CardOpen/Card Pack")]
public class CardPackData : ScriptableObject
{
    public string Name;
    public Texture2D FrontImage;
    public Texture2D BackImage;
    [Min(1)] public int CardsPerPack = 5;

    [Header("Draw Rules")]
    public bool UseRarityRates;
    [Range(0f, 100f)] public float CommonRate = 60f;
    [Range(0f, 100f)] public float UncommonRate = 25f;
    [Range(0f, 100f)] public float RareRate = 10f;
    [Range(0f, 100f)] public float EpicRate = 5f;
    [Range(0f, 100f)] public float LegendaryRate = 0f;
    [Tooltip("Ignore stored entry attributes and roll a new number and color for every draw.")]
    public bool RandomizeNumberAndColor;

    public List<CardPackEntry> IncludeCards = new List<CardPackEntry>();

    [ContextMenu("Apply Rarity Rates To Included Cards")]
    public void ApplyRarityRatesToEntries()
    {
        if (IncludeCards == null || IncludeCards.Count == 0) return;

        int[] rarityCounts = new int[Enum.GetValues(typeof(CardRarity)).Length];
        for (int i = 0; i < IncludeCards.Count; i++)
        {
            CardPackEntry entry = IncludeCards[i];
            if (entry == null || entry.Card == null) continue;
            rarityCounts[(int)entry.Card.Rare]++;
        }

        for (int i = 0; i < IncludeCards.Count; i++)
        {
            CardPackEntry entry = IncludeCards[i];
            if (entry == null || entry.Card == null) continue;
            int rarityIndex = (int)entry.Card.Rare;
            int count = rarityCounts[rarityIndex];
            entry.InclusionRate = count > 0 ? GetRarityRate(entry.Card.Rare) / count : 0f;
        }
    }

    public float GetRarityRate(CardRarity rarity)
    {
        switch (rarity)
        {
            case CardRarity.Uncommon: return UncommonRate;
            case CardRarity.Rare: return RareRate;
            case CardRarity.Epic: return EpicRate;
            case CardRarity.Legendary: return LegendaryRate;
            default: return CommonRate;
        }
    }
    public CardPackEntry DrawRandomCard()
    {
        if (IncludeCards == null || IncludeCards.Count == 0) return null;

        CardPackEntry selected = UseRarityRates ? DrawByRarity() : DrawByEntryRate(null);
        if (selected == null) selected = DrawByEntryRate(null);
        if (selected == null) return null;

        if (!RandomizeNumberAndColor) return selected;
        return new CardPackEntry
        {
            Card = selected.Card,
            Number = UnityEngine.Random.Range(1, 7),
            Color = (CardColor)UnityEngine.Random.Range(0, 5),
            InclusionRate = selected.InclusionRate
        };
    }

    public CardPackEntry DrawRandomCard(CardRarity rarity)
    {
        if (IncludeCards == null || IncludeCards.Count == 0) return null;
        CardPackEntry selected = DrawByEntryRate(rarity);
        if (selected == null) return null;
        if (!RandomizeNumberAndColor) return selected;
        return new CardPackEntry
        {
            Card = selected.Card,
            Number = UnityEngine.Random.Range(1, 7),
            Color = (CardColor)UnityEngine.Random.Range(0, 5),
            InclusionRate = selected.InclusionRate
        };
    }

    public CardPackEntry DrawRandomCard(CardTag tag)
    {
        if (IncludeCards == null || IncludeCards.Count == 0) return null;
        CardPackEntry selected = DrawByTagRate(tag);
        if (selected == null) return null;
        if (!RandomizeNumberAndColor) return selected;
        return new CardPackEntry
        {
            Card = selected.Card,
            Number = UnityEngine.Random.Range(1, 7),
            Color = (CardColor)UnityEngine.Random.Range(0, 5),
            InclusionRate = selected.InclusionRate
        };
    }

    private CardPackEntry DrawByTagRate(CardTag tag)
    {
        float totalRate = 0f;
        for (int i = 0; i < IncludeCards.Count; i++)
        {
            CardPackEntry entry = IncludeCards[i];
            if (entry != null && entry.Card != null && entry.InclusionRate > 0f
                && entry.Card.HasTag(tag))
                totalRate += entry.InclusionRate;
        }
        if (totalRate <= 0f) return null;

        float roll = UnityEngine.Random.value * totalRate;
        float accumulated = 0f;
        for (int i = 0; i < IncludeCards.Count; i++)
        {
            CardPackEntry entry = IncludeCards[i];
            if (entry == null || entry.Card == null || entry.InclusionRate <= 0f
                || !entry.Card.HasTag(tag)) continue;
            accumulated += entry.InclusionRate;
            if (roll <= accumulated) return entry;
        }
        return null;
    }

    private CardPackEntry DrawByRarity()
    {
        float common = HasCards(CardRarity.Common) ? CommonRate : 0f;
        float uncommon = HasCards(CardRarity.Uncommon) ? UncommonRate : 0f;
        float rare = HasCards(CardRarity.Rare) ? RareRate : 0f;
        float epic = HasCards(CardRarity.Epic) ? EpicRate : 0f;
        float legendary = HasCards(CardRarity.Legendary) ? LegendaryRate : 0f;
        float total = common + uncommon + rare + epic + legendary;
        if (total <= 0f) return null;

        float roll = UnityEngine.Random.value * total;
        CardRarity rarity;
        if (roll < common) rarity = CardRarity.Common;
        else if (roll < common + uncommon) rarity = CardRarity.Uncommon;
        else if (roll < common + uncommon + rare) rarity = CardRarity.Rare;
        else if (roll < common + uncommon + rare + epic) rarity = CardRarity.Epic;
        else rarity = CardRarity.Legendary;
        return DrawByEntryRate(rarity);
    }

    private bool HasCards(CardRarity rarity)
    {
        for (int i = 0; i < IncludeCards.Count; i++)
        {
            CardPackEntry entry = IncludeCards[i];
            if (entry != null && entry.Card != null && entry.Card.Rare == rarity && entry.InclusionRate > 0f)
                return true;
        }
        return false;
    }

    private CardPackEntry DrawByEntryRate(CardRarity? rarity)
    {
        float totalRate = 0f;
        for (int i = 0; i < IncludeCards.Count; i++)
        {
            CardPackEntry entry = IncludeCards[i];
            if (entry != null && entry.Card != null && entry.InclusionRate > 0f &&
                (!rarity.HasValue || entry.Card.Rare == rarity.Value))
                totalRate += entry.InclusionRate;
        }
        if (totalRate <= 0f) return null;

        float roll = UnityEngine.Random.value * totalRate;
        float accumulated = 0f;
        for (int i = 0; i < IncludeCards.Count; i++)
        {
            CardPackEntry entry = IncludeCards[i];
            if (entry == null || entry.Card == null || entry.InclusionRate <= 0f ||
                (rarity.HasValue && entry.Card.Rare != rarity.Value)) continue;
            accumulated += entry.InclusionRate;
            if (roll <= accumulated) return entry;
        }
        return null;
    }

    private void OnValidate()
    {
        CardsPerPack = Mathf.Max(1, CardsPerPack);
        CommonRate = Mathf.Clamp(CommonRate, 0f, 100f);
        UncommonRate = Mathf.Clamp(UncommonRate, 0f, 100f);
        RareRate = Mathf.Clamp(RareRate, 0f, 100f);
        EpicRate = Mathf.Clamp(EpicRate, 0f, 100f);
        LegendaryRate = Mathf.Clamp(LegendaryRate, 0f, 100f);
        if (IncludeCards == null) return;
        for (int i = 0; i < IncludeCards.Count; i++)
        {
            CardPackEntry entry = IncludeCards[i];
            if (entry == null) continue;
            entry.Number = Mathf.Clamp(entry.Number, 1, 6);
            entry.InclusionRate = Mathf.Clamp(entry.InclusionRate, 0f, 100f);
        }
    }
}