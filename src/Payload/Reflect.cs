using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace HFTrainer
{
    /// <summary>Ссылка на конкретное поле конкретного объекта (или статическое поле).</summary>
    internal sealed class FieldRef
    {
        public object Owner;          // null для статических
        public FieldInfo Field;
        public string Path;
        public string Edit = "";
        public bool Frozen;
        public string FrozenValue;

        public bool Valid => Field.IsStatic || (Owner != null && !(Owner is UnityEngine.Object uo && uo == null));

        public string Read()
        {
            try { var v = Field.GetValue(Owner); return v == null ? "null" : Reflect.Format(v); }
            catch (Exception e) { return "<" + e.GetType().Name + ">"; }
        }

        public bool Write(string text)
        {
            try
            {
                object v = Reflect.Convert(text, Field.FieldType);
                if (v == null && Field.FieldType.IsValueType) return false;
                Field.SetValue(Owner, v);
                return true;
            }
            catch (Exception e) { Log.Write("Write " + Path + ": " + e.Message); return false; }
        }
    }

    internal static class Reflect
    {
        public const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static List<Assembly> _gameAsms;

        /// <summary>Сборки самой игры (Assembly-CSharp и т.п.).</summary>
        public static List<Assembly> GameAssemblies
        {
            get
            {
                if (_gameAsms != null) return _gameAsms;
                _gameAsms = new List<Assembly>();
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string n = a.GetName().Name;
                    if (n.StartsWith("Assembly-CSharp") || n.StartsWith("HouseFlipper") || n.StartsWith("Empyrean") || n.StartsWith("Frozen"))
                        _gameAsms.Add(a);
                }
                return _gameAsms;
            }
        }

        public static Type[] SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e)
            {
                var l = new List<Type>();
                foreach (var t in e.Types) if (t != null) l.Add(t);
                return l.ToArray();
            }
        }

        public static bool IsEditable(Type t)
        {
            if (t.IsPrimitive || t == typeof(string) || t == typeof(decimal) || t.IsEnum) return true;
            return FindImplicitFrom(t) != null; // ObscuredInt и подобные обёртки
        }

        private static MethodInfo FindImplicitFrom(Type t)
        {
            foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (m.Name != "op_Implicit" || m.ReturnType != t) continue;
                var p = m.GetParameters();
                if (p.Length == 1 && (p[0].ParameterType.IsPrimitive || p[0].ParameterType == typeof(decimal))) return m;
            }
            return null;
        }

        public static object Convert(string text, Type t)
        {
            text = text.Trim();
            var ci = CultureInfo.InvariantCulture;
            if (t == typeof(string)) return text;
            if (t == typeof(bool))
            {
                string l = text.ToLowerInvariant();
                return l == "1" || l == "true" || l == "да" || l == "on";
            }
            if (t.IsEnum) return Enum.Parse(t, text, true);
            if (t.IsPrimitive || t == typeof(decimal))
                return System.Convert.ChangeType(double.Parse(text.Replace(',', '.'), NumberStyles.Float, ci), t, ci);
            var imp = FindImplicitFrom(t);
            if (imp != null)
            {
                Type pt = imp.GetParameters()[0].ParameterType;
                object arg = System.Convert.ChangeType(double.Parse(text.Replace(',', '.'), NumberStyles.Float, ci), pt, ci);
                return imp.Invoke(null, new[] { arg });
            }
            return null;
        }

        public static string Format(object v)
        {
            if (v is float f) return f.ToString("0.###", CultureInfo.InvariantCulture);
            if (v is double d) return d.ToString("0.###", CultureInfo.InvariantCulture);
            if (v is UnityEngine.Object uo) return uo == null ? "null(destroyed)" : uo.name + " (" + v.GetType().Name + ")";
            if (v is string s) return "\"" + s + "\"";
            if (v is ICollection c) return v.GetType().Name + " [" + c.Count + "]";
            return v.ToString();
        }

        /// <summary>Все поля в иерархии типа до UnityEngine (включая private базовых классов).</summary>
        public static IEnumerable<FieldInfo> AllInstanceFields(Type t)
        {
            for (Type cur = t; cur != null && cur != typeof(object) && cur != typeof(MonoBehaviour) && cur != typeof(Component); cur = cur.BaseType)
                foreach (var f in cur.GetFields(Inst | BindingFlags.DeclaredOnly))
                    yield return f;
        }

        public static IEnumerable<MethodInfo> ZeroArgMethods(Type t)
        {
            for (Type cur = t; cur != null && cur != typeof(MonoBehaviour) && cur != typeof(object) && cur != typeof(Component); cur = cur.BaseType)
                foreach (var m in cur.GetMethods(Inst | BindingFlags.DeclaredOnly))
                    if (m.GetParameters().Length == 0 && !m.IsSpecialName && !m.ContainsGenericParameters)
                        yield return m;
        }

        public static MethodInfo FindMethod(Type t, string name)
        {
            for (Type cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                foreach (var m in cur.GetMethods(Inst | BindingFlags.DeclaredOnly))
                    if (m.Name == name && m.GetParameters().Length == 0) return m;
            return null;
        }

        private static bool IsPlainGameObject(object v)
        {
            if (v == null) return false;
            Type t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || v is string || v is UnityEngine.Object || v is IEnumerable || v is Delegate) return false;
            string ns = t.Namespace ?? "";
            return !ns.StartsWith("System") && !ns.StartsWith("UnityEngine");
        }

        /// <summary>
        /// Поиск полей по подстроке имени: поля MonoBehaviour-ов в сцене, вложенные обычные объекты (1 уровень)
        /// и статические поля всех типов игры (+ поля объектов-синглтонов).
        /// </summary>
        public static List<FieldRef> Search(string query, int limit)
        {
            var res = new List<FieldRef>();
            if (string.IsNullOrEmpty(query)) return res;
            var visited = new HashSet<object>(new RefEq());

            Action<object, string, int> visit = null;
            visit = (obj, path, depth) =>
            {
                if (obj == null || res.Count >= limit || !visited.Add(obj)) return;
                foreach (var f in AllInstanceFields(obj.GetType()))
                {
                    if (res.Count >= limit) return;
                    if (f.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 && IsEditable(f.FieldType))
                        res.Add(new FieldRef { Owner = obj, Field = f, Path = path + "." + Clean(f.Name) });
                    else if (depth > 0 && !f.FieldType.IsPrimitive && f.FieldType != typeof(string))
                    {
                        object v;
                        try { v = f.GetValue(obj); } catch { continue; }
                        if (IsPlainGameObject(v)) visit(v, path + "." + Clean(f.Name), depth - 1);
                    }
                }
            };

            // 1) объекты сцены
            foreach (var o in UnityEngine.Object.FindObjectsOfType(typeof(MonoBehaviour)))
            {
                if (res.Count >= limit) break;
                var t = o.GetType();
                if (Scanner.IsEngineType(t)) continue;
                visit(o, t.Name, 1);
            }

            // 2) статические поля и синглтоны
            foreach (var asm in GameAssemblies)
                foreach (var t in SafeTypes(asm))
                {
                    if (res.Count >= limit) break;
                    if (t.IsGenericTypeDefinition) continue;
                    FieldInfo[] fs;
                    try { fs = t.GetFields(Stat); } catch { continue; }
                    foreach (var f in fs)
                    {
                        if (f.IsLiteral) continue;
                        if (f.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 && IsEditable(f.FieldType) && !f.IsInitOnly)
                            res.Add(new FieldRef { Owner = null, Field = f, Path = t.Name + "::" + Clean(f.Name) });
                        else if (!f.FieldType.IsPrimitive && f.FieldType != typeof(string) && !f.FieldType.IsGenericTypeDefinition)
                        {
                            object v;
                            try { v = f.GetValue(null); } catch { continue; }
                            if (v != null && (IsPlainGameObject(v) || v is MonoBehaviour))
                                visit(v, t.Name + "::" + Clean(f.Name), 1);
                        }
                    }
                }
            return res;
        }

        /// <summary>Читаемые имена auto-property: &lt;Money&gt;k__BackingField -> Money</summary>
        public static string Clean(string n)
        {
            if (n.StartsWith("<") && n.Contains(">k__BackingField")) return n.Substring(1, n.IndexOf('>') - 1);
            return n;
        }

        /// <summary>Текстовое описание объекта (поля + коллекции) для вкладки «Задания».</summary>
        public static void Describe(object obj, StringBuilder sb, int depth, string indent, HashSet<object> visited)
        {
            if (obj == null) { sb.AppendLine(indent + "null"); return; }
            if (!visited.Add(obj)) { sb.AppendLine(indent + "(уже показан)"); return; }
            foreach (var f in AllInstanceFields(obj.GetType()))
            {
                object v;
                try { v = f.GetValue(obj); } catch { continue; }
                string name = Clean(f.Name);
                if (v is IEnumerable en && !(v is string))
                {
                    int count = 0;
                    var items = new List<object>();
                    try { foreach (var it in en) { if (count < 30) items.Add(it); count++; } } catch { }
                    sb.AppendLine(indent + name + ": " + v.GetType().Name + " [" + count + "]");
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        sb.AppendLine(indent + "  [" + i + "] " + (it == null ? "null" : Format(it)));
                        if (depth > 0 && it != null && (IsPlainGameObject(it) || (it is MonoBehaviour mb && mb != null)))
                            Describe(it, sb, depth - 1, indent + "      ", visited);
                    }
                }
                else
                {
                    sb.AppendLine(indent + name + " = " + (v == null ? "null" : Format(v)));
                    if (depth > 0 && IsPlainGameObject(v)) Describe(v, sb, depth - 1, indent + "    ", visited);
                }
            }
        }

        /// <summary>Дамп всех типов игры с полями и методами — пришлите файл, чтобы добавить точные функции.</summary>
        public static string Dump()
        {
            string path = System.IO.Path.Combine(Log.Dir, "HFTrainer_dump.txt");
            using (var w = new StreamWriter(path, false, Encoding.UTF8))
            {
                foreach (var asm in GameAssemblies)
                {
                    w.WriteLine("===== " + asm.GetName().Name);
                    foreach (var t in SafeTypes(asm))
                    {
                        try
                        {
                            w.WriteLine((t.IsEnum ? "enum " : t.IsValueType ? "struct " : "class ") + t.FullName +
                                        (t.BaseType != null ? " : " + t.BaseType.Name : ""));
                            if (t.IsEnum) { w.WriteLine("    " + string.Join(", ", Enum.GetNames(t))); continue; }
                            foreach (var f in t.GetFields(Inst | BindingFlags.Static | BindingFlags.DeclaredOnly))
                                w.WriteLine("    " + (f.IsStatic ? "static " : "") + f.FieldType.Name + " " + f.Name);
                            foreach (var m in t.GetMethods(Inst | BindingFlags.Static | BindingFlags.DeclaredOnly))
                            {
                                var ps = m.GetParameters();
                                var pn = new string[ps.Length];
                                for (int i = 0; i < ps.Length; i++) pn[i] = ps[i].ParameterType.Name + " " + ps[i].Name;
                                w.WriteLine("    " + (m.IsStatic ? "static " : "") + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", pn) + ")");
                            }
                        }
                        catch (Exception e) { w.WriteLine("    <" + e.Message + ">"); }
                    }
                }
            }
            return path;
        }

        public sealed class RefEq : IEqualityComparer<object>
        {
            bool IEqualityComparer<object>.Equals(object a, object b) => ReferenceEquals(a, b);
            int IEqualityComparer<object>.GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
