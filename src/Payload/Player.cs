using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HFTrainer
{
    /// <summary>Функции игрока, не зависящие от внутренних классов игры.</summary>
    internal static class Player
    {
        private static Transform _root;
        private static CharacterController _cc;
        private static float _nextFind;

        public static bool Fly;
        private static bool _flyActive;
        private static bool _ccWasEnabled;
        private static Vector3 _flyPos;

        private static readonly List<KeyValuePair<FieldRef, float>> SpeedFields = new List<KeyValuePair<FieldRef, float>>();
        private static float _appliedMult = 1f;

        public static readonly Vector3?[] Slots = new Vector3?[3];

        public static Camera Cam => Camera.main;

        public static Transform Root
        {
            get
            {
                if (_root == null || Time.unscaledTime > _nextFind) Find();
                return _root;
            }
        }

        private static void Find()
        {
            _nextFind = Time.unscaledTime + 3f;
            var prev = _root;
            _cc = UnityEngine.Object.FindObjectOfType(typeof(CharacterController)) as CharacterController;
            if (_cc != null) _root = _cc.transform;
            else if (Cam != null) _root = Cam.transform.root;
            else _root = null;
            if (prev != _root)
            {
                SpeedFields.Clear();
                _appliedMult = 1f;
                if (_root != null) Log.Write("Игрок: " + _root.name + (_cc != null ? " (CharacterController)" : " (корень камеры)"));
            }
        }

        public static string Describe()
        {
            var r = Root;
            if (r == null) return "игрок не найден";
            Vector3 p = r.position;
            return r.name + "  (" + p.x.ToString("0.0") + "; " + p.y.ToString("0.0") + "; " + p.z.ToString("0.0") + ")";
        }

        // ---------------- телепорт ----------------
        public static void TeleportTo(Vector3 pos)
        {
            var r = Root;
            if (r == null) return;
            bool en = _cc != null && _cc.enabled;
            if (en) _cc.enabled = false;
            r.position = pos;
            _flyPos = pos;
            if (en) _cc.enabled = true;
        }

        /// <summary>Телепорт к объекту: встаём на 1.5 м перед ним (со стороны игрока), на уровне пола.</summary>
        public static void TeleportNear(Target t)
        {
            var r = Root;
            if (r == null || t == null || t.Comp == null) return;
            Vector3 c = t.Center;
            Vector3 dir = r.position - c; dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
            Vector3 dest = c + dir.normalized * 1.5f;
            dest.y = Mathf.Max(c.y - 0.5f, r.position.y - 0.2f);
            if (Mathf.Abs(c.y - r.position.y) > 2.5f) dest.y = c.y; // другой этаж
            TeleportTo(dest);
        }

        // ---------------- полёт ----------------
        public static void UpdateFly(Func<KeyCode, bool> key, float speed)
        {
            var r = Root;
            if (r == null) return;
            if (Fly && !_flyActive)
            {
                _flyActive = true;
                _flyPos = r.position;
                _ccWasEnabled = _cc != null && _cc.enabled;
                if (_cc != null) _cc.enabled = false;
            }
            else if (!Fly && _flyActive)
            {
                _flyActive = false;
                if (_cc != null) _cc.enabled = _ccWasEnabled;
                return;
            }
            if (!_flyActive) return;

            Transform view = Cam != null ? Cam.transform : r;
            Vector3 move = Vector3.zero;
            if (key(KeyCode.W)) move += view.forward;
            if (key(KeyCode.S)) move -= view.forward;
            if (key(KeyCode.D)) move += view.right;
            if (key(KeyCode.A)) move -= view.right;
            if (key(KeyCode.Space) || key(KeyCode.E)) move += Vector3.up;
            if (key(KeyCode.LeftControl) || key(KeyCode.Q)) move -= Vector3.up;
            float s = speed * (key(KeyCode.LeftShift) ? 3f : 1f);
            _flyPos += move.normalized * s * Time.unscaledDeltaTime;
        }

        /// <summary>Вызывается в LateUpdate, чтобы перебить позицию, выставленную контроллером игры.</summary>
        public static void LateApplyFly()
        {
            if (_flyActive && Root != null) Root.position = _flyPos;
        }

        // ---------------- скорость ----------------
        private static readonly string[] SpeedWords = { "speed", "velocity" };
        private static readonly string[] SpeedSkip = { "mouse", "look", "rotat", "sensitiv", "anim", "turn", "camera", "bob", "fov", "zoom" };

        /// <summary>Находит float-поля "*speed*" в компонентах игрока (ходьба/бег) и умножает их.</summary>
        public static int ApplySpeed(float mult)
        {
            var r = Root;
            if (r == null) return 0;
            if (SpeedFields.Count == 0)
            {
                foreach (var mb in r.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || Scanner.IsEngineType(mb.GetType())) continue;
                    foreach (var f in Reflect.AllInstanceFields(mb.GetType()))
                    {
                        if (f.FieldType != typeof(float)) continue;
                        string n = f.Name.ToLowerInvariant();
                        bool ok = false;
                        foreach (var w in SpeedWords) if (n.Contains(w)) ok = true;
                        foreach (var w in SpeedSkip) if (n.Contains(w)) ok = false;
                        if (!ok) continue;
                        float orig = (float)f.GetValue(mb);
                        if (orig <= 0f) continue;
                        SpeedFields.Add(new KeyValuePair<FieldRef, float>(
                            new FieldRef { Owner = mb, Field = f, Path = mb.GetType().Name + "." + Reflect.Clean(f.Name) }, orig));
                    }
                }
                Log.Write("Поля скорости: " + SpeedFields.Count);
            }
            foreach (var kv in SpeedFields)
                if (kv.Key.Valid) kv.Key.Field.SetValue(kv.Key.Owner, kv.Value * mult);
            _appliedMult = mult;
            return SpeedFields.Count;
        }

        public static IEnumerable<FieldRef> SpeedFieldRefs
        {
            get { foreach (var kv in SpeedFields) yield return kv.Key; }
        }

        public static float AppliedSpeed => _appliedMult;

        public static void Restore()
        {
            if (_appliedMult != 1f) ApplySpeed(1f);
            Fly = false;
            if (_flyActive)
            {
                _flyActive = false;
                if (_cc != null) _cc.enabled = _ccWasEnabled;
            }
        }
    }
}
