using System.Collections;
using UnityEngine;

namespace CardOpen.Prototype
{
    [DefaultExecutionOrder(-10000)]
    public sealed class InspectionReturnController : MonoBehaviour
    {
        private static readonly Rect PackTearZone = new Rect(410f, 0f, 460f, 320f);
        private static readonly Rect CardGestureZone = new Rect(470f, 105f, 340f, 505f);
        private Transform inspectedTarget;
        private Coroutine returnRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateController()
        {
            if (FindAnyObjectByType<InspectionReturnController>() != null) return;
            new GameObject("Inspection Return Controller").AddComponent<InspectionReturnController>();
        }

        private void OnGUI()
        {
            const float width = 1280f;
            const float height = 720f;
            float scale = Mathf.Min(Screen.width / width, Screen.height / height);
            float offsetX = (Screen.width - width * scale) * 0.5f;
            float offsetY = (Screen.height - height * scale) * 0.5f;
            Vector2 raw = Event.current.mousePosition;
            Vector2 point = new Vector2((raw.x - offsetX) / scale, (raw.y - offsetY) / scale);

            if (Event.current.type == EventType.MouseDown)
            {
                StopReturning();
                Transform current = FindCurrentTarget(out bool isPack);
                Rect gestureZone = isPack ? PackTearZone : CardGestureZone;
                inspectedTarget = current != null && !gestureZone.Contains(point) ? current : null;
            }
            else if (Event.current.type == EventType.MouseUp && inspectedTarget != null)
            {
                Transform target = inspectedTarget;
                inspectedTarget = null;
                returnRoutine = StartCoroutine(ReturnToRest(target, GetRestRotation(target)));
            }
        }

        private static Transform FindCurrentTarget(out bool isPack)
        {
            PackVisual pack = FindAnyObjectByType<PackVisual>();
            if (pack != null && pack.gameObject.activeInHierarchy) { isPack = true; return pack.transform; }
            isPack = false;
            CardStackVisual stack = FindAnyObjectByType<CardStackVisual>();
            return stack != null && stack.gameObject.activeInHierarchy ? stack.transform : null;
        }

        private static Quaternion GetRestRotation(Transform target)
        {
            if (target.GetComponent<PackVisual>() != null) return PackTearVisual.DefaultRotation;
            return Quaternion.identity;
        }

        private IEnumerator ReturnToRest(Transform target, Quaternion restRotation)
        {
            Quaternion start = target.rotation;
            const float duration = 0.85f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (target == null) yield break;
                target.rotation = Quaternion.Slerp(start, restRotation, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            if (target != null) target.rotation = restRotation;
            returnRoutine = null;
        }

        private void StopReturning()
        {
            if (returnRoutine == null) return;
            StopCoroutine(returnRoutine);
            returnRoutine = null;
        }
    }
}
