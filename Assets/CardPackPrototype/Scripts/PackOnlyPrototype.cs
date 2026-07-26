using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
            public bool AddedToPendingScore;
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
            public readonly Dictionary<int, float> AccumulatedPercentByAbility =
                new Dictionary<int, float>();
        }
        private enum RevealPhase { Pack, CardBack, CardFront, Animating }
        private const int FallbackCardsPerPack = 5;
        private const float RevealedCardScale = 1.5f;
        private static readonly Rect PackTearZone = new Rect(410f, 0f, 460f, 320f);
        private static readonly Rect CardGestureZone = new Rect(500f, 105f, 340f, 505f);
        private static readonly Vector3 PackHome = new Vector3(0f, 0.5f, -0.65f);
        private static readonly Vector3 CardHome = new Vector3(0f, 1.15f, -0.24f);
        private static readonly Vector3 PackedCardOffset = new Vector3(0f, -0.55f, 0f);
        private readonly List<CardVisual> cards = new List<CardVisual>();
        private readonly List<StoredCard> currentPackCards = new List<StoredCard>();
        private readonly List<StoredCard> deckCards = new List<StoredCard>();
        private StoredCard previousRevealedCard;
        private readonly List<GameObject> deckVisuals = new List<GameObject>();
        private readonly List<ScorePopup> scorePopups = new List<ScorePopup>();
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private PackVisual pack;
        private PackTearVisual tearVisual;
        private Transform cardStack;
        private Transform deckRoot;
        private readonly List<GameObject> emptyDeckPlaceholders = new List<GameObject>();
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
        private global::CardData[] fallbackCards;
        private global::CardData runtimeFallbackCard;
        private global::CardPackEntry runtimeFallbackEntry;
        private Font font;
        private RevealPhase phase;
        private int cardIndex;
        private bool currentPackIsHolographic;
        private bool gestureDragging;
        private bool inspectionDragging;
        private Vector2 dragStart;
        private Vector2 dragDelta;
        private Vector3 gestureStartPosition;
        private Quaternion gestureStartRotation;
        private Transform inspectionTarget;
        private Quaternion inspectionStartRotation;
        private CardVisual activeSlidingCard;
        private bool cardTransitionActive;
        private bool transitionDragActive;
        private bool transitionSwipeCommitted;
        private int queuedCardSwipes;
        private float queuedSwipeDirection;
        private int totalScore;
        private int pendingScore;
        private float pendingScoreCommitTime = -1f;
        private int scoreTransferAmount;
        private int scoreTransferApplied;
        private float scoreTransferStartTime = -1f;
        private GUIStyle scoreStyle;
        private GUIStyle scorePopupStyle;
        private GUIStyle packGuideStyle;
        private GUIStyle deckHeaderStyle;
        private GUIStyle discardButtonStyle;
        private GUIStyle discardPanelStyle;
        private GUIStyle discardMessageStyle;
        private GUIStyle deckRarityStyle;
        private Texture2D roundedDiscardTexture;
        private bool discardConfirmationVisible;

        private void Awake() { SetupScene(); BeginSequence(); }

        private void LateUpdate()
        {
            UpdatePendingScore();
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
            packObject.transform.position = PackHome;
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

        private void BeginSequence()
        {
            ResetPerPackAccumulatedBonuses();
            ClearCards();
            cardStack.position = Vector3.zero;
            cardStack.rotation = Quaternion.identity;
            cardStack.localScale = Vector3.one;
            currentPackIsHolographic = Random.value < 0.01f;
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
            pack.transform.position = PackHome;
            pack.transform.localScale = Vector3.one * 1.50f;
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
                global::CardPackEntry entry = DrawCard();
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
                    rarityPatternMaterial, illustrationMaterial, costMaterial, font);
                bool isHolographic = currentPackIsHolographic || Random.value < 0.1f;
                if (isHolographic) visual.EnableHologram();
                visual.PrepareFaceUp(CardHome + new Vector3(0f, i * 0.025f, i * 0.065f), RevealedCardScale,
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
                runtimeFallbackCard.Description = "7의 피해를 줍니다.";
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
        private global::CardPackData LoadCardPackData()
        {
            global::CardPackData farAndWide = Resources.Load<global::CardPackData>("CardPacks/FarAndWide");
            if (farAndWide != null) return farAndWide;

            global::CardPackData[] packs = Resources.LoadAll<global::CardPackData>(string.Empty);
            return packs.Length > 0 ? packs[0] : null;
        }

        public void SetCardPackData(global::CardPackData data)
        {
            activePackData = data;
            if (materials.TryGetValue("PackFrontArtwork", out Material front))
                ApplyTextureOrFallback(front, data != null ? data.FrontImage : null,
                    Resources.Load<Texture2D>("Textures/CardPackFrontStoryTailBlueSky"));
            if (materials.TryGetValue("PackBackArtwork", out Material back))
                ApplyTextureOrFallback(back, data != null ? data.BackImage : null,
                    Resources.Load<Texture2D>("Textures/CardPackBackStoryTail"));
            if (pack != null) BeginSequence();
        }
        private IEnumerator RemovePack(Vector2 direction)
        {
            phase = RevealPhase.Animating;
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].gameObject.SetActive(true);
                cards[i].SetFaceDetailsVisible(true);
            }
            cards[0].PrepareFaceUp(CardHome, RevealedCardScale, 0f);
            yield return tearVisual.PeelInDirection(direction, cardStack, CardHome, PackedCardOffset);
            pack.gameObject.SetActive(false);
            yield return ReturnCardStackToFront();
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
                CardVisual current = cards[cardIndex];
                if (cardIndex + 1 < cards.Count)
                {
                    CardVisual next = cards[cardIndex + 1];
                    next.gameObject.SetActive(true);
                    next.PrepareFaceUp(CardHome + new Vector3(0f, 0.035f, 0.035f), RevealedCardScale, 0f);
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
                    BeginSequence();
                    yield break;
                }
                yield return cards[cardIndex].MoveToFront(CardHome, RevealedCardScale, 0f);
                yield return RestoreCardStackRotation();
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
        private void OnGUI()
        {
            const float width = 1280f;
            const float height = 720f;
            float scale = Mathf.Min(Screen.width / width, Screen.height / height);
            float offsetX = (Screen.width - width * scale) * 0.5f;
            float offsetY = (Screen.height - height * scale) * 0.5f;
            if (inspectedDeckIndex < 0)
            {
                DrawScore(scale, offsetX, offsetY);
                DrawDeck(scale, offsetX, offsetY);
                DrawScorePopups(scale, offsetX, offsetY);
                DrawPackTearGuide(scale, offsetX, offsetY);
            }
            else
            {
                DrawDeckInspectionControls(scale, offsetX, offsetY);
            }

            Vector2 raw = Event.current.mousePosition;
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
            if (inputEvent.type == EventType.MouseDown && new Rect(0f, 0f, 1280f, 720f).Contains(point))
            {
                dragStart = point;
                dragDelta = Vector2.zero;
                bool objectGesture = phase == RevealPhase.Pack ? PackTearZone.Contains(point) : CardGestureZone.Contains(point);
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
            if (inspectionDragging) { inspectionDragging = false; inspectionTarget = null; }
            else if (gestureDragging) { gestureDragging = false; CompleteObjectGesture(); }
            else return;
            inputEvent.Use();
        }

        private void HandleAnimatingCardSwipe(Vector2 point, Event inputEvent)
        {
            if (!cardTransitionActive) return;
            if (inputEvent.type == EventType.MouseDown && CardGestureZone.Contains(point))
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
            inspectionDragging = true;
            gestureDragging = false;
            inspectionStartRotation = inspectionTarget.rotation;
        }

        private void UpdateInspectionRotation()
        {
            if (inspectionTarget != null)
                inspectionTarget.rotation = Quaternion.Euler(-dragDelta.y * 0.24f, dragDelta.x * 0.28f, 0f) * inspectionStartRotation;
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

            const float distance = 24f;
            float height = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            GameObject background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "2D Background";
            background.transform.position = camera.transform.position + camera.transform.forward * distance;
            background.transform.rotation = Quaternion.LookRotation(-camera.transform.forward, camera.transform.up);
            background.transform.localScale = new Vector3(height * camera.aspect * 1.05f, height * 1.05f, 1f);
            Collider backgroundCollider = background.GetComponent<Collider>();
            if (backgroundCollider != null) Destroy(backgroundCollider);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
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
                    earnedScore = 200; reason = "고급 카드"; popupColor = new Color(0.45f, 1f, 0.72f); break;
                case global::CardRarity.Rare:
                    earnedScore = 300; reason = "희귀 카드"; popupColor = new Color(0.72f, 0.88f, 1f); break;
                case global::CardRarity.Epic:
                    earnedScore = 500; reason = "영웅 카드"; popupColor = new Color(1f, 0.73f, 0.22f); break;
                default:
                    earnedScore = 100; reason = "일반 카드"; popupColor = Color.white; break;
            }

            int baseCardScoreTotal = earnedScore * (currentCard.IsHolographic ? 2 : 1);
            AddScorePopup("+" + earnedScore + "점\n" + reason, popupColor,
                Time.unscaledTime, scorePopups.Count, earnedScore);
            if (currentCard.IsHolographic)
            {
                AddScorePopup("+" + earnedScore + "점\n홀로그램!", new Color(0.55f, 0.9f, 1f),
                    Time.unscaledTime + 0.22f, scorePopups.Count, earnedScore);
            }
            TriggerDeckAbilities(currentCard, baseCardScoreTotal);
            previousRevealedCard = currentCard;
        }

        private void TriggerDeckAbilities(StoredCard revealedCard, int baseCardScoreTotal)
        {
            int triggerRequirementCount = CountTriggeredDeckEffects(revealedCard);
            AccumulateDeckScoreBonuses(revealedCard, triggerRequirementCount);
            int triggeredCount = 0;
            int flatAbilityScoreTotal = 0;

            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard abilityOwner = deckCards[i];
                if (abilityOwner == null || abilityOwner.Data == null || abilityOwner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(abilityOwner);
                for (int j = 0; j < abilityOwner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = abilityOwner.Data.DeckAbilities[j];
                    if (ability == null || !IsFlatScoreEffect(ability.Effect)
                        || !DoesDeckAbilityTrigger(ability, abilityOwner, revealedCard, triggerRequirementCount)) continue;
                    int flatScore = GetFlatDeckAbilityScore(ability, revealedCard);
                    if (flatScore <= 0) continue;
                    for (int copy = 0; copy < effectiveCopies; copy++)
                    {
                        flatAbilityScoreTotal += flatScore;
                        AddDeckAbilityPopup(abilityOwner, ability, flatScore, copy, triggeredCount++);
                    }
                }
            }

            int scoreBeforePercentageBonus = baseCardScoreTotal + flatAbilityScoreTotal;
            float scoreBonusEfficiency = GetScoreBonusEfficiencyMultiplier();
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard abilityOwner = deckCards[i];
                if (abilityOwner == null || abilityOwner.Data == null || abilityOwner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(abilityOwner);
                for (int j = 0; j < abilityOwner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = abilityOwner.Data.DeckAbilities[j];
                    if (ability == null || !DoesDeckAbilityTrigger(
                            ability, abilityOwner, revealedCard, triggerRequirementCount)) continue;

                    if (ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusPerDraw)
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
                    {
                        AddDeckAbilityPopup(abilityOwner, ability, bonusScore, copy, triggeredCount++);
                    }
                }
            }
        }

        private int CountTriggeredDeckEffects(StoredCard revealedCard)
        {
            int count = 0;
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard owner = deckCards[i];
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null || ability.Trigger == global::DeckAbilityTrigger.TriggeredEffectsAtLeastThree
                        || ability.Effect == global::DeckAbilityEffect.AddNextPackCards) continue;
                    bool hasScoreValue = IsFlatScoreEffect(ability.Effect)
                        ? GetFlatDeckAbilityScore(ability, revealedCard) > 0
                        : (ability.Effect == global::DeckAbilityEffect.AddTriggeredScorePercent
                            || ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusPerDraw)
                            && ability.PercentBonus > 0f;
                    if (hasScoreValue && DoesDeckAbilityTrigger(ability, owner, revealedCard))
                        count += effectiveCopies;
                }
            }
            return count;
        }
        private void AccumulateDeckScoreBonuses(StoredCard revealedCard, int triggerRequirementCount)
        {
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard owner = deckCards[i];
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null || ability.Effect != global::DeckAbilityEffect.AccumulateScoreBonusPerDraw
                        || ability.PercentBonus <= 0f
                        || !DoesDeckAbilityTrigger(ability, owner, revealedCard, triggerRequirementCount)) continue;
                    owner.AccumulatedPercentByAbility.TryGetValue(j, out float accumulatedPercent);
                    owner.AccumulatedPercentByAbility[j] =
                        accumulatedPercent + ability.PercentBonus * effectiveCopies;
                }
            }
        }

        private static bool IsFlatScoreEffect(global::DeckAbilityEffect effect)
        {
            return effect == global::DeckAbilityEffect.AddScore
                || effect == global::DeckAbilityEffect.AddRevealedNumberTimesScore;
        }

        private static int GetFlatDeckAbilityScore(global::CardDeckAbility ability, StoredCard revealedCard)
        {
            if (ability.Effect == global::DeckAbilityEffect.AddRevealedNumberTimesScore)
                return Mathf.Max(0, revealedCard.Number * ability.NumberMultiplier);
            return Mathf.Max(0, ability.Score);
        }

        private void ResetPerPackAccumulatedBonuses()
        {
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard owner = deckCards[i];
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability != null && ability.Effect == global::DeckAbilityEffect.AccumulateScoreBonusPerDraw
                        && ability.ResetAccumulationAfterPack)
                        owner.AccumulatedPercentByAbility.Remove(j);
                }
            }
        }

        private int GetAdditionalNextPackCardCount()
        {
            int additionalCards = 0;
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard owner = deckCards[i];
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
        private void ApplyDeckCardTransformEffects(StoredCard revealedCard)
        {
            if (revealedCard == null || revealedCard.IsHolographic) return;
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard owner = deckCards[i];
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null || ability.Effect != global::DeckAbilityEffect.GrantHologramChance
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
            for (int i = 0; i < deckCards.Count; i++)
            {
                StoredCard owner = deckCards[i];
                if (owner == null || owner.Data == null || owner.Data.DeckAbilities == null) continue;
                int effectiveCopies = GetEffectiveDeckCopyCount(owner);
                for (int j = 0; j < owner.Data.DeckAbilities.Count; j++)
                {
                    global::CardDeckAbility ability = owner.Data.DeckAbilities[j];
                    if (ability == null || ability.Effect != global::DeckAbilityEffect.IncreaseScoreBonusEfficiency
                        || ability.PercentBonus <= 0f) continue;
                    addedEfficiency += ability.PercentBonus * 0.01f * effectiveCopies;
                }
            }
            return 1f + addedEfficiency;
        }

        private void AddDeckAbilityPopup(StoredCard owner, global::CardDeckAbility ability, int score,            int copyIndex, int triggeredIndex)
        {
            string ownerReason = owner.Name;
            if (copyIndex > 0) ownerReason += " 홀로그램";
            AddScorePopup(ownerReason + "  +" + score + "점",
                copyIndex > 0 ? new Color(0.55f, 0.9f, 1f) : new Color(0.66f, 1f, 0.48f),
                Time.unscaledTime + triggeredIndex * 0.16f, 1 + triggeredIndex % 4, score);
        }

        private void AddScorePopup(string text, Color color, float startTime, int lane, int score)
        {
            scorePopups.Add(new ScorePopup
            {
                Text = text,
                Color = color,
                StartTime = startTime,
                Lane = lane,
                Score = Mathf.Max(0, score)
            });
            pendingScoreCommitTime = Mathf.Max(pendingScoreCommitTime, startTime + 0.2f);
        }

        private void UpdatePendingScore()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < scorePopups.Count; i++)
            {
                ScorePopup popup = scorePopups[i];
                if (popup.AddedToPendingScore || now < popup.StartTime) continue;
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
            return card != null && card.IsHolographic ? 2 : 1;
        }
        private bool DoesDeckAbilityTrigger(global::CardDeckAbility ability, StoredCard owner, StoredCard revealedCard, int triggeredEffectCount = 0)
        {
            switch (ability.Trigger)
            {
                case global::DeckAbilityTrigger.MatchingColor:
                    return owner.Color == revealedCard.Color;
                case global::DeckAbilityTrigger.OddNumber:
                case global::DeckAbilityTrigger.EvenNumber:
                case global::DeckAbilityTrigger.NumberAtLeastFour:
                case global::DeckAbilityTrigger.NumberAtMostThree:
                case global::DeckAbilityTrigger.NumberAtMostTwo:
                case global::DeckAbilityTrigger.IncludedNumbers:
                    return ability.ApplicableNumbers != null
                        && ability.ApplicableNumbers.Contains(revealedCard.Number);
                case global::DeckAbilityTrigger.DifferentColor:
                    return owner.Color != revealedCard.Color;
                case global::DeckAbilityTrigger.MatchingNumber:
                    return owner.Number == revealedCard.Number;
                case global::DeckAbilityTrigger.EveryCard:
                    return true;
                case global::DeckAbilityTrigger.MatchingColorOrRed:
                    return owner.Color == revealedCard.Color || revealedCard.Color == global::CardColor.Red;
                case global::DeckAbilityTrigger.PreviousCardDifferentColor:
                    return previousRevealedCard != null && previousRevealedCard.Color != revealedCard.Color;

                case global::DeckAbilityTrigger.TriggeredEffectsAtLeastThree:
                    return triggeredEffectCount >= 3;
                case global::DeckAbilityTrigger.RedCard:
                    return revealedCard.Color == global::CardColor.Red;
                case global::DeckAbilityTrigger.IncludedColors:
                    return ability.ApplicableColors != null
                        && ability.ApplicableColors.Contains(revealedCard.Color);

                default:
                    return false;
            }
        }

        private bool StoreCurrentCardInDeck(StoredCard card, int preferredSlot = -1)
        {
            if (card == null || card.IsStoredInDeck || deckCards.Count >= 5) return false;
            int slot = preferredSlot >= 0 && preferredSlot < 5 && GetDeckIndexAtSlot(preferredSlot) < 0
                ? preferredSlot
                : GetFirstEmptyDeckSlot();
            if (slot < 0) return false;

            card.IsStoredInDeck = true;
            card.DeckSlot = slot;
            deckCards.Add(card);
            CreateStoredCardVisual();
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
                float viewportX = 0.042f + i * 0.058f;
                placeholder.transform.position =
                    camera.ViewportToWorldPoint(new Vector3(viewportX, 0.135f, depth));
                placeholder.transform.localScale = Vector3.one * 0.43f;
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
                    visual.transform.position = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.51f, depth));
                    visual.transform.localScale = Vector3.one * 1.72f;
                }
                else
                {
                    int slot = i < deckCards.Count && deckCards[i] != null ? deckCards[i].DeckSlot : i;
                    float viewportX = 0.042f + Mathf.Clamp(slot, 0, 4) * 0.058f;
                    visual.transform.position = camera.ViewportToWorldPoint(new Vector3(viewportX, 0.135f, depth));
                    visual.transform.localScale = Vector3.one * 0.43f;
                }
                if (!selected || (!deckInspectionDragging && !deckInspectionReturning))
                    visual.transform.rotation = camera.transform.rotation;
            }

            LayoutDeckInspectionBackdrop(camera, depth);
        }

        private void CreateDeckInspectionBackdrop()
        {
            deckInspectionBackdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            deckInspectionBackdrop.name = "Deck Inspection Backdrop";
            Collider backdropCollider = deckInspectionBackdrop.GetComponent<Collider>();
            if (backdropCollider != null) Destroy(backdropCollider);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
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

        private bool HandleDeckPointer(Vector2 screenPoint, Event inputEvent)
        {
            bool isInspecting = inspectedDeckIndex >= 0;
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
                if (deckCardDragActive && pressedDeckIndex < deckVisuals.Count)
                {
                    GameObject dragged = deckVisuals[pressedDeckIndex];
                    float depth = camera.WorldToScreenPoint(CardHome).z - 0.45f;
                    dragged.transform.position = camera.ScreenToWorldPoint(
                        new Vector3(screenPoint.x, Screen.height - screenPoint.y, depth));
                    dragged.transform.rotation = camera.transform.rotation;
                    dragged.transform.localScale = Vector3.one * 0.52f;
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
                else if (IsPointOverCurrentCard(screenPoint, camera))
                {
                    SwapDeckCardWithCurrent(sourceIndex);
                }
                else
                {
                    int targetSlot = GetDeckSlotAtPoint(screenPoint);
                    if (targetSlot >= 0)
                        MoveDeckCardToSlot(sourceIndex, targetSlot);
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
            float normalizedX = screenPoint.x / Screen.width;
            float normalizedY = screenPoint.y / Screen.height;
            return normalizedX >= 0f && normalizedX <= 0.36f && normalizedY >= 0.72f && normalizedY <= 1f;
        }

        private static int GetDeckSlotAtPoint(Vector2 screenPoint)
        {
            if (!IsPointInDeckRow(screenPoint) || Screen.width <= 0) return -1;
            float viewportX = screenPoint.x / Screen.width;
            int slot = Mathf.RoundToInt((viewportX - 0.042f) / 0.058f);
            return Mathf.Clamp(slot, 0, 4);
        }

        private void TryDropCurrentCardIntoDeck(Vector2 screenPoint)
        {
            if (cardIndex < 0 || cardIndex >= cards.Count || cardIndex >= currentPackCards.Count) return;
            int slot = GetDeckSlotAtPoint(screenPoint);
            if (slot < 0) return;

            int occupiedDeckIndex = GetDeckIndexAtSlot(slot);
            if (occupiedDeckIndex >= 0)
            {
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
            phase = RevealPhase.Animating;
            cardTransitionActive = true;
            CardVisual current = cards[cardIndex];

            if (cardIndex + 1 < cards.Count)
            {
                CardVisual next = cards[cardIndex + 1];
                next.gameObject.SetActive(true);
                next.PrepareFaceUp(CardHome + new Vector3(0f, 0.035f, 0.035f), RevealedCardScale, 0f);
                next.SetFaceDetailsVisible(true);
            }

            if (current != null) current.gameObject.SetActive(false);
            cardIndex++;
            if (cardIndex >= cards.Count)
            {
                cardTransitionActive = false;
                yield return new WaitForSeconds(0.35f);
                BeginSequence();
                yield break;
            }

            yield return cards[cardIndex].MoveToFront(CardHome, RevealedCardScale, 0f);
            yield return RestoreCardStackRotation();
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
            incomingVisual.PrepareFaceUp(CardHome, RevealedCardScale, 0f);
            incomingVisual.SetFaceDetailsVisible(true);
            cards[cardIndex] = incomingVisual;
            LayoutDeckVisuals();
            return true;
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
            if (deckInspectionBackdrop != null) deckInspectionBackdrop.SetActive(false);
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
            for (int i = scorePopups.Count - 1; i >= 0; i--)
            {
                ScorePopup popup = scorePopups[i];
                float age = Time.unscaledTime - popup.StartTime;
                if (age < 0f) continue;
                if (age >= 1.35f)
                {
                    scorePopups.RemoveAt(i);
                    continue;
                }

                float enter = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.18f));
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 1.35f, age));
                float x = Mathf.Lerp(783f, 837f, enter);
                float y = 270f + popup.Lane * 72f - Mathf.Clamp01(age / 1.35f) * 24f;
                GUI.color = new Color(popup.Color.r, popup.Color.g, popup.Color.b, fade);
                GUI.Label(new Rect(x, y, 210f, 76f), popup.Text, scorePopupStyle);
            }
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
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

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            string scoreText = "점수  " + totalScore.ToString("N0");
            if (pendingScore > 0) scoreText += "  + " + pendingScore.ToString("N0");
            GUI.Label(new Rect(24f, 18f, 440f, 48f), scoreText, scoreStyle);
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
                GUI.Label(new Rect(490f, 18f, 300f, 48f),
                    GetRarityDisplayName(inspectedCard.Rarity), deckRarityStyle);
                GUI.color = previousColor;
            }

            if (!discardConfirmationVisible)
            {
                if (GUI.Button(new Rect(550f, 646f, 180f, 52f), "카드 버리기", discardButtonStyle))
                    discardConfirmationVisible = true;
            }
            else
            {
                Rect panelRect = new Rect(430f, 252f, 420f, 206f);
                GUI.Box(panelRect, GUIContent.none, discardPanelStyle);
                GUI.Label(new Rect(455f, 278f, 370f, 64f), "이 카드를 버릴까요?", discardMessageStyle);
                if (GUI.Button(new Rect(480f, 370f, 140f, 52f), "버리기", discardButtonStyle))
                    DiscardInspectedDeckCard();
                if (GUI.Button(new Rect(660f, 370f, 140f, 52f), "취소", discardButtonStyle))
                    discardConfirmationVisible = false;
            }

            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private static string GetRarityDisplayName(global::CardRarity rarity)
        {
            switch (rarity)
            {
                case global::CardRarity.Uncommon: return "고급";
                case global::CardRarity.Rare: return "희귀";
                case global::CardRarity.Epic: return "영웅";
                default: return "일반";
            }
        }

        private static Color GetRarityDisplayColor(global::CardRarity rarity)
        {
            switch (rarity)
            {
                case global::CardRarity.Uncommon: return new Color(0.45f, 1f, 0.72f);
                case global::CardRarity.Rare: return new Color(0.72f, 0.88f, 1f);
                case global::CardRarity.Epic: return new Color(0.72f, 0.30f, 1f);
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
            }
            deckCards.RemoveAt(index);
            deckVisuals.RemoveAt(index);
            if (discardedVisual != null) Destroy(discardedVisual);
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
            GUI.Label(new Rect(24f, 516f, 260f, 34f), "덱  " + deckCards.Count + "/5", deckHeaderStyle);


            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }
        private void DrawPackTearGuide(float scale, float offsetX, float offsetY)
        {
            if (phase != RevealPhase.Pack) return;
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

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            GUI.Label(new Rect(390f, 52f, 500f, 42f), "팩 위쪽을 드래그해서 뜯기", packGuideStyle);
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
