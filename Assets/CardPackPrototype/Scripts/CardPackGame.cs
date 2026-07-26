using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CardOpen.Prototype
{
    public sealed class CardPackGame : MonoBehaviour
    {
        private enum GamePhase
        {
            Ready,
            DraggingPack,
            CardBack,
            CardFront,
            Animating,
            RoundWon,
            RoundLost
        }

        private const int CardsPerPack = 5;
        private const int PacksPerRound = 3;
        private static readonly Vector3 PackHome = new Vector3(0f, 1.48f, -0.62f);
        private static readonly Vector3 CardHome = new Vector3(0f, 1.2f, -0.24f);

        private readonly List<CardData> currentCards = new List<CardData>();
        private readonly List<CardVisual> cardVisuals = new List<CardVisual>();
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        private GamePhase phase;
        private PackVisual pack;
        private Font font;
        private int round;
        private int targetScore;
        private int score;
        private int packsLeft;
        private int currentCardIndex;
        private int flatPointBonus;
        private int luckLevel;
        private int comboLevel;
        private bool pointerDragging;
        private Vector2 pointerStart;
        private Vector2 pointerDelta;
        private string lastResult;

        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle smallStyle;
        private GUIStyle buttonStyle;
        private GUIStyle centeredStyle;
        private Texture2D whiteTexture;

        private static readonly string[,] Names =
        {
            { "EMBER FOX", "CINDER CROW", "MAGMA RAM", "SUN DRAKE" },
            { "BUBBLE FIN", "CORAL OWL", "TIDAL WOLF", "ABYSS WHALE" },
            { "MOSS MOUSE", "THORN DEER", "BLOOM BEAR", "ANCIENT OAK" },
            { "DUST MOTH", "MOON HARE", "COMET LYNX", "VOID SERPENT" }
        };

        private void Awake()
        {
            SetupWorld();
            StartNewRun();
        }

        private void SetupWorld()
        {
            Application.targetFrameRate = 60;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            camera.transform.position = new Vector3(0f, 4.25f, -11.5f);
            camera.transform.LookAt(new Vector3(0f, 1.05f, 0.15f));
            camera.fieldOfView = 46f;
            camera.backgroundColor = new Color(0.025f, 0.035f, 0.07f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = false;

            Light key = FindAnyObjectByType<Light>();
            if (key != null)
            {
                key.type = LightType.Directional;
                key.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
                key.color = new Color(1f, 0.87f, 0.72f);
                key.intensity = 1.15f;
                key.shadows = LightShadows.None;
            }

            GameObject fillObject = new GameObject("Fill Light");
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.transform.rotation = Quaternion.Euler(28f, 145f, 0f);
            fill.color = new Color(0.34f, 0.53f, 1f);
            fill.intensity = 0.75f;
            fill.shadows = LightShadows.None;

            GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Low Poly Table";
            table.transform.position = new Vector3(0f, -0.2f, 1.1f);
            table.transform.localScale = new Vector3(15f, 0.32f, 8f);
            table.GetComponent<Renderer>().sharedMaterial = GetMaterial("Table", new Color(0.045f, 0.105f, 0.13f), 0.05f);
            Destroy(table.GetComponent<Collider>());

            GameObject packObject = new GameObject("3D Card Pack");
            packObject.transform.position = PackHome;
            pack = packObject.AddComponent<PackVisual>();
            pack.Build(GetMaterial("Pack", new Color(0.18f, 0.07f, 0.32f), 0.18f), GetMaterial("Gold", new Color(1f, 0.55f, 0.08f), 0.45f));
        }

        private void StartNewRun()
        {
            round = 1;
            flatPointBonus = 0;
            luckLevel = 0;
            comboLevel = 0;
            StartRound();
        }

        private void StartRound()
        {
            ClearCards();
            targetScore = Mathf.RoundToInt(230f * Mathf.Pow(1.42f, round - 1));
            score = 0;
            packsLeft = PacksPerRound;
            BeginPack();
        }

        private void BeginPack()
        {
            if (packsLeft <= 0) return;
            ClearCards();
            packsLeft--;
            currentCardIndex = 0;
            currentCards.Clear();
            for (int i = 0; i < CardsPerPack; i++) currentCards.Add(RollCard());
            BuildCardStack();

            pack.ResetVisual();
            pack.transform.position = PackHome;
            pack.transform.localScale = Vector3.one * 1.72f;
            pack.transform.rotation = Quaternion.identity;
            pointerDragging = false;
            pointerDelta = Vector2.zero;
            phase = GamePhase.DraggingPack;
            lastResult = "Drag the large pack away to uncover the cards.";
        }

        private void BuildCardStack()
        {
            for (int i = 0; i < currentCards.Count; i++)
            {
                CardData card = currentCards[i];
                GameObject cardObject = new GameObject("Card - " + card.Name);
                CardVisual visual = cardObject.AddComponent<CardVisual>();
                visual.Build(
                    card,
                    GetMaterial("Edge_" + card.Rarity, card.RarityColor, 0.35f),
                    GetMaterial("CardBack", new Color(0.055f, 0.035f, 0.13f), 0.12f),
                    GetMaterial("Face", new Color(0.91f, 0.88f, 0.77f), 0.04f),
                    GetMaterial("Family_" + card.Family, card.FamilyColor, 0.15f),
                    font);

                Vector3 stackPosition = CardHome + new Vector3(0f, i * 0.025f, i * 0.065f);
                visual.PrepareFaceDown(stackPosition, 1.72f - i * 0.025f, (i - 2) * 0.7f);
                cardVisuals.Add(visual);
            }
        }

        private IEnumerator DiscardPack(Vector2 screenDirection)
        {
            phase = GamePhase.Animating;
            pointerDragging = false;
            Vector3 startPosition = pack.transform.position;
            Quaternion startRotation = pack.transform.rotation;
            Vector3 worldDirection = new Vector3(screenDirection.x, -screenDirection.y, 0f).normalized;
            if (worldDirection.sqrMagnitude < 0.1f) worldDirection = Vector3.right;
            Vector3 endPosition = startPosition + worldDirection * 9f + Vector3.up;
            Quaternion endRotation = Quaternion.Euler(0f, 35f, -worldDirection.x * 65f);

            const float duration = 0.38f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / duration);
                pack.transform.position = Vector3.Lerp(startPosition, endPosition, u);
                pack.transform.rotation = Quaternion.Slerp(startRotation, endRotation, u);
                yield return null;
            }
            pack.gameObject.SetActive(false);
            phase = GamePhase.CardBack;
            lastResult = "Tap the top card to reveal it.";
        }

        private IEnumerator RevealCurrentCard()
        {
            phase = GamePhase.Animating;
            pointerDragging = false;
            CardVisual visual = cardVisuals[currentCardIndex];
            yield return visual.RevealInPlace();
            CardData card = currentCards[currentCardIndex];
            phase = GamePhase.CardFront;
            lastResult = "Card " + (currentCardIndex + 1) + "/" + CardsPerPack + "  •  " + card.Rarity + " " + card.Name + "  •  " + card.Points + " points\nSwipe it left or right for the next card.";
        }

        private IEnumerator AdvanceCard(float direction)
        {
            phase = GamePhase.Animating;
            pointerDragging = false;
            CardVisual visual = cardVisuals[currentCardIndex];
            yield return visual.SlideAway(direction);
            Destroy(visual.gameObject);
            currentCardIndex++;

            if (currentCardIndex >= currentCards.Count)
            {
                CompletePack();
                yield break;
            }

            CardVisual next = cardVisuals[currentCardIndex];
            next.PrepareFaceDown(CardHome, 1.72f, 0f);
            phase = GamePhase.CardBack;
            lastResult = "Tap card " + (currentCardIndex + 1) + "/" + CardsPerPack + " to reveal it.";
        }

        private void CompletePack()
        {
            int gained = CalculatePackScore(currentCards, out string breakdown);
            score += gained;
            lastResult = "Pack total: +" + gained + "  •  " + breakdown;

            if (score >= targetScore)
            {
                phase = GamePhase.RoundWon;
                lastResult += "\nTARGET CLEARED! Choose a round reward.";
            }
            else if (packsLeft <= 0)
            {
                phase = GamePhase.RoundLost;
                lastResult += "\nRUN OVER - target missed by " + (targetScore - score) + ".";
            }
            else
            {
                phase = GamePhase.Ready;
                pack.ResetVisual();
                pack.transform.position = new Vector3(0f, 1.45f, 0.1f);
                pack.transform.localScale = Vector3.one * 1.05f;
            }
        }

        private void HandlePointer(Vector2 point, Event currentEvent)
        {
            Rect interactionArea = new Rect(360f, 118f, 560f, 455f);
            if (currentEvent.type == EventType.MouseDown && interactionArea.Contains(point) && IsInteractivePhase())
            {
                pointerDragging = true;
                pointerStart = point;
                pointerDelta = Vector2.zero;
                currentEvent.Use();
                return;
            }

            if (!pointerDragging) return;
            if (currentEvent.type == EventType.MouseDrag)
            {
                pointerDelta = point - pointerStart;
                if (phase == GamePhase.DraggingPack)
                {
                    pack.transform.position = PackHome + new Vector3(pointerDelta.x * 0.008f, pointerDelta.y * -0.008f, 0f);
                    pack.transform.rotation = Quaternion.Euler(pointerDelta.y * 0.025f, pointerDelta.x * 0.035f, pointerDelta.x * -0.055f);
                }
                else if (phase == GamePhase.CardFront)
                {
                    CardVisual visual = cardVisuals[currentCardIndex];
                    visual.transform.position = CardHome + new Vector3(pointerDelta.x * 0.008f, pointerDelta.y * -0.004f, -Mathf.Abs(pointerDelta.x) * 0.0005f);
                    visual.transform.rotation = Quaternion.Euler(-4f, 0f, pointerDelta.x * -0.045f);
                }
                currentEvent.Use();
                return;
            }

            if (currentEvent.type != EventType.MouseUp) return;
            pointerDragging = false;
            if (phase == GamePhase.DraggingPack)
            {
                if (pointerDelta.magnitude >= 145f) StartCoroutine(DiscardPack(pointerDelta));
                else
                {
                    pack.transform.position = PackHome;
                    pack.transform.rotation = Quaternion.identity;
                }
            }
            else if (phase == GamePhase.CardBack)
            {
                if (pointerDelta.magnitude < 80f) StartCoroutine(RevealCurrentCard());
            }
            else if (phase == GamePhase.CardFront)
            {
                if (Mathf.Abs(pointerDelta.x) >= 115f) StartCoroutine(AdvanceCard(Mathf.Sign(pointerDelta.x)));
                else
                {
                    CardVisual visual = cardVisuals[currentCardIndex];
                    visual.transform.position = CardHome;
                    visual.transform.rotation = Quaternion.Euler(-4f, 0f, 0f);
                }
            }
            currentEvent.Use();
        }

        private bool IsInteractivePhase()
        {
            return phase == GamePhase.DraggingPack || phase == GamePhase.CardBack || phase == GamePhase.CardFront;
        }

        private CardData RollCard()
        {
            float roll = Random.value;
            float luck = luckLevel * 0.035f;
            CardRarity rarity;
            if (roll > 0.975f - luck * 0.45f) rarity = CardRarity.Legendary;
            else if (roll > 0.865f - luck) rarity = CardRarity.Epic;
            else if (roll > 0.60f - luck) rarity = CardRarity.Rare;
            else rarity = CardRarity.Common;

            CardFamily family = (CardFamily)Random.Range(0, 4);
            int tier = (int)rarity;
            int[] bases = { 13, 25, 44, 78 };
            int[] spreads = { 8, 11, 15, 24 };
            return new CardData
            {
                Name = Names[(int)family, tier],
                Rarity = rarity,
                Family = family,
                Points = bases[tier] + Random.Range(0, spreads[tier]) + flatPointBonus
            };
        }

        private int CalculatePackScore(List<CardData> cards, out string breakdown)
        {
            int basePoints = cards.Sum(card => card.Points);
            int familyBonus = 0;
            CardFamily? comboFamily = null;
            foreach (IGrouping<CardFamily, CardData> group in cards.GroupBy(card => card.Family))
            {
                if (group.Count() < 3) continue;
                familyBonus = Mathf.RoundToInt(basePoints * (0.5f + comboLevel * 0.15f));
                comboFamily = group.Key;
                break;
            }

            int duplicatePairs = cards.GroupBy(card => card.Name).Sum(group => group.Count() * (group.Count() - 1) / 2);
            int duplicateBonus = duplicatePairs * (30 + comboLevel * 10);
            breakdown = "Base " + basePoints;
            if (comboFamily.HasValue) breakdown += " + " + comboFamily.Value + " set " + familyBonus;
            if (duplicateBonus > 0) breakdown += " + duplicates " + duplicateBonus;
            if (!comboFamily.HasValue && duplicateBonus == 0) breakdown += " (no combo)";
            return basePoints + familyBonus + duplicateBonus;
        }

        private void ChooseReward(int reward)
        {
            if (phase != GamePhase.RoundWon) return;
            if (reward == 0) flatPointBonus += 3;
            else if (reward == 1) luckLevel++;
            else comboLevel++;
            round++;
            StartRound();
        }

        private void ClearCards()
        {
            foreach (CardVisual visual in cardVisuals)
                if (visual != null) Destroy(visual.gameObject);
            cardVisuals.Clear();
            currentCards.Clear();
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

        private void EnsureGuiStyles()
        {
            if (titleStyle != null) return;
            whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 31, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = new Color(0.86f, 0.90f, 0.96f) }, wordWrap = true };
            smallStyle = new GUIStyle(bodyStyle) { fontSize = 15 };
            centeredStyle = new GUIStyle(bodyStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.active.textColor = Color.white;
        }

        private void DrawRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, whiteTexture);
            GUI.color = old;
        }

        private void OnGUI()
        {
            EnsureGuiStyles();
            const float referenceWidth = 1280f;
            const float referenceHeight = 720f;
            float scale = Mathf.Min(Screen.width / referenceWidth, Screen.height / referenceHeight);
            float offsetX = (Screen.width - referenceWidth * scale) * 0.5f;
            float offsetY = (Screen.height - referenceHeight * scale) * 0.5f;
            Vector2 rawPoint = Event.current.mousePosition;
            Vector2 referencePoint = new Vector2((rawPoint.x - offsetX) / scale, (rawPoint.y - offsetY) / scale);
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity, Vector3.one * scale);

            DrawRect(new Rect(28, 24, 1224, 82), new Color(0.025f, 0.035f, 0.075f, 0.93f));
            GUI.Label(new Rect(52, 34, 470, 44), "PACK ASCENT", titleStyle);
            GUI.Label(new Rect(720, 32, 170, 36), "ROUND " + round, centeredStyle);
            GUI.Label(new Rect(875, 32, 175, 36), "LEFT " + packsLeft, centeredStyle);
            GUI.Label(new Rect(1040, 32, 190, 36), score + " / " + targetScore, centeredStyle);

            Rect progressBack = new Rect(720, 77, 510, 12);
            DrawRect(progressBack, new Color(0.12f, 0.14f, 0.22f));
            float progress = Mathf.Clamp01((float)score / targetScore);
            DrawRect(new Rect(progressBack.x, progressBack.y, progressBack.width * progress, progressBack.height), progress >= 1f ? new Color(0.36f, 0.92f, 0.50f) : new Color(1f, 0.54f, 0.13f));

            DrawRect(new Rect(28, 582, 1224, 111), new Color(0.025f, 0.035f, 0.075f, 0.93f));
            GUI.Label(new Rect(48, 595, 760, 80), lastResult, bodyStyle);

            if (phase == GamePhase.Ready)
            {
                if (GUI.Button(new Rect(930, 608, 285, 58), "NEXT PACK", buttonStyle)) BeginPack();
            }
            else if (phase == GamePhase.RoundLost)
            {
                if (GUI.Button(new Rect(930, 608, 285, 58), "START NEW RUN", buttonStyle)) StartNewRun();
            }
            else if (phase == GamePhase.RoundWon)
            {
                GUI.Label(new Rect(790, 582, 440, 24), "CHOOSE ONE", centeredStyle);
                if (GUI.Button(new Rect(790, 615, 140, 55), "+3 EACH", buttonStyle)) ChooseReward(0);
                if (GUI.Button(new Rect(940, 615, 140, 55), "MORE LUCK", buttonStyle)) ChooseReward(1);
                if (GUI.Button(new Rect(1090, 615, 140, 55), "COMBO +", buttonStyle)) ChooseReward(2);
            }

            string gesture = phase == GamePhase.DraggingPack ? "DRAG PACK AWAY" : phase == GamePhase.CardBack ? "TAP TO FLIP" : phase == GamePhase.CardFront ? "SWIPE LEFT OR RIGHT" : string.Empty;
            if (!string.IsNullOrEmpty(gesture)) GUI.Label(new Rect(430, 525, 420, 42), gesture, centeredStyle);
            GUI.Label(new Rect(44, 116, 320, 50), "3 matching families: +50%\nDuplicate pair: +30 points", smallStyle);

            HandlePointer(referencePoint, Event.current);
        }
    }
}
