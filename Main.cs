using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Demo
{
    public partial class Main : Form
    {
        private sealed class AppConfiguration
        {
            public int ComboDelayMinimumMilliseconds { get; set; }

            public int ComboDelayMaximumMilliseconds { get; set; }

            public Keys ToggleHotkey { get; set; }
        }

        private enum SequenceStage
        {
            Idle,
            WaitingForSecondKey,
            WaitingForCycleEnd
        }

        private sealed class ComboSequenceState
        {
            public Keys SourceKey { get; set; }

            public Keys FirstKey { get; set; }

            public Keys SecondKey { get; set; }

            public SequenceStage Stage { get; set; }

            public int DueTick { get; set; }
        }

        private readonly GlobalKeyboardHook keyboardHook;
        private readonly Timer stateTimer;
        private readonly Random random = new Random();
        private readonly Dictionary<Keys, int> injectedKeyRefCounts = new Dictionary<Keys, int>();
        private Icon baseAppIcon;
        private Icon enabledTrayIcon;
        private Icon disabledTrayIcon;
        private ComboSequenceState activeSequence = new ComboSequenceState();
        private int comboDelayMinimumMilliseconds = DefaultComboDelayMinimumMilliseconds;
        private int comboDelayMaximumMilliseconds = DefaultComboDelayMaximumMilliseconds;
        private Keys toggleHotkey = DefaultToggleHotkey;
        private bool qHeld;
        private bool eHeld;
        private bool toggleHotkeyHeld;
        private bool pauseHeld;
        private bool isMappingEnabled = true;
        private bool hookFailureLogged;
        private bool startHidden = true;

        public Main()
        {
            InitializeComponent();

            baseAppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (baseAppIcon != null)
            {
                Icon = baseAppIcon;
            }

            LoadConfiguration();

            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.KeyboardPressed += KeyboardHook_KeyboardPressed;
            keyboardHook.HookError += KeyboardHook_HookError;

            stateTimer = new Timer();
            stateTimer.Interval = StateTimerIntervalMilliseconds;
            stateTimer.Tick += StateTimer_Tick;
            stateTimer.Start();

            UpdateStatusLabel();

            FormClosing += Main_Closing;
        }

        private void Main_Load(object sender, EventArgs e)
        {
        }

        private void Main_Shown(object sender, EventArgs e)
        {
            if (startHidden)
            {
                startHidden = false;
                HideToTray();
            }
        }

        private void Main_Closing(object sender, CancelEventArgs e)
        {
            stateTimer.Stop();
            keyboardHook.HookError -= KeyboardHook_HookError;
            keyboardHook.Dispose();
            stateTimer.Dispose();
            notifyIcon1.Visible = false;
            DisposeTrayIcons();
        }

        private void KeyboardHook_HookError(object sender, Exception e)
        {
            RunOnUiThread(() =>
            {
                if (hookFailureLogged)
                {
                    return;
                }

                hookFailureLogged = true;
                activeSequence.Stage = SequenceStage.Idle;
            });
        }

        private void KeyboardHook_KeyboardPressed(object sender, GlobalKeyboardHookEventArgs e)
        {
            if (e.IsInjected)
            {
                return;
            }

            if (!isMappingEnabled)
            {
                return;
            }

            if (e.KeyCode == Keys.Q)
            {
                e.Handled = true;
                HandleComboKey(ref qHeld, e.IsKeyDown, Keys.A, Keys.S);
                return;
            }

            if (e.KeyCode == Keys.E)
            {
                e.Handled = true;
                HandleComboKey(ref eHeld, e.IsKeyDown, Keys.D, Keys.S);
            }
        }

        private void HandleComboKey(ref bool isHeld, bool isKeyDown, Keys firstKey, Keys secondKey)
        {
            if (isKeyDown)
            {
                if (isHeld)
                {
                    return;
                }

                isHeld = true;
                if (!PressInjectedKey(firstKey))
                {
                    isHeld = false;
                    return;
                }

                // 启动定时器发送第二个按键（A + 随机延迟 + S）
                ScheduleSecondKey(firstKey, secondKey);
                return;
            }

            if (!isHeld)
            {
                return;
            }

            isHeld = false;
            ReleaseInjectedKey(firstKey);
            ReleaseInjectedKey(secondKey);
            CancelPendingKeys();
        }

        private Keys pendingSecondKeyFirst;
        private Keys pendingSecondKeySecond;
        private int pendingSecondKeyDueTick;

        private void ScheduleSecondKey(Keys firstKey, Keys secondKey)
        {
            pendingSecondKeyFirst = firstKey;
            pendingSecondKeySecond = secondKey;
            pendingSecondKeyDueTick = unchecked(Environment.TickCount + GetRandomComboKeyDelayMilliseconds());
        }

        private void CancelPendingKeys()
        {
            pendingSecondKeyFirst = Keys.None;
            pendingSecondKeySecond = Keys.None;
            pendingSecondKeyDueTick = 0;
        }

        private void ProcessPendingSecondKey()
        {
            if (pendingSecondKeyFirst == Keys.None)
            {
                return;
            }

            if (!HasTickElapsed(Environment.TickCount, pendingSecondKeyDueTick))
            {
                return;
            }

            // 发送第二个按键（S），发送后立即完成序列
            if (!PressInjectedKey(pendingSecondKeySecond))
            {
                ReleaseInjectedKey(pendingSecondKeyFirst);
                if (qHeld) qHeld = false;
                if (eHeld) eHeld = false;
            }
            CancelPendingKeys();
        }

        private bool PressInjectedKey(Keys key)
        {
            int refCount;
            if (injectedKeyRefCounts.TryGetValue(key, out refCount))
            {
                injectedKeyRefCounts[key] = refCount + 1;
                return true;
            }

            if (!TrySendKeyboardInput(key, false))
            {
                return false;
            }

            injectedKeyRefCounts[key] = 1;
            return true;
        }

        private void ReleaseInjectedKey(Keys key)
        {
            int refCount;
            if (!injectedKeyRefCounts.TryGetValue(key, out refCount))
            {
                return;
            }

            refCount--;
            if (refCount == 0)
            {
                TrySendKeyboardInput(key, true);
                injectedKeyRefCounts.Remove(key);
            }
            else
            {
                injectedKeyRefCounts[key] = refCount;
            }
        }

        private bool TrySendKeyboardInput(Keys key, bool keyUp)
        {
            ushort scanCode;
            if (!TryGetScanCode(key, out scanCode))
            {
                return false;
            }

            var flags = keyUp ? KeyeventfKeyup : 0u;
            keybd_event((byte)key, (byte)scanCode, flags, InjectionMarker);
            return true;
        }

        private void StateTimer_Tick(object sender, EventArgs e)
        {
            ProcessPolledHotkeys();

            if (!isMappingEnabled)
            {
                return;
            }

            ProcessPendingSecondKey();
        }



        private void ProcessPolledHotkeys()
        {
            var toggleDown = IsPhysicalKeyDown(toggleHotkey);
            if (toggleDown && !toggleHotkeyHeld)
            {
                toggleHotkeyHeld = true;
                var targetEnabled = !isMappingEnabled;
                if (targetEnabled)
                {
                    LoadConfiguration();
                }

                SetMappingEnabled(targetEnabled);
            }
            else if (!toggleDown)
            {
                toggleHotkeyHeld = false;
            }

            var pauseDown = IsPhysicalKeyDown(Keys.Pause);
            if (pauseDown && !pauseHeld)
            {
                pauseHeld = true;
                ToggleVisibility();
            }
            else if (!pauseDown)
            {
                pauseHeld = false;
            }
        }

        private bool TryGetScanCode(Keys key, out ushort scanCode)
        {
            scanCode = (ushort)MapVirtualKey((uint)key, MapvkVkToVsc);
            return scanCode != 0;
        }

        private void SetMappingEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(SetMappingEnabled), enabled);
                return;
            }

            if (isMappingEnabled == enabled)
            {
                return;
            }

            isMappingEnabled = enabled;
            
            // 无论启用还是禁用，都重置状态机
            qHeld = false;
            eHeld = false;
            activeSequence.Stage = SequenceStage.Idle;
            hookFailureLogged = false;

            if (enabled)
            {
                keyboardHook.Reset();
            }

            UpdateStatusLabel();
        }

        private void UpdateStatusLabel()
        {
            statusLabel.Text = isMappingEnabled ? "Enabled" : "Disabled";
            statusLabel.BackColor = isMappingEnabled ? Color.FromArgb(220, 252, 231) : Color.FromArgb(254, 226, 226);
            statusLabel.ForeColor = isMappingEnabled ? Color.FromArgb(22, 101, 52) : Color.FromArgb(153, 27, 27);
            UpdateTrayIcon();
        }

        private void ToggleVisibility()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ToggleVisibility));
                return;
            }

            Visible = !Visible;
            if (Visible)
            {
                ShowFromTray();
            }
            else
            {
                HideToTray();
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ToggleVisibility();
        }

        private void UpdateTrayIcon()
        {
            EnsureTrayIcons();
            notifyIcon1.Icon = isMappingEnabled ? enabledTrayIcon : disabledTrayIcon;
        }

        private void EnsureTrayIcons()
        {
            if (baseAppIcon == null)
            {
                return;
            }

            if (enabledTrayIcon == null)
            {
                enabledTrayIcon = CreateStatusTrayIcon(baseAppIcon, Color.FromArgb(34, 197, 94));
            }

            if (disabledTrayIcon == null)
            {
                disabledTrayIcon = CreateStatusTrayIcon(baseAppIcon, Color.FromArgb(239, 68, 68));
            }
        }

        private static Icon CreateStatusTrayIcon(Icon sourceIcon, Color dotColor)
        {
            using (var bitmap = sourceIcon.ToBitmap())
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(dotColor))
            using (var pen = new Pen(Color.White, 1.5f))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                const int dotSize = 9;
                var dotBounds = new Rectangle(bitmap.Width - dotSize - 1, bitmap.Height - dotSize - 1, dotSize, dotSize);
                graphics.FillEllipse(brush, dotBounds);
                graphics.DrawEllipse(pen, dotBounds);

                var iconHandle = bitmap.GetHicon();
                try
                {
                    return Icon.FromHandle(iconHandle).Clone() as Icon;
                }
                finally
                {
                    DestroyIcon(iconHandle);
                }
            }
        }

        private void HideToTray()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Hide();
        }

        private void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void DisposeTrayIcons()
        {
            if (enabledTrayIcon != null)
            {
                enabledTrayIcon.Dispose();
                enabledTrayIcon = null;
            }

            if (disabledTrayIcon != null)
            {
                disabledTrayIcon.Dispose();
                disabledTrayIcon = null;
            }

            if (baseAppIcon != null)
            {
                baseAppIcon.Dispose();
                baseAppIcon = null;
            }
        }

        private void RunOnUiThread(Action action)
        {
            if (IsDisposed)
            {
                return;
            }

            if (!IsHandleCreated)
            {
                var unused = Handle;
            }

            if (InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }

            action();
        }

        private void LoadConfiguration()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigurationFileName);
            if (!File.Exists(configPath))
            {
                comboDelayMinimumMilliseconds = DefaultComboDelayMinimumMilliseconds;
                comboDelayMaximumMilliseconds = DefaultComboDelayMaximumMilliseconds;
                toggleHotkey = DefaultToggleHotkey;
                return;
            }

            var values = ReadConfigurationValues(configPath);
            var config = CreateConfiguration(values);
            comboDelayMinimumMilliseconds = config.ComboDelayMinimumMilliseconds;
            comboDelayMaximumMilliseconds = config.ComboDelayMaximumMilliseconds;
            toggleHotkey = config.ToggleHotkey;
        }

        private static Dictionary<string, string> ReadConfigurationValues(string configPath)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in File.ReadAllLines(configPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, separatorIndex).Trim();
                var value = line.Substring(separatorIndex + 1).Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                values[key] = value;
            }

            return values;
        }

        private static AppConfiguration CreateConfiguration(IDictionary<string, string> values)
        {
            var config = new AppConfiguration();
            config.ComboDelayMinimumMilliseconds = DefaultComboDelayMinimumMilliseconds;
            config.ComboDelayMaximumMilliseconds = DefaultComboDelayMaximumMilliseconds;
            config.ToggleHotkey = DefaultToggleHotkey;

            int delayMinimum;
            if (TryGetPositiveInt(values, "ComboDelayMinMs", out delayMinimum))
            {
                config.ComboDelayMinimumMilliseconds = delayMinimum;
            }

            int delayMaximum;
            if (TryGetPositiveInt(values, "ComboDelayMaxMs", out delayMaximum))
            {
                config.ComboDelayMaximumMilliseconds = delayMaximum;
            }

            if (config.ComboDelayMinimumMilliseconds > config.ComboDelayMaximumMilliseconds)
            {
                var swap = config.ComboDelayMinimumMilliseconds;
                config.ComboDelayMinimumMilliseconds = config.ComboDelayMaximumMilliseconds;
                config.ComboDelayMaximumMilliseconds = swap;
            }

            Keys parsedToggleHotkey;
            if (TryGetKeyValue(values, "ToggleHotkey", out parsedToggleHotkey))
            {
                config.ToggleHotkey = parsedToggleHotkey;
            }

            return config;
        }

        private static bool TryGetPositiveInt(IDictionary<string, string> values, string key, out int parsedValue)
        {
            parsedValue = 0;
            string rawValue;
            if (!values.TryGetValue(key, out rawValue))
            {
                return false;
            }

            int candidate;
            if (!int.TryParse(rawValue, out candidate) || candidate <= 0)
            {
                return false;
            }

            parsedValue = candidate;
            return true;
        }

        private static bool TryGetKeyValue(IDictionary<string, string> values, string key, out Keys parsedKey)
        {
            parsedKey = Keys.None;
            string rawValue;
            if (!values.TryGetValue(key, out rawValue))
            {
                return false;
            }

            return Enum.TryParse(rawValue, true, out parsedKey) && parsedKey != Keys.None;
        }

        private int GetRandomComboKeyDelayMilliseconds()
        {
            return random.Next(comboDelayMinimumMilliseconds, comboDelayMaximumMilliseconds + 1);
        }

        private static bool HasTickElapsed(int currentTick, int dueTick)
        {
            return unchecked(currentTick - dueTick) >= 0;
        }

        private const string ConfigurationFileName = "kof6key.ini";
        private const int DefaultComboDelayMinimumMilliseconds = 25;
        private const int DefaultComboDelayMaximumMilliseconds = 30;
        private static readonly Keys DefaultToggleHotkey = Keys.Right;
        private const int StateTimerIntervalMilliseconds = 10;
        private const uint KeyeventfKeyup = 0x0002;
        private const uint MapvkVkToVsc = 0;
        private static readonly IntPtr InjectionMarker = new IntPtr(unchecked((int)0x4B364B36));

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(Keys vKey);

        private bool IsPhysicalKeyDown(Keys key)
        {
            return (GetAsyncKeyState(key) & 0x8000) != 0;
        }
    }
}
