using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace CardOpen.Prototype
{
    public sealed class CardVisual : MonoBehaviour
    {
        private const int CardNameCharactersPerLine = 9;
        private const int LatinCardNameCharactersPerLine = 20;
        private const float CardNameMaximumWidth = 1.32f;
        private const float CardNameMaximumHeight = 0.38f;
        private const float LatinCardNameHorizontalScale = 0.9f;
        private MeshRenderer cardRenderer;
        private Material backMaterial;
        private Material frontMaterial;
        private bool accelerateSlide;
        private readonly List<MeshRenderer> faceLayerRenderers = new List<MeshRenderer>();
        private static readonly Dictionary<Font, Material> WorldTextMaterials = new Dictionary<Font, Material>();
        private static readonly Dictionary<Font, TMP_FontAsset> TmpFontAssets = new Dictionary<Font, TMP_FontAsset>();
        private static Material hologramMaterial;
        private static readonly Dictionary<global::CardRarity, Material> RarityFinishMaterials =
            new Dictionary<global::CardRarity, Material>();
        private static readonly Dictionary<Material, Material> UncommonGlossyMaterials =
            new Dictionary<Material, Material>();
        public bool IsHolographic { get; private set; }
        public bool IsFaceUp { get; private set; }

        static CardVisual()
        {
            Font.textureRebuilt += HandleFontTextureRebuilt;
        }

        public void Build(CardData data, Material rarityMaterial, Material cardBackMaterial, Material unusedFaceMaterial, Material unusedFamilyMaterial, Font unusedFont)
        {
            GameObject cardMesh = new GameObject("Single Rounded Card Mesh");
            cardMesh.transform.SetParent(transform, false);
            cardMesh.AddComponent<MeshFilter>().sharedMesh = BuildRoundedCardMesh();
            cardRenderer = cardMesh.AddComponent<MeshRenderer>();
            backMaterial = cardBackMaterial;
            frontMaterial = rarityMaterial;
            SetFaceUp(true);
        }

        public void BuildLayered(CardData data, Material attributeMaterial, Material cardBackMaterial,
            Material rarityPatternMaterial, Material contentMaterial, Material costMaterial)
        {
            GameObject cardMesh = new GameObject("Layered Rounded Card Mesh");
            cardMesh.transform.SetParent(transform, false);
            cardMesh.AddComponent<MeshFilter>().sharedMesh = BuildRoundedCardMesh();
            cardRenderer = cardMesh.AddComponent<MeshRenderer>();
            backMaterial = cardBackMaterial;
            frontMaterial = attributeMaterial;
            faceLayerRenderers.Clear();
            CreateFrontLayer("Rarity Pattern", rarityPatternMaterial, 0.0008f);
            CreateFrontLayer("Card Content", contentMaterial, 0.0016f);
            CreateFrontLayer("Cost Symbol", costMaterial, 0.0024f);
            SetFaceUp(true);
        }

        public void BuildFromData(global::CardData data, global::CardColor color, Material attributeMaterial, Material cardBackMaterial,
            Material rarityPatternMaterial, Material illustrationMaterial, Material costMaterial, Font textFont,
            bool useEnglish = false)
        {
            GameObject cardMesh = new GameObject("Data Driven Rounded Card Mesh");
            cardMesh.transform.SetParent(transform, false);
            cardMesh.AddComponent<MeshFilter>().sharedMesh = BuildRoundedCardMesh();
            cardRenderer = cardMesh.AddComponent<MeshRenderer>();
            backMaterial = cardBackMaterial;
            bool isLegendary = data != null && data.Rare == global::CardRarity.Legendary;
            frontMaterial = data != null && data.Rare == global::CardRarity.Uncommon
                ? GetUncommonGlossyMaterial(attributeMaterial)
                : attributeMaterial;
            faceLayerRenderers.Clear();
            if (isLegendary)
            {
                if (illustrationMaterial != null)
                    CreateFrontLayer("Legendary Full Art", illustrationMaterial, 0.0008f, true);
            }
            else
            {
                CreateFrontLayer("Rarity Pattern", rarityPatternMaterial, 0.0008f);
                if (illustrationMaterial != null)
                    CreateIllustrationLayer("Card Illustration", illustrationMaterial, 0.0016f,
                        data != null && data.FitBackgroundImageToWidth);
            }
            CreateFrontLayer("Cost Symbol", costMaterial, 0.0024f);
            ApplyRarityFinish(data != null ? data.Rare : global::CardRarity.Common);

            Color textColor = isLegendary
                ? Color.white
                : color == global::CardColor.Black
                ? Color.white
                : Color.black;
            string localizedName = data != null ? data.GetLocalizedName(useEnglish) : string.Empty;
            string cardName = !string.IsNullOrWhiteSpace(localizedName)
                ? localizedName : useEnglish ? "Unnamed" : "이름 없음";
            string displayName = FormatCardName(cardName, out int longestNameLine, out _);
            string description = BuildTaggedDescription(data,
                data != null ? data.GetLocalizedDescription(useEnglish) : string.Empty, useEnglish);
            float nameScaleTarget = ContainsWideNameCharacter(cardName) ? 5f : 14f;
            float nameLengthScale = longestNameLine > nameScaleTarget
                ? nameScaleTarget / longestNameLine : 1f;
            float nameScale = 0.04f * nameLengthScale;
            CreateTextLayer("Card Name", displayName, new Vector3(0.20f, 1.39f, -0.0105f),
                textFont, 64, nameScale, TextAnchor.MiddleCenter, TextAlignment.Center, textColor, 30,
                CardNameMaximumWidth, CardNameMaximumHeight, isLegendary);
            SetCardNameHorizontalScale(ContainsWideNameCharacter(cardName)
                ? 1f : LatinCardNameHorizontalScale);
            CreateDescriptionLayer(description, new Vector3(0f, -0.47f, -0.0108f), textFont, textColor, 31,
                isLegendary);
            SetFaceUp(true);
        }

        public void SetDisplayName(string cardName)
        {
            if (string.IsNullOrWhiteSpace(cardName)) return;
            string displayName = FormatCardName(cardName, out int longestNameLine, out _);

            float nameScaleTarget = ContainsWideNameCharacter(cardName) ? 5f : 14f;
            float nameLengthScale = longestNameLine > nameScaleTarget
                ? nameScaleTarget / longestNameLine : 1f;
            float characterSize = 0.04f * nameLengthScale;
            TextMesh[] textMeshes = GetComponentsInChildren<TextMesh>(true);
            for (int i = 0; i < textMeshes.Length; i++)
            {
                TextMesh textMesh = textMeshes[i];
                if (textMesh == null || !textMesh.gameObject.name.StartsWith("Card Name")) continue;
                PrepareLegacyTextCharacters(textMesh.font, displayName, textMesh.fontSize);
                textMesh.text = displayName;
                textMesh.characterSize = characterSize;
                textMesh.transform.localScale = Vector3.one;
                MeshRenderer renderer = textMesh.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = GetWorldTextMaterial(textMesh.font);
                FitTextRendererInside(textMesh.transform, renderer,
                    CardNameMaximumWidth, CardNameMaximumHeight);
            }
            SetCardNameHorizontalScale(ContainsWideNameCharacter(cardName)
                ? 1f : LatinCardNameHorizontalScale);
        }

        private void SetCardNameHorizontalScale(float horizontalScale)
        {
            TextMesh[] textMeshes = GetComponentsInChildren<TextMesh>(true);
            for (int i = 0; i < textMeshes.Length; i++)
            {
                TextMesh textMesh = textMeshes[i];
                if (textMesh == null || !textMesh.gameObject.name.StartsWith("Card Name")) continue;
                Vector3 scale = textMesh.transform.localScale;
                scale.x *= horizontalScale;
                textMesh.transform.localScale = scale;
            }
        }

        private static string FormatCardName(string cardName, out int longestLine, out int lineCount)
        {
            string normalized = (cardName ?? string.Empty).Replace("\r", string.Empty)
                .Replace("\n", " ").Trim();
            int charactersPerLine = ContainsWideNameCharacter(normalized)
                ? CardNameCharactersPerLine : LatinCardNameCharactersPerLine;
            List<string> segments = new List<string>();
            int equipmentIndex = normalized.IndexOf('(');
            if (equipmentIndex > 0)
            {
                segments.Add(normalized.Substring(0, equipmentIndex).TrimEnd());
                segments.Add(normalized.Substring(equipmentIndex).TrimStart());
            }
            else
            {
                segments.Add(normalized);
            }

            List<string> lines = new List<string>();
            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                string remaining = segments[segmentIndex];
                while (remaining.Length > charactersPerLine)
                {
                    int splitIndex = charactersPerLine;
                    for (int i = charactersPerLine - 1; i > 0; i--)
                    {
                        if (!char.IsWhiteSpace(remaining[i])) continue;
                        splitIndex = i;
                        break;
                    }
                    string line = remaining.Substring(0, splitIndex).Trim();
                    if (line.Length == 0)
                    {
                        splitIndex = charactersPerLine;
                        line = remaining.Substring(0, splitIndex);
                    }
                    lines.Add(line);
                    remaining = remaining.Substring(splitIndex).TrimStart();
                }
                if (remaining.Length > 0) lines.Add(remaining);
            }
            if (lines.Count == 0) lines.Add(string.Empty);
            longestLine = 0;
            for (int i = 0; i < lines.Count; i++)
                longestLine = Mathf.Max(longestLine, lines[i].Length);
            lineCount = lines.Count;
            return string.Join("\n", lines);
        }

        private static bool ContainsWideNameCharacter(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= '\u1100' && c <= '\u11FF')
                    || (c >= '\u3130' && c <= '\u318F')
                    || (c >= '\uAC00' && c <= '\uD7AF')
                    || (c >= '\u2E80' && c <= '\u9FFF')) return true;
            }
            return false;
        }

        public void SetDisplayDescription(global::CardData data, string description, bool useEnglish = false)
        {
            string displayDescription = BuildTaggedDescription(data, description, useEnglish);
            TextMeshPro[] textMeshes = GetComponentsInChildren<TextMeshPro>(true);
            for (int i = 0; i < textMeshes.Length; i++)
            {
                TextMeshPro textMesh = textMeshes[i];
                if (textMesh == null || !textMesh.gameObject.name.StartsWith("Card Description")) continue;
                PrepareTmpCharacters(textMesh.font, displayDescription);
                textMesh.text = displayDescription;
                textMesh.ForceMeshUpdate(true, true);
            }
        }

        private static string BuildTaggedDescription(global::CardData data, string description, bool useEnglish)
        {
            string body = description ?? string.Empty;
            string natureEffect = useEnglish
                ? "When another Nature card ability activates, chain this ability."
                : "다른 자연 카드의 능력이 발동하면 이 능력 연쇄 발동";
            bool hasNatureChainTarget = false;
            if (data != null && data.DeckAbilities != null)
            {
                for (int i = 0; i < data.DeckAbilities.Count; i++)
                {
                    if (data.DeckAbilities[i] == null
                        || !data.DeckAbilities[i].CanBeTriggeredByNatureChain()) continue;
                    hasNatureChainTarget = true;
                    break;
                }
            }
            bool alreadyDescribesNatureTrigger = body.Contains(natureEffect)
                || (useEnglish
                    ? body.IndexOf("another Nature card", System.StringComparison.OrdinalIgnoreCase) >= 0
                        && body.IndexOf("chain", System.StringComparison.OrdinalIgnoreCase) >= 0
                    : body.Contains("다른 자연 카드") && body.Contains("연쇄 발동"));
            if (data != null && data.HasTag(global::CardTag.Nature)
                && hasNatureChainTarget && !alreadyDescribesNatureTrigger)
                body = string.IsNullOrWhiteSpace(body) ? natureEffect : body + "\n" + natureEffect;
            string stackEffect = useEnglish
                ? "Whenever a card is drawn, give 1 stack to every Stack card in the deck."
                : "카드를 뽑을 때 덱의 모든 스택 카드에 1스택 제공";
            bool alreadyDescribesStackEffect = useEnglish
                ? body.IndexOf("every Stack card", System.StringComparison.OrdinalIgnoreCase) >= 0
                : body.Contains("모든 스택 카드") && body.Contains("1스택");
            if (data != null && data.HasTag(global::CardTag.Stack) && !alreadyDescribesStackEffect)
                body = string.IsNullOrWhiteSpace(body) ? stackEffect : body + "\n" + stackEffect;
            string mineralEffect = useEnglish
                ? "Abilities do not activate from the deck and activate only when drawn."
                : "덱에 있을 때는 능력이 발동하지 않고 뽑힐 때만 발동";
            bool alreadyDescribesMineralEffect = useEnglish
                ? body.IndexOf("only when drawn", System.StringComparison.OrdinalIgnoreCase) >= 0
                : body.Contains("덱에 있을 때") && body.Contains("뽑힐 때만");
            if (data != null && data.HasTag(global::CardTag.Mineral) && !alreadyDescribesMineralEffect)
                body = string.IsNullOrWhiteSpace(body) ? mineralEffect : body + "\n" + mineralEffect;
            string miningEffect = useEnglish
                ? "Mining odds improve with the number of Mining cards in the deck."
                : "덱에 보유한 채굴 카드 수에 따라 광석 카드 채굴 확률 향상";
            if (data != null && data.HasTag(global::CardTag.Mining)
                && body.IndexOf(useEnglish ? "Mining odds" : "채굴 확률",
                    System.StringComparison.OrdinalIgnoreCase) < 0)
                body = string.IsNullOrWhiteSpace(body) ? miningEffect : body + "\n" + miningEffect;
            if (data == null || data.Tags == null || data.Tags.Count == 0)
                return body;
            List<string> tagNames = new List<string>();
            for (int i = 0; i < data.Tags.Count; i++)
            {
                string tagName;
                switch (data.Tags[i])
                {
                    case global::CardTag.Nature: tagName = useEnglish ? "Nature" : "\uC790\uC5F0"; break;
                    case global::CardTag.Magic: tagName = useEnglish ? "Magic" : "\uB9C8\uBC95"; break;
                    case global::CardTag.Rune: tagName = useEnglish ? "Rune" : "\uB8EC"; break;
                    case global::CardTag.Weapon: tagName = useEnglish ? "Weapon" : "\uBB34\uAE30"; break;
                    case global::CardTag.Magitech: tagName = useEnglish ? "Magitech" : "\uB9C8\uACF5\uD559"; break;
                    case global::CardTag.Warrior: tagName = useEnglish ? "Warrior" : "\uC804\uC0AC"; break;
                    case global::CardTag.Mage: tagName = useEnglish ? "Mage" : "\uB9C8\uBC95\uC0AC"; break;
                    case global::CardTag.Stack: tagName = useEnglish ? "Stack" : "\uC2A4\uD0DD"; break;
                    case global::CardTag.Mineral: tagName = useEnglish ? "Mineral" : "\uAD11\uBB3C"; break;
                    case global::CardTag.Mining: tagName = useEnglish ? "Mining" : "\uCC44\uAD74"; break;
                    default: tagName = data.Tags[i].ToString(); break;
                }
                if (!tagNames.Contains(tagName)) tagNames.Add(tagName);
            }
            string tagLine = "[" + string.Join(", ", tagNames) + "]";
            return string.IsNullOrWhiteSpace(body) ? tagLine : tagLine + "\n" + body;
        }
        private void CreateIllustrationLayer(string layerName, Material material, float depthOffset,
            bool fitBackgroundToWidth)
        {
            const float frameMinX = -0.79f;
            const float frameMinY = -0.20f;
            const float frameMaxX = 0.79f;
            const float frameMaxY = 1.10f;
            float width = frameMaxX - frameMinX;
            float height = frameMaxY - frameMinY;
            float centerX = (frameMinX + frameMaxX) * 0.5f;
            float centerY = (frameMinY + frameMaxY) * 0.5f;

            if (material != null)
            {
                material.mainTextureScale = Vector2.one;
                material.mainTextureOffset = Vector2.zero;
            }
            Texture texture = material != null ? material.mainTexture : null;
            if (texture != null && texture.width > 0 && texture.height > 0)
            {
                float textureAspect = texture.width / (float)texture.height;
                float frameAspect = width / height;
                if (fitBackgroundToWidth)
                {
                    if (textureAspect < frameAspect)
                    {
                        float visibleVerticalRatio = textureAspect / frameAspect;
                        material.mainTextureScale = new Vector2(1f, visibleVerticalRatio);
                        material.mainTextureOffset = new Vector2(0f, (1f - visibleVerticalRatio) * 0.5f);
                    }
                    else
                    {
                        height = width / textureAspect;
                    }
                }
                else if (textureAspect < frameAspect)
                {
                    width = height * textureAspect;
                }
                else
                {
                    height = width / textureAspect;
                }
            }

            GameObject layer = new GameObject(layerName);
            layer.transform.SetParent(transform, false);
            layer.AddComponent<MeshFilter>().sharedMesh = BuildRectLayerMesh(
                centerX - width * 0.5f, centerY - height * 0.5f,
                centerX + width * 0.5f, centerY + height * 0.5f, depthOffset);
            MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 10;
            faceLayerRenderers.Add(renderer);
        }

        private static Material GetWorldTextMaterial(Font font)
        {
            if (WorldTextMaterials.TryGetValue(font, out Material cached))
            {
                cached.mainTexture = font.material.mainTexture;
                if (cached.HasProperty("_MainTex"))
                    cached.SetTexture("_MainTex", font.material.mainTexture);
                return cached;
            }

            Shader shader = Shader.Find("CardOpen/WorldText");
            Material material = shader != null ? new Material(shader) : new Material(font.material);
            material.name = "World Text - " + font.name;
            material.mainTexture = font.material.mainTexture;
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", font.material.mainTexture);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            material.renderQueue = 3100;
            WorldTextMaterials.Add(font, material);
            return material;
        }
        private static void PrepareLegacyTextCharacters(Font font, string value, int fontSize)
        {
            if (font == null || string.IsNullOrEmpty(value)) return;
            font.RequestCharactersInTexture(value, fontSize, FontStyle.Bold);
            HandleFontTextureRebuilt(font);
        }

        private static void HandleFontTextureRebuilt(Font rebuiltFont)
        {
            if (rebuiltFont == null
                || !WorldTextMaterials.TryGetValue(rebuiltFont, out Material material)
                || material == null) return;
            Texture atlasTexture = rebuiltFont.material.mainTexture;
            material.mainTexture = atlasTexture;
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", atlasTexture);
        }

        private void CreateTextLayer(string layerName, string value, Vector3 position, Font font, int fontSize,
            float characterSize, TextAnchor anchor, TextAlignment alignment, Color color, int sortingOrder,
            float maximumWidth = 0f, float maximumHeight = 0f, bool addOutline = false)
        {
            if (font == null || string.IsNullOrEmpty(value)) return;
            PrepareLegacyTextCharacters(font, value, fontSize);
            if (addOutline)
                CreateTextOutlineLayers(layerName, value, position, font, fontSize, characterSize,
                    anchor, alignment, sortingOrder, maximumWidth, maximumHeight);
            GameObject textObject = new GameObject(layerName);
            textObject.transform.SetParent(transform, false);
            textObject.transform.localPosition = position;
            TextMesh textMesh = textObject.AddComponent<TextMesh>();
            textMesh.font = font;
            textMesh.fontSize = fontSize;
            textMesh.characterSize = characterSize;
            textMesh.anchor = anchor;
            textMesh.alignment = alignment;
            textMesh.color = color;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.text = value;
            MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetWorldTextMaterial(font);
            renderer.sortingOrder = sortingOrder;
            FitTextRendererInside(textObject.transform, renderer, maximumWidth, maximumHeight);
            faceLayerRenderers.Add(renderer);
            CreateTextWeightLayers(layerName, value, position, font, fontSize, characterSize,
                anchor, alignment, color, sortingOrder, maximumWidth, maximumHeight);
        }

        private void CreateTextWeightLayers(string layerName, string value, Vector3 position, Font font,
            int fontSize, float characterSize, TextAnchor anchor, TextAlignment alignment, Color color,
            int sortingOrder, float maximumWidth, float maximumHeight)
        {
            const float weightOffset = 0.0025f;
            float[] offsets = { -weightOffset, weightOffset };
            for (int i = 0; i < offsets.Length; i++)
            {
                GameObject weightObject = new GameObject(layerName + " Weight");
                weightObject.transform.SetParent(transform, false);
                weightObject.transform.localPosition = position + new Vector3(offsets[i], 0f, 0.0002f);
                TextMesh weightText = weightObject.AddComponent<TextMesh>();
                weightText.font = font;
                weightText.fontSize = fontSize;
                weightText.characterSize = characterSize;
                weightText.anchor = anchor;
                weightText.alignment = alignment;
                weightText.color = color;
                weightText.fontStyle = FontStyle.Bold;
                weightText.text = value;
                MeshRenderer weightRenderer = weightObject.GetComponent<MeshRenderer>();
                weightRenderer.sharedMaterial = GetWorldTextMaterial(font);
                weightRenderer.sortingOrder = sortingOrder;
                FitTextRendererInside(weightObject.transform, weightRenderer, maximumWidth, maximumHeight);
                faceLayerRenderers.Add(weightRenderer);
            }
        }

        private void CreateTextOutlineLayers(string layerName, string value, Vector3 position, Font font,
            int fontSize, float characterSize, TextAnchor anchor, TextAlignment alignment, int sortingOrder,
            float maximumWidth, float maximumHeight)
        {
            float offset = Mathf.Max(0.008f, characterSize * 0.20f);
            Vector2[] offsets =
            {
                new Vector2(-offset, 0f),
                new Vector2(offset, 0f),
                new Vector2(0f, -offset),
                new Vector2(0f, offset),
                new Vector2(-offset, -offset),
                new Vector2(-offset, offset),
                new Vector2(offset, -offset),
                new Vector2(offset, offset)
            };
            for (int i = 0; i < offsets.Length; i++)
            {
                GameObject outlineObject = new GameObject(layerName + " Outline");
                outlineObject.transform.SetParent(transform, false);
                outlineObject.transform.localPosition = position
                    + new Vector3(offsets[i].x, offsets[i].y, 0.0008f);
                TextMesh outline = outlineObject.AddComponent<TextMesh>();
                outline.font = font;
                outline.fontSize = fontSize;
                outline.characterSize = characterSize;
                outline.anchor = anchor;
                outline.alignment = alignment;
                outline.color = Color.black;
                outline.fontStyle = FontStyle.Bold;
                outline.text = value;
                MeshRenderer renderer = outlineObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = GetWorldTextMaterial(font);
                renderer.sortingOrder = sortingOrder - 1;
                FitTextRendererInside(outlineObject.transform, renderer, maximumWidth, maximumHeight);
                faceLayerRenderers.Add(renderer);
            }
        }

        private static void FitTextRendererInside(Transform textTransform, Renderer renderer,
            float maximumWidth, float maximumHeight)
        {
            if (renderer == null || maximumWidth <= 0f || maximumHeight <= 0f) return;
            Vector3 renderedSize = renderer.localBounds.size;
            if (renderedSize.x <= 0f || renderedSize.y <= 0f) return;

            float fitScale = Mathf.Min(1f, maximumWidth / renderedSize.x, maximumHeight / renderedSize.y);
            textTransform.localScale = new Vector3(fitScale, fitScale, 1f);
        }

        private static TMP_FontAsset GetTmpFontAsset(Font font)
        {
            if (font == null) return TMP_Settings.defaultFontAsset;
            if (TmpFontAssets.TryGetValue(font, out TMP_FontAsset cached)) return cached;

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(font);
            if (fontAsset == null) fontAsset = TMP_Settings.defaultFontAsset;
            if (fontAsset != null)
            {
                fontAsset.name = "Runtime TMP - " + font.name;
                fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                fontAsset.isMultiAtlasTexturesEnabled = true;
            }
            TmpFontAssets.Add(font, fontAsset);
            return fontAsset;
        }

        private static void PrepareTmpCharacters(TMP_FontAsset fontAsset, string value)
        {
            if (fontAsset == null || string.IsNullOrEmpty(value)
                || fontAsset.atlasPopulationMode != AtlasPopulationMode.Dynamic) return;
            fontAsset.TryAddCharacters(value, out _);
        }

        private void CreateDescriptionLayer(string value, Vector3 position, Font font, Color color, int sortingOrder, bool addOutline = false)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            TMP_FontAsset fontAsset = GetTmpFontAsset(font);
            if (fontAsset == null) return;

            GameObject textObject = new GameObject("Card Description");
            textObject.transform.SetParent(transform, false);
            TextMeshPro textMesh = textObject.AddComponent<TextMeshPro>();
            RectTransform rectTransform = textMesh.rectTransform;
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.sizeDelta = new Vector2(1.50f, 0.98f);
            rectTransform.localPosition = position;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;

            textMesh.font = fontAsset;
            PrepareTmpCharacters(fontAsset, value);
            textMesh.text = value.Replace("\r", string.Empty);
            textMesh.color = color;
            textMesh.fontStyle = FontStyles.Bold;
            textMesh.fontWeight = FontWeight.Heavy;
            textMesh.alignment = TextAlignmentOptions.Top;
            textMesh.textWrappingMode = TextWrappingModes.Normal;
            textMesh.overflowMode = TextOverflowModes.Truncate;
            textMesh.enableAutoSizing = true;
            textMesh.fontSize = 1.45f;
            textMesh.fontSizeMax = 1.45f;
            textMesh.fontSizeMin = 0.35f;
            textMesh.margin = new Vector4(0.025f, 0.015f, 0.025f, 0.015f);
            textMesh.richText = false;
            textMesh.extraPadding = true;
            textMesh.outlineWidth = 0f;
            textMesh.ForceMeshUpdate(true, true);
            if (addOutline)
                CreateDescriptionOutlineLayers(textObject, position, sortingOrder);

            MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
            renderer.sortingOrder = sortingOrder;
            faceLayerRenderers.Add(renderer);
        }
        private void CreateDescriptionOutlineLayers(GameObject source, Vector3 position, int sortingOrder)
        {
            const float offset = 0.008f;
            Vector2[] offsets =
            {
                new Vector2(-offset, 0f),
                new Vector2(offset, 0f),
                new Vector2(0f, -offset),
                new Vector2(0f, offset),
                new Vector2(-offset, -offset),
                new Vector2(-offset, offset),
                new Vector2(offset, -offset),
                new Vector2(offset, offset)
            };
            for (int i = 0; i < offsets.Length; i++)
            {
                GameObject outlineObject = Instantiate(source, transform);
                outlineObject.name = "Card Description Outline";
                TextMeshPro outlineText = outlineObject.GetComponent<TextMeshPro>();
                outlineText.color = Color.black;
                outlineText.outlineWidth = 0f;
                RectTransform outlineTransform = outlineText.rectTransform;
                outlineTransform.localPosition = position
                    + new Vector3(offsets[i].x, offsets[i].y, 0.0008f);
                outlineTransform.localRotation = Quaternion.identity;
                outlineTransform.localScale = Vector3.one;
                outlineText.ForceMeshUpdate(true, true);
                MeshRenderer outlineRenderer = outlineObject.GetComponent<MeshRenderer>();
                outlineRenderer.sortingOrder = sortingOrder - 1;
                faceLayerRenderers.Add(outlineRenderer);
            }
        }

        private void CreateFrontLayer(string layerName, Material material, float depthOffset,
            bool preserveTextureAspect = false)
        {
            GameObject layer = new GameObject(layerName);
            layer.transform.SetParent(transform, false);
            layer.AddComponent<MeshFilter>().sharedMesh = BuildFrontLayerMesh(
                depthOffset, material, preserveTextureAspect);
            MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            faceLayerRenderers.Add(renderer);
        }

        public void PrepareFaceDown(Vector3 position, float scale, float angle)
        {
            transform.localPosition = position;
            transform.localScale = Vector3.one * scale;
            transform.localRotation = Quaternion.Euler(0f, 180f, angle);
            SetFaceUp(false);
        }

        public void PrepareFaceUp(Vector3 position, float scale, float angle)
        {
            transform.localPosition = position;
            transform.localScale = Vector3.one * scale;
            transform.localRotation = Quaternion.Euler(-4f, 0f, angle);
            SetFaceUp(true);
        }

        public IEnumerator MoveToFront(Vector3 position, float scale, float angle)
        {
            Vector3 startPosition = transform.localPosition;
            Vector3 startScale = transform.localScale;
            Quaternion startRotation = transform.localRotation;
            Vector3 endScale = Vector3.one * scale;
            Quaternion endRotation = Quaternion.Euler(-4f, 0f, angle);
            const float duration = 0.05f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float normalized = Mathf.Clamp01(t / duration);
                float u = Mathf.SmoothStep(0f, 1f, normalized);
                Vector3 movingPosition = Vector3.Lerp(startPosition, position, u);
                movingPosition.y += Mathf.Sin(normalized * Mathf.PI) * 0.08f;
                transform.localPosition = movingPosition;
                transform.localScale = Vector3.Lerp(startScale, endScale, u);
                transform.localRotation = Quaternion.Slerp(startRotation, endRotation, u);
                yield return null;
            }
            transform.localPosition = position;
            transform.localScale = endScale;
            transform.localRotation = endRotation;
            SetFaceUp(true);
        }

        public IEnumerator RevealInPlace()
        {
            Quaternion start = transform.localRotation;
            float zAngle = Mathf.DeltaAngle(0f, transform.localEulerAngles.z);
            Quaternion end = Quaternion.Euler(-4f, 0f, zAngle);
            const float duration = 0.42f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / duration);
                transform.localRotation = Quaternion.Slerp(start, end, u);
                if (u > 0.5f && !IsFaceUp) SetFaceUp(true);
                yield return null;
            }
            transform.localRotation = end;
            SetFaceUp(true);
        }

        public void AccelerateSlideAway() { accelerateSlide = true; }

        public IEnumerator SlideAway(float direction)
        {
            accelerateSlide = false;
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            Vector3 endPosition = startPosition + new Vector3(direction * 9f, 1.1f, -0.5f);
            Quaternion endRotation = Quaternion.Euler(0f, 0f, direction * -48f);
            const float duration = 0.36f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime * (accelerateSlide ? 4.5f : 1f);
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                transform.position = Vector3.Lerp(startPosition, endPosition, u);
                transform.rotation = Quaternion.Slerp(startRotation, endRotation, u);
                yield return null;
            }
            transform.position = endPosition;
            transform.rotation = endRotation;
        }

        private static Material GetUncommonGlossyMaterial(Material source)
        {
            if (source == null) return null;
            if (UncommonGlossyMaterials.TryGetValue(source, out Material cached)) return cached;

            Material glossy = new Material(source) { name = source.name + " - Uncommon Pack Gloss" };
            if (glossy.HasProperty("_Smoothness")) glossy.SetFloat("_Smoothness", 0.82f);
            if (glossy.HasProperty("_Metallic")) glossy.SetFloat("_Metallic", 0.2f);
            if (glossy.HasProperty("_CoatMask")) glossy.SetFloat("_CoatMask", 0.35f);
            if (glossy.HasProperty("_CoatSmoothness")) glossy.SetFloat("_CoatSmoothness", 0.9f);
            glossy.EnableKeyword("_CLEARCOAT");
            UncommonGlossyMaterials.Add(source, glossy);
            return glossy;
        }
        private void ApplyRarityFinish(global::CardRarity rarity)
        {
            if (rarity == global::CardRarity.Common) return;
            Material material = GetRarityFinishMaterial(rarity);
            if (material != null) CreateFrontLayer("Rarity Finish - " + rarity, material, 0.003f);
        }

        private static Material GetRarityFinishMaterial(global::CardRarity rarity)
        {
            if (RarityFinishMaterials.TryGetValue(rarity, out Material cached)) return cached;
            Shader shader = Shader.Find("CardOpen/RarityFinish");
            if (shader == null || !shader.isSupported) return null;

            Material material = new Material(shader) { name = "Card Finish - " + rarity };
            switch (rarity)
            {
                case global::CardRarity.Uncommon:
                    material.SetFloat("_EffectMode", 0f);
                    material.SetColor("_Tint", new Color(0.95f, 0.98f, 1f, 1f));
                    material.SetFloat("_Intensity", 0.28f);
                    break;
                case global::CardRarity.Rare:
                    material.SetFloat("_EffectMode", 1f);
                    material.SetColor("_Tint", new Color(0.72f, 0.84f, 1f, 1f));
                    material.SetFloat("_Intensity", 0.78f);
                    break;
                case global::CardRarity.Epic:
                    material.SetFloat("_EffectMode", 2f);
                    material.SetColor("_Tint", new Color(0.72f, 0.30f, 1f, 1f));
                    material.SetFloat("_Intensity", 0.9f);
                    break;
                case global::CardRarity.Legendary:
                    material.SetFloat("_EffectMode", 3f);
                    material.SetColor("_Tint", new Color(0.32f, 0.88f, 1f, 1f));
                    material.SetFloat("_Intensity", 1f);
                    break;
                default:
                    return null;
            }
            material.renderQueue = 3040;
            RarityFinishMaterials.Add(rarity, material);
            return material;
        }
        public void EnableHologram()
        {
            if (IsHolographic) return;
            Material material = GetHologramMaterial();
            if (material == null) return;
            CreateFrontLayer("Hologram Foil", material, 0.0032f);
            IsHolographic = true;
        }

        private static Material GetHologramMaterial()
        {
            if (hologramMaterial != null) return hologramMaterial;
            Shader shader = Shader.Find("CardOpen/Hologram");
            if (shader == null) return null;

            hologramMaterial = new Material(shader) { name = "Animated Card Hologram" };
            hologramMaterial.SetFloat("_Intensity", 0.65f);
            hologramMaterial.renderQueue = 3050;
            return hologramMaterial;
        }
        public void SetFaceDetailsVisible(bool visible)
        {
            for (int i = 0; i < faceLayerRenderers.Count; i++)
                if (faceLayerRenderers[i] != null) faceLayerRenderers[i].enabled = visible;
        }

        private void SetFaceUp(bool faceUp)
        {
            IsFaceUp = faceUp;
            if (cardRenderer != null)
                cardRenderer.sharedMaterials = new[] { frontMaterial, backMaterial, frontMaterial };
            SetFaceDetailsVisible(faceUp);
        }

        private static Mesh BuildRectLayerMesh(float minX, float minY, float maxX, float maxY, float depthOffset)
        {
            float z = -0.006f - depthOffset;
            Vector3[] vertices =
            {
                new Vector3(minX, minY, z), new Vector3(minX, maxY, z),
                new Vector3(maxX, maxY, z), new Vector3(maxX, minY, z)
            };
            Vector2[] uvs =
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 0f)
            };
            Mesh mesh = new Mesh { name = "Card Illustration Layer" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildFrontLayerMesh(float depthOffset, Material material = null, bool preserveTextureAspect = false)
        {
            const float width = 1.82f;
            const float height = 3.28f;
            const float radius = 0.09f;
            const int cornerSegments = 3;
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float uvScaleX = 1f;
            float uvScaleY = 1f;
            Texture texture = null;
            if (preserveTextureAspect && material != null)
            {
                if (material.HasProperty("_BaseMap"))
                    texture = material.GetTexture("_BaseMap");
                else if (material.HasProperty("_MainTex"))
                    texture = material.GetTexture("_MainTex");
            }
            if (preserveTextureAspect && texture != null && texture.width > 0 && texture.height > 0)
            {
                float textureAspect = texture.width / (float)texture.height;
                float cardAspect = width / height;
                if (textureAspect > cardAspect)
                    uvScaleX = cardAspect / textureAspect;
                else if (textureAspect < cardAspect)
                    uvScaleY = textureAspect / cardAspect;
            }
            List<Vector2> outline = new List<Vector2>();
            AddCorner(outline, new Vector2(halfWidth - radius, -halfHeight + radius), -90f, 0f, radius, cornerSegments);
            AddCorner(outline, new Vector2(halfWidth - radius, halfHeight - radius), 0f, 90f, radius, cornerSegments);
            AddCorner(outline, new Vector2(-halfWidth + radius, halfHeight - radius), 90f, 180f, radius, cornerSegments);
            AddCorner(outline, new Vector2(-halfWidth + radius, -halfHeight + radius), 180f, 270f, radius, cornerSegments);

            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            float z = -0.006f - depthOffset;
            vertices.Add(new Vector3(0f, 0f, z));
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 point = outline[i];
                vertices.Add(new Vector3(point.x, point.y, z));
                uvs.Add(new Vector2(point.x / width * uvScaleX + 0.5f, point.y / height * uvScaleY + 0.5f));
            }
            for (int i = 0; i < outline.Count; i++)
            {
                int next = (i + 1) % outline.Count;
                triangles.Add(0);
                triangles.Add(next + 1);
                triangles.Add(i + 1);
            }
            Mesh mesh = new Mesh { name = "Rounded Card Front Layer" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildRoundedCardMesh()
        {
            const float width = 1.82f;
            const float height = 3.28f;
            const float radius = 0.09f;
            const float thickness = 0.012f;
            const int cornerSegments = 3;
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float halfDepth = thickness * 0.5f;

            List<Vector2> outline = new List<Vector2>();
            AddCorner(outline, new Vector2(halfWidth - radius, -halfHeight + radius), -90f, 0f, radius, cornerSegments);
            AddCorner(outline, new Vector2(halfWidth - radius, halfHeight - radius), 0f, 90f, radius, cornerSegments);
            AddCorner(outline, new Vector2(-halfWidth + radius, halfHeight - radius), 90f, 180f, radius, cornerSegments);
            AddCorner(outline, new Vector2(-halfWidth + radius, -halfHeight + radius), 180f, 270f, radius, cornerSegments);

            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            int frontCenter = vertices.Count;
            vertices.Add(new Vector3(0f, 0f, -halfDepth));
            uvs.Add(new Vector2(0.5f, 0.5f));
            int frontStart = vertices.Count;
            foreach (Vector2 point in outline)
            {
                vertices.Add(new Vector3(point.x, point.y, -halfDepth));
                uvs.Add(new Vector2(point.x / width + 0.5f, point.y / height + 0.5f));
            }
            int backCenter = vertices.Count;
            vertices.Add(new Vector3(0f, 0f, halfDepth));
            uvs.Add(new Vector2(0.5f, 0.5f));
            int backStart = vertices.Count;
            foreach (Vector2 point in outline)
            {
                vertices.Add(new Vector3(point.x, point.y, halfDepth));
                uvs.Add(new Vector2(point.x / width + 0.5f, point.y / height + 0.5f));
            }

            List<int> frontTriangles = new List<int>();
            List<int> backTriangles = new List<int>();
            List<int> sideTriangles = new List<int>();
            int count = outline.Count;
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                frontTriangles.Add(frontCenter); frontTriangles.Add(frontStart + next); frontTriangles.Add(frontStart + i);
                backTriangles.Add(backCenter); backTriangles.Add(backStart + i); backTriangles.Add(backStart + next);
                sideTriangles.Add(frontStart + i); sideTriangles.Add(frontStart + next); sideTriangles.Add(backStart + i);
                sideTriangles.Add(frontStart + next); sideTriangles.Add(backStart + next); sideTriangles.Add(backStart + i);
            }
            Mesh mesh = new Mesh { name = "Single Ultra Thin Rounded Card" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(frontTriangles, 0);
            mesh.SetTriangles(backTriangles, 1);
            mesh.SetTriangles(sideTriangles, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddCorner(List<Vector2> points, Vector2 center, float startAngle, float endAngle, float radius, int segments)
        {
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(startAngle, endAngle, i / (float)segments) * Mathf.Deg2Rad;
                points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }
    }
}
