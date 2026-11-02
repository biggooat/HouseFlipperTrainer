using System.Collections.Generic;
using UnityEngine;

namespace HFTrainer
{
    internal static class Esp
    {
        private static readonly Vector3[] Corners = new Vector3[8];

        public static void Render(Config cfg)
        {
            var cam = Camera.main;
            if (cam == null) return;
            float sh = Screen.height;
            Vector2 tracerFrom = new Vector2(Screen.width / 2f, sh - 4f);

            for (int i = Scanner.Targets.Count - 1; i >= 0; i--) // дальние сначала, ближние сверху
            {
                var t = Scanner.Targets[i];
                if (!t.Alive || t.Distance > cfg.EspMaxDistance) continue;

                Rect box;
                if (!ScreenRect(cam, t, sh, out box)) continue;

                Color c = t.Rule.Color;
                float alpha = Mathf.Lerp(1f, 0.45f, t.Distance / Mathf.Max(1f, cfg.EspMaxDistance));
                c.a = alpha;

                if (cfg.EspBoxes) Draw.OutlinedFrame(box, c);
                if (cfg.EspTracers) Draw.Line(tracerFrom, new Vector2(box.center.x, box.yMax), new Color(c.r, c.g, c.b, 0.5f), 1.2f);

                string text = t.Rule.Label;
                if (cfg.EspDistance) text += "  " + t.Distance.ToString("0") + "м";
                Draw.Tag(box.center.x, box.yMax + 3f, text, c);
            }

            if (cfg.EspCounterHud) DrawHud();
        }

        private static bool ScreenRect(Camera cam, Target t, float sh, out Rect rect)
        {
            rect = default(Rect);
            if (t.TryGetBounds(out Bounds b))
            {
                Vector3 mn = b.min, mx = b.max;
                Corners[0] = new Vector3(mn.x, mn.y, mn.z); Corners[1] = new Vector3(mx.x, mn.y, mn.z);
                Corners[2] = new Vector3(mn.x, mx.y, mn.z); Corners[3] = new Vector3(mx.x, mx.y, mn.z);
                Corners[4] = new Vector3(mn.x, mn.y, mx.z); Corners[5] = new Vector3(mx.x, mn.y, mx.z);
                Corners[6] = new Vector3(mn.x, mx.y, mx.z); Corners[7] = new Vector3(mx.x, mx.y, mx.z);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 s = cam.WorldToScreenPoint(Corners[i]);
                    if (s.z <= 0.05f) return PointRect(cam, b.center, sh, out rect);
                    float y = sh - s.y;
                    if (s.x < x0) x0 = s.x; if (s.x > x1) x1 = s.x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
                if (x1 < 0 || y1 < 0 || x0 > Screen.width || y0 > sh) return false;
                // огромные рамки (стены/пол) обрезаем до экрана
                x0 = Mathf.Max(x0, 0); y0 = Mathf.Max(y0, 0); x1 = Mathf.Min(x1, Screen.width); y1 = Mathf.Min(y1, sh - 20);
                if (x1 - x0 < 8) { float cx = (x0 + x1) / 2; x0 = cx - 4; x1 = cx + 4; }
                if (y1 - y0 < 8) { float cy = (y0 + y1) / 2; y0 = cy - 4; y1 = cy + 4; }
                rect = Rect.MinMaxRect(x0, y0, x1, y1);
                return true;
            }
            return PointRect(cam, t.Comp.transform.position, sh, out rect);
        }

        private static bool PointRect(Camera cam, Vector3 p, float sh, out Rect rect)
        {
            rect = default(Rect);
            Vector3 s = cam.WorldToScreenPoint(p);
            if (s.z <= 0.05f) return false;
            float size = Mathf.Clamp(300f / s.z, 8f, 40f);
            rect = new Rect(s.x - size / 2, sh - s.y - size / 2, size, size);
            return rect.xMax > 0 && rect.yMax > 0 && rect.x < Screen.width && rect.y < sh;
        }

        private static void DrawHud()
        {
            if (Scanner.RuleCounts.Count == 0) return;
            // Объединяем правила с одинаковой подписью (Trash + Garbage -> Мусор)
            var byLabel = new Dictionary<string, KeyValuePair<Color, int>>();
            var order = new List<string>();
            foreach (var kv in Scanner.RuleCounts)
            {
                string l = kv.Key.Label;
                if (!byLabel.TryGetValue(l, out var e)) { byLabel[l] = new KeyValuePair<Color, int>(kv.Key.Color, kv.Value); order.Add(l); }
                else byLabel[l] = new KeyValuePair<Color, int>(e.Key, e.Value + kv.Value);
            }
            float y = 10f;
            Draw.Fill(new Rect(8, y, 190, 20 + order.Count * 18), new Color(0, 0, 0, 0.55f));
            GUI.Label(new Rect(14, y + 1, 180, 20), "Осталось сделать:", Draw.Label);
            y += 20;
            foreach (var l in order)
            {
                var e = byLabel[l];
                Draw.Fill(new Rect(14, y + 5, 8, 8), e.Key);
                GUI.Label(new Rect(28, y, 170, 20), l + ": " + e.Value, Draw.Label);
                y += 18;
            }
            Target nearest = null;
            foreach (var t in Scanner.Targets) if (t.Alive) { nearest = t; break; }
            if (nearest != null)
            {
                Draw.Fill(new Rect(8, y + 2, 190, 20), new Color(0, 0, 0, 0.55f));
                GUI.Label(new Rect(14, y + 2, 180, 20), "Ближайшее: " + nearest.Rule.Label + " " + nearest.Distance.ToString("0") + "м", Draw.Label);
            }
        }
    }
}
