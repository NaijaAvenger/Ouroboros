using UnityEngine;

namespace Ouroboros.Data
{
    /// <summary>
    /// Maps <see cref="Core.PlayerClassType"/> to its designer-facing <see cref="ClassData"/>.
    /// Assigned to <c>GameSessionManager</c>, which publishes it as <see cref="Active"/> on every peer so
    /// <c>NetworkPlayer</c> can apply data when it builds a class component (including for late joiners).
    /// Without a registry, classes fall back to the defaults coded in their <c>OnInitialize</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "Class Registry", menuName = "Ouroboros/Class Registry")]
    public class ClassRegistry : ScriptableObject
    {
        public static ClassRegistry Active { get; set; }

        public ClassData[] classes;

        public ClassData Get(Core.PlayerClassType type)
        {
            if (classes == null) return null;
            for (int i = 0; i < classes.Length; i++)
            {
                if (classes[i] != null && classes[i].classType == type) return classes[i];
            }
            return null;
        }
    }
}
