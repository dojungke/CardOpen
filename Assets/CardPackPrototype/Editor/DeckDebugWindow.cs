#if UNITY_EDITOR
using CardOpen.Prototype;
using UnityEditor;
using UnityEngine;

namespace CardOpen.Editor
{
    public sealed class DeckDebugWindow : EditorWindow
    {
        private CardData selectedCard;
        private int cardNumber = 1;
        private CardColor cardColor = CardColor.Green;
        private string feedback;

        [MenuItem("CardOpen/Debug/덱에 카드 즉시 추가")]
        private static void Open()
        {
            GetWindow<DeckDebugWindow>("덱 카드 추가");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("덱에 카드 즉시 추가", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("플레이 중인 CardOpen 게임의 덱에 카드를 추가합니다. 기존 병합, 장착, 조합 규칙을 적용합니다.", MessageType.Info);

            PackOnlyPrototype game = FindAnyObjectByType<PackOnlyPrototype>();
            if (game == null)
            {
                EditorGUILayout.HelpBox("플레이 모드에서 CardOpen 게임을 실행한 뒤 사용하세요.", MessageType.Warning);
                return;
            }

            selectedCard = (CardData)EditorGUILayout.ObjectField("카드", selectedCard,
                typeof(CardData), false);
            cardNumber = EditorGUILayout.IntSlider("카드 숫자", cardNumber, 1, 6);
            cardColor = (CardColor)EditorGUILayout.EnumPopup("카드 색상", cardColor);

            using (new EditorGUI.DisabledScope(selectedCard == null))
            {
                if (GUILayout.Button("덱에 즉시 추가", GUILayout.Height(34f)))
                {
                    if (game.DebugAddCardToDeck(selectedCard, cardNumber, cardColor))
                        feedback = selectedCard.GetLocalizedName(false) + " 카드를 덱에 추가했습니다.";
                    else
                        feedback = "덱이 가득 찼거나 카드를 추가할 수 없습니다.";
                }
            }

            if (!string.IsNullOrEmpty(feedback))
                EditorGUILayout.HelpBox(feedback, MessageType.None);
        }
    }
}
#endif