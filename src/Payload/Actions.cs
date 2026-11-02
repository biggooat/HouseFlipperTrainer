using System;
using UnityEngine;

namespace HFTrainer
{
    /// <summary>"Мгновенное выполнение" цели ESP согласно действию правила.</summary>
    internal static class Actions
    {
        public static bool Execute(Target t)
        {
            if (t == null || t.Comp == null || t.Rule == null) return false;
            string a = (t.Rule.Action ?? "").Trim();
            if (a.Length == 0) return false;
            var go = t.Comp.gameObject;
            try
            {
                if (a == "#disable") { go.SetActive(false); return true; }
                if (a == "#destroy") { UnityEngine.Object.Destroy(go); return true; }

                // "Метод" — на найденном компоненте; "Тип.Метод" — на другом компоненте того же объекта.
                Component target = t.Comp;
                string method = a;
                int dot = a.LastIndexOf('.');
                if (dot > 0)
                {
                    string typeName = a.Substring(0, dot);
                    method = a.Substring(dot + 1);
                    target = null;
                    foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
                        if (c != null && c.GetType().Name == typeName) { target = c; break; }
                    if (target == null) return false;
                }
                var mi = Reflect.FindMethod(target.GetType(), method);
                if (mi == null) { Log.Write("Метод " + a + " не найден у " + target.GetType().Name); return false; }
                mi.Invoke(mi.IsStatic ? null : target, null);
                return true;
            }
            catch (Exception e)
            {
                Log.Write("Действие " + a + " на " + t.Comp.GetType().Name + ": " + (e.InnerException ?? e).Message);
                return false;
            }
        }

        public static int ExecuteInRadius(float radius)
        {
            int n = 0;
            foreach (var t in Scanner.Targets.ToArray())
                if (t.Alive && t.Distance <= radius && Execute(t)) n++;
            return n;
        }

        public static int ExecuteRule(EspRule rule)
        {
            int n = 0;
            foreach (var t in Scanner.Targets.ToArray())
                if (t.Rule == rule && t.Alive && Execute(t)) n++;
            return n;
        }
    }
}
