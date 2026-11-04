using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SwiftMacroPro
{
    public enum MacroActionType
    {
        // Bước nguyên tử (tự động Down -> giữ HoldDurationMs -> Up)
        LeftClick,
        RightClick,
        MiddleClick,
        MouseWheel,
        MouseMove,
        KeyPress,
        WaitOnly,

        // Bước cặp rời (dành cho thao tác kéo thả Drag & Drop hoặc giữ phím)
        LeftDown,
        LeftUp,
        RightDown,
        RightUp,
        MiddleDown,
        MiddleUp,
        KeyDown,
        KeyUp
    }

    public enum PlaybackMode
    {
        SendInput,
        SnapAndRestore,
        BackgroundPostMessage,
        HeadlessChrome
    }

    public class ScreenTopology
    {
        public int VirtualLeft { get; set; }
        public int VirtualTop { get; set; }
        public int VirtualWidth { get; set; }
        public int VirtualHeight { get; set; }
        public int MonitorCount { get; set; }

        public static ScreenTopology CaptureCurrent()
        {
            return new ScreenTopology
            {
                VirtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
                VirtualTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
                VirtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
                VirtualHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN),
                MonitorCount = NativeMethods.GetSystemMetrics(NativeMethods.SM_CMONITORS)
            };
        }

        public bool Matches(ScreenTopology other)
        {
            if (other == null) return true;
            return VirtualLeft == other.VirtualLeft
                && VirtualTop == other.VirtualTop
                && VirtualWidth == other.VirtualWidth
                && VirtualHeight == other.VirtualHeight
                && MonitorCount == other.MonitorCount;
        }

        public override string ToString()
        {
            return string.Format("{0} màn hình | VirtualDesk: ({1}, {2}) {3}x{4}",
                MonitorCount, VirtualLeft, VirtualTop, VirtualWidth, VirtualHeight);
        }
    }

    public class MacroStep
    {
        private int _delayBeforeMs;
        private int _holdDurationMs;

        public int StepId { get; set; }
        public string ActionType { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int MouseData { get; set; }
        public int VirtualKeyCode { get; set; }

        public int DelayBeforeMs
        {
            get { return _delayBeforeMs; }
            set { _delayBeforeMs = Math.Max(0, value); }
        }

        public int HoldDurationMs
        {
            get { return _holdDurationMs; }
            set { _holdDurationMs = Math.Max(0, value); }
        }

        public int? PairStepId { get; set; }

        public MacroStep()
        {
            ActionType = MacroActionType.LeftClick.ToString();
            _delayBeforeMs = 100;
            _holdDurationMs = 35;
        }

        [ScriptIgnore]
        public MacroActionType ParsedActionType
        {
            get
            {
                MacroActionType result;
                if (Enum.TryParse(ActionType, true, out result))
                {
                    return result;
                }
                return MacroActionType.LeftClick;
            }
            set
            {
                ActionType = value.ToString();
            }
        }

        /// <summary>
        /// Thuộc tính gộp cho phép sửa trực tiếp Mã phím (VK) hoặc Độ cuộn chuột (Wheel) ngay trên bảng DataGridView.
        /// </summary>
        [ScriptIgnore]
        public int KeyOrWheelValue
        {
            get
            {
                return ParsedActionType == MacroActionType.MouseWheel ? MouseData : VirtualKeyCode;
            }
            set
            {
                if (ParsedActionType == MacroActionType.MouseWheel)
                {
                    MouseData = value;
                    VirtualKeyCode = 0;
                }
                else
                {
                    VirtualKeyCode = Math.Max(0, value);
                    MouseData = 0;
                }
            }
        }

        [ScriptIgnore]
        public string ExtraInfoDisplay
        {
            get
            {
                List<string> parts = new List<string>();
                MacroActionType t = ParsedActionType;
                if ((t == MacroActionType.KeyPress || t == MacroActionType.KeyDown || t == MacroActionType.KeyUp) && VirtualKeyCode > 0)
                {
                    Keys k = (Keys)VirtualKeyCode;
                    parts.Add(string.Format("Phím: {0} ({1})", k, VirtualKeyCode));
                }
                else if (t == MacroActionType.MouseWheel)
                {
                    parts.Add(string.Format("Cuộn: {0}", MouseData));
                }

                if (PairStepId.HasValue)
                {
                    parts.Add(string.Format("Cặp #{0}", PairStepId.Value));
                }
                return string.Join(" | ", parts.ToArray());
            }
        }

        public MacroStep Clone()
        {
            return new MacroStep
            {
                StepId = this.StepId,
                ActionType = this.ActionType,
                X = this.X,
                Y = this.Y,
                MouseData = this.MouseData,
                VirtualKeyCode = this.VirtualKeyCode,
                DelayBeforeMs = this.DelayBeforeMs,
                HoldDurationMs = this.HoldDurationMs,
                PairStepId = this.PairStepId
            };
        }
    }

    public class MacroProfile
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; }
        public string Name { get; set; }
        public int LoopCount { get; set; }
        public double SpeedMultiplier { get; set; }
        public int RandomJitterMs { get; set; }
        public string PlaybackModeName { get; set; }
        public string BrowserUrl { get; set; }
        public int BrowserOriginX { get; set; }
        public int BrowserOriginY { get; set; }
        public int BrowserViewportWidth { get; set; }
        public int BrowserViewportHeight { get; set; }
        public ScreenTopology ScreenTopology { get; set; }
        public List<MacroStep> Steps { get; set; }

        public MacroProfile()
        {
            SchemaVersion = CurrentSchemaVersion;
            Name = "Kịch bản mới";
            LoopCount = 1;
            SpeedMultiplier = 1.0;
            RandomJitterMs = 0;
            PlaybackModeName = PlaybackMode.SendInput.ToString();
            BrowserUrl = "";
            BrowserViewportWidth = 1280;
            BrowserViewportHeight = 720;
            ScreenTopology = ScreenTopology.CaptureCurrent();
            Steps = new List<MacroStep>();
        }

        [ScriptIgnore]
        public PlaybackMode ParsedPlaybackMode
        {
            get
            {
                PlaybackMode mode;
                if (!string.IsNullOrEmpty(PlaybackModeName) && Enum.TryParse(PlaybackModeName, true, out mode))
                {
                    return mode;
                }
                return PlaybackMode.SendInput;
            }
            set
            {
                PlaybackModeName = value.ToString();
            }
        }
    }

    public static class MacroValidator
    {
        public static bool IsDownAction(MacroActionType type)
        {
            return type == MacroActionType.LeftDown
                || type == MacroActionType.RightDown
                || type == MacroActionType.MiddleDown
                || type == MacroActionType.KeyDown;
        }

        public static bool IsUpAction(MacroActionType type)
        {
            return type == MacroActionType.LeftUp
                || type == MacroActionType.RightUp
                || type == MacroActionType.MiddleUp
                || type == MacroActionType.KeyUp;
        }

        public static MacroActionType? GetCounterpartAction(MacroActionType type)
        {
            switch (type)
            {
                case MacroActionType.LeftDown: return MacroActionType.LeftUp;
                case MacroActionType.LeftUp: return MacroActionType.LeftDown;
                case MacroActionType.RightDown: return MacroActionType.RightUp;
                case MacroActionType.RightUp: return MacroActionType.RightDown;
                case MacroActionType.MiddleDown: return MacroActionType.MiddleUp;
                case MacroActionType.MiddleUp: return MacroActionType.MiddleDown;
                case MacroActionType.KeyDown: return MacroActionType.KeyUp;
                case MacroActionType.KeyUp: return MacroActionType.KeyDown;
                default: return null;
            }
        }

        public static void ReindexSteps(List<MacroStep> steps)
        {
            if (steps == null) return;
            Dictionary<int, int> oldToNewId = new Dictionary<int, int>();
            for (int i = 0; i < steps.Count; i++)
            {
                int newId = i + 1;
                if (steps[i].StepId > 0 && !oldToNewId.ContainsKey(steps[i].StepId))
                {
                    oldToNewId[steps[i].StepId] = newId;
                }
                steps[i].StepId = newId;
            }

            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].PairStepId.HasValue)
                {
                    int mapped;
                    if (oldToNewId.TryGetValue(steps[i].PairStepId.Value, out mapped))
                    {
                        steps[i].PairStepId = mapped;
                    }
                    else
                    {
                        steps[i].PairStepId = null;
                    }
                }
            }
        }

        public static List<string> ValidateAndBindPairs(MacroProfile profile)
        {
            List<string> errors = new List<string>();
            if (profile == null)
            {
                errors.Add("Kịch bản rỗng.");
                return errors;
            }

            if (profile.SchemaVersion <= 0 || profile.SchemaVersion > MacroProfile.CurrentSchemaVersion)
            {
                errors.Add(string.Format("SchemaVersion ({0}) không tương thích (hỗ trợ tối đa v{1}).",
                    profile.SchemaVersion, MacroProfile.CurrentSchemaVersion));
            }

            if (profile.SpeedMultiplier <= 0)
            {
                errors.Add("Hệ số tốc độ (SpeedMultiplier) phải lớn hơn 0.");
            }

            if (profile.RandomJitterMs < 0)
            {
                errors.Add("Độ trễ ngẫu nhiên (RandomJitterMs) không được âm.");
            }

            if (profile.Steps == null || profile.Steps.Count == 0)
            {
                return errors;
            }

            ReindexSteps(profile.Steps);

            MacroStep openLeftDown = null;
            MacroStep openRightDown = null;
            MacroStep openMiddleDown = null;
            Dictionary<int, MacroStep> openKeysDown = new Dictionary<int, MacroStep>();

            foreach (MacroStep step in profile.Steps)
            {
                if (step.DelayBeforeMs < 0)
                {
                    errors.Add(string.Format("Bước #{0}: DelayBeforeMs ({1} ms) không được âm.", step.StepId, step.DelayBeforeMs));
                }
                if (step.HoldDurationMs < 0)
                {
                    errors.Add(string.Format("Bước #{0}: HoldDurationMs ({1} ms) không được âm.", step.StepId, step.HoldDurationMs));
                }

                MacroActionType type = step.ParsedActionType;
                switch (type)
                {
                    case MacroActionType.LeftDown:
                        if (openLeftDown != null)
                        {
                            errors.Add(string.Format("Bước #{0}: LeftDown bị lặp khi Bước #{1} chưa có LeftUp.", step.StepId, openLeftDown.StepId));
                        }
                        openLeftDown = step;
                        break;

                    case MacroActionType.LeftUp:
                        if (openLeftDown == null)
                        {
                            errors.Add(string.Format("Bước #{0}: LeftUp mồ côi (không có LeftDown trước đó).", step.StepId));
                        }
                        else
                        {
                            openLeftDown.PairStepId = step.StepId;
                            step.PairStepId = openLeftDown.StepId;
                            openLeftDown = null;
                        }
                        break;

                    case MacroActionType.RightDown:
                        if (openRightDown != null)
                        {
                            errors.Add(string.Format("Bước #{0}: RightDown bị lặp khi Bước #{1} chưa có RightUp.", step.StepId, openRightDown.StepId));
                        }
                        openRightDown = step;
                        break;

                    case MacroActionType.RightUp:
                        if (openRightDown == null)
                        {
                            errors.Add(string.Format("Bước #{0}: RightUp mồ côi (không có RightDown trước đó).", step.StepId));
                        }
                        else
                        {
                            openRightDown.PairStepId = step.StepId;
                            step.PairStepId = openRightDown.StepId;
                            openRightDown = null;
                        }
                        break;

                    case MacroActionType.MiddleDown:
                        if (openMiddleDown != null)
                        {
                            errors.Add(string.Format("Bước #{0}: MiddleDown bị lặp khi Bước #{1} chưa có MiddleUp.", step.StepId, openMiddleDown.StepId));
                        }
                        openMiddleDown = step;
                        break;

                    case MacroActionType.MiddleUp:
                        if (openMiddleDown == null)
                        {
                            errors.Add(string.Format("Bước #{0}: MiddleUp mồ côi (không có MiddleDown trước đó).", step.StepId));
                        }
                        else
                        {
                            openMiddleDown.PairStepId = step.StepId;
                            step.PairStepId = openMiddleDown.StepId;
                            openMiddleDown = null;
                        }
                        break;

                    case MacroActionType.KeyDown:
                        if (openKeysDown.ContainsKey(step.VirtualKeyCode))
                        {
                            errors.Add(string.Format("Bước #{0}: KeyDown ({1}) lặp khi Bước #{2} chưa có KeyUp.",
                                step.StepId, (Keys)step.VirtualKeyCode, openKeysDown[step.VirtualKeyCode].StepId));
                        }
                        openKeysDown[step.VirtualKeyCode] = step;
                        break;

                    case MacroActionType.KeyUp:
                        MacroStep downStep;
                        if (!openKeysDown.TryGetValue(step.VirtualKeyCode, out downStep))
                        {
                            errors.Add(string.Format("Bước #{0}: KeyUp ({1}) mồ côi (không có KeyDown trước đó).",
                                step.StepId, (Keys)step.VirtualKeyCode));
                        }
                        else
                        {
                            downStep.PairStepId = step.StepId;
                            step.PairStepId = downStep.StepId;
                            openKeysDown.Remove(step.VirtualKeyCode);
                        }
                        break;

                    default:
                        step.PairStepId = null;
                        break;
                }
            }

            if (openLeftDown != null)
                errors.Add(string.Format("Bước #{0} (LeftDown) chưa có bước LeftUp đóng lại trước khi kết thúc kịch bản.", openLeftDown.StepId));
            if (openRightDown != null)
                errors.Add(string.Format("Bước #{0} (RightDown) chưa có bước RightUp đóng lại trước khi kết thúc kịch bản.", openRightDown.StepId));
            if (openMiddleDown != null)
                errors.Add(string.Format("Bước #{0} (MiddleDown) chưa có bước MiddleUp đóng lại trước khi kết thúc kịch bản.", openMiddleDown.StepId));

            foreach (KeyValuePair<int, MacroStep> kvp in openKeysDown)
            {
                errors.Add(string.Format("Bước #{0} (KeyDown {1}) chưa có bước KeyUp đóng lại trước khi kết thúc kịch bản.",
                    kvp.Value.StepId, (Keys)kvp.Key));
            }

            return errors;
        }
    }

    public static class MacroStorage
    {
        public static void SaveToFile(string filePath, MacroProfile profile)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            string rawJson = serializer.Serialize(profile);
            string formatted = FormatJson(rawJson);
            File.WriteAllText(filePath, formatted, new UTF8Encoding(false));
        }

        public static MacroProfile LoadFromFile(string filePath)
        {
            string json = File.ReadAllText(filePath, Encoding.UTF8);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            MacroProfile profile = serializer.Deserialize<MacroProfile>(json);
            if (profile == null)
            {
                throw new InvalidDataException("Không thể đọc dữ liệu JSON từ file.");
            }
            if (profile.Steps == null)
            {
                profile.Steps = new List<MacroStep>();
            }
            if (profile.SpeedMultiplier <= 0)
            {
                profile.SpeedMultiplier = 1.0;
            }
            if (profile.RandomJitterMs < 0)
            {
                profile.RandomJitterMs = 0;
            }
            if (string.IsNullOrEmpty(profile.PlaybackModeName))
            {
                profile.PlaybackModeName = PlaybackMode.SendInput.ToString();
            }
            return profile;
        }

        private static string FormatJson(string json)
        {
            StringBuilder sb = new StringBuilder();
            bool inQuotes = false;
            bool escaped = false;
            int indent = 0;

            foreach (char ch in json)
            {
                if (escaped)
                {
                    sb.Append(ch);
                    escaped = false;
                    continue;
                }
                if (ch == '\\' && inQuotes)
                {
                    sb.Append(ch);
                    escaped = true;
                    continue;
                }
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    sb.Append(ch);
                    continue;
                }
                if (!inQuotes)
                {
                    switch (ch)
                    {
                        case '{':
                        case '[':
                            sb.Append(ch);
                            sb.AppendLine();
                            indent++;
                            sb.Append(new string(' ', indent * 2));
                            break;
                        case '}':
                        case ']':
                            sb.AppendLine();
                            indent = Math.Max(0, indent - 1);
                            sb.Append(new string(' ', indent * 2));
                            sb.Append(ch);
                            break;
                        case ',':
                            sb.Append(ch);
                            sb.AppendLine();
                            sb.Append(new string(' ', indent * 2));
                            break;
                        case ':':
                            sb.Append(": ");
                            break;
                        default:
                            sb.Append(ch);
                            break;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
    }
}
