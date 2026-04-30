// ==============================================
// 文件名: Main.cs
// 功能描述: KOF6 按键映射工具主窗体
// 实现 Q/E 按键到 AS/DS 组合键的映射功能
// 支持配置文件、托盘图标、热键切换等功能
// ==============================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Demo
{
    /// <summary>
    /// KOF6 按键映射工具主窗体
    /// 实现 Q/E 按键到 AS/DS 组合键的映射
    /// </summary>
    public partial class Main : Form
    {
        private sealed class AppConfiguration
        {
            public int ComboDelayBaseMilliseconds { get; set; }

            public int ComboDelayVarianceMilliseconds { get; set; }

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
        private int comboDelayBaseMilliseconds = DefaultComboDelayBaseMilliseconds;
        private int comboDelayVarianceMilliseconds = DefaultComboDelayVarianceMilliseconds;
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
                HandleComboKey(ref qHeld, e.IsKeyDown, Keys.A, Keys.S, true);
                return;
            }

            if (e.KeyCode == Keys.E)
            {
                e.Handled = true;
                HandleComboKey(ref eHeld, e.IsKeyDown, Keys.D, Keys.S, false);
            }
        }

        private void HandleComboKey(ref bool isHeld, bool isKeyDown, Keys firstKey, Keys secondKey, bool isQKey)
        {
            if (isKeyDown)
            {
                isHeld = true;
                
                // 创建新的队列项
                var queueItem = new ComboQueueItem
                {
                    FirstKey = firstKey,
                    SecondKey = secondKey,
                    DueTick = unchecked(Environment.TickCount + GetRandomComboKeyDelayMilliseconds()),
                    FirstKeyReleaseDelay = 0,
                    ReleaseDueTick = 0,
                    SentFirstKeyReleased = false,
                    SentSecondKey = false
                };

                // 发送第一个按键
                PressInjectedKey(firstKey);

                // 添加到队列
                if (isQKey)
                {
                    qComboQueue.Enqueue(queueItem);
                }
                else
                {
                    eComboQueue.Enqueue(queueItem);
                }
                return;
            }

            // 按键释放时，只标记状态
            if (!isHeld)
            {
                return;
            }

            isHeld = false;
        }

        // 按键序列队列项
        private sealed class ComboQueueItem
        {
            public Keys FirstKey { get; set; }
            public Keys SecondKey { get; set; }
            public int DueTick { get; set; }
            public int FirstKeyReleaseDelay { get; set; }
            public int ReleaseDueTick { get; set; }
            public bool SentFirstKeyReleased { get; set; }
            public bool SentSecondKey { get; set; }
        }

        // Q键的队列
        private readonly Queue<ComboQueueItem> qComboQueue = new Queue<ComboQueueItem>();
        private ComboQueueItem qCurrentItem;

        // E键的队列
        private readonly Queue<ComboQueueItem> eComboQueue = new Queue<ComboQueueItem>();
        private ComboQueueItem eCurrentItem;

        private void CancelQPendingKeys()
        {
            // 清空队列
            qComboQueue.Clear();
            qCurrentItem = null;
        }

        private void CancelEPendingKeys()
        {
            // 清空队列
            eComboQueue.Clear();
            eCurrentItem = null;
        }

        private void ProcessPendingSecondKey()
        {
            // 处理Q键队列
            ProcessComboQueue(qComboQueue, ref qCurrentItem);

            // 处理E键队列
            ProcessComboQueue(eComboQueue, ref eCurrentItem);
        }

        private void ProcessComboQueue(Queue<ComboQueueItem> queue, ref ComboQueueItem currentItem)
        {
            // 如果没有当前项且队列不为空，取出下一项
            if (currentItem == null && queue.Count > 0)
            {
                currentItem = queue.Dequeue();
            }

            // 如果有当前项，处理它
            if (currentItem != null)
            {
                // 检查是否需要释放第一个按键并准备发送第二个按键
                // 注意：只有当第一个按键还未释放时，才执行此检查
                if (!currentItem.SentFirstKeyReleased && HasTickElapsed(Environment.TickCount, currentItem.DueTick))
                {
                    // 先释放第一个按键
                    ReleaseInjectedKey(currentItem.FirstKey);
                    // 设置第一个按键释放后的延迟
                    currentItem.FirstKeyReleaseDelay = unchecked(Environment.TickCount + comboDelayBaseMilliseconds + random.Next(comboDelayVarianceMilliseconds + 1));
                    currentItem.SentFirstKeyReleased = true;
                    return;
                }

                // 检查是否需要发送第二个按键（在第一个按键释放延迟后）
                if (currentItem.SentFirstKeyReleased && !currentItem.SentSecondKey && HasTickElapsed(Environment.TickCount, currentItem.FirstKeyReleaseDelay))
                {
                    // 发送第二个按键
                    PressInjectedKey(currentItem.SecondKey);
                    // 设置第二个按键的释放延迟（base + random(0~variance)，与第一个按键间隔相同）
                    currentItem.ReleaseDueTick = unchecked(Environment.TickCount + comboDelayBaseMilliseconds + random.Next(comboDelayVarianceMilliseconds + 1));
                    currentItem.SentSecondKey = true;
                    return;
                }

                // 检查是否需要释放第二个按键
                if (currentItem.SentSecondKey && HasTickElapsed(Environment.TickCount, currentItem.ReleaseDueTick))
                {
                    // 释放第二个按键
                    ReleaseInjectedKey(currentItem.SecondKey);
                    // 清除当前项，允许处理下一个
                    currentItem = null;
                }
            }
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

        private void ClearInjectedKeyRefCounts()
        {
            // 遍历所有注入的按键，发送释放事件
            foreach (var key in injectedKeyRefCounts.Keys)
            {
                TrySendKeyboardInput(key, true);
            }
            // 清空引用计数字典
            injectedKeyRefCounts.Clear();
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

            // 状态恢复：检测物理按键是否真正被按住
            // 当键盘钩子丢失释放事件时，强制重置状态
            RecoverStaleKeyStates();

            ProcessPendingSecondKey();
        }

        private void RecoverStaleKeyStates()
        {
            // 恢复 Q 键状态
            // 队列机制会自动处理序列，只需重置 held 状态
            if (qHeld && !IsPhysicalKeyDown(Keys.Q))
            {
                qHeld = false;
            }

            // 恢复 E 键状态
            // 队列机制会自动处理序列，只需重置 held 状态
            if (eHeld && !IsPhysicalKeyDown(Keys.E))
            {
                eHeld = false;
            }
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

            // 清理待发送的按键状态
            CancelQPendingKeys();
            CancelEPendingKeys();

            // 清理引用计数，确保所有注入的按键都被释放
            ClearInjectedKeyRefCounts();

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
            // 确保托盘图标已初始化（在显示气泡提示前需要设置 Icon）
            EnsureTrayIcons();
            if (baseAppIcon != null && notifyIcon1.Icon == null)
            {
                notifyIcon1.Icon = baseAppIcon;
            }
            
            // 如果 notifyIcon1.Icon 仍然为 null，创建一个简单的默认图标
            if (notifyIcon1.Icon == null)
            {
                notifyIcon1.Icon = CreateDefaultIcon();
            }
            
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigurationFileName);
            if (!File.Exists(configPath))
            {
                comboDelayBaseMilliseconds = DefaultComboDelayBaseMilliseconds;
                comboDelayVarianceMilliseconds = DefaultComboDelayVarianceMilliseconds;
                toggleHotkey = DefaultToggleHotkey;
                return;
            }

            var values = ReadConfigurationValues(configPath);
            var config = CreateConfiguration(values);
            comboDelayBaseMilliseconds = config.ComboDelayBaseMilliseconds;
            comboDelayVarianceMilliseconds = config.ComboDelayVarianceMilliseconds;
            toggleHotkey = config.ToggleHotkey;
        }

        private static Icon CreateDefaultIcon()
        {
            // 创建一个简单的默认图标（绿色圆形）
            var bitmap = new Bitmap(16, 16);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                using (var brush = new SolidBrush(Color.Green))
                {
                    graphics.FillEllipse(brush, 2, 2, 12, 12);
                }
            }
            return Icon.FromHandle(bitmap.GetHicon());
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
            config.ComboDelayBaseMilliseconds = DefaultComboDelayBaseMilliseconds;
            config.ComboDelayVarianceMilliseconds = DefaultComboDelayVarianceMilliseconds;
            config.ToggleHotkey = DefaultToggleHotkey;

            int delayBase;
            if (TryGetPositiveInt(values, "ComboDelayBaseMs", out delayBase))
            {
                config.ComboDelayBaseMilliseconds = delayBase;
            }

            int delayVariance;
            if (TryGetPositiveInt(values, "ComboDelayVarianceMs", out delayVariance))
            {
                config.ComboDelayVarianceMilliseconds = delayVariance;
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
            // 实际延迟 = 基础固定值 + 0到浮动值之间的随机数
            return comboDelayBaseMilliseconds + random.Next(comboDelayVarianceMilliseconds + 1);
        }

        private static bool HasTickElapsed(int currentTick, int dueTick)
        {
            return unchecked(currentTick - dueTick) >= 0;
        }

        private const string ConfigurationFileName = "kof6key.ini";
        private const int DefaultComboDelayBaseMilliseconds = 40;
        private const int DefaultComboDelayVarianceMilliseconds = 5;
        private static readonly Keys DefaultToggleHotkey = Keys.Right;
        private const int StateTimerIntervalMilliseconds = 1;
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
