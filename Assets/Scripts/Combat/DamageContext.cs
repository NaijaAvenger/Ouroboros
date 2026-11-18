using System;
using UnityEngine;

namespace Ouroboros.Combat
{
    /// <summary>Extra facts about a hit that <see cref="IDamageable.ApplyDamage(float, Network.NetworkPlayer)"/> has no room for.</summary>
    public struct DamageInfo
    {
        /// <summary>Who or what did it when there is no attacking player: "Guard", "Sentry Turret", "Laser Grid".</summary>
        public string Source;
        /// <summary>With what: a weapon name, "Explosion", "Trap", "Fire".</summary>
        public string Means;
        public bool Headshot;
        public bool HasPoint;
        /// <summary>Where the hit landed, for damage numbers.</summary>
        public Vector3 Point;
    }

    /// <summary>
    /// A way to describe a hit without changing every damage signature (v0.14). The caller opens a scope around
    /// its ApplyDamage call; the victim reads <see cref="Current"/> while it runs:
    /// <code>using (DamageContext.Begin(means: "Trap")) target.ApplyDamage(40f, owner);</code>
    /// State authority only, single threaded, and always restored when the scope ends.
    /// </summary>
    public static class DamageContext
    {
        public static DamageInfo Current;

        public struct Scope : IDisposable
        {
            private DamageInfo previous;
            internal Scope(DamageInfo previous) { this.previous = previous; }
            public void Dispose() { Current = previous; }
        }

        /// <summary>Fields left null keep whatever an outer scope set, so "Trap" survives the explosion helper it calls.</summary>
        public static Scope Begin(string source = null, string means = null, bool headshot = false, Vector3? point = null)
        {
            var scope = new Scope(Current);
            var info = Current;
            if (source != null) info.Source = source;
            if (means != null && string.IsNullOrEmpty(info.Means)) info.Means = means;
            info.Headshot = headshot;
            info.HasPoint = point.HasValue;
            info.Point = point ?? default;
            Current = info;
            return scope;
        }
    }
}
