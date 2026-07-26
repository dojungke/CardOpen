using UnityEngine;

namespace CardOpen.Prototype
{
    public static class PrototypeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreatePrototype()
        {
            if (Object.FindAnyObjectByType<PackOnlyPrototype>() != null) return;
            GameObject root = new GameObject("Pack Only Prototype");
            root.AddComponent<PackOnlyPrototype>();
        }
    }
}
