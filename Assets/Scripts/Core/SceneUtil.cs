using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.Core
{
    /// <summary>
    /// Version-safe scene lookups. Unity 2023+ deprecates <c>FindObjectOfType</c>; this wraps both APIs.
    /// Lookups that take an interface type scan all MonoBehaviours, so reserve them for rare events
    /// (ability casts), never per-tick loops.
    /// </summary>
    public static class SceneUtil
    {
        public static T Find<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        public static T[] FindAll<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>();
#endif
        }

        /// <summary>Finds the nearest component implementing <typeparamref name="T"/> within range, or null.</summary>
        public static T FindNearest<T>(Vector3 origin, float range) where T : class
        {
            T best = null;
            float bestSq = range * range;
            foreach (var mb in FindAll<MonoBehaviour>())
            {
                if (mb is T candidate)
                {
                    float dSq = (mb.transform.position - origin).sqrMagnitude;
                    if (dSq <= bestSq)
                    {
                        bestSq = dSq;
                        best = candidate;
                    }
                }
            }
            return best;
        }

        /// <summary>Enumerates every component implementing <typeparamref name="T"/> within range.</summary>
        public static List<T> FindAllInRadius<T>(Vector3 origin, float range) where T : class
        {
            var results = new List<T>();
            float rangeSq = range * range;
            foreach (var mb in FindAll<MonoBehaviour>())
            {
                if (mb is T candidate && (mb.transform.position - origin).sqrMagnitude <= rangeSq)
                {
                    results.Add(candidate);
                }
            }
            return results;
        }
    }
}
