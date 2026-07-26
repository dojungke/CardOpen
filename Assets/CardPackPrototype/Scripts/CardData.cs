using System;
using System.Collections.Generic;
using UnityEngine;

public enum CardRarity
{
    Common,
    Uncommon,
    Rare,
    Epic
}

public enum CardColor
{
    Green,
    Blue,
    Red,
    Black,
    White
}

public enum DeckAbilityTrigger
{
    None,
    MatchingColor,
    OddNumber,
    EvenNumber,
    DifferentColor,
    MatchingNumber,
    EveryCard,
    MatchingColorOrRed,
    PreviousCardDifferentColor,
    NumberAtLeastFour,
    NumberAtMostThree,
    TriggeredEffectsAtLeastThree,
    RedCard,
    NumberAtMostTwo,
    IncludedNumbers,
    IncludedColors
}

public enum DeckAbilityEffect
{
    AddScore,
    AddTriggeredScorePercent,
    AddRevealedNumberTimesScore,
    AddNextPackCards,
    GrantHologramChance,
    IncreaseScoreBonusEfficiency,
    AccumulateScoreBonusPerDraw
}

[Serializable]
public sealed class CardDeckAbility
{
    public DeckAbilityTrigger Trigger;
    public DeckAbilityEffect Effect;
    [Min(0)] public int Score;
    [Range(0f, 500f)] public float PercentBonus;
    [Min(0)] public int NumberMultiplier;
    [Min(0)] public int PackCardCount;
    [Range(0f, 100f)] public float ChancePercent;
    [Tooltip("이 능력이 적용되는 카드 숫자 목록")]
    public List<int> ApplicableNumbers = new List<int>();
    [Tooltip("이 능력이 적용되는 카드 색상 목록")]
    public List<CardColor> ApplicableColors = new List<CardColor>();
    public bool ResetAccumulationAfterPack;
    [TextArea(1, 3)] public string Description;
}

[CreateAssetMenu(fileName = "Card", menuName = "CardOpen/Card")]
public class CardData : ScriptableObject
{
    public string Name;
    [TextArea(2, 5)] public string Description;
    public CardRarity Rare;
    public Texture2D Image;

    [Header("Deck Abilities")]
    public List<CardDeckAbility> DeckAbilities = new List<CardDeckAbility>();

    public string RarityAssetKey
    {
        get
        {
            switch (Rare)
            {
                case CardRarity.Uncommon: return "Rare";
                case CardRarity.Rare: return "Epic";
                case CardRarity.Epic: return "Legendary";
                default: return "Common";
            }
        }
    }
}