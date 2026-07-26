#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CardPackData)), CanEditMultipleObjects]
public sealed class CardPackDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10f);
        EditorGUILayout.HelpBox(
            "각 레어도의 목표 봉입률을 해당 레어도 카드 수로 나눠 Include Cards에 균등 배분합니다. " +
            "예: 일반 60%, 일반 카드 4장이라면 카드마다 15가 적용됩니다.",
            MessageType.Info);

        if (!GUILayout.Button("레어도 봉입률 일괄 적용", GUILayout.Height(34f))) return;

        Undo.RecordObjects(targets, "Apply Card Pack Rarity Rates");
        for (int i = 0; i < targets.Length; i++)
        {
            CardPackData pack = targets[i] as CardPackData;
            if (pack == null) continue;
            pack.ApplyRarityRatesToEntries();
            EditorUtility.SetDirty(pack);
        }
        serializedObject.Update();
    }
}
#endif