using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Demo
{
    public partial class Main : Form
    {
        private sealed class AppConfiguration
        {
            public int ComboDelayBaseMilliseconds { get; set; }

            public int ComboDelayVarianceMilliseconds { get; set; }

            public Keys ToggleHotkey { get; set; }

            public bool VerboseLoggingEnabled { get; set; }
        }

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

        private readonly GlobalKeyboardHook keyboardHook;
        private readonly System.Threading.Timer stateTimer;
        private readonly Random random = new Random();
        private readonly object stateSync = new object();
        private readonly object logSync = new object();
        private readonly string logFilePath;
        private readonly Queue<string> pendingLogLines = new Queue<string>();
        private int logFlushScheduled;
        private readonly Dictionary<Keys, int> injectedKeyRefCounts = new Dictionary<Keys, int>();
        private readonly Queue<ComboQueueItem> qComboQueue = new Queue<ComboQueueItem>();
        private readonly Queue<ComboQueueItem> eComboQueue = new Queue<ComboQueueItem>();
        private Icon baseAppIcon;
        private Icon enabledTrayIcon;
        private Icon disabledTrayIcon;
        private ComboQueueItem qCurrentItem;
        private ComboQueueItem eCurrentItem;
        private int comboDelayBaseMilliseconds = DefaultComboDelayBaseMilliseconds;
        private int comboDelayVarianceMilliseconds = DefaultComboDelayVarianceMilliseconds;
        private Keys toggleHotkey = DefaultToggleHotkey;
        private bool verboseLoggingEnabled;
        private bool toggleHotkeyHeld;
        private bool pauseHeld;
        private volatile bool isMappingEnabled = true;
        private bool startHidden = true;
        private int pendingQComboCount;
        private int pendingEComboCount;
        private int stateTimerRunning;
        private volatile bool isClosing;
        private int lastActivityTick = Environment.TickCount;
        private const int StateStuckTimeoutMilliseconds = 5000;
        private int lastLoggedStuckWarning = Environment.TickCount;
        private int lastHookActivityTick = Environment.TickCount;
        private const int HookTimeoutMilliseconds = 1000;
        private int hookRecoveryAttempts = 0;
        private const int MaxHookRecoveryAttempts = 5;
        private int lastComboKeyPressTick = Environment.TickCount;
        private const int ComboQueueDrainTimeoutMilliseconds = 500;

        public Main()
        {
            InitializeComponent();
            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogFileName);

            baseAppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (baseAppIcon != null)
            {
                Icon = baseAppIcon;
            }

            LoadConfiguration();

            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.KeyboardPressed += KeyboardHook_KeyboardPressed;
            keyboardHook.HookError += KeyboardHook_HookError;

            stateTimer = new System.Threading.Timer(
                StateTimer_Tick,
                null,
                StateTimerIntervalMilliseconds,
                StateTimerIntervalMilliseconds);

            UpdateStatusLabel();

            FormClosing += Main_Closing;
            LogDebug("app-start");
        }

        private void Main_Load(object sender, EventArgs e)
        {
        }

        private void Main_Shown(object sender, EventArgs e)
        {
            if (!startHidden)
            {
                return;
            }

            startHidden = false;
            HideToTray();
        }

        private void Main_Closing(object sender, CancelEventArgs e)
        {
            isClosing = true;
            stateTimer.Change(Timeout.Infinite, Timeout.Infinite);
            lock (stateSync)
            {
                ResetRuntimeState();
            }

            LogDebug("app-closing");
            FlushPendingLogsSync();
            keyboardHook.HookError -= KeyboardHook_HookError;
            keyboardHook.KeyboardPressed -= KeyboardHook_KeyboardPressed;
            keyboardHook.Dispose();
            stateTimer.Dispose();
            notifyIcon1.Visible = false;
            DisposeTrayIcons();
        }

        private void KeyboardHook_HookError(object sender, Exception e)
        {
            LogDebug("hook-error " + e.GetType().Name + " " + e.Message);
            RunOnUiThread(() =>
            {
                lock (stateSync)
                {
                    ResetRuntimeState();
                }
            });
        }

        private const int HookCallbackTimeoutMilliseconds = 200;

        private void KeyboardHook_KeyboardPressed(object sender, GlobalKeyboardHookEventArgs e)
        {
            var startTime = Environment.TickCount;
            
            if (CheckHookTimeout(startTime))
            {
                return;
            }

            Interlocked.Exchange(ref lastHookActivityTick, Environment.TickCount);
            
            if (CheckHookTimeout(startTime))
            {
                return;
            }

            if (!isMappingEnabled || e.IsInjected)
            {
                return;
            }

            if (CheckHookTimeout(startTime))
            {
                return;
            }

            if (e.KeyCode == Keys.Q)
            {
                e.Handled = true;
                if (!CheckHookTimeout(startTime))
                {
                    RegisterComboRequest(Keys.Q, e.IsKeyDown);
                }
            }
            else if (e.KeyCode == Keys.E)
            {
                e.Handled = true;
                if (!CheckHookTimeout(startTime))
                {
                    RegisterComboRequest(Keys.E, e.IsKeyDown);
                }
            }
        }

        private bool CheckHookTimeout(int startTime)
        {
            var elapsed = unchecked(Environment.TickCount - startTime);
            if (elapsed > HookCallbackTimeoutMilliseconds)
            {
                LogDebug("hook-callback-timeout elapsed=" + elapsed + "ms");
                return true;
            }
            return false;
        }

        private void StateTimer_Tick(object state)
        {
            if (isClosing || Interlocked.Exchange(ref stateTimerRunning, 1) != 0)
            {
                return;
            }

            try
            {
                HeartbeatLog();
                ProcessPolledHotkeys();
                CheckAndRecoverFromHookFailure();

                if (!isMappingEnabled)
                {
                    // LogVerbose("state-timer-disabled");
                    return;
                }

                lock (stateSync)
                {
                    var pendingQ = pendingQComboCount;
                    var pendingE = pendingEComboCount;
                    var qCount = qComboQueue.Count;
                    var eCount = eComboQueue.Count;
                    var qActive = qCurrentItem != null;
                    var eActive = eCurrentItem != null;
                    
                    if (pendingQ > 0 || pendingE > 0 || qCount > 0 || eCount > 0 || qActive || eActive)
                    {
                        LogDebug("state-timer qPending=" + pendingQ + " ePending=" + pendingE +
                                 " qQueue=" + qCount + " eQueue=" + eCount +
                                 " qActive=" + qActive + " eActive=" + eActive);
                    }
                    
                    DrainPendingComboRequests();
                    ProcessPendingSecondKey();
                    CheckAndRecoverFromStuckState();
                }
            }
            catch (Exception ex)
            {
                LogDebug("state-timer-error " + ex.GetType().Name + " " + ex.Message);
                RunOnUiThread(() =>
                {
                    lock (stateSync)
                    {
                        ResetRuntimeState();
                    }
                });
            }
            finally
            {
                Interlocked.Exchange(ref stateTimerRunning, 0);
            }
        }

        private static int lastHeartbeatTick = 0;
        private static int lastHeartbeatLogged = 0;
        
        private void HeartbeatLog()
        {
            var currentTick = Environment.TickCount;
            if (unchecked(currentTick - lastHeartbeatTick) > 10000)
            {
                lastHeartbeatTick = currentTick;
                var lastHookTick = Interlocked.CompareExchange(ref lastHookActivityTick, 0, 0);
                var timeSinceLastHook = unchecked(currentTick - lastHookTick);
                if (unchecked(currentTick - lastHeartbeatLogged) > 60000)
                {
                    lastHeartbeatLogged = currentTick;
                    LogDebug("heartbeat enabled=" + isMappingEnabled + " qPending=" + pendingQComboCount + 
                             " ePending=" + pendingEComboCount + " qCurr=" + (qCurrentItem != null) +
                             " eCurr=" + (eCurrentItem != null) + " qQueue=" + qComboQueue.Count +
                             " eQueue=" + eComboQueue.Count + " hookTime=" + timeSinceLastHook + "ms");
                }
            }
        }

        private void CheckAndRecoverFromStuckState()
        {
            var currentTick = Environment.TickCount;
            
            bool hasActiveCombo = qCurrentItem != null || eCurrentItem != null;
            bool hasQueuedItems = qComboQueue.Count > 0 || eComboQueue.Count > 0;
            
            if (hasActiveCombo || hasQueuedItems)
            {
                return;
            }
            
            if (unchecked(currentTick - lastActivityTick) > StateStuckTimeoutMilliseconds)
            {
                var lastWarning = Interlocked.Exchange(ref lastLoggedStuckWarning, currentTick);
                if (unchecked(currentTick - lastWarning) > StateStuckTimeoutMilliseconds)
                {
                    LogDebug("state-check no-activity timeout=" + unchecked(currentTick - lastActivityTick) + "ms");
                }
            }
        }

        private void CheckAndRecoverFromHookFailure()
        {
            var currentTick = Environment.TickCount;
            var lastHookTick = Interlocked.CompareExchange(ref lastHookActivityTick, 0, 0);
            var timeSinceLastHook = unchecked(currentTick - lastHookTick);

            if (timeSinceLastHook > HookTimeoutMilliseconds && hookRecoveryAttempts < MaxHookRecoveryAttempts)
            {
                LogDebug("hook-check timeout=" + timeSinceLastHook + "ms attempts=" + hookRecoveryAttempts);
                
                try
                {
                    LogDebug("hook-reset attempting recovery");
                    keyboardHook.Reset();
                    Interlocked.Increment(ref hookRecoveryAttempts);
                    Interlocked.Exchange(ref lastHookActivityTick, Environment.TickCount);
                    LogDebug("hook-reset successful");
                }
                catch (Exception ex)
                {
                    LogDebug("hook-reset failed " + ex.GetType().Name + " " + ex.Message);
                }
            }
            else if (timeSinceLastHook <= HookTimeoutMilliseconds && hookRecoveryAttempts > 0)
            {
                Interlocked.Exchange(ref hookRecoveryAttempts, 0);
                LogDebug("hook-check recovery-reset attempts=" + hookRecoveryAttempts);
            }
        }

        private void ProcessPolledHotkeys()
        {
            var toggleDown = IsPhysicalKeyDown(toggleHotkey);
            if (toggleDown && !toggleHotkeyHeld)
            {
                toggleHotkeyHeld = true;
                var targetEnabled = !isMappingEnabled;
                RunOnUiThread(() =>
                {
                    if (targetEnabled)
                    {
                        LoadConfiguration();
                    }

                    SetMappingEnabled(targetEnabled);
                });
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

        private void RegisterComboRequest(Keys sourceKey, bool isKeyDown)
        {
            if (!isKeyDown)
            {
                return;
            }

            Interlocked.Exchange(ref lastComboKeyPressTick, Environment.TickCount);

            if (sourceKey == Keys.Q)
            {
                var pending = Interlocked.Increment(ref pendingQComboCount);
                // LogVerbose("state Q pending=" + pending);
                return;
            }

            if (sourceKey == Keys.E)
            {
                var pending = Interlocked.Increment(ref pendingEComboCount);
                // LogVerbose("state E pending=" + pending);
            }
        }

        private void EnqueueCombo(Keys firstKey, Keys secondKey, Queue<ComboQueueItem> queue, string sourceName)
        {
            var queueItem = new ComboQueueItem
            {
                FirstKey = firstKey,
                SecondKey = secondKey,
                DueTick = unchecked(Environment.TickCount + GetRandomComboDelayMilliseconds()),
                FirstKeyReleaseDelay = 0,
                ReleaseDueTick = 0,
                SentFirstKeyReleased = false,
                SentSecondKey = false
            };

            if (!PressInjectedKeyUnsafe(firstKey))
            {
                LogDebug("enqueue-failed " + sourceName + " first=" + firstKey);
                ResetRuntimeStateUnsafe();
                return;
            }

            queue.Enqueue(queueItem);
            // LogVerbose("enqueue " + sourceName + " first=" + firstKey + " second=" + secondKey + " qCount=" + qComboQueue.Count + " eCount=" + eComboQueue.Count);
        }

        private bool PressInjectedKey(Keys key)
        {
            lock (stateSync)
            {
                return PressInjectedKeyUnsafe(key);
            }
        }

        private bool PressInjectedKeyUnsafe(Keys key)
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
            // LogVerbose("press " + key + " ref=1");
            return true;
        }

        private void ReleaseInjectedKey(Keys key)
        {
            lock (stateSync)
            {
                ReleaseInjectedKeyUnsafe(key);
            }
        }

        private void ReleaseInjectedKeyUnsafe(Keys key)
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
                // LogVerbose("release " + key + " ref=0");
            }
            else
            {
                injectedKeyRefCounts[key] = refCount;
                // LogVerbose("release-defer " + key + " ref=" + refCount);
            }
        }

        private void ClearInjectedKeyRefCounts()
        {
            lock (stateSync)
            {
                ClearInjectedKeyRefCountsUnsafe();
            }
        }

        private void ClearInjectedKeyRefCountsUnsafe()
        {
            foreach (var key in new List<Keys>(injectedKeyRefCounts.Keys))
            {
                TrySendKeyboardInput(key, true);
            }

            injectedKeyRefCounts.Clear();
        }

        private void CancelPendingKeys()
        {
            qComboQueue.Clear();
            qCurrentItem = null;
            eComboQueue.Clear();
            eCurrentItem = null;
        }

        private void ResetRuntimeState()
        {
            lock (stateSync)
            {
                ResetRuntimeStateUnsafe();
            }
        }

        private void ResetRuntimeStateUnsafe()
        {
            LogDebug("state-reset qCurr=" + (qCurrentItem != null ? qCurrentItem.FirstKey.ToString() : "null") +
                     " eCurr=" + (eCurrentItem != null ? eCurrentItem.FirstKey.ToString() : "null") +
                     " qQueue=" + qComboQueue.Count + " eQueue=" + eComboQueue.Count +
                     " qPending=" + pendingQComboCount + " ePending=" + pendingEComboCount);
            CancelPendingKeys();
            Interlocked.Exchange(ref pendingQComboCount, 0);
            Interlocked.Exchange(ref pendingEComboCount, 0);
            ClearInjectedKeyRefCountsUnsafe();
            qCurrentItem = null;
            eCurrentItem = null;
        }

        private void ProcessPendingSecondKey()
        {
            ProcessComboQueue(qComboQueue, ref qCurrentItem, "Q");
            ProcessComboQueue(eComboQueue, ref eCurrentItem, "E");
        }

        private void DrainPendingComboRequests()
        {
            var currentTick = Environment.TickCount;
            var lastPressTick = Interlocked.CompareExchange(ref lastComboKeyPressTick, 0, 0);
            var timeSinceLastPress = unchecked(currentTick - lastPressTick);

            if (timeSinceLastPress > ComboQueueDrainTimeoutMilliseconds)
            {
                if (qComboQueue.Count > 0)
                {
                    // LogVerbose("flush Q queue=" + qComboQueue.Count);
                    qComboQueue.Clear();
                }
                if (eComboQueue.Count > 0)
                {
                    // LogVerbose("flush E queue=" + eComboQueue.Count);
                    eComboQueue.Clear();
                }
            }

            var pendingQ = Interlocked.Exchange(ref pendingQComboCount, 0);
            for (var i = 0; i < pendingQ; i++)
            {
                // LogVerbose("drain Q pending=" + (pendingQ - i - 1));
                EnqueueCombo(Keys.A, Keys.S, qComboQueue, "Q");
            }

            var pendingE = Interlocked.Exchange(ref pendingEComboCount, 0);
            for (var i = 0; i < pendingE; i++)
            {
                // LogVerbose("drain E pending=" + (pendingE - i - 1));
                EnqueueCombo(Keys.D, Keys.S, eComboQueue, "E");
            }
        }

        private void ProcessComboQueue(Queue<ComboQueueItem> queue, ref ComboQueueItem currentItem, string sourceName)
        {
            if (currentItem == null && queue.Count > 0)
            {
                currentItem = queue.Dequeue();
            }

            if (currentItem == null)
            {
                return;
            }

            if (!currentItem.SentFirstKeyReleased)
            {
                var elapsed = unchecked(Environment.TickCount - currentItem.DueTick);
                if (elapsed >= 0)
                {
                    ReleaseInjectedKeyUnsafe(currentItem.FirstKey);
                    currentItem.FirstKeyReleaseDelay = unchecked(Environment.TickCount + GetRandomComboDelayMilliseconds());
                    currentItem.SentFirstKeyReleased = true;
                    // LogVerbose("release-first " + sourceName + " key=" + currentItem.FirstKey);
                }
                else
                {
                    var waitMs = unchecked(0 - elapsed);
                    if (waitMs > 100)
                    {
                        // LogVerbose("wait-first " + sourceName + " key=" + currentItem.FirstKey + " waitMs=" + waitMs);
                    }
                }
                return;
            }

            if (currentItem.SentFirstKeyReleased && !currentItem.SentSecondKey)
            {
                var elapsed = unchecked(Environment.TickCount - currentItem.FirstKeyReleaseDelay);
                if (elapsed >= 0)
                {
                    if (!PressInjectedKeyUnsafe(currentItem.SecondKey))
                    {
                        LogDebug("second-key-failed " + sourceName + " second=" + currentItem.SecondKey);
                        ResetRuntimeStateUnsafe();
                        return;
                    }

                    currentItem.ReleaseDueTick = unchecked(Environment.TickCount + GetRandomComboDelayMilliseconds());
                    currentItem.SentSecondKey = true;
                    // LogVerbose("press-second " + sourceName + " key=" + currentItem.SecondKey);
                }
                else
                {
                    var waitMs = unchecked(0 - elapsed);
                    if (waitMs > 100)
                    {
                        // LogVerbose("wait-second " + sourceName + " key=" + currentItem.SecondKey + " waitMs=" + waitMs);
                    }
                }
                return;
            }

            if (currentItem.SentSecondKey)
            {
                var elapsed = unchecked(Environment.TickCount - currentItem.ReleaseDueTick);
                if (elapsed >= 0)
                {
                    ReleaseInjectedKeyUnsafe(currentItem.SecondKey);
                    // LogVerbose("release-second " + sourceName + " key=" + currentItem.SecondKey);
                    currentItem = null;
                }
                else
                {
                    var waitMs = unchecked(0 - elapsed);
                    if (waitMs > 100)
                    {
                        // LogVerbose("wait-release " + sourceName + " key=" + currentItem.SecondKey + " waitMs=" + waitMs);
                    }
                }
                return;
            }
        }

        private bool TrySendKeyboardInput(Keys key, bool keyUp)
        {
            ushort scanCode;
            if (!TryGetScanCode(key, out scanCode))
            {
                LogDebug("scan-code-failed key=" + key);
                return false;
            }

            var flags = keyUp ? KeyeventfKeyup : 0u;
            keybd_event((byte)key, (byte)scanCode, flags, InjectionMarker);
            return true;
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

            LogDebug("mapping-enabled " + enabled);
            isMappingEnabled = enabled;
            toggleHotkeyHeld = false;
            pauseHeld = false;
            lock (stateSync)
            {
                ResetRuntimeState();
            }

            if (enabled)
            {
                keyboardHook.Reset();
            }

            LogDebug("mapping-" + (enabled ? "enabled" : "disabled"));
            UpdateStatusLabel();
        }

        private void LogDebug(string message)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message;
            lock (logSync)
            {
                pendingLogLines.Enqueue(line);
            }

            ScheduleLogFlush();
        }

        private void ScheduleLogFlush()
        {
            if (Interlocked.CompareExchange(ref logFlushScheduled, 1, 0) != 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(_ => FlushPendingLogsAsync());
        }

        private void FlushPendingLogsAsync()
        {
            while (true)
            {
                string[] linesToWrite;

                lock (logSync)
                {
                    if (pendingLogLines.Count == 0)
                    {
                        Interlocked.Exchange(ref logFlushScheduled, 0);
                        if (pendingLogLines.Count == 0)
                        {
                            return;
                        }

                        if (Interlocked.CompareExchange(ref logFlushScheduled, 1, 0) != 0)
                        {
                            return;
                        }
                    }

                    linesToWrite = pendingLogLines.ToArray();
                    pendingLogLines.Clear();
                }

                try
                {
                    File.AppendAllLines(logFilePath, linesToWrite);
                }
                catch
                {
                }
            }
        }

        private void FlushPendingLogsSync()
        {
            try
            {
                string[] linesToWrite;
                lock (logSync)
                {
                    if (pendingLogLines.Count == 0)
                    {
                        return;
                    }

                    linesToWrite = pendingLogLines.ToArray();
                    pendingLogLines.Clear();
                }

                File.AppendAllLines(logFilePath, linesToWrite);
            }
            catch
            {
            }
        }

        private void LogVerbose(string message)
        {
            if (!verboseLoggingEnabled)
            {
                return;
            }

            LogDebug("verbose " + message);
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
                    return (Icon)Icon.FromHandle(iconHandle).Clone();
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
            EnsureTrayIcons();
            if (baseAppIcon != null && notifyIcon1.Icon == null)
            {
                notifyIcon1.Icon = baseAppIcon;
            }

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
            verboseLoggingEnabled = config.VerboseLoggingEnabled;
        }

        private static Icon CreateDefaultIcon()
        {
            using (var bitmap = new Bitmap(16, 16))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(Color.Green))
            {
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(brush, 2, 2, 12, 12);

                var iconHandle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(iconHandle).Clone();
                }
                finally
                {
                    DestroyIcon(iconHandle);
                }
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
            config.ComboDelayBaseMilliseconds = DefaultComboDelayBaseMilliseconds;
            config.ComboDelayVarianceMilliseconds = DefaultComboDelayVarianceMilliseconds;
            config.ToggleHotkey = DefaultToggleHotkey;
            config.VerboseLoggingEnabled = DefaultVerboseLoggingEnabled;

            int delayBase;
            if (TryGetNonNegativeInt(values, "ComboDelayBaseMs", out delayBase))
            {
                config.ComboDelayBaseMilliseconds = delayBase;
            }

            int delayVariance;
            if (TryGetNonNegativeInt(values, "ComboDelayVarianceMs", out delayVariance))
            {
                config.ComboDelayVarianceMilliseconds = delayVariance;
            }

            if (!values.ContainsKey("ComboDelayBaseMs") && !values.ContainsKey("ComboDelayVarianceMs"))
            {
                ApplyLegacyDelayRange(values, config);
            }

            Keys parsedToggleHotkey;
            if (TryGetKeyValue(values, "ToggleHotkey", out parsedToggleHotkey))
            {
                config.ToggleHotkey = parsedToggleHotkey;
            }

            bool parsedVerboseLogging;
            if (TryGetBooleanValue(values, "VerboseLogging", out parsedVerboseLogging))
            {
                config.VerboseLoggingEnabled = parsedVerboseLogging;
            }

            return config;
        }

        private static void ApplyLegacyDelayRange(IDictionary<string, string> values, AppConfiguration config)
        {
            int minDelay;
            int maxDelay;
            var hasMin = TryGetNonNegativeInt(values, "ComboDelayMinMs", out minDelay);
            var hasMax = TryGetNonNegativeInt(values, "ComboDelayMaxMs", out maxDelay);
            if (!hasMin && !hasMax)
            {
                return;
            }

            if (!hasMin)
            {
                minDelay = DefaultComboDelayBaseMilliseconds;
            }

            if (!hasMax)
            {
                maxDelay = minDelay;
            }

            if (minDelay > maxDelay)
            {
                var swap = minDelay;
                minDelay = maxDelay;
                maxDelay = swap;
            }

            config.ComboDelayBaseMilliseconds = minDelay;
            config.ComboDelayVarianceMilliseconds = maxDelay - minDelay;
        }

        private static bool TryGetNonNegativeInt(IDictionary<string, string> values, string key, out int parsedValue)
        {
            parsedValue = 0;
            string rawValue;
            if (!values.TryGetValue(key, out rawValue))
            {
                return false;
            }

            int candidate;
            if (!int.TryParse(rawValue, out candidate) || candidate < 0)
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

        private static bool TryGetBooleanValue(IDictionary<string, string> values, string key, out bool parsedValue)
        {
            parsedValue = false;
            string rawValue;
            if (!values.TryGetValue(key, out rawValue))
            {
                return false;
            }

            if (string.Equals(rawValue, "1", StringComparison.OrdinalIgnoreCase))
            {
                parsedValue = true;
                return true;
            }

            if (string.Equals(rawValue, "0", StringComparison.OrdinalIgnoreCase))
            {
                parsedValue = false;
                return true;
            }

            return bool.TryParse(rawValue, out parsedValue);
        }

        private int GetRandomComboDelayMilliseconds()
        {
            if (comboDelayVarianceMilliseconds <= 0)
            {
                return comboDelayBaseMilliseconds;
            }

            return comboDelayBaseMilliseconds + random.Next(comboDelayVarianceMilliseconds + 1);
        }

        private static bool HasTickElapsed(int currentTick, int dueTick)
        {
            return unchecked(currentTick - dueTick) >= 0;
        }

        private bool TryGetScanCode(Keys key, out ushort scanCode)
        {
            scanCode = (ushort)MapVirtualKey((uint)key, MapvkVkToVsc);
            return scanCode != 0;
        }

        private const string ConfigurationFileName = "kof6key.ini";
        private const string LogFileName = "kof6key.log";
        private const int DefaultComboDelayBaseMilliseconds = 40;
        private const int DefaultComboDelayVarianceMilliseconds = 0;
        private const bool DefaultVerboseLoggingEnabled = false;
        private const int StateTimerIntervalMilliseconds = 1;
        private const uint KeyeventfKeyup = 0x0002;
        private const uint MapvkVkToVsc = 0;
        private static readonly Keys DefaultToggleHotkey = Keys.Right;
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
