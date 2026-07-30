#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(CardPackPoolData))]
public sealed class CardPackPoolDataEditor : Editor
{
    private SerializedProperty packs;
    private ReorderableList packList;

    private void OnEnable()
    {
        packs = serializedObject.FindProperty("Packs");
        packList = new ReorderableList(serializedObject, packs, true, true, true, true)
        {
            drawHeaderCallback = DrawHeader,
            drawElementCallback = DrawElement,
            elementHeight = EditorGUIUtility.singleLineHeight + 5f,
            onAddCallback = AddEmptyEntry
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.LabelField("Available Packs & Appearance Rates", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Enable packs that may appear. Weights are relative and do not need to total 100.", MessageType.Info);
        packList.DoLayoutList();
        serializedObject.ApplyModifiedProperties();

        CardPackPoolData pool = (CardPackPoolData)target;
        float total = pool.TotalWeight;
        EditorGUILayout.LabelField("Active weight total", total.ToString("0.##"));
        if (total <= 0f)
            EditorGUILayout.HelpBox("No pack can appear. Enable a valid pack with a weight above 0.", MessageType.Error);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add All Pack Assets")) AddAllPacks(pool);
            if (GUILayout.Button("Clean Missing / Duplicates")) CleanEntries(pool);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Equal Weights")) SetEqualWeights(pool);
            if (GUILayout.Button("Normalize Weights To 100")) NormalizeWeights(pool);
        }
    }

    private void DrawHeader(Rect rect)
    {
        GetColumns(rect, out Rect enabled, out Rect pack, out Rect weight, out Rect probability);
        EditorGUI.LabelField(enabled, "On");
        EditorGUI.LabelField(pack, "Pack");
        EditorGUI.LabelField(weight, "Weight");
        EditorGUI.LabelField(probability, "Chance");
    }

    private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        SerializedProperty entry = packs.GetArrayElementAtIndex(index);
        SerializedProperty enabled = entry.FindPropertyRelative("Enabled");
        SerializedProperty pack = entry.FindPropertyRelative("Pack");
        SerializedProperty weight = entry.FindPropertyRelative("Weight");
        rect.y += 2f;
        rect.height = EditorGUIUtility.singleLineHeight;
        GetColumns(rect, out Rect enabledRect, out Rect packRect, out Rect weightRect, out Rect probabilityRect);
        EditorGUI.PropertyField(enabledRect, enabled, GUIContent.none);
        EditorGUI.PropertyField(packRect, pack, GUIContent.none);
        EditorGUI.PropertyField(weightRect, weight, GUIContent.none);
        float total = GetSerializedTotalWeight();
        bool valid = enabled.boolValue && pack.objectReferenceValue != null && weight.floatValue > 0f;
        EditorGUI.LabelField(probabilityRect, (valid && total > 0f ? weight.floatValue / total : 0f).ToString("P1"));
    }

    private float GetSerializedTotalWeight()
    {
        float total = 0f;
        for (int i = 0; i < packs.arraySize; i++)
        {
            SerializedProperty entry = packs.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("Enabled").boolValue && entry.FindPropertyRelative("Pack").objectReferenceValue != null)
                total += Mathf.Max(0f, entry.FindPropertyRelative("Weight").floatValue);
        }
        return total;
    }

    private static void GetColumns(Rect rect, out Rect enabled, out Rect pack, out Rect weight, out Rect probability)
    {
        const float gap = 4f;
        enabled = new Rect(rect.x, rect.y, 28f, rect.height);
        probability = new Rect(rect.xMax - 58f, rect.y, 58f, rect.height);
        weight = new Rect(probability.x - gap - 68f, rect.y, 68f, rect.height);
        pack = new Rect(enabled.xMax + gap, rect.y, weight.x - enabled.xMax - gap * 2f, rect.height);
    }

    private void AddEmptyEntry(ReorderableList list)
    {
        int index = packs.arraySize;
        packs.InsertArrayElementAtIndex(index);
        SerializedProperty entry = packs.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("Enabled").boolValue = true;
        entry.FindPropertyRelative("Pack").objectReferenceValue = null;
        entry.FindPropertyRelative("Weight").floatValue = 1f;
        serializedObject.ApplyModifiedProperties();
        list.index = index;
    }

    private static void AddAllPacks(CardPackPoolData pool)
    {
        Mutate(pool, "Add All Card Packs", () =>
        {
            if (pool.Packs == null) pool.Packs = new List<CardPackPoolEntry>();
            HashSet<CardPackData> existing = new HashSet<CardPackData>();
            for (int i = 0; i < pool.Packs.Count; i++)
                if (pool.Packs[i] != null && pool.Packs[i].Pack != null) existing.Add(pool.Packs[i].Pack);
            string[] guids = AssetDatabase.FindAssets("t:CardPackData", new[] { "Assets/CardPackPrototype/Resources/CardPacks" });
            Array.Sort(guids, (left, right) => string.Compare(AssetDatabase.GUIDToAssetPath(left), AssetDatabase.GUIDToAssetPath(right), StringComparison.Ordinal));
            for (int i = 0; i < guids.Length; i++)
            {
                CardPackData pack = AssetDatabase.LoadAssetAtPath<CardPackData>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (pack != null && existing.Add(pack))
                    pool.Packs.Add(new CardPackPoolEntry { Enabled = true, Pack = pack, Weight = 1f });
            }
        });
    }

    private static void CleanEntries(CardPackPoolData pool)
    {
        Mutate(pool, "Clean Card Pack Pool", () =>
        {
            if (pool.Packs == null) return;
            HashSet<CardPackData> found = new HashSet<CardPackData>();
            for (int i = 0; i < pool.Packs.Count; i++)
            {
                CardPackPoolEntry entry = pool.Packs[i];
                if (entry == null || entry.Pack == null || !found.Add(entry.Pack)) pool.Packs.RemoveAt(i--);
            }
        });
    }

    private static void SetEqualWeights(CardPackPoolData pool)
    {
        Mutate(pool, "Set Equal Card Pack Weights", () =>
        {
            if (pool.Packs == null) return;
            for (int i = 0; i < pool.Packs.Count; i++)
                if (pool.Packs[i] != null && pool.Packs[i].Enabled && pool.Packs[i].Pack != null) pool.Packs[i].Weight = 1f;
        });
    }

    private static void NormalizeWeights(CardPackPoolData pool)
    {
        Mutate(pool, "Normalize Card Pack Weights", () =>
        {
            float total = pool.TotalWeight;
            if (total <= 0f || pool.Packs == null) return;
            float scale = 100f / total;
            for (int i = 0; i < pool.Packs.Count; i++)
            {
                CardPackPoolEntry entry = pool.Packs[i];
                if (entry != null && entry.Enabled && entry.Pack != null && entry.Weight > 0f) entry.Weight *= scale;
            }
        });
    }

    [MenuItem("Card Pack/Open Pack Pool")]
    private static void OpenPackPool()
    {
        const string path = "Assets/CardPackPrototype/Resources/CardPacks/CardPackPool.asset";
        CardPackPoolData pool = AssetDatabase.LoadAssetAtPath<CardPackPoolData>(path);
        if (pool == null)
        {
            AssetDatabase.Refresh();
            pool = AssetDatabase.LoadAssetAtPath<CardPackPoolData>(path);
        }
        if (pool == null)
        {
            Debug.LogError("CardPackPool asset could not be loaded at " + path);
            return;
        }
        Selection.activeObject = pool;
        EditorGUIUtility.PingObject(pool);
    }
    private static void Mutate(CardPackPoolData pool, string undoName, Action action)
    {
        Undo.RecordObject(pool, undoName);
        action();
        EditorUtility.SetDirty(pool);
    }
}
#endif