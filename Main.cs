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
        private sealed class ComboBindingState
        {
            public ComboBindingState(Keys sourceKey, Keys firstKey, Keys secondKey)
            {
                SourceKey = sourceKey;
                FirstKey = firstKey;
                SecondKey = secondKey;
            }

            public Keys SourceKey { get; private set; }

            public Keys FirstKey { get; private set; }

            public Keys SecondKey { get; private set; }

            public bool IsHeld { get; set; }

            public bool IsSecondKeyActive { get; set; }

            public bool IsSecondKeyPending { get; set; }

            public int SecondKeyDueTick { get; set; }
        }

        private sealed class AutoFireState
        {
            public AutoFireState()
            {
                HeldKeys = new HashSet<Keys>();
                NextShotTicks = new Dictionary<Keys, int>();
            }

            public HashSet<Keys> HeldKeys { get; private set; }

            public Dictionary<Keys, int> NextShotTicks { get; private set; }
        }

        private sealed class AppConfiguration
        {
            public int ComboDelayMinimumMilliseconds { get; set; }

            public int ComboDelayMaximumMilliseconds { get; set; }

            public Keys ToggleHotkey { get; set; }

            public List<Keys> AutoFireKeys { get; set; }

            public int AutoFireHoldDelayMilliseconds { get; set; }

            public bool AutoFireEnabled { get; set; }
        }

        private readonly object stateLock = new object();
        private readonly GlobalKeyboardHook keyboardHook;
        private readonly ComboBindingState qBinding = new ComboBindingState(Keys.Q, Keys.A, Keys.S);
        private readonly ComboBindingState eBinding = new ComboBindingState(Keys.E, Keys.D, Keys.S);
        private readonly AutoFireState autoFireState = new AutoFireState();
        private readonly Timer recoveryTimer;
        private readonly Random random = new Random();
        private readonly HashSet<Keys> autoFireKeys = new HashSet<Keys>();
        private readonly Dictionary<Keys, int> injectedKeyRefCounts = new Dictionary<Keys, int>();
        private readonly Dictionary<Keys, ushort> scanCodeCache = new Dictionary<Keys, ushort>();
        private Icon baseAppIcon;
        private Icon enabledTrayIcon;
        private Icon disabledTrayIcon;
        private int comboDelayMinimumMilliseconds = DefaultComboDelayMinimumMilliseconds;
        private int comboDelayMaximumMilliseconds = DefaultComboDelayMaximumMilliseconds;
        private Keys toggleHotkey = DefaultToggleHotkey;
        private int autoFireHoldDelayMilliseconds = DefaultAutoFireHoldDelayMilliseconds;
        private bool isMappingEnabled = true;
        private bool autoFireEnabled = true;
        private bool sendFailureLogged;
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
            autoFireCheckBox.Checked = autoFireEnabled;

            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.KeyboardPressed += KeyboardHook_KeyboardPressed;
            keyboardHook.HookError += KeyboardHook_HookError;

            recoveryTimer = new Timer();
            recoveryTimer.Interval = StateTimerIntervalMilliseconds;
            recoveryTimer.Tick += RecoveryTimer_Tick;
            recoveryTimer.Start();

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
            recoveryTimer.Stop();
            ReleaseAllInjectedKeys();
            keyboardHook.HookError -= KeyboardHook_HookError;
            keyboardHook.Dispose();
            recoveryTimer.Dispose();
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
                ReleaseAllInjectedKeys();
            });
        }

        private void KeyboardHook_KeyboardPressed(object sender, GlobalKeyboardHookEventArgs e)
        {
            if (e.IsInjected)
            {
                return;
            }

            if (e.IsKeyDown)
            {
                lock (stateLock)
                {
                    RecoverStaleComboStatesCore();
                    RecoverStaleAutoFireStateCore();
                }
            }

            if (HandleHotkeys(e))
            {
                e.Handled = true;
                return;
            }

            if (!isMappingEnabled)
            {
                return;
            }

            if (IsAutoFireKey(e.KeyCode) && (autoFireEnabled || IsAutoFireHeld()))
            {
                e.Handled = true;
                HandleAutoFireKey(e.KeyCode, e.IsKeyDown);
                return;
            }

            if (e.KeyCode == Keys.Q)
            {
                e.Handled = true;
                HandleComboKey(qBinding, e.IsKeyDown);
                return;
            }

            if (e.KeyCode == Keys.E)
            {
                e.Handled = true;
                HandleComboKey(eBinding, e.IsKeyDown);
            }
        }

        private bool HandleHotkeys(GlobalKeyboardHookEventArgs e)
        {
            if (!e.IsKeyUp)
            {
                return false;
            }

            if (e.KeyCode == toggleHotkey)
            {
                SetMappingEnabled(!isMappingEnabled, "Toggle hotkey toggled mapping");
                return true;
            }

            if (e.KeyCode == Keys.Pause)
            {
                ToggleVisibility();
                return true;
            }

            return false;
        }

        private void HandleComboKey(ComboBindingState binding, bool isKeyDown)
        {
            lock (stateLock)
            {
                hookFailureLogged = false;

                if (isKeyDown)
                {
                    if (binding.IsHeld)
                    {
                        return;
                    }

                    binding.IsHeld = true;
                    binding.IsSecondKeyActive = false;
                    binding.IsSecondKeyPending = false;
                    if (!PressInjectedKey(binding.FirstKey))
                    {
                        binding.IsHeld = false;
                        return;
                    }

                    binding.SecondKeyDueTick = unchecked(Environment.TickCount + GetRandomComboKeyDelayMilliseconds());
                    binding.IsSecondKeyPending = true;
                    return;
                }

                if (!binding.IsHeld)
                {
                    return;
                }

                ClearComboBinding(binding);
            }
        }

        private bool PressInjectedKey(Keys key)
        {
            var count = GetInjectedKeyCount(key);
            injectedKeyRefCounts[key] = count + 1;
            if (count == 0)
            {
                if (!TrySendKeyboardInput(key, false))
                {
                    injectedKeyRefCounts.Remove(key);
                    DisableMappingAfterSendFailure(key, false);
                    return false;
                }
            }

            return true;
        }

        private void ReleaseInjectedKey(Keys key)
        {
            var count = GetInjectedKeyCount(key);
            if (count <= 0)
            {
                return;
            }

            if (count == 1)
            {
                injectedKeyRefCounts.Remove(key);
                if (!TrySendKeyboardInput(key, true))
                {
                    DisableMappingAfterSendFailure(key, true);
                }
                return;
            }

            injectedKeyRefCounts[key] = count - 1;
        }

        private int GetInjectedKeyCount(Keys key)
        {
            int count;
            return injectedKeyRefCounts.TryGetValue(key, out count) ? count : 0;
        }

        private void ReleaseAllInjectedKeys()
        {
            lock (stateLock)
            {
                ClearAutoFireStateCore();
                ClearComboBindingStateOnly(qBinding);
                ClearComboBindingStateOnly(eBinding);

                foreach (var key in new List<Keys>(injectedKeyRefCounts.Keys))
                {
                    TrySendKeyboardInput(key, true);
                }

                injectedKeyRefCounts.Clear();
            }
        }

        private bool RecoverStaleComboStatesCore()
        {
            var recoveredQ = RecoverStaleComboStateCore(qBinding);
            var recoveredE = RecoverStaleComboStateCore(eBinding);
            return recoveredQ || recoveredE;
        }

        private bool RecoverStaleComboStateCore(ComboBindingState binding)
        {
            if (!binding.IsHeld || IsPhysicalKeyDown(binding.SourceKey))
            {
                return false;
            }

            ClearComboBinding(binding);
            return true;
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

        private bool TryGetScanCode(Keys key, out ushort scanCode)
        {
            if (scanCodeCache.TryGetValue(key, out scanCode))
            {
                return scanCode != 0;
            }

            scanCode = (ushort)MapVirtualKey((uint)key, MapvkVkToVsc);
            scanCodeCache[key] = scanCode;
            return scanCode != 0;
        }

        private void DisableMappingAfterSendFailure(Keys key, bool keyUp)
        {
            if (sendFailureLogged)
            {
                return;
            }

            sendFailureLogged = true;
            SetMappingEnabled(false, "Low-level send failed");
        }

        private void SetMappingEnabled(bool enabled, string reason)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool, string>(SetMappingEnabled), enabled, reason);
                return;
            }

            if (isMappingEnabled == enabled)
            {
                return;
            }

            isMappingEnabled = enabled;

            if (!enabled)
            {
                ReleaseAllInjectedKeys();
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

        private void autoFireCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            autoFireEnabled = autoFireCheckBox.Checked;
            if (!autoFireEnabled)
            {
                lock (stateLock)
                {
                    ClearAutoFireStateCore();
                }
            }
        }

        private void RecoveryTimer_Tick(object sender, EventArgs e)
        {
            var shouldResetHook = false;
            lock (stateLock)
            {
                var nowTick = Environment.TickCount;
                ProcessPendingComboSecondKeys(nowTick);
                ProcessAutoFire(nowTick);
                shouldResetHook = RecoverStaleComboStatesCore();
                RecoverStaleAutoFireStateCore();
            }

            if (shouldResetHook)
            {
                TryResetKeyboardHook();
            }
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

        private void ProcessPendingComboSecondKeys(int nowTick)
        {
            ProcessPendingComboSecondKey(qBinding, nowTick);
            ProcessPendingComboSecondKey(eBinding, nowTick);
        }

        private void ProcessPendingComboSecondKey(ComboBindingState binding, int nowTick)
        {
            if (!binding.IsHeld || !binding.IsSecondKeyPending || !HasTickElapsed(nowTick, binding.SecondKeyDueTick))
            {
                return;
            }

            binding.IsSecondKeyPending = false;
            if (!PressInjectedKey(binding.SecondKey))
            {
                ReleaseInjectedKey(binding.FirstKey);
                ClearComboBindingStateOnly(binding);
                return;
            }

            binding.IsSecondKeyActive = true;
        }

        private void HandleAutoFireKey(Keys key, bool isKeyDown)
        {
            lock (stateLock)
            {
                hookFailureLogged = false;

                if (isKeyDown)
                {
                    if (autoFireState.HeldKeys.Contains(key))
                    {
                        return;
                    }

                    autoFireState.HeldKeys.Add(key);
                    autoFireState.NextShotTicks[key] = unchecked(Environment.TickCount + autoFireHoldDelayMilliseconds);
                    SendAutoFireShot(key);
                    return;
                }

                if (!autoFireState.HeldKeys.Contains(key))
                {
                    return;
                }

                autoFireState.HeldKeys.Remove(key);
                autoFireState.NextShotTicks.Remove(key);
            }
        }

        private void ProcessAutoFire(int nowTick)
        {
            if (!autoFireEnabled || autoFireState.HeldKeys.Count == 0)
            {
                return;
            }

            var heldKeys = new List<Keys>(autoFireState.HeldKeys);
            foreach (var autoFireKey in heldKeys)
            {
                int nextShotTick;
                if (!autoFireState.NextShotTicks.TryGetValue(autoFireKey, out nextShotTick))
                {
                    continue;
                }

                if (!HasTickElapsed(nowTick, nextShotTick))
                {
                    continue;
                }

                if (!IsPhysicalKeyDown(autoFireKey))
                {
                    autoFireState.HeldKeys.Remove(autoFireKey);
                    autoFireState.NextShotTicks.Remove(autoFireKey);
                    continue;
                }

                SendAutoFireShot(autoFireKey);
                autoFireState.NextShotTicks[autoFireKey] = unchecked(nowTick + AutoFireRepeatIntervalMilliseconds);
            }
        }

        private void SendAutoFireShot(Keys key)
        {
            TrySendKeyboardInput(key, false);
            TrySendKeyboardInput(key, true);
        }

        private bool IsAutoFireHeld()
        {
            lock (stateLock)
            {
                return autoFireState.HeldKeys.Count > 0;
            }
        }

        private void RecoverStaleAutoFireStateCore()
        {
            var heldKeys = new List<Keys>(autoFireState.HeldKeys);
            foreach (var heldKey in heldKeys)
            {
                if (IsPhysicalKeyDown(heldKey))
                {
                    continue;
                }

                autoFireState.HeldKeys.Remove(heldKey);
                autoFireState.NextShotTicks.Remove(heldKey);
            }
        }

        private void ClearAutoFireStateCore()
        {
            autoFireState.HeldKeys.Clear();
            autoFireState.NextShotTicks.Clear();
        }

        private void ClearComboBinding(ComboBindingState binding)
        {
            ReleaseInjectedKey(binding.FirstKey);
            if (binding.IsSecondKeyActive)
            {
                ReleaseInjectedKey(binding.SecondKey);
            }

            ClearComboBindingStateOnly(binding);
        }

        private void ClearComboBindingStateOnly(ComboBindingState binding)
        {
            binding.IsHeld = false;
            binding.IsSecondKeyActive = false;
            binding.IsSecondKeyPending = false;
            binding.SecondKeyDueTick = 0;
        }

        private void TryResetKeyboardHook()
        {
            try
            {
                keyboardHook.Reset();
                hookFailureLogged = false;
            }
            catch (Exception)
            {
                SetMappingEnabled(false, "Hook reset failed");
            }
        }

        private static bool IsPhysicalKeyDown(Keys key)
        {
            return (GetAsyncKeyState((int)key) & 0x8000) != 0;
        }

        private static bool HasTickElapsed(int currentTick, int dueTick)
        {
            return unchecked(currentTick - dueTick) >= 0;
        }

        private void LoadConfiguration()
        {
            autoFireKeys.Clear();
            autoFireKeys.Add(DefaultAutoFireKey);

            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigurationFileName);
            if (!File.Exists(configPath))
            {
                return;
            }

            var values = ReadConfigurationValues(configPath);
            var config = CreateConfiguration(values);

            comboDelayMinimumMilliseconds = config.ComboDelayMinimumMilliseconds;
            comboDelayMaximumMilliseconds = config.ComboDelayMaximumMilliseconds;
            toggleHotkey = config.ToggleHotkey;
            autoFireHoldDelayMilliseconds = config.AutoFireHoldDelayMilliseconds;
            autoFireEnabled = config.AutoFireEnabled;

            autoFireKeys.Clear();
            foreach (var autoFireKey in config.AutoFireKeys)
            {
                autoFireKeys.Add(autoFireKey);
            }
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
            config.AutoFireKeys = new List<Keys>();
            config.AutoFireKeys.Add(DefaultAutoFireKey);
            config.AutoFireHoldDelayMilliseconds = DefaultAutoFireHoldDelayMilliseconds;
            config.AutoFireEnabled = true;

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

            List<Keys> parsedAutoFireKeys;
            if (TryGetKeyList(values, "AutoFireKeys", out parsedAutoFireKeys) && parsedAutoFireKeys.Count > 0)
            {
                config.AutoFireKeys = parsedAutoFireKeys;
            }

            int autoFireHoldDelay;
            if (TryGetPositiveInt(values, "AutoFireHoldDelayMs", out autoFireHoldDelay))
            {
                config.AutoFireHoldDelayMilliseconds = autoFireHoldDelay;
            }

            bool parsedAutoFireEnabled;
            if (TryGetBoolean(values, "AutoFireEnabled", out parsedAutoFireEnabled))
            {
                config.AutoFireEnabled = parsedAutoFireEnabled;
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

        private static bool TryGetBoolean(IDictionary<string, string> values, string key, out bool parsedValue)
        {
            parsedValue = false;
            string rawValue;
            if (!values.TryGetValue(key, out rawValue))
            {
                return false;
            }

            return bool.TryParse(rawValue, out parsedValue);
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

        private static bool TryGetKeyList(IDictionary<string, string> values, string key, out List<Keys> parsedKeys)
        {
            parsedKeys = new List<Keys>();
            string rawValue;
            if (!values.TryGetValue(key, out rawValue))
            {
                return false;
            }

            foreach (var part in rawValue.Split(new[] { ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Keys parsedKey;
                if (!Enum.TryParse(part, true, out parsedKey) || parsedKey == Keys.None || parsedKeys.Contains(parsedKey))
                {
                    continue;
                }

                parsedKeys.Add(parsedKey);
            }

            return parsedKeys.Count > 0;
        }

        private bool IsAutoFireKey(Keys key)
        {
            lock (stateLock)
            {
                return autoFireKeys.Contains(key);
            }
        }

        private bool IsAnyAutoFireKeyDown()
        {
            lock (stateLock)
            {
                foreach (var autoFireKey in autoFireKeys)
                {
                    if (IsPhysicalKeyDown(autoFireKey))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private int GetRandomComboKeyDelayMilliseconds()
        {
            return random.Next(comboDelayMinimumMilliseconds, comboDelayMaximumMilliseconds + 1);
        }

        private const string ConfigurationFileName = "kof6key.ini";
        private const int DefaultComboDelayMinimumMilliseconds = 10;
        private const int DefaultComboDelayMaximumMilliseconds = 30;
        private const int DefaultAutoFireHoldDelayMilliseconds = 500;
        private static readonly Keys DefaultToggleHotkey = Keys.Right;
        private static readonly Keys DefaultAutoFireKey = Keys.U;
        private const int AutoFireRepeatIntervalMilliseconds = 50;
        private const int StateTimerIntervalMilliseconds = 10;
        private const uint KeyeventfKeyup = 0x0002;
        private const uint MapvkVkToVsc = 0;
        private static readonly IntPtr InjectionMarker = new IntPtr(unchecked((int)0x4B364B36));

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

    }
}
