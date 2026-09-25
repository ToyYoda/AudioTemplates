using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioTemplates
{
    class TemplateLine
    {
        public string InId = "", InName = "", OutId = "", OutName = "";
    }

    class DeviceItem
    {
        public readonly string Id, Name;
        public readonly bool Missing;

        public DeviceItem(string id, string name, bool missing)
        {
            Id = id; Name = name; Missing = missing;
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Id)) return "(keep current)";
            return Missing ? Name + "  (not connected)" : Name;
        }
    }

    class MainForm : Form
    {
        public const int MaxLines = 5;
        public static readonly int ShowMessage = RegisterWindowMessage("AudioTemplates.Show");

        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
        // Win-key combinations aren't offered: Explorer reserves Win(+Ctrl/Alt/Shift)+digit for the taskbar.
        static readonly string[] ModifierNames = { "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Ctrl+Alt+Shift" };
        static readonly uint[] ModifierFlags = { MOD_CONTROL | MOD_ALT, MOD_CONTROL | MOD_SHIFT, MOD_ALT | MOD_SHIFT, MOD_CONTROL | MOD_ALT | MOD_SHIFT };
        const uint VK_1 = 0x31, VK_NUMPAD1 = 0x61;
        const int NumpadHotkeyOffset = 10;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "AudioTemplates";

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int RegisterWindowMessage(string message);

        readonly List<TemplateLine> lines = new List<TemplateLine>();
        readonly string configPath;
        List<DeviceInfo> inputs = new List<DeviceInfo>();
        List<DeviceInfo> outputs = new List<DeviceInfo>();
        bool notify = true;
        int modifierIndex;
        bool exiting, trayHintShown, rebuilding;

        readonly TableLayoutPanel grid;
        readonly Label hint;
        readonly Button addButton;
        readonly CheckBox notifyBox;
        readonly Label status;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip trayMenu;

        public MainForm()
        {
            configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioTemplates", "config.txt");
            LoadConfig();

            float scale;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            Func<int, int> px = v => (int)(v * scale);

            Text = "Audio Templates";
            Font = new Font("Segoe UI", 9f);
            Icon = AppIcon();
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(px(820), px(320));
            MinimumSize = new Size(px(600), px(260));
            Padding = new Padding(px(12));

            grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 5,
                Padding = new Padding(0, px(6), 0, 0),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var gridHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            gridHost.Controls.Add(grid);

            hint = new Label { Dock = DockStyle.Top, AutoSize = true, ForeColor = SystemColors.GrayText };
            UpdateHint();

            var modifierLabel = new Label { Text = "Hotkey:", AutoSize = true, Margin = new Padding(px(12), px(7), 0, 0) };
            var modifierBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = px(130) };
            foreach (string name in ModifierNames) modifierBox.Items.Add(name + " + 1…5");
            modifierBox.SelectedIndex = modifierIndex;
            modifierBox.SelectedIndexChanged += delegate
            {
                modifierIndex = modifierBox.SelectedIndex;
                SaveConfig();
                UpdateHint();
                RebuildGrid();
                RegisterHotkeys();
            };

            addButton = new Button { Text = "Add line", AutoSize = true };
            addButton.Click += delegate
            {
                if (lines.Count >= MaxLines) return;
                lines.Add(new TemplateLine());
                SaveConfig();
                RebuildGrid();
                RegisterHotkeys();
            };

            var refreshButton = new Button { Text = "Refresh devices", AutoSize = true };
            refreshButton.Click += delegate { RefreshDevices(); };

            var autostartBox = new CheckBox { Text = "Start with Windows", AutoSize = true, Checked = GetAutostart(), Margin = new Padding(px(12), px(6), 0, 0) };
            autostartBox.CheckedChanged += delegate { SetAutostart(autostartBox.Checked); };

            notifyBox = new CheckBox { Text = "Show notification when switching", AutoSize = true, Checked = notify, Margin = new Padding(px(12), px(6), 0, 0) };
            notifyBox.CheckedChanged += delegate { notify = notifyBox.Checked; SaveConfig(); };

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true };
            bottom.Controls.AddRange(new Control[] { addButton, refreshButton, modifierLabel, modifierBox, autostartBox, notifyBox });

            status = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = px(24), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

            // Docking runs in reverse add order: status, bottom, hint, then gridHost fills the rest.
            Controls.Add(gridHost);
            Controls.Add(hint);
            Controls.Add(bottom);
            Controls.Add(status);

            trayMenu = new ContextMenuStrip();
            trayMenu.Opening += (s, e) => { BuildTrayMenu(); e.Cancel = false; };
            tray = new NotifyIcon { Icon = Icon, Text = "Audio Templates", ContextMenuStrip = trayMenu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };

            RefreshDevices();
        }

        // ---------- devices & grid ----------

        void RefreshDevices()
        {
            try
            {
                inputs = AudioDevices.List(EDataFlow.Capture);
                outputs = AudioDevices.List(EDataFlow.Render);
                SetStatus(string.Format("Found {0} microphone(s) and {1} output device(s).", inputs.Count, outputs.Count));
            }
            catch (Exception ex)
            {
                SetStatus("Could not read audio devices: " + ex.Message);
            }
            RebuildGrid();
        }

        void RebuildGrid()
        {
            rebuilding = true;
            grid.SuspendLayout();
            while (grid.Controls.Count > 0) grid.Controls[0].Dispose();
            grid.RowStyles.Clear();
            grid.RowCount = lines.Count + 1;

            string[] headers = { "Hotkey", "Microphone", "Output", "", "" };
            for (int c = 0; c < headers.Length; c++)
                grid.Controls.Add(new Label { Text = headers[c], AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, c, 0);

            for (int i = 0; i < lines.Count; i++)
            {
                int index = i;
                TemplateLine line = lines[i];

                var key = new Label { Text = HotkeyText(i), AutoSize = true, Anchor = AnchorStyles.Left };

                var mic = MakeCombo(inputs, line.InId, line.InName);
                mic.SelectedIndexChanged += delegate
                {
                    if (rebuilding) return;
                    var item = (DeviceItem)mic.SelectedItem;
                    line.InId = item.Id;
                    line.InName = item.Name;
                    SaveConfig();
                };

                var output = MakeCombo(outputs, line.OutId, line.OutName);
                output.SelectedIndexChanged += delegate
                {
                    if (rebuilding) return;
                    var item = (DeviceItem)output.SelectedItem;
                    line.OutId = item.Id;
                    line.OutName = item.Name;
                    SaveConfig();
                };

                var activate = new Button { Text = "Activate", AutoSize = true };
                activate.Click += delegate { ActivateLine(index); };

                var remove = new Button { Text = "Remove", AutoSize = true };
                remove.Click += delegate
                {
                    // Defer: the button disposes itself during the rebuild.
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        lines.RemoveAt(index);
                        SaveConfig();
                        RebuildGrid();
                        RegisterHotkeys();
                    }));
                };

                grid.Controls.Add(key, 0, i + 1);
                grid.Controls.Add(mic, 1, i + 1);
                grid.Controls.Add(output, 2, i + 1);
                grid.Controls.Add(activate, 3, i + 1);
                grid.Controls.Add(remove, 4, i + 1);
            }

            grid.ResumeLayout();
            addButton.Enabled = lines.Count < MaxLines;
            rebuilding = false;
        }

        static ComboBox MakeCombo(List<DeviceInfo> devices, string selectedId, string selectedName)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            combo.Items.Add(new DeviceItem("", "", false));
            int selected = 0;
            foreach (var d in devices)
            {
                int n = combo.Items.Add(new DeviceItem(d.Id, d.Name, false));
                if (d.Id == selectedId) selected = n;
            }
            // Keep a saved device selectable even while it's unplugged.
            if (!string.IsNullOrEmpty(selectedId) && selected == 0)
                selected = combo.Items.Add(new DeviceItem(selectedId, string.IsNullOrEmpty(selectedName) ? "Unknown device" : selectedName, true));
            combo.SelectedIndex = selected;
            return combo;
        }

        // ---------- switching ----------

        void ActivateLine(int index)
        {
            if (index < 0 || index >= lines.Count) return;
            TemplateLine line = lines[index];
            var messages = new List<string>();
            bool failed = false;

            failed |= !Apply(line.InId, line.InName, "Mic", messages);
            failed |= !Apply(line.OutId, line.OutName, "Out", messages);
            if (messages.Count == 0) messages.Add("Nothing selected in this line.");

            string title = "Audio template " + (index + 1);
            SetStatus(title + ":  " + string.Join("   |   ", messages));
            if (notify || failed)
                tray.ShowBalloonTip(2500, title, string.Join("\n", messages), failed ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        static bool Apply(string id, string name, string label, List<string> messages)
        {
            if (string.IsNullOrEmpty(id)) return true;
            try
            {
                AudioDevices.SetDefault(id);
                messages.Add(label + ": " + name);
                return true;
            }
            catch (Exception ex)
            {
                messages.Add(label + ": " + name + " – FAILED (" + ex.Message + ")");
                return false;
            }
        }

        // ---------- hotkeys ----------

        void RegisterHotkeys()
        {
            if (!IsHandleCreated) return;
            UnregisterHotkeys();
            var failed = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                uint mods = ModifierFlags[modifierIndex] | MOD_NOREPEAT;
                if (!RegisterHotKey(Handle, 1 + i, mods, VK_1 + (uint)i))
                    failed.Add(HotkeyText(i));
                RegisterHotKey(Handle, 1 + i + NumpadHotkeyOffset, mods, VK_NUMPAD1 + (uint)i);
            }
            if (failed.Count > 0)
                SetStatus("Already used by another app: " + string.Join(", ", failed));
            else if (lines.Count > 0)
                SetStatus("Hotkeys active: " + HotkeyText(0) + (lines.Count > 1 ? " … " + HotkeyText(lines.Count - 1) : ""));
        }

        void UnregisterHotkeys()
        {
            for (int i = 0; i < MaxLines; i++)
            {
                UnregisterHotKey(Handle, 1 + i);
                UnregisterHotKey(Handle, 1 + i + NumpadHotkeyOffset);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotkeys();
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                ActivateLine((id > NumpadHotkeyOffset ? id - NumpadHotkeyOffset : id) - 1);
                return;
            }
            if (ShowMessage != 0 && m.Msg == ShowMessage)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        // ---------- tray ----------

        void BuildTrayMenu()
        {
            trayMenu.Items.Clear();
            var open = new ToolStripMenuItem("Open Audio Templates", null, delegate { ShowFromTray(); });
            open.Font = new Font(open.Font, FontStyle.Bold);
            trayMenu.Items.Add(open);
            trayMenu.Items.Add(new ToolStripSeparator());
            for (int i = 0; i < lines.Count; i++)
            {
                int index = i;
                TemplateLine l = lines[i];
                string text = string.Format("{0}:  {1}  /  {2}", i + 1,
                    string.IsNullOrEmpty(l.InId) ? "(keep mic)" : l.InName,
                    string.IsNullOrEmpty(l.OutId) ? "(keep output)" : l.OutName);
                var item = new ToolStripMenuItem(text, null, delegate { ActivateLine(index); });
                item.ShortcutKeyDisplayString = HotkeyText(i);
                trayMenu.Items.Add(item);
            }
            if (lines.Count > 0) trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitApp(); }));
        }

        void ShowFromTray()
        {
            RefreshDevices();
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void HideToTray()
        {
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                tray.ShowBalloonTip(3000, "Audio Templates", "Still running in the tray. Use " + ModifierNames[modifierIndex] + "+1…5 to switch.", ToolTipIcon.Info);
            }
        }

        void ExitApp()
        {
            exiting = true;
            Close();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && Visible) HideToTray();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
            Application.Exit();
        }

        // ---------- persistence ----------

        void LoadConfig()
        {
            lines.Clear();
            try
            {
                if (File.Exists(configPath))
                {
                    foreach (string raw in File.ReadAllLines(configPath))
                    {
                        if (raw.StartsWith("notify="))
                            notify = raw.Substring(7).Trim() != "0";
                        else if (raw.StartsWith("modifiers="))
                        {
                            int found = Array.IndexOf(ModifierNames, raw.Substring(10).Trim());
                            if (found >= 0) modifierIndex = found;
                        }
                        else if (raw.StartsWith("line=") && lines.Count < MaxLines)
                        {
                            string[] p = raw.Substring(5).Split('\t');
                            lines.Add(new TemplateLine
                            {
                                InId = p.Length > 0 ? p[0] : "",
                                InName = p.Length > 1 ? p[1] : "",
                                OutId = p.Length > 2 ? p[2] : "",
                                OutName = p.Length > 3 ? p[3] : "",
                            });
                        }
                    }
                }
            }
            catch { /* unreadable config: start fresh */ }
            if (lines.Count == 0) lines.Add(new TemplateLine());
        }

        void SaveConfig()
        {
            try
            {
                var output = new List<string> { "notify=" + (notify ? "1" : "0"), "modifiers=" + ModifierNames[modifierIndex] };
                foreach (var l in lines)
                    output.Add("line=" + string.Join("\t", new[] { l.InId, l.InName, l.OutId, l.OutName }));
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                File.WriteAllLines(configPath, output);
            }
            catch (Exception ex)
            {
                SetStatus("Could not save settings: " + ex.Message);
            }
        }

        static bool GetAutostart()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(RunValue) != null;
        }

        void SetAutostart(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled) key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\" --minimized");
                    else key.DeleteValue(RunValue, false);
                }
            }
            catch (Exception ex)
            {
                SetStatus("Could not change autostart: " + ex.Message);
            }
        }

        // ---------- helpers ----------

        string HotkeyText(int lineIndex)
        {
            return ModifierNames[modifierIndex] + "+" + (lineIndex + 1);
        }

        void UpdateHint()
        {
            string mods = ModifierNames[modifierIndex];
            hint.Text = "Press " + mods + "+1…5 (or " + mods + "+Numpad 1…5) anywhere to switch to that line.\n" +
                        "Closing or minimizing the window keeps the app running in the tray.";
        }

        void SetStatus(string text)
        {
            if (status != null) status.Text = text;
        }

        static Icon AppIcon()
        {
            try
            {
                string sndVol = Path.Combine(Environment.SystemDirectory, "SndVol.exe");
                if (File.Exists(sndVol))
                {
                    Icon icon = Icon.ExtractAssociatedIcon(sndVol);
                    if (icon != null) return icon;
                }
            }
            catch { }
            return SystemIcons.Application;
        }
    }
}
