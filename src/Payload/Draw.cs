using UnityEngine;

namespace HFTrainer
{
    internal static class Draw
    {
        private static Texture2D _white;
        private static GUIStyle _label, _labelCenter;

        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                    _white.hideFlags = HideFlags.HideAndDontSave;
                }
                return _white;
            }
        }

        public static GUIStyle LabelCenter
        {
            get
            {
                if (_labelCenter == null)
                {
                    _labelCenter = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
                    _labelCenter.normal.textColor = Color.white;
                    _labelCenter.padding = new RectOffset(4, 4, 1, 1);
                    _labelCenter.wordWrap = false;
                }
                return _labelCenter;
            }
        }

        public static GUIStyle Label
        {
            get
            {
                if (_label == null)
                {
                    _label = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false };
                    _label.normal.textColor = Color.white;
                }
                return _label;
            }
        }

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = old;
        }

        public static void Frame(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        /// <summary>Рамка с тёмной обводкой, чтобы была видна на любом фоне.</summary>
        public static void OutlinedFrame(Rect r, Color c, float t = 1.5f)
        {
            var shadow = new Color(0, 0, 0, 0.75f);
            Frame(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), shadow, t + 2);
            Frame(r, c, t);
        }

        /// <summary>Табличка с текстом, центрированная по X, верхний край — y.</summary>
        public static Rect Tag(float centerX, float y, string text, Color accent)
        {
            var content = new GUIContent(text);
            Vector2 size = LabelCenter.CalcSize(content);
            var r = new Rect(centerX - size.x / 2f, y, size.x, size.y);
            Fill(r, new Color(0.05f, 0.05f, 0.05f, 0.78f));
            Fill(new Rect(r.x, r.y, 3, r.height), accent);
            GUI.Label(r, content, LabelCenter);
            return r;
        }

        public static void Line(Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            Fill(new Rect(a.x, a.y - width / 2f, len, width), c);
            GUI.matrix = m;
        }
    }
}
