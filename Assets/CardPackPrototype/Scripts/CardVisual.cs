using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CardOpen.Prototype
{
    public sealed class CardVisual : MonoBehaviour
    {
        private MeshRenderer cardRenderer;
        private Material backMaterial;
        private Material frontMaterial;
        private bool accelerateSlide;
        private readonly List<MeshRenderer> faceLayerRenderers = new List<MeshRenderer>();
        private static readonly Dictionary<Font, Material> WorldTextMaterials = new Dictionary<Font, Material>();
        private static Material hologramMaterial;
        private static readonly Dictionary<global::CardRarity, Material> RarityFinishMaterials =
            new Dictionary<global::CardRarity, Material>();
        private static readonly Dictionary<Material, Material> UncommonGlossyMaterials =
            new Dictionary<Material, Material>();
        public bool IsHolographic { get; private set; }
        public bool IsFaceUp { get; private set; }

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
            Material rarityPatternMaterial, Material illustrationMaterial, Material costMaterial, Font textFont)
        {
            GameObject cardMesh = new GameObject("Data Driven Rounded Card Mesh");
            cardMesh.transform.SetParent(transform, false);
            cardMesh.AddComponent<MeshFilter>().sharedMesh = BuildRoundedCardMesh();
            cardRenderer = cardMesh.AddComponent<MeshRenderer>();
            backMaterial = cardBackMaterial;
            frontMaterial = data != null && data.Rare == global::CardRarity.Uncommon
                ? GetUncommonGlossyMaterial(attributeMaterial)
                : attributeMaterial;
            faceLayerRenderers.Clear();
            CreateFrontLayer("Rarity Pattern", rarityPatternMaterial, 0.0008f);
            if (illustrationMaterial != null)
                CreateIllustrationLayer("Card Illustration", illustrationMaterial, 0.0016f);
            CreateFrontLayer("Cost Symbol", costMaterial, 0.0024f);
            ApplyRarityFinish(data != null ? data.Rare : global::CardRarity.Common);

            Color textColor = color == global::CardColor.Black
                ? Color.white
                : Color.black;
            string cardName = data != null && !string.IsNullOrWhiteSpace(data.Name) ? data.Name : "이름 없음";
            string displayName = cardName;
            int longestNameLine = cardName.Length;
            if (cardName.Length > 9)
            {
                int splitIndex = Mathf.CeilToInt(cardName.Length * 0.5f);
                displayName = cardName.Insert(splitIndex, "\n");
                longestNameLine = Mathf.Max(splitIndex, cardName.Length - splitIndex);
            }
            string description = data != null ? WrapDescription(data.Description, 9) : string.Empty;
            float descriptionScale = CalculateFittedTextScale(description, 0.03f, 9, 6);
            float nameLengthScale = longestNameLine > 5 ? 5f / longestNameLine : 1f;
            float nameScale = 0.04f * nameLengthScale;
            CreateTextLayer("Card Name", displayName, new Vector3(0.20f, 1.39f, -0.0105f),
                textFont, 64, nameScale, TextAnchor.MiddleCenter, TextAlignment.Center, textColor, 30);
            CreateTextLayer("Card Description", description, new Vector3(0f, -0.47f, -0.0108f),
                textFont, 56, descriptionScale, TextAnchor.UpperCenter, TextAlignment.Center, textColor, 31,
                1.50f, 0.98f);
            SetFaceUp(true);
        }

        private void CreateIllustrationLayer(string layerName, Material material, float depthOffset)
        {
            GameObject layer = new GameObject(layerName);
            layer.transform.SetParent(transform, false);
            layer.AddComponent<MeshFilter>().sharedMesh = BuildRectLayerMesh(-0.75f, -0.20f, 0.75f, 1.10f, depthOffset);
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
        private void CreateTextLayer(string layerName, string value, Vector3 position, Font font, int fontSize,
            float characterSize, TextAnchor anchor, TextAlignment alignment, Color color, int sortingOrder,
            float maximumWidth = 0f, float maximumHeight = 0f)
        {
            if (font == null || string.IsNullOrEmpty(value)) return;
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
            textMesh.text = value;
            MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetWorldTextMaterial(font);
            renderer.sortingOrder = 0;
            FitTextRendererInside(textObject.transform, renderer, maximumWidth, maximumHeight);
            faceLayerRenderers.Add(renderer);
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

        private static float CalculateFittedTextScale(string value, float baseScale,
            int maximumCharactersPerLine, int maximumLines)
        {
            if (string.IsNullOrEmpty(value)) return baseScale;
            string[] lines = value.Split('\n');
            int longestLine = 0;
            for (int i = 0; i < lines.Length; i++)
                longestLine = Mathf.Max(longestLine, lines[i].Length);

            float widthScale = longestLine > maximumCharactersPerLine
                ? maximumCharactersPerLine / (float)longestLine
                : 1f;
            float heightScale = lines.Length > maximumLines
                ? maximumLines / (float)lines.Length
                : 1f;
            return baseScale * Mathf.Min(1f, widthScale, heightScale);
        }

        private static string WrapDescription(string value, int charactersPerLine)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string normalized = value.Replace("\r", string.Empty);
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            int column = 0;
            for (int i = 0; i < normalized.Length; i++)
            {
                char character = normalized[i];
                if (character == '\n')
                {
                    builder.Append(character);
                    column = 0;
                    continue;
                }
                if (column >= charactersPerLine && character != ' ')
                {
                    builder.Append('\n');
                    column = 0;
                }
                builder.Append(character);
                column++;
            }
            return builder.ToString();
        }

        private void CreateFrontLayer(string layerName, Material material, float depthOffset)
        {
            GameObject layer = new GameObject(layerName);
            layer.transform.SetParent(transform, false);
            layer.AddComponent<MeshFilter>().sharedMesh = BuildFrontLayerMesh(depthOffset);
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

        private static Mesh BuildFrontLayerMesh(float depthOffset)
        {
            const float width = 1.82f;
            const float height = 3.28f;
            const float radius = 0.09f;
            const int cornerSegments = 3;
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
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
                uvs.Add(new Vector2(point.x / width + 0.5f, point.y / height + 0.5f));
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
