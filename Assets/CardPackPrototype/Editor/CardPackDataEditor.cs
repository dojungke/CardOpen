#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(CardPackData)), CanEditMultipleObjects]
public sealed class CardPackDataEditor : Editor
{
    private SerializedProperty packName;
    private SerializedProperty frontImage;
    private SerializedProperty backImage;
    private SerializedProperty cardsPerPack;
    private SerializedProperty useRarityRates;
    private SerializedProperty commonRate;
    private SerializedProperty uncommonRate;
    private SerializedProperty rareRate;
    private SerializedProperty epicRate;
    private SerializedProperty legendaryRate;
    private SerializedProperty randomizeNumberAndColor;
    private SerializedProperty includeCards;
    private ReorderableList cardList;
    private CardData cardToAdd;

    private void OnEnable()
    {
        packName = serializedObject.FindProperty("Name");
        frontImage = serializedObject.FindProperty("FrontImage");
        backImage = serializedObject.FindProperty("BackImage");
        cardsPerPack = serializedObject.FindProperty("CardsPerPack");
        useRarityRates = serializedObject.FindProperty("UseRarityRates");
        commonRate = serializedObject.FindProperty("CommonRate");
        uncommonRate = serializedObject.FindProperty("UncommonRate");
        rareRate = serializedObject.FindProperty("RareRate");
        epicRate = serializedObject.FindProperty("EpicRate");
        legendaryRate = serializedObject.FindProperty("LegendaryRate");
        randomizeNumberAndColor = serializedObject.FindProperty("RandomizeNumberAndColor");
        includeCards = serializedObject.FindProperty("IncludeCards");
        cardList = new ReorderableList(serializedObject, includeCards, true, true, true, true)
        {
            elementHeight = EditorGUIUtility.singleLineHeight + 5f,
            drawHeaderCallback = DrawListHeader,
            drawElementCallback = DrawListElement,
            onAddCallback = AddEmptyEntry
        };
    }

    public override void OnInspectorGUI()
    {
        if (serializedObject.isEditingMultipleObjects)
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Apply Rarity Rates To All Selected Packs"))
                ApplyToSelectedPacks("Apply Card Pack Rarity Rates", pack => pack.ApplyRarityRatesToEntries());
            return;
        }

        serializedObject.Update();
        EditorGUILayout.LabelField("Pack Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(packName);
        EditorGUILayout.PropertyField(frontImage);
        EditorGUILayout.PropertyField(backImage);
        EditorGUILayout.PropertyField(cardsPerPack);
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Draw Rules", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(useRarityRates);
        if (useRarityRates.boolValue)
        {
            EditorGUILayout.PropertyField(commonRate);
            EditorGUILayout.PropertyField(uncommonRate);
            EditorGUILayout.PropertyField(rareRate);
            EditorGUILayout.PropertyField(epicRate);
            EditorGUILayout.PropertyField(legendaryRate);
            float total = commonRate.floatValue + uncommonRate.floatValue + rareRate.floatValue
                + epicRate.floatValue + legendaryRate.floatValue;
            if (!Mathf.Approximately(total, 100f))
                EditorGUILayout.HelpBox("Rarity rate total: " + total.ToString("0.##") + "%", MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Default 60 / 25 / 10 / 5 / 0")) SetDefaultRarityRates();
                if (GUILayout.Button("Normalize To 100%")) NormalizeRarityRates();
            }
        }
        EditorGUILayout.PropertyField(randomizeNumberAndColor);
        serializedObject.ApplyModifiedProperties();
        DrawCardTools();
        serializedObject.Update();
        DrawCardSummary();
        cardList.DoLayoutList();
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawCardTools()
    {
        CardPackData pack = (CardPackData)target;
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Card List Tools", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            cardToAdd = (CardData)EditorGUILayout.ObjectField(cardToAdd, typeof(CardData), false);
            using (new EditorGUI.DisabledScope(cardToAdd == null))
            {
                if (GUILayout.Button("Add", GUILayout.Width(64f)))
                {
                    AddCards(pack, new[] { cardToAdd });
                    cardToAdd = null;
                }
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Selected Cards")) AddSelectedCards(pack);
            if (GUILayout.Button("Add All Cards (Manual)")) AddAllCards(pack);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clean Missing / Duplicates")) CleanEntries(pack);
            if (GUILayout.Button("Sort By Rarity / Name")) SortEntries(pack);
        }
        if (GUILayout.Button("Apply Rarity Rates To Entry Weights"))
            MutatePack(pack, "Apply Card Pack Rarity Rates", pack.ApplyRarityRatesToEntries);
        EditorGUILayout.HelpBox(
            "New card assets are never added automatically. Use Add Selected Cards or Add All Cards only when you want them in this pack.",
            MessageType.Info);
    }

    private void DrawCardSummary()
    {
        CardPackData pack = (CardPackData)target;
        int common = 0;
        int uncommon = 0;
        int rare = 0;
        int epic = 0;
        int legendary = 0;
        if (pack.IncludeCards != null)
        {
            for (int i = 0; i < pack.IncludeCards.Count; i++)
            {
                CardPackEntry entry = pack.IncludeCards[i];
                if (entry == null || entry.Card == null) continue;
                switch (entry.Card.Rare)
                {
                    case CardRarity.Uncommon: uncommon++; break;
                    case CardRarity.Rare: rare++; break;
                    case CardRarity.Epic: epic++; break;
                    case CardRarity.Legendary: legendary++; break;
                    default: common++; break;
                }
            }
        }
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(
            "Included Cards  |  Total " + (common + uncommon + rare + epic + legendary) +
            "   Common " + common + "   Uncommon " + uncommon + "   Rare " + rare + "   Epic " + epic + "   Legendary " + legendary,
            EditorStyles.boldLabel);
    }

    private void DrawListHeader(Rect rect)
    {
        GetColumnRects(rect, out Rect cardRect, out Rect numberRect, out Rect colorRect, out Rect rateRect);
        EditorGUI.LabelField(cardRect, "Card");
        EditorGUI.LabelField(numberRect, "Number");
        EditorGUI.LabelField(colorRect, "Color");
        EditorGUI.LabelField(rateRect, "Weight");
    }

    private void DrawListElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        SerializedProperty entry = includeCards.GetArrayElementAtIndex(index);
        SerializedProperty card = entry.FindPropertyRelative("Card");
        SerializedProperty number = entry.FindPropertyRelative("Number");
        SerializedProperty color = entry.FindPropertyRelative("Color");
        SerializedProperty rate = entry.FindPropertyRelative("InclusionRate");
        rect.y += 2f;
        rect.height = EditorGUIUtility.singleLineHeight;
        GetColumnRects(rect, out Rect cardRect, out Rect numberRect, out Rect colorRect, out Rect rateRect);
        EditorGUI.PropertyField(cardRect, card, GUIContent.none);
        using (new EditorGUI.DisabledScope(randomizeNumberAndColor.boolValue))
        {
            EditorGUI.PropertyField(numberRect, number, GUIContent.none);
            EditorGUI.PropertyField(colorRect, color, GUIContent.none);
        }
        EditorGUI.PropertyField(rateRect, rate, GUIContent.none);
    }

    private static void GetColumnRects(Rect rect, out Rect card, out Rect number, out Rect color, out Rect rate)
    {
        const float gap = 4f;
        float usable = rect.width - gap * 3f;
        card = new Rect(rect.x, rect.y, usable * 0.47f, rect.height);
        number = new Rect(card.xMax + gap, rect.y, usable * 0.13f, rect.height);
        color = new Rect(number.xMax + gap, rect.y, usable * 0.20f, rect.height);
        rate = new Rect(color.xMax + gap, rect.y, usable * 0.20f, rect.height);
    }

    private void AddEmptyEntry(ReorderableList list)
    {
        int index = includeCards.arraySize;
        includeCards.InsertArrayElementAtIndex(index);
        SerializedProperty entry = includeCards.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("Card").objectReferenceValue = null;
        entry.FindPropertyRelative("Number").intValue = 1;
        entry.FindPropertyRelative("Color").enumValueIndex = (int)CardColor.Green;
        entry.FindPropertyRelative("InclusionRate").floatValue = 100f;
        serializedObject.ApplyModifiedProperties();
        list.index = index;
    }

    private void AddSelectedCards(CardPackData pack)
    {
        List<CardData> selectedCards = new List<CardData>();
        for (int i = 0; i < Selection.objects.Length; i++)
        {
            CardData card = Selection.objects[i] as CardData;
            if (card != null) selectedCards.Add(card);
        }
        AddCards(pack, selectedCards);
    }

    private void AddAllCards(CardPackData pack)
    {
        string[] guids = AssetDatabase.FindAssets("t:CardData", new[] { "Assets/CardPackPrototype/Resources/Cards" });
        List<CardData> cards = new List<CardData>();
        for (int i = 0; i < guids.Length; i++)
        {
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (card != null) cards.Add(card);
        }
        cards.Sort(CompareCards);
        AddCards(pack, cards);
    }

    private void AddCards(CardPackData pack, IEnumerable<CardData> cards)
    {
        MutatePack(pack, "Add Cards To Pack", () =>
        {
            if (pack.IncludeCards == null) pack.IncludeCards = new List<CardPackEntry>();
            HashSet<CardData> included = new HashSet<CardData>();
            for (int i = 0; i < pack.IncludeCards.Count; i++)
            {
                CardPackEntry entry = pack.IncludeCards[i];
                if (entry != null && entry.Card != null) included.Add(entry.Card);
            }
            foreach (CardData card in cards)
            {
                if (card == null || !included.Add(card)) continue;
                pack.IncludeCards.Add(new CardPackEntry
                {
                    Card = card,
                    Number = 1,
                    Color = CardColor.Green,
                    InclusionRate = 100f
                });
            }
        });
    }

    private void CleanEntries(CardPackData pack)
    {
        MutatePack(pack, "Clean Card Pack Entries", () =>
        {
            if (pack.IncludeCards == null) return;
            HashSet<CardData> included = new HashSet<CardData>();
            for (int i = 0; i < pack.IncludeCards.Count; i++)
            {
                CardPackEntry entry = pack.IncludeCards[i];
                if (entry == null || entry.Card == null || !included.Add(entry.Card))
                    pack.IncludeCards.RemoveAt(i--);
            }
        });
    }

    private void SortEntries(CardPackData pack)
    {
        MutatePack(pack, "Sort Card Pack Entries", () =>
        {
            if (pack.IncludeCards == null) return;
            pack.IncludeCards.Sort((left, right) =>
            {
                if (ReferenceEquals(left, right)) return 0;
                if (left == null || left.Card == null) return 1;
                if (right == null || right.Card == null) return -1;
                return CompareCards(left.Card, right.Card);
            });
        });
    }

    private static int CompareCards(CardData left, CardData right)
    {
        int rarity = left.Rare.CompareTo(right.Rare);
        return rarity != 0 ? rarity : string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
    }

    private void SetDefaultRarityRates()
    {
        commonRate.floatValue = 60f;
        uncommonRate.floatValue = 25f;
        rareRate.floatValue = 10f;
        epicRate.floatValue = 5f;
        legendaryRate.floatValue = 0f;
        serializedObject.ApplyModifiedProperties();
    }

    private void NormalizeRarityRates()
    {
        float total = commonRate.floatValue + uncommonRate.floatValue + rareRate.floatValue
            + epicRate.floatValue + legendaryRate.floatValue;
        if (total <= 0f)
        {
            SetDefaultRarityRates();
            return;
        }
        float scale = 100f / total;
        commonRate.floatValue *= scale;
        uncommonRate.floatValue *= scale;
        rareRate.floatValue *= scale;
        epicRate.floatValue *= scale;
        legendaryRate.floatValue *= scale;
        serializedObject.ApplyModifiedProperties();
    }

    private void MutatePack(CardPackData pack, string undoName, Action action)
    {
        serializedObject.ApplyModifiedProperties();
        Undo.RecordObject(pack, undoName);
        action();
        EditorUtility.SetDirty(pack);
        serializedObject.Update();
    }

    private static void ApplyToSelectedPacks(string undoName, Action<CardPackData> action)
    {
        UnityEngine.Object[] selected = Selection.objects;
        Undo.RecordObjects(selected, undoName);
        for (int i = 0; i < selected.Length; i++)
        {
            CardPackData pack = selected[i] as CardPackData;
            if (pack == null) continue;
            action(pack);
            EditorUtility.SetDirty(pack);
        }
    }
}
#endif