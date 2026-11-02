using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace HFTrainer.Injector
{
    internal sealed class MainForm : Form
    {
        private readonly Label _status = new Label { AutoSize = false, Height = 24, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        private readonly TextBox _procName = new TextBox { Text = "Flipper", Width = 140 };
        private readonly Button _inject = new Button { Text = "Внедрить", Width = 110, Height = 30, Enabled = false };
        private readonly CheckBox _auto = new CheckBox { Text = "Автовнедрение при запуске игры", AutoSize = true, Checked = true };
        private readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
        private readonly Timer _timer = new Timer { Interval = 1500 };
        private Process _game;
        private int _injectedPid = -1;
        private DateTime _foundAt;

        public MainForm()
        {
            Text = "House Flipper Trainer";
            Width = 560; Height = 460;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6) };
            top.Controls.Add(new Label { Text = "Процесс содержит:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
            top.Controls.Add(_procName);
            top.Controls.Add(_inject);
            top.Controls.Add(_auto);

            var help = new Label
            {
                Dock = DockStyle.Top, Height = 92, Padding = new Padding(8, 4, 8, 4),
                Text = "Горячие клавиши в игре:\n" +
                       "  Insert / F1 — меню трейнера      F2 — ESP вкл/выкл\n" +
                       "  F3 — полёт (noclip)               F4 — выполнить цели ESP в радиусе\n" +
                       "  F5 — телепорт к ближайшей цели    End — выгрузить трейнер\n" +
                       "Payload можно заменить: положите HFTrainer.Payload.dll рядом с .exe."
            };

            Controls.Add(_log);
            Controls.Add(help);
            Controls.Add(top);
            Controls.Add(_status);

            _inject.Click += (s, e) => DoInject();
            _timer.Tick += (s, e) => Poll();
            _timer.Start();
            Poll();
            Log("Трейнер запущен. Запустите House Flipper и загрузите сохранение.");
        }

        private void Log(string msg)
        {
            _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
        }

        private void Poll()
        {
            if (_game != null && _game.HasExited) { _game = null; _injectedPid = -1; Log("Игра закрыта."); }
            if (_game == null)
            {
                string key = _procName.Text.Trim();
                _game = Process.GetProcesses().FirstOrDefault(p =>
                    key.Length > 0 && p.ProcessName.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
                if (_game != null) { _foundAt = DateTime.Now; Log("Найден процесс " + _game.ProcessName + " (PID " + _game.Id + ")"); }
            }

            if (_game == null)
            {
                _status.Text = "  ● Игра не запущена"; _status.ForeColor = Color.Firebrick; _inject.Enabled = false;
                return;
            }
            bool injected = _injectedPid == _game.Id;
            _status.Text = injected ? "  ● Трейнер активен (Insert — меню в игре)" : "  ● Игра найдена: " + _game.ProcessName + " (PID " + _game.Id + ")";
            _status.ForeColor = injected ? Color.ForestGreen : Color.DarkOrange;
            _inject.Enabled = !injected;

            // Автовнедрение: даём игре ~15 с на загрузку Mono и сцены
            if (_auto.Checked && !injected && (DateTime.Now - _foundAt).TotalSeconds > 15)
                DoInject();
        }

        private byte[] LoadPayload()
        {
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HFTrainer.Payload.dll");
            if (File.Exists(local)) { Log("Используется внешний payload: " + local); return File.ReadAllBytes(local); }
            using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("HFTrainer.Payload.dll"))
            {
                if (s == null) throw new InvalidOperationException("Payload не вшит в .exe");
                var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
            }
        }

        private void DoInject()
        {
            if (_game == null) return;
            int pid = _game.Id;
            _injectedPid = pid; // не пытаться повторно в цикле при ошибке
            try
            {
                byte[] payload = LoadPayload();
                using (var inj = new MonoInjector(_game) { Log = Log })
                {
                    Log("Mono-модуль: " + inj.MonoModuleName);
                    inj.Inject(payload, "HFTrainer", "Loader", "Load");
                }
                Log("Готово! В игре нажмите Insert.");
            }
            catch (Exception ex)
            {
                Log("ОШИБКА: " + ex.Message);
                _auto.Checked = false;
                _injectedPid = -1;
            }
            Poll();
        }
    }
}
