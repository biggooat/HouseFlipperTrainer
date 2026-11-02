using System;
using System.Collections.Generic;
using UnityEngine;

namespace HFTrainer
{
    internal sealed class Target
    {
        public Component Comp;
        public EspRule Rule;
        public Renderer[] Renderers;
        public Collider Collider;
        public float Distance;

        public bool Alive => Comp != null && Comp.gameObject.activeInHierarchy;

        public Vector3 Center
        {
            get
            {
                if (TryGetBounds(out Bounds b)) return b.center;
                return Comp.transform.position;
            }
        }

        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = default(Bounds);
            bool has = false;
            if (Renderers != null)
            {
                for (int i = 0; i < Renderers.Length; i++)
                {
                    var r = Renderers[i];
                    if (r == null || !r.enabled) continue;
                    if (!has) { bounds = r.bounds; has = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
            if (!has && Collider != null && Collider.enabled)
            {
                bounds = Collider.bounds;
                has = true;
            }
            return has;
        }
    }

    /// <summary>Периодически находит в сцене объекты, подходящие под правила ESP.</summary>
    internal static class Scanner
    {
        public static readonly List<Target> Targets = new List<Target>();
        public static readonly Dictionary<Type, int> TypeCounts = new Dictionary<Type, int>();
        public static readonly Dictionary<EspRule, int> RuleCounts = new Dictionary<EspRule, int>();
        private static readonly Dictionary<Type, EspRule> RuleCache = new Dictionary<Type, EspRule>();
        private static float _next;

        public static bool CollectTypeStats;

        public static void Invalidate()
        {
            RuleCache.Clear();
            _next = 0;
        }

        public static void Tick(Config cfg, Transform player)
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + cfg.ScanInterval;
            try { Scan(cfg, player); }
            catch (Exception e) { Log.Write("Scan: " + e); }
        }

        private static EspRule MatchType(Config cfg, Type t)
        {
            if (RuleCache.TryGetValue(t, out EspRule cached)) return cached;
            EspRule found = null;
            string name = t.Name;
            bool excluded = false;
            foreach (var ex in cfg.Excludes)
                if (ex.Length > 0 && name.IndexOf(ex, StringComparison.Ordinal) >= 0) { excluded = true; break; }
            if (!excluded)
            {
                foreach (var r in cfg.Rules)
                {
                    if (!r.Enabled || r.ByGameObjectName || r.Needle.Length == 0) continue;
                    if (name.IndexOf(r.Needle, StringComparison.OrdinalIgnoreCase) >= 0) { found = r; break; }
                }
            }
            RuleCache[t] = found;
            return found;
        }

        private static void Scan(Config cfg, Transform player)
        {
            Targets.Clear();
            RuleCounts.Clear();
            if (CollectTypeStats) TypeCounts.Clear();
            var seen = new HashSet<int>();

            var all = UnityEngine.Object.FindObjectsOfType(typeof(MonoBehaviour));
            for (int i = 0; i < all.Length; i++)
            {
                var mb = all[i] as MonoBehaviour;
                if (mb == null) continue;
                Type t = mb.GetType();
                if (CollectTypeStats && !IsEngineType(t))
                {
                    TypeCounts.TryGetValue(t, out int n);
                    TypeCounts[t] = n + 1;
                }
                if (t == typeof(TrainerBehaviour)) continue;
                EspRule rule = MatchType(cfg, t);
                if (rule != null) Add(mb, rule, seen, player);
            }

            // Правила по имени объекта (go:...) — ищем среди коллайдеров (всё, с чем можно взаимодействовать)
            bool anyGo = false;
            foreach (var r in cfg.Rules) if (r.Enabled && r.ByGameObjectName && r.Needle.Length > 0) { anyGo = true; break; }
            if (anyGo)
            {
                var cols = UnityEngine.Object.FindObjectsOfType(typeof(Collider));
                for (int i = 0; i < cols.Length; i++)
                {
                    var c = cols[i] as Collider;
                    if (c == null) continue;
                    string goName = c.gameObject.name;
                    foreach (var r in cfg.Rules)
                    {
                        if (!r.Enabled || !r.ByGameObjectName || r.Needle.Length == 0) continue;
                        if (goName.IndexOf(r.Needle, StringComparison.OrdinalIgnoreCase) >= 0) { Add(c, r, seen, player); break; }
                    }
                }
            }

            Targets.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }

        private static void Add(Component comp, EspRule rule, HashSet<int> seen, Transform player)
        {
            var go = comp.gameObject;
            if (!go.activeInHierarchy) return;
            if (player != null && comp.transform.IsChildOf(player)) return;
            if (!seen.Add(go.GetInstanceID())) return;
            var t = new Target
            {
                Comp = comp,
                Rule = rule,
                Renderers = go.GetComponentsInChildren<Renderer>(),
                Collider = go.GetComponent<Collider>()
            };
            t.Distance = player != null ? Vector3.Distance(player.position, t.Center) : 0f;
            Targets.Add(t);
            RuleCounts.TryGetValue(rule, out int n);
            RuleCounts[rule] = n + 1;
        }

        public static bool IsEngineType(Type t)
        {
            string ns = t.Namespace ?? "";
            return ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro") || ns.StartsWith("Unity.") || ns.StartsWith("HFTrainer");
        }

        public static void UpdateDistances(Transform player)
        {
            if (player == null) return;
            Vector3 p = player.position;
            for (int i = 0; i < Targets.Count; i++)
            {
                var t = Targets[i];
                if (t.Comp == null) continue;
                t.Distance = Vector3.Distance(p, t.Center);
            }
        }
    }
}
