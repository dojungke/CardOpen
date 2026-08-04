using System;
using System.Collections.Generic;
using System.IO;
using CardOpen.Prototype;
using UnityEditor;
using UnityEngine;

public sealed class CardCaptureWindow : EditorWindow
{
    private const int CaptureLayer = 31;
    private CardData card;
    private CardColor color = CardColor.Green;
    private int number = 1;
    private bool useEnglish;
    private bool holographic;
    private bool captureBack;
    private bool transparentBackground = true;
    private Color backgroundColor = new Color(0.02f, 0.025f, 0.045f, 1f);
    private int width = 1440;
    private int height = 2560;
    private int antiAliasing = 4;

    [MenuItem("CardOpen/카드 고화질 PNG 저장")]
    public static void Open()
    {
        OpenWithCard(Selection.activeObject as CardData);
    }

    public static void OpenWithCard(CardData selectedCard)
    {
        CardCaptureWindow window = GetWindow<CardCaptureWindow>("카드 PNG 저장");
        if (selectedCard != null) window.card = selectedCard;
        window.minSize = new Vector2(390f, 430f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("고화질 카드 PNG 저장", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("게임의 카드 렌더링을 그대로 사용해 PNG로 저장합니다.", MessageType.Info);

        card = (CardData)EditorGUILayout.ObjectField("카드 데이터", card, typeof(CardData), false);
        color = (CardColor)EditorGUILayout.EnumPopup("색상", color);
        number = EditorGUILayout.IntSlider("숫자", number, 1, 6);
        useEnglish = EditorGUILayout.Toggle("영문", useEnglish);
        holographic = EditorGUILayout.Toggle("홀로그램", holographic);
        captureBack = EditorGUILayout.Toggle("뒷면 저장", captureBack);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("이미지", EditorStyles.boldLabel);
        width = Mathf.Clamp(EditorGUILayout.IntField("가로 해상도", width), 256, 8192);
        height = Mathf.Clamp(EditorGUILayout.IntField("세로 해상도", height), 256, 8192);
        antiAliasing = EditorGUILayout.IntPopup("안티앨리어싱", antiAliasing,
            new[] { "없음", "2배", "4배", "8배" }, new[] { 1, 2, 4, 8 });
        transparentBackground = EditorGUILayout.Toggle("투명 배경", transparentBackground);
        using (new EditorGUI.DisabledScope(transparentBackground))
            backgroundColor = EditorGUILayout.ColorField("배경색", backgroundColor);

        EditorGUILayout.Space(12f);
        using (new EditorGUI.DisabledScope(card == null))
        {
            if (GUILayout.Button("PNG 저장...", GUILayout.Height(38f))) SaveCardPng();
        }
    }

    private void SaveCardPng()
    {
        if (card == null) return;
        string localizedName = card.GetLocalizedName(useEnglish);
        string defaultName = SanitizeFileName(string.IsNullOrWhiteSpace(localizedName) ? card.name : localizedName);
        if (captureBack) defaultName += "_Back";
        if (holographic && !captureBack) defaultName += "_Holographic";
        string path = EditorUtility.SaveFilePanel("카드 PNG 저장", Application.dataPath,
            defaultName + ".png", "png");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            byte[] png = RenderCard();
            File.WriteAllBytes(path, png);
            if (IsInsideProject(path)) AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(path);
            Debug.Log("Card PNG saved: " + path);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("카드 PNG 저장 실패", exception.Message, "확인");
        }
    }

    private byte[] RenderCard()
    {
        GameObject root = null;
        RenderTexture renderTexture = null;
        Texture2D capturedTexture = null;
        RenderTexture previousActive = RenderTexture.active;
        List<Material> ownedMaterials = new List<Material>();
        try
        {
            root = new GameObject("Card Capture Root") { hideFlags = HideFlags.HideAndDontSave };
            GameObject cardObject = new GameObject("Captured Card - " + card.name);
            cardObject.transform.SetParent(root.transform, false);
            CardVisual visual = cardObject.AddComponent<CardVisual>();

            Material attributeMaterial = CreateTextureMaterial(
                Resources.Load<Texture2D>("CardAssets/Attributes/Attribute" + color), false, 0, ownedMaterials);
            Material cardBackMaterial = CreateTextureMaterial(
                Resources.Load<Texture2D>("CardAssets/Attributes/AttributeBackRemasterPurple"), false, 0, ownedMaterials);
            Material rarityMaterial = CreateTextureMaterial(
                Resources.Load<Texture2D>("CardAssets/Rarities/Pattern" + card.RarityAssetKey), true, 0, ownedMaterials);
            string costAsset = "Cost" + number;
            Material costMaterial = CreateTextureMaterial(
                Resources.Load<Texture2D>("CardAssets/Costs/" + costAsset), true, 20, ownedMaterials);
            Material illustrationMaterial = CreateTextureMaterial(card.Image, true, 10, ownedMaterials);
            Font font = Resources.Load<Font>("Fonts/CardFont");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial Unicode MS", "Arial" }, 64);

            visual.BuildFromData(card, color, attributeMaterial, cardBackMaterial,
                rarityMaterial, illustrationMaterial, costMaterial, font, useEnglish);
            if (holographic && !captureBack) visual.EnableHologram();
            if (captureBack)
                visual.PrepareFaceDown(Vector3.zero, 1f, 0f);
            else
            {
                visual.PrepareFaceUp(Vector3.zero, 1f, 0f);
                visual.transform.rotation = Quaternion.identity;
                visual.SetFaceDetailsVisible(true);
            }
            ForceCaptureMaterialsDoubleSided(cardObject);

            GameObject cameraObject = new GameObject("Card Capture Camera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            Camera captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.orthographic = true;
            captureCamera.orthographicSize = 1.72f;
            captureCamera.aspect = width / (float)height;
            captureCamera.nearClipPlane = 0.01f;
            captureCamera.farClipPlane = 30f;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            Color clearColor = transparentBackground ? new Color(0f, 0f, 0f, 0f) : backgroundColor;
            clearColor.a = transparentBackground ? 0f : 1f;
            captureCamera.backgroundColor = clearColor;
            captureCamera.allowHDR = false;
            captureCamera.allowMSAA = antiAliasing > 1;
            captureCamera.cullingMask = 1 << CaptureLayer;

            CreateDirectionalLight(root.transform, "Card Capture Key Light",
                Quaternion.Euler(45f, -30f, 0f), new Color(1f, 0.86f, 0.72f), 1.25f);
            CreateDirectionalLight(root.transform, "Card Capture Fill Light",
                Quaternion.Euler(20f, 150f, 0f), new Color(0.34f, 0.50f, 1f), 0.7f);
            SetLayerRecursively(root, CaptureLayer);

            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                antiAliasing = antiAliasing,
                filterMode = FilterMode.Bilinear,
                name = "Card Capture " + width + "x" + height
            };
            if (!renderTexture.Create()) throw new InvalidOperationException("RenderTexture를 생성하지 못했습니다.");
            captureCamera.targetTexture = renderTexture;
            captureCamera.Render();

            RenderTexture.active = renderTexture;
            capturedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            capturedTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            capturedTexture.Apply(false, false);
            return capturedTexture.EncodeToPNG();
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (renderTexture != null)
            {
                renderTexture.Release();
                DestroyImmediate(renderTexture);
            }
            if (capturedTexture != null) DestroyImmediate(capturedTexture);
            if (root != null)
            {
                MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    Mesh mesh = filters[i].sharedMesh;
                    if (mesh != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh)))
                        DestroyImmediate(mesh);
                }
                DestroyImmediate(root);
            }
            for (int i = 0; i < ownedMaterials.Count; i++)
                if (ownedMaterials[i] != null) DestroyImmediate(ownedMaterials[i]);
        }
    }

    private static Material CreateTextureMaterial(Texture texture, bool transparent, int queueOffset,
        List<Material> ownedMaterials)
    {
        if (texture == null) return null;
        Shader shader = Shader.Find(transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find(transparent ? "Unlit/Transparent" : "Standard");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        if (shader == null) throw new InvalidOperationException("카드 캡처용 셰이더를 찾지 못했습니다.");

        Material material = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave,
            mainTexture = texture,
            color = Color.white
        };
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
            if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", 1f);
            if (material.HasProperty("_DstBlendAlpha")) material.SetFloat("_DstBlendAlpha", 10f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.renderQueue = 3000 + queueOffset;
        }
        ownedMaterials.Add(material);
        return material;
    }

    private static void ForceCaptureMaterialsDoubleSided(GameObject cardObject)
    {
        Renderer[] renderers = cardObject.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Material[] rendererMaterials = renderers[rendererIndex].sharedMaterials;
            for (int materialIndex = 0; materialIndex < rendererMaterials.Length; materialIndex++)
            {
                Material material = rendererMaterials[materialIndex];
                if (material == null) continue;
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
                if (material.HasProperty("_CullMode")) material.SetFloat("_CullMode", 0f);
                if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", 1f);
                if (material.HasProperty("_DstBlendAlpha")) material.SetFloat("_DstBlendAlpha", 10f);
            }
        }
    }

    private static void CreateDirectionalLight(Transform parent, string objectName, Quaternion rotation,
        Color color, float intensity)
    {
        GameObject lightObject = new GameObject(objectName);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.rotation = rotation;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        light.cullingMask = 1 << CaptureLayer;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        for (int i = 0; i < target.transform.childCount; i++)
            SetLayerRecursively(target.transform.GetChild(i).gameObject, layer);
    }

    private static bool IsInsideProject(string path)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(projectRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string value)
    {
        string result = value;
        foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(result) ? "Card" : result.Trim();
    }
}

[CustomEditor(typeof(CardData))]
[CanEditMultipleObjects]
public sealed class CardDataCaptureInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(8f);
        using (new EditorGUI.DisabledScope(targets.Length != 1))
        {
            if (GUILayout.Button("고화질 카드 PNG 저장...", GUILayout.Height(30f)))
                CardCaptureWindow.OpenWithCard(target as CardData);
        }
    }
}
