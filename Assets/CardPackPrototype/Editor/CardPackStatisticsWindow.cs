#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class CardPackStatisticsWindow : EditorWindow
{
    private const float SimilarityWarningThreshold = 0.70f;

    private sealed class PackSummary
    {
        public CardPackData Pack;
        public readonly int[] Counts = new int[Enum.GetValues(typeof(CardRarity)).Length];
        public readonly float[] WeightSums = new float[Enum.GetValues(typeof(CardRarity)).Length];
        public readonly HashSet<CardData> IncludedCards = new HashSet<CardData>();
        public readonly List<PackSimilarity> SimilarPacks = new List<PackSimilarity>();
        public int EntryCount;
        public int ValidCount;
        public int EmptyCount;
        public int DuplicateCount;
    }

    private sealed class PackSimilarity
    {
        public PackSummary Left;
        public PackSummary Right;
        public int IntersectionCount;
        public int LeftCount;
        public int RightCount;
        public float Ratio;

        public PackSummary GetOther(PackSummary summary)
        {
            return ReferenceEquals(summary, Left) ? Right : Left;
        }
    }

    private sealed class CardSummary
    {
        public CardData Card;
        public readonly List<CardPackData> Packs = new List<CardPackData>();
    }

    private readonly List<PackSummary> packSummaries = new List<PackSummary>();
    private readonly List<PackSimilarity> similarityWarnings = new List<PackSimilarity>();
    private readonly List<CardSummary> cardSummaries = new List<CardSummary>();
    private Vector2 scrollPosition;
    private string searchText = string.Empty;
    private bool showPackSummary = true;
    private bool showCardSummary = true;
    private bool onlyShowProblems;
    private bool refreshQueued;

    [MenuItem("CardOpen/카드팩 자동 집계")]
    private static void OpenWindow()
    {
        CardPackStatisticsWindow window = GetWindow<CardPackStatisticsWindow>();
        window.titleContent = new GUIContent("카드팩 집계");
        window.minSize = new Vector2(760f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.projectChanged += QueueRefresh;
        Undo.undoRedoPerformed += QueueRefresh;
        Refresh();
    }

    private void OnDisable()
    {
        EditorApplication.projectChanged -= QueueRefresh;
        Undo.undoRedoPerformed -= QueueRefresh;
        EditorApplication.delayCall -= DelayedRefresh;
    }

    private void QueueRefresh()
    {
        if (refreshQueued) return;
        refreshQueued = true;
        EditorApplication.delayCall += DelayedRefresh;
    }

    private void DelayedRefresh()
    {
        EditorApplication.delayCall -= DelayedRefresh;
        refreshQueued = false;
        if (this == null) return;
        Refresh();
        Repaint();
    }

    private void Refresh()
    {
        packSummaries.Clear();
        similarityWarnings.Clear();
        cardSummaries.Clear();

        List<CardPackData> packs = FindAssets<CardPackData>();
        List<CardData> cards = FindAssets<CardData>();
        Dictionary<CardData, CardSummary> cardLookup = new Dictionary<CardData, CardSummary>();
        for (int i = 0; i < cards.Count; i++)
        {
            CardSummary summary = new CardSummary { Card = cards[i] };
            cardSummaries.Add(summary);
            cardLookup.Add(cards[i], summary);
        }

        for (int packIndex = 0; packIndex < packs.Count; packIndex++)
        {
            CardPackData pack = packs[packIndex];
            PackSummary summary = new PackSummary { Pack = pack };
            List<CardPackEntry> entries = pack.IncludeCards;
            summary.EntryCount = entries != null ? entries.Count : 0;

            if (entries != null)
            {
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    CardPackEntry entry = entries[entryIndex];
                    if (entry == null || entry.Card == null)
                    {
                        summary.EmptyCount++;
                        continue;
                    }

                    if (!summary.IncludedCards.Add(entry.Card))
                    {
                        summary.DuplicateCount++;
                        continue;
                    }

                    summary.ValidCount++;
                    int rarityIndex = (int)entry.Card.Rare;
                    if (rarityIndex >= 0 && rarityIndex < summary.Counts.Length)
                    {
                        summary.Counts[rarityIndex]++;
                        summary.WeightSums[rarityIndex] += entry.InclusionRate;
                    }

                    if (!cardLookup.TryGetValue(entry.Card, out CardSummary cardSummary))
                    {
                        cardSummary = new CardSummary { Card = entry.Card };
                        cardSummaries.Add(cardSummary);
                        cardLookup.Add(entry.Card, cardSummary);
                    }
                    cardSummary.Packs.Add(pack);
                }
            }
            packSummaries.Add(summary);
        }

        CalculatePackSimilarities();
        packSummaries.Sort((left, right) => CompareDisplayName(left.Pack, right.Pack));
        cardSummaries.Sort((left, right) =>
        {
            int count = right.Packs.Count.CompareTo(left.Packs.Count);
            return count != 0 ? count : CompareDisplayName(left.Card, right.Card);
        });
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawOverallSummary();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        DrawSimilarityWarnings();
        EditorGUILayout.Space(10f);
        DrawPackSection();
        EditorGUILayout.Space(10f);
        DrawCardSection();
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(72f))) Refresh();
            GUILayout.Space(6f);
            GUILayout.Label("검색", GUILayout.Width(30f));
            string nextSearch = GUILayout.TextField(searchText, GUI.skin.FindStyle("ToolbarSearchTextField"), GUILayout.MinWidth(140f));
            if (!string.Equals(nextSearch, searchText, StringComparison.Ordinal)) searchText = nextSearch;
            GUILayout.FlexibleSpace();
            onlyShowProblems = GUILayout.Toggle(onlyShowProblems, "문제만 보기", EditorStyles.toolbarButton, GUILayout.Width(88f));
        }
    }

    private void DrawOverallSummary()
    {
        int problemPacks = 0;
        for (int i = 0; i < packSummaries.Count; i++)
        {
            if (HasProblem(packSummaries[i])) problemPacks++;
        }

        int includedCards = 0;
        for (int i = 0; i < cardSummaries.Count; i++)
        {
            if (cardSummaries[i].Packs.Count > 0) includedCards++;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox(
            "카드팩 " + packSummaries.Count + "개  |  카드 에셋 " + cardSummaries.Count +
            "개  |  봉입된 카드 " + includedCards + "개  |  미봉입 카드 " + (cardSummaries.Count - includedCards) +
            "개  |  문제가 있는 팩 " + problemPacks + "개  |  유사도 경고 " + similarityWarnings.Count + "쌍",
            problemPacks > 0 ? MessageType.Warning : MessageType.Info);
    }

    private void DrawSimilarityWarnings()
    {
        if (similarityWarnings.Count == 0) return;

        EditorGUILayout.LabelField("팩 유사도 70% 이상", EditorStyles.boldLabel);
        for (int i = 0; i < similarityWarnings.Count; i++)
        {
            PackSimilarity similarity = similarityWarnings[i];
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button(GetDisplayName(similarity.Left.Pack), EditorStyles.linkLabel, GUILayout.MinWidth(130f)))
                    SelectAsset(similarity.Left.Pack);
                GUILayout.Label("↔", GUILayout.Width(18f));
                if (GUILayout.Button(GetDisplayName(similarity.Right.Pack), EditorStyles.linkLabel, GUILayout.MinWidth(130f)))
                    SelectAsset(similarity.Right.Pack);
                GUILayout.Label(
                    (similarity.Ratio * 100f).ToString("0.#") + "%  (공통 " + similarity.IntersectionCount +
                    "장 / " + similarity.LeftCount + "장·" + similarity.RightCount + "장)",
                    GUILayout.MinWidth(220f));
                GUILayout.FlexibleSpace();
            }
        }
    }

    private void DrawPackSection()
    {
        showPackSummary = EditorGUILayout.Foldout(showPackSummary, "팩별 집계", true, EditorStyles.foldoutHeader);
        if (!showPackSummary) return;

        DrawPackHeader();
        bool drewAny = false;
        for (int i = 0; i < packSummaries.Count; i++)
        {
            PackSummary summary = packSummaries[i];
            if (!MatchesSearch(GetDisplayName(summary.Pack), summary.Pack.name)) continue;
            if (onlyShowProblems && !HasProblem(summary)) continue;
            DrawPackRow(summary, i % 2 == 0);
            drewAny = true;
        }
        if (!drewAny) EditorGUILayout.HelpBox("조건에 맞는 카드팩이 없습니다.", MessageType.None);
    }

    private static void DrawPackHeader()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label("카드팩", EditorStyles.boldLabel, GUILayout.MinWidth(155f));
            GUILayout.Label("전체", EditorStyles.boldLabel, GUILayout.Width(42f));
            DrawRarityHeader("일반");
            DrawRarityHeader("고급");
            DrawRarityHeader("희귀");
            DrawRarityHeader("영웅");
            DrawRarityHeader("전설");
            GUILayout.Label("상태", EditorStyles.boldLabel, GUILayout.Width(110f));
        }
    }

    private static void DrawRarityHeader(string label)
    {
        GUILayout.Label(label + " (장/%)", EditorStyles.boldLabel, GUILayout.Width(78f));
    }

    private static void DrawPackRow(PackSummary summary, bool alternate)
    {
        GUIStyle rowStyle = alternate ? new GUIStyle(EditorStyles.helpBox) : GUIStyle.none;
        using (new EditorGUILayout.HorizontalScope(rowStyle))
        {
            if (GUILayout.Button(GetDisplayName(summary.Pack), EditorStyles.linkLabel, GUILayout.MinWidth(155f)))
                SelectAsset(summary.Pack);
            GUILayout.Label(summary.ValidCount.ToString(), GUILayout.Width(42f));
            for (int rarityIndex = 0; rarityIndex < summary.Counts.Length; rarityIndex++)
                GUILayout.Label(summary.Counts[rarityIndex] + " / " + summary.WeightSums[rarityIndex].ToString("0.##"), GUILayout.Width(78f));

            string status = GetProblemText(summary);
            Color previous = GUI.color;
            if (!string.Equals(status, "정상", StringComparison.Ordinal)) GUI.color = new Color(1f, 0.65f, 0.45f);
            GUILayout.Label(status, GUILayout.Width(110f));
            GUI.color = previous;
        }
    }

    private void DrawCardSection()
    {
        showCardSummary = EditorGUILayout.Foldout(showCardSummary, "카드별 등장 팩", true, EditorStyles.foldoutHeader);
        if (!showCardSummary) return;

        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label("카드", EditorStyles.boldLabel, GUILayout.MinWidth(180f));
            GUILayout.Label("등급", EditorStyles.boldLabel, GUILayout.Width(54f));
            GUILayout.Label("팩 수", EditorStyles.boldLabel, GUILayout.Width(42f));
            GUILayout.Label("등장 카드팩", EditorStyles.boldLabel, GUILayout.MinWidth(320f));
        }

        bool drewAny = false;
        int visibleIndex = 0;
        for (int i = 0; i < cardSummaries.Count; i++)
        {
            CardSummary summary = cardSummaries[i];
            string cardName = GetDisplayName(summary.Card);
            string packNames = string.Join(", ", summary.Packs.Select(GetDisplayName).ToArray());
            if (!MatchesSearch(cardName, summary.Card.name, packNames)) continue;
            if (onlyShowProblems && summary.Packs.Count > 0) continue;
            DrawCardRow(summary, packNames, visibleIndex++ % 2 == 0);
            drewAny = true;
        }
        if (!drewAny) EditorGUILayout.HelpBox("조건에 맞는 카드가 없습니다.", MessageType.None);
    }

    private static void DrawCardRow(CardSummary summary, string packNames, bool alternate)
    {
        GUIStyle rowStyle = alternate ? new GUIStyle(EditorStyles.helpBox) : GUIStyle.none;
        using (new EditorGUILayout.HorizontalScope(rowStyle))
        {
            if (GUILayout.Button(GetDisplayName(summary.Card), EditorStyles.linkLabel, GUILayout.MinWidth(180f)))
                SelectAsset(summary.Card);
            GUILayout.Label(GetRarityName(summary.Card.Rare), GUILayout.Width(54f));
            GUILayout.Label(summary.Packs.Count.ToString(), GUILayout.Width(42f));
            Color previous = GUI.color;
            if (summary.Packs.Count == 0) GUI.color = new Color(1f, 0.65f, 0.45f);
            GUILayout.Label(summary.Packs.Count > 0 ? packNames : "미봉입", GUILayout.MinWidth(320f));
            GUI.color = previous;
        }
    }

    private static bool HasProblem(PackSummary summary)
    {
        if (summary.EmptyCount > 0 || summary.DuplicateCount > 0 || summary.SimilarPacks.Count > 0) return true;
        if (!summary.Pack.UseRarityRates) return false;
        for (int rarityIndex = 0; rarityIndex < summary.Counts.Length; rarityIndex++)
        {
            CardRarity rarity = (CardRarity)rarityIndex;
            float target = summary.Pack.GetRarityRate(rarity);
            if (summary.Counts[rarityIndex] > 0 && !Mathf.Approximately(summary.WeightSums[rarityIndex], target)) return true;
            if (summary.Counts[rarityIndex] == 0 && target > 0f) return true;
        }
        return false;
    }

    private static string GetProblemText(PackSummary summary)
    {
        List<string> issues = new List<string>();
        if (summary.EmptyCount > 0) issues.Add("빈 참조 " + summary.EmptyCount);
        if (summary.DuplicateCount > 0) issues.Add("중복 " + summary.DuplicateCount);
        if (summary.SimilarPacks.Count > 0)
        {
            PackSimilarity highest = summary.SimilarPacks[0];
            PackSummary other = highest.GetOther(summary);
            issues.Add(GetDisplayName(other.Pack) + " " + (highest.Ratio * 100f).ToString("0.#") + "%");
        }
        if (summary.Pack.UseRarityRates)
        {
            for (int rarityIndex = 0; rarityIndex < summary.Counts.Length; rarityIndex++)
            {
                CardRarity rarity = (CardRarity)rarityIndex;
                float target = summary.Pack.GetRarityRate(rarity);
                bool mismatch = summary.Counts[rarityIndex] > 0
                    ? !Mathf.Approximately(summary.WeightSums[rarityIndex], target)
                    : target > 0f;
                if (mismatch)
                {
                    issues.Add(GetRarityName(rarity) + " 확률");
                    break;
                }
            }
        }
        return issues.Count > 0 ? string.Join(", ", issues.ToArray()) : "정상";
    }

    private void CalculatePackSimilarities()
    {
        for (int leftIndex = 0; leftIndex < packSummaries.Count; leftIndex++)
        {
            PackSummary left = packSummaries[leftIndex];
            for (int rightIndex = leftIndex + 1; rightIndex < packSummaries.Count; rightIndex++)
            {
                PackSummary right = packSummaries[rightIndex];
                int intersection = 0;
                foreach (CardData card in left.IncludedCards)
                {
                    if (right.IncludedCards.Contains(card)) intersection++;
                }

                int totalCardCount = left.IncludedCards.Count + right.IncludedCards.Count;
                float ratio = totalCardCount > 0 ? (2f * intersection) / totalCardCount : 0f;
                if (ratio < SimilarityWarningThreshold) continue;

                PackSimilarity similarity = new PackSimilarity
                {
                    Left = left,
                    Right = right,
                    IntersectionCount = intersection,
                    LeftCount = left.IncludedCards.Count,
                    RightCount = right.IncludedCards.Count,
                    Ratio = ratio
                };
                similarityWarnings.Add(similarity);
                left.SimilarPacks.Add(similarity);
                right.SimilarPacks.Add(similarity);
            }
        }

        similarityWarnings.Sort((left, right) => right.Ratio.CompareTo(left.Ratio));
        for (int i = 0; i < packSummaries.Count; i++)
            packSummaries[i].SimilarPacks.Sort((left, right) => right.Ratio.CompareTo(left.Ratio));
    }

    private bool MatchesSearch(params string[] values)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return true;
        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrEmpty(values[i]) && values[i].IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static List<T> FindAssets<T>() where T : UnityEngine.Object
    {
        string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        List<T> assets = new List<T>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (asset != null) assets.Add(asset);
        }
        return assets;
    }

    private static int CompareDisplayName(UnityEngine.Object left, UnityEngine.Object right)
    {
        return string.Compare(GetDisplayName(left), GetDisplayName(right), StringComparison.CurrentCulture);
    }

    private static string GetDisplayName(UnityEngine.Object asset)
    {
        CardPackData pack = asset as CardPackData;
        if (pack != null && !string.IsNullOrWhiteSpace(pack.Name)) return pack.Name;
        CardData card = asset as CardData;
        if (card != null && !string.IsNullOrWhiteSpace(card.Name)) return card.Name;
        return asset != null ? asset.name : "없음";
    }

    private static string GetRarityName(CardRarity rarity)
    {
        switch (rarity)
        {
            case CardRarity.Uncommon: return "고급";
            case CardRarity.Rare: return "희귀";
            case CardRarity.Epic: return "영웅";
            case CardRarity.Legendary: return "전설";
            default: return "일반";
        }
    }

    private static void SelectAsset(UnityEngine.Object asset)
    {
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }
}
#endif
