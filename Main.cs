using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
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
        }

        private readonly GlobalKeyboardHook keyboardHook;
        private readonly ComboBindingState qBinding = new ComboBindingState(Keys.Q, Keys.A, Keys.S);
        private readonly ComboBindingState eBinding = new ComboBindingState(Keys.E, Keys.D, Keys.S);
        private readonly Timer qSecondKeyTimer;
        private readonly Timer eSecondKeyTimer;
        private readonly Timer recoveryTimer;
        private readonly Random random = new Random();
        private readonly Dictionary<Keys, int> injectedKeyRefCounts = new Dictionary<Keys, int>();
        private readonly Dictionary<Keys, ushort> scanCodeCache = new Dictionary<Keys, ushort>();
        private Icon baseAppIcon;
        private Icon enabledTrayIcon;
        private Icon disabledTrayIcon;
        private bool isMappingEnabled = true;
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

            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.KeyboardPressed += KeyboardHook_KeyboardPressed;
            keyboardHook.HookError += KeyboardHook_HookError;

            qSecondKeyTimer = CreateSecondKeyTimer(qBinding);
            eSecondKeyTimer = CreateSecondKeyTimer(eBinding);
            recoveryTimer = new Timer();
            recoveryTimer.Interval = RecoveryTimerIntervalMilliseconds;
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
            qSecondKeyTimer.Stop();
            eSecondKeyTimer.Stop();
            ReleaseAllInjectedKeys();
            keyboardHook.HookError -= KeyboardHook_HookError;
            keyboardHook.Dispose();
            recoveryTimer.Dispose();
            qSecondKeyTimer.Dispose();
            eSecondKeyTimer.Dispose();
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
                RecoverStaleComboStates();
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

            if (e.KeyCode == Keys.Q)
            {
                e.Handled = true;
                HandleComboKey(qBinding, qSecondKeyTimer, e.IsKeyDown);
                return;
            }

            if (e.KeyCode == Keys.E)
            {
                e.Handled = true;
                HandleComboKey(eBinding, eSecondKeyTimer, e.IsKeyDown);
            }
        }

        private bool HandleHotkeys(GlobalKeyboardHookEventArgs e)
        {
            if (!e.IsKeyUp)
            {
                return false;
            }

            switch (e.KeyCode)
            {
                case Keys.Right:
                    SetMappingEnabled(!isMappingEnabled, "Right Arrow toggled mapping");
                    return true;
                case Keys.Pause:
                    ToggleVisibility();
                    return true;
                default:
                    return false;
            }
        }

        private void HandleComboKey(ComboBindingState binding, Timer secondKeyTimer, bool isKeyDown)
        {
            hookFailureLogged = false;

            if (isKeyDown)
            {
                if (binding.IsHeld)
                {
                    return;
                }

                binding.IsHeld = true;
                if (!PressInjectedKey(binding.FirstKey))
                {
                    binding.IsHeld = false;
                    return;
                }

                secondKeyTimer.Stop();
                secondKeyTimer.Interval = GetRandomComboKeyDelayMilliseconds();
                secondKeyTimer.Start();
                return;
            }

            if (!binding.IsHeld)
            {
                return;
            }

            binding.IsHeld = false;
            secondKeyTimer.Stop();
            ReleaseInjectedKey(binding.FirstKey);
            ReleaseInjectedKey(binding.SecondKey);
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
            qBinding.IsHeld = false;
            eBinding.IsHeld = false;
            qSecondKeyTimer.Stop();
            eSecondKeyTimer.Stop();

            foreach (var key in new List<Keys>(injectedKeyRefCounts.Keys))
            {
                TrySendKeyboardInput(key, true);
            }

            injectedKeyRefCounts.Clear();
        }

        private bool RecoverStaleComboStates()
        {
            var recoveredQ = RecoverStaleComboState(qBinding, qSecondKeyTimer);
            var recoveredE = RecoverStaleComboState(eBinding, eSecondKeyTimer);
            return recoveredQ || recoveredE;
        }

        private bool RecoverStaleComboState(ComboBindingState binding, Timer secondKeyTimer)
        {
            if (!binding.IsHeld || IsPhysicalKeyDown(binding.SourceKey))
            {
                return false;
            }

            binding.IsHeld = false;
            secondKeyTimer.Stop();
            ReleaseInjectedKey(binding.FirstKey);
            ReleaseInjectedKey(binding.SecondKey);
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

        private void RecoveryTimer_Tick(object sender, EventArgs e)
        {
            if (RecoverStaleComboStates())
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

        private Timer CreateSecondKeyTimer(ComboBindingState binding)
        {
            var timer = new Timer();
            timer.Interval = ComboKeyDelayMinimumMilliseconds;
            timer.Tick += delegate
            {
                timer.Stop();
                HandleDelayedSecondKey(binding);
            };
            return timer;
        }

        private void HandleDelayedSecondKey(ComboBindingState binding)
        {
            if (!binding.IsHeld)
            {
                return;
            }

            if (!PressInjectedKey(binding.SecondKey))
            {
                ReleaseInjectedKey(binding.FirstKey);
                binding.IsHeld = false;
            }
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

        private int GetRandomComboKeyDelayMilliseconds()
        {
            return random.Next(ComboKeyDelayMinimumMilliseconds, ComboKeyDelayMaximumMilliseconds + 1);
        }

        private const int ComboKeyDelayMinimumMilliseconds = 5;
        private const int ComboKeyDelayMaximumMilliseconds = 15;
        private const int RecoveryTimerIntervalMilliseconds = 25;
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
