using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CardOpen.Prototype
{
    public sealed class PackOnlyPrototype : MonoBehaviour
    {
        private sealed class ScorePopup
        {
            public string Text;
            public Color Color;
            public float StartTime;
            public int Lane;
            public int Score;
            public float PlaybackSpeed = 1f;
            public float AudioVolumeScale = 1f;
            public bool AddedToPendingScore;
            public bool SoundPlayed;
        }
        private sealed class StoredCard
        {
            public string Name;
            public global::CardData Data;
            public global::CardRarity Rarity;
            public global::CardColor Color;
            public int Number;
            public bool IsHolographic;
            public bool IsStoredInDeck;
            public int DeckSlot = -1;
            public int CombinedCopies = 1;
            public int CombinedHolographicCopies;
            public StoredCard EquippedMagic;
            public StoredCard EquippedWeapon;
            public readonly List<StoredCard> InheritedRelics = new List<StoredCard>();
            public readonly Dictionary<int, float> AccumulatedPercentByAbility =
                new Dictionary<int, float>();
            public readonly Dictionary<int, int> AccumulatedFlatScoreByAbility =
                new Dictionary<int, int>();
            public readonly Dictionary<int, int> RemainingDrawsByAbility =
                new Dictionary<int, int>();
            public readonly Dictionary<int, int> StackByAbilityCopy = new Dictionary<int, int>();
            public readonly Dictionary<int, int> TriggeredStackCountsThisDraw = new Dictionary<int, int>();
            public readonly HashSet<int> UsedOncePerPackAbilityCopies = new HashSet<int>();
            public readonly Dictionary<int, int> PerPackTriggerCountByAbility =
                new Dictionary<int, int>();
            public readonly Dictionary<int, int> PacksElapsedByAbility =
                new Dictionary<int, int>();
        }
        [Serializable]
        private sealed class SharedIntValue
        {
            public int Key;
            public int Value;
        }

        [Serializable]
        private sealed class SharedFloatValue
        {
            public int Key;
            public float Value;
        }

        [Serializable]
        private sealed class SharedCardData
        {
            public string ResourceName;
            public int Color;
            public int Number;
            public int Rarity;
            public int DeckSlot;
            public int CombinedCopies;
            public int CombinedHolographicCopies;
            public bool IsHolographic;
            public SharedCardData EquippedMagic;
            public SharedCardData EquippedWeapon;
            public SharedCardData[] InheritedRelics;
            public SharedIntValue[] AccumulatedFlatScore;
            public SharedIntValue[] RemainingDraws;
            public SharedIntValue[] Stacks;
            public SharedIntValue[] PerPackTriggers;
            public SharedIntValue[] PacksElapsed;
            public SharedFloatValue[] AccumulatedPercent;
        }

        [Serializable]
        private sealed class SharedResultData
        {
            public int Version = 1;
            public int TotalScore;
            public int RoundScore;
            public int GoalIndex;
            public int CompletedPacks;
            public bool Cleared;
            public SharedCardData[] Deck;
        }
        private enum RevealPhase { PackChoice, Pack, CardBack, CardFront, Animating, GameOver, RunCleared }
        private const int FallbackCardsPerPack = 5;
        private const int PacksPerGoal = 3;
        private const int ScorePopupTrailCapacity = 5;
        private const float ReferenceWidth = 1280f;
        private const float ReferenceHeight = 720f;
        private const float PortraitWidth = 720f;
        private const float PortraitHeight = 1280f;
        private static readonly int[] GoalScores = { 3000, 10000, 20000, 30000, 50000 };
        private const float RevealedCardScale = 1.5f;
        private static readonly Rect PackTearZone = new Rect(410f, 0f, 460f, 380f);
        private static readonly Rect CardGestureZone = new Rect(500f, 105f, 340f, 505f);
        private static readonly Vector3 PackHome = new Vector3(0f, 0.5f, -0.65f);
        private static readonly Vector3 CardHome = new Vector3(0f, 1.15f, -0.24f);
        private static readonly Vector3 PackedCardOffset = new Vector3(0f, -0.55f, 0f);
        private readonly List<CardVisual> cards = new List<CardVisual>();
        private readonly List<StoredCard> currentPackCards = new List<StoredCard>();
        private readonly List<StoredCard> deckCards = new List<StoredCard>();
        private StoredCard previousRevealedCard;
        private readonly Dictionary<StoredCard, int> naturallyTriggeredNatureCounts = new Dictionary<StoredCard, int>();
        private readonly HashSet<StoredCard> pendingPackOpenNatureSources = new HashSet<StoredCard>();
        private bool natureAbilityChainActive;
        private int natureAbilityChainTriggerCount;
        private readonly List<GameObject> deckVisuals = new List<GameObject>();
        private readonly List<ScorePopup> scorePopups = new List<ScorePopup>();
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private PackVisual pack;
        private PackTearVisual tearVisual;
        private Transform cardStack;
        private Transform deckRoot;
        private readonly List<GameObject> emptyDeckPlaceholders = new List<GameObject>();
        private GameObject background;
        private GameObject deckInspectionBackdrop;
        private int inspectedDeckIndex = -1;
        private bool inspectionPackWasActive;
        private bool inspectionStackWasActive;
        private bool deckInspectionDragging;
        private bool deckInspectionReturning;
        private bool deckInspectionPressOutside;
        private bool deckInspectionHasDragged;
        private Vector2 deckInspectionDragStart;
        private Quaternion deckInspectionStartRotation;
        private Coroutine deckInspectionReturnRoutine;
        private int pressedDeckIndex = -1;
        private bool deckCardDragActive;
        private Vector2 deckCardDragStart;
        [SerializeField] private global::CardPackData activePackData;
        [SerializeField] private global::CardPackPoolData packPoolData;
        private global::CardPackData[] randomPackPool;
        private global::CardPackData leftPackChoice;
        private global::CardPackData rightPackChoice;
        private PackVisual leftPackChoiceVisual;
        private PackVisual rightPackChoiceVisual;
        private readonly List<Material> packChoiceMaterials = new List<Material>();
        private global::CardPackData inspectedPackChoice;
        private Vector2 packContentsScroll;
        private CardVisual packContentsPreviewVisual;
        private int packContentsPreviewIndex;
        private bool packContentsPackWasActive;
        private bool packContentsStackWasActive;
        private global::CardData[] fallbackCards;
        private global::CardData runtimeFallbackCard;
        private global::CardPackEntry runtimeFallbackEntry;
        private Font font;
        private AudioSource scorePopupAudioSource;
        private AudioClip scorePopupAudioClip;
        private AudioSource abilityEffectAudioSource;
        private AudioClip magicEquipAudioClip;
        private AudioClip runeResonanceAudioClip;
        private AudioSource packTearAudioSource;
        private AudioClip packTearAudioClip;
        private AudioSource cardRarityAudioSource;
        private readonly AudioClip[] cardRarityAudioClips = new AudioClip[5];
        private RevealPhase phase;
        private int cardIndex;
        private bool currentPackIsHolographic;
        private bool packTearInProgress;
        private bool runeResonanceWasActive;
        private bool gestureDragging;
        private bool inspectionDragging;
        private Vector2 dragStart;
        private Vector2 dragDelta;
        private Vector3 gestureStartPosition;
        private Quaternion gestureStartRotation;
        private Transform inspectionTarget;
        private Quaternion inspectionStartRotation;
        private Vector3 inspectionPivotWorld;
        private Coroutine inspectionReturnRoutine;
        private CardVisual activeSlidingCard;
        private bool cardTransitionActive;
        private bool transitionDragActive;
        private bool transitionSwipeCommitted;
        private int queuedCardSwipes;
        private float queuedSwipeDirection;
        private int totalScore;
        private int roundScore;
        private int completedPacks;
        private int currentGoalIndex;
        private bool currentPackOpenedForGoal;
        private int pendingScore;
        private float pendingScoreCommitTime = -1f;
        private int scoreTransferAmount;
        private int scoreTransferApplied;
        private float scoreTransferStartTime = -1f;
        private GUIStyle scoreStyle;
        private GUIStyle goalStyle;
        private GUIStyle runEndTitleStyle;
        private GUIStyle runEndBodyStyle;
        private GUIStyle runEndButtonStyle;
        private GUIStyle runEndBadgeStyle;
        private GUIStyle runEndStatLabelStyle;
        private GUIStyle runEndStatValueStyle;
        private GUIStyle runEndHintStyle;
        private GUIStyle packChoiceTitleStyle;
        private GUIStyle packContentsTitleStyle;
        private GUIStyle packContentsCardStyle;
        private GUIStyle scorePopupStyle;
        private GUIStyle packGuideStyle;
        private GUIStyle controlGuideStyle;
        private GUIStyle controlGuideTitleStyle;
        private GUIStyle controlGuideToggleStyle;
        private GUIStyle deckHeaderStyle;
        private GUIStyle discardButtonStyle;
        private GUIStyle discardPanelStyle;
        private GUIStyle discardMessageStyle;
        private GUIStyle deckRarityStyle;
        private GUIStyle deckStatusStyle;
        private GUIStyle deckInspectionStatusStyle;
        private Texture2D roundedDiscardTexture;
        private bool discardConfirmationVisible;
        private bool settingsOpen;
        private bool abandonConfirmationVisible;
        private int uiLanguage;
        private float masterVolume = 1f;
        private bool controlGuideOpen = true;
        private GUIStyle settingsTitleStyle;
        private GUIStyle settingsLabelStyle;
        private bool sharedResultMode;
        private bool sharedPackPreviewActive;
        private string sharedResultSnapshotJson;
        private string shareFeedback;
        private float shareFeedbackUntil;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void CardOpenShareResult(string title, string text, string url);
        [DllImport("__Internal")]
        private static extern void CardOpenReportReady();
#endif

        private void Awake()
        {
            LoadUserSettings();
            SetupScene();
            StartNewRun();
            TryLoadSharedResultFromUrl();
#if UNITY_WEBGL && !UNITY_EDITOR
            CardOpenReportReady();
#endif
        }

        private bool IsEnglishUi { get { return uiLanguage == 1; } }

        private string Ui(string korean, string english)
        {
            return IsEnglishUi ? english : korean;
        }

        private void LoadUserSettings()
        {
            uiLanguage = Mathf.Clamp(PlayerPrefs.GetInt("CardOpen.UiLanguage", 0), 0, 1);
            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("CardOpen.MasterVolume", 1f));
            controlGuideOpen = PlayerPrefs.GetInt("CardOpen.ControlGuideOpen", 1) != 0;
            AudioListener.volume = masterVolume;
        }

        private void SaveUserSettings()
        {
            PlayerPrefs.SetInt("CardOpen.UiLanguage", uiLanguage);
            PlayerPrefs.SetFloat("CardOpen.MasterVolume", masterVolume);
            PlayerPrefs.SetInt("CardOpen.ControlGuideOpen", controlGuideOpen ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void SetUiLanguage(int language)
        {
            int clamped = Mathf.Clamp(language, 0, 1);
            if (uiLanguage == clamped) return;
            uiLanguage = clamped;
            SaveUserSettings();
            RefreshLocalizedCardDisplays();
        }

        private void SetMasterVolume(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
            if (Mathf.Approximately(masterVolume, clamped)) return;
            masterVolume = clamped;
            AudioListener.volume = masterVolume;
            SaveUserSettings();
        }
        private void ShareCurrentResult()
        {
            string url = BuildSharedResultUrl();
            if (string.IsNullOrEmpty(url))
            {
                shareFeedback = Ui("WebGL \uBE4C\uB4DC\uC5D0\uC11C \uACF5\uC720\uD560 \uC218 \uC788\uC2B5\uB2C8\uB2E4.", "Sharing is available in the WebGL build.");
                shareFeedbackUntil = Time.unscaledTime + 3f;
                return;
            }

            string title = Ui("\uCE74\uB4DC\uD329 \uACB0\uACFC", "Card Pack Result");
            string message = Ui("총점 ", "Total score ") + totalScore.ToString("N0");
#if UNITY_WEBGL && !UNITY_EDITOR
            CardOpenShareResult(title, message, url);
            shareFeedback = Ui("\uACF5\uC720 \uCC3D\uC744 \uC5F4\uC5C8\uC2B5\uB2C8\uB2E4.", "Share dialog opened.");
#else
            GUIUtility.systemCopyBuffer = url;
            shareFeedback = Ui("\uACF5\uC720 \uB9C1\uD06C\uB97C \uBCF5\uC0AC\uD588\uC2B5\uB2C8\uB2E4.", "Share link copied.");
#endif
            shareFeedbackUntil = Time.unscaledTime + 3f;
        }

        private string BuildSharedResultUrl()
        {
            string baseUrl = Application.absoluteURL;
            if (string.IsNullOrWhiteSpace(baseUrl)) return string.Empty;
            int hashIndex = baseUrl.IndexOf('#');
            if (hashIndex >= 0) baseUrl = baseUrl.Substring(0, hashIndex);
            int queryIndex = baseUrl.IndexOf('?');
            if (queryIndex >= 0) baseUrl = baseUrl.Substring(0, queryIndex);

            SharedResultData result = new SharedResultData
            {
                TotalScore = totalScore,
                RoundScore = roundScore,
                GoalIndex = currentGoalIndex,
                CompletedPacks = completedPacks,
                Cleared = phase == RevealPhase.RunCleared,
                Deck = new SharedCardData[deckCards.Count]
            };
            for (int i = 0; i < deckCards.Count; i++) result.Deck[i] = CaptureSharedCard(deckCards[i]);
            string payload = EncodeSharedResult(JsonUtility.ToJson(result));
            return baseUrl + "?cardopenResult=" + Uri.EscapeDataString(payload);
        }

        private static string EncodeSharedResult(string json)
        {
            byte[] source = Encoding.UTF8.GetBytes(json);
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
                    gzip.Write(source, 0, source.Length);
                return "z." + ToBase64Url(output.ToArray());
            }
        }

        private static string DecodeSharedResult(string payload)
        {
            string decodedPayload = Uri.UnescapeDataString(payload);
            bool compressed = decodedPayload.StartsWith("z.", StringComparison.Ordinal);
            if (compressed) decodedPayload = decodedPayload.Substring(2);
            byte[] source = FromBase64Url(decodedPayload);
            if (!compressed) return Encoding.UTF8.GetString(source);

            using (MemoryStream input = new MemoryStream(source))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[4096];
                int total = 0;
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > 262144) throw new InvalidDataException("Shared result is too large.");
                    output.Write(buffer, 0, read);
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private static string ToBase64Url(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] FromBase64Url(string value)
        {
            string normalized = value.Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 2: normalized += "=="; break;
                case 3: normalized += "="; break;
                case 1: throw new FormatException("Invalid shared result encoding.");
            }
            return Convert.FromBase64String(normalized);
        }

        private SharedCardData CaptureSharedCard(StoredCard card)
        {
            if (card == null || card.Data == null) return null;
            SharedCardData data = new SharedCardData
            {
                ResourceName = card.Data.name,
                Color = (int)card.Color,
                Number = card.Number,
                Rarity = (int)card.Rarity,
                DeckSlot = card.DeckSlot,
                CombinedCopies = card.CombinedCopies,
                CombinedHolographicCopies = card.CombinedHolographicCopies,
                IsHolographic = card.IsHolographic,
                EquippedMagic = CaptureSharedCard(card.EquippedMagic),
                EquippedWeapon = CaptureSharedCard(card.EquippedWeapon),
                AccumulatedFlatScore = CaptureIntValues(card.AccumulatedFlatScoreByAbility),
                RemainingDraws = CaptureIntValues(card.RemainingDrawsByAbility),
                Stacks = CaptureIntValues(card.StackByAbilityCopy),
                PerPackTriggers = CaptureIntValues(card.PerPackTriggerCountByAbility),
                PacksElapsed = CaptureIntValues(card.PacksElapsedByAbility),
                AccumulatedPercent = CaptureFloatValues(card.AccumulatedPercentByAbility)
            };
            data.InheritedRelics = new SharedCardData[card.InheritedRelics.Count];
            for (int i = 0; i < card.InheritedRelics.Count; i++)
                data.InheritedRelics[i] = CaptureSharedCard(card.InheritedRelics[i]);
            return data;
        }

        private static SharedIntValue[] CaptureIntValues(Dictionary<int, int> source)
        {
            SharedIntValue[] values = new SharedIntValue[source.Count];
            int index = 0;
            foreach (KeyValuePair<int, int> pair in source)
                values[index++] = new SharedIntValue { Key = pair.Key, Value = pair.Value };
            return values;
        }

        private static SharedFloatValue[] CaptureFloatValues(Dictionary<int, float> source)
        {
            SharedFloatValue[] values = new SharedFloatValue[source.Count];
            int index = 0;
            foreach (KeyValuePair<int, float> pair in source)
                values[index++] = new SharedFloatValue { Key = pair.Key, Value = pair.Value };
            return values;
        }

        private bool TryLoadSharedResultFromUrl()
        {
            string payload = GetQueryValue(Application.absoluteURL, "cardopenResult");
            if (string.IsNullOrEmpty(payload)) return false;
            try
            {
                string json = DecodeSharedResult(payload);
                SharedResultData result = JsonUtility.FromJson<SharedResultData>(json);
                if (result == null || result.Version != 1) return false;
                sharedResultSnapshotJson = json;
                RestoreSharedResult(result);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not load shared card result: " + exception.Message);
                return false;
            }
        }

        private static string GetQueryValue(string url, string key)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int queryIndex = url.IndexOf('?');
            if (queryIndex < 0 || queryIndex + 1 >= url.Length) return null;
            int fragmentIndex = url.IndexOf('#', queryIndex + 1);
            string query = fragmentIndex >= 0
                ? url.Substring(queryIndex + 1, fragmentIndex - queryIndex - 1)
                : url.Substring(queryIndex + 1);
            string[] entries = query.Split('&');
            for (int i = 0; i < entries.Length; i++)
            {
                int equalsIndex = entries[i].IndexOf('=');
                string entryKey = equalsIndex >= 0 ? entries[i].Substring(0, equalsIndex) : entries[i];
                if (!string.Equals(entryKey, key, StringComparison.Ordinal)) continue;
                return equalsIndex >= 0 ? entries[i].Substring(equalsIndex + 1) : string.Empty;
            }
            return null;
        }

        private void RestoreSharedResult(SharedResultData result)
        {
            CloseDeckInspection();
            ClearPackChoiceVisuals();
            ClearCards();
            for (int i = 0; i < deckVisuals.Count; i++)
                if (deckVisuals[i] != null) Destroy(deckVisuals[i]);
            deckVisuals.Clear();
            deckCards.Clear();
            if (pack != null) pack.gameObject.SetActive(false);
            if (cardStack != null) cardStack.gameObject.SetActive(false);

            totalScore = Mathf.Max(0, result.TotalScore);
            roundScore = Mathf.Max(0, result.RoundScore);
            currentGoalIndex = Mathf.Clamp(result.GoalIndex, 0, GoalScores.Length);
            completedPacks = Mathf.Max(0, result.CompletedPacks);
            pendingScore = 0;
            pendingScoreCommitTime = -1f;
            scoreTransferAmount = 0;
            scoreTransferApplied = 0;
            scoreTransferStartTime = -1f;
            scorePopups.Clear();

            global::CardData[] resources = Resources.LoadAll<global::CardData>(string.Empty);
            Dictionary<string, global::CardData> lookup = new Dictionary<string, global::CardData>();
            for (int i = 0; i < resources.Length; i++)
                if (resources[i] != null && !lookup.ContainsKey(resources[i].name))
                    lookup.Add(resources[i].name, resources[i]);
            if (result.Deck != null)
            {
                for (int i = 0; i < result.Deck.Length && deckCards.Count < 5; i++)
                {
                    StoredCard card = RestoreSharedCard(result.Deck[i], lookup);
                    if (card == null) continue;
                    card.IsStoredInDeck = true;
                    deckCards.Add(card);
                    deckVisuals.Add(BuildDeckVisualForStoredCard(card));
                }
            }

            sharedResultMode = true;
            phase = result.Cleared ? RevealPhase.RunCleared : RevealPhase.GameOver;
            RefreshDeckCardDisplayNames();
            LayoutDeckVisuals();
        }

        private StoredCard RestoreSharedCard(SharedCardData source,
            Dictionary<string, global::CardData> lookup)
        {
            if (source == null || string.IsNullOrEmpty(source.ResourceName)
                || !lookup.TryGetValue(source.ResourceName, out global::CardData data)) return null;
            StoredCard card = new StoredCard
            {
                Name = data.Name,
                Data = data,
                Rarity = (global::CardRarity)source.Rarity,
                Color = (global::CardColor)source.Color,
                Number = source.Number,
                DeckSlot = source.DeckSlot,
                CombinedCopies = Mathf.Max(1, source.CombinedCopies),
                CombinedHolographicCopies = Mathf.Max(0, source.CombinedHolographicCopies),
                IsHolographic = source.IsHolographic,
                IsStoredInDeck = true
            };
            card.EquippedMagic = RestoreSharedCard(source.EquippedMagic, lookup);
            card.EquippedWeapon = RestoreSharedCard(source.EquippedWeapon, lookup);
            if (source.InheritedRelics != null)
                for (int i = 0; i < source.InheritedRelics.Length; i++)
                {
                    StoredCard relic = RestoreSharedCard(source.InheritedRelics[i], lookup);
                    if (relic != null) card.InheritedRelics.Add(relic);
                }
            RestoreIntValues(source.AccumulatedFlatScore, card.AccumulatedFlatScoreByAbility);
            RestoreIntValues(source.RemainingDraws, card.RemainingDrawsByAbility);
            RestoreIntValues(source.Stacks, card.StackByAbilityCopy);
            RestoreIntValues(source.PerPackTriggers, card.PerPackTriggerCountByAbility);
            RestoreIntValues(source.PacksElapsed, card.PacksElapsedByAbility);
            RestoreFloatValues(source.AccumulatedPercent, card.AccumulatedPercentByAbility);
            RemoveLegacySatelliteRelics(card);
            return card;
        }

        private static void RemoveLegacySatelliteRelics(StoredCard card)
        {
            if (card == null || card.Data == null || card.Data.name != "MagicEngineeringSatellite")
                return;
            card.InheritedRelics.Clear();
        }

        private static void RestoreIntValues(SharedIntValue[] source, Dictionary<int, int> target)
        {
            if (source == null) return;
            for (int i = 0; i < source.Length; i++)
                if (source[i] != null) target[source[i].Key] = source[i].Value;
        }

        private static void RestoreFloatValues(SharedFloatValue[] source, Dictionary<int, float> target)
        {
            if (source == null) return;
            for (int i = 0; i < source.Length; i++)
                if (source[i] != null) target[source[i].Key] = source[i].Value;
        }
        private void LateUpdate()
        {
            UpdatePendingScore();
            if (background != null && Camera.main != null)
                LayoutBackground(Camera.main);
            if (pack != null && phase == RevealPhase.Pack)
            {
                pack.transform.localScale = Vector3.one * ResponsiveWorldScale(1.95f, 1.50f);
                if (!gestureDragging && !inspectionDragging)
                    pack.transform.position = CurrentPackHome;
            }
            if (packTearInProgress)
            {
                if (pack != null)
                {
                    pack.transform.position = CurrentPackHome;
                    pack.transform.localScale = Vector3.one * ResponsiveWorldScale(1.95f, 1.50f);
                }
                if (cardStack != null)
                {
                    Vector3 tearCardOffset = IsPortraitUi ? Vector3.zero : PackedCardOffset;
                    cardStack.position = CardHome + tearCardOffset - cardStack.rotation * CardHome;
                }
                for (int i = 0; i < cards.Count; i++)
                    if (cards[i] != null && cards[i].gameObject.activeSelf)
                        cards[i].transform.localScale = Vector3.one * CurrentRevealedCardScale;
            }
            if (phase == RevealPhase.CardBack || phase == RevealPhase.CardFront)
            {
                for (int i = 0; i < cards.Count; i++)
                    if (cards[i] != null && cards[i].gameObject.activeSelf)
                        cards[i].transform.localScale = Vector3.one * CurrentRevealedCardScale;
            }
            if (packContentsPreviewVisual != null)
                packContentsPreviewVisual.transform.localScale = Vector3.one * CurrentRevealedCardScale;
            if (deckRoot != null) LayoutDeckVisuals();
        }

        private void SetupScene()
        {
            Application.targetFrameRate = 60;
            if (activePackData == null) activePackData = LoadCardPackData();
            font = Resources.Load<Font>("Fonts/CardFont");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial Unicode MS", "Arial" }, 64);
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            SetupScorePopupAudio();
            SetupAbilityEffectAudio();
            SetupPackTearAudio();
            SetupCardRarityAudio();
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            camera.transform.position = new Vector3(0f, 0.95f, -10.8f);
            camera.transform.LookAt(new Vector3(0f, 0.95f, 0f));
            camera.fieldOfView = 43f;
            camera.backgroundColor = new Color(0.018f, 0.022f, 0.038f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = false;
            CreateBackground(camera);

            Light key = FindAnyObjectByType<Light>();
            if (key != null)
            {
                key.type = LightType.Directional;
                key.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
                key.color = new Color(1f, 0.86f, 0.72f);
                key.intensity = 1.25f;
                key.shadows = LightShadows.None;
            }
            GameObject fillObject = new GameObject("Fill Light");
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
            fill.color = new Color(0.34f, 0.50f, 1f);
            fill.intensity = 0.7f;
            fill.shadows = LightShadows.None;

            GameObject stackObject = new GameObject("Card Stack");
            cardStack = stackObject.transform;
            stackObject.AddComponent<CardStackVisual>();

            GameObject deckObject = new GameObject("Stored Card Deck");
            deckRoot = deckObject.transform;
            CreateEmptyDeckPlaceholder();
            CreateDeckInspectionBackdrop();

            GameObject packObject = new GameObject("3D Card Pack");
            packObject.transform.position = CurrentPackHome;
            pack = packObject.AddComponent<PackVisual>();
            Material packMaterial = GetMaterial("Pack", new Color(0.18f, 0.07f, 0.32f), 0.18f);
            Material packFrontMaterial = GetMaterial("PackFrontArtwork", Color.white, 0.82f);
            Material packBackMaterial = GetMaterial("PackBackArtwork", Color.white, 0.82f);
            ApplyTextureOrFallback(packFrontMaterial, activePackData != null ? activePackData.FrontImage : null,
                Resources.Load<Texture2D>("Textures/CardPackFrontStoryTailBlueSky"));
            ApplyTextureOrFallback(packBackMaterial, activePackData != null ? activePackData.BackImage : null,
                Resources.Load<Texture2D>("Textures/CardPackBackStoryTail"));
            pack.Build(packMaterial, packFrontMaterial, packBackMaterial);
            tearVisual = packObject.AddComponent<PackTearVisual>();
            tearVisual.Initialize(packMaterial);
        }

        private void CompletePackAndBeginNextSequence()
        {
            CommitPendingScoreImmediately();
            if (sharedPackPreviewActive)
            {
                ReturnToSharedResultAfterPackPreview();
                return;
            }
            AdvanceDeckTransformationsAfterPack();
            completedPacks++;
            if (completedPacks % PacksPerGoal == 0)
            {
                int targetScore = GoalScores[currentGoalIndex];
                if (roundScore < targetScore)
                {
                    phase = RevealPhase.GameOver;
                    return;
                }

                currentGoalIndex++;
                if (currentGoalIndex >= GoalScores.Length)
                {
                    phase = RevealPhase.RunCleared;
                    return;
                }

                roundScore = 0;
            }
            BeginPackChoice();
        }

        private void StartNewRun()
        {
            sharedPackPreviewActive = false;
            sharedResultSnapshotJson = null;
            sharedResultMode = false;
            shareFeedback = null;
            CloseDeckInspection();
            ClearPackChoiceVisuals();
            if (pack != null) pack.gameObject.SetActive(true);
            if (cardStack != null) cardStack.gameObject.SetActive(true);
            for (int i = 0; i < deckVisuals.Count; i++)
                if (deckVisuals[i] != null) Destroy(deckVisuals[i]);
            deckVisuals.Clear();
            deckCards.Clear();
            runeResonanceWasActive = false;
            scorePopups.Clear();
            totalScore = 0;
            roundScore = 0;
            pendingScore = 0;
            pendingScoreCommitTime = -1f;
            scoreTransferAmount = 0;
            scoreTransferApplied = 0;
            scoreTransferStartTime = -1f;
            completedPacks = 0;
            currentGoalIndex = 0;
            currentPackOpenedForGoal = false;
            previousRevealedCard = null;
            pendingPackOpenNatureSources.Clear();
            ClearNatureAbilityChain();
            leftPackChoice = null;
            rightPackChoice = null;
            activePackData = Resources.Load<global::CardPackData>("CardPacks/TaleTail");
            if (activePackData == null) activePackData = LoadCardPackData();
            BeginSequence(false);
        }

        private void BeginSharedPackPreview()
        {
            if (!sharedResultMode || string.IsNullOrEmpty(sharedResultSnapshotJson)) return;
            CloseDeckInspection();
            ClearPackChoiceVisuals();
            sharedPackPreviewActive = true;
            sharedResultMode = false;
            shareFeedback = null;
            previousRevealedCard = null;
            pendingPackOpenNatureSources.Clear();
            ClearNatureAbilityChain();
            BeginPackChoice();
        }

        private void ReturnToSharedResultAfterPackPreview()
        {
            string snapshotJson = sharedResultSnapshotJson;
            sharedPackPreviewActive = false;
            previousRevealedCard = null;
            pendingPackOpenNatureSources.Clear();
            ClearNatureAbilityChain();
            if (string.IsNullOrEmpty(snapshotJson))
            {
                StartNewRun();
                return;
            }

            SharedResultData snapshot = JsonUtility.FromJson<SharedResultData>(snapshotJson);
            if (snapshot == null || snapshot.Version != 1)
            {
                StartNewRun();
                return;
            }
            RestoreSharedResult(snapshot);
        }

        private void AdvanceDeckTransformationsAfterPack()
        {
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard card = deckCards[i];
                if (card == null || card.Data == null || card.Data.DeckAbilities == null) continue;
                for (int j = 0; j < card.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = card.Data.DeckAbilities[j];
                    if (ability == null || ability.Effect != global::DeckAbilityEffect.TransformAfterPacks
                        || ability.TransformedCard == null) continue;
                    int requiredPacks = Mathf.Max(1, ability.PacksToTransform);
                    card.PacksElapsedByAbility.TryGetValue(j, out int elapsedPacks);
                    elapsedPacks++;
                    card.PacksElapsedByAbility[j] = elapsedPacks;
                    if (elapsedPacks < requiredPacks) continue;
                    TransformStoredDeckCard(i, ability.TransformedCard);
                    break;
                }
            }
            LayoutDeckVisuals();
        }

        private void TransformStoredDeckCard(int deckIndex, global::CardData transformedData)
        {
            if (deckIndex < 0 || deckIndex >= deckCards.Count || transformedData == null) return;
            StoredCard card = deckCards[deckIndex];
            if (card == null) return;
            card.Name = transformedData.Name;
            card.Data = transformedData;
            card.Rarity = transformedData.Rare;
            card.AccumulatedPercentByAbility.Clear();
            card.AccumulatedFlatScoreByAbility.Clear();
            card.RemainingDrawsByAbility.Clear();
            card.StackByAbilityCopy.Clear();
            card.TriggeredStackCountsThisDraw.Clear();
            card.UsedOncePerPackAbilityCopies.Clear();
            card.PerPackTriggerCountByAbility.Clear();
            card.PacksElapsedByAbility.Clear();

            GameObject oldVisual = deckIndex < deckVisuals.Count ? deckVisuals[deckIndex] : null;
            GameObject newVisual = BuildDeckVisualForStoredCard(card);
            if (deckIndex < deckVisuals.Count) deckVisuals[deckIndex] = newVisual;
            if (oldVisual != null) Destroy(oldVisual);
        }

        private GameObject BuildDeckVisualForStoredCard(StoredCard card)
        {
            if (card == null || card.Data == null || deckRoot == null) return null;
            global::CardData data = card.Data;
            GameObject cardObject = new GameObject("Stored Card - " + data.Name);
            cardObject.transform.SetParent(deckRoot, false);
            CardVisual visual = cardObject.AddComponent<CardVisual>();
            string attributeKey = card.Color.ToString();
            Material attributeMaterial = GetTextureMaterial("Attribute_" + attributeKey,
                "CardAssets/Attributes/Attribute" + attributeKey, false);
            Material rarityPatternMaterial = GetTextureMaterial("Pattern_" + data.RarityAssetKey,
                "CardAssets/Rarities/Pattern" + data.RarityAssetKey, true, 0);
            string costAsset = card.Number == 6 ? "CostSigma" : "Cost" + card.Number;
            Material costMaterial = GetTextureMaterial("Cost_" + card.Number,
                "CardAssets/Costs/" + costAsset, true, 20);
            Material illustrationMaterial = GetTextureMaterial("CardImage_" + data.GetHashCode(), data.Image, true, 10);
            visual.BuildFromData(data, card.Color, attributeMaterial,
                GetTextureMaterial("CardBack", "CardAssets/Attributes/AttributeBackRemasterPurple", false),
                rarityPatternMaterial, illustrationMaterial, costMaterial, font, IsEnglishUi);
            visual.SetDisplayName(GetStoredCardDisplayName(card));
            visual.SetDisplayDescription(card.Data, GetStoredCardDisplayDescription(card), IsEnglishUi);
            if (card.IsHolographic) visual.EnableHologram();
            visual.PrepareFaceUp(Vector3.zero, 1f, 0f);
            visual.SetFaceDetailsVisible(true);
            cardObject.SetActive(true);
            SetStoredVisualShadowMode(cardObject);
            return cardObject;
        }

        private bool RollCurrentPackHolographic()
        {
            if (Random.value < 0.01f) return true;
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null
                        || ability.Effect != global::DeckAbilityEffect.GrantHologramChanceToPacksAndCards
                        || ability.ChancePercent <= 0f) continue;
                    for (int copy = 0; copy < effectiveCopies; copy++)
                        if (Random.value < ability.ChancePercent * 0.01f) return true;
                }
            }
            return false;
        }

        private void BeginSequence()
        {
            BeginSequence(true);
        }

        private void BeginSequence(bool chooseRandomPack)
        {
            currentPackOpenedForGoal = false;
            packTearInProgress = false;
            if (chooseRandomPack)
            {
                global::CardPackData selectedPack = LoadCardPackData();
                if (selectedPack != null) activePackData = selectedPack;
            }
            RefreshActivePackArtwork();
            ResetPerPackAccumulatedBonuses();
            ResetOncePerPackAbilityUsage();
            ClearCards();
            cardStack.position = Vector3.zero;
            cardStack.rotation = Quaternion.identity;
            cardStack.localScale = Vector3.one;
            currentPackIsHolographic = RollCurrentPackHolographic();
            BuildHiddenCardStack();
            cardIndex = 0;
            gestureDragging = false;
            inspectionDragging = false;
            dragDelta = Vector2.zero;
            activeSlidingCard = null;
            cardTransitionActive = false;
            transitionDragActive = false;
            transitionSwipeCommitted = false;
            queuedCardSwipes = 0;
            pack.ResetVisual();
            pack.SetHolographic(currentPackIsHolographic);
            tearVisual.ResetTear();
            pack.transform.position = CurrentPackHome;
            pack.transform.localScale = Vector3.one * ResponsiveWorldScale(1.95f, 1.50f);
            pack.transform.rotation = Quaternion.identity;
            phase = RevealPhase.Pack;

            if (inspectedDeckIndex >= 0)
            {
                inspectionPackWasActive = true;
                inspectionStackWasActive = true;
                pack.gameObject.SetActive(false);
                cardStack.gameObject.SetActive(false);
            }
        }

        private void BuildHiddenCardStack()
        {
            int baseCardCount = activePackData != null ? activePackData.CardsPerPack : FallbackCardsPerPack;
            int cardCount = baseCardCount + GetAdditionalNextPackCardCount();
            for (int i = 0; i < cardCount; i++)
            {
                global::CardPackEntry entry = i == 4 && activePackData != null
                    ? activePackData.DrawRandomCardAtLeast(global::CardRarity.Rare)
                    : null;
                if (entry == null) entry = DrawCard();
                if (entry == null || entry.Card == null) continue;
                global::CardData data = entry.Card;

                GameObject cardObject = new GameObject("Card - " + data.Name);
                cardObject.transform.SetParent(cardStack, true);
                CardVisual visual = cardObject.AddComponent<CardVisual>();
                Material attributeMaterial = GetTextureMaterial("Attribute_" + entry.AttributeAssetKey,
                    "CardAssets/Attributes/Attribute" + entry.AttributeAssetKey, false);
                Material rarityPatternMaterial = GetTextureMaterial("Pattern_" + data.RarityAssetKey,
                    "CardAssets/Rarities/Pattern" + data.RarityAssetKey, true, 0);
                string costAsset = entry.DisplayNumber == 6 ? "CostSigma" : "Cost" + entry.DisplayNumber;
                Material costMaterial = GetTextureMaterial("Cost_" + entry.DisplayNumber,
                    "CardAssets/Costs/" + costAsset, true, 20);
                Material illustrationMaterial = GetTextureMaterial("CardImage_" + data.GetHashCode(), data.Image, true, 10);
                visual.BuildFromData(data, entry.Color, attributeMaterial,
                    GetTextureMaterial("CardBack", "CardAssets/Attributes/AttributeBackRemasterPurple", false),
                    rarityPatternMaterial, illustrationMaterial, costMaterial, font, IsEnglishUi);
                bool isHolographic = currentPackIsHolographic || Random.value < 0.1f;
                if (isHolographic) visual.EnableHologram();
                visual.PrepareFaceUp(CardHome + new Vector3(0f, i * 0.025f, i * 0.065f), CurrentRevealedCardScale,
                    (i - (cardCount - 1) * 0.5f) * 0.7f);
                visual.gameObject.SetActive(false);
                cards.Add(visual);
                currentPackCards.Add(new StoredCard
                {
                    Name = data.Name,
                    Data = data,
                    Rarity = data.Rare,
                    Color = entry.Color,
                    Number = entry.DisplayNumber,
                    IsHolographic = isHolographic
                });
            }
        }

        private global::CardPackEntry DrawCard()
        {
            if (activePackData != null)
            {
                global::CardPackEntry includedCard = activePackData.DrawRandomCard();
                if (includedCard != null) return includedCard;
            }

            if (fallbackCards == null || fallbackCards.Length == 0)
                fallbackCards = Resources.LoadAll<global::CardData>(string.Empty);
            if (fallbackCards != null && fallbackCards.Length > 0)
            {
                return new global::CardPackEntry
                {
                    Card = fallbackCards[Random.Range(0, fallbackCards.Length)],
                    Number = 1,
                    Color = global::CardColor.Green,
                    InclusionRate = 100f
                };
            }

            if (runtimeFallbackCard == null)
            {
                runtimeFallbackCard = ScriptableObject.CreateInstance<global::CardData>();
                runtimeFallbackCard.Name = "마법 총알";
                runtimeFallbackCard.Description = "7\uC758 \uD53C\uD574\uB97C \uC90D\uB2C8\uB2E4.";
                runtimeFallbackCard.EnglishName = "Magic Bullet";
                runtimeFallbackCard.EnglishDescription = "Deals 7 damage.";
                runtimeFallbackCard.Rare = global::CardRarity.Common;
                runtimeFallbackEntry = new global::CardPackEntry
                {
                    Card = runtimeFallbackCard,
                    Number = 5,
                    Color = global::CardColor.Green,
                    InclusionRate = 100f
                };
            }
            return runtimeFallbackEntry;
        }
        private void BeginPackChoice()
        {
            ClearCards();
            ClearPackChoiceVisuals();
            currentPackOpenedForGoal = false;
            if (pack != null) pack.gameObject.SetActive(false);
            if (cardStack != null) cardStack.gameObject.SetActive(false);
            leftPackChoice = LoadCardPackData();
            rightPackChoice = DrawAlternativePack(leftPackChoice);
            if (leftPackChoice == null)
            {
                activePackData = Resources.Load<global::CardPackData>("CardPacks/TaleTail");
                if (pack != null) pack.gameObject.SetActive(true);
                if (cardStack != null) cardStack.gameObject.SetActive(true);
                BeginSequence(false);
                return;
            }
            if (rightPackChoice == null)
            {
                SelectPackChoice(leftPackChoice);
                return;
            }
            CreatePackChoiceVisuals();
            phase = RevealPhase.PackChoice;
        }

        private global::CardPackData DrawAlternativePack(global::CardPackData excludedPack)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                global::CardPackData candidate = LoadCardPackData();
                if (candidate != null && candidate != excludedPack) return candidate;
            }

            if (randomPackPool == null)
                randomPackPool = Resources.LoadAll<global::CardPackData>("CardPacks");
            for (int i = 0; i < randomPackPool.Length; i++)
                if (randomPackPool[i] != null && randomPackPool[i] != excludedPack)
                    return randomPackPool[i];
            return null;
        }

        private void SelectPackChoice(global::CardPackData selectedPack)
        {
            if (selectedPack == null) return;
            ClearPackChoiceVisuals();
            activePackData = selectedPack;
            leftPackChoice = null;
            rightPackChoice = null;
            if (pack != null) pack.gameObject.SetActive(true);
            if (cardStack != null) cardStack.gameObject.SetActive(true);
            BeginSequence(false);
        }

        private void CreatePackChoiceVisuals()
        {
            float choiceX = IsPortraitUi ? 1.05f : 1.8f;
            float choiceY = IsPortraitUi ? 0.42f : 0.55f;
            leftPackChoiceVisual = CreatePackChoiceVisual(
                "Left Pack Choice", leftPackChoice, new Vector3(-choiceX, choiceY, -0.65f));
            rightPackChoiceVisual = CreatePackChoiceVisual(
                "Right Pack Choice", rightPackChoice, new Vector3(choiceX, choiceY, -0.65f));
        }

        private PackVisual CreatePackChoiceVisual(
            string objectName, global::CardPackData data, Vector3 position)
        {
            if (data == null) return null;

            Texture2D frontTexture = data.FrontImage != null
                ? data.FrontImage
                : Resources.Load<Texture2D>("Textures/CardPackFrontStoryTailBlueSky");
            Texture2D backTexture = data.BackImage != null
                ? data.BackImage
                : Resources.Load<Texture2D>("Textures/CardPackBackStoryTail");
            Material frontMaterial = CreateTextureMaterial(objectName + " Front", frontTexture, false, 0);
            Material backMaterial = CreateTextureMaterial(objectName + " Back", backTexture, false, 0);
            packChoiceMaterials.Add(frontMaterial);
            packChoiceMaterials.Add(backMaterial);

            GameObject choiceObject = new GameObject(objectName);
            choiceObject.transform.position = position;
            choiceObject.transform.localScale = Vector3.one * ResponsiveWorldScale(1.45f, 1.18f);
            PackVisual choiceVisual = choiceObject.AddComponent<PackVisual>();
            choiceVisual.Build(
                GetMaterial("Pack", new Color(0.18f, 0.07f, 0.32f), 0.18f),
                frontMaterial,
                backMaterial);
            return choiceVisual;
        }

        private void ClearPackChoiceVisuals()
        {
            if (leftPackChoiceVisual != null)
            {
                leftPackChoiceVisual.gameObject.SetActive(false);
                Destroy(leftPackChoiceVisual.gameObject);
                leftPackChoiceVisual = null;
            }
            if (rightPackChoiceVisual != null)
            {
                rightPackChoiceVisual.gameObject.SetActive(false);
                Destroy(rightPackChoiceVisual.gameObject);
                rightPackChoiceVisual = null;
            }
            for (int i = 0; i < packChoiceMaterials.Count; i++)
            {
                if (packChoiceMaterials[i] != null) Destroy(packChoiceMaterials[i]);
            }
            packChoiceMaterials.Clear();
            ClearPackContentsPreview();
            inspectedPackChoice = null;
            packContentsScroll = Vector2.zero;
        }
        private global::CardPackData LoadCardPackData()
        {
            if (packPoolData == null)
                packPoolData = Resources.Load<global::CardPackPoolData>("CardPacks/CardPackPool");
            if (packPoolData != null)
                return packPoolData.DrawRandomPack();

            if (randomPackPool == null)
                randomPackPool = Resources.LoadAll<global::CardPackData>("CardPacks");
            if (randomPackPool.Length > 0)
                return randomPackPool[Random.Range(0, randomPackPool.Length)];
            return null;
        }

        private void RefreshActivePackArtwork()
        {
            if (materials.TryGetValue("PackFrontArtwork", out Material front))
                ApplyTextureOrFallback(front, activePackData != null ? activePackData.FrontImage : null,
                    Resources.Load<Texture2D>("Textures/CardPackFrontStoryTailBlueSky"));
            if (materials.TryGetValue("PackBackArtwork", out Material back))
                ApplyTextureOrFallback(back, activePackData != null ? activePackData.BackImage : null,
                    Resources.Load<Texture2D>("Textures/CardPackBackStoryTail"));
        }

        public void SetCardPackData(global::CardPackData data)
        {
            activePackData = data;
            RefreshActivePackArtwork();
            if (pack != null) BeginSequence(false);
        }
        private IEnumerator RemovePack(Vector2 direction)
        {
            phase = RevealPhase.Animating;
            packTearInProgress = true;
            PlayPackTearSound();
            currentPackOpenedForGoal = true;
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].transform.localScale = Vector3.one * CurrentRevealedCardScale;
                cards[i].SetFaceDetailsVisible(true);
                cards[i].gameObject.SetActive(true);
            }
            cards[0].PrepareFaceUp(CardHome, CurrentRevealedCardScale, 0f);
            Vector3 tearCardOffset = IsPortraitUi ? Vector3.zero : PackedCardOffset;
            yield return tearVisual.PeelInDirection(direction, cardStack, CardHome, tearCardOffset);
            packTearInProgress = false;
            pack.gameObject.SetActive(false);
            TriggerPackOpenedDeckAbilities();
            yield return ReturnCardStackToFront();
            PlayCardRarityRevealSound(currentPackCards[cardIndex].Rarity);
            AwardCurrentCardScore();
            phase = RevealPhase.CardFront;
        }

        private IEnumerator ReturnCardStackToFront()
        {
            Vector3 startPosition = cardStack.position;
            Quaternion startRotation = cardStack.rotation;
            const float duration = 0.22f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / duration);
                cardStack.position = Vector3.Lerp(startPosition, Vector3.zero, u);
                cardStack.rotation = Quaternion.Slerp(startRotation, Quaternion.identity, u);
                yield return null;
            }
            cardStack.position = Vector3.zero;
            cardStack.rotation = Quaternion.identity;
        }

        private IEnumerator FlipCard()
        {
            phase = RevealPhase.Animating;
            yield return cards[cardIndex].RevealInPlace();
            PlayCardRarityRevealSound(currentPackCards[cardIndex].Rarity);
            phase = RevealPhase.CardFront;
        }

        private IEnumerator MoveToNextCard(float direction)
        {
            phase = RevealPhase.Animating;
            cardTransitionActive = true;
            queuedCardSwipes = 0;
            float currentDirection = direction;
            while (true)
            {
                CommitPendingScoreImmediately();
                CardVisual current = cards[cardIndex];
                if (cardIndex + 1 < cards.Count)
                {
                    CardVisual next = cards[cardIndex + 1];
                    next.gameObject.SetActive(true);
                    next.PrepareFaceUp(CardHome + new Vector3(0f, 0.035f, 0.035f), CurrentRevealedCardScale, 0f);
                    next.SetFaceDetailsVisible(true);
                }
                activeSlidingCard = current;
                yield return current.SlideAway(currentDirection);
                activeSlidingCard = null;
                if (cardIndex >= 0 && cardIndex < currentPackCards.Count)
                    StoreCurrentCardInDeck(currentPackCards[cardIndex]);
                current.gameObject.SetActive(false);
                cardIndex++;
                if (cardIndex >= cards.Count)
                {
                    cardTransitionActive = false;
                    queuedCardSwipes = 0;
                    yield return new WaitForSeconds(0.35f);
                    CompletePackAndBeginNextSequence();
                    yield break;
                }
                yield return cards[cardIndex].MoveToFront(CardHome, CurrentRevealedCardScale, 0f);
                yield return RestoreCardStackRotation();
                PlayCardRarityRevealSound(currentPackCards[cardIndex].Rarity);
                AwardCurrentCardScore();
                if (queuedCardSwipes <= 0) break;
                queuedCardSwipes--;
                currentDirection = queuedSwipeDirection;
            }
            cardTransitionActive = false;
            phase = RevealPhase.CardFront;
        }

        private IEnumerator RestoreCardStackRotation()
        {
            Quaternion startRotation = cardStack.rotation;
            if (Quaternion.Angle(startRotation, Quaternion.identity) < 0.05f)
            {
                cardStack.rotation = Quaternion.identity;
                yield break;
            }

            const float duration = 0.16f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                cardStack.rotation = Quaternion.Slerp(startRotation, Quaternion.identity, u);
                yield return null;
            }
            cardStack.rotation = Quaternion.identity;
        }
        private static bool IsPortraitUi
        {
            get
            {
                Rect safeArea = Screen.safeArea;
                return safeArea.height > safeArea.width;
            }
        }

        private static float UiReferenceWidth { get { return IsPortraitUi ? PortraitWidth : ReferenceWidth; } }
        private static float PortraitExtraHeight
        {
            get
            {
                if (!IsPortraitUi) return 0f;
                Rect safeArea = Screen.safeArea;
                if (safeArea.width <= 0f) return 0f;
                float widthScale = safeArea.width / PortraitWidth;
                if (widthScale <= 0f) return 0f;
                return Mathf.Max(0f, safeArea.height / widthScale - PortraitHeight);
            }
        }
        private static float UiReferenceHeight { get { return IsPortraitUi ? PortraitHeight + PortraitExtraHeight : ReferenceHeight; } }
        private static float PortraitWorldScaleFactor
        {
            get
            {
                if (!IsPortraitUi) return 1f;
                GetUiLayout(out float uiScale, out _, out _);
                float screenHeightScale = Screen.height > 0 ? Screen.height / ReferenceHeight : 1f;
                return screenHeightScale > 0f ? uiScale / screenHeightScale : 1f;
            }
        }

        private static float ResponsiveWorldScale(float portraitScale, float landscapeScale)
        {
            return IsPortraitUi ? portraitScale * PortraitWorldScaleFactor : landscapeScale;
        }
        private static float CurrentRevealedCardScale { get { return ResponsiveWorldScale(2.10f, RevealedCardScale); } }

        private static Rect UiRect(Rect landscape, Rect portrait)
        {
            return IsPortraitUi ? portrait : landscape;
        }

        private static void GetUiLayout(out float scale, out float offsetX, out float offsetY)
        {
            Rect safeArea = Screen.safeArea;
            if (safeArea.width <= 0f || safeArea.height <= 0f)
                safeArea = new Rect(0f, 0f, Screen.width, Screen.height);

            scale = Mathf.Min(safeArea.width / UiReferenceWidth, safeArea.height / UiReferenceHeight);
            offsetX = safeArea.xMin + (safeArea.width - UiReferenceWidth * scale) * 0.5f;
            float safeTop = Screen.height - safeArea.yMax;
            offsetY = safeTop + (safeArea.height - UiReferenceHeight * scale) * 0.5f;
        }

        private static Vector3 CurrentPackHome
        {
            get
            {
                if (!IsPortraitUi) return PackHome;
                Camera camera = Camera.main;
                if (camera == null) return PackHome;
                Vector3 cardScreenPosition = camera.WorldToScreenPoint(CardHome);
                float packDepth = camera.WorldToScreenPoint(PackHome).z;
                return camera.ScreenToWorldPoint(new Vector3(cardScreenPosition.x, cardScreenPosition.y, packDepth));
            }
        }
        private static Vector2 ScreenToReferencePoint(Vector2 screenPoint)
        {
            GetUiLayout(out float scale, out float offsetX, out float offsetY);
            if (scale <= 0f) return Vector2.zero;
            return new Vector2((screenPoint.x - offsetX) / scale, (screenPoint.y - offsetY) / scale);
        }

        private void OnGUI()
        {
            GetUiLayout(out float scale, out float offsetX, out float offsetY);
            if (settingsOpen)
            {
                DrawSettingsOverlay(scale, offsetX, offsetY);
                return;
            }
            Vector2 raw = Event.current.mousePosition;
            if (inspectedDeckIndex >= 0)
            {
                DrawDeckInspectionControls(scale, offsetX, offsetY);
                HandleDeckPointer(raw, Event.current);
                return;
            }
            if (inspectedPackChoice != null)
            {
                DrawActualPackContentsOverlay(scale, offsetX, offsetY);
                HandleDeckPointer(raw, Event.current);
                return;
            }
            if (DrawSettingsButton(scale, offsetX, offsetY)) return;


            DrawScore(scale, offsetX, offsetY);
            DrawControlGuide(scale, offsetX, offsetY);
            DrawDeck(scale, offsetX, offsetY);
            DrawScorePopups(scale, offsetX, offsetY);
            DrawPackTearGuide(scale, offsetX, offsetY);

            if (phase == RevealPhase.PackChoice)
            {
                DrawPackChoice(scale, offsetX, offsetY);
                HandleDeckPointer(raw, Event.current);
                return;
            }
            if (phase == RevealPhase.GameOver || phase == RevealPhase.RunCleared)
            {
                DrawRunEndOverlay(scale, offsetX, offsetY);
                HandleDeckPointer(raw, Event.current);
                return;
            }
            if (phase == RevealPhase.Pack && DrawActivePackContentsButton(scale, offsetX, offsetY)) return;
            if (HandleDeckPointer(raw, Event.current)) return;
            HandlePointer(new Vector2((raw.x - offsetX) / scale, (raw.y - offsetY) / scale), Event.current);
        }
        private void HandlePointer(Vector2 point, Event inputEvent)
        {
            if (phase == RevealPhase.Animating)
            {
                HandleAnimatingCardSwipe(point, inputEvent);
                return;
            }
            if (inputEvent.type == EventType.MouseDown
                && new Rect(0f, 0f, UiReferenceWidth, UiReferenceHeight).Contains(point))
            {
                dragStart = point;
                dragDelta = Vector2.zero;
                Rect packZone = IsPortraitUi
                    ? new Rect(145f, 175f, 430f, 580f + PortraitExtraHeight * 0.45f) : PackTearZone;
                Rect cardZone = IsPortraitUi
                    ? new Rect(145f, 185f, 430f, 800f + PortraitExtraHeight * 0.85f) : CardGestureZone;
                bool objectGesture = phase == RevealPhase.Pack ? packZone.Contains(point) : cardZone.Contains(point);
                if (objectGesture) BeginObjectGesture(); else BeginInspection();
                inputEvent.Use();
                return;
            }
            if (inputEvent.type == EventType.MouseDrag)
            {
                dragDelta = point - dragStart;
                if (inspectionDragging) UpdateInspectionRotation();
                else if (gestureDragging) UpdateObjectGesture();
                else return;
                inputEvent.Use();
                return;
            }
            if (inputEvent.type != EventType.MouseUp) return;
            if (inspectionDragging)
            {
                inspectionDragging = false;
                Transform releasedTarget = inspectionTarget;
                inspectionTarget = null;
                BeginInspectionReturn(releasedTarget);
            }
            else if (gestureDragging) { gestureDragging = false; CompleteObjectGesture(); }
            else return;
            inputEvent.Use();
        }

        private void HandleAnimatingCardSwipe(Vector2 point, Event inputEvent)
        {
            if (!cardTransitionActive) return;
            Rect cardZone = IsPortraitUi
                ? new Rect(145f, 185f, 430f, 800f + PortraitExtraHeight * 0.85f) : CardGestureZone;
            if (inputEvent.type == EventType.MouseDown && cardZone.Contains(point))
            {
                dragStart = point;
                dragDelta = Vector2.zero;
                transitionDragActive = true;
                transitionSwipeCommitted = false;
                inputEvent.Use();
                return;
            }
            if (inputEvent.type == EventType.MouseDrag && transitionDragActive)
            {
                dragDelta = point - dragStart;
                if (!transitionSwipeCommitted && Mathf.Abs(dragDelta.x) >= 70f)
                {
                    transitionSwipeCommitted = true;
                    queuedSwipeDirection = Mathf.Sign(dragDelta.x);
                    queuedCardSwipes = Mathf.Min(queuedCardSwipes + 1, cards.Count);
                    if (activeSlidingCard != null) activeSlidingCard.AccelerateSlideAway();
                }
                inputEvent.Use();
                return;
            }
            if (inputEvent.type == EventType.MouseUp && transitionDragActive)
            {
                transitionDragActive = false;
                inputEvent.Use();
            }
        }

        private void BeginObjectGesture()
        {
            if (inspectionReturnRoutine != null)
            {
                StopCoroutine(inspectionReturnRoutine);
                inspectionReturnRoutine = null;
            }
            gestureDragging = true;
            inspectionDragging = false;
            Transform target = CurrentGestureTarget();
            gestureStartPosition = target.position;
            gestureStartRotation = target.rotation;
            if (phase == RevealPhase.Pack) tearVisual.BeginGesture();
        }

        private void BeginInspection()
        {
            inspectionTarget = CurrentInspectionTarget();
            if (inspectionTarget == null) return;
            if (inspectionReturnRoutine != null)
            {
                StopCoroutine(inspectionReturnRoutine);
                inspectionReturnRoutine = null;
            }
            inspectionDragging = true;
            gestureDragging = false;
            inspectionStartRotation = inspectionTarget.rotation;
            if (inspectionTarget == cardStack)
                inspectionPivotWorld = inspectionTarget.position
                    + inspectionStartRotation * CardHome;
        }

        private void UpdateInspectionRotation()
        {
            if (inspectionTarget == null) return;
            Quaternion rotation = Quaternion.Euler(-dragDelta.y * 0.24f,
                dragDelta.x * 0.28f, 0f) * inspectionStartRotation;
            inspectionTarget.rotation = rotation;
            if (inspectionTarget == cardStack)
                inspectionTarget.position = inspectionPivotWorld - rotation * CardHome;
        }

        private void BeginInspectionReturn(Transform target)
        {
            if (target == null) return;
            if (inspectionReturnRoutine != null) StopCoroutine(inspectionReturnRoutine);
            Vector3 restPosition = target == pack.transform ? CurrentPackHome : Vector3.zero;
            inspectionReturnRoutine = StartCoroutine(ReturnInspectionPose(target, restPosition));
        }

        private IEnumerator ReturnInspectionPose(Transform target, Vector3 restPosition)
        {
            Vector3 startPosition = target.position;
            Quaternion startRotation = target.rotation;
            const float duration = 0.38f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                if (target == null) break;
                float u = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                target.position = Vector3.Lerp(startPosition, restPosition, u);
                target.rotation = Quaternion.Slerp(startRotation, Quaternion.identity, u);
                yield return null;
            }
            if (target != null)
            {
                target.position = restPosition;
                target.rotation = Quaternion.identity;
            }
            inspectionReturnRoutine = null;
        }

        private void UpdateObjectGesture()
        {
            if (phase == RevealPhase.Pack)
            {
                tearVisual.PreviewTilt(dragDelta);
                if (dragDelta.magnitude >= 145f) { gestureDragging = false; StartCoroutine(RemovePack(dragDelta)); }
            }
            else if (phase == RevealPhase.CardFront)
            {
                CardVisual card = cards[cardIndex];
                card.transform.position = gestureStartPosition + new Vector3(dragDelta.x * 0.008f, dragDelta.y * -0.004f, 0f);
                card.transform.rotation = Quaternion.Euler(0f, 0f, dragDelta.x * -0.045f) * gestureStartRotation;
            }
        }

        private void CompleteObjectGesture()
        {
            if (phase == RevealPhase.Pack) tearVisual.CancelGesture();
            else if (phase == RevealPhase.CardBack)
            {
                if (dragDelta.magnitude < 80f) StartCoroutine(FlipCard());
            }
            else if (phase == RevealPhase.CardFront)
            {
                if (Mathf.Abs(dragDelta.x) >= 115f) StartCoroutine(MoveToNextCard(Mathf.Sign(dragDelta.x)));
                else RestoreGesturePose(cards[cardIndex].transform);
            }
        }

        private void RestoreGesturePose(Transform target) { target.position = gestureStartPosition; target.rotation = gestureStartRotation; }
        private Transform CurrentGestureTarget() { return phase == RevealPhase.Pack ? pack.transform : cards[cardIndex].transform; }
        private Transform CurrentInspectionTarget()
        {
            if (phase == RevealPhase.Pack) return pack.transform;
            if (phase == RevealPhase.CardBack || phase == RevealPhase.CardFront) return cardStack;
            return null;
        }

        private Material GetTextureMaterial(string key, Texture2D texture, bool transparent, int queueOffset = 0)
        {
            if (texture == null) return null;
            if (materials.TryGetValue(key, out Material cached)) return cached;
            Material material = CreateTextureMaterial(key, texture, transparent, queueOffset);
            materials.Add(key, material);
            return material;
        }

        private static void ApplyTextureOrFallback(Material material, Texture2D texture, Texture2D fallback)
        {
            Texture2D selectedTexture = texture != null ? texture : fallback;
            material.mainTexture = selectedTexture;
            material.mainTextureScale = Vector2.one;
            material.mainTextureOffset = Vector2.zero;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", selectedTexture);
                material.SetTextureScale("_BaseMap", Vector2.one);
                material.SetTextureOffset("_BaseMap", Vector2.zero);
            }
        }
        private Material CreateTextureMaterial(string key, Texture texture, bool transparent, int queueOffset)
        {
            Shader shader = Shader.Find(transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find(transparent ? "Unlit/Transparent" : "Standard");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null)
                throw new InvalidOperationException("CardOpen could not load a runtime texture shader. Check Always Included Shaders.");
            Material material = new Material(shader) { name = key, color = Color.white };
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", transparent ? 0f : 0.24f);
            if (transparent)
            {
                material.SetOverrideTag("RenderType", "Transparent");
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
                if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
                if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", 5f);
                if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", 10f);
                if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetShaderPassEnabled("ShadowCaster", false);
                material.renderQueue = 3000 + queueOffset;
            }
            return material;
        }
        private void CreateBackground(Camera camera)
        {
            Texture2D texture = Resources.Load<Texture2D>("Textures/SimpleBackground");
            if (texture == null || camera == null) return;

            background = CreateQuadObject("2D Background");
            LayoutBackground(camera);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null)
                throw new InvalidOperationException("CardOpen could not load the background shader. Check Always Included Shaders.");
            Material material = new Material(shader) { name = "Simple 2D Background", mainTexture = texture };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.renderQueue = 1000;
            MeshRenderer renderer = background.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = material;
        }

        private void LayoutBackground(Camera camera)
        {
            if (background == null || camera == null) return;
            const float distance = 24f;
            float height = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            background.transform.position = camera.transform.position + camera.transform.forward * distance;
            background.transform.rotation = Quaternion.LookRotation(-camera.transform.forward, camera.transform.up);
            background.transform.localScale = new Vector3(height * camera.aspect * 1.05f, height * 1.05f, 1f);
        }


        private static GameObject CreateQuadObject(string objectName)
        {
            GameObject quadObject = new GameObject(objectName);
            MeshFilter filter = quadObject.AddComponent<MeshFilter>();
            quadObject.AddComponent<MeshRenderer>();

            Mesh mesh = new Mesh { name = objectName + " Mesh" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            return quadObject;
        }

        private Material GetTextureMaterial(string key, string resourcePath, bool transparent, int queueOffset = 0)
        {
            if (materials.TryGetValue(key, out Material cached)) return cached;
            Material material = CreateTextureMaterial(key, Resources.Load<Texture2D>(resourcePath), transparent, queueOffset);
            materials.Add(key, material);
            return material;
        }

        private Material GetMaterial(string key, Color color, float smoothness)
        {
            if (materials.TryGetValue(key, out Material material)) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
                throw new InvalidOperationException("CardOpen could not load a runtime material shader. Check Always Included Shaders.");
            material = new Material(shader) { name = key, color = color };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", smoothness * 0.25f);
            materials.Add(key, material);
            return material;
        }

        private void AwardCurrentCardScore()
        {
            if (cardIndex < 0 || cardIndex >= currentPackCards.Count) return;
            StoredCard currentCard = currentPackCards[cardIndex];
            ApplyDeckCardTransformEffects(currentCard);
            int earnedScore;
            string reason;
            Color popupColor;
            switch (currentCard.Rarity)
            {
                case global::CardRarity.Uncommon:
                    earnedScore = 200; reason = Ui("\uACE0\uAE09 \uCE74\uB4DC", "Uncommon card"); popupColor = new Color(0.45f, 1f, 0.72f); break;
                case global::CardRarity.Rare:
                    earnedScore = 300; reason = Ui("\uD76C\uADC0 \uCE74\uB4DC", "Rare card"); popupColor = new Color(0.72f, 0.88f, 1f); break;
                case global::CardRarity.Epic:
                    earnedScore = 500; reason = Ui("\uC601\uC6C5 \uCE74\uB4DC", "Epic card"); popupColor = new Color(1f, 0.73f, 0.22f); break;
                case global::CardRarity.Legendary:
                    earnedScore = 1000; reason = Ui("\uC804\uC124 \uCE74\uB4DC", "Legendary card"); popupColor = new Color(1f, 0.82f, 0.28f); break;
                default:
                    earnedScore = 100; reason = Ui("\uC77C\uBC18 \uCE74\uB4DC", "Common card"); popupColor = Color.white; break;
            }

            int baseCardScoreTotal = earnedScore * (currentCard.IsHolographic ? 2 : 1);
            AddScorePopup(reason + "\n+" + earnedScore + Ui("\uC810", " pts"), popupColor,
                Time.unscaledTime, scorePopups.Count, earnedScore);
            RegisterOtherCardScoreEvent(currentCard);
            if (currentCard.IsHolographic)
            {
                AddScorePopup(Ui("\uD640\uB85C\uADF8\uB7A8!\n+", "Holographic!\n+") + earnedScore + Ui("\uC810", " pts"), new Color(0.55f, 0.9f, 1f),
                    Time.unscaledTime + 0.22f, scorePopups.Count, earnedScore);
                RegisterOtherCardScoreEvent(currentCard);
            }
            TriggerDeckAbilities(currentCard, baseCardScoreTotal);
            previousRevealedCard = currentCard;
        }

        private void TriggerDeckAbilities(StoredCard revealedCard, int baseCardScoreTotal)
        {
            PrepareNatureAbilityChain(revealedCard);
            TriggerPackCardGenerationAbilities(revealedCard);
            PrepareStackBonusTriggers(revealedCard);
            TriggerStackCardGenerationAbilities();
            AccumulatePerPackEffects(revealedCard);
            int triggerRequirementCount = CountTriggeredDeckEffects(revealedCard);
            AccumulateDeckScoreBonuses(revealedCard, triggerRequirementCount);
            int triggeredCount = 0;
            int flatAbilityScoreTotal = 0;

            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard abilityOwner = GetAbilityOwnerAt(i);
                if (abilityOwner == null || abilityOwner.Data == null || abilityOwner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(abilityOwner);
                for (int j = 0; j < abilityOwner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = abilityOwner.Data.DeckAbilities[j];
                    if (ability == null || !IsFlatScoreEffect(ability.Effect)) continue;
                    if (ability.Effect == global::DeckAbilityEffect.AccumulateFlatScorePerDraw)
                    {
                        if (!abilityOwner.AccumulatedFlatScoreByAbility.TryGetValue(j, out int accumulatedFlatScore)
                            || accumulatedFlatScore <= 0) continue;
                        flatAbilityScoreTotal += accumulatedFlatScore;
                        AddDeckAbilityPopup(abilityOwner, ability, accumulatedFlatScore, 0, triggeredCount++);
                        continue;
                    }
                    int flatScore = GetFlatDeckAbilityScore(ability, revealedCard);
                    if (flatScore <= 0) continue;

                    if (ability.Effect == global::DeckAbilityEffect.TriggerScoreAtStackThreshold)
                    {
                        for (int copy = 0; copy < effectiveCopies; copy++)
                        {
                            int triggerCount = GetStackTriggerCount(abilityOwner, j, copy);
                            for (int trigger = 0; trigger < triggerCount; trigger++)
                            {
                                flatAbilityScoreTotal += flatScore;
                                AddDeckAbilityPopup(abilityOwner, ability, flatScore, copy, triggeredCount++);
                            }
                        }
                        continue;
                    }

                    int abilityTriggerCount = GetDeckAbilityTriggerCount(
                        ability, abilityOwner, revealedCard, triggerRequirementCount);
                    if (abilityTriggerCount <= 0) continue;
                    for (int copy = 0; copy < effectiveCopies; copy++)
                    {
                        for (int trigger = 0; trigger < abilityTriggerCount; trigger++)
                        {
                            flatAbilityScoreTotal += flatScore;
                            AddDeckAbilityPopup(abilityOwner, ability, flatScore, copy, triggeredCount++);
                        }
                    }
                }
            }

            int scoreBeforePercentageBonus = baseCardScoreTotal + flatAbilityScoreTotal;
            float scoreBonusEfficiency = GetScoreBonusEfficiencyMultiplier();
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard abilityOwner = GetAbilityOwnerAt(i);
                if (abilityOwner == null || abilityOwner.Data == null || abilityOwner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(abilityOwner);
                for (int j = 0; j < abilityOwner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = abilityOwner.Data.DeckAbilities[j];
                    if (ability == null) continue;
                    if (IsResonatingRuneAbility(abilityOwner, ability,
                        global::DeckAbilityEffect.AddTriggeredScorePercent)) continue;

                    if (ability.Effect == global::DeckAbilityEffect.GrantTemporaryPercentForNextDraws)
                    {
                        if (!abilityOwner.RemainingDrawsByAbility.TryGetValue(j, out int remainingDraws)
                            || remainingDraws <= 0 || ability.PercentBonus <= 0f) continue;
                        int temporaryBonusScore = Mathf.RoundToInt(
                            scoreBeforePercentageBonus * ability.PercentBonus * 0.01f * scoreBonusEfficiency);
                        if (temporaryBonusScore > 0)
                        {
                            for (int copy = 0; copy < effectiveCopies; copy++)
                                AddDeckAbilityPopup(abilityOwner, ability, temporaryBonusScore, copy, triggeredCount++);
                        }
                        abilityOwner.RemainingDrawsByAbility[j] = remainingDraws - 1;
                        continue;
                    }

                    if (ability.Effect == global::DeckAbilityEffect.TriggerPercentAtStackThreshold
                        || ability.Effect == global::DeckAbilityEffect.TriggerPercentEveryDrawCount)
                    {
                        if (ability.PercentBonus <= 0f) continue;
                        int stackBonusScore = Mathf.RoundToInt(
                            scoreBeforePercentageBonus * ability.PercentBonus * 0.01f * scoreBonusEfficiency);
                        if (stackBonusScore <= 0) continue;
                        for (int copy = 0; copy < effectiveCopies; copy++)
                        {
                            int triggerCount = GetStackTriggerCount(abilityOwner, j, copy);
                            for (int trigger = 0; trigger < triggerCount; trigger++)
                                AddDeckAbilityPopup(abilityOwner, ability, stackBonusScore, copy, triggeredCount++);
                        }
                        continue;
                    }

                    if (!DoesDeckAbilityTrigger(ability, abilityOwner, revealedCard, triggerRequirementCount)) continue;
                    if (ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusPerDraw
                        || ability.Effect == global::DeckAbilityEffect.AccumulatePercentAtStackThreshold)
                    {
                        if (!abilityOwner.AccumulatedPercentByAbility.TryGetValue(j, out float accumulatedPercent)
                            || accumulatedPercent <= 0f) continue;
                        int accumulatedBonusScore = Mathf.RoundToInt(
                            scoreBeforePercentageBonus * accumulatedPercent * 0.01f * scoreBonusEfficiency);
                        if (accumulatedBonusScore > 0)
                            AddDeckAbilityPopup(abilityOwner, ability, accumulatedBonusScore, 0, triggeredCount++);
                        continue;
                    }

                    if (ability.Effect != global::DeckAbilityEffect.AddTriggeredScorePercent
                        || ability.PercentBonus <= 0f) continue;
                    int bonusScore = Mathf.RoundToInt(
                        scoreBeforePercentageBonus * ability.PercentBonus * 0.01f * scoreBonusEfficiency);
                    if (bonusScore <= 0) continue;
                    for (int copy = 0; copy < effectiveCopies; copy++)
                        AddDeckAbilityPopup(abilityOwner, ability, bonusScore, copy, triggeredCount++);
                }
            }

            float runeResonancePercent = GetRuneResonanceValue(revealedCard,
                global::DeckAbilityEffect.AddTriggeredScorePercent,
                out StoredCard runePopupOwner, out global::CardDeckAbility runePopupAbility);
            if (runeResonancePercent > 0f && runePopupOwner != null)
            {
                int runeBonusScore = Mathf.RoundToInt(
                    scoreBeforePercentageBonus * runeResonancePercent * 0.01f * scoreBonusEfficiency);
                if (runeBonusScore > 0)
                    AddDeckAbilityPopup(runePopupOwner, runePopupAbility, runeBonusScore, 0, triggeredCount++);
            }
            ActivateTemporaryDrawBonuses(revealedCard);
            ClearNatureAbilityChain();
        }

        private void PrepareNatureAbilityChain(StoredCard revealedCard)
        {
            ClearNatureAbilityChain();
            foreach (StoredCard pendingSource in pendingPackOpenNatureSources)
            {
                if (pendingSource != null && pendingSource.Data != null
                    && pendingSource.Data.HasTag(global::CardTag.Nature))
                {
                    AddNaturallyTriggeredNatureCount(pendingSource, GetEffectiveDeckCopyCount(pendingSource));
                }
            }
            pendingPackOpenNatureSources.Clear();
            if (revealedCard != null)
            {
                for (int i = 0; i < GetAbilityOwnerCount(); i++)
                {
                    StoredCard owner = GetAbilityOwnerAt(i);
                    if (owner == null || owner.Data == null
                        || !owner.Data.HasTag(global::CardTag.Nature)
                        || owner.Data.DeckAbilities == null) continue;
                    for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                    {
                        global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                        if (!IsNatureChainEligibleAbility(ability)) continue;
                        int naturalTriggerCount = GetNormalDeckAbilityTriggerCount(
                            ability, owner, revealedCard);
                        if (naturalTriggerCount <= 0) continue;
                        AddNaturallyTriggeredNatureCount(owner,
                            GetEffectiveDeckCopyCount(owner) * naturalTriggerCount);
                    }
                }
            }

            if (natureAbilityChainTriggerCount == 0) return;
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner != null && owner.Data != null
                    && owner.Data.HasTag(global::CardTag.Nature)
                    && natureAbilityChainTriggerCount > GetNaturallyTriggeredNatureCount(owner)
                    && HasNatureChainTargetAbility(owner.Data))
                {
                    natureAbilityChainActive = true;
                    return;
                }
            }
        }

        private void ClearNatureAbilityChain()
        {
            natureAbilityChainActive = false;
            natureAbilityChainTriggerCount = 0;
            naturallyTriggeredNatureCounts.Clear();
        }

        private void AddNaturallyTriggeredNatureCount(StoredCard owner, int count)
        {
            if (owner == null || count <= 0) return;
            naturallyTriggeredNatureCounts.TryGetValue(owner, out int currentCount);
            naturallyTriggeredNatureCounts[owner] = currentCount + count;
            natureAbilityChainTriggerCount += count;
        }

        private int GetNaturallyTriggeredNatureCount(StoredCard owner)
        {
            if (owner == null) return 0;
            return naturallyTriggeredNatureCounts.TryGetValue(owner, out int count) ? count : 0;
        }

        private static bool HasNatureChainTargetAbility(global::CardData data)
        {
            if (data == null || data.DeckAbilities == null) return false;
            for (int i = 0; i < data.DeckAbilities.Count; i++)
                if (IsNatureChainEligibleAbility(data.DeckAbilities[i])) return true;
            return false;
        }

        private static bool IsNatureChainEligibleAbility(global::CardDeckAbility ability)
        {
            return ability != null && ability.CanBeTriggeredByNatureChain();
        }

        private static bool IsNatureChainEligibleEffect(global::DeckAbilityEffect effect)
        {
            return global::CardDeckAbility.IsNatureChainEffectSupported(effect);
        }

        private void ActivateTemporaryDrawBonuses(StoredCard revealedCard)
        {
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null
                        || ability.Effect != global::DeckAbilityEffect.GrantTemporaryPercentForNextDraws
                        || ability.DurationDrawCount <= 0
                        || !DoesDeckAbilityTrigger(ability, owner, revealedCard)) continue;
                    owner.RemainingDrawsByAbility.TryGetValue(j, out int remainingDraws);
                    owner.RemainingDrawsByAbility[j] = remainingDraws + ability.DurationDrawCount;
                }
            }
        }
        private int CountTriggeredDeckEffects(StoredCard revealedCard)
        {
            int count = 0;
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null || ability.Trigger == global::DeckAbilityTrigger.TriggeredEffectsAtLeastThree
                        || ability.Effect == global::DeckAbilityEffect.AddNextPackCards) continue;
                    if (ability.Effect == global::DeckAbilityEffect.GrantTemporaryPercentForNextDraws)
                    {
                        if (owner.RemainingDrawsByAbility.TryGetValue(j, out int remainingDraws)
                            && remainingDraws > 0 && ability.PercentBonus > 0f) count += effectiveCopies;
                        continue;
                    }
                    if (ability.Effect == global::DeckAbilityEffect.AccumulateFlatScorePerDraw)
                    {
                        if (owner.AccumulatedFlatScoreByAbility.TryGetValue(j, out int accumulatedFlatScore)
                            && accumulatedFlatScore > 0) count++;
                        continue;
                    }
                    if (ability.Effect == global::DeckAbilityEffect.AccumulatePercentAtStackThreshold)
                    {
                        if (owner.AccumulatedPercentByAbility.TryGetValue(j, out float accumulatedPercent)
                            && accumulatedPercent > 0f) count++;
                        continue;
                    }
                    if (IsStackThresholdEffect(ability.Effect))
                    {
                        for (int copy = 0; copy < effectiveCopies; copy++)
                            count += GetStackTriggerCount(owner, j, copy);
                        continue;
                    }
                    if (ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusEfficiencyByNumber)
                    {
                        owner.AccumulatedPercentByAbility.TryGetValue(j, out float currentEfficiency);
                        float maximumEfficiency = ability.MaximumPercent > 0f ? ability.MaximumPercent : 100f;
                        if (ability.NumberMultiplier > 0 && currentEfficiency < maximumEfficiency
                            && DoesDeckAbilityTrigger(ability, owner, revealedCard))
                            count += effectiveCopies;
                        continue;
                    }
                    bool hasScoreValue = IsFlatScoreEffect(ability.Effect)
                        ? GetFlatDeckAbilityScore(ability, revealedCard) > 0
                        : (ability.Effect == global::DeckAbilityEffect.AddTriggeredScorePercent
                            || ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusPerDraw)
                            && ability.PercentBonus > 0f;
                    if (hasScoreValue)
                        count += effectiveCopies * GetDeckAbilityTriggerCount(
                            ability, owner, revealedCard);
                }
            }
            return count;
        }

        private void PrepareStackBonusTriggers(StoredCard revealedCard)
        {
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null) continue;
                owner.TriggeredStackCountsThisDraw.Clear();
                if (owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    bool countsDraws = ability != null
                        && ability.Effect == global::DeckAbilityEffect.TriggerPercentEveryDrawCount;
                    if (ability == null || !IsStackThresholdEffect(ability.Effect)
                        || ability.StackThreshold <= 0 || (!countsDraws && ability.NumberMultiplier <= 0)
                        || !DoesDeckAbilityTrigger(ability, owner, revealedCard)) continue;
                    int gainedStacks = countsDraws
                        ? 1
                        : Mathf.Max(0, revealedCard.Number * ability.NumberMultiplier);
                    int preparedTriggerCount = 0;
                    owner.PerPackTriggerCountByAbility.TryGetValue(j, out int usedThisPack);
                    for (int copy = 0; copy < effectiveCopies; copy++)
                    {
                        int stackKey = GetAbilityCopyKey(j, copy);
                        owner.StackByAbilityCopy.TryGetValue(stackKey, out int currentStacks);
                        int nextStacks = currentStacks + gainedStacks;
                        int triggerCount = nextStacks / ability.StackThreshold;
                        if (ability.Effect == global::DeckAbilityEffect.AddSpecificCardAtStackThreshold
                            && ability.MaxTriggersPerPack > 0)
                        {
                            int availableTriggers = Mathf.Max(0,
                                ability.MaxTriggersPerPack - usedThisPack - preparedTriggerCount);
                            triggerCount = Mathf.Min(triggerCount, availableTriggers);
                            owner.StackByAbilityCopy[stackKey] =
                                nextStacks - triggerCount * ability.StackThreshold;
                        }
                        else
                        {
                            owner.StackByAbilityCopy[stackKey] = nextStacks % ability.StackThreshold;
                        }
                        if (triggerCount <= 0) continue;
                        owner.TriggeredStackCountsThisDraw[stackKey] = triggerCount;
                        preparedTriggerCount += triggerCount;
                    }
                }
            }
        }
        private void AccumulatePerPackEffects(StoredCard revealedCard)
        {
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null) continue;
                    if (ability.Effect == global::DeckAbilityEffect.AccumulateFlatScorePerDraw)
                    {
                        if (ability.Score <= 0 || !DoesDeckAbilityTrigger(ability, owner, revealedCard)) continue;
                        owner.AccumulatedFlatScoreByAbility.TryGetValue(j, out int accumulatedScore);
                        owner.AccumulatedFlatScoreByAbility[j] =
                            accumulatedScore + ability.Score * effectiveCopies;
                        continue;
                    }

                    if (ability.Effect != global::DeckAbilityEffect.AccumulatePercentAtStackThreshold
                        || ability.PercentBonus <= 0f) continue;
                    int totalTriggerCount = 0;
                    for (int copy = 0; copy < effectiveCopies; copy++)
                        totalTriggerCount += GetStackTriggerCount(owner, j, copy);
                    if (totalTriggerCount <= 0) continue;
                    owner.AccumulatedPercentByAbility.TryGetValue(j, out float accumulatedPercent);
                    owner.AccumulatedPercentByAbility[j] =
                        accumulatedPercent + ability.PercentBonus * totalTriggerCount;
                }
            }
        }
        private static bool IsStackThresholdEffect(global::DeckAbilityEffect effect)
        {
            return effect == global::DeckAbilityEffect.TriggerPercentAtStackThreshold
                || effect == global::DeckAbilityEffect.TriggerScoreAtStackThreshold
                || effect == global::DeckAbilityEffect.AccumulatePercentAtStackThreshold
                || effect == global::DeckAbilityEffect.AddSpecificCardAtStackThreshold
                || effect == global::DeckAbilityEffect.TriggerPercentEveryDrawCount;
        }

        private static int GetStackTriggerCount(StoredCard owner, int abilityIndex, int copyIndex)
        {
            if (owner == null) return 0;
            owner.TriggeredStackCountsThisDraw.TryGetValue(
                GetAbilityCopyKey(abilityIndex, copyIndex), out int triggerCount);
            return Mathf.Max(0, triggerCount);
        }

        private static int GetAbilityCopyKey(int abilityIndex, int copyIndex)
        {
            return abilityIndex * 100 + copyIndex;
        }
        private void AccumulateDeckScoreBonuses(StoredCard revealedCard, int triggerRequirementCount)
        {
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null
                        || !DoesDeckAbilityTrigger(ability, owner, revealedCard, triggerRequirementCount)) continue;
                    owner.AccumulatedPercentByAbility.TryGetValue(j, out float accumulatedPercent);
                    if (ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusEfficiencyByNumber)
                    {
                        if (ability.NumberMultiplier <= 0) continue;
                        float gainedEfficiency = revealedCard.Number * ability.NumberMultiplier * effectiveCopies;
                        float maximumEfficiency = ability.MaximumPercent > 0f ? ability.MaximumPercent : 100f;
                        owner.AccumulatedPercentByAbility[j] =
                            Mathf.Min(maximumEfficiency, accumulatedPercent + gainedEfficiency);
                        continue;
                    }
                    if (ability.Effect != global::DeckAbilityEffect.AccumulateScoreBonusPerDraw
                        || ability.PercentBonus <= 0f) continue;
                    owner.AccumulatedPercentByAbility[j] =
                        accumulatedPercent + ability.PercentBonus * effectiveCopies;
                }
            }
        }

        private static bool IsFlatScoreEffect(global::DeckAbilityEffect effect)
        {
            return effect == global::DeckAbilityEffect.AddScore
                || effect == global::DeckAbilityEffect.AddRevealedNumberTimesScore
                || effect == global::DeckAbilityEffect.TriggerScoreAtStackThreshold
                || effect == global::DeckAbilityEffect.AccumulateFlatScorePerDraw;
        }

        private static int GetFlatDeckAbilityScore(global::CardDeckAbility ability, StoredCard revealedCard)
        {
            if (ability.Effect == global::DeckAbilityEffect.AddRevealedNumberTimesScore)
                return Mathf.Max(0, revealedCard.Number * ability.NumberMultiplier);
            return Mathf.Max(0, ability.Score);
        }

        private void ResetOncePerPackAbilityUsage()
        {
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null) continue;
                owner.UsedOncePerPackAbilityCopies.Clear();
                owner.PerPackTriggerCountByAbility.Clear();
            }
        }

        private void TriggerPackCardGenerationAbilities(StoredCard revealedCard)
        {
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null || ability.Effect != global::DeckAbilityEffect.AddRandomCommonCardToPackEnd
                        || !DoesDeckAbilityTrigger(ability, owner, revealedCard)) continue;
                    for (int copy = 0; copy < effectiveCopies; copy++)
                    {
                        int usageKey = GetAbilityCopyKey(j, copy);
                        if (owner.UsedOncePerPackAbilityCopies.Contains(usageKey)) continue;
                        if (!AppendRandomCommonCardToCurrentPack()) continue;
                        owner.UsedOncePerPackAbilityCopies.Add(usageKey);
                    }
                }
            }
        }

        private void TriggerStackCardGenerationAbilities()
        {
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null
                        || ability.Effect != global::DeckAbilityEffect.AddSpecificCardAtStackThreshold
                        || ability.GeneratedCard == null) continue;
                    owner.PerPackTriggerCountByAbility.TryGetValue(j, out int usedThisPack);
                    int maximumTriggers = ability.MaxTriggersPerPack > 0
                        ? ability.MaxTriggersPerPack
                        : int.MaxValue;
                    for (int copy = 0; copy < effectiveCopies && usedThisPack < maximumTriggers; copy++)
                    {
                        int triggerCount = GetStackTriggerCount(owner, j, copy);
                        for (int trigger = 0; trigger < triggerCount && usedThisPack < maximumTriggers; trigger++)
                        {
                            if (!AppendSpecificCardToCurrentPack(ability.GeneratedCard)) break;
                            usedThisPack++;
                        }
                    }
                    owner.PerPackTriggerCountByAbility[j] = usedThisPack;
                }
            }
        }

        private bool AppendRandomCommonCardToCurrentPack()
        {
            global::CardPackEntry entry = DrawCommonCard();
            return AppendCardToCurrentPack(entry);
        }

        private bool AppendRandomTaggedCardToCurrentPack(global::CardTag tag)
        {
            if (activePackData == null) return false;
            global::CardPackEntry entry = activePackData.DrawRandomCard(tag);
            return AppendCardToCurrentPack(entry);
        }

        private bool AppendSpecificCardToCurrentPack(global::CardData data)
        {
            if (data == null) return false;
            return AppendCardToCurrentPack(new global::CardPackEntry
            {
                Card = data,
                Number = Random.Range(1, 7),
                Color = (global::CardColor)Random.Range(0, 5),
                InclusionRate = 100f
            });
        }

        private bool AppendCardToCurrentPack(global::CardPackEntry entry)
        {
            if (entry == null || entry.Card == null) return false;
            global::CardData data = entry.Card;
            int index = cards.Count;

            GameObject cardObject = new GameObject("Card - " + data.Name + " (Generated)");
            cardObject.transform.SetParent(cardStack, true);
            CardVisual visual = cardObject.AddComponent<CardVisual>();
            Material attributeMaterial = GetTextureMaterial("Attribute_" + entry.AttributeAssetKey,
                "CardAssets/Attributes/Attribute" + entry.AttributeAssetKey, false);
            Material rarityPatternMaterial = GetTextureMaterial("Pattern_" + data.RarityAssetKey,
                "CardAssets/Rarities/Pattern" + data.RarityAssetKey, true, 0);
            string costAsset = entry.DisplayNumber == 6 ? "CostSigma" : "Cost" + entry.DisplayNumber;
            Material costMaterial = GetTextureMaterial("Cost_" + entry.DisplayNumber,
                "CardAssets/Costs/" + costAsset, true, 20);
            Material illustrationMaterial = GetTextureMaterial("CardImage_" + data.GetHashCode(), data.Image, true, 10);
            visual.BuildFromData(data, entry.Color, attributeMaterial,
                GetTextureMaterial("CardBack", "CardAssets/Attributes/AttributeBackRemasterPurple", false),
                rarityPatternMaterial, illustrationMaterial, costMaterial, font, IsEnglishUi);
            bool isHolographic = currentPackIsHolographic || Random.value < 0.1f;
            if (isHolographic) visual.EnableHologram();
            visual.PrepareFaceUp(CardHome + new Vector3(0f, index * 0.025f, index * 0.065f),
                CurrentRevealedCardScale, index * 0.35f);
            // Cards generated by deck abilities are appended after the pack is already open.
            // Keep them visible immediately so the physical card stack grows at trigger time.
            visual.gameObject.SetActive(true);
            visual.SetFaceDetailsVisible(true);
            cards.Add(visual);
            currentPackCards.Add(new StoredCard
            {
                Name = data.Name,
                Data = data,
                Rarity = data.Rare,
                Color = entry.Color,
                Number = entry.DisplayNumber,
                IsHolographic = isHolographic
            });
            return true;
        }
        private global::CardPackEntry DrawCommonCard()
        {
            if (activePackData != null)
            {
                global::CardPackEntry entry = activePackData.DrawRandomCard(global::CardRarity.Common);
                if (entry != null) return entry;
            }

            if (fallbackCards == null || fallbackCards.Length == 0)
                fallbackCards = Resources.LoadAll<global::CardData>(string.Empty);
            int commonCount = 0;
            for (int i = 0; fallbackCards != null && i < fallbackCards.Length; i++)
                if (fallbackCards[i] != null && fallbackCards[i].Rare == global::CardRarity.Common) commonCount++;
            if (commonCount <= 0) return null;
            int selectedIndex = Random.Range(0, commonCount);
            for (int i = 0; i < fallbackCards.Length; i++)
            {
                global::CardData card = fallbackCards[i];
                if (card == null || card.Rare != global::CardRarity.Common) continue;
                if (selectedIndex-- > 0) continue;
                return new global::CardPackEntry
                {
                    Card = card,
                    Number = Random.Range(1, 7),
                    Color = (global::CardColor)Random.Range(0, 5),
                    InclusionRate = 100f
                };
            }
            return null;
        }

        private void ResetPerPackAccumulatedBonuses()
        {
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null || !ability.ResetAccumulationAfterPack) continue;
                    if (ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusPerDraw
                        || ability.Effect == global::DeckAbilityEffect.AccumulatePercentAtStackThreshold
                        || ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusEfficiencyByNumber)
                        owner.AccumulatedPercentByAbility.Remove(j);
                    if (ability.Effect == global::DeckAbilityEffect.AccumulateFlatScorePerDraw)
                        owner.AccumulatedFlatScoreByAbility.Remove(j);
                }
            }
        }
        private int GetAdditionalNextPackCardCount()
        {
            int additionalCards = 0;
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null || ability.Effect != global::DeckAbilityEffect.AddNextPackCards) continue;
                    additionalCards += Mathf.Max(0, ability.PackCardCount) * GetEffectiveDeckCopyCount(owner);
                }
            }
            return additionalCards;
        }
        private void TriggerPackOpenedDeckAbilities()
        {
            int triggeredCount = 0;
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null) continue;
                    if (ability.Effect == global::DeckAbilityEffect.AddNextPackCards)
                    {
                        if (ability.PackCardCount <= 0) continue;
                        if (owner.Data.HasTag(global::CardTag.Nature))
                            pendingPackOpenNatureSources.Add(owner);
                        continue;
                    }
                    if (ability.Effect == global::DeckAbilityEffect.AddRandomTaggedCardOnPackOpen)
                    {
                        int cardsPerCopy = Mathf.Max(1, ability.PackCardCount);
                        for (int copy = 0; copy < effectiveCopies; copy++)
                            for (int generated = 0; generated < cardsPerCopy; generated++)
                                AppendRandomTaggedCardToCurrentPack(ability.GeneratedCardTag);
                        continue;
                    }
                    if (ability.Effect != global::DeckAbilityEffect.AddScoreOnPackOpen
                        || ability.Score <= 0) continue;
                    for (int copy = 0; copy < effectiveCopies; copy++)
                        AddDeckAbilityPopup(owner, ability, ability.Score, copy, triggeredCount++);
                }
            }
        }

        private void ApplyDeckCardTransformEffects(StoredCard revealedCard)
        {
            if (revealedCard == null || revealedCard.IsHolographic) return;
            float runeHologramChance = GetRuneResonanceValue(revealedCard,
                global::DeckAbilityEffect.GrantHologramChance,
                out StoredCard runePopupOwner, out global::CardDeckAbility runePopupAbility);
            if (runeHologramChance > 0f && Random.value < Mathf.Min(100f, runeHologramChance) * 0.01f)
            {
                revealedCard.IsHolographic = true;
                if (cardIndex >= 0 && cardIndex < cards.Count) cards[cardIndex].EnableHologram();
                return;
            }

            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null
                        || IsResonatingRuneAbility(owner, ability,
                            global::DeckAbilityEffect.GrantHologramChance)
                        || (ability.Effect != global::DeckAbilityEffect.GrantHologramChance
                            && ability.Effect != global::DeckAbilityEffect.GrantHologramChanceToPacksAndCards)
                        || ability.ChancePercent <= 0f || !DoesDeckAbilityTrigger(ability, owner, revealedCard)) continue;
                    for (int copy = 0; copy < effectiveCopies; copy++)
                    {
                        if (Random.value >= ability.ChancePercent * 0.01f) continue;
                        revealedCard.IsHolographic = true;
                        if (cardIndex >= 0 && cardIndex < cards.Count) cards[cardIndex].EnableHologram();
                        return;
                    }
                }
            }
        }

        private float GetScoreBonusEfficiencyMultiplier()
        {
            float addedEfficiency = 0f;
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null) continue;
                    if (ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusEfficiencyByNumber)
                    {
                        owner.AccumulatedPercentByAbility.TryGetValue(j, out float accumulatedEfficiency);
                        addedEfficiency += Mathf.Max(0f, accumulatedEfficiency) * 0.01f;
                        continue;
                    }
                    if (ability.Effect != global::DeckAbilityEffect.IncreaseScoreBonusEfficiency
                        || ability.PercentBonus <= 0f) continue;
                    addedEfficiency += ability.PercentBonus * 0.01f * effectiveCopies;
                }
            }
            return 1f + addedEfficiency;
        }

        private void AddDeckAbilityPopup(StoredCard owner, global::CardDeckAbility ability, int score,
            int copyIndex, int triggeredIndex, bool countForOtherCardScoreEvents = true)
        {
            string ownerReason = (IsNatureChainForcedTrigger(owner, ability)
                    ? Ui("\uC790\uC5F0-", "Nature - ") : string.Empty)
                + GetStoredCardDisplayName(owner);
            if (copyIndex > 0) ownerReason += Ui(" \uD640\uB85C\uADF8\uB7A8", " Holographic");
            AddScorePopup(ownerReason + "\n+" + score + Ui("\uC810", " pts"),
                copyIndex > 0 ? new Color(0.55f, 0.9f, 1f) : new Color(0.66f, 1f, 0.48f),
                Time.unscaledTime + triggeredIndex * 0.16f, 1 + triggeredIndex % 4, score);
            if (countForOtherCardScoreEvents)
                RegisterOtherCardScoreEvent(owner);
        }

        private void RegisterOtherCardScoreEvent(StoredCard scoringOwner)
        {
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard listener = GetAbilityOwnerAt(i);
                if (listener == null || object.ReferenceEquals(listener, scoringOwner)
                    || listener.Data == null || listener.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(listener);
                for (int abilityIndex = 0; abilityIndex < listener.Data.DeckAbilities.Count; abilityIndex++)
                {
                    global::CardDeckAbility ability = listener.Data.DeckAbilities[abilityIndex];
                    if (ability == null
                        || ability.Effect != global::DeckAbilityEffect.AddScoreEveryOtherCardScoreEvents
                        || ability.Score <= 0) continue;
                    int threshold = Mathf.Max(1, ability.StackThreshold);
                    for (int copy = 0; copy < effectiveCopies; copy++)
                    {
                        int stackKey = GetAbilityCopyKey(abilityIndex, copy);
                        listener.StackByAbilityCopy.TryGetValue(stackKey, out int currentStack);
                        currentStack++;
                        int triggerCount = currentStack / threshold;
                        listener.StackByAbilityCopy[stackKey] = currentStack % threshold;
                        for (int trigger = 0; trigger < triggerCount; trigger++)
                        {
                            AddDeckAbilityPopup(listener, ability, ability.Score, copy,
                                scorePopups.Count, false);
                        }
                    }
                }
            }
        }

        private void AddScorePopup(string text, Color color, float startTime, int lane, int score)
        {
            const float baseSameLaneSpacing = 1.36f;
            int burstCount = scorePopups.Count + 1;
            float burstSpeed = GetScorePopupBurstPlaybackSpeed(burstCount);
            float audioVolumeScale = GetScorePopupBurstAudioScale(burstCount);
            int normalizedLane = ((lane % ScorePopupTrailCapacity) + ScorePopupTrailCapacity)
                % ScorePopupTrailCapacity;
            float scheduledStartTime = startTime;
            for (int i = 0; i < scorePopups.Count; i++)
            {
                ScorePopup existing = scorePopups[i];
                existing.PlaybackSpeed = Mathf.Max(existing.PlaybackSpeed, burstSpeed);
                existing.AudioVolumeScale = Mathf.Min(existing.AudioVolumeScale, audioVolumeScale);
                if (existing.Lane != normalizedLane) continue;
                float laneSpacing = baseSameLaneSpacing / existing.PlaybackSpeed;
                scheduledStartTime = Mathf.Max(scheduledStartTime, existing.StartTime + laneSpacing);
            }

            scorePopups.Add(new ScorePopup
            {
                Text = text,
                Color = color,
                StartTime = scheduledStartTime,
                Lane = normalizedLane,
                Score = Mathf.Max(0, score),
                PlaybackSpeed = burstSpeed,
                AudioVolumeScale = audioVolumeScale
            });
            pendingScoreCommitTime = Mathf.Max(pendingScoreCommitTime, scheduledStartTime + 0.2f);
        }

        private static float GetScorePopupBurstPlaybackSpeed(int popupCount)
        {
            if (popupCount >= 30) return 12f;
            if (popupCount >= 20) return 8f;
            if (popupCount >= 10) return 5f;
            if (popupCount >= 6) return 2.5f;
            return 1f;
        }

        private static float GetScorePopupBurstAudioScale(int popupCount)
        {
            if (popupCount >= 30) return 0.12f;
            if (popupCount >= 20) return 0.18f;
            if (popupCount >= 10) return 0.30f;
            if (popupCount >= 6) return 0.55f;
            return 1f;
        }
        private void CommitPendingScoreImmediately()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < scorePopups.Count; i++)
            {
                ScorePopup popup = scorePopups[i];
                float visualAge = (now - popup.StartTime) * Mathf.Max(1f, popup.PlaybackSpeed);
                popup.PlaybackSpeed = 12f;
                popup.StartTime = now - visualAge / popup.PlaybackSpeed;
                if (popup.AddedToPendingScore) continue;
                popup.AddedToPendingScore = true;
                pendingScore += popup.Score;
            }

            int remainingScore = Mathf.Max(0, pendingScore - scoreTransferApplied);
            totalScore += remainingScore;
            roundScore += remainingScore;
            pendingScore = 0;
            pendingScoreCommitTime = -1f;
            scoreTransferAmount = 0;
            scoreTransferApplied = 0;
            scoreTransferStartTime = -1f;
        }

        private void SetupCardRarityAudio()
        {
            cardRarityAudioSource = gameObject.AddComponent<AudioSource>();
            cardRarityAudioSource.playOnAwake = false;
            cardRarityAudioSource.loop = false;
            cardRarityAudioSource.spatialBlend = 0f;
            cardRarityAudioSource.volume = 0.46f;

            const int sampleRate = 44100;
            float[] rootFrequencies = { 392f, 440f, 587.33f, 783.99f, 987.77f };
            float[] durations = { 0.16f, 0.26f, 0.30f, 0.42f, 0.62f };
            for (int tier = 0; tier < cardRarityAudioClips.Length; tier++)
            {
                int sampleCount = Mathf.CeilToInt(sampleRate * durations[tier]);
                float[] samples = new float[sampleCount];
                float root = rootFrequencies[tier];
                for (int i = 0; i < sampleCount; i++)
                {
                    float time = i / (float)sampleRate;
                    float attack = Mathf.Clamp01(time / 0.005f);
                    float envelope = attack * Mathf.Exp(-time * (9.5f - tier * 0.9f));
                    float tone = Mathf.Sin(2f * Mathf.PI * root * time) * 0.62f;
                    if (tier == 1)
                    {
                        const float secondNoteStart = 0.065f;
                        float secondNoteTime = Mathf.Max(0f, time - secondNoteStart);
                        float secondNoteFade = Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01(secondNoteTime / 0.018f));
                        tone *= 1f - secondNoteFade * 0.32f;
                        tone += Mathf.Sin(2f * Mathf.PI * root * 1.25f * secondNoteTime)
                            * 0.18f * secondNoteFade * Mathf.Exp(-secondNoteTime * 8f);
                    }
                    else if (tier >= 2)
                    {
                        float fifthFade = Mathf.Clamp01((time - 0.018f) / 0.012f);
                        tone += Mathf.Sin(2f * Mathf.PI * root * 1.4983f * time) * 0.18f * fifthFade;
                    }
                    if (tier >= 2)
                    {
                        float thirdFade = Mathf.Clamp01((time - 0.036f) / 0.014f);
                        tone += Mathf.Sin(2f * Mathf.PI * root * 1.2599f * time) * 0.13f * thirdFade;
                    }
                    if (tier >= 3)
                        tone += Mathf.Sin(2f * Mathf.PI * root * 2f * time) * 0.06f;
                    if (tier >= 4)
                        tone += Mathf.Sin(2f * Mathf.PI * root * 3f * time)
                            * 0.025f * Mathf.Exp(-time * 7f);
                    float tierVolume = tier == 1 ? 0.21f : 0.29f;
                    samples[i] = tone * envelope * tierVolume;
                }

                AudioClip clip = AudioClip.Create(
                    "Card Rarity Reveal " + tier, sampleCount, 1, sampleRate, false);
                clip.SetData(samples, 0);
                cardRarityAudioClips[tier] = clip;
            }
        }

        private void PlayCardRarityRevealSound(global::CardRarity rarity)
        {
            int tier = Mathf.Clamp((int)rarity, 0, cardRarityAudioClips.Length - 1);
            AudioClip clip = cardRarityAudioClips[tier];
            if (cardRarityAudioSource == null || clip == null) return;
            cardRarityAudioSource.Stop();
            cardRarityAudioSource.PlayOneShot(clip);
        }

        private void SetupPackTearAudio()
        {
            packTearAudioSource = gameObject.AddComponent<AudioSource>();
            packTearAudioSource.playOnAwake = false;
            packTearAudioSource.loop = false;
            packTearAudioSource.spatialBlend = 0f;
            packTearAudioSource.volume = 0.48f;

            const int sampleRate = 44100;
            const float duration = 0.26f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            uint noiseState = 0xA3C59AC3u;
            float smoothedNoise = 0f;
            float softenedSample = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float progress = time / duration;
                noiseState = unchecked(noiseState * 1664525u + 1013904223u);
                float rawNoise = ((noiseState >> 8) / 16777215f) * 2f - 1f;
                smoothedNoise = Mathf.Lerp(smoothedNoise, rawNoise, 0.16f);
                float crispNoise = rawNoise - smoothedNoise * 0.62f;
                float crackle = Mathf.Abs(rawNoise) > 0.76f ? rawNoise : 0f;
                float pulse = 0.78f + 0.22f * Mathf.Sin(2f * Mathf.PI * 34f * time);
                float scrapePhase = 280f * time + 720f * time * time;
                float scrape = Mathf.Sin(2f * Mathf.PI * scrapePhase);
                float attack = Mathf.Clamp01(time / 0.012f);
                float envelope = attack * Mathf.Pow(Mathf.Clamp01(1f - progress), 1.35f);
                float mixedSample = crispNoise * 0.42f + smoothedNoise * 0.22f
                    + crackle * 0.12f + scrape * 0.06f;
                softenedSample = Mathf.Lerp(softenedSample, mixedSample, 0.34f);
                samples[i] = softenedSample * pulse * envelope * 0.44f;
            }

            packTearAudioClip = AudioClip.Create(
                "Card Pack Tear", sampleCount, 1, sampleRate, false);
            packTearAudioClip.SetData(samples, 0);
        }

        private void PlayPackTearSound()
        {
            if (packTearAudioSource == null || packTearAudioClip == null) return;
            packTearAudioSource.Stop();
            packTearAudioSource.PlayOneShot(packTearAudioClip);
        }

        private void SetupScorePopupAudio()
        {
            scorePopupAudioSource = gameObject.AddComponent<AudioSource>();
            scorePopupAudioSource.playOnAwake = false;
            scorePopupAudioSource.loop = false;
            scorePopupAudioSource.spatialBlend = 0f;
            scorePopupAudioSource.volume = 0.28f;

            const int sampleRate = 44100;
            const float duration = 0.38f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float attack = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / 0.028f));
                float envelope = attack * Mathf.Exp(-time * 7.4f);
                float fundamental = Mathf.Sin(2f * Mathf.PI * 440f * time);
                float warmThird = Mathf.Sin(2f * Mathf.PI * 554.37f * time);
                float softFifth = Mathf.Sin(2f * Mathf.PI * 659.25f * time);
                samples[i] = (fundamental * 0.72f + warmThird * 0.18f + softFifth * 0.07f)
                    * envelope * 0.22f;
            }

            scorePopupAudioClip = AudioClip.Create(
                "Score Popup Ding", sampleCount, 1, sampleRate, false);
            scorePopupAudioClip.SetData(samples, 0);
        }

        private void PlayScorePopupSound(float volumeScale)
        {
            if (scorePopupAudioSource == null || scorePopupAudioClip == null) return;
            scorePopupAudioSource.PlayOneShot(scorePopupAudioClip, Mathf.Clamp01(volumeScale));
        }

        private void SetupAbilityEffectAudio()
        {
            abilityEffectAudioSource = gameObject.AddComponent<AudioSource>();
            abilityEffectAudioSource.playOnAwake = false;
            abilityEffectAudioSource.loop = false;
            abilityEffectAudioSource.spatialBlend = 0f;
            abilityEffectAudioSource.volume = 0.48f;

            const int sampleRate = 44100;
            const float equipDuration = 0.34f;
            int equipSampleCount = Mathf.CeilToInt(sampleRate * equipDuration);
            float[] equipSamples = new float[equipSampleCount];
            float phase = 0f;
            for (int i = 0; i < equipSampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float progress = time / equipDuration;
                float frequency = Mathf.Lerp(440f, 1174.66f, Mathf.SmoothStep(0f, 1f, progress));
                phase += 2f * Mathf.PI * frequency / sampleRate;
                float attack = Mathf.Clamp01(time / 0.012f);
                float envelope = attack * Mathf.Pow(Mathf.Clamp01(1f - progress), 1.35f);
                float tone = Mathf.Sin(phase) * 0.62f + Mathf.Sin(phase * 2f) * 0.17f;
                float sparkle = Mathf.Sin(2f * Mathf.PI * 2349.32f * time)
                    * Mathf.Clamp01((time - 0.12f) / 0.04f) * 0.12f;
                equipSamples[i] = (tone + sparkle) * envelope * 0.38f;
            }
            magicEquipAudioClip = AudioClip.Create(
                "Magic Equip", equipSampleCount, 1, sampleRate, false);
            magicEquipAudioClip.SetData(equipSamples, 0);

            const float resonanceDuration = 0.72f;
            int resonanceSampleCount = Mathf.CeilToInt(sampleRate * resonanceDuration);
            float[] resonanceSamples = new float[resonanceSampleCount];
            for (int i = 0; i < resonanceSampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float progress = time / resonanceDuration;
                float attack = Mathf.Clamp01(time / 0.045f);
                float envelope = attack * Mathf.Pow(Mathf.Clamp01(1f - progress), 0.82f);
                float chord = Mathf.Sin(2f * Mathf.PI * 261.63f * time) * 0.42f
                    + Mathf.Sin(2f * Mathf.PI * 329.63f * time) * 0.34f
                    + Mathf.Sin(2f * Mathf.PI * 392f * time) * 0.28f
                    + Mathf.Sin(2f * Mathf.PI * 783.99f * time) * 0.08f;
                float shimmer = Mathf.Sin(2f * Mathf.PI * (1174.66f + progress * 392f) * time)
                    * Mathf.Sin(Mathf.PI * progress) * 0.08f;
                resonanceSamples[i] = (chord + shimmer) * envelope * 0.42f;
            }
            runeResonanceAudioClip = AudioClip.Create(
                "Rune Resonance", resonanceSampleCount, 1, sampleRate, false);
            runeResonanceAudioClip.SetData(resonanceSamples, 0);
        }

        private void PlayMagicEquipSound()
        {
            if (abilityEffectAudioSource == null || magicEquipAudioClip == null) return;
            abilityEffectAudioSource.Stop();
            abilityEffectAudioSource.PlayOneShot(magicEquipAudioClip);
        }

        private void PlayRuneResonanceSound()
        {
            if (abilityEffectAudioSource == null || runeResonanceAudioClip == null) return;
            abilityEffectAudioSource.Stop();
            abilityEffectAudioSource.PlayOneShot(runeResonanceAudioClip);
        }

        private void UpdatePendingScore()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < scorePopups.Count; i++)
            {
                ScorePopup popup = scorePopups[i];
                if (now < popup.StartTime) continue;
                if (!popup.SoundPlayed)
                {
                    popup.SoundPlayed = true;
                    PlayScorePopupSound(popup.AudioVolumeScale);
                }
                if (popup.AddedToPendingScore) continue;
                popup.AddedToPendingScore = true;
                pendingScore += popup.Score;
            }

            if (scoreTransferStartTime >= 0f)
            {
                float progress = Mathf.Clamp01((now - scoreTransferStartTime) / 0.5f);
                int targetApplied = Mathf.RoundToInt(scoreTransferAmount * Mathf.SmoothStep(0f, 1f, progress));
                int scoreDelta = targetApplied - scoreTransferApplied;
                if (scoreDelta > 0)
                {
                    totalScore += scoreDelta;
                    roundScore += scoreDelta;
                    scoreTransferApplied = targetApplied;
                }

                if (progress < 1f) return;
                pendingScore = Mathf.Max(0, pendingScore - scoreTransferAmount);
                scoreTransferAmount = 0;
                scoreTransferApplied = 0;
                scoreTransferStartTime = -1f;
            }

            if (pendingScore <= 0 || pendingScoreCommitTime < 0f || now < pendingScoreCommitTime) return;
            scoreTransferAmount = pendingScore;
            scoreTransferApplied = 0;
            scoreTransferStartTime = now;
            pendingScoreCommitTime = -1f;
        }

        private static int GetEffectiveDeckCopyCount(StoredCard card)
        {
            if (card == null) return 1;
            int physicalCopies = Mathf.Max(1, card.CombinedCopies);
            return physicalCopies + GetCombinedHolographicCopyCount(card);
        }

        private static int GetCombinedHolographicCopyCount(StoredCard card)
        {
            if (card == null) return 0;
            int physicalCopies = Mathf.Max(1, card.CombinedCopies);
            if (card.CombinedHolographicCopies > 0)
                return Mathf.Clamp(card.CombinedHolographicCopies, 0, physicalCopies);
            return card.IsHolographic ? physicalCopies : 0;
        }

        private int GetAbilityOwnerCount()
        {
            int count = deckCards.Count;
            for (int i = 0; i < deckCards.Count; i++)
            {
                if (deckCards[i] == null) continue;
                if (deckCards[i].EquippedMagic != null) count++;
                if (deckCards[i].EquippedWeapon != null) count++;
                for (int j = 0; j < deckCards[i].InheritedRelics.Count; j++)
                    if (deckCards[i].InheritedRelics[j] != null) count++;
            }
            return count;
        }

        private StoredCard GetAbilityOwnerAt(int index)
        {
            if (index < 0) return null;
            if (index < deckCards.Count) return deckCards[index];
            index -= deckCards.Count;
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard host = deckCards[i];
                if (host == null) continue;
                if (host.EquippedMagic != null)
                {
                    if (index == 0) return host.EquippedMagic;
                    index--;
                }
                if (host.EquippedWeapon != null)
                {
                    if (index == 0) return host.EquippedWeapon;
                    index--;
                }
                for (int j = 0; j < host.InheritedRelics.Count; j++)
                {
                    StoredCard relic = host.InheritedRelics[j];
                    if (relic == null) continue;
                    if (index == 0) return relic;
                    index--;
                }
            }
            return null;
        }

        private static bool IsRuneCard(StoredCard card)
        {
            return card != null && card.Data != null && card.Data.HasTag(global::CardTag.Rune);
        }

        private bool IsRuneResonanceActive()
        {
            HashSet<global::CardData> runeTypes = new HashSet<global::CardData>();
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard card = deckCards[i];
                if (IsRuneCard(card)) runeTypes.Add(card.Data);
            }
            return runeTypes.Count >= 2;
        }

        private string GetStoredCardDisplayName(StoredCard card)
        {
            if (card == null) return string.Empty;
            string localizedName = card.Data != null ? card.Data.GetLocalizedName(IsEnglishUi) : string.Empty;
            string baseName = !string.IsNullOrWhiteSpace(localizedName) ? localizedName : card.Name;
            string displayName = IsRuneCard(card) && IsRuneResonanceActive()
                ? baseName + Ui("-공명", " - Resonant") : baseName;
            if (card.CombinedCopies > 1) displayName += " * " + card.CombinedCopies;
            List<string> equippedNames = new List<string>();
            if (card.EquippedMagic != null && card.EquippedMagic.Data != null)
                equippedNames.Add(card.EquippedMagic.Data.GetLocalizedName(IsEnglishUi));
            if (card.EquippedWeapon != null && card.EquippedWeapon.Data != null)
                equippedNames.Add(card.EquippedWeapon.Data.GetLocalizedName(IsEnglishUi));
            return equippedNames.Count > 0
                ? displayName + "(" + string.Join(", ", equippedNames) + ")"
                : displayName;
        }

        private bool IsResonatingRuneAbility(StoredCard owner, global::CardDeckAbility ability,
            global::DeckAbilityEffect effect)
        {
            return ability != null && ability.Effect == effect && IsRuneCard(owner)
                && IsRuneResonanceActive();
        }

        private float GetRuneResonanceValue(StoredCard revealedCard, global::DeckAbilityEffect effect,
            out StoredCard popupOwner, out global::CardDeckAbility popupAbility)
        {
            popupOwner = null;
            popupAbility = null;
            if (revealedCard == null || !IsRuneResonanceActive()) return 0f;

            float total = 0f;
            bool matchesResonanceColor = false;
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (!IsRuneCard(owner) || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null || ability.Effect != effect) continue;
                    if (RevealedCardMatchesAnyColor(revealedCard, owner, ability.ApplicableColors))
                    {
                        matchesResonanceColor = true;
                        if (popupOwner == null)
                        {
                            popupOwner = owner;
                            popupAbility = ability;
                        }
                    }
                    total += effect == global::DeckAbilityEffect.AddTriggeredScorePercent
                        ? ability.PercentBonus * effectiveCopies
                        : ability.ChancePercent * effectiveCopies;
                }
            }
            return matchesResonanceColor ? total : 0f;
        }

        private float GetRuneResonanceTotalValue(global::DeckAbilityEffect effect)
        {
            if (!IsRuneResonanceActive()) return 0f;
            float total = 0f;
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (!IsRuneCard(owner) || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null || ability.Effect != effect) continue;
                    total += effect == global::DeckAbilityEffect.AddTriggeredScorePercent
                        ? ability.PercentBonus * effectiveCopies
                        : ability.ChancePercent * effectiveCopies;
                }
            }
            return total;
        }

        private string GetStoredCardDisplayDescription(StoredCard card)
        {
            if (card == null || card.Data == null) return string.Empty;
            string description = card.Data.GetLocalizedDescription(IsEnglishUi) ?? string.Empty;
            description = ApplyInheritedRelicDescription(card, description);
            description = ApplyEquippedMagicDescription(card, description);
            description = ApplyEquippedWeaponDescription(card, description);
            if (!IsRuneCard(card) || !IsRuneResonanceActive()
                || card.Data.DeckAbilities == null) return description;

            description = ApplyRuneResonanceIncrease(description, card,
                global::DeckAbilityEffect.AddTriggeredScorePercent);
            description = ApplyRuneResonanceIncrease(description, card,
                global::DeckAbilityEffect.GrantHologramChance);
            return description;
        }

        private string ApplyInheritedRelicDescription(StoredCard card, string description)
        {
            if (card == null || card.InheritedRelics.Count == 0) return description;
            List<string> lines = new List<string> { Ui("\uC870\uB9BD \uC720\uBB3C \uD6A8\uACFC", "[Assembled Relic Effects]") };
            for (int i = 0; i < card.InheritedRelics.Count; i++)
            {
                StoredCard relic = card.InheritedRelics[i];
                if (relic == null || relic.Data == null || relic.Data.DeckAbilities == null) continue;
                int copies = GetEffectiveDeckCopyCount(relic);
                for (int j = 0; j < relic.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = relic.Data.DeckAbilities[j];
                    if (ability == null) continue;
                    switch (ability.Effect)
                    {
                        case global::DeckAbilityEffect.AddScore:
                            lines.Add(Ui("\uB098\uC0AC: \uB9E4 \uCE74\uB4DC +", "Screw: +") + ability.Score * copies
                                + Ui("\uC810", " pts each draw"));
                            break;
                        case global::DeckAbilityEffect.IncreaseScoreBonusEfficiency:
                            lines.Add(Ui("\uBC14\uD034: \uBCF4\uB108\uC2A4 \uD6A8\uC728 +", "Wheel: bonus efficiency +") +
                                (ability.PercentBonus * copies).ToString("0.#") + "%");
                            break;
                        case global::DeckAbilityEffect.AccumulateScoreBonusPerDraw:
                            string label = relic.Data.name == "MagicEngine"
                                ? Ui("\uC5D4\uC9C4", "Engine") : Ui("\uBC30\uD130\uB9AC", "Battery");
                            string suffix = ability.ResetAccumulationAfterPack
                                ? Ui(" (\uD329 \uC885\uB8CC \uC2DC \uCD08\uAE30\uD654)", " (resets after pack)") : string.Empty;
                            lines.Add(label + Ui(": \uB9E4 \uCE74\uB4DC \uB204\uC801 +", ": accumulates +") +
                                (ability.PercentBonus * copies).ToString("0.#")
                                + Ui("%", "% each draw") + suffix);
                            break;
                    }
                }
            }
            return lines.Count <= 1 ? description : description + "\n" + string.Join("\n", lines);
        }

        private string ApplyRuneResonanceIncrease(string description, StoredCard card,
            global::DeckAbilityEffect effect)
        {
            for (int i = 0; i < card.Data.DeckAbilities.Count; i++)
            {
                global::CardDeckAbility ability = card.Data.DeckAbilities[i];
                if (ability == null || ability.Effect != effect) continue;
                float baseValue = effect == global::DeckAbilityEffect.AddTriggeredScorePercent
                    ? ability.PercentBonus : ability.ChancePercent;
                if (baseValue <= 0f) continue;
                float totalValue = GetRuneResonanceTotalValue(effect);
                float increase = Mathf.Max(0f, totalValue - baseValue);
                if (increase <= 0f) return description;
                string baseText = baseValue.ToString("0.#") + "%";
                int percentIndex = description.IndexOf(baseText);
                if (percentIndex < 0) return description;
                string increaseText = "(+" + increase.ToString("0.#") + "%)";
                return description.Insert(percentIndex + baseText.Length, increaseText);
            }
            return description;
        }

        private string ApplyEquippedMagicDescription(StoredCard card, string description)
        {
            if (card.Data == null || !card.Data.CanEquipMagic || card.EquippedMagic == null
                || card.EquippedMagic.Data == null) return description;
            global::CardData magic = card.EquippedMagic.Data;
            string equippedEffect = magic.GetLocalizedName(IsEnglishUi) + ": "
                + (magic.GetLocalizedDescription(IsEnglishUi) ?? string.Empty)
                + Ui(" (\uC7A5\uCC29\uB428)", " (Equipped)");
            string[] markers =
            {
                "\uB9C8\uBC95\uC744 1\uC7A5 \uC7A5\uCC29\uD560 \uC218 \uC788\uB2E4.",
                "\uB9C8\uBC95\uC744 1\uC7A5 \uC7A5\uCC29\uD560 \uC218 \uC788\uB2E4",
                "\uB9C8\uBC95\uC744 \uD558\uB098 \uC7A5\uCC29\uD560 \uC218 \uC788\uB2E4.",
                "\uB9C8\uBC95\uC744 \uD558\uB098 \uC7A5\uCC29\uD560\uC218 \uC788\uB2E4.",
                "Can equip 1 spell.",
                "Can equip one spell."
            };
            for (int i = 0; i < markers.Length; i++)
            {
                if (description.Contains(markers[i]))
                    return ReplaceEquipmentMarkerWithLineBreak(description, markers[i], equippedEffect);
            }
            return string.IsNullOrWhiteSpace(description)
                ? equippedEffect : description + "\n" + equippedEffect;
        }

        private string ApplyEquippedWeaponDescription(StoredCard card, string description)
        {
            if (card.Data == null || !card.Data.CanEquipWeapon || card.EquippedWeapon == null
                || card.EquippedWeapon.Data == null) return description;
            global::CardData weapon = card.EquippedWeapon.Data;
            string equippedEffect = weapon.GetLocalizedName(IsEnglishUi) + ": "
                + (weapon.GetLocalizedDescription(IsEnglishUi) ?? string.Empty)
                + Ui(" (\uC7A5\uCC29\uB428)", " (Equipped)");
            string[] markers =
            {
                "\uBB34\uAE30\uB97C 1\uAC1C \uC7A5\uCC29\uD560 \uC218 \uC788\uB2E4.",
                "\uBB34\uAE30\uB97C 1\uAC1C \uC7A5\uCC29\uD560 \uC218 \uC788\uB2E4",
                "\uBB34\uAE30\uB97C \uD558\uB098 \uC7A5\uCC29\uD560 \uC218 \uC788\uB2E4.",
                "\uBB34\uAE30\uB97C \uD558\uB098 \uC7A5\uCC29\uD560\uC218 \uC788\uB2E4.",
                "Can equip 1 weapon.",
                "Can equip one weapon."
            };
            for (int i = 0; i < markers.Length; i++)
            {
                if (description.Contains(markers[i]))
                    return ReplaceEquipmentMarkerWithLineBreak(description, markers[i], equippedEffect);
            }
            return string.IsNullOrWhiteSpace(description)
                ? equippedEffect : description + "\n" + equippedEffect;
        }

        private static string ReplaceEquipmentMarkerWithLineBreak(
            string description, string marker, string equippedEffect)
        {
            int markerIndex = description.IndexOf(marker, System.StringComparison.Ordinal);
            if (markerIndex < 0) return description;
            string before = description.Substring(0, markerIndex).TrimEnd();
            if (before.EndsWith(",", System.StringComparison.Ordinal))
                before = before.Substring(0, before.Length - 1).TrimEnd();
            string after = description.Substring(markerIndex + marker.Length).TrimStart();
            if (after.StartsWith(",", System.StringComparison.Ordinal))
                after = after.Substring(1).TrimStart();
            string result = string.IsNullOrEmpty(before) ? equippedEffect : before + "\n" + equippedEffect;
            return string.IsNullOrEmpty(after) ? result : result + "\n" + after;
        }

        private void RefreshLocalizedCardDisplays()
        {
            for (int i = 0; i < cards.Count && i < currentPackCards.Count; i++)
            {
                CardVisual visual = cards[i];
                StoredCard card = currentPackCards[i];
                if (visual == null || card == null || card.Data == null) continue;
                visual.SetDisplayName(GetStoredCardDisplayName(card));
                visual.SetDisplayDescription(card.Data, GetStoredCardDisplayDescription(card), IsEnglishUi);
            }
            RefreshDeckCardDisplayNames();
            if (packContentsPreviewVisual != null) BuildPackContentsPreviewCard();
        }

        private void RefreshDeckCardDisplayNames()
        {
            bool resonanceActive = IsRuneResonanceActive();
            if (resonanceActive && !runeResonanceWasActive) PlayRuneResonanceSound();
            runeResonanceWasActive = resonanceActive;
            for (int i = 0; i < deckCards.Count && i < deckVisuals.Count; i++)
            {
                GameObject visualObject = deckVisuals[i];
                if (visualObject == null) continue;
                CardVisual visual = visualObject.GetComponent<CardVisual>();
                if (visual == null) continue;
                visual.SetDisplayName(GetStoredCardDisplayName(deckCards[i]));
                visual.SetDisplayDescription(deckCards[i].Data,
                    GetStoredCardDisplayDescription(deckCards[i]), IsEnglishUi);
            }
        }
        private int GetDeckAbilityTriggerCount(global::CardDeckAbility ability, StoredCard owner,
            StoredCard revealedCard, int triggeredEffectCount = 0)
        {
            int triggerCount = GetNormalDeckAbilityTriggerCount(
                ability, owner, revealedCard, triggeredEffectCount);
            if (IsNatureChainForcedTrigger(owner, ability))
                triggerCount += Mathf.Max(0, natureAbilityChainTriggerCount
                    - GetNaturallyTriggeredNatureCount(owner));
            return triggerCount;
        }

        private int GetNormalDeckAbilityTriggerCount(global::CardDeckAbility ability, StoredCard owner,
            StoredCard revealedCard, int triggeredEffectCount = 0)
        {
            if (!DoesDeckAbilityTriggerNormally(ability, owner, revealedCard, triggeredEffectCount)) return 0;
            return ability.Trigger == global::DeckAbilityTrigger.IncludedColors
                ? CountMatchingAbilityColors(revealedCard, owner, ability.ApplicableColors) : 1;
        }

        private bool DoesDeckAbilityTrigger(global::CardDeckAbility ability, StoredCard owner, StoredCard revealedCard, int triggeredEffectCount = 0)
        {
            return GetDeckAbilityTriggerCount(ability, owner, revealedCard, triggeredEffectCount) > 0;
        }

        private bool IsNatureChainForcedTrigger(StoredCard owner, global::CardDeckAbility ability)
        {
            return natureAbilityChainActive
                && owner != null && owner.Data != null
                && owner.Data.HasTag(global::CardTag.Nature)
                && natureAbilityChainTriggerCount > GetNaturallyTriggeredNatureCount(owner)
                && IsNatureChainEligibleAbility(ability);
        }

        private bool DoesDeckAbilityTriggerNormally(global::CardDeckAbility ability, StoredCard owner, StoredCard revealedCard, int triggeredEffectCount = 0)
        {
            switch (ability.Trigger)
            {
                case global::DeckAbilityTrigger.IncludedNumbers:
                    return ability.ApplicableNumbers != null
                        && ability.ApplicableNumbers.Contains(revealedCard.Number);
                case global::DeckAbilityTrigger.DifferentColor:
                    return !CardColorsMatch(owner, revealedCard);
                case global::DeckAbilityTrigger.MatchingColorAndNumber:
                    return CardColorsMatch(owner, revealedCard)
                        && owner.Number == revealedCard.Number;
                case global::DeckAbilityTrigger.MatchingNumber:
                    return owner.Number == revealedCard.Number;
                case global::DeckAbilityTrigger.EveryCard:
                    return true;
                case global::DeckAbilityTrigger.PreviousCardDifferentColor:
                    return previousRevealedCard != null
                        && !CardColorsMatch(previousRevealedCard, revealedCard);

                case global::DeckAbilityTrigger.TriggeredEffectsAtLeastThree:
                    return triggeredEffectCount >= 3;
                case global::DeckAbilityTrigger.IncludedColors:
                    return RevealedCardMatchesAnyColor(revealedCard, owner, ability.ApplicableColors);

                default:
                    return false;
            }
        }

        private bool ColorsMatch(global::CardColor left, global::CardColor right)
        {
            if (left == right) return true;
            return HasWhiteCardsCountAsAllColors()
                && (left == global::CardColor.White || right == global::CardColor.White);
        }

        private bool CardColorsMatch(StoredCard left, StoredCard right)
        {
            if (IsAllColorCard(left) || IsAllColorCard(right)) return true;
            return left != null && right != null && ColorsMatch(left.Color, right.Color);
        }

        private static bool IsAllColorCard(StoredCard card)
        {
            return card != null && card.Rarity == global::CardRarity.Legendary;
        }
        private bool RevealedCardHasColor(StoredCard card, global::CardColor color)
        {
            if (card == null) return false;
            return IsAllColorCard(card) || card.Color == color
                || (card.Color == global::CardColor.White && HasWhiteCardsCountAsAllColors());
        }

        private bool RevealedCardMatchesAnyColor(StoredCard card, StoredCard owner,
            List<global::AbilityColor> colors)
        {
            return CountMatchingAbilityColors(card, owner, colors) > 0;
        }

        private int CountMatchingAbilityColors(StoredCard card, StoredCard owner,
            List<global::AbilityColor> colors)
        {
            if (card == null || colors == null || colors.Count == 0) return 0;
            int matchCount = 0;
            for (int i = 0; i < colors.Count; i++)
            {
                global::AbilityColor color = colors[i];
                if (color == global::AbilityColor.Self)
                {
                    if (owner != null && CardColorsMatch(owner, card)) matchCount++;
                    continue;
                }

                if (RevealedCardHasColor(card, (global::CardColor)color)) matchCount++;
            }
            return matchCount;
        }
        private bool HasWhiteCardsCountAsAllColors()
        {
            for (int i = 0; i < GetAbilityOwnerCount(); i++)
            {
                StoredCard owner = GetAbilityOwnerAt(i);
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability != null
                        && ability.Effect == global::DeckAbilityEffect.WhiteCardsCountAsAllColors)
                        return true;
                }
            }
            return false;
        }
        private bool TryAutoEquipMagic(StoredCard magic)
        {
            if (magic == null || magic.Data == null || magic.IsStoredInDeck
                || IsStackableCardData(magic.Data)
                || !magic.Data.HasTag(global::CardTag.Magic)) return false;
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard host = deckCards[i];
                if (host == null || host.Data == null || !host.Data.CanEquipMagic
                    || host.EquippedMagic != null) continue;
                magic.IsStoredInDeck = true;
                magic.DeckSlot = -1;
                host.EquippedMagic = magic;
                PlayMagicEquipSound();
                RefreshDeckCardDisplayNames();
                return true;
            }
            return false;
        }

        private bool TryAutoEquipWeapon(StoredCard weapon)
        {
            if (weapon == null || weapon.Data == null || weapon.IsStoredInDeck
                || IsStackableCardData(weapon.Data)
                || !weapon.Data.HasTag(global::CardTag.Weapon)) return false;
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard host = deckCards[i];
                if (host == null || host.Data == null || !host.Data.CanEquipWeapon
                    || host.EquippedWeapon != null) continue;
                weapon.IsStoredInDeck = true;
                weapon.DeckSlot = -1;
                host.EquippedWeapon = weapon;
                PlayMagicEquipSound();
                RefreshDeckCardDisplayNames();
                return true;
            }
            return false;
        }

        private bool TryAutoEquipStoredCardsToHost(StoredCard host)
        {
            if (host == null || host.Data == null) return false;
            bool equipped = false;
            if (host.Data.CanEquipMagic && host.EquippedMagic == null)
            {
                int hostIndex = deckCards.IndexOf(host);
                for (int i = 0; i < deckCards.Count && hostIndex >= 0; i++)
                {
                    StoredCard candidate = deckCards[i];
                    if (candidate == host || candidate == null || candidate.Data == null
                        || IsStackableCardData(candidate.Data)
                        || !candidate.Data.HasTag(global::CardTag.Magic)) continue;
                    equipped |= TryEquipDeckMagic(i, hostIndex);
                    break;
                }
            }
            if (host.Data.CanEquipWeapon && host.EquippedWeapon == null)
            {
                int hostIndex = deckCards.IndexOf(host);
                for (int i = 0; i < deckCards.Count && hostIndex >= 0; i++)
                {
                    StoredCard candidate = deckCards[i];
                    if (candidate == host || candidate == null || candidate.Data == null
                        || IsStackableCardData(candidate.Data)
                        || !candidate.Data.HasTag(global::CardTag.Weapon)) continue;
                    equipped |= TryEquipDeckWeapon(i, hostIndex);
                    break;
                }
            }
            if (equipped)
            {
                RefreshDeckCardDisplayNames();
                LayoutDeckVisuals();
            }
            return equipped;
        }

        private static bool IsStackableCardData(global::CardData data)
        {
            return data != null && (data.UnlimitedMergeCount || data.MaxMergeCount > 1);
        }

        private bool TryMergeCardIntoDeck(StoredCard incoming)
        {
            if (incoming == null || incoming.Data == null
                || (!incoming.Data.UnlimitedMergeCount && incoming.Data.MaxMergeCount <= 1)
                || incoming.EquippedMagic != null || incoming.EquippedWeapon != null) return false;
            int incomingCopies = Mathf.Max(1, incoming.CombinedCopies);
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard target = deckCards[i];
                if (target == null || target.Data != incoming.Data || target.EquippedMagic != null
                    || target.EquippedWeapon != null) continue;
                int targetCopies = Mathf.Max(1, target.CombinedCopies);
                int mergeLimit = target.Data.UnlimitedMergeCount
                    ? int.MaxValue : Mathf.Max(1, target.Data.MaxMergeCount);
                if (incomingCopies > mergeLimit - targetCopies) continue;

                bool wasHolographic = target.IsHolographic;
                int holographicCopies = GetCombinedHolographicCopyCount(target)
                    + GetCombinedHolographicCopyCount(incoming);
                target.CombinedCopies = targetCopies + incomingCopies;
                target.CombinedHolographicCopies = Mathf.Clamp(holographicCopies, 0, target.CombinedCopies);
                target.IsHolographic = target.CombinedHolographicCopies > 0;
                incoming.IsStoredInDeck = true;
                incoming.DeckSlot = -1;

                if (!wasHolographic && target.IsHolographic && i < deckVisuals.Count
                    && deckVisuals[i] != null)
                {
                    CardVisual visual = deckVisuals[i].GetComponent<CardVisual>();
                    if (visual != null) visual.EnableHologram();
                }
                PlayMagicEquipSound();
                if (!TryFuseMagicEngineeringSatellite())
                {
                    RefreshDeckCardDisplayNames();
                    LayoutDeckVisuals();
                }
                return true;
            }
            return false;
        }

        private int FindFullyMergedCardIndex(string assetName, int requiredCopies)
        {
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard card = deckCards[i];
                if (card == null || card.Data == null || card.Data.name != assetName) continue;
                if (Mathf.Max(1, card.CombinedCopies) >= requiredCopies) return i;
            }
            return -1;
        }

        private bool TryFuseMagicEngineeringSatellite()
        {
            int screwIndex = FindFullyMergedCardIndex("MagicScrew", 4);
            int wheelIndex = FindFullyMergedCardIndex("MagicWheel", 2);
            int batteryIndex = FindFullyMergedCardIndex("MagicBattery", 2);
            int engineIndex = FindFullyMergedCardIndex("MagicEngine", 1);
            if (screwIndex < 0 || wheelIndex < 0 || batteryIndex < 0 || engineIndex < 0) return false;

            global::CardData satelliteData =
                Resources.Load<global::CardData>("Cards/Legend/MagicEngineeringSatellite");
            if (satelliteData == null) return false;

            StoredCard engine = deckCards[engineIndex];
            StoredCard screw = deckCards[screwIndex];
            StoredCard wheel = deckCards[wheelIndex];
            StoredCard battery = deckCards[batteryIndex];
            int resultSlot = Mathf.Min(screw.DeckSlot, wheel.DeckSlot,
                deckCards[batteryIndex].DeckSlot, engine.DeckSlot);
            bool resultIsHolographic = GetCombinedHolographicCopyCount(deckCards[screwIndex]) > 0
                || GetCombinedHolographicCopyCount(deckCards[wheelIndex]) > 0
                || GetCombinedHolographicCopyCount(deckCards[batteryIndex]) > 0
                || GetCombinedHolographicCopyCount(engine) > 0;
            StoredCard satellite = new StoredCard
            {
                Name = satelliteData.Name,
                Data = satelliteData,
                Rarity = satelliteData.Rare,
                Color = engine.Color,
                Number = engine.Number,
                IsHolographic = resultIsHolographic,
                IsStoredInDeck = true,
                DeckSlot = resultSlot,
                CombinedCopies = 1,
                CombinedHolographicCopies = resultIsHolographic ? 1 : 0
            };

            List<int> materialIndices = new List<int>
                { screwIndex, wheelIndex, batteryIndex, engineIndex };
            materialIndices.Sort();
            for (int i = materialIndices.Count - 1; i >= 0; i--)
            {
                int materialIndex = materialIndices[i];
                GameObject materialVisual = materialIndex < deckVisuals.Count
                    ? deckVisuals[materialIndex] : null;
                deckCards.RemoveAt(materialIndex);
                if (materialIndex < deckVisuals.Count) deckVisuals.RemoveAt(materialIndex);
                if (materialVisual != null) Destroy(materialVisual);
            }

            deckCards.Add(satellite);
            deckVisuals.Add(BuildDeckVisualForStoredCard(satellite));
            PlayMagicEquipSound();
            RefreshDeckCardDisplayNames();
            LayoutDeckVisuals();
            return true;
        }

        private bool StoreCurrentCardInDeck(StoredCard card, int preferredSlot = -1)
        {
            if (card == null || card.IsStoredInDeck) return false;
            if (TryMergeCardIntoDeck(card)) return true;
            if (TryAutoEquipMagic(card) || TryAutoEquipWeapon(card)) return true;
            if (deckCards.Count >= 5) return false;
            int slot = preferredSlot >= 0 && preferredSlot < 5 && GetDeckIndexAtSlot(preferredSlot) < 0
                ? preferredSlot
                : GetFirstEmptyDeckSlot();
            if (slot < 0) return false;

            card.IsStoredInDeck = true;
            card.DeckSlot = slot;
            deckCards.Add(card);
            CreateStoredCardVisual();
            TryAutoEquipStoredCardsToHost(card);
            TryFuseMagicEngineeringSatellite();
            RefreshDeckCardDisplayNames();
            return true;
        }

        private int GetFirstEmptyDeckSlot()
        {
            for (int slot = 0; slot < 5; slot++)
                if (GetDeckIndexAtSlot(slot) < 0) return slot;
            return -1;
        }

        private int GetDeckIndexAtSlot(int slot)
        {
            for (int i = 0; i < deckCards.Count; i++)
                if (deckCards[i] != null && deckCards[i].DeckSlot == slot) return i;
            return -1;
        }

        private void CreateStoredCardVisual()
        {
            if (cardIndex < 0 || cardIndex >= cards.Count || deckRoot == null) return;
            GameObject copy = Instantiate(cards[cardIndex].gameObject, deckRoot);
            copy.name = "Stored " + cards[cardIndex].gameObject.name;
            copy.SetActive(true);
            Renderer[] storedRenderers = copy.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < storedRenderers.Length; i++)
            {
                storedRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                storedRenderers[i].receiveShadows = false;
            }
            deckVisuals.Add(copy);
            LayoutDeckVisuals();
        }

        private void CreateEmptyDeckPlaceholder()
        {
            if (deckRoot == null) return;
            Material blackMaterial = GetMaterial("EmptyDeckCard", Color.black, 0.08f);
            for (int i = 0; i < 5; i++)
            {
                GameObject placeholder = new GameObject("Empty Deck Card " + (i + 1));
                placeholder.transform.SetParent(deckRoot, false);
                CardVisual placeholderVisual = placeholder.AddComponent<CardVisual>();
                placeholderVisual.Build(default(CardData), blackMaterial, blackMaterial, null, null, font);
                SetStoredVisualShadowMode(placeholder);
                emptyDeckPlaceholders.Add(placeholder);
            }
        }

        private void LayoutDeckVisuals()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            float depth = camera.WorldToScreenPoint(CardHome).z;
            GetUiLayout(out float uiScale, out float offsetX, out float offsetY);
            float screenHeightScale = Screen.height > 0 ? Screen.height / ReferenceHeight : 1f;
            float deckScale = screenHeightScale > 0f ? uiScale / screenHeightScale : 1f;
            bool resultScreen = phase == RevealPhase.GameOver || phase == RevealPhase.RunCleared;
            float deckLayoutY = IsPortraitUi ? 1165f + PortraitExtraHeight : (resultScreen ? 635f : 622.8f);
            float inspectionLayoutY = IsPortraitUi ? 610f + PortraitExtraHeight * 0.5f : 352.8f;
            float deckGuiY = offsetY + deckLayoutY * uiScale;
            float inspectionGuiY = offsetY + inspectionLayoutY * uiScale;
            float deckCardScale = IsPortraitUi ? 0.66f : 0.43f;
            float deckStartX = IsPortraitUi ? 140f : (resultScreen ? 470f : 53.76f);
            bool isInspecting = inspectedDeckIndex >= 0 && inspectedDeckIndex < deckVisuals.Count;

            int liftedDeckSlot = deckCardDragActive && pressedDeckIndex >= 0 && pressedDeckIndex < deckCards.Count
                && deckCards[pressedDeckIndex] != null
                ? deckCards[pressedDeckIndex].DeckSlot
                : -1;
            for (int i = 0; i < emptyDeckPlaceholders.Count; i++)
            {
                GameObject placeholder = emptyDeckPlaceholders[i];
                if (placeholder == null) continue;
                bool showPlaceholder = (GetDeckIndexAtSlot(i) < 0 || i == liftedDeckSlot) && !isInspecting;
                placeholder.SetActive(showPlaceholder);
                if (!showPlaceholder) continue;
                float deckSpacing = IsPortraitUi ? 110f : (resultScreen ? 85f : 74.24f);
                float deckGuiX = offsetX + (deckStartX + i * deckSpacing) * uiScale;
                placeholder.transform.position =
                    camera.ScreenToWorldPoint(new Vector3(deckGuiX, Screen.height - deckGuiY, depth));
                placeholder.transform.localScale = Vector3.one * (deckCardScale * deckScale);
                placeholder.transform.rotation = camera.transform.rotation;
            }

            for (int i = 0; i < deckVisuals.Count; i++)
            {
                GameObject visual = deckVisuals[i];
                if (visual == null) continue;
                bool selected = isInspecting && i == inspectedDeckIndex;
                visual.SetActive(!isInspecting || selected);
                if (!visual.activeSelf) continue;
                if (!isInspecting && deckCardDragActive && i == pressedDeckIndex) continue;

                if (selected)
                {
                    float inspectionGuiX = offsetX + UiReferenceWidth * 0.5f * uiScale;
                    visual.transform.position = camera.ScreenToWorldPoint(
                        new Vector3(inspectionGuiX, Screen.height - inspectionGuiY, depth));
                    visual.transform.localScale = Vector3.one * ((IsPortraitUi ? 2.10f : 1.72f) * deckScale);
                }
                else
                {
                    int slot = i < deckCards.Count && deckCards[i] != null ? deckCards[i].DeckSlot : i;
                    float deckSpacing = IsPortraitUi ? 110f : (resultScreen ? 85f : 74.24f);
                    float deckGuiX = offsetX + (deckStartX + Mathf.Clamp(slot, 0, 4) * deckSpacing) * uiScale;
                    visual.transform.position = camera.ScreenToWorldPoint(
                        new Vector3(deckGuiX, Screen.height - deckGuiY, depth));
                    visual.transform.localScale = Vector3.one * (deckCardScale * deckScale);
                }
                if (!selected || (!deckInspectionDragging && !deckInspectionReturning))
                    visual.transform.rotation = camera.transform.rotation;
            }

            LayoutDeckInspectionBackdrop(camera, depth);
        }
        private void CreateDeckInspectionBackdrop()
        {
            deckInspectionBackdrop = CreateQuadObject("Deck Inspection Backdrop");

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
                throw new InvalidOperationException("CardOpen could not load the inspection backdrop shader. Check Always Included Shaders.");
            Material material = new Material(shader)
            {
                name = "Deck Inspection Black",
                color = new Color(0f, 0f, 0f, 0.78f)
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.78f));
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", 5f);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", 10f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.renderQueue = 2990;
            MeshRenderer renderer = deckInspectionBackdrop.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            deckInspectionBackdrop.SetActive(false);
        }

        private void LayoutDeckInspectionBackdrop(Camera camera, float cardDepth)
        {
            if (deckInspectionBackdrop == null || inspectedDeckIndex < 0) return;
            float backdropDepth = cardDepth + 3.2f;
            float height = 2f * backdropDepth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            deckInspectionBackdrop.transform.position = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, backdropDepth));
            deckInspectionBackdrop.transform.rotation = Quaternion.LookRotation(-camera.transform.forward, camera.transform.up);
            deckInspectionBackdrop.transform.localScale = new Vector3(height * camera.aspect * 1.08f, height * 1.08f, 1f);
        }

        private bool IsDeckInspectionReadOnly()
        {
            return sharedResultMode || phase == RevealPhase.PackChoice
                || phase == RevealPhase.GameOver || phase == RevealPhase.RunCleared;
        }

        private bool HandleDeckPointer(Vector2 screenPoint, Event inputEvent)
        {
            bool isInspecting = inspectedDeckIndex >= 0;
            bool readOnly = IsDeckInspectionReadOnly();
            Camera camera = Camera.main;
            if (camera == null) return isInspecting;

            if (isInspecting)
            {
                if (discardConfirmationVisible)
                {
                    if (inputEvent.isMouse) inputEvent.Use();
                    return true;
                }
                GameObject selected = inspectedDeckIndex < deckVisuals.Count ? deckVisuals[inspectedDeckIndex] : null;
                if (inputEvent.type == EventType.MouseDown)
                {
                    if (deckInspectionReturnRoutine != null) StopCoroutine(deckInspectionReturnRoutine);
                    deckInspectionReturnRoutine = null;
                    deckInspectionReturning = false;
                    deckInspectionDragging = true;
                    deckInspectionHasDragged = false;
                    deckInspectionPressOutside = selected == null || !GetVisualScreenRect(selected, camera).Contains(screenPoint);
                    deckInspectionDragStart = screenPoint;
                    if (selected != null) deckInspectionStartRotation = selected.transform.rotation;
                    inputEvent.Use();
                    return true;
                }

                if (inputEvent.type == EventType.MouseDrag && deckInspectionDragging)
                {
                    Vector2 delta = screenPoint - deckInspectionDragStart;
                    if (delta.sqrMagnitude >= 16f) deckInspectionHasDragged = true;
                    if (selected != null && deckInspectionHasDragged)
                    {
                        selected.transform.rotation = Quaternion.Euler(-delta.y * 0.24f, delta.x * 0.28f, 0f)
                            * deckInspectionStartRotation;
                    }
                    inputEvent.Use();
                    return true;
                }

                if (inputEvent.type == EventType.MouseUp && deckInspectionDragging)
                {
                    deckInspectionDragging = false;
                    if (deckInspectionPressOutside && !deckInspectionHasDragged)
                        CloseDeckInspection();
                    else if (selected != null && deckInspectionHasDragged)
                        deckInspectionReturnRoutine = StartCoroutine(ReturnInspectedDeckCard(selected));
                    deckInspectionPressOutside = false;
                    deckInspectionHasDragged = false;
                    inputEvent.Use();
                    return true;
                }

                if (inputEvent.isMouse) inputEvent.Use();
                return true;
            }

            if (inputEvent.type == EventType.MouseUp && gestureDragging && phase == RevealPhase.CardFront
                && IsPointInDeckRow(screenPoint))
            {
                gestureDragging = false;
                TryDropCurrentCardIntoDeck(screenPoint);
                inputEvent.Use();
                return true;
            }

            if (inputEvent.type == EventType.MouseDown)
            {
                for (int i = deckVisuals.Count - 1; i >= 0; i--)
                {
                    GameObject visual = deckVisuals[i];
                    if (visual == null || !visual.activeSelf || !GetVisualScreenRect(visual, camera).Contains(screenPoint)) continue;
                    pressedDeckIndex = i;
                    deckCardDragStart = screenPoint;
                    deckCardDragActive = false;
                    inputEvent.Use();
                    return true;
                }
                return false;
            }

            if (inputEvent.type == EventType.MouseDrag && pressedDeckIndex >= 0)
            {
                Vector2 delta = screenPoint - deckCardDragStart;
                if (delta.sqrMagnitude >= 25f) deckCardDragActive = true;
                if (deckCardDragActive && !readOnly && pressedDeckIndex < deckVisuals.Count)
                {
                    GameObject dragged = deckVisuals[pressedDeckIndex];
                    float depth = camera.WorldToScreenPoint(CardHome).z - 0.45f;
                    dragged.transform.position = camera.ScreenToWorldPoint(
                        new Vector3(screenPoint.x, Screen.height - screenPoint.y, depth));
                    dragged.transform.rotation = camera.transform.rotation;
                    GetUiLayout(out float uiScale, out _, out _);
                    float heightScale = Screen.height > 0 ? Screen.height / ReferenceHeight : 1f;
                    float dragScale = heightScale > 0f ? uiScale / heightScale : 1f;
                    dragged.transform.localScale = Vector3.one * (0.52f * dragScale);
                }
                inputEvent.Use();
                return true;
            }

            if (inputEvent.type == EventType.MouseUp && pressedDeckIndex >= 0)
            {
                int sourceIndex = pressedDeckIndex;
                bool wasDragged = deckCardDragActive;
                pressedDeckIndex = -1;
                deckCardDragActive = false;

                if (!wasDragged)
                {
                    OpenDeckInspection(sourceIndex);
                }
                else if (readOnly)
                {
                    // Result and pack-choice decks can be inspected but not edited.
                }
                else if (IsPointOverCurrentCard(screenPoint, camera))
                {
                    SwapDeckCardWithCurrent(sourceIndex);
                }
                else
                {
                    int targetSlot = GetDeckSlotAtPoint(screenPoint);
                    int targetIndex = GetDeckIndexAtSlot(targetSlot);
                    if (targetIndex >= 0 && targetIndex != sourceIndex
                        && (TryEquipDeckMagic(sourceIndex, targetIndex)
                            || TryEquipDeckWeapon(sourceIndex, targetIndex)))
                    {
                        RefreshDeckCardDisplayNames();
                    }
                    else if (targetSlot >= 0)
                    {
                        MoveDeckCardToSlot(sourceIndex, targetSlot);
                    }
                }
                LayoutDeckVisuals();
                inputEvent.Use();
                return true;
            }

            return pressedDeckIndex >= 0;
        }

        private bool IsPointOverCurrentCard(Vector2 screenPoint, Camera camera)
        {
            return phase == RevealPhase.CardFront && cardIndex >= 0 && cardIndex < cards.Count
                && cards[cardIndex] != null
                && GetVisualScreenRect(cards[cardIndex].gameObject, camera).Contains(screenPoint);
        }

        private static bool IsPointInDeckRow(Vector2 screenPoint)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return false;
            Vector2 referencePoint = ScreenToReferencePoint(screenPoint);
            Rect deckRow = IsPortraitUi
                ? new Rect(0f, 1035f + PortraitExtraHeight, PortraitWidth, 245f)
                : new Rect(0f, 518.4f, 460.8f, 201.6f);
            return deckRow.Contains(referencePoint);
        }

        private static int GetDeckSlotAtPoint(Vector2 screenPoint)
        {
            if (!IsPointInDeckRow(screenPoint)) return -1;
            Vector2 referencePoint = ScreenToReferencePoint(screenPoint);
            float startX = IsPortraitUi ? 150f : 53.76f;
            float spacing = IsPortraitUi ? 105f : 74.24f;
            int slot = Mathf.RoundToInt((referencePoint.x - startX) / spacing);
            return Mathf.Clamp(slot, 0, 4);
        }

        private bool TryEquipDeckMagic(int sourceIndex, int hostIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= deckCards.Count || hostIndex < 0
                || hostIndex >= deckCards.Count || sourceIndex == hostIndex) return false;
            StoredCard magic = deckCards[sourceIndex];
            StoredCard host = deckCards[hostIndex];
            if (magic == null || magic.Data == null || !magic.Data.HasTag(global::CardTag.Magic)
                || IsStackableCardData(magic.Data)
                || host == null || host.Data == null || !host.Data.CanEquipMagic) return false;

            if (host.EquippedMagic != null)
            {
                host.EquippedMagic.IsStoredInDeck = false;
                host.EquippedMagic.DeckSlot = -1;
            }
            GameObject magicVisual = sourceIndex < deckVisuals.Count ? deckVisuals[sourceIndex] : null;
            deckCards.RemoveAt(sourceIndex);
            if (sourceIndex < deckVisuals.Count) deckVisuals.RemoveAt(sourceIndex);
            if (magicVisual != null) Destroy(magicVisual);
            magic.IsStoredInDeck = true;
            magic.DeckSlot = -1;
            host.EquippedMagic = magic;
            PlayMagicEquipSound();
            return true;
        }

        private bool TryEquipCurrentMagic(int deckIndex)
        {
            if (deckIndex < 0 || deckIndex >= deckCards.Count || cardIndex < 0
                || cardIndex >= currentPackCards.Count) return false;
            StoredCard host = deckCards[deckIndex];
            StoredCard magic = currentPackCards[cardIndex];
            if (host == null || host.Data == null || !host.Data.CanEquipMagic
                || magic == null || magic.Data == null || magic.IsStoredInDeck
                || IsStackableCardData(magic.Data)
                || !magic.Data.HasTag(global::CardTag.Magic)) return false;

            if (host.EquippedMagic != null)
            {
                host.EquippedMagic.IsStoredInDeck = false;
                host.EquippedMagic.DeckSlot = -1;
            }
            magic.IsStoredInDeck = true;
            magic.DeckSlot = -1;
            host.EquippedMagic = magic;
            PlayMagicEquipSound();
            RefreshDeckCardDisplayNames();
            return true;
        }

        private bool TryEquipDeckWeapon(int sourceIndex, int hostIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= deckCards.Count || hostIndex < 0
                || hostIndex >= deckCards.Count || sourceIndex == hostIndex) return false;
            StoredCard weapon = deckCards[sourceIndex];
            StoredCard host = deckCards[hostIndex];
            if (weapon == null || weapon.Data == null || !weapon.Data.HasTag(global::CardTag.Weapon)
                || IsStackableCardData(weapon.Data)
                || host == null || host.Data == null || !host.Data.CanEquipWeapon) return false;

            if (host.EquippedWeapon != null)
            {
                host.EquippedWeapon.IsStoredInDeck = false;
                host.EquippedWeapon.DeckSlot = -1;
            }
            GameObject weaponVisual = sourceIndex < deckVisuals.Count ? deckVisuals[sourceIndex] : null;
            deckCards.RemoveAt(sourceIndex);
            if (sourceIndex < deckVisuals.Count) deckVisuals.RemoveAt(sourceIndex);
            if (weaponVisual != null) Destroy(weaponVisual);
            weapon.IsStoredInDeck = true;
            weapon.DeckSlot = -1;
            host.EquippedWeapon = weapon;
            PlayMagicEquipSound();
            return true;
        }

        private bool TryEquipCurrentWeapon(int deckIndex)
        {
            if (deckIndex < 0 || deckIndex >= deckCards.Count || cardIndex < 0
                || cardIndex >= currentPackCards.Count) return false;
            StoredCard host = deckCards[deckIndex];
            StoredCard weapon = currentPackCards[cardIndex];
            if (host == null || host.Data == null || !host.Data.CanEquipWeapon
                || weapon == null || weapon.Data == null || weapon.IsStoredInDeck
                || IsStackableCardData(weapon.Data)
                || !weapon.Data.HasTag(global::CardTag.Weapon)) return false;

            if (host.EquippedWeapon != null)
            {
                host.EquippedWeapon.IsStoredInDeck = false;
                host.EquippedWeapon.DeckSlot = -1;
            }
            weapon.IsStoredInDeck = true;
            weapon.DeckSlot = -1;
            host.EquippedWeapon = weapon;
            PlayMagicEquipSound();
            RefreshDeckCardDisplayNames();
            return true;
        }

        private void TryDropCurrentCardIntoDeck(Vector2 screenPoint)
        {
            if (cardIndex < 0 || cardIndex >= cards.Count || cardIndex >= currentPackCards.Count) return;
            int slot = GetDeckSlotAtPoint(screenPoint);
            if (slot < 0) return;

            int occupiedDeckIndex = GetDeckIndexAtSlot(slot);
            if (occupiedDeckIndex >= 0)
            {
                if (TryEquipCurrentMagic(occupiedDeckIndex)
                    || TryEquipCurrentWeapon(occupiedDeckIndex))
                {
                    StartCoroutine(AdvanceAfterDeckDrop());
                    LayoutDeckVisuals();
                    return;
                }
                SwapDeckCardWithCurrent(occupiedDeckIndex);
                LayoutDeckVisuals();
                return;
            }

            if (!StoreCurrentCardInDeck(currentPackCards[cardIndex], slot)) return;
            StartCoroutine(AdvanceAfterDeckDrop());
            LayoutDeckVisuals();
        }

        private IEnumerator AdvanceAfterDeckDrop()
        {
            CommitPendingScoreImmediately();
            phase = RevealPhase.Animating;
            cardTransitionActive = true;
            CardVisual current = cards[cardIndex];

            if (cardIndex + 1 < cards.Count)
            {
                CardVisual next = cards[cardIndex + 1];
                next.gameObject.SetActive(true);
                next.PrepareFaceUp(CardHome + new Vector3(0f, 0.035f, 0.035f), CurrentRevealedCardScale, 0f);
                next.SetFaceDetailsVisible(true);
            }

            if (current != null) current.gameObject.SetActive(false);
            cardIndex++;
            if (cardIndex >= cards.Count)
            {
                cardTransitionActive = false;
                yield return new WaitForSeconds(0.35f);
                CompletePackAndBeginNextSequence();
                yield break;
            }

            yield return cards[cardIndex].MoveToFront(CardHome, CurrentRevealedCardScale, 0f);
            yield return RestoreCardStackRotation();
            PlayCardRarityRevealSound(currentPackCards[cardIndex].Rarity);
            AwardCurrentCardScore();
            cardTransitionActive = false;
            phase = RevealPhase.CardFront;
        }

        private bool SwapDeckCardWithCurrent(int deckIndex)
        {
            if (phase != RevealPhase.CardFront || cardIndex < 0 || cardIndex >= cards.Count
                || cardIndex >= currentPackCards.Count || deckIndex < 0 || deckIndex >= deckCards.Count) return false;

            StoredCard currentData = currentPackCards[cardIndex];
            int existingCurrentSlot = deckCards.IndexOf(currentData);
            if (existingCurrentSlot == deckIndex) return false;
            if (existingCurrentSlot >= 0)
            {
                GameObject duplicateVisual = deckVisuals[existingCurrentSlot];
                deckCards.RemoveAt(existingCurrentSlot);
                deckVisuals.RemoveAt(existingCurrentSlot);
                if (duplicateVisual != null) Destroy(duplicateVisual);
                currentData.IsStoredInDeck = false;
                if (existingCurrentSlot < deckIndex) deckIndex--;
            }
            if (deckIndex < 0 || deckIndex >= deckCards.Count) return false;

            StoredCard deckData = deckCards[deckIndex];
            GameObject deckObject = deckVisuals[deckIndex];
            CardVisual incomingVisual = deckObject != null ? deckObject.GetComponent<CardVisual>() : null;
            CardVisual outgoingVisual = cards[cardIndex];
            if (deckData == null || incomingVisual == null || outgoingVisual == null) return false;

            TransferOrSwapCompatibleEquipment(deckData, currentData);

            currentData.IsStoredInDeck = true;
            currentData.DeckSlot = deckData.DeckSlot;
            deckData.IsStoredInDeck = false;
            deckData.DeckSlot = -1;
            deckCards[deckIndex] = currentData;
            currentPackCards[cardIndex] = deckData;

            GameObject outgoingObject = outgoingVisual.gameObject;
            outgoingObject.transform.SetParent(deckRoot, true);
            outgoingObject.SetActive(true);
            SetStoredVisualShadowMode(outgoingObject);
            deckVisuals[deckIndex] = outgoingObject;

            deckObject.transform.SetParent(cardStack, false);
            deckObject.SetActive(true);
            incomingVisual.PrepareFaceUp(CardHome, CurrentRevealedCardScale, 0f);
            incomingVisual.SetFaceDetailsVisible(true);
            cards[cardIndex] = incomingVisual;
            incomingVisual.SetDisplayName(GetStoredCardDisplayName(deckData));
            incomingVisual.SetDisplayDescription(deckData.Data, GetStoredCardDisplayDescription(deckData), IsEnglishUi);
            RefreshDeckCardDisplayNames();
            LayoutDeckVisuals();
            return true;
        }

        private void TransferOrSwapCompatibleEquipment(StoredCard source, StoredCard target)
        {
            if (source == null || source.Data == null || target == null || target.Data == null) return;
            bool changed = false;
            if (source.EquippedMagic != null && target.Data.CanEquipMagic)
            {
                StoredCard targetMagic = target.EquippedMagic;
                target.EquippedMagic = source.EquippedMagic;
                source.EquippedMagic = targetMagic;
                changed = true;
            }
            if (source.EquippedWeapon != null && target.Data.CanEquipWeapon)
            {
                StoredCard targetWeapon = target.EquippedWeapon;
                target.EquippedWeapon = source.EquippedWeapon;
                source.EquippedWeapon = targetWeapon;
                changed = true;
            }
            if (changed) PlayMagicEquipSound();
        }

        private void MoveDeckCardToSlot(int sourceIndex, int targetSlot)
        {
            if (sourceIndex < 0 || sourceIndex >= deckCards.Count || targetSlot < 0 || targetSlot >= 5) return;
            StoredCard source = deckCards[sourceIndex];
            if (source == null || source.DeckSlot == targetSlot) return;

            int sourceSlot = source.DeckSlot;
            int targetIndex = GetDeckIndexAtSlot(targetSlot);
            if (targetIndex >= 0 && targetIndex != sourceIndex && deckCards[targetIndex] != null)
                deckCards[targetIndex].DeckSlot = sourceSlot;
            source.DeckSlot = targetSlot;
        }

        private static void SetStoredVisualShadowMode(GameObject visual)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }
        }
        private IEnumerator ReturnInspectedDeckCard(GameObject selected)
        {
            if (selected == null)
            {
                deckInspectionReturnRoutine = null;
                yield break;
            }

            deckInspectionReturning = true;
            Quaternion startRotation = selected.transform.rotation;
            const float duration = 0.38f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                Camera camera = Camera.main;
                if (camera == null) break;
                float u = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                selected.transform.rotation = Quaternion.Slerp(startRotation, camera.transform.rotation, u);
                yield return null;
            }
            Camera finalCamera = Camera.main;
            if (selected != null && finalCamera != null) selected.transform.rotation = finalCamera.transform.rotation;
            deckInspectionReturning = false;
            deckInspectionReturnRoutine = null;
        }
        private void OpenDeckInspection(int index)
        {
            if (index < 0 || index >= deckVisuals.Count) return;
            inspectedDeckIndex = index;
            discardConfirmationVisible = false;
            deckInspectionDragging = false;
            deckInspectionReturning = false;
            deckInspectionPressOutside = false;
            deckInspectionHasDragged = false;
            inspectionPackWasActive = pack != null && pack.gameObject.activeSelf;
            inspectionStackWasActive = cardStack != null && cardStack.gameObject.activeSelf;
            if (pack != null) pack.gameObject.SetActive(false);
            if (cardStack != null) cardStack.gameObject.SetActive(false);
            if (phase == RevealPhase.PackChoice)
            {
                if (leftPackChoiceVisual != null) leftPackChoiceVisual.gameObject.SetActive(false);
                if (rightPackChoiceVisual != null) rightPackChoiceVisual.gameObject.SetActive(false);
            }
            if (packContentsPreviewVisual != null)
                packContentsPreviewVisual.gameObject.SetActive(false);
            if (deckInspectionBackdrop != null) deckInspectionBackdrop.SetActive(true);
            LayoutDeckVisuals();
        }

        private void CloseDeckInspection()
        {
            if (deckInspectionReturnRoutine != null) StopCoroutine(deckInspectionReturnRoutine);
            deckInspectionReturnRoutine = null;
            deckInspectionDragging = false;
            deckInspectionReturning = false;
            deckInspectionPressOutside = false;
            deckInspectionHasDragged = false;
            discardConfirmationVisible = false;
            inspectedDeckIndex = -1;
            if (pack != null) pack.gameObject.SetActive(inspectionPackWasActive);
            if (cardStack != null) cardStack.gameObject.SetActive(inspectionStackWasActive);
            if (phase == RevealPhase.PackChoice && inspectedPackChoice == null)
            {
                if (leftPackChoiceVisual != null) leftPackChoiceVisual.gameObject.SetActive(true);
                if (rightPackChoiceVisual != null) rightPackChoiceVisual.gameObject.SetActive(true);
            }
            if (deckInspectionBackdrop != null) deckInspectionBackdrop.SetActive(false);
            if (inspectedPackChoice != null && packContentsPreviewVisual != null)
                packContentsPreviewVisual.gameObject.SetActive(true);
            for (int i = 0; i < deckVisuals.Count; i++)
                if (deckVisuals[i] != null) deckVisuals[i].SetActive(true);
            LayoutDeckVisuals();
        }

        private static Rect GetVisualScreenRect(GameObject visual, Camera camera)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return Rect.zero;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 screen = camera.WorldToScreenPoint(center + Vector3.Scale(extents, new Vector3(x, y, z)));
                float guiY = Screen.height - screen.y;
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, guiY);
                maxY = Mathf.Max(maxY, guiY);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }
        private void DrawScorePopups(float scale, float offsetX, float offsetY)
        {
            if (scorePopupStyle == null)
            {
                scorePopupStyle = new GUIStyle(GUI.skin.label)
                {
                    font = font,
                    fontSize = 25,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Overflow
                };
                scorePopupStyle.normal.textColor = Color.white;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));

            const float portraitPopupStartX = 558f;
            const float portraitPopupEndX = 532f;
            const float portraitPopupWidth = 176f;

            TextAnchor previousAlignment = scorePopupStyle.alignment;
            TextClipping previousClipping = scorePopupStyle.clipping;
            bool previousWordWrap = scorePopupStyle.wordWrap;
            int previousFontSize = scorePopupStyle.fontSize;
            if (IsPortraitUi)
            {
                scorePopupStyle.alignment = TextAnchor.MiddleLeft;
                scorePopupStyle.clipping = TextClipping.Clip;
                scorePopupStyle.wordWrap = false;
                scorePopupStyle.fontSize = 22;
            }
            for (int i = scorePopups.Count - 1; i >= 0; i--)
            {
                ScorePopup popup = scorePopups[i];
                float age = (Time.unscaledTime - popup.StartTime) * Mathf.Max(1f, popup.PlaybackSpeed);
                if (age < 0f) continue;
                if (age >= 1.35f)
                {
                    scorePopups.RemoveAt(i);
                    continue;
                }

                float enter = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.18f));
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 1.35f, age));
                float x = Mathf.Lerp(IsPortraitUi ? portraitPopupStartX : 783f,
                    IsPortraitUi ? portraitPopupEndX : 837f, enter);
                float y = (IsPortraitUi ? 350f + PortraitExtraHeight * 0.35f : 270f) + popup.Lane * 72f - Mathf.Clamp01(age / 1.35f) * 24f;
                GUI.color = new Color(popup.Color.r, popup.Color.g, popup.Color.b, fade);
                if (IsPortraitUi)
                {
                    const int maximumPortraitFontSize = 22;
                    scorePopupStyle.fontSize = maximumPortraitFontSize;
                    string[] popupLines = popup.Text.Split('\n');
                    float widestLine = 0f;
                    for (int lineIndex = 0; lineIndex < popupLines.Length; lineIndex++)
                        widestLine = Mathf.Max(widestLine,
                            scorePopupStyle.CalcSize(new GUIContent(popupLines[lineIndex])).x);
                    if (widestLine > portraitPopupWidth)
                        scorePopupStyle.fontSize = Mathf.Max(1, Mathf.FloorToInt(
                            maximumPortraitFontSize * portraitPopupWidth / widestLine));
                }
                GUI.Label(new Rect(x, y, IsPortraitUi ? portraitPopupWidth : 210f, 76f), popup.Text, scorePopupStyle);
            }
            scorePopupStyle.alignment = previousAlignment;
            scorePopupStyle.clipping = previousClipping;
            scorePopupStyle.wordWrap = previousWordWrap;
            scorePopupStyle.fontSize = previousFontSize;
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }
        private bool DrawSettingsButton(float scale, float offsetX, float offsetY)
        {
            EnsureDiscardStyles();
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            Rect settingsButtonRect = UiRect(new Rect(1060f, 28f, 120f, 54f), new Rect(572f, 4f, 120f, 54f));
            bool clicked = GUI.Button(settingsButtonRect, Ui("\uC124\uC815", "Settings"), discardButtonStyle);
            bool consumed = clicked || Event.current.type == EventType.Used;
            GUI.matrix = previousMatrix;
            if (clicked)
            {
                abandonConfirmationVisible = false;
                settingsOpen = true;
            }
            return consumed;
        }

        private void DrawSettingsOverlay(float scale, float offsetX, float offsetY)
        {
            EnsureDiscardStyles();
            if (settingsTitleStyle == null)
            {
                settingsTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    font = font,
                    fontSize = 38,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.black }
                };
                settingsLabelStyle = new GUIStyle(settingsTitleStyle)
                {
                    fontSize = 23,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            GUI.color = new Color(0f, 0f, 0f, 0.68f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));

            if (abandonConfirmationVisible)
            {
                GUI.Box(UiRect(new Rect(390f, 220f, 500f, 280f), new Rect(70f, 430f, 580f, 360f)),
                    GUIContent.none, discardPanelStyle);
                GUI.Label(UiRect(new Rect(430f, 245f, 420f, 100f), new Rect(110f, 475f, 500f, 120f)),
                    Ui("\uB3C4\uC804\uC744 \uD3EC\uAE30\uD560\uAE4C\uC694?\n\uD604\uC7AC \uACB0\uACFC \uD654\uBA74\uC73C\uB85C \uC774\uB3D9\uD569\uB2C8\uB2E4.",
                        "Abandon this run?\nYou will move to the current result."),
                    discardMessageStyle);
                if (GUI.Button(UiRect(new Rect(445f, 390f, 170f, 58f), new Rect(120f, 650f, 210f, 68f)),
                    Ui("\uD3EC\uAE30", "Abandon"), discardButtonStyle))
                {
                    GUI.matrix = previousMatrix;
                    AbandonChallengeToResults();
                    return;
                }
                if (GUI.Button(UiRect(new Rect(665f, 390f, 170f, 58f), new Rect(390f, 650f, 210f, 68f)),
                    Ui("\uCDE8\uC18C", "Cancel"), discardButtonStyle))
                    abandonConfirmationVisible = false;
                GUI.color = previousColor;
                GUI.matrix = previousMatrix;
                return;
            }

            bool canAbandonChallenge = phase != RevealPhase.GameOver && phase != RevealPhase.RunCleared;
            Rect settingsPanelRect = canAbandonChallenge
                ? UiRect(new Rect(390f, 115f, 500f, 535f), new Rect(60f, 250f, 600f, 750f))
                : UiRect(new Rect(390f, 145f, 500f, 430f), new Rect(60f, 300f, 600f, 620f));
            GUI.Box(settingsPanelRect, GUIContent.none, discardPanelStyle);
            GUI.Label(canAbandonChallenge
                    ? UiRect(new Rect(440f, 140f, 400f, 58f), new Rect(110f, 285f, 500f, 70f))
                    : UiRect(new Rect(440f, 170f, 400f, 58f), new Rect(110f, 335f, 500f, 70f)),
                Ui("\uC124\uC815", "Settings"), settingsTitleStyle);

            GUI.Label(UiRect(new Rect(455f, 250f, 180f, 44f), new Rect(105f, 445f, 220f, 50f)), Ui("\uC5B8\uC5B4", "Language"), settingsLabelStyle);
            if (GUI.Button(UiRect(new Rect(455f, 300f, 170f, 52f), new Rect(105f, 510f, 230f, 62f)),
                (uiLanguage == 0 ? "\u25CF " : string.Empty) + "\uD55C\uAD6D\uC5B4", discardButtonStyle))
                SetUiLanguage(0);
            if (GUI.Button(UiRect(new Rect(655f, 300f, 170f, 52f), new Rect(385f, 510f, 230f, 62f)),
                (uiLanguage == 1 ? "\u25CF " : string.Empty) + "English", discardButtonStyle))
                SetUiLanguage(1);

            GUI.Label(UiRect(new Rect(455f, 380f, 260f, 44f), new Rect(105f, 635f, 320f, 50f)),
                Ui("\uC74C\uB7C9  ", "Volume  ") + Mathf.RoundToInt(masterVolume * 100f) + "%", settingsLabelStyle);
            float changedVolume = GUI.HorizontalSlider(UiRect(new Rect(455f, 438f, 370f, 28f), new Rect(105f, 710f, 510f, 34f)), masterVolume, 0f, 1f);
            if (!Mathf.Approximately(changedVolume, masterVolume)) SetMasterVolume(changedVolume);

            if (canAbandonChallenge
                && GUI.Button(UiRect(new Rect(530f, 500f, 220f, 58f), new Rect(220f, 810f, 280f, 68f)),
                    Ui("\uB3C4\uC804 \uD3EC\uAE30", "Abandon Run"), discardButtonStyle))
                abandonConfirmationVisible = true;

            Rect closeRect = canAbandonChallenge
                ? UiRect(new Rect(555f, 575f, 170f, 52f), new Rect(245f, 900f, 230f, 64f))
                : UiRect(new Rect(555f, 500f, 170f, 52f), new Rect(245f, 810f, 230f, 64f));
            if (GUI.Button(closeRect, Ui("\uB2EB\uAE30", "Close"), discardButtonStyle))
            {
                abandonConfirmationVisible = false;
                settingsOpen = false;
            }
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }
        private void AbandonChallengeToResults()
        {
            CommitPendingScoreImmediately();
            StopAllCoroutines();
            settingsOpen = false;
            abandonConfirmationVisible = false;
            if (sharedPackPreviewActive)
            {
                ReturnToSharedResultAfterPackPreview();
                return;
            }
            sharedResultMode = false;
            shareFeedback = null;
            scorePopups.Clear();
            packTearInProgress = false;
            gestureDragging = false;
            inspectionDragging = false;
            transitionDragActive = false;
            transitionSwipeCommitted = false;
            queuedCardSwipes = 0;
            cardTransitionActive = false;
            activeSlidingCard = null;
            ClearPackChoiceVisuals();
            ClearCards();
            if (pack != null) pack.gameObject.SetActive(false);
            if (cardStack != null) cardStack.gameObject.SetActive(false);
            phase = RevealPhase.GameOver;
            LayoutDeckVisuals();
        }
        private void DrawScore(float scale, float offsetX, float offsetY)
        {
            if (scoreStyle == null)
            {
                scoreStyle = new GUIStyle(GUI.skin.label)
                {
                    font = font,
                    fontSize = 30,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
                scoreStyle.normal.textColor = Color.white;
            }
            if (goalStyle == null)
            {
                goalStyle = new GUIStyle(GUI.skin.label)
                {
                    font = font,
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft
                };
                goalStyle.normal.textColor = Color.white;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            string scoreText = Ui("\uCD1D\uC810  ", "Total  ") + totalScore.ToString("N0");
            if (pendingScore > 0) scoreText += "  + " + pendingScore.ToString("N0");
            GUI.Label(UiRect(new Rect(24f, 18f, 440f, 48f), new Rect(24f, 0f, 440f, 48f)), scoreText, scoreStyle);
            int goalIndex = Mathf.Clamp(currentGoalIndex, 0, GoalScores.Length - 1);
            bool runEnded = phase == RevealPhase.GameOver || phase == RevealPhase.RunCleared;
            int openedPacksInGoal = completedPacks % PacksPerGoal + (currentPackOpenedForGoal ? 1 : 0);
            int packsRemaining = runEnded ? 0 : Mathf.Max(0, PacksPerGoal - openedPacksInGoal);
            string goalText = Ui("\uB77C\uC6B4\uB4DC \uC810\uC218  ", "Round score  ")
                + roundScore.ToString("N0") + " / " + GoalScores[goalIndex].ToString("N0")
                + Ui("\uC810\n\uB0A8\uC740 \uD329 ", " pts\nPacks left ") + packsRemaining;
            GUI.Label(UiRect(new Rect(24f, 64f, 440f, 58f), new Rect(24f, 48f, 440f, 58f)), goalText, goalStyle);
            GUI.matrix = previousMatrix;
        }

        private void DrawPackChoice(float scale, float offsetX, float offsetY)
        {
            EnsureDiscardStyles();
            if (packChoiceTitleStyle == null)
            {
                packChoiceTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    font = font,
                    fontSize = 32,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                packChoiceTitleStyle.hover.textColor = Color.white;
                packChoiceTitleStyle.active.textColor = Color.white;
            }

            if (inspectedPackChoice != null)
            {
                DrawActualPackContentsOverlay(scale, offsetX, offsetY);
                return;
            }

            Camera camera = Camera.main;
            if (camera == null || leftPackChoiceVisual == null || rightPackChoiceVisual == null) return;

            if (IsPortraitUi)
            {
                float choiceDepth = camera.WorldToScreenPoint(PackHome).z;
                float choiceScreenY = Screen.height - (offsetY + (550f + PortraitExtraHeight * 0.5f) * scale);
                leftPackChoiceVisual.transform.position = camera.ScreenToWorldPoint(
                    new Vector3(offsetX + 190f * scale, choiceScreenY, choiceDepth));
                rightPackChoiceVisual.transform.position = camera.ScreenToWorldPoint(
                    new Vector3(offsetX + 530f * scale, choiceScreenY, choiceDepth));
            }
            else
            {
                leftPackChoiceVisual.transform.position = new Vector3(-1.8f, 0.55f, -0.65f);
                rightPackChoiceVisual.transform.position = new Vector3(1.8f, 0.55f, -0.65f);
            }

            Rect leftRect = GetVisualScreenRect(leftPackChoiceVisual.gameObject, camera);
            Rect rightRect = GetVisualScreenRect(rightPackChoiceVisual.gameObject, camera);
            Vector2 mousePosition = Event.current.mousePosition;
            bool leftHovered = leftRect.Contains(mousePosition);
            bool rightHovered = rightRect.Contains(mousePosition);
            float leftScale = ResponsiveWorldScale(leftHovered ? 1.58f : 1.45f, leftHovered ? 1.25f : 1.18f);
            float rightScale = ResponsiveWorldScale(rightHovered ? 1.58f : 1.45f, rightHovered ? 1.25f : 1.18f);
            leftPackChoiceVisual.transform.localScale = Vector3.one * leftScale;
            rightPackChoiceVisual.transform.localScale = Vector3.one * rightScale;

            if (Event.current.type == EventType.MouseDown && leftHovered)
            {
                Event.current.Use();
                SelectPackChoice(leftPackChoice);
                return;
            }
            if (Event.current.type == EventType.MouseDown && rightHovered)
            {
                Event.current.Use();
                SelectPackChoice(rightPackChoice);
                return;
            }

            Rect leftInfoButtonRect = new Rect(
                (leftRect.center.x - offsetX) / scale - 27f, (leftRect.yMin - offsetY) / scale - 62f, 54f, 54f);
            Rect rightInfoButtonRect = new Rect(
                (rightRect.center.x - offsetX) / scale - 27f, (rightRect.yMin - offsetY) / scale - 62f, 54f, 54f);

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            GUI.Label(UiRect(new Rect(390f, 72f, 500f, 52f), new Rect(110f, 150f, 500f, 52f)), Ui("\uB2E4\uC74C \uD329\uC744 \uC120\uD0DD\uD558\uC138\uC694", "Choose the next pack"), packChoiceTitleStyle);

            if (GUI.Button(leftInfoButtonRect, "?", discardButtonStyle))
                OpenPackContents(leftPackChoice);
            if (GUI.Button(rightInfoButtonRect, "?", discardButtonStyle))
                OpenPackContents(rightPackChoice);
            GUI.matrix = previousMatrix;
        }

        private void OpenPackContents(global::CardPackData packData)
        {
            if (packData == null) return;
            inspectedPackChoice = packData;
            packContentsScroll = Vector2.zero;
            packContentsPreviewIndex = 0;
            packContentsPackWasActive = pack != null && pack.gameObject.activeSelf;
            packContentsStackWasActive = cardStack != null && cardStack.gameObject.activeSelf;
            if (leftPackChoiceVisual != null) leftPackChoiceVisual.gameObject.SetActive(false);
            if (rightPackChoiceVisual != null) rightPackChoiceVisual.gameObject.SetActive(false);
            if (pack != null) pack.gameObject.SetActive(false);
            if (cardStack != null) cardStack.gameObject.SetActive(false);
            BuildPackContentsPreviewCard();
        }

        private void ClosePackContents()
        {
            ClearPackContentsPreview();
            inspectedPackChoice = null;
            if (phase == RevealPhase.PackChoice)
            {
                if (leftPackChoiceVisual != null) leftPackChoiceVisual.gameObject.SetActive(true);
                if (rightPackChoiceVisual != null) rightPackChoiceVisual.gameObject.SetActive(true);
            }
            else
            {
                if (pack != null) pack.gameObject.SetActive(packContentsPackWasActive);
                if (cardStack != null) cardStack.gameObject.SetActive(packContentsStackWasActive);
            }
        }

        private void DrawPackContentsOverlay(float scale, float offsetX, float offsetY)
        {
            if (packContentsTitleStyle == null)
            {
                packContentsTitleStyle = new GUIStyle(discardMessageStyle)
                {
                    fontSize = 30,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
                packContentsCardStyle = new GUIStyle(discardMessageStyle)
                {
                    fontSize = 17,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
                packContentsTitleStyle.hover.textColor = Color.black;
                packContentsTitleStyle.active.textColor = Color.black;
                packContentsTitleStyle.focused.textColor = Color.black;
                packContentsTitleStyle.onNormal.textColor = Color.black;
                packContentsTitleStyle.onHover.textColor = Color.black;
                packContentsTitleStyle.onActive.textColor = Color.black;
                packContentsTitleStyle.onFocused.textColor = Color.black;
                packContentsCardStyle.hover.textColor = Color.black;
                packContentsCardStyle.active.textColor = Color.black;
                packContentsCardStyle.focused.textColor = Color.black;
                packContentsCardStyle.onNormal.textColor = Color.black;
                packContentsCardStyle.onHover.textColor = Color.black;
                packContentsCardStyle.onActive.textColor = Color.black;
                packContentsCardStyle.onFocused.textColor = Color.black;
            }

            int cardCount = 0;
            if (inspectedPackChoice.IncludeCards != null)
            {
                for (int i = 0; i < inspectedPackChoice.IncludeCards.Count; i++)
                {
                    global::CardPackEntry entry = inspectedPackChoice.IncludeCards[i];
                    if (entry != null && entry.Card != null) cardCount++;
                }
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            GUI.Box(new Rect(85f, 55f, 1110f, 610f), GUIContent.none, discardPanelStyle);
            GUI.Label(new Rect(125f, 75f, 650f, 50f),
                Ui("\uBD09\uC785 \uCE74\uB4DC  ", "Included cards  ") + cardCount.ToString("N0") + Ui("\uC7A5", string.Empty), packContentsTitleStyle);
            if (GUI.Button(new Rect(995f, 75f, 155f, 48f), Ui("\uB2EB\uAE30", "Close"), discardButtonStyle))
            {
                GUI.matrix = previousMatrix;
                ClosePackContents();
                return;
            }

            const int columns = 5;
            const float cellWidth = 170f;
            const float cellHeight = 200f;
            const float gapX = 20f;
            const float gapY = 18f;
            int rows = Mathf.Max(1, Mathf.CeilToInt(cardCount / (float)columns));
            Rect viewport = new Rect(125f, 140f, 1030f, 485f);
            Rect content = new Rect(0f, 0f, 995f, 12f + rows * (cellHeight + gapY));
            packContentsScroll = GUI.BeginScrollView(viewport, packContentsScroll, content);

            int visibleIndex = 0;
            if (inspectedPackChoice.IncludeCards != null)
            {
                for (int i = 0; i < inspectedPackChoice.IncludeCards.Count; i++)
                {
                    global::CardPackEntry entry = inspectedPackChoice.IncludeCards[i];
                    if (entry == null || entry.Card == null) continue;
                    int column = visibleIndex % columns;
                    int row = visibleIndex / columns;
                    float x = 12f + column * (cellWidth + gapX);
                    float y = 8f + row * (cellHeight + gapY);
                    Rect cellRect = new Rect(x, y, cellWidth, cellHeight);
                    GUI.Box(cellRect, GUIContent.none, discardPanelStyle);
                    if (entry.Card.Image != null)
                        GUI.DrawTexture(new Rect(x + 10f, y + 10f, 150f, 142f),
                            entry.Card.Image, ScaleMode.ScaleToFit, true);
                    string localizedName = entry.Card.GetLocalizedName(IsEnglishUi);
                    string cardName = !string.IsNullOrWhiteSpace(localizedName)
                        ? localizedName : entry.Card.name;
                    GUI.Label(new Rect(x + 8f, y + 154f, 154f, 40f), cardName, packContentsCardStyle);
                    visibleIndex++;
                }
            }
            if (cardCount == 0)
                GUI.Label(new Rect(250f, 150f, 500f, 60f), Ui("\uD45C\uC2DC\uD560 \uCE74\uB4DC\uAC00 \uC5C6\uC2B5\uB2C8\uB2E4.", "No cards to display."), packContentsCardStyle);
            GUI.EndScrollView();
            GUI.matrix = previousMatrix;
        }

        private int GetPackContentsCardCount()
        {
            if (inspectedPackChoice == null || inspectedPackChoice.IncludeCards == null) return 0;
            int count = 0;
            for (int i = 0; i < inspectedPackChoice.IncludeCards.Count; i++)
            {
                global::CardPackEntry entry = inspectedPackChoice.IncludeCards[i];
                if (entry != null && entry.Card != null) count++;
            }
            return count;
        }

        private global::CardPackEntry GetPackContentsEntry(int visibleIndex)
        {
            if (inspectedPackChoice == null || inspectedPackChoice.IncludeCards == null) return null;
            int current = 0;
            for (int i = 0; i < inspectedPackChoice.IncludeCards.Count; i++)
            {
                global::CardPackEntry entry = inspectedPackChoice.IncludeCards[i];
                if (entry == null || entry.Card == null) continue;
                if (current == visibleIndex) return entry;
                current++;
            }
            return null;
        }

        private static int GetPackContentsRarityNumber(global::CardRarity rarity)
        {
            switch (rarity)
            {
                case global::CardRarity.Uncommon: return 2;
                case global::CardRarity.Rare: return 3;
                case global::CardRarity.Epic: return 4;
                case global::CardRarity.Legendary: return 5;
                default: return 1;
            }
        }

        private void BuildPackContentsPreviewCard()
        {
            ClearPackContentsPreview();
            global::CardPackEntry entry = GetPackContentsEntry(packContentsPreviewIndex);
            if (entry == null || entry.Card == null) return;
            global::CardData data = entry.Card;
            int previewNumber = GetPackContentsRarityNumber(data.Rare);
            global::CardColor previewColor = global::CardColor.Black;
            string previewAttributeKey = previewColor.ToString();

            GameObject cardObject = new GameObject("Pack Contents Preview - " + data.Name);
            CardVisual visual = cardObject.AddComponent<CardVisual>();
            Material attributeMaterial = GetTextureMaterial("Attribute_" + previewAttributeKey,
                "CardAssets/Attributes/Attribute" + previewAttributeKey, false);
            Material rarityPatternMaterial = GetTextureMaterial("Pattern_" + data.RarityAssetKey,
                "CardAssets/Rarities/Pattern" + data.RarityAssetKey, true, 0);
            string costAsset = "Cost" + previewNumber;
            Material costMaterial = GetTextureMaterial("Cost_" + previewNumber,
                "CardAssets/Costs/" + costAsset, true, 20);
            Material illustrationMaterial = GetTextureMaterial(
                "CardImage_" + data.GetHashCode(), data.Image, true, 10);
            visual.BuildFromData(data, previewColor, attributeMaterial,
                GetTextureMaterial("CardBack", "CardAssets/Attributes/AttributeBackRemasterPurple", false),
                rarityPatternMaterial, illustrationMaterial, costMaterial, font, IsEnglishUi);
            visual.PrepareFaceUp(new Vector3(0f, 0.92f, -0.24f), CurrentRevealedCardScale, 0f);
            visual.SetFaceDetailsVisible(true);
            SetStoredVisualShadowMode(cardObject);
            packContentsPreviewVisual = visual;
        }

        private void ClearPackContentsPreview()
        {
            if (packContentsPreviewVisual == null) return;
            packContentsPreviewVisual.gameObject.SetActive(false);
            Destroy(packContentsPreviewVisual.gameObject);
            packContentsPreviewVisual = null;
        }

        private void ChangePackContentsPreview(int direction)
        {
            int count = GetPackContentsCardCount();
            if (count <= 0) return;
            packContentsPreviewIndex = (packContentsPreviewIndex + direction + count) % count;
            BuildPackContentsPreviewCard();
        }

        private void DrawActualPackContentsOverlay(float scale, float offsetX, float offsetY)
        {
            EnsureDiscardStyles();
            if (packContentsTitleStyle == null)
            {
                packContentsTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    font = font,
                    fontSize = 30,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                packContentsCardStyle = new GUIStyle(packContentsTitleStyle)
                {
                    fontSize = 22
                };
            }

            int count = GetPackContentsCardCount();
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            GUI.Label(UiRect(new Rect(390f, 28f, 500f, 52f), new Rect(110f, 28f, 500f, 52f)), Ui("\uBD09\uC785 \uCE74\uB4DC", "Included cards"), packContentsTitleStyle);
            if (GUI.Button(UiRect(new Rect(1060f, 28f, 170f, 52f), new Rect(522f, 95f, 170f, 52f)), Ui("\uB2EB\uAE30", "Close"), discardButtonStyle))
            {
                GUI.matrix = previousMatrix;
                ClosePackContents();
                return;
            }

            if (count > 0)
            {
                if (GUI.Button(UiRect(new Rect(250f, 320f, 150f, 62f), new Rect(20f, 590f, 140f, 68f)), "\u25C0", discardButtonStyle))
                    ChangePackContentsPreview(-1);
                if (GUI.Button(UiRect(new Rect(880f, 320f, 150f, 62f), new Rect(560f, 590f, 140f, 68f)), "\u25B6", discardButtonStyle))
                    ChangePackContentsPreview(1);
                GUI.Label(UiRect(new Rect(490f, 642f, 300f, 42f), new Rect(210f, 1160f, 300f, 42f)),
                    (packContentsPreviewIndex + 1) + " / " + count, packContentsCardStyle);
            }
            else
            {
                GUI.Label(UiRect(new Rect(390f, 320f, 500f, 60f), new Rect(110f, 590f, 500f, 60f)),
                    Ui("\uD45C\uC2DC\uD560 \uCE74\uB4DC\uAC00 \uC5C6\uC2B5\uB2C8\uB2E4.", "No cards to display."), packContentsCardStyle);
            }
            GUI.matrix = previousMatrix;
        }

        private bool DrawActivePackContentsButton(float scale, float offsetX, float offsetY)
        {
            EnsureDiscardStyles();
            Rect buttonRect = UiRect(new Rect(880f, 105f, 54f, 54f), new Rect(638f, 105f, 54f, 54f));
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            bool clicked = GUI.Button(buttonRect, "?", discardButtonStyle);
            GUI.matrix = previousMatrix;
            if (clicked) OpenPackContents(activePackData);
            return clicked || Event.current.type == EventType.Used;
        }

        private void DrawRunEndOverlay(float scale, float offsetX, float offsetY)
        {
            EnsureDiscardStyles();
            if (runEndTitleStyle == null)
            {
                runEndTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    font = font,
                    fontSize = 44,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.black }
                };
                runEndTitleStyle.hover.textColor = Color.black;
                runEndTitleStyle.active.textColor = Color.black;
                runEndTitleStyle.focused.textColor = Color.black;
                runEndTitleStyle.onNormal.textColor = Color.black;
                runEndTitleStyle.onHover.textColor = Color.black;
                runEndTitleStyle.onActive.textColor = Color.black;
                runEndTitleStyle.onFocused.textColor = Color.black;
                runEndBodyStyle = new GUIStyle(runEndTitleStyle)
                {
                    fontSize = 25,
                    fontStyle = FontStyle.Bold,
                    wordWrap = true
                };
                runEndButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    font = font,
                    fontSize = 25,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                runEndButtonStyle.normal.textColor = Color.black;
                runEndButtonStyle.hover.textColor = Color.black;
                runEndButtonStyle.active.textColor = Color.black;
                runEndButtonStyle.focused.textColor = Color.black;
                runEndButtonStyle.border = new RectOffset(12, 12, 12, 12);
                runEndButtonStyle.normal.background = roundedDiscardTexture;
                runEndButtonStyle.hover.background = roundedDiscardTexture;
                runEndButtonStyle.active.background = roundedDiscardTexture;
                runEndButtonStyle.focused.background = roundedDiscardTexture;

                runEndBadgeStyle = new GUIStyle(runEndBodyStyle)
                {
                    fontSize = 18,
                    alignment = TextAnchor.MiddleCenter
                };
                runEndStatLabelStyle = new GUIStyle(runEndBodyStyle)
                {
                    fontSize = 17,
                    alignment = TextAnchor.MiddleCenter
                };
                runEndStatValueStyle = new GUIStyle(runEndBodyStyle)
                {
                    fontSize = 29,
                    alignment = TextAnchor.UpperCenter
                };
                runEndHintStyle = new GUIStyle(runEndBodyStyle)
                {
                    fontSize = 17,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            GUI.Box(UiRect(new Rect(270f, 45f, 740f, 485f), new Rect(50f, 270f, 620f, 650f)), GUIContent.none, discardPanelStyle);

            bool cleared = phase == RevealPhase.RunCleared;
            int goalIndex = Mathf.Clamp(currentGoalIndex, 0, GoalScores.Length - 1);
            int targetScore = GoalScores[goalIndex];
            int reachedStage = cleared ? GoalScores.Length : Mathf.Clamp(currentGoalIndex + 1, 1, GoalScores.Length);
            string title = cleared ? Ui("\uB7F0 \uD074\uB9AC\uC5B4!", "RUN CLEARED!") : Ui("\uAC8C\uC784 \uC624\uBC84", "GAME OVER");
            string resultMessage = cleared
                ? Ui("\uBAA8\uB4E0 \uBAA9\uD45C\uB97C \uB2EC\uC131\uD588\uC2B5\uB2C8\uB2E4.", "All goals cleared.")
                : Ui("\uBAA9\uD45C \uC810\uC218\uC5D0 \uB3C4\uB2EC\uD558\uC9C0 \uBABB\uD588\uC2B5\uB2C8\uB2E4.", "Goal score not reached.");
            string roundValue = cleared
                ? Ui("\uC644\uB8CC", "CLEAR")
                : roundScore.ToString("N0") + " / " + targetScore.ToString("N0");

            if (sharedResultMode)
                GUI.Label(UiRect(new Rect(450f, 58f, 380f, 32f), new Rect(190f, 292f, 340f, 36f)),
                    Ui("\uACF5\uC720\uBC1B\uC740 \uACB0\uACFC", "SHARED RESULT"), runEndBadgeStyle);
            GUI.Label(UiRect(new Rect(320f, 88f, 640f, 70f), new Rect(90f, 330f, 540f, 82f)), title, runEndTitleStyle);
            GUI.Label(UiRect(new Rect(340f, 154f, 600f, 42f), new Rect(90f, 410f, 540f, 48f)), resultMessage, runEndBodyStyle);

            GUI.Label(UiRect(new Rect(320f, 212f, 200f, 30f), new Rect(80f, 485f, 180f, 32f)), Ui("\uCD1D\uC810", "TOTAL SCORE"), runEndStatLabelStyle);
            GUI.Label(UiRect(new Rect(540f, 212f, 200f, 30f), new Rect(270f, 485f, 180f, 32f)), Ui("\uB3C4\uB2EC \uB2E8\uACC4", "STAGE"), runEndStatLabelStyle);
            GUI.Label(UiRect(new Rect(760f, 212f, 200f, 30f), new Rect(460f, 485f, 180f, 32f)), Ui("\uAC1C\uBD09 \uD329", "PACKS"), runEndStatLabelStyle);
            GUI.Label(UiRect(new Rect(320f, 242f, 200f, 48f), new Rect(80f, 520f, 180f, 48f)), totalScore.ToString("N0"), runEndStatValueStyle);
            GUI.Label(UiRect(new Rect(540f, 242f, 200f, 48f), new Rect(270f, 520f, 180f, 48f)), reachedStage + " / " + GoalScores.Length, runEndStatValueStyle);
            GUI.Label(UiRect(new Rect(760f, 242f, 200f, 48f), new Rect(460f, 520f, 180f, 48f)), completedPacks.ToString("N0"), runEndStatValueStyle);

            GUI.Label(UiRect(new Rect(335f, 300f, 610f, 42f), new Rect(80f, 585f, 560f, 62f)),
                Ui("\uB77C\uC6B4\uB4DC \uC810\uC218  ", "ROUND SCORE  ") + roundValue, runEndBodyStyle);
            GUI.Label(UiRect(new Rect(335f, 340f, 610f, 32f), new Rect(80f, 650f, 560f, 54f)),
                Ui("\uC544\uB798 \uB371 \uCE74\uB4DC\uB97C \uB20C\uB7EC \uC0C1\uC138\uD788 \uBCFC \uC218 \uC788\uC5B4\uC694.", "Select a deck card below to inspect it."), runEndHintStyle);

            Rect leftButtonRect = UiRect(new Rect(360f, 400f, 260f, 70f), new Rect(90f, 755f, 250f, 76f));
            Rect rightButtonRect = UiRect(new Rect(660f, 400f, 260f, 70f), new Rect(380f, 755f, 250f, 76f));
            if (sharedResultMode)
            {
                if (GUI.Button(leftButtonRect, Ui("\uD329 \uAE4C\uBCF4\uAE30", "Open a Pack"), runEndButtonStyle))
                {
                    GUI.matrix = previousMatrix;
                    BeginSharedPackPreview();
                    return;
                }
                if (GUI.Button(rightButtonRect, Ui("\uB3C4\uC804\uD558\uAE30", "Challenge"), runEndButtonStyle))
                    StartNewRun();
            }
            else
            {
                if (GUI.Button(leftButtonRect, Ui("\uACF5\uC720", "Share"), runEndButtonStyle))
                    ShareCurrentResult();
                if (GUI.Button(rightButtonRect, Ui("\uB2E4\uC2DC \uC2DC\uC791", "Restart"), runEndButtonStyle))
                    StartNewRun();
            }
            if (!sharedResultMode && !string.IsNullOrEmpty(shareFeedback) && Time.unscaledTime < shareFeedbackUntil)
                GUI.Label(UiRect(new Rect(340f, 478f, 600f, 38f), new Rect(85f, 842f, 550f, 42f)), shareFeedback, runEndHintStyle);
            GUI.matrix = previousMatrix;
        }
        private void DrawDeckInspectionControls(float scale, float offsetX, float offsetY)
        {
            EnsureDiscardStyles();
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));

            if (inspectedDeckIndex >= 0 && inspectedDeckIndex < deckCards.Count
                && deckCards[inspectedDeckIndex] != null)
            {
                StoredCard inspectedCard = deckCards[inspectedDeckIndex];
                GUI.color = GetRarityDisplayColor(inspectedCard.Rarity);
                GUI.Label(UiRect(new Rect(490f, 18f, 300f, 48f), new Rect(210f, 205f + PortraitExtraHeight * 0.5f, 300f, 52f)),
                    GetRarityDisplayName(inspectedCard.Rarity), deckRarityStyle);
                GUI.color = previousColor;
                string progressText = GetDeckProgressText(inspectedCard);
                if (!string.IsNullOrEmpty(progressText))
                {
                    EnsureDeckStatusStyles();
                    DrawStatusLabelWithShadow(UiRect(new Rect(855f, 270f, 390f, 120f), new Rect(150f, 950f + PortraitExtraHeight * 0.5f, 420f, 150f)),
                        progressText, deckInspectionStatusStyle, new Color(0.55f, 0.95f, 1f));
                }
            }

            if (IsDeckInspectionReadOnly())
            {
                discardConfirmationVisible = false;
            }
            else if (!discardConfirmationVisible)
            {
                if (GUI.Button(UiRect(new Rect(550f, 646f, 180f, 52f), new Rect(270f, 1115f + PortraitExtraHeight * 0.5f, 180f, 62f)), Ui("\uCE74\uB4DC \uBC84\uB9AC\uAE30", "Discard card"), discardButtonStyle))
                    discardConfirmationVisible = true;
            }
            else
            {
                Rect panelRect = UiRect(new Rect(430f, 252f, 420f, 206f), new Rect(70f, 470f, 580f, 300f));
                GUI.Box(panelRect, GUIContent.none, discardPanelStyle);
                GUI.Label(UiRect(new Rect(455f, 278f, 370f, 64f), new Rect(110f, 515f, 500f, 80f)), Ui("\uC774 \uCE74\uB4DC\uB97C \uBC84\uB9B4\uAE4C\uC694?", "Discard this card?"), discardMessageStyle);
                if (GUI.Button(UiRect(new Rect(480f, 370f, 140f, 52f), new Rect(130f, 650f, 190f, 64f)), Ui("\uBC84\uB9AC\uAE30", "Discard"), discardButtonStyle))
                    DiscardInspectedDeckCard();
                if (GUI.Button(UiRect(new Rect(660f, 370f, 140f, 52f), new Rect(400f, 650f, 190f, 64f)), Ui("\uCDE8\uC18C", "Cancel"), discardButtonStyle))
                    discardConfirmationVisible = false;
            }

            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private string GetDeckProgressText(StoredCard card, bool includeEquipment = true)
        {
            if (card == null || card.Data == null || card.Data.DeckAbilities == null) return string.Empty;
            List<string> statusLines = new List<string>();
            if (includeEquipment && card.EquippedMagic != null && card.EquippedMagic.Data != null)
                statusLines.Add(Ui("마법: ", "Magic: ")
                    + card.EquippedMagic.Data.GetLocalizedName(IsEnglishUi));
            if (includeEquipment && card.EquippedWeapon != null && card.EquippedWeapon.Data != null)
                statusLines.Add(Ui("무기: ", "Weapon: ")
                    + card.EquippedWeapon.Data.GetLocalizedName(IsEnglishUi));
            int effectiveCopies = GetEffectiveDeckCopyCount(card);
            for (int i = 0; i < card.Data.DeckAbilities.Count; i++)
            {
                global::CardDeckAbility ability = card.Data.DeckAbilities[i];
                if (ability == null) continue;
                if (ability.Effect == global::DeckAbilityEffect.TransformAfterPacks)
                {
                    card.PacksElapsedByAbility.TryGetValue(i, out int elapsedPacks);
                    statusLines.Add(elapsedPacks + "/" + Mathf.Max(1, ability.PacksToTransform));
                }
                if (ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusPerDraw
                    || ability.Effect == global::DeckAbilityEffect.AccumulatePercentAtStackThreshold
                    || ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusEfficiencyByNumber)
                {
                    card.AccumulatedPercentByAbility.TryGetValue(i, out float accumulatedPercent);
                    statusLines.Add(accumulatedPercent.ToString("0.#") + "%");
                }
                if (ability.Effect == global::DeckAbilityEffect.GrantTemporaryPercentForNextDraws)
                {
                    card.RemainingDrawsByAbility.TryGetValue(i, out int remainingDraws);
                    if (remainingDraws > 0)
                        statusLines.Add(remainingDraws + Ui("\uD68C", " uses"));
                }
                if (ability.Effect == global::DeckAbilityEffect.AccumulateFlatScorePerDraw)
                {
                    card.AccumulatedFlatScoreByAbility.TryGetValue(i, out int accumulatedScore);
                    statusLines.Add(accumulatedScore + Ui("\uC810", " pts"));
                }
                if (IsStackThresholdEffect(ability.Effect)
                    || ability.Effect == global::DeckAbilityEffect.AddScoreEveryOtherCardScoreEvents)
                {
                    int threshold = Mathf.Max(1, ability.StackThreshold);
                    card.StackByAbilityCopy.TryGetValue(GetAbilityCopyKey(i, 0), out int currentStacks);
                    string stackText = currentStacks + "/" + threshold;
                    if (effectiveCopies > 1) stackText += " \u00D7" + effectiveCopies;
                    statusLines.Add(stackText);
                    if (ability.Effect == global::DeckAbilityEffect.AddSpecificCardAtStackThreshold
                        && ability.MaxTriggersPerPack > 0)
                    {
                        card.PerPackTriggerCountByAbility.TryGetValue(i, out int usedThisPack);
                        statusLines.Add(usedThisPack + Ui("\uD68C", " uses"));
                    }
                }
            }
            for (int i = 0; i < card.InheritedRelics.Count; i++)
            {
                StoredCard relic = card.InheritedRelics[i];
                string relicProgress = GetDeckProgressText(relic, false);
                if (string.IsNullOrWhiteSpace(relicProgress)) continue;
                string relicName = GetInheritedRelicShortName(relic);
                statusLines.Add(relicName + " " + relicProgress.Replace("\n", " / "));
            }
            return string.Join("\n", statusLines);
        }

        private string GetInheritedRelicShortName(StoredCard relic)
        {
            if (relic == null || relic.Data == null) return Ui("\uC870\uB9BD \uC720\uBB3C", "Relic");
            switch (relic.Data.name)
            {
                case "MagicScrew": return Ui("\uB098\uC0AC", "Screw");
                case "MagicWheel": return Ui("\uBC14\uD034", "Wheel");
                case "MagicBattery": return Ui("\uBC30\uD130\uB9AC", "Battery");
                case "MagicEngine": return Ui("\uC5D4\uC9C4", "Engine");
                default: return relic.Data.GetLocalizedName(IsEnglishUi);
            }
        }
        private static void DrawStatusLabelWithShadow(Rect rect, string text, GUIStyle style, Color color)
        {
            GUIStyle drawStyle = style;
            if (!style.wordWrap && style.fontSize > 0 && rect.width > 4f)
            {
                float availableWidth = rect.width - 4f;
                float maxLineWidth = 0f;
                string[] lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    float lineWidth = style.CalcSize(new GUIContent(lines[i])).x;
                    maxLineWidth = Mathf.Max(maxLineWidth, lineWidth);
                }
                if (maxLineWidth > availableWidth)
                {
                    drawStyle = new GUIStyle(style);
                    drawStyle.fontSize = Mathf.Max(9,
                        Mathf.FloorToInt(style.fontSize * availableWidth / maxLineWidth) - 1);
                }
            }

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, drawStyle);
            GUI.color = color;
            GUI.Label(rect, text, drawStyle);
            GUI.color = previousColor;
        }

        private void EnsureDeckStatusStyles()
        {
            if (deckStatusStyle != null) return;
            deckStatusStyle = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                wordWrap = false,
                normal = { textColor = Color.white }
            };
            deckInspectionStatusStyle = new GUIStyle(deckStatusStyle)
            {
                fontSize = 24,
                alignment = TextAnchor.MiddleLeft
            };
        }

        private string GetRarityDisplayName(global::CardRarity rarity)
        {
            switch (rarity)
            {
                case global::CardRarity.Uncommon: return Ui("\uACE0\uAE09", "Uncommon");
                case global::CardRarity.Rare: return Ui("\uD76C\uADC0", "Rare");
                case global::CardRarity.Epic: return Ui("\uC601\uC6C5", "Epic");
                case global::CardRarity.Legendary: return Ui("\uC804\uC124", "Legendary");
                default: return Ui("\uC77C\uBC18", "Common");
            }
        }

        private static Color GetRarityDisplayColor(global::CardRarity rarity)
        {
            switch (rarity)
            {
                case global::CardRarity.Uncommon: return new Color(0.45f, 1f, 0.72f);
                case global::CardRarity.Rare: return new Color(0.72f, 0.88f, 1f);
                case global::CardRarity.Epic: return new Color(0.72f, 0.30f, 1f);
                case global::CardRarity.Legendary: return new Color(1f, 0.72f, 0.20f);
                default: return Color.white;
            }
        }
        private void EnsureDiscardStyles()
        {
            if (discardButtonStyle != null) return;
            roundedDiscardTexture = CreateRoundedBorderTexture(40, 10f, 3f);
            discardButtonStyle = new GUIStyle(GUI.skin.button)
            {
                font = font,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(12, 12, 12, 12),
                normal = { background = roundedDiscardTexture, textColor = Color.black },
                hover = { background = roundedDiscardTexture, textColor = Color.black },
                active = { background = roundedDiscardTexture, textColor = Color.black }
            };
            discardPanelStyle = new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(12, 12, 12, 12),
                normal = { background = roundedDiscardTexture }
            };
            discardMessageStyle = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.black }
            };
            deckRarityStyle = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
        }

        private static Texture2D CreateRoundedBorderTexture(int size, float radius, float borderWidth)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Rounded Black White Button",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color[] pixels = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f - half;
                float py = y + 0.5f - half;
                float outerX = Mathf.Max(Mathf.Abs(px) - (half - radius), 0f);
                float outerY = Mathf.Max(Mathf.Abs(py) - (half - radius), 0f);
                bool insideOuter = outerX * outerX + outerY * outerY <= radius * radius;

                float innerHalf = half - borderWidth;
                float innerRadius = Mathf.Max(1f, radius - borderWidth);
                float innerX = Mathf.Max(Mathf.Abs(px) - (innerHalf - innerRadius), 0f);
                float innerY = Mathf.Max(Mathf.Abs(py) - (innerHalf - innerRadius), 0f);
                bool insideInner = Mathf.Abs(px) <= innerHalf && Mathf.Abs(py) <= innerHalf
                    && innerX * innerX + innerY * innerY <= innerRadius * innerRadius;
                pixels[y * size + x] = !insideOuter ? Color.clear : insideInner ? Color.white : Color.black;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }



        private void DiscardInspectedDeckCard()
        {
            int index = inspectedDeckIndex;
            if (index < 0 || index >= deckCards.Count || index >= deckVisuals.Count) return;
            GameObject discardedVisual = deckVisuals[index];
            StoredCard discardedCard = deckCards[index];
            if (discardedCard != null)
            {
                discardedCard.IsStoredInDeck = false;
                discardedCard.DeckSlot = -1;
                if (discardedCard.EquippedMagic != null)
                {
                    discardedCard.EquippedMagic.IsStoredInDeck = false;
                    discardedCard.EquippedMagic.DeckSlot = -1;
                }
                if (discardedCard.EquippedWeapon != null)
                {
                    discardedCard.EquippedWeapon.IsStoredInDeck = false;
                    discardedCard.EquippedWeapon.DeckSlot = -1;
                }
            }
            deckCards.RemoveAt(index);
            deckVisuals.RemoveAt(index);
            if (discardedVisual != null) Destroy(discardedVisual);
            RefreshDeckCardDisplayNames();
            discardConfirmationVisible = false;
            CloseDeckInspection();
        }
        private void DrawDeck(float scale, float offsetX, float offsetY)
        {
            if (deckHeaderStyle == null)
            {
                deckHeaderStyle = new GUIStyle(GUI.skin.label)
                {
                    font = font,
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = Color.white }
                };
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            bool resultScreen = phase == RevealPhase.GameOver || phase == RevealPhase.RunCleared;
            Rect deckHeaderRect = resultScreen && !IsPortraitUi
                ? new Rect(470f, 545f, 260f, 34f)
                : UiRect(new Rect(24f, 516f, 260f, 34f), new Rect(75f, 975f + PortraitExtraHeight, 260f, 42f));
            GUI.Label(deckHeaderRect, Ui("\uB371  ", "Deck  ") + deckCards.Count + "/5", deckHeaderStyle);
            EnsureDeckStatusStyles();
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard card = deckCards[i];
                if (card == null) continue;
                string progressText = GetDeckProgressText(card, false);
                if (string.IsNullOrEmpty(progressText)) continue;
                int slot = Mathf.Clamp(card.DeckSlot, 0, 4);
                int progressLineCount = progressText.Split('\n').Length;
                float portraitStatusExtraHeight = Mathf.Max(0, progressLineCount - 1) * 22f;
                Rect statusRect = IsPortraitUi
                    ? new Rect(90f + slot * 110f,
                        1050f + PortraitExtraHeight - portraitStatusExtraHeight,
                        100f, 48f + portraitStatusExtraHeight)
                    : (resultScreen ? new Rect(430f + slot * 85f, 680f, 80f, 40f) : new Rect(14f + slot * 74.25f, 674f, 80f, 40f));
                DrawStatusLabelWithShadow(statusRect,
                    progressText, deckStatusStyle, new Color(0.55f, 0.95f, 1f));
            }

            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }
        private void DrawPackTearGuide(float scale, float offsetX, float offsetY)
        {
            if (phase != RevealPhase.Pack || controlGuideOpen) return;
            if (packGuideStyle == null)
            {
                packGuideStyle = new GUIStyle(GUI.skin.label)
                {
                    font = font,
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                packGuideStyle.normal.textColor = Color.white;
            }

            float guideX = IsPortraitUi ? 150f : 430f;
            float guideY = IsPortraitUi ? 260f + PortraitExtraHeight * 0.5f : 52f;
            Camera camera = Camera.main;
            if (pack != null && pack.gameObject.activeInHierarchy && camera != null && scale > 0f)
            {
                Vector3 packCenter = camera.WorldToScreenPoint(pack.transform.position);
                if (packCenter.z > 0f)
                {
                    float packCenterX = (packCenter.x - offsetX) / scale;
                    guideX = Mathf.Clamp(packCenterX - 210f, 8f, UiReferenceWidth - 428f);
                    float packCenterY = (Screen.height - packCenter.y - offsetY) / scale;
                    if (IsPortraitUi)
                        guideY = Mathf.Clamp(packCenterY - 430f, 120f, UiReferenceHeight - 62f);
                }
            }
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            GUI.Label(new Rect(guideX, guideY, 420f, 42f), Ui("\uD329 \uC704\uCABD\uC744 \uB4DC\uB798\uADF8\uD574\uC11C \uB72F\uAE30", "Drag pack top to open"), packGuideStyle);
            GUI.matrix = previousMatrix;
        }
        private void DrawControlGuide(float scale, float offsetX, float offsetY)
        {
            EnsureDiscardStyles();
            if (controlGuideStyle == null)
            {
                controlGuideStyle = new GUIStyle(GUI.skin.label)
                {
                    font = font,
                    fontSize = 17,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                    normal = { textColor = Color.white }
                };
                controlGuideTitleStyle = new GUIStyle(controlGuideStyle)
                {
                    fontSize = 22,
                    alignment = TextAnchor.MiddleLeft
                };
                controlGuideToggleStyle = new GUIStyle(discardButtonStyle)
                {
                    fontSize = 18,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0)
                };
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            string title = Ui("\uB3C4\uC6C0\uB9D0", "Help");
            GUI.Label(new Rect(24f, 132f, 180f, 38f), title, controlGuideTitleStyle);
            float toggleX = 24f + controlGuideTitleStyle.CalcSize(new GUIContent(title)).x + 8f;
            if (GUI.Button(new Rect(toggleX, 136f, 32f, 30f),
                controlGuideOpen ? "-" : "+", controlGuideToggleStyle))
            {
                controlGuideOpen = !controlGuideOpen;
                SaveUserSettings();
            }

            if (controlGuideOpen)
            {
                string guide = Ui(
                    "\uD329 \uC704\uCABD \uB4DC\uB798\uADF8\uB85C \uD329 \uB72F\uAE30\n"
                    + "\uCE74\uB4DC \uB4DC\uB798\uADF8\uB85C \uB2E4\uC74C \uCE74\uB4DC\n"
                    + "\uBC14\uAE65 \uACF5\uAC04 \uB4DC\uB798\uADF8\uB85C \uD68C\uC804\n"
                    + "\uCE74\uB4DC\uB97C \uB371\uC73C\uB85C \uB4DC\uB798\uADF8\uD558\uC5EC \uBCF4\uAD00/\uAD50\uCCB4\n"
                    + "? \uBC84\uD2BC\uC73C\uB85C \uBD09\uC785 \uCE74\uB4DC \uD655\uC778\n"
                    + "\uB371 \uCE74\uB4DC \uD074\uB9AD\uC73C\uB85C \uC790\uC138\uD788 \uBCF4\uAE30\n"
                    + "\uD640\uB85C\uADF8\uB7A8 \uCE74\uB4DC\uB294 \uC810\uC218\uC640 \uB371 \uD6A8\uACFC\uAC00 2\uBC30",
                    "Drag pack top to open\n"
                    + "Drag card for next card\n"
                    + "Drag outside to rotate\n"
                    + "Drag card to deck to store/swap\n"
                    + "Use ? to check included cards\n"
                    + "Click deck card to inspect\n"
                    + "Holographic cards double score and deck effects");
                DrawStatusLabelWithShadow(new Rect(24f, 180f, 340f, 180f), guide,
                    controlGuideStyle, Color.white);
            }
            GUI.matrix = previousMatrix;
        }
        private void ClearCards()
        {
            foreach (CardVisual card in cards) if (card != null) Destroy(card.gameObject);
            cards.Clear();
            currentPackCards.Clear();
        }
    }
}
