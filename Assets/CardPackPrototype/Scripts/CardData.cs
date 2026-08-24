using System;
using System.Collections.Generic;
using UnityEngine;

public enum CardRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public enum CardColor
{
    Green,
    Blue,
    Red,
    Black,
    White
}

public enum AbilityColor
{
    Green = 0,
    Blue = 1,
    Red = 2,
    Black = 3,
    White = 4,
    Self = 5
}



public enum CardTag
{
    [InspectorName("자연")]
    Nature = 0,
    [InspectorName("마법")]
    Magic = 2,
    [InspectorName("룬")]
    Rune = 3,
    [InspectorName("무기")]
    Weapon = 4,
    [InspectorName("마공학")]
    Magitech = 5,
    [InspectorName("전사")]
    Warrior = 6,
    [InspectorName("마법사")]
    Mage = 7,
    [InspectorName("스택")]
    Stack = 8,
    [InspectorName("광물")]
    Mineral = 9,
    [InspectorName("채굴")]
    Mining = 10
}

public enum DeckAbilityTrigger
{
    None = 0,
    DifferentColor = 4,
    MatchingNumber = 5,
    EveryCard = 6,
    PreviousCardDifferentColor = 8,
    TriggeredEffectsAtLeastThree = 11,
    IncludedNumbers = 14,
    IncludedColors = 15,
    MatchingColorAndNumber = 16,
    WhenDrawn = 17
}

public enum DeckAbilityEffect
{
    AddScore,
    AddTriggeredScorePercent,
    AddRevealedNumberTimesScore,
    AddNextPackCards,
    GrantHologramChance,
    IncreaseScoreBonusEfficiency,
    AccumulateScoreBonusPerDraw,
    TriggerPercentAtStackThreshold,
    AddScoreOnPackOpen,
    AddRandomCommonCardToPackEnd,
    TriggerScoreAtStackThreshold,
    AccumulateFlatScorePerDraw,
    AccumulatePercentAtStackThreshold,
    GrantTemporaryPercentForNextDraws,
    WhiteCardsCountAsAllColors,
    AddSpecificCardAtStackThreshold,
    AddRandomTaggedCardOnPackOpen,
    AccumulateScoreBonusEfficiencyByNumber,
    TransformAfterPacks,
    GrantHologramChanceToPacksAndCards,
    TriggerPercentEveryDrawCount,
    AddScoreEveryOtherCardScoreEvents,
    AddScorePerDecayingStack,
    AddScorePercentPerPackStack,
    TriggerScoreAndPercentAtStackThresholdEveryDraw,
    GrantStackToOtherStackCardsAtThreshold,
    EnchantRandomDeckCardHolographic,
    AddMinedMineralCardToPackEnd,
    AddMinedMineralCardOnPackOpen,
    AddMinedMineralCardAtDrawThreshold,
    ReplaceNextPackWithMinedMineralsWhenLeftmost
}

[Serializable]
public sealed class CardDeckAbility
{
    public DeckAbilityTrigger Trigger;
    public DeckAbilityEffect Effect;
    [Min(0)] public int Score;
    [Range(0f, 500f)] public float PercentBonus;
    [Min(0f)] public float MaximumPercent;
    [Min(0)] public int NumberMultiplier;
    [Min(0)] public int PackCardCount;
    [Range(0f, 100f)] public float ChancePercent;
    [Tooltip("이 능력이 적용되는 카드 숫자 목록")]
    public List<int> ApplicableNumbers = new List<int>();
    [Tooltip("이 능력이 적용되는 카드 색상 목록")]
    public List<AbilityColor> ApplicableColors = new List<AbilityColor>();
    public bool ResetAccumulationAfterPack;
    [Min(1)] public int StackThreshold = 1;
    [Min(0)] public int DurationDrawCount;
    public CardData GeneratedCard;
    public CardData TransformedCard;
    [Min(1)] public int PacksToTransform = 1;
    public CardTag GeneratedCardTag;
    [Min(0)] public int MaxTriggersPerPack;
    [Tooltip("다른 자연 카드의 능력으로는 이 능력을 연쇄 발동하지 않음")]
    [InspectorName("자연 연쇄 발동 제외")]
    public bool ExcludeFromNatureChain;

    public bool CanBeTriggeredByNatureChain()
    {
        return !ExcludeFromNatureChain && IsNatureChainEffectSupported(Effect);
    }

    public static bool IsNatureChainEffectSupported(DeckAbilityEffect effect)
    {
        return effect != DeckAbilityEffect.AddNextPackCards
            && effect != DeckAbilityEffect.AddScoreOnPackOpen
            && effect != DeckAbilityEffect.AddRandomCommonCardToPackEnd
            && effect != DeckAbilityEffect.AddSpecificCardAtStackThreshold
            && effect != DeckAbilityEffect.AddRandomTaggedCardOnPackOpen
            && effect != DeckAbilityEffect.IncreaseScoreBonusEfficiency
            && effect != DeckAbilityEffect.WhiteCardsCountAsAllColors
            && effect != DeckAbilityEffect.TransformAfterPacks
            && effect != DeckAbilityEffect.GrantHologramChance
            && effect != DeckAbilityEffect.GrantHologramChanceToPacksAndCards
            && effect != DeckAbilityEffect.EnchantRandomDeckCardHolographic
            && effect != DeckAbilityEffect.AddMinedMineralCardToPackEnd
            && effect != DeckAbilityEffect.AddMinedMineralCardOnPackOpen
            && effect != DeckAbilityEffect.AddMinedMineralCardAtDrawThreshold
            && effect != DeckAbilityEffect.ReplaceNextPackWithMinedMineralsWhenLeftmost;
    }
}

[CreateAssetMenu(fileName = "Card", menuName = "CardOpen/Card")]
public class CardData : ScriptableObject
{
    public string Name;
    [TextArea(2, 5)] public string Description;
    public string EnglishName;
    [TextArea(2, 5)] public string EnglishDescription;
    public CardRarity Rare;
    public Texture2D Image;
    [Tooltip("Opaque background artwork fills the illustration width while preserving aspect ratio.")]
    public bool FitBackgroundImageToWidth;

    [Header("Tags")]
    public List<CardTag> Tags = new List<CardTag>();

    [Header("Equipment Slots")]
    public bool CanEquipMagic;
    public bool CanEquipWeapon;

    [Header("Card Combining")]
    [Min(1)] public int MaxMergeCount = 1;
    public bool UnlimitedMergeCount;

    [Header("Deck Abilities")]
    public List<CardDeckAbility> DeckAbilities = new List<CardDeckAbility>();

    public bool HasTag(CardTag tag)
    {
        return Tags != null && Tags.Contains(tag);
    }

    public string GetLocalizedName(bool english)
    {
        return english && !string.IsNullOrWhiteSpace(EnglishName) ? EnglishName : Name;
    }

    public string GetLocalizedDescription(bool english)
    {
        return english && !string.IsNullOrWhiteSpace(EnglishDescription) ? EnglishDescription : Description;
    }

    public string RarityAssetKey
    {
        get
        {
            switch (Rare)
            {
                case CardRarity.Uncommon: return "Rare";
                case CardRarity.Rare: return "Epic";
                case CardRarity.Epic: return "Legendary";
                case CardRarity.Legendary: return "Legendary";
                default: return "Common";
            }
        }
    }
}