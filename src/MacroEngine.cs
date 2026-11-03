using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SwiftMacroPro
{
    internal struct RawInputEvent
    {
        public int Message;
        public int X;
        public int Y;
        public int MouseData;
        public int VkCode;
        public long TimestampMs;
    }

    public sealed class MacroRecorder : IDisposable
    {
        private IntPtr _mouseHookId = IntPtr.Zero;
        private IntPtr _keyboardHookId = IntPtr.Zero;

        private readonly NativeMethods.LowLevelProc _mouseProc;
        private readonly NativeMethods.LowLevelProc _keyboardProc;

        private readonly ConcurrentQueue<RawInputEvent> _eventQueue = new ConcurrentQueue<RawInputEvent>();
        private readonly AutoResetEvent _queueSignal = new AutoResetEvent(false);
        private Thread _consumerThread;
        private volatile bool _isRecording;

        private readonly Stopwatch _stopwatch = new Stopwatch();
        private long _lastEventTimestampMs;
        private int _lastRecordedMoveX = int.MinValue;
        private int _lastRecordedMoveY = int.MinValue;
        private long _lastRecordedMoveTimeMs;

        private readonly List<MacroStep> _recordedSteps = new List<MacroStep>();
        private readonly HashSet<int> _ignoredHotKeyVkCodes = new HashSet<int>();
        private readonly HashSet<int> _currentlyDownKeys = new HashSet<int>();

        public bool RecordMouseMove { get; set; }
        public bool RecordKeyboard { get; set; }

        public event Action<MacroStep> StepRecorded;

        public MacroRecorder()
        {
            _mouseProc = MouseHookCallback;
            _keyboardProc = KeyboardHookCallback;
        }

        public bool IsRecording
        {
            get { return _isRecording; }
        }

        public void SetIgnoredHotkeyVkCodes(IEnumerable<int> vkCodes)
        {
            lock (_ignoredHotKeyVkCodes)
            {
                _ignoredHotKeyVkCodes.Clear();
                if (vkCodes != null)
                {
                    foreach (int vk in vkCodes)
                    {
                        if (vk > 0) _ignoredHotKeyVkCodes.Add(vk);
                    }
                }
                _ignoredHotKeyVkCodes.Add((int)Keys.F8);
                _ignoredHotKeyVkCodes.Add((int)Keys.F9);
                _ignoredHotKeyVkCodes.Add((int)Keys.F10);
            }
        }

        public void Start()
        {
            if (_isRecording) return;

            RawInputEvent discarded;
            while (_eventQueue.TryDequeue(out discarded)) { }
            _recordedSteps.Clear();
            _currentlyDownKeys.Clear();

            _stopwatch.Restart();
            _lastEventTimestampMs = 0;
            _lastRecordedMoveX = int.MinValue;
            _lastRecordedMoveY = int.MinValue;
            _lastRecordedMoveTimeMs = 0;

            _isRecording = true;
            _consumerThread = new Thread(ConsumerLoop)
            {
                IsBackground = true,
                Name = "MacroRecorderConsumer"
            };
            _consumerThread.Start();

            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                IntPtr hMod = NativeMethods.GetModuleHandle(curModule.ModuleName);
                _mouseHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);
                if (RecordKeyboard)
                {
                    _keyboardHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
                }
            }
        }

        public List<MacroStep> Stop()
        {
            if (!_isRecording) return new List<MacroStep>(_recordedSteps);

            _isRecording = false;
            UninstallHooks();

            _queueSignal.Set();
            if (_consumerThread != null && _consumerThread.IsAlive)
            {
                _consumerThread.Join(500);
            }

            _stopwatch.Stop();

            CloseDanglingDownEventsAtStop();
            MacroValidator.ReindexSteps(_recordedSteps);
            return new List<MacroStep>(_recordedSteps);
        }

        private void UninstallHooks()
        {
            if (_mouseHookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
            if (_keyboardHookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isRecording)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_LBUTTONDOWN || msg == NativeMethods.WM_LBUTTONUP ||
                    msg == NativeMethods.WM_RBUTTONDOWN || msg == NativeMethods.WM_RBUTTONUP ||
                    msg == NativeMethods.WM_MBUTTONDOWN || msg == NativeMethods.WM_MBUTTONUP ||
                    msg == NativeMethods.WM_MOUSEWHEEL ||
                    (RecordMouseMove && msg == NativeMethods.WM_MOUSEMOVE))
                {
                    NativeMethods.MSLLHOOKSTRUCT hookStruct =
                        (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));

                    if (hookStruct.dwExtraInfo == NativeMethods.SELF_INJECTED_SIGNATURE ||
                        (hookStruct.flags & NativeMethods.LLMHF_INJECTED) != 0)
                    {
                        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                    }

                    int wheelDelta = 0;
                    if (msg == NativeMethods.WM_MOUSEWHEEL)
                    {
                        wheelDelta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
                    }

                    RawInputEvent ev = new RawInputEvent
                    {
                        Message = msg,
                        X = hookStruct.pt.x,
                        Y = hookStruct.pt.y,
                        MouseData = wheelDelta,
                        VkCode = 0,
                        TimestampMs = _stopwatch.ElapsedMilliseconds
                    };
                    _eventQueue.Enqueue(ev);
                    _queueSignal.Set();
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isRecording && RecordKeyboard)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN ||
                    msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                {
                    NativeMethods.KBDLLHOOKSTRUCT hookStruct =
                        (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));

                    if (hookStruct.dwExtraInfo == NativeMethods.SELF_INJECTED_SIGNATURE ||
                        (hookStruct.flags & NativeMethods.LLKHF_INJECTED) != 0)
                    {
                        return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
                    }

                    int vk = (int)hookStruct.vkCode;
                    bool isIgnored;
                    lock (_ignoredHotKeyVkCodes)
                    {
                        isIgnored = _ignoredHotKeyVkCodes.Contains(vk);
                    }

                    if (!isIgnored)
                    {
                        RawInputEvent ev = new RawInputEvent
                        {
                            Message = msg,
                            X = 0,
                            Y = 0,
                            MouseData = 0,
                            VkCode = vk,
                            TimestampMs = _stopwatch.ElapsedMilliseconds
                        };
                        _eventQueue.Enqueue(ev);
                        _queueSignal.Set();
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private void ConsumerLoop()
        {
            while (_isRecording || !_eventQueue.IsEmpty)
            {
                RawInputEvent ev;
                if (_eventQueue.TryDequeue(out ev))
                {
                    ProcessRawEvent(ev);
                }
                else if (_isRecording)
                {
                    _queueSignal.WaitOne(50);
                }
            }
        }

        private void ProcessRawEvent(RawInputEvent ev)
        {
            if (ev.Message == NativeMethods.WM_MOUSEMOVE)
            {
                long dtMove = ev.TimestampMs - _lastRecordedMoveTimeMs;
                int dx = Math.Abs(ev.X - _lastRecordedMoveX);
                int dy = Math.Abs(ev.Y - _lastRecordedMoveY);
                if (dtMove < 30 || (dx < 15 && dy < 15))
                {
                    return;
                }
                _lastRecordedMoveX = ev.X;
                _lastRecordedMoveY = ev.Y;
                _lastRecordedMoveTimeMs = ev.TimestampMs;
            }

            bool isKeyDown = (ev.Message == NativeMethods.WM_KEYDOWN || ev.Message == NativeMethods.WM_SYSKEYDOWN);
            bool isKeyUp = (ev.Message == NativeMethods.WM_KEYUP || ev.Message == NativeMethods.WM_SYSKEYUP);
            if (isKeyDown)
            {
                if (_currentlyDownKeys.Contains(ev.VkCode))
                {
                    return;
                }
                _currentlyDownKeys.Add(ev.VkCode);
            }
            else if (isKeyUp)
            {
                if (!_currentlyDownKeys.Contains(ev.VkCode))
                {
                    return;
                }
                _currentlyDownKeys.Remove(ev.VkCode);
            }

            int delayBefore = (int)Math.Max(0L, ev.TimestampMs - _lastEventTimestampMs);
            _lastEventTimestampMs = ev.TimestampMs;

            if (TryCoalesceWithPreviousStep(ev, delayBefore))
            {
                return;
            }

            MacroActionType actionType;
            switch (ev.Message)
            {
                case NativeMethods.WM_LBUTTONDOWN: actionType = MacroActionType.LeftDown; break;
                case NativeMethods.WM_LBUTTONUP: actionType = MacroActionType.LeftUp; break;
                case NativeMethods.WM_RBUTTONDOWN: actionType = MacroActionType.RightDown; break;
                case NativeMethods.WM_RBUTTONUP: actionType = MacroActionType.RightUp; break;
                case NativeMethods.WM_MBUTTONDOWN: actionType = MacroActionType.MiddleDown; break;
                case NativeMethods.WM_MBUTTONUP: actionType = MacroActionType.MiddleUp; break;
                case NativeMethods.WM_MOUSEWHEEL: actionType = MacroActionType.MouseWheel; break;
                case NativeMethods.WM_MOUSEMOVE: actionType = MacroActionType.MouseMove; break;
                case NativeMethods.WM_KEYDOWN:
                case NativeMethods.WM_SYSKEYDOWN: actionType = MacroActionType.KeyDown; break;
                case NativeMethods.WM_KEYUP:
                case NativeMethods.WM_SYSKEYUP: actionType = MacroActionType.KeyUp; break;
                default: return;
            }

            MacroStep step = new MacroStep
            {
                StepId = _recordedSteps.Count + 1,
                ActionType = actionType.ToString(),
                X = ev.X,
                Y = ev.Y,
                MouseData = ev.MouseData,
                VirtualKeyCode = ev.VkCode,
                DelayBeforeMs = delayBefore,
                HoldDurationMs = 0
            };

            BindUpWithPriorOpenDown(step, actionType);

            _recordedSteps.Add(step);
            Action<MacroStep> handler = StepRecorded;
            if (handler != null)
            {
                handler(step);
            }
        }

        private bool TryCoalesceWithPreviousStep(RawInputEvent ev, int holdMs)
        {
            if (_recordedSteps.Count == 0 || holdMs > 350) return false;

            MacroStep last = _recordedSteps[_recordedSteps.Count - 1];
            MacroActionType lastType = last.ParsedActionType;

            bool closeEnough = Math.Abs(ev.X - last.X) <= 4 && Math.Abs(ev.Y - last.Y) <= 4;

            if (ev.Message == NativeMethods.WM_LBUTTONUP && lastType == MacroActionType.LeftDown && closeEnough)
            {
                last.ParsedActionType = MacroActionType.LeftClick;
                last.HoldDurationMs = Math.Max(10, holdMs);
                last.PairStepId = null;
                return true;
            }
            if (ev.Message == NativeMethods.WM_RBUTTONUP && lastType == MacroActionType.RightDown && closeEnough)
            {
                last.ParsedActionType = MacroActionType.RightClick;
                last.HoldDurationMs = Math.Max(10, holdMs);
                last.PairStepId = null;
                return true;
            }
            if (ev.Message == NativeMethods.WM_MBUTTONUP && lastType == MacroActionType.MiddleDown && closeEnough)
            {
                last.ParsedActionType = MacroActionType.MiddleClick;
                last.HoldDurationMs = Math.Max(10, holdMs);
                last.PairStepId = null;
                return true;
            }
            if ((ev.Message == NativeMethods.WM_KEYUP || ev.Message == NativeMethods.WM_SYSKEYUP) &&
                lastType == MacroActionType.KeyDown && last.VirtualKeyCode == ev.VkCode)
            {
                last.ParsedActionType = MacroActionType.KeyPress;
                last.HoldDurationMs = Math.Max(10, holdMs);
                last.PairStepId = null;
                return true;
            }

            return false;
        }

        private void BindUpWithPriorOpenDown(MacroStep upStep, MacroActionType upType)
        {
            MacroActionType? targetDown = MacroValidator.GetCounterpartAction(upType);
            if (!targetDown.HasValue || !MacroValidator.IsUpAction(upType)) return;

            for (int i = _recordedSteps.Count - 1; i >= 0; i--)
            {
                MacroStep candidate = _recordedSteps[i];
                if (candidate.ParsedActionType == targetDown.Value && !candidate.PairStepId.HasValue)
                {
                    if (upType == MacroActionType.KeyUp && candidate.VirtualKeyCode != upStep.VirtualKeyCode)
                        continue;

                    candidate.PairStepId = upStep.StepId;
                    upStep.PairStepId = candidate.StepId;
                    break;
                }
            }
        }

        private void CloseDanglingDownEventsAtStop()
        {
            for (int i = 0; i < _recordedSteps.Count; i++)
            {
                MacroStep step = _recordedSteps[i];
                MacroActionType t = step.ParsedActionType;
                if (MacroValidator.IsDownAction(t) && !step.PairStepId.HasValue)
                {
                    MacroActionType? counterpart = MacroValidator.GetCounterpartAction(t);
                    if (counterpart.HasValue)
                    {
                        MacroStep autoUp = new MacroStep
                        {
                            StepId = _recordedSteps.Count + 1,
                            ActionType = counterpart.Value.ToString(),
                            X = step.X,
                            Y = step.Y,
                            VirtualKeyCode = step.VirtualKeyCode,
                            DelayBeforeMs = 20,
                            HoldDurationMs = 0,
                            PairStepId = step.StepId
                        };
                        step.PairStepId = autoUp.StepId;
                        _recordedSteps.Add(autoUp);
                    }
                }
            }
        }

        public void Dispose()
        {
            UninstallHooks();
            _queueSignal.Dispose();
        }
    }

    /// <summary>
    /// Bộ phát kịch bản (Macro Player):
    /// Hỗ trợ 3 chế độ phát:
    /// 1. SendInput (Chuẩn phần cứng OS)
    /// 2. SnapAndRestore (Mượn & Trả chuột tức thời ~15ms, tối ưu khi để Web ở màn hình phụ)
    /// 3. BackgroundPostMessage (Bắn thông điệp WM_LBUTTONDOWN/UP theo HWND không đụng chuột)
    /// </summary>
    public sealed class MacroPlayer
    {
        private enum HeldMouseButton
        {
            Left,
            Right,
            Middle
        }

        private CancellationTokenSource _cts;
        private Thread _workerThread;
        private volatile bool _isPlaying;

        private readonly HashSet<HeldMouseButton> _pressedMouseButtons = new HashSet<HeldMouseButton>();
        private readonly HashSet<ushort> _pressedVirtualKeys = new HashSet<ushort>();
        private IntPtr _lastTargetHwnd = IntPtr.Zero;
        private int _lastTargetX;
        private int _lastTargetY;
        private readonly object _stateLock = new object();

        public event Action<int, int, int> ProgressChanged;
        public event Action<bool, string> PlaybackFinished;

        public bool IsPlaying
        {
            get { return _isPlaying; }
        }

        public void Start(MacroProfile profile)
        {
            if (_isPlaying || profile == null || profile.Steps == null || profile.Steps.Count == 0)
                return;

            List<MacroStep> stepsCopy = new List<MacroStep>(profile.Steps.Count);
            foreach (MacroStep s in profile.Steps)
            {
                stepsCopy.Add(s.Clone());
            }

            int loopCount = profile.LoopCount;
            double speedMultiplier = profile.SpeedMultiplier > 0 ? profile.SpeedMultiplier : 1.0;
            int randomJitterMs = Math.Max(0, profile.RandomJitterMs);
            PlaybackMode mode = profile.ParsedPlaybackMode;

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _isPlaying = true;

            _workerThread = new Thread(() => RunPlaybackLoop(stepsCopy, loopCount, speedMultiplier, randomJitterMs, mode, token))
            {
                IsBackground = true,
                Name = "MacroPlayerWorker"
            };
            _workerThread.Start();
        }

        public void Stop()
        {
            CancellationTokenSource cts = _cts;
            if (cts != null && !cts.IsCancellationRequested)
            {
                try { cts.Cancel(); } catch { }
            }
        }

        public static int CalculateEffectiveDelayMs(int rawDelayMs, double speedMultiplier, int randomJitterMs, Random rng)
        {
            if (rawDelayMs <= 0 && randomJitterMs <= 0) return 0;
            double speed = speedMultiplier > 0.01 ? speedMultiplier : 1.0;
            double scaled = rawDelayMs / speed;
            int delta = 0;
            if (randomJitterMs > 0 && rng != null)
            {
                delta = rng.Next(-randomJitterMs, randomJitterMs + 1);
            }
            int result = (int)Math.Round(scaled) + delta;
            return Math.Max(0, result);
        }

        private void RunPlaybackLoop(List<MacroStep> steps, int loopCount, double speedMultiplier, int randomJitterMs, PlaybackMode mode, CancellationToken token)
        {
            bool cancelled = false;
            string finishMessage = "Hoàn thành kịch bản.";
            Random rng = new Random();

            lock (_stateLock)
            {
                _pressedMouseButtons.Clear();
                _pressedVirtualKeys.Clear();
                _lastTargetHwnd = IntPtr.Zero;
            }

            try
            {
                int currentLoop = 0;
                while (!token.IsCancellationRequested)
                {
                    currentLoop++;
                    if (loopCount > 0 && currentLoop > loopCount)
                    {
                        break;
                    }

                    for (int i = 0; i < steps.Count; i++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }

                        MacroStep step = steps[i];
                        int effectiveDelay = CalculateEffectiveDelayMs(step.DelayBeforeMs, speedMultiplier, randomJitterMs, rng);
                        if (effectiveDelay > 0)
                        {
                            if (!WaitResponsive(effectiveDelay, token))
                            {
                                cancelled = true;
                                break;
                            }
                        }

                        if (token.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }

                        Action<int, int, int> progressHandler = ProgressChanged;
                        if (progressHandler != null)
                        {
                            progressHandler(currentLoop, loopCount, step.StepId);
                        }

                        if (mode == PlaybackMode.BackgroundPostMessage)
                        {
                            ExecuteStepPostMessage(step, speedMultiplier, randomJitterMs, rng, token);
                        }
                        else if (mode == PlaybackMode.SnapAndRestore)
                        {
                            ExecuteStepSnapAndRestore(step, speedMultiplier, randomJitterMs, rng, token);
                        }
                        else
                        {
                            ExecuteStepSendInput(step, speedMultiplier, randomJitterMs, rng, token);
                        }
                    }

                    if (cancelled) break;
                }

                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    finishMessage = "Đã dừng kịch bản theo yêu cầu người dùng.";
                }
            }
            catch (Exception ex)
            {
                cancelled = true;
                finishMessage = "Lỗi khi phát: " + ex.Message;
            }
            finally
            {
                ReleaseMacroHeldInputsOnly(mode);
                _isPlaying = false;

                Action<bool, string> finishedHandler = PlaybackFinished;
                if (finishedHandler != null)
                {
                    finishedHandler(cancelled, finishMessage);
                }
            }
        }

        private void ExecuteStepSendInput(MacroStep step, double speedMultiplier, int randomJitterMs, Random rng, CancellationToken token)
        {
            MacroActionType type = step.ParsedActionType;
            int holdMs = CalculateEffectiveDelayMs(step.HoldDurationMs > 0 ? step.HoldDurationMs : 25, speedMultiplier, Math.Min(5, randomJitterMs), rng);

            switch (type)
            {
                case MacroActionType.MouseMove:
                    NativeMethods.SendMouseVirtualDesk(step.X, step.Y, 0);
                    break;

                case MacroActionType.LeftClick:
                    PressMouseButton(HeldMouseButton.Left, step.X, step.Y, NativeMethods.MOUSEEVENTF_LEFTDOWN);
                    WaitResponsive(Math.Max(10, holdMs), token);
                    ReleaseMouseButton(HeldMouseButton.Left, step.X, step.Y, NativeMethods.MOUSEEVENTF_LEFTUP);
                    break;

                case MacroActionType.RightClick:
                    PressMouseButton(HeldMouseButton.Right, step.X, step.Y, NativeMethods.MOUSEEVENTF_RIGHTDOWN);
                    WaitResponsive(Math.Max(10, holdMs), token);
                    ReleaseMouseButton(HeldMouseButton.Right, step.X, step.Y, NativeMethods.MOUSEEVENTF_RIGHTUP);
                    break;

                case MacroActionType.MiddleClick:
                    PressMouseButton(HeldMouseButton.Middle, step.X, step.Y, NativeMethods.MOUSEEVENTF_MIDDLEDOWN);
                    WaitResponsive(Math.Max(10, holdMs), token);
                    ReleaseMouseButton(HeldMouseButton.Middle, step.X, step.Y, NativeMethods.MOUSEEVENTF_MIDDLEUP);
                    break;

                case MacroActionType.LeftDown:
                    PressMouseButton(HeldMouseButton.Left, step.X, step.Y, NativeMethods.MOUSEEVENTF_LEFTDOWN);
                    break;

                case MacroActionType.LeftUp:
                    ReleaseMouseButton(HeldMouseButton.Left, step.X, step.Y, NativeMethods.MOUSEEVENTF_LEFTUP);
                    break;

                case MacroActionType.RightDown:
                    PressMouseButton(HeldMouseButton.Right, step.X, step.Y, NativeMethods.MOUSEEVENTF_RIGHTDOWN);
                    break;

                case MacroActionType.RightUp:
                    ReleaseMouseButton(HeldMouseButton.Right, step.X, step.Y, NativeMethods.MOUSEEVENTF_RIGHTUP);
                    break;

                case MacroActionType.MiddleDown:
                    PressMouseButton(HeldMouseButton.Middle, step.X, step.Y, NativeMethods.MOUSEEVENTF_MIDDLEDOWN);
                    break;

                case MacroActionType.MiddleUp:
                    ReleaseMouseButton(HeldMouseButton.Middle, step.X, step.Y, NativeMethods.MOUSEEVENTF_MIDDLEUP);
                    break;

                case MacroActionType.MouseWheel:
                    NativeMethods.SendMouseVirtualDesk(step.X, step.Y, NativeMethods.MOUSEEVENTF_WHEEL, step.MouseData);
                    break;

                case MacroActionType.KeyPress:
                    if (step.VirtualKeyCode > 0)
                    {
                        ushort vk = (ushort)step.VirtualKeyCode;
                        PressKey(vk);
                        WaitResponsive(Math.Max(10, holdMs), token);
                        ReleaseKey(vk);
                    }
                    break;

                case MacroActionType.KeyDown:
                    if (step.VirtualKeyCode > 0) PressKey((ushort)step.VirtualKeyCode);
                    break;

                case MacroActionType.KeyUp:
                    if (step.VirtualKeyCode > 0) ReleaseKey((ushort)step.VirtualKeyCode);
                    break;

                case MacroActionType.WaitOnly:
                    break;
            }
        }

        /// <summary>
        /// Chế độ Snap & Restore:
        /// Lưu vị trí con trỏ hiện tại của người dùng -> nhảy tới điểm đích thực hiện Click -> trả ngay chuột về vị trí cũ.
        /// Bỏ qua các bước MouseMove trung gian nếu không có nút chuột nào đang giữ (Drag) để không giằng chuột của người dùng.
        /// </summary>
        private void ExecuteStepSnapAndRestore(MacroStep step, double speedMultiplier, int randomJitterMs, Random rng, CancellationToken token)
        {
            MacroActionType type = step.ParsedActionType;

            if (type == MacroActionType.MouseMove)
            {
                bool anyMouseHeld;
                lock (_stateLock)
                {
                    anyMouseHeld = _pressedMouseButtons.Count > 0;
                }
                if (anyMouseHeld)
                {
                    NativeMethods.SendMouseVirtualDesk(step.X, step.Y, 0);
                }
                return;
            }

            if (type == MacroActionType.LeftClick || type == MacroActionType.RightClick ||
                type == MacroActionType.MiddleClick || type == MacroActionType.MouseWheel)
            {
                NativeMethods.POINT savedPt;
                bool hasSaved = NativeMethods.GetCursorPos(out savedPt);

                // Giữ thời gian click ngắn gọn (~15ms) để trả chuột về ngay lập tức
                int snapHoldMs = Math.Min(25, Math.Max(10, step.HoldDurationMs));

                switch (type)
                {
                    case MacroActionType.LeftClick:
                        PressMouseButton(HeldMouseButton.Left, step.X, step.Y, NativeMethods.MOUSEEVENTF_LEFTDOWN);
                        WaitResponsive(snapHoldMs, token);
                        ReleaseMouseButton(HeldMouseButton.Left, step.X, step.Y, NativeMethods.MOUSEEVENTF_LEFTUP);
                        break;

                    case MacroActionType.RightClick:
                        PressMouseButton(HeldMouseButton.Right, step.X, step.Y, NativeMethods.MOUSEEVENTF_RIGHTDOWN);
                        WaitResponsive(snapHoldMs, token);
                        ReleaseMouseButton(HeldMouseButton.Right, step.X, step.Y, NativeMethods.MOUSEEVENTF_RIGHTUP);
                        break;

                    case MacroActionType.MiddleClick:
                        PressMouseButton(HeldMouseButton.Middle, step.X, step.Y, NativeMethods.MOUSEEVENTF_MIDDLEDOWN);
                        WaitResponsive(snapHoldMs, token);
                        ReleaseMouseButton(HeldMouseButton.Middle, step.X, step.Y, NativeMethods.MOUSEEVENTF_MIDDLEUP);
                        break;

                    case MacroActionType.MouseWheel:
                        NativeMethods.SendMouseVirtualDesk(step.X, step.Y, NativeMethods.MOUSEEVENTF_WHEEL, step.MouseData);
                        break;
                }

                if (hasSaved)
                {
                    NativeMethods.SendMouseVirtualDesk(savedPt.x, savedPt.y, 0);
                }
                return;
            }

            // Các bước KeyPress / Down / Up khác thực thi chuẩn
            ExecuteStepSendInput(step, speedMultiplier, randomJitterMs, rng, token);
        }

        /// <summary>
        /// Chế độ Background PostMessage:
        /// Bắn trực tiếp WM_LBUTTONDOWN / WM_LBUTTONUP / WM_KEYDOWN vào HWND tại tọa độ đích mà không di chuyển chuột thật.
        /// </summary>
        private void ExecuteStepPostMessage(MacroStep step, double speedMultiplier, int randomJitterMs, Random rng, CancellationToken token)
        {
            MacroActionType type = step.ParsedActionType;
            int holdMs = CalculateEffectiveDelayMs(step.HoldDurationMs > 0 ? step.HoldDurationMs : 25, speedMultiplier, Math.Min(5, randomJitterMs), rng);

            switch (type)
            {
                case MacroActionType.MouseMove:
                    _lastTargetHwnd = NativeMethods.SendBackgroundMouseMessage(step.X, step.Y, NativeMethods.WM_MOUSEMOVE, 0);
                    _lastTargetX = step.X;
                    _lastTargetY = step.Y;
                    break;

                case MacroActionType.LeftClick:
                    PressMousePostMessage(HeldMouseButton.Left, step.X, step.Y, NativeMethods.WM_LBUTTONDOWN, NativeMethods.MK_LBUTTON);
                    WaitResponsive(Math.Max(10, holdMs), token);
                    ReleaseMousePostMessage(HeldMouseButton.Left, step.X, step.Y, NativeMethods.WM_LBUTTONUP, 0);
                    break;

                case MacroActionType.RightClick:
                    PressMousePostMessage(HeldMouseButton.Right, step.X, step.Y, NativeMethods.WM_RBUTTONDOWN, NativeMethods.MK_RBUTTON);
                    WaitResponsive(Math.Max(10, holdMs), token);
                    ReleaseMousePostMessage(HeldMouseButton.Right, step.X, step.Y, NativeMethods.WM_RBUTTONUP, 0);
                    break;

                case MacroActionType.MiddleClick:
                    PressMousePostMessage(HeldMouseButton.Middle, step.X, step.Y, NativeMethods.WM_MBUTTONDOWN, NativeMethods.MK_MBUTTON);
                    WaitResponsive(Math.Max(10, holdMs), token);
                    ReleaseMousePostMessage(HeldMouseButton.Middle, step.X, step.Y, NativeMethods.WM_MBUTTONUP, 0);
                    break;

                case MacroActionType.LeftDown:
                    PressMousePostMessage(HeldMouseButton.Left, step.X, step.Y, NativeMethods.WM_LBUTTONDOWN, NativeMethods.MK_LBUTTON);
                    break;

                case MacroActionType.LeftUp:
                    ReleaseMousePostMessage(HeldMouseButton.Left, step.X, step.Y, NativeMethods.WM_LBUTTONUP, 0);
                    break;

                case MacroActionType.RightDown:
                    PressMousePostMessage(HeldMouseButton.Right, step.X, step.Y, NativeMethods.WM_RBUTTONDOWN, NativeMethods.MK_RBUTTON);
                    break;

                case MacroActionType.RightUp:
                    ReleaseMousePostMessage(HeldMouseButton.Right, step.X, step.Y, NativeMethods.WM_RBUTTONUP, 0);
                    break;

                case MacroActionType.MiddleDown:
                    PressMousePostMessage(HeldMouseButton.Middle, step.X, step.Y, NativeMethods.WM_MBUTTONDOWN, NativeMethods.MK_MBUTTON);
                    break;

                case MacroActionType.MiddleUp:
                    ReleaseMousePostMessage(HeldMouseButton.Middle, step.X, step.Y, NativeMethods.WM_MBUTTONUP, 0);
                    break;

                case MacroActionType.MouseWheel:
                    _lastTargetHwnd = NativeMethods.SendBackgroundMouseMessage(step.X, step.Y, NativeMethods.WM_MOUSEWHEEL, 0, step.MouseData);
                    break;

                case MacroActionType.KeyPress:
                    if (step.VirtualKeyCode > 0)
                    {
                        ushort vk = (ushort)step.VirtualKeyCode;
                        PressKeyPostMessage(vk);
                        WaitResponsive(Math.Max(10, holdMs), token);
                        ReleaseKeyPostMessage(vk);
                    }
                    break;

                case MacroActionType.KeyDown:
                    if (step.VirtualKeyCode > 0) PressKeyPostMessage((ushort)step.VirtualKeyCode);
                    break;

                case MacroActionType.KeyUp:
                    if (step.VirtualKeyCode > 0) ReleaseKeyPostMessage((ushort)step.VirtualKeyCode);
                    break;

                case MacroActionType.WaitOnly:
                    break;
            }
        }

        private void PressMouseButton(HeldMouseButton button, int x, int y, uint downFlag)
        {
            lock (_stateLock)
            {
                _pressedMouseButtons.Add(button);
            }
            NativeMethods.SendMouseVirtualDesk(x, y, downFlag);
        }

        private void ReleaseMouseButton(HeldMouseButton button, int x, int y, uint upFlag)
        {
            NativeMethods.SendMouseVirtualDesk(x, y, upFlag);
            lock (_stateLock)
            {
                _pressedMouseButtons.Remove(button);
            }
        }

        private void PressMousePostMessage(HeldMouseButton button, int x, int y, uint msg, uint wParamFlags)
        {
            lock (_stateLock)
            {
                _pressedMouseButtons.Add(button);
                _lastTargetX = x;
                _lastTargetY = y;
            }
            _lastTargetHwnd = NativeMethods.SendBackgroundMouseMessage(x, y, msg, wParamFlags);
        }

        private void ReleaseMousePostMessage(HeldMouseButton button, int x, int y, uint msg, uint wParamFlags)
        {
            _lastTargetHwnd = NativeMethods.SendBackgroundMouseMessage(x, y, msg, wParamFlags);
            lock (_stateLock)
            {
                _pressedMouseButtons.Remove(button);
            }
        }

        private void PressKey(ushort vk)
        {
            lock (_stateLock)
            {
                _pressedVirtualKeys.Add(vk);
            }
            NativeMethods.SendKeyInput(vk, false);
        }

        private void ReleaseKey(ushort vk)
        {
            NativeMethods.SendKeyInput(vk, true);
            lock (_stateLock)
            {
                _pressedVirtualKeys.Remove(vk);
            }
        }

        private void PressKeyPostMessage(ushort vk)
        {
            lock (_stateLock)
            {
                _pressedVirtualKeys.Add(vk);
            }
            NativeMethods.SendBackgroundKeyMessage(_lastTargetHwnd, vk, false);
        }

        private void ReleaseKeyPostMessage(ushort vk)
        {
            NativeMethods.SendBackgroundKeyMessage(_lastTargetHwnd, vk, true);
            lock (_stateLock)
            {
                _pressedVirtualKeys.Remove(vk);
            }
        }

        private void ReleaseMacroHeldInputsOnly(PlaybackMode mode)
        {
            List<HeldMouseButton> heldButtons;
            List<ushort> heldKeys;
            int lastX, lastY;
            IntPtr lastHwnd;

            lock (_stateLock)
            {
                heldButtons = new List<HeldMouseButton>(_pressedMouseButtons);
                heldKeys = new List<ushort>(_pressedVirtualKeys);
                lastX = _lastTargetX;
                lastY = _lastTargetY;
                lastHwnd = _lastTargetHwnd;
                _pressedMouseButtons.Clear();
                _pressedVirtualKeys.Clear();
            }

            if (mode == PlaybackMode.BackgroundPostMessage)
            {
                foreach (HeldMouseButton btn in heldButtons)
                {
                    switch (btn)
                    {
                        case HeldMouseButton.Left:
                            NativeMethods.SendBackgroundMouseMessage(lastX, lastY, NativeMethods.WM_LBUTTONUP, 0);
                            break;
                        case HeldMouseButton.Right:
                            NativeMethods.SendBackgroundMouseMessage(lastX, lastY, NativeMethods.WM_RBUTTONUP, 0);
                            break;
                        case HeldMouseButton.Middle:
                            NativeMethods.SendBackgroundMouseMessage(lastX, lastY, NativeMethods.WM_MBUTTONUP, 0);
                            break;
                    }
                }
                foreach (ushort vk in heldKeys)
                {
                    NativeMethods.SendBackgroundKeyMessage(lastHwnd, vk, true);
                }
            }
            else
            {
                foreach (HeldMouseButton btn in heldButtons)
                {
                    switch (btn)
                    {
                        case HeldMouseButton.Left:
                            NativeMethods.SendMouseReleaseOnly(NativeMethods.MOUSEEVENTF_LEFTUP);
                            break;
                        case HeldMouseButton.Right:
                            NativeMethods.SendMouseReleaseOnly(NativeMethods.MOUSEEVENTF_RIGHTUP);
                            break;
                        case HeldMouseButton.Middle:
                            NativeMethods.SendMouseReleaseOnly(NativeMethods.MOUSEEVENTF_MIDDLEUP);
                            break;
                    }
                }
                foreach (ushort vk in heldKeys)
                {
                    NativeMethods.SendKeyInput(vk, true);
                }
            }
        }

        private static bool WaitResponsive(int targetMs, CancellationToken token)
        {
            if (targetMs <= 0) return !token.IsCancellationRequested;

            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                if (token.IsCancellationRequested) return false;

                long remaining = targetMs - sw.ElapsedMilliseconds;
                if (remaining <= 0) return true;

                if (remaining > 20)
                {
                    Thread.Sleep(10);
                }
                else if (remaining > 3)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.Yield();
                }
            }
        }
    }
}
