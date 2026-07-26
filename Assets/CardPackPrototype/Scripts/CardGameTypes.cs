using System;
using UnityEngine;

namespace CardOpen.Prototype
{
    public enum CardRarity
    {
        Common,
        Rare,
        Epic,
        Legendary
    }

    public enum CardAttribute
    {
        Green,
        Blue,
        Red,
        Black,
        White
    }

    public enum CardFamily
    {
        Flame,
        Tide,
        Grove,
        Astral
    }

    [Serializable]
    public struct CardData
    {
        public string Name;
        public CardRarity Rarity;
        public CardFamily Family;
        public CardAttribute Attribute;
        public int Cost;
        public int Points;

        public Color RarityColor
        {
            get
            {
                switch (Rarity)
                {
                    case CardRarity.Rare: return new Color(0.20f, 0.58f, 0.95f);
                    case CardRarity.Epic: return new Color(0.67f, 0.31f, 0.92f);
                    case CardRarity.Legendary: return new Color(1.00f, 0.63f, 0.12f);
                    default: return new Color(0.70f, 0.75f, 0.80f);
                }
            }
        }

        public Color FamilyColor
        {
            get
            {
                switch (Family)
                {
                    case CardFamily.Flame: return new Color(0.95f, 0.24f, 0.16f);
                    case CardFamily.Tide: return new Color(0.12f, 0.62f, 0.95f);
                    case CardFamily.Grove: return new Color(0.25f, 0.78f, 0.35f);
                    default: return new Color(0.66f, 0.37f, 0.95f);
                }
            }
        }
    }
}
