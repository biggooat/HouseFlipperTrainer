using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace HFTrainer
{
    internal sealed class TrainerBehaviour : MonoBehaviour
    {
        private Config _cfg;
        private bool _menu;
        private Rect _win = new Rect(60, 60, 600, 640);
        private int _tab;
        private static readonly string[] Tabs = { "ESP", "Цели", "Игрок", "Мир", "Поиск значений", "Сцена", "Задания" };
        private readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();
        private Vector2 _scroll, _scroll2;
        private string _status = "";
        private float _statusUntil;

        // курсор
        private CursorLockMode _prevLock;
        private bool _prevVisible;

        // поиск
        private string _query = "money";
        private List<FieldRef> _results = new List<FieldRef>();
        private readonly List<FieldRef> _frozen = new List<FieldRef>();
        private float _nextFreeze, _nextAuto;

        // сцена
        private string _sceneFilter = "";
        private Type _inspectType;
        private string _inspectText = "";
        private List<KeyValuePair<Type, int>> _sceneTypes = new List<KeyValuePair<Type, int>>();
        private float _nextSceneRefresh;

        // задания
        private List<KeyValuePair<string, object>> _taskObjs = new List<KeyValuePair<string, object>>();
        private int _taskSel = -1;
        private string[] _taskLines = new string[0];
        private static readonly string[] TaskWords = { "Task", "Objective", "Quest", "Job", "Mission", "Order", "Goal", "Request", "Contract", "Todo" };

        // мир
        private float _timeScale = 1f;
        private bool _timeScaleTouched;

        private static readonly Color[] Palette =
        {
            new Color(1f, 0.85f, 0.2f), new Color(1f, 0.5f, 0.1f), new Color(1f, 0.25f, 0.25f), new Color(0.4f, 1f, 0.4f),
            new Color(0.3f, 0.8f, 1f), new Color(0.75f, 0.5f, 1f), new Color(1f, 0.45f, 0.8f), new Color(0.9f, 0.9f, 0.9f)
        };

        private void Awake()
        {
            _cfg = Config.LoadOrDefault();
            Log.Write("TrainerBehaviour.Awake, правил ESP: " + _cfg.Rules.Count);
            Flash("Трейнер загружен — Insert / F1 открывает меню", 6f);
        }

        private void OnDestroy()
        {
            try
            {
                Player.Restore();
                if (_timeScaleTouched) Time.timeScale = 1f;
                if (_menu) { Cursor.lockState = _prevLock; Cursor.visible = _prevVisible; }
                _cfg.Save();
            }
            catch (Exception e) { Log.Write("OnDestroy: " + e); }
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus) _held.Clear();
        }

        private void Flash(string s, float sec = 3f)
        {
            _status = s;
            _statusUntil = Time.unscaledTime + sec;
        }

        private bool Key(KeyCode k) => _held.Contains(k);

        // ======================= цикл =======================
        private void Update()
        {
            try
            {
                Scanner.CollectTypeStats = _menu && _tab == 5;
                Transform player = Player.Root;
                Scanner.Tick(_cfg, player);
                Scanner.UpdateDistances(player);
                Player.UpdateFly(Key, _cfg.FlySpeed);

                if (_cfg.Fov > 1f && Player.Cam != null) Player.Cam.fieldOfView = _cfg.Fov;

                if (Time.unscaledTime > _nextFreeze)
                {
                    _nextFreeze = Time.unscaledTime + 0.1f;
                    _frozen.RemoveAll(f => !f.Valid || !f.Frozen);
                    foreach (var f in _frozen) f.Write(f.FrozenValue);
                }

                if (_cfg.AutoAction && Time.unscaledTime > _nextAuto)
                {
                    _nextAuto = Time.unscaledTime + 0.25f;
                    Actions.ExecuteInRadius(_cfg.ActionRadius);
                }

                if (_menu) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            }
            catch (Exception e) { Log.Write("Update: " + e); }
        }

        private void LateUpdate()
        {
            Player.LateApplyFly();
            if (_menu) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        private void OnHotkey(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Insert:
                case KeyCode.F1: ToggleMenu(); break;
                case KeyCode.F2: _cfg.EspEnabled = !_cfg.EspEnabled; Flash("ESP: " + OnOff(_cfg.EspEnabled)); break;
                case KeyCode.F3: Player.Fly = !Player.Fly; Flash("Полёт: " + OnOff(Player.Fly)); break;
                case KeyCode.F4:
                    int n = Actions.ExecuteInRadius(_cfg.ActionRadius);
                    Flash(n > 0 ? "Выполнено целей: " + n : "Рядом нет целей с настроенным действием (вкладка ESP → «Действие»)");
                    break;
                case KeyCode.F5: TeleportNearest(); break;
                case KeyCode.F6: if (Player.Root != null) { Player.Slots[0] = Player.Root.position; Flash("Позиция сохранена"); } break;
                case KeyCode.F7: if (Player.Slots[0].HasValue) { Player.TeleportTo(Player.Slots[0].Value); Flash("Позиция загружена"); } break;
                case KeyCode.F8: SetTimeScale(Mathf.Approximately(Time.timeScale, 1f) ? 5f : 1f); Flash("Скорость игры x" + Time.timeScale); break;
                case KeyCode.End: Loader.Unload(); break;
            }
        }

        private static string OnOff(bool b) => b ? "ВКЛ" : "ВЫКЛ";

        private void ToggleMenu()
        {
            _menu = !_menu;
            if (_menu)
            {
                _prevLock = Cursor.lockState;
                _prevVisible = Cursor.visible;
            }
            else
            {
                Cursor.lockState = _prevLock;
                Cursor.visible = _prevVisible;
                _cfg.Save();
            }
        }

        private void TeleportNearest()
        {
            var t = Scanner.Targets.FirstOrDefault(x => x.Alive && x.Distance > 2f);
            if (t == null) { Flash("Целей нет"); return; }
            Player.TeleportNear(t);
            Flash("Телепорт: " + t.Rule.Label + " (" + t.Distance.ToString("0") + "м)");
        }

        private void SetTimeScale(float v)
        {
            _timeScale = v;
            Time.timeScale = v;
            _timeScaleTouched = true;
        }

        // ======================= GUI =======================
        private void OnGUI()
        {
            Event e = Event.current;
            if (e.isKey && e.keyCode != KeyCode.None)
            {
                if (e.type == EventType.KeyDown)
                {
                    // Add() == false — это автоповтор зажатой клавиши, его игнорируем
                    if (_held.Add(e.keyCode) && GUIUtility.keyboardControl == 0) OnHotkey(e.keyCode);
                }
                else if (e.type == EventType.KeyUp) _held.Remove(e.keyCode);
            }
            // Insert / F1 закрывают меню даже когда фокус в текстовом поле
            if (e.type == EventType.KeyDown && _menu && GUIUtility.keyboardControl != 0 && (e.keyCode == KeyCode.Insert || e.keyCode == KeyCode.F1))
            {
                GUIUtility.keyboardControl = 0;
                ToggleMenu();
            }

            try
            {
                if (e.type == EventType.Repaint)
                {
                    if (_cfg.EspEnabled) Esp.Render(_cfg);
                    if (Time.unscaledTime < _statusUntil && _status.Length > 0)
                        Draw.Tag(Screen.width / 2f, 40f, _status, new Color(0.3f, 0.8f, 1f));
                    if (Player.Fly) Draw.Tag(Screen.width / 2f, Screen.height - 60f, "ПОЛЁТ  WASD · Space/Ctrl · Shift", new Color(0.4f, 1f, 0.4f));
                }
                if (_menu)
                {
                    GUI.backgroundColor = new Color(0.85f, 0.9f, 1f);
                    _win = GUI.Window(0x4846, _win, DrawWindow, "House Flipper Trainer   [Insert — закрыть, End — выгрузить]");
                }
            }
            catch (Exception ex) { Log.Write("OnGUI: " + ex); }
        }

        private void DrawWindow(int id)
        {
            int newTab = GUILayout.SelectionGrid(_tab, Tabs, 4);
            if (newTab != _tab) { _tab = newTab; _scroll = Vector2.zero; }
            GUILayout.Space(4);
            switch (_tab)
            {
                case 0: TabEsp(); break;
                case 1: TabTargets(); break;
                case 2: TabPlayer(); break;
                case 3: TabWorld(); break;
                case 4: TabSearch(); break;
                case 5: TabScene(); break;
                case 6: TabTasks(); break;
            }
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        // ---------- ESP ----------
        private void TabEsp()
        {
            GUILayout.BeginHorizontal();
            _cfg.EspEnabled = GUILayout.Toggle(_cfg.EspEnabled, "ESP (F2)");
            _cfg.EspBoxes = GUILayout.Toggle(_cfg.EspBoxes, "Рамки");
            _cfg.EspTracers = GUILayout.Toggle(_cfg.EspTracers, "Линии");
            _cfg.EspDistance = GUILayout.Toggle(_cfg.EspDistance, "Дистанция");
            _cfg.EspCounterHud = GUILayout.Toggle(_cfg.EspCounterHud, "Счётчик");
            GUILayout.EndHorizontal();
            _cfg.EspMaxDistance = Slider("Макс. дистанция", _cfg.EspMaxDistance, 5, 300, "0");
            _cfg.ScanInterval = Slider("Интервал сканирования, с", _cfg.ScanInterval, 0.2f, 5, "0.0");

            GUILayout.Label("Правила: шаблон — часть имени класса (или go:часть_имени_объекта). " +
                            "Действие — метод без параметров (напр. Clean), Тип.Метод, #disable или #destroy.");
            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(22));
            GUILayout.Label("Шаблон", GUILayout.Width(120));
            GUILayout.Label("Подпись", GUILayout.Width(110));
            GUILayout.Label("Действие", GUILayout.Width(110));
            GUILayout.EndHorizontal();

            bool changed = false;
            EspRule remove = null;
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(300));
            foreach (var r in _cfg.Rules)
            {
                GUILayout.BeginHorizontal();
                bool en = GUILayout.Toggle(r.Enabled, "", GUILayout.Width(22));
                string pat = GUILayout.TextField(r.Pattern, GUILayout.Width(120));
                r.Label = GUILayout.TextField(r.Label, GUILayout.Width(110));
                r.Action = GUILayout.TextField(r.Action, GUILayout.Width(110));
                if (en != r.Enabled || pat != r.Pattern) { r.Enabled = en; r.Pattern = pat; changed = true; }
                var old = GUI.backgroundColor;
                GUI.backgroundColor = r.Color;
                if (GUILayout.Button("■", GUILayout.Width(26))) r.Color = Palette[(Array.IndexOf(Palette, r.Color) + 1) % Palette.Length];
                GUI.backgroundColor = old;
                Scanner.RuleCounts.TryGetValue(r, out int cnt);
                GUILayout.Label(cnt.ToString(), GUILayout.Width(34));
                if (GUILayout.Button("Все", GUILayout.Width(40)))
                {
                    int n = Actions.ExecuteRule(r);
                    Flash(r.Action.Length == 0 ? "У правила не задано действие" : "Выполнено: " + n);
                    Scanner.Invalidate();
                }
                if (GUILayout.Button("×", GUILayout.Width(22))) remove = r;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (remove != null) { _cfg.Rules.Remove(remove); changed = true; }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Правило")) { _cfg.Rules.Add(new EspRule { Pattern = "", Label = "Новое", Color = Palette[_cfg.Rules.Count % Palette.Length] }); }
            if (GUILayout.Button("Сохранить")) { _cfg.Save(); Flash("Сохранено в " + Config.FilePath); }
            if (GUILayout.Button("Сбросить по умолчанию")) { _cfg = Config.CreateDefault(); changed = true; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Исключить типы (с учётом регистра):", GUILayout.Width(230));
            string ex = GUILayout.TextField(string.Join(",", _cfg.Excludes.ToArray()));
            GUILayout.EndHorizontal();
            if (ex != string.Join(",", _cfg.Excludes.ToArray()))
            {
                _cfg.Excludes.Clear();
                foreach (var s in ex.Split(',')) if (s.Trim().Length > 0) _cfg.Excludes.Add(s.Trim());
                changed = true;
            }
            if (changed) Scanner.Invalidate();
        }

        // ---------- Цели ----------
        private void TabTargets()
        {
            _cfg.ActionRadius = Slider("Радиус действий", _cfg.ActionRadius, 1, 100, "0");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Выполнить в радиусе (F4)")) Flash("Выполнено: " + Actions.ExecuteInRadius(_cfg.ActionRadius));
            if (GUILayout.Button("ТП к ближайшей (F5)")) TeleportNearest();
            _cfg.AutoAction = GUILayout.Toggle(_cfg.AutoAction, "Авто-выполнение рядом");
            GUILayout.EndHorizontal();
            GUILayout.Label("Целей найдено: " + Scanner.Targets.Count + "   (кнопка «Д» = выполнить действие правила)");

            _scroll = GUILayout.BeginScrollView(_scroll);
            int shown = 0;
            foreach (var t in Scanner.Targets.ToArray())
            {
                if (!t.Alive) continue;
                if (++shown > 150) { GUILayout.Label("…и ещё " + (Scanner.Targets.Count - 150)); break; }
                GUILayout.BeginHorizontal();
                var old = GUI.contentColor; GUI.contentColor = t.Rule.Color;
                GUILayout.Label("■", GUILayout.Width(14));
                GUI.contentColor = old;
                GUILayout.Label(t.Rule.Label + " — " + t.Comp.GetType().Name + " «" + t.Comp.gameObject.name + "»", GUILayout.Width(400));
                GUILayout.Label(t.Distance.ToString("0.0") + "м", GUILayout.Width(50));
                if (GUILayout.Button("ТП", GUILayout.Width(32))) Player.TeleportNear(t);
                if (GUILayout.Button("Д", GUILayout.Width(26))) Flash(Actions.Execute(t) ? "Готово" : "Действие не задано или не сработало");
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // ---------- Игрок ----------
        private void TabPlayer()
        {
            GUILayout.Label("Игрок: " + Player.Describe());
            GUILayout.Space(4);
            bool fly = GUILayout.Toggle(Player.Fly, "Полёт / сквозь стены (F3) — WASD, Space/E вверх, Ctrl/Q вниз, Shift быстрее");
            if (fly != Player.Fly) Player.Fly = fly;
            _cfg.FlySpeed = Slider("Скорость полёта", _cfg.FlySpeed, 1, 40, "0");

            GUILayout.Space(6);
            _cfg.SpeedMultiplier = Slider("Множитель скорости ходьбы", _cfg.SpeedMultiplier, 0.5f, 6f, "0.0");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Применить скорость"))
            {
                int n = Player.ApplySpeed(_cfg.SpeedMultiplier);
                Flash(n > 0 ? "Изменено полей скорости: " + n : "Поля скорости не найдены — используйте «Поиск значений» (speed)");
            }
            if (GUILayout.Button("Сбросить")) { _cfg.SpeedMultiplier = 1f; Player.ApplySpeed(1f); }
            GUILayout.EndHorizontal();
            foreach (var f in Player.SpeedFieldRefs) GUILayout.Label("   " + f.Path + " = " + f.Read());

            GUILayout.Space(6);
            _cfg.Fov = Slider("Поле зрения (0 = как в игре)", _cfg.Fov, 0, 120, "0");

            GUILayout.Space(6);
            GUILayout.Label("Позиции (F6 — сохранить слот 1, F7 — загрузить):");
            for (int i = 0; i < Player.Slots.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Слот " + (i + 1) + ": " + (Player.Slots[i].HasValue ? Player.Slots[i].Value.ToString("F1") : "пусто"), GUILayout.Width(260));
                if (GUILayout.Button("Сохранить") && Player.Root != null) Player.Slots[i] = Player.Root.position;
                if (GUILayout.Button("Загрузить") && Player.Slots[i].HasValue) Player.TeleportTo(Player.Slots[i].Value);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Телепорт туда, куда смотрю"))
            {
                var cam = Player.Cam;
                if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 200f))
                    Player.TeleportTo(hit.point + hit.normal * 0.6f + Vector3.up * 0.2f);
                else Flash("Не во что телепортироваться");
            }
        }

        // ---------- Мир ----------
        private void TabWorld()
        {
            GUILayout.Label("Скорость игры ускоряет ВСЕ процессы: анимации уборки, покраски, сушки, ходьбу. F8 — x5 / x1.");
            float ts = Slider("Скорость игры", _timeScale, 0.1f, 10f, "0.0");
            if (Math.Abs(ts - _timeScale) > 0.001f) SetTimeScale(ts);
            GUILayout.BeginHorizontal();
            foreach (var v in new[] { 0.5f, 1f, 2f, 3f, 5f, 10f })
                if (GUILayout.Button("x" + v)) SetTimeScale(v);
            GUILayout.EndHorizontal();
            GUILayout.Label("Текущее Time.timeScale = " + Time.timeScale.ToString("0.##"));
            GUILayout.Space(10);
            GUILayout.Label("Файлы: " + Log.Dir);
            GUILayout.Label("  HFTrainer.cfg — настройки, HFTrainer.log — журнал, HFTrainer_dump.txt — дамп типов");
        }

        // ---------- Поиск значений ----------
        private void TabSearch()
        {
            GUILayout.Label("Ищет поля по имени во всех объектах игры. Деньги: money / cash / balance; опыт: exp / xp; навыки: skill / point / perk.");
            GUILayout.BeginHorizontal();
            _query = GUILayout.TextField(_query, GUILayout.Width(200));
            if (GUILayout.Button("Искать", GUILayout.Width(80))) DoSearch();
            foreach (var q in new[] { "money", "cash", "exp", "skill", "point", "level", "perk" })
                if (GUILayout.Button(q)) { _query = q; DoSearch(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("Найдено: " + _results.Count + "   Заморожено: " + _frozen.Count);

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var f in _results)
            {
                if (!f.Valid) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(f.Path + " : " + f.Field.FieldType.Name, GUILayout.Width(260));
                GUILayout.Label(f.Read(), GUILayout.Width(90));
                f.Edit = GUILayout.TextField(f.Edit, GUILayout.Width(80));
                if (GUILayout.Button("Задать", GUILayout.Width(60))) Flash(f.Write(f.Edit) ? "Записано: " + f.Path : "Не удалось записать");
                bool fr = GUILayout.Toggle(f.Frozen, "Заморозить");
                if (fr != f.Frozen)
                {
                    f.Frozen = fr;
                    if (fr) { f.FrozenValue = f.Edit.Length > 0 ? f.Edit : f.Read(); if (!_frozen.Contains(f)) _frozen.Add(f); }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private void DoSearch()
        {
            try
            {
                _results = Reflect.Search(_query.Trim(), 300);
                foreach (var f in _frozen) { int i = _results.FindIndex(r => r.Owner == f.Owner && r.Field == f.Field); if (i >= 0) _results[i] = f; }
                Flash("Найдено полей: " + _results.Count);
            }
            catch (Exception e) { Log.Write("Search: " + e); Flash("Ошибка поиска, см. HFTrainer.log"); }
        }

        // ---------- Сцена ----------
        private void TabScene()
        {
            GUILayout.Label("Типы компонентов в текущей сцене. Встаньте в доме с заданием, найдите типы грязи/мусора и нажмите «+ESP».");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Фильтр:", GUILayout.Width(55));
            _sceneFilter = GUILayout.TextField(_sceneFilter, GUILayout.Width(180));
            if (GUILayout.Button("Дамп всех типов игры в файл"))
            {
                try { Flash("Дамп: " + Reflect.Dump(), 8f); } catch (Exception e) { Log.Write("Dump: " + e); Flash("Ошибка дампа"); }
            }
            GUILayout.EndHorizontal();

            if (Time.unscaledTime > _nextSceneRefresh)
            {
                _nextSceneRefresh = Time.unscaledTime + 1f;
                _sceneTypes = Scanner.TypeCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Name).ToList();
            }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(_inspectType != null ? 230 : 480));
            int shown = 0;
            foreach (var kv in _sceneTypes)
            {
                if (_sceneFilter.Length > 0 && kv.Key.Name.IndexOf(_sceneFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > 300) break;
                GUILayout.BeginHorizontal();
                GUILayout.Label(kv.Key.Name, GUILayout.Width(330));
                GUILayout.Label("×" + kv.Value, GUILayout.Width(50));
                if (GUILayout.Button("+ESP", GUILayout.Width(50)))
                {
                    _cfg.Rules.Add(new EspRule { Pattern = kv.Key.Name, Label = kv.Key.Name, Color = Palette[_cfg.Rules.Count % Palette.Length] });
                    Scanner.Invalidate();
                    Flash("Добавлено правило ESP: " + kv.Key.Name + " (подпись можно изменить во вкладке ESP)");
                }
                if (GUILayout.Button("Инфо", GUILayout.Width(50))) Inspect(kv.Key);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (_inspectType != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("▶ " + _inspectType.FullName);
                if (GUILayout.Button("Закрыть", GUILayout.Width(70))) _inspectType = null;
                GUILayout.EndHorizontal();
                if (_inspectType == null) return;
                _scroll2 = GUILayout.BeginScrollView(_scroll2, GUILayout.Height(250));
                GUILayout.Label(_inspectText);
                GUILayout.Label("Методы без параметров (вызов на ВСЕХ объектах типа — сначала сохраните игру!):");
                foreach (var m in Reflect.ZeroArgMethods(_inspectType).Take(80))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(m.ReturnType.Name + " " + m.Name + "()", GUILayout.Width(300));
                    if (GUILayout.Button("Вызвать на всех", GUILayout.Width(120))) Flash("Вызвано на объектах: " + InvokeAll(_inspectType, m));
                    if (GUILayout.Button("→ Действие ESP", GUILayout.Width(110))) SetAsAction(_inspectType, m.Name);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
        }

        private void Inspect(Type t)
        {
            _inspectType = t;
            _scroll2 = Vector2.zero;
            var inst = UnityEngine.Object.FindObjectOfType(t);
            var sb = new StringBuilder();
            sb.AppendLine("Базовый класс: " + (t.BaseType != null ? t.BaseType.Name : "-") + "   Пример объекта: " + (inst != null ? inst.name : "-"));
            if (inst != null)
                Reflect.Describe(inst, sb, 0, "  ", new HashSet<object>(new Reflect.RefEq()));
            string s = sb.ToString();
            _inspectText = s.Length > 12000 ? s.Substring(0, 12000) + "\n…" : s;
        }

        private static int InvokeAll(Type t, System.Reflection.MethodInfo m)
        {
            int n = 0;
            foreach (var o in UnityEngine.Object.FindObjectsOfType(t))
            {
                try { m.Invoke(o, null); n++; }
                catch (Exception e) { Log.Write("Invoke " + m.Name + ": " + (e.InnerException ?? e).Message); }
            }
            Scanner.Invalidate();
            return n;
        }

        private void SetAsAction(Type t, string method)
        {
            var rule = _cfg.Rules.FirstOrDefault(r => !r.ByGameObjectName && r.Needle.Length > 0 &&
                                                      t.Name.IndexOf(r.Needle, StringComparison.OrdinalIgnoreCase) >= 0);
            if (rule == null)
            {
                rule = new EspRule { Pattern = t.Name, Label = t.Name, Color = Palette[_cfg.Rules.Count % Palette.Length] };
                _cfg.Rules.Add(rule);
            }
            rule.Action = method;
            Scanner.Invalidate();
            _cfg.Save();
            Flash("Правило «" + rule.Label + "»: действие = " + method);
        }

        // ---------- Задания ----------
        private void TabTasks()
        {
            GUILayout.Label("Показывает внутренние данные заданий/целей, включая скрытые пункты и счётчики.");
            if (GUILayout.Button("Найти объекты заданий")) FindTaskObjects();
            GUILayout.BeginHorizontal();
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(210), GUILayout.Height(500));
            for (int i = 0; i < _taskObjs.Count; i++)
            {
                if (GUILayout.Toggle(_taskSel == i, _taskObjs[i].Key, "Button") && _taskSel != i)
                {
                    _taskSel = i;
                    DescribeTask();
                }
            }
            if (_taskObjs.Count == 0) GUILayout.Label("Нажмите «Найти»");
            GUILayout.EndScrollView();
            _scroll2 = GUILayout.BeginScrollView(_scroll2, GUILayout.Height(500));
            if (_taskSel >= 0 && GUILayout.Button("Обновить")) DescribeTask();
            for (int i = 0; i < _taskLines.Length; i += 40)
            {
                int cnt = Math.Min(40, _taskLines.Length - i);
                GUILayout.Label(string.Join("\n", _taskLines, i, cnt));
            }
            GUILayout.EndScrollView();
            GUILayout.EndHorizontal();
        }

        private static bool IsTaskName(string n)
        {
            foreach (var w in TaskWords) if (n.IndexOf(w, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private void FindTaskObjects()
        {
            _taskObjs = new List<KeyValuePair<string, object>>();
            _taskSel = -1; _taskLines = new string[0];
            var seen = new HashSet<object>(new Reflect.RefEq());
            try
            {
                foreach (var o in UnityEngine.Object.FindObjectsOfType(typeof(MonoBehaviour)))
                {
                    var t = o.GetType();
                    if (Scanner.IsEngineType(t) || !IsTaskName(t.Name) || !seen.Add(o)) continue;
                    _taskObjs.Add(new KeyValuePair<string, object>(t.Name + " «" + o.name + "»", o));
                }
                foreach (var asm in Reflect.GameAssemblies)
                    foreach (var t in Reflect.SafeTypes(asm))
                    {
                        if (!IsTaskName(t.Name) || t.IsGenericTypeDefinition) continue;
                        foreach (var f in t.GetFields(Reflect.Stat))
                        {
                            object v;
                            try { v = f.GetValue(null); } catch { continue; }
                            if (v == null || v is string || v.GetType().IsPrimitive || !seen.Add(v)) continue;
                            if (v is UnityEngine.Object uo && uo == null) continue;
                            _taskObjs.Add(new KeyValuePair<string, object>(t.Name + "::" + Reflect.Clean(f.Name), v));
                        }
                    }
            }
            catch (Exception e) { Log.Write("FindTaskObjects: " + e); }
            _taskObjs = _taskObjs.Take(200).ToList();
            Flash("Найдено объектов заданий: " + _taskObjs.Count);
        }

        private void DescribeTask()
        {
            if (_taskSel < 0 || _taskSel >= _taskObjs.Count) return;
            var sb = new StringBuilder();
            try { Reflect.Describe(_taskObjs[_taskSel].Value, sb, 2, "", new HashSet<object>(new Reflect.RefEq())); }
            catch (Exception e) { sb.AppendLine("Ошибка: " + e.Message); }
            var lines = sb.ToString().Split('\n');
            _taskLines = lines.Length > 1500 ? lines.Take(1500).Concat(new[] { "…(обрезано)" }).ToArray() : lines;
        }

        // ---------- helpers ----------
        private static float Slider(string label, float v, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + v.ToString(fmt, CultureInfo.InvariantCulture), GUILayout.Width(250));
            float r = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return r;
        }
    }
}
