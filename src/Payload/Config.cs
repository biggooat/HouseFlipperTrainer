using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace HFTrainer
{
    internal sealed class EspRule
    {
        public bool Enabled = true;
        /// <summary>Подстрока имени типа компонента. С префиксом "go:" — подстрока имени GameObject.</summary>
        public string Pattern = "";
        public string Label = "";
        public Color Color = Color.yellow;
        /// <summary>
        /// Действие "выполнить мгновенно": имя метода без параметров на найденном компоненте
        /// (например Clean), либо #disable (скрыть объект) / #destroy (удалить объект). Пусто — нет действия.
        /// </summary>
        public string Action = "";

        public bool ByGameObjectName => Pattern.StartsWith("go:", StringComparison.OrdinalIgnoreCase);
        public string Needle => ByGameObjectName ? Pattern.Substring(3) : Pattern;
    }

    internal sealed class Config
    {
        public readonly List<EspRule> Rules = new List<EspRule>();
        public readonly List<string> Excludes = new List<string>();

        // ESP
        public bool EspEnabled = true;
        public bool EspBoxes = true;
        public bool EspTracers = false;
        public bool EspDistance = true;
        public bool EspCounterHud = true;
        public float EspMaxDistance = 60f;
        public float ScanInterval = 1.0f;

        // Действия
        public float ActionRadius = 6f;
        public bool AutoAction = false;

        // Игрок
        public float FlySpeed = 8f;
        public float SpeedMultiplier = 1f;
        public float Fov = 0f; // 0 — не трогать

        // Мир
        public float TimeScale = 1f;

        public static string FilePath => Path.Combine(Log.Dir, "HFTrainer.cfg");

        public static Config CreateDefault()
        {
            var c = new Config();
            // Это предположения по названиям классов House Flipper. Точные имена смотрите во вкладке «Сцена»
            // (стоя в доме с заданием) и добавляйте их кнопкой «+ESP».
            c.Rules.Add(new EspRule { Pattern = "Dirt", Label = "Уборка", Color = new Color(1f, 0.85f, 0.2f) });
            c.Rules.Add(new EspRule { Pattern = "Stain", Label = "Пятно", Color = new Color(1f, 0.7f, 0.3f) });
            c.Rules.Add(new EspRule { Pattern = "Trash", Label = "Мусор", Color = new Color(1f, 0.5f, 0.1f) });
            c.Rules.Add(new EspRule { Pattern = "Garbage", Label = "Мусор", Color = new Color(1f, 0.5f, 0.1f) });
            c.Rules.Add(new EspRule { Pattern = "Cobweb", Label = "Паутина", Color = new Color(0.8f, 0.8f, 0.8f) });
            c.Rules.Add(new EspRule { Pattern = "Broken", Label = "Ремонт", Color = new Color(1f, 0.25f, 0.25f) });
            c.Rules.Add(new EspRule { Pattern = "Repair", Label = "Ремонт", Color = new Color(1f, 0.25f, 0.25f) });
            c.Rules.Add(new EspRule { Pattern = "Mount", Label = "Монтаж", Color = new Color(0.3f, 0.8f, 1f), Enabled = false });
            c.Rules.Add(new EspRule { Pattern = "Paint", Label = "Покраска", Color = new Color(0.4f, 1f, 0.4f), Enabled = false });
            c.Rules.Add(new EspRule { Pattern = "Tile", Label = "Плитка", Color = new Color(0.6f, 0.6f, 1f), Enabled = false });
            c.Rules.Add(new EspRule { Pattern = "Window", Label = "Окно", Color = new Color(0.5f, 0.9f, 1f), Enabled = false });
            c.Excludes.AddRange(new[] { "Manager", "Controller", "System", "UI", "Settings", "Spawner", "Handler", "Menu", "Database" });
            // Исключения сравниваются с учётом регистра: "UI" отсечёт DirtUI, но не Building.
            return c;
        }

        public static Config LoadOrDefault()
        {
            try
            {
                if (!File.Exists(FilePath)) return CreateDefault();
                var c = new Config();
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var p = line.Split('|');
                    switch (p[0])
                    {
                        case "rule" when p.Length >= 5:
                            c.Rules.Add(new EspRule
                            {
                                Enabled = p[1] == "1",
                                Pattern = p[2],
                                Label = p[3],
                                Color = ParseColor(p[4]),
                                Action = p.Length > 5 ? p[5] : ""
                            });
                            break;
                        case "exclude" when p.Length >= 2:
                            c.Excludes.Add(p[1]);
                            break;
                        case "set" when p.Length >= 3:
                            c.Set(p[1], p[2]);
                            break;
                    }
                }
                return c;
            }
            catch (Exception e)
            {
                Log.Write("Ошибка чтения конфига: " + e.Message);
                return CreateDefault();
            }
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# House Flipper Trainer config");
                sb.AppendLine("# rule|вкл|шаблон (тип или go:имя_объекта)|подпись|r,g,b|действие (Метод / #disable / #destroy)");
                foreach (var r in Rules)
                    sb.AppendLine("rule|" + (r.Enabled ? "1" : "0") + "|" + r.Pattern + "|" + r.Label + "|" + FormatColor(r.Color) + "|" + r.Action);
                foreach (var e in Excludes) sb.AppendLine("exclude|" + e);
                foreach (var kv in Settings()) sb.AppendLine("set|" + kv.Key + "|" + kv.Value);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e) { Log.Write("Ошибка сохранения конфига: " + e.Message); }
        }

        private IEnumerable<KeyValuePair<string, string>> Settings()
        {
            yield return Kv("EspEnabled", EspEnabled);
            yield return Kv("EspBoxes", EspBoxes);
            yield return Kv("EspTracers", EspTracers);
            yield return Kv("EspDistance", EspDistance);
            yield return Kv("EspCounterHud", EspCounterHud);
            yield return Kv("EspMaxDistance", EspMaxDistance);
            yield return Kv("ScanInterval", ScanInterval);
            yield return Kv("ActionRadius", ActionRadius);
            yield return Kv("FlySpeed", FlySpeed);
        }

        private void Set(string k, string v)
        {
            switch (k)
            {
                case "EspEnabled": EspEnabled = v == "1"; break;
                case "EspBoxes": EspBoxes = v == "1"; break;
                case "EspTracers": EspTracers = v == "1"; break;
                case "EspDistance": EspDistance = v == "1"; break;
                case "EspCounterHud": EspCounterHud = v == "1"; break;
                case "EspMaxDistance": EspMaxDistance = F(v, EspMaxDistance); break;
                case "ScanInterval": ScanInterval = Mathf.Max(0.2f, F(v, ScanInterval)); break;
                case "ActionRadius": ActionRadius = F(v, ActionRadius); break;
                case "FlySpeed": FlySpeed = F(v, FlySpeed); break;
            }
        }

        private static KeyValuePair<string, string> Kv(string k, bool v) => new KeyValuePair<string, string>(k, v ? "1" : "0");
        private static KeyValuePair<string, string> Kv(string k, float v) => new KeyValuePair<string, string>(k, v.ToString(CultureInfo.InvariantCulture));
        private static float F(string s, float def) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : def;

        private static Color ParseColor(string s)
        {
            var p = s.Split(',');
            if (p.Length < 3) return Color.yellow;
            return new Color(F(p[0], 1), F(p[1], 1), F(p[2], 0));
        }

        private static string FormatColor(Color c) =>
            c.r.ToString("0.##", CultureInfo.InvariantCulture) + "," +
            c.g.ToString("0.##", CultureInfo.InvariantCulture) + "," +
            c.b.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
