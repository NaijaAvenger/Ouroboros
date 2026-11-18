using UnityEngine;

namespace Ouroboros.Core
{
    /// <summary>
    /// (v0.8) Editor-only safety net. The dev scene builder fills these scene references, but references held
    /// across an AssetDatabase refresh can serialize as null; when that happens, play mode loads the dev asset by
    /// path and warns, so testing never stalls on an empty field. Compiles to no-ops in builds: fix the scene for
    /// real with Ouroboros > Setup > Rebuild Scene References.
    /// </summary>
    public static class DevAssetFallback
    {
        public const string ConfigPath = "Assets/Ouroboros/DevGameModeConfig.asset";
        public const string FeedbackPath = "Assets/Ouroboros/FeedbackLibrary.asset";
        public const string ClassRegistryPath = "Assets/Ouroboros/Classes/ClassRegistry.asset";
        public const string EquipmentRegistryPath = "Assets/Ouroboros/Equipment/EquipmentRegistry.asset";
        public const string PlayerPrefabPath = "Assets/Ouroboros/Prefabs/Player.prefab";
        public const string LootDropPrefabPath = "Assets/Ouroboros/Prefabs/LootDrop.prefab";
        public const string IDCardPrefabPath = "Assets/Ouroboros/Prefabs/IDCard.prefab";
        public const string GuardPrefabPath = "Assets/Ouroboros/Prefabs/Guard.prefab";
        public const string EliteGuardPrefabPath = "Assets/Ouroboros/Prefabs/EliteGuard.prefab";
        public const string SniperGuardPrefabPath = "Assets/Ouroboros/Prefabs/SniperGuard.prefab";

        public static T Load<T>(string path, string owner, string field) where T : Object
        {
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                Debug.LogWarning($"[Ouroboros] {owner}.{field} was empty: loaded '{path}' as an editor-only fallback. " +
                                 "Run Ouroboros > Setup > Rebuild Scene References so the scene keeps it (builds have no fallback).");
            }
            return asset;
#else
            return null;
#endif
        }

        public static Fusion.NetworkObject LoadPrefab(string path, string owner, string field)
        {
            var go = Load<GameObject>(path, owner, field);
            return go != null ? go.GetComponent<Fusion.NetworkObject>() : null;
        }
    }
}
