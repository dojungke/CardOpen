using UnityEngine;
using UnityEngine.Scripting;

namespace CardOpen.Prototype
{
    [Preserve]
    public static class PrototypeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        [Preserve]
        private static void CreatePrototype()
        {
            if (Object.FindAnyObjectByType<PackOnlyPrototype>() != null) return;
            GameObject root = new GameObject("Pack Only Prototype");
            root.AddComponent<PackOnlyPrototype>();
        }
    }
}
