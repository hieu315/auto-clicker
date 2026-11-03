using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SwiftMacroPro
{
    public class MainForm : Form
    {
        private const int HOTKEY_ID_RECORD = 1001;
        private const int HOTKEY_ID_PLAY = 1002;
        private const int HOTKEY_ID_STOP = 1003;

        private readonly MacroRecorder _recorder = new MacroRecorder();
        private readonly MacroPlayer _player = new MacroPlayer();
        private MacroProfile _currentProfile = new MacroProfile();
        private BindingList<MacroStep> _stepsBinding;

        // Điều khiển chính
        private Button _btnRecord;
        private Button _btnPlay;
        private Button _btnStop;
        private Button _btnValidate;
        private Label _lblStatusBadge;
        private Label _lblScreenInfo;
        private Label _lblCursorLive;
        private System.Windows.Forms.Timer _cursorTimer;

        // Tùy chọn Ghi & Phát
        private CheckBox _chkRecordMove;
        private CheckBox _chkRecordKeys;
        private CheckBox _chkMinimizeOnRun;
        private CheckBox _chkAlwaysOnTop;
        private ComboBox _cboPlaybackMode;
        private NumericUpDown _numLoopCount;
        private NumericUpDown _numSpeed;
        private NumericUpDown _numJitter;

        // Cấu hình Phím tắt
        private ComboBox _cboModRecord, _cboKeyRecord;
        private ComboBox _cboModPlay, _cboKeyPlay;
        private ComboBox _cboModStop, _cboKeyStop;
        private Button _btnApplyHotkeys;
        private Label _lblHotkeyStatus;

        // Bảng bước (Sửa trực tiếp trên bảng - Inline Editing)
        private DataGridView _gridSteps;
        private MacroActionType _editingCellOldActionType;
        private bool _suppressCellSync;

        // Thanh công cụ thêm nhanh & thao tác hàng loạt phía dưới
        private ComboBox _cboQuickAction;
        private NumericUpDown _numQuickX, _numQuickY, _numQuickDelay, _numBatchDelay;
        private Button _btnAddStep, _btnApplyBatchDelay, _btnClearSteps;
        private Button _btnSaveJson, _btnLoadJson;

        public MainForm()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = "SwiftMacro Pro — High-Precision Macro Recorder & Web Automation";
            ClientSize = new Size(1240, 780);
            MinimumSize = new Size(1060, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            Padding = new Padding(8);

            InitializeLayout();
            BindProfileToGrid();

            _recorder.StepRecorded += OnStepRecordedFromHook;
            _player.ProgressChanged += OnPlayerProgressChanged;
            _player.PlaybackFinished += OnPlayerFinished;

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            _cursorTimer = new System.Windows.Forms.Timer { Interval = 60 };
            _cursorTimer.Tick += (s, e) =>
            {
                Point pt = Cursor.Position;
                _lblCursorLive.Text = string.Format("Con trỏ: X = {0}, Y = {1}", pt.X, pt.Y);
            };
            _cursorTimer.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterConfiguredHotkeys();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cursorTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            UnregisterAllHotkeys();
            if (_recorder.IsRecording) _recorder.Stop();
            if (_player.IsPlaying) _player.Stop();
            _recorder.Dispose();
            base.OnFormClosing(e);
        }

        private void InitializeLayout()
        {
            // Bố cục: Đặt các GroupBox mỏng gọn ở Top và Bottom, dành tối đa diện tích ở giữa (DockStyle.Fill) cho bảng DataGridView
            // Lưu ý trong WinForms: Không dùng FlowLayoutPanel WrapContents=true bên trong GroupBox AutoSize vì sẽ làm phình chiều cao GroupBox!

            // =========================================================================
            // 1. Nhóm Điều khiển chính & Trạng thái (Gọn 1 hàng nút + 1 dòng thông tin)
            // =========================================================================
            GroupBox grpControl = new GroupBox
            {
                Text = "Bảng Điều Khiển && Trạng Thái",
                Dock = DockStyle.Top,
                Height = 86,
                Padding = new Padding(10, 4, 10, 4)
            };

            FlowLayoutPanel flpTopRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                WrapContents = false,
                AutoSize = false,
                Margin = new Padding(0)
            };

            _btnRecord = CreateActionButton("● Ghi Macro (Record)", Color.FromArgb(220, 53, 69), Color.White, true);
            _btnRecord.Click += (s, e) => ToggleRecording();

            _btnPlay = CreateActionButton("▶ Phát lại (Play)", Color.FromArgb(25, 135, 84), Color.White, true);
            _btnPlay.Click += (s, e) => TogglePlayback();

            _btnStop = CreateActionButton("■ Dừng (Stop)", Color.FromArgb(108, 117, 125), Color.White, true);
            _btnStop.Enabled = false;
            _btnStop.Click += (s, e) => StopAllActiveOperations();

            _btnValidate = CreateActionButton("✓ Kiểm tra Kịch bản", SystemColors.Control, SystemColors.ControlText, false);
            _btnValidate.Click += (s, e) => RunManualValidation();

            _lblStatusBadge = new Label
            {
                Text = "TRẠNG THÁI: SẴN SÀNG (IDLE)",
                AutoSize = true,
                UseMnemonic = false,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(13, 110, 253),
                Margin = new Padding(12, 8, 16, 0)
            };

            _lblCursorLive = new Label
            {
                Text = "Con trỏ: X = 0, Y = 0",
                AutoSize = true,
                UseMnemonic = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.DarkSlateGray,
                Margin = new Padding(0, 9, 0, 0)
            };

            flpTopRow.Controls.AddRange(new Control[]
            {
                _btnRecord, _btnPlay, _btnStop, _btnValidate, _lblStatusBadge, _lblCursorLive
            });

            _lblScreenInfo = new Label
            {
                Text = "Màn hình: " + ScreenTopology.CaptureCurrent() + "   |   💡 Mẹo: Click trực tiếp vào từng ô trên bảng bên dưới để sửa nhanh thông số!",
                Dock = DockStyle.Bottom,
                Height = 20,
                UseMnemonic = false,
                ForeColor = Color.DimGray
            };

            grpControl.Controls.Add(_lblScreenInfo);
            grpControl.Controls.Add(flpTopRow);

            // =========================================================================
            // 2. Nhóm Cấu hình Chế độ phát, Vòng lặp & Phím tắt (Gọn trong 2 dòng)
            // =========================================================================
            GroupBox grpSettings = new GroupBox
            {
                Text = "Cấu hình Chế độ Phát (Web / Không chiếm chuột), Vòng lặp && Phím tắt Toàn cục",
                Dock = DockStyle.Top,
                Height = 96,
                Padding = new Padding(10, 4, 10, 4)
            };

            // Dòng 1 của Settings: Chế độ phát + Tùy chọn CheckBox
            FlowLayoutPanel flpSettingsRow1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                WrapContents = false,
                AutoSize = false,
                Margin = new Padding(0)
            };

            Label lblMode = CreateInlineLabel("Chế độ phát:");
            lblMode.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _cboPlaybackMode = new ComboBox
            {
                Width = 315,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 3, 18, 2)
            };
            _cboPlaybackMode.Items.Add("1. SendInput (Mặc định - Chuẩn OS)");
            _cboPlaybackMode.Items.Add("2. Snap & Restore (Mượn & Trả chuột nhanh - Tối ưu Web)");
            _cboPlaybackMode.Items.Add("3. PostMessage (Chạy ngầm Cửa sổ HWND)");
            _cboPlaybackMode.SelectedIndex = 1;

            _chkRecordMove = new CheckBox { Text = "Ghi đường di chuột", AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 6, 16, 2) };
            _chkRecordKeys = new CheckBox { Text = "Ghi bàn phím", AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 6, 16, 2) };
            _chkMinimizeOnRun = new CheckBox { Text = "Thu nhỏ khi chạy", AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 6, 16, 2) };
            _chkAlwaysOnTop = new CheckBox { Text = "Luôn nổi (TopMost)", AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 6, 0, 2) };
            _chkAlwaysOnTop.CheckedChanged += (s, e) => { TopMost = _chkAlwaysOnTop.Checked; };

            flpSettingsRow1.Controls.AddRange(new Control[]
            {
                lblMode, _cboPlaybackMode,
                _chkRecordMove, _chkRecordKeys, _chkMinimizeOnRun, _chkAlwaysOnTop
            });

            // Dòng 2 của Settings: Vòng lặp + Tốc độ + Jitter + Phím tắt trên cùng 1 hàng ngang gọn gàng
            FlowLayoutPanel flpSettingsRow2 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                WrapContents = false,
                AutoSize = false,
                Margin = new Padding(0)
            };

            Label lblLoop = CreateInlineLabel("Vòng lặp (0=∞):");
            _numLoopCount = new NumericUpDown
            {
                Width = 65,
                Minimum = 0,
                Maximum = 100000,
                Value = 1,
                Margin = new Padding(0, 4, 14, 2)
            };

            Label lblSpeed = CreateInlineLabel("Tốc độ (x):");
            _numSpeed = new NumericUpDown
            {
                Width = 62,
                DecimalPlaces = 2,
                Increment = 0.25M,
                Minimum = 0.10M,
                Maximum = 20.00M,
                Value = 1.00M,
                Margin = new Padding(0, 4, 14, 2)
            };

            Label lblJitter = CreateInlineLabel("Jitter (±ms):");
            _numJitter = new NumericUpDown
            {
                Width = 60,
                Minimum = 0,
                Maximum = 5000,
                Value = 5,
                Margin = new Padding(0, 4, 18, 2)
            };

            Label lblHkRec = CreateInlineLabel("Ghi:");
            _cboModRecord = CreateModifierCombo("Alt");
            _cboKeyRecord = CreateKeyCombo("F8");

            Label lblHkPlay = CreateInlineLabel("Phát:");
            _cboModPlay = CreateModifierCombo("Alt");
            _cboKeyPlay = CreateKeyCombo("F9");

            Label lblHkStop = CreateInlineLabel("Dừng:");
            _cboModStop = CreateModifierCombo("Alt");
            _cboKeyStop = CreateKeyCombo("F10");

            _btnApplyHotkeys = new Button
            {
                Text = "Áp dụng Hotkey",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 2, 6, 2),
                Margin = new Padding(2, 2, 8, 2)
            };
            _btnApplyHotkeys.Click += (s, e) => RegisterConfiguredHotkeys();

            _lblHotkeyStatus = new Label
            {
                Text = "✓ Hotkey OK",
                AutoSize = true,
                UseMnemonic = false,
                ForeColor = Color.SeaGreen,
                Margin = new Padding(0, 7, 0, 2)
            };

            flpSettingsRow2.Controls.AddRange(new Control[]
            {
                lblLoop, _numLoopCount,
                lblSpeed, _numSpeed,
                lblJitter, _numJitter,
                lblHkRec, _cboModRecord, _cboKeyRecord,
                lblHkPlay, _cboModPlay, _cboKeyPlay,
                lblHkStop, _cboModStop, _cboKeyStop,
                _btnApplyHotkeys, _lblHotkeyStatus
            });

            grpSettings.Controls.Add(flpSettingsRow2);
            grpSettings.Controls.Add(flpSettingsRow1);

            // =========================================================================
            // 3. Thanh công cụ phía dưới (Gọn 1 dòng duy nhất - Height = 64)
            // =========================================================================
            GroupBox grpEditor = new GroupBox
            {
                Text = "Thêm Bước Nhanh, Chỉnh Delay Hàng Loạt && Lưu trữ Kịch bản (.json)",
                Dock = DockStyle.Bottom,
                Height = 66,
                Padding = new Padding(10, 4, 10, 4)
            };

            Panel pnlBottomContent = new Panel
            {
                Dock = DockStyle.Fill
            };

            FlowLayoutPanel flpLeftTools = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                AutoSize = false,
                Margin = new Padding(0)
            };

            Label lblAct = CreateInlineLabel("Loại:");
            _cboQuickAction = new ComboBox
            {
                Width = 110,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 5, 8, 2)
            };
            foreach (string name in Enum.GetNames(typeof(MacroActionType)))
            {
                _cboQuickAction.Items.Add(name);
            }
            _cboQuickAction.SelectedIndex = 0;

            Label lblX = CreateInlineLabel("X:");
            _numQuickX = new NumericUpDown
            {
                Width = 68,
                Minimum = -32768,
                Maximum = 32768,
                Value = 500,
                Margin = new Padding(0, 5, 8, 2)
            };

            Label lblY = CreateInlineLabel("Y:");
            _numQuickY = new NumericUpDown
            {
                Width = 68,
                Minimum = -32768,
                Maximum = 32768,
                Value = 500,
                Margin = new Padding(0, 5, 8, 2)
            };

            Label lblDelay = CreateInlineLabel("Delay (ms):");
            _numQuickDelay = new NumericUpDown
            {
                Width = 75,
                Minimum = 0,
                Maximum = 3600000,
                Value = 200,
                Margin = new Padding(0, 5, 8, 2)
            };

            _btnAddStep = CreateEditorButton("+ Thêm bước");
            _btnAddStep.Click += (s, e) => AddQuickStep();

            Label lblBatch = CreateInlineLabel("|  Đặt tất cả Delay (ms):");
            _numBatchDelay = new NumericUpDown
            {
                Width = 75,
                Minimum = 0,
                Maximum = 3600000,
                Value = 150,
                Margin = new Padding(0, 5, 6, 2)
            };

            _btnApplyBatchDelay = CreateEditorButton("⚡ Gán hàng loạt");
            _btnApplyBatchDelay.Click += (s, e) => ApplyBatchDelayToAllSteps();

            _btnClearSteps = CreateEditorButton("✕ Xóa tất cả");
            _btnClearSteps.Click += (s, e) => ClearAllSteps();

            flpLeftTools.Controls.AddRange(new Control[]
            {
                lblAct, _cboQuickAction,
                lblX, _numQuickX,
                lblY, _numQuickY,
                lblDelay, _numQuickDelay,
                _btnAddStep,
                lblBatch, _numBatchDelay, _btnApplyBatchDelay,
                _btnClearSteps
            });

            FlowLayoutPanel flpJsonActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _btnSaveJson = CreateActionButton("💾 Lưu JSON...", Color.FromArgb(13, 110, 253), Color.White, false);
            _btnSaveJson.Click += (s, e) => SaveMacroToJsonDialog();

            _btnLoadJson = CreateEditorButton("📂 Mở JSON...");
            _btnLoadJson.Margin = new Padding(0, 2, 0, 2);
            _btnLoadJson.Click += (s, e) => LoadMacroFromJsonDialog();

            flpJsonActions.Controls.AddRange(new Control[] { _btnSaveJson, _btnLoadJson });

            pnlBottomContent.Controls.Add(flpLeftTools);
            pnlBottomContent.Controls.Add(flpJsonActions);
            grpEditor.Controls.Add(pnlBottomContent);

            // =========================================================================
            // 4. Bảng DataGridView: CHIẾM TOÀN BỘ KHÔNG GIAN GIỮA (~500px -> Hiển thị 15+ dòng)
            // =========================================================================
            Panel pnlGridContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 6, 0, 6)
            };

            _gridSteps = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                EditMode = DataGridViewEditMode.EditOnEnter,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing,
                ColumnHeadersHeight = 36
            };
            _gridSteps.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _gridSteps.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _gridSteps.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _gridSteps.RowTemplate.Height = 28;

            DataGridViewTextBoxColumn colStepId = new DataGridViewTextBoxColumn
            {
                Name = "colStepId",
                DataPropertyName = "StepId",
                HeaderText = "STT (#)",
                ReadOnly = true,
                FillWeight = 7F,
                MinimumWidth = 60
            };
            colStepId.DefaultCellStyle.BackColor = Color.FromArgb(245, 247, 250);

            DataGridViewComboBoxColumn colActionType = new DataGridViewComboBoxColumn
            {
                Name = "colActionType",
                DataPropertyName = "ActionType",
                HeaderText = "Loại thao tác (Click để đổi)",
                FlatStyle = FlatStyle.Popup,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
                FillWeight = 16F,
                MinimumWidth = 145
            };
            foreach (string actionName in Enum.GetNames(typeof(MacroActionType)))
            {
                colActionType.Items.Add(actionName);
            }

            DataGridViewTextBoxColumn colX = new DataGridViewTextBoxColumn
            {
                Name = "colX",
                DataPropertyName = "X",
                HeaderText = "Tọa độ X ✎",
                FillWeight = 10F,
                MinimumWidth = 85
            };

            DataGridViewTextBoxColumn colY = new DataGridViewTextBoxColumn
            {
                Name = "colY",
                DataPropertyName = "Y",
                HeaderText = "Tọa độ Y ✎",
                FillWeight = 10F,
                MinimumWidth = 85
            };

            DataGridViewTextBoxColumn colDelay = new DataGridViewTextBoxColumn
            {
                Name = "colDelay",
                DataPropertyName = "DelayBeforeMs",
                HeaderText = "Chờ trước bước (ms) ✎",
                FillWeight = 15F,
                MinimumWidth = 135
            };

            DataGridViewTextBoxColumn colHold = new DataGridViewTextBoxColumn
            {
                Name = "colHold",
                DataPropertyName = "HoldDurationMs",
                HeaderText = "Thời gian giữ (ms) ✎",
                FillWeight = 14F,
                MinimumWidth = 130
            };

            DataGridViewTextBoxColumn colKeyOrWheel = new DataGridViewTextBoxColumn
            {
                Name = "colKeyOrWheel",
                DataPropertyName = "KeyOrWheelValue",
                HeaderText = "Mã VK / Wheel ✎",
                FillWeight = 11F,
                MinimumWidth = 110
            };

            DataGridViewTextBoxColumn colInfo = new DataGridViewTextBoxColumn
            {
                Name = "colInfo",
                DataPropertyName = "ExtraInfoDisplay",
                HeaderText = "Chi tiết Phím / Cặp Down-Up",
                ReadOnly = true,
                FillWeight = 16F,
                MinimumWidth = 140
            };
            colInfo.DefaultCellStyle.BackColor = Color.FromArgb(245, 247, 250);

            DataGridViewButtonColumn colDeleteBtn = new DataGridViewButtonColumn
            {
                Name = "colDeleteBtn",
                HeaderText = "Xóa",
                Text = "✕ Xóa",
                UseColumnTextForButtonValue = true,
                FillWeight = 7F,
                MinimumWidth = 65
            };

            _gridSteps.Columns.AddRange(new DataGridViewColumn[]
            {
                colStepId, colActionType, colX, colY, colDelay, colHold, colKeyOrWheel, colInfo, colDeleteBtn
            });

            _gridSteps.CellBeginEdit += OnGridCellBeginEdit;
            _gridSteps.CurrentCellDirtyStateChanged += OnGridCurrentCellDirtyStateChanged;
            _gridSteps.CellValueChanged += OnGridCellValueChanged;
            _gridSteps.CellValidating += OnGridCellValidating;
            _gridSteps.DataError += OnGridDataError;
            _gridSteps.CellContentClick += OnGridCellContentClick;

            pnlGridContainer.Controls.Add(_gridSteps);

            // Thứ tự Dock trong WinForms: Thêm Fill trước, sau đó thêm Bottom và Top để phần Fill chiếm trọn diện tích giữa
            Controls.Add(pnlGridContainer);
            Controls.Add(grpEditor);
            Controls.Add(grpSettings);
            Controls.Add(grpControl);
        }

        private static Button CreateActionButton(string text, Color backColor, Color foreColor, bool bold)
        {
            Button btn = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                UseMnemonic = false,
                Padding = new Padding(10, 4, 10, 4),
                Margin = new Padding(0, 2, 8, 2),
                Font = new Font("Segoe UI", 9F, bold ? FontStyle.Bold : FontStyle.Regular)
            };
            if (backColor != SystemColors.Control)
            {
                btn.BackColor = backColor;
                btn.ForeColor = foreColor;
                btn.FlatStyle = FlatStyle.Flat;
            }
            return btn;
        }

        private static Button CreateEditorButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                UseMnemonic = false,
                Padding = new Padding(8, 3, 8, 3),
                Margin = new Padding(0, 2, 6, 2)
            };
        }

        private static Label CreateInlineLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                UseMnemonic = false,
                Margin = new Padding(0, 7, 3, 2)
            };
        }

        private ComboBox CreateModifierCombo(string defaultMod)
        {
            ComboBox cbo = new ComboBox
            {
                Width = 76,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 3, 3, 2)
            };
            cbo.Items.AddRange(new object[] { "Alt", "Ctrl", "Shift", "Ctrl+Alt", "Ctrl+Shift" });
            cbo.SelectedItem = defaultMod;
            return cbo;
        }

        private ComboBox CreateKeyCombo(string defaultKey)
        {
            ComboBox cbo = new ComboBox
            {
                Width = 68,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 3, 12, 2)
            };
            cbo.Items.AddRange(new object[]
            {
                "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
                "Escape", "Home", "End", "Insert", "Delete", "Pause"
            });
            cbo.SelectedItem = defaultKey;
            return cbo;
        }

        private void BindProfileToGrid()
        {
            MacroValidator.ValidateAndBindPairs(_currentProfile);
            _stepsBinding = new BindingList<MacroStep>(_currentProfile.Steps);
            _gridSteps.DataSource = _stepsBinding;
        }

        // ==================== XỬ LÝ SỬA TRỰC TIẾP TRÊN BẢNG (INLINE TABLE EDITING) ====================

        private void OnGridCellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _currentProfile.Steps.Count) return;
            MacroStep step = _currentProfile.Steps[e.RowIndex];
            _editingCellOldActionType = step.ParsedActionType;
        }

        private void OnGridCurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (_gridSteps.IsCurrentCellDirty && _gridSteps.CurrentCell is DataGridViewComboBoxCell)
            {
                _gridSteps.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void OnGridCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string colName = _gridSteps.Columns[e.ColumnIndex].Name;

            if (colName == "colX" || colName == "colY" || colName == "colDelay" || colName == "colHold" || colName == "colKeyOrWheel")
            {
                string input = Convert.ToString(e.FormattedValue);
                int parsed;
                if (!int.TryParse(input, out parsed))
                {
                    MessageBox.Show("Vui lòng nhập một số nguyên hợp lệ vào ô này.", "Dữ liệu không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                    return;
                }

                if ((colName == "colDelay" || colName == "colHold") && parsed < 0)
                {
                    MessageBox.Show("Thời gian chờ / giữ (ms) không được là số âm.", "Dữ liệu không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            }
        }

        private void OnGridDataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private void OnGridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppressCellSync || e.RowIndex < 0 || e.RowIndex >= _currentProfile.Steps.Count) return;

            _suppressCellSync = true;
            try
            {
                MacroStep step = _currentProfile.Steps[e.RowIndex];
                string colName = _gridSteps.Columns[e.ColumnIndex].Name;

                if (colName == "colActionType")
                {
                    MacroActionType newType = step.ParsedActionType;
                    if (step.PairStepId.HasValue && _editingCellOldActionType != newType)
                    {
                        MacroStep paired = FindStepById(step.PairStepId.Value);
                        if (paired != null)
                        {
                            MacroActionType? expectedCounterpart = MacroValidator.GetCounterpartAction(newType);
                            if (expectedCounterpart.HasValue)
                            {
                                paired.ActionType = expectedCounterpart.Value.ToString();
                                paired.VirtualKeyCode = step.VirtualKeyCode;
                            }
                            else
                            {
                                DialogResult dr = MessageBox.Show(
                                    string.Format("Bạn vừa đổi Bước #{0} từ '{1}' sang '{2}'. Bạn có muốn xóa bước đối ứng #{3} ({4}) để không bị thừa lệnh nhả/nhấn không?",
                                        step.StepId, _editingCellOldActionType, newType, paired.StepId, paired.ActionType),
                                    "Đồng bộ cặp Down/Up",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Question);
                                if (dr == DialogResult.Yes)
                                {
                                    _currentProfile.Steps.Remove(paired);
                                }
                                step.PairStepId = null;
                            }
                        }
                    }
                }
                else if (colName == "colKeyOrWheel" && step.PairStepId.HasValue)
                {
                    MacroStep paired = FindStepById(step.PairStepId.Value);
                    if (paired != null)
                    {
                        paired.VirtualKeyCode = step.VirtualKeyCode;
                    }
                }

                MacroValidator.ValidateAndBindPairs(_currentProfile);
                _gridSteps.Invalidate();
            }
            finally
            {
                _suppressCellSync = false;
            }
        }

        private void OnGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _currentProfile.Steps.Count) return;
            if (_gridSteps.Columns[e.ColumnIndex].Name == "colDeleteBtn")
            {
                if (_gridSteps.IsCurrentCellInEditMode)
                {
                    _gridSteps.EndEdit();
                }
                MacroStep step = _currentProfile.Steps[e.RowIndex];
                DeleteStepWithPairCheck(step);
            }
        }

        // ==================== QUẢN LÝ PHÍM TẮT (HOTKEYS) ====================

        private void RegisterConfiguredHotkeys()
        {
            if (!IsHandleCreated) return;
            UnregisterAllHotkeys();

            Keys vkRec, vkPlay, vkStop;
            uint modRec = ParseModifier(_cboModRecord.Text);
            uint modPlay = ParseModifier(_cboModPlay.Text);
            uint modStop = ParseModifier(_cboModStop.Text);

            Enum.TryParse(_cboKeyRecord.Text, true, out vkRec);
            Enum.TryParse(_cboKeyPlay.Text, true, out vkPlay);
            Enum.TryParse(_cboKeyStop.Text, true, out vkStop);

            _recorder.SetIgnoredHotkeyVkCodes(new int[] { (int)vkRec, (int)vkPlay, (int)vkStop });

            List<string> failed = new List<string>();

            if (!NativeMethods.RegisterHotKey(Handle, HOTKEY_ID_RECORD, modRec | NativeMethods.MOD_NOREPEAT, (uint)vkRec))
            {
                int err = Marshal.GetLastWin32Error();
                failed.Add(string.Format("Ghi ({0}+{1}, mã lỗi {2})", _cboModRecord.Text, _cboKeyRecord.Text, err));
            }

            if (!NativeMethods.RegisterHotKey(Handle, HOTKEY_ID_PLAY, modPlay | NativeMethods.MOD_NOREPEAT, (uint)vkPlay))
            {
                int err = Marshal.GetLastWin32Error();
                failed.Add(string.Format("Phát ({0}+{1}, mã lỗi {2})", _cboModPlay.Text, _cboKeyPlay.Text, err));
            }

            if (!NativeMethods.RegisterHotKey(Handle, HOTKEY_ID_STOP, modStop | NativeMethods.MOD_NOREPEAT, (uint)vkStop))
            {
                int err = Marshal.GetLastWin32Error();
                failed.Add(string.Format("Dừng ({0}+{1}, mã lỗi {2})", _cboModStop.Text, _cboKeyStop.Text, err));
            }

            if (failed.Count == 0)
            {
                _lblHotkeyStatus.Text = "✓ Hotkey OK";
                _lblHotkeyStatus.ForeColor = Color.SeaGreen;
            }
            else
            {
                _lblHotkeyStatus.Text = "⚠ Trùng Hotkey!";
                _lblHotkeyStatus.ForeColor = Color.Crimson;
                MessageBox.Show(
                    "Không thể đăng ký tổ hợp phím tắt sau (có thể đã bị ứng dụng khác hoặc hệ điều hành chiếm dụng):\n\n- "
                    + string.Join("\n- ", failed.ToArray())
                    + "\n\nVui lòng chọn tổ hợp phím khác rồi bấm 'Áp dụng Hotkey'.",
                    "Cảnh báo trùng Phím tắt (RegisterHotKey)",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void UnregisterAllHotkeys()
        {
            if (!IsHandleCreated) return;
            NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_RECORD);
            NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_PLAY);
            NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_STOP);
        }

        private static uint ParseModifier(string text)
        {
            switch (text)
            {
                case "Alt": return NativeMethods.MOD_ALT;
                case "Ctrl": return NativeMethods.MOD_CONTROL;
                case "Shift": return NativeMethods.MOD_SHIFT;
                case "Ctrl+Alt": return NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
                case "Ctrl+Shift": return NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT;
                default: return NativeMethods.MOD_ALT;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_ID_RECORD)
                {
                    ToggleRecording();
                }
                else if (id == HOTKEY_ID_PLAY)
                {
                    TogglePlayback();
                }
                else if (id == HOTKEY_ID_STOP)
                {
                    StopAllActiveOperations();
                }
            }
            base.WndProc(ref m);
        }

        // ==================== ĐIỀU KHIỂN GHI & PHÁT ====================

        private void ToggleRecording()
        {
            if (_player.IsPlaying) return;

            if (!_recorder.IsRecording)
            {
                if (_gridSteps.IsCurrentCellInEditMode) _gridSteps.EndEdit();

                _currentProfile.Steps.Clear();
                _currentProfile.ScreenTopology = ScreenTopology.CaptureCurrent();
                _stepsBinding.ResetBindings();

                _recorder.RecordMouseMove = _chkRecordMove.Checked;
                _recorder.RecordKeyboard = _chkRecordKeys.Checked;

                if (_chkMinimizeOnRun.Checked)
                {
                    WindowState = FormWindowState.Minimized;
                }

                _btnRecord.Text = "■ Dừng Ghi";
                _btnPlay.Enabled = false;
                _btnStop.Enabled = true;
                _lblStatusBadge.Text = "TRẠNG THÁI: ĐANG GHI MACRO (RECORDING...)";
                _lblStatusBadge.ForeColor = Color.Crimson;

                _recorder.Start();
            }
            else
            {
                StopRecordingInternal();
            }
        }

        private void StopRecordingInternal()
        {
            List<MacroStep> recorded = _recorder.Stop();
            _currentProfile.Steps = recorded;
            BindProfileToGrid();

            _btnRecord.Text = "● Ghi Macro (Record)";
            _btnPlay.Enabled = true;
            _btnStop.Enabled = false;
            _lblStatusBadge.Text = string.Format("ĐÃ GHI XONG ({0} bước)", _currentProfile.Steps.Count);
            _lblStatusBadge.ForeColor = Color.FromArgb(13, 110, 253);

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
        }

        private void OnStepRecordedFromHook(MacroStep step)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<MacroStep>(OnStepRecordedFromHook), step);
                return;
            }
            _lblStatusBadge.Text = string.Format("ĐANG GHI... (#{0}: {1} tại {2},{3})",
                step.StepId, step.ActionType, step.X, step.Y);
        }

        private void TogglePlayback()
        {
            if (_recorder.IsRecording) return;

            if (_player.IsPlaying)
            {
                _player.Stop();
                return;
            }

            if (_gridSteps.IsCurrentCellInEditMode)
            {
                _gridSteps.EndEdit();
            }

            SyncUiSettingsToProfile();

            if (_currentProfile.Steps.Count == 0)
            {
                MessageBox.Show("Kịch bản chưa có bước nào để phát.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<string> errors = MacroValidator.ValidateAndBindPairs(_currentProfile);
            _stepsBinding.ResetBindings();
            if (errors.Count > 0)
            {
                MessageBox.Show(
                    "Kịch bản có lỗi dữ liệu hoặc cặp Down/Up chưa cân bằng:\n\n- " + string.Join("\n- ", errors.ToArray()),
                    "Chặn chạy do lỗi Kịch bản",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            ScreenTopology currentScreen = ScreenTopology.CaptureCurrent();
            if (_currentProfile.ScreenTopology != null && !currentScreen.Matches(_currentProfile.ScreenTopology))
            {
                DialogResult dr = MessageBox.Show(
                    string.Format("Cấu hình màn hình hiện tại khác với lúc ghi kịch bản!\n\n- Lúc ghi: {0}\n- Hiện tại: {1}\n\nBạn vẫn muốn tiếp tục phát lại?",
                        _currentProfile.ScreenTopology, currentScreen),
                    "Cảnh báo lệch Cấu hình Màn hình (DPI / Topology)",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes) return;
            }

            if (_chkMinimizeOnRun.Checked)
            {
                WindowState = FormWindowState.Minimized;
            }

            _btnRecord.Enabled = false;
            _btnPlay.Text = "■ Dừng Phát";
            _btnStop.Enabled = true;
            _lblStatusBadge.Text = string.Format("TRẠNG THÁI: ĐANG PHÁT ({0})...", _currentProfile.ParsedPlaybackMode);
            _lblStatusBadge.ForeColor = Color.SeaGreen;

            _player.Start(_currentProfile);
        }

        private void StopAllActiveOperations()
        {
            if (_recorder.IsRecording)
            {
                StopRecordingInternal();
            }
            if (_player.IsPlaying)
            {
                _player.Stop();
            }
        }

        private void OnPlayerProgressChanged(int currentLoop, int totalLoops, int stepId)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, int, int>(OnPlayerProgressChanged), currentLoop, totalLoops, stepId);
                return;
            }
            string loopText = totalLoops == 0 ? string.Format("{0}/∞", currentLoop) : string.Format("{0}/{1}", currentLoop, totalLoops);
            _lblStatusBadge.Text = string.Format("ĐANG PHÁT ({0}): Vòng {1} - Bước #{2}",
                _currentProfile.ParsedPlaybackMode, loopText, stepId);
        }

        private void OnPlayerFinished(bool wasCancelled, string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool, string>(OnPlayerFinished), wasCancelled, message);
                return;
            }

            _btnRecord.Enabled = true;
            _btnPlay.Text = "▶ Phát lại (Play)";
            _btnStop.Enabled = false;
            _lblStatusBadge.Text = message;
            _lblStatusBadge.ForeColor = wasCancelled ? Color.DarkOrange : Color.FromArgb(13, 110, 253);

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(OnDisplaySettingsChanged), sender, e);
                return;
            }
            ScreenTopology current = ScreenTopology.CaptureCurrent();
            _lblScreenInfo.Text = "Màn hình (vừa thay đổi): " + current;
            _lblScreenInfo.ForeColor = Color.DarkOrange;
        }

        // ==================== THÊM BƯỚC NHANH & THAO TÁC HÀNG LOẠT ====================

        private void AddQuickStep()
        {
            if (_gridSteps.IsCurrentCellInEditMode) _gridSteps.EndEdit();

            MacroActionType selectedType;
            Enum.TryParse(_cboQuickAction.Text, true, out selectedType);

            MacroStep newStep = new MacroStep
            {
                StepId = _currentProfile.Steps.Count + 1,
                ActionType = selectedType.ToString(),
                X = (int)_numQuickX.Value,
                Y = (int)_numQuickY.Value,
                DelayBeforeMs = (int)_numQuickDelay.Value,
                HoldDurationMs = 35
            };

            _currentProfile.Steps.Add(newStep);

            if (MacroValidator.IsDownAction(selectedType))
            {
                MacroActionType? counterpart = MacroValidator.GetCounterpartAction(selectedType);
                if (counterpart.HasValue)
                {
                    DialogResult dr = MessageBox.Show(
                        string.Format("Bạn vừa thêm bước '{0}'. Bạn có muốn tự động thêm bước '{1}' đối ứng để tránh kẹt nút/phím không?",
                            selectedType, counterpart.Value),
                        "Đồng bộ cặp Down/Up",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (dr == DialogResult.Yes)
                    {
                        MacroStep upStep = new MacroStep
                        {
                            StepId = _currentProfile.Steps.Count + 1,
                            ActionType = counterpart.Value.ToString(),
                            X = newStep.X,
                            Y = newStep.Y,
                            DelayBeforeMs = 50,
                            HoldDurationMs = 0,
                            PairStepId = newStep.StepId
                        };
                        newStep.PairStepId = upStep.StepId;
                        _currentProfile.Steps.Add(upStep);
                    }
                }
            }

            MacroValidator.ValidateAndBindPairs(_currentProfile);
            _stepsBinding.ResetBindings();
        }

        private void ApplyBatchDelayToAllSteps()
        {
            if (_currentProfile.Steps.Count == 0) return;
            if (_gridSteps.IsCurrentCellInEditMode) _gridSteps.EndEdit();

            int targetDelay = (int)_numBatchDelay.Value;
            foreach (MacroStep step in _currentProfile.Steps)
            {
                step.DelayBeforeMs = targetDelay;
            }
            _stepsBinding.ResetBindings();
            _lblStatusBadge.Text = string.Format("Đã gán DelayBeforeMs = {0} ms cho toàn bộ {1} bước.", targetDelay, _currentProfile.Steps.Count);
        }

        private void DeleteStepWithPairCheck(MacroStep step)
        {
            if (step == null) return;

            if (step.PairStepId.HasValue)
            {
                MacroStep paired = FindStepById(step.PairStepId.Value);
                if (paired != null)
                {
                    DialogResult dr = MessageBox.Show(
                        string.Format("Bước #{0} ({1}) đang ghép cặp với Bước #{2} ({3}).\n\nChọn [Yes] để xóa CẢ CẶP (khuyên dùng, tránh kẹt phím/chuột).\nChọn [No] để chỉ xóa riêng Bước #{0}.\nChọn [Cancel] để hủy.",
                            step.StepId, step.ActionType, paired.StepId, paired.ActionType),
                        "Xác nhận xóa cặp sự kiện Down/Up",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Warning);

                    if (dr == DialogResult.Cancel) return;
                    if (dr == DialogResult.Yes)
                    {
                        _currentProfile.Steps.Remove(paired);
                    }
                    else
                    {
                        paired.PairStepId = null;
                    }
                }
            }

            _currentProfile.Steps.Remove(step);
            MacroValidator.ValidateAndBindPairs(_currentProfile);
            _stepsBinding.ResetBindings();
        }

        private void ClearAllSteps()
        {
            if (_currentProfile.Steps.Count == 0) return;
            if (_gridSteps.IsCurrentCellInEditMode) _gridSteps.EndEdit();

            if (MessageBox.Show("Bạn có chắc muốn xóa toàn bộ danh sách bước?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _currentProfile.Steps.Clear();
                _stepsBinding.ResetBindings();
            }
        }

        private MacroStep FindStepById(int stepId)
        {
            foreach (MacroStep s in _currentProfile.Steps)
            {
                if (s.StepId == stepId) return s;
            }
            return null;
        }

        private void RunManualValidation()
        {
            if (_gridSteps.IsCurrentCellInEditMode) _gridSteps.EndEdit();
            SyncUiSettingsToProfile();
            List<string> errors = MacroValidator.ValidateAndBindPairs(_currentProfile);
            _stepsBinding.ResetBindings();

            if (errors.Count == 0)
            {
                MessageBox.Show(
                    string.Format("Kịch bản hợp lệ!\n- Tổng số bước: {0}\n- Chế độ phát: {1}\n- SchemaVersion: {2}\n- Màn hình: {3}",
                        _currentProfile.Steps.Count, _currentProfile.ParsedPlaybackMode, _currentProfile.SchemaVersion, _currentProfile.ScreenTopology),
                    "Kiểm tra Kịch bản",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    "Phát hiện các vấn đề sau:\n\n- " + string.Join("\n- ", errors.ToArray()),
                    "Lỗi Kịch bản",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        // ==================== LƯU & ĐỌC FILE JSON ====================

        private void SyncUiSettingsToProfile()
        {
            _currentProfile.SchemaVersion = MacroProfile.CurrentSchemaVersion;
            _currentProfile.LoopCount = (int)_numLoopCount.Value;
            _currentProfile.SpeedMultiplier = (double)_numSpeed.Value;
            _currentProfile.RandomJitterMs = (int)_numJitter.Value;

            if (_cboPlaybackMode.SelectedIndex == 1)
                _currentProfile.ParsedPlaybackMode = PlaybackMode.SnapAndRestore;
            else if (_cboPlaybackMode.SelectedIndex == 2)
                _currentProfile.ParsedPlaybackMode = PlaybackMode.BackgroundPostMessage;
            else
                _currentProfile.ParsedPlaybackMode = PlaybackMode.SendInput;

            if (_currentProfile.ScreenTopology == null)
            {
                _currentProfile.ScreenTopology = ScreenTopology.CaptureCurrent();
            }
        }

        private void SaveMacroToJsonDialog()
        {
            if (_gridSteps.IsCurrentCellInEditMode) _gridSteps.EndEdit();
            SyncUiSettingsToProfile();
            List<string> errors = MacroValidator.ValidateAndBindPairs(_currentProfile);
            _stepsBinding.ResetBindings();

            if (errors.Count > 0)
            {
                DialogResult dr = MessageBox.Show(
                    "Kịch bản đang có cảnh báo:\n- " + string.Join("\n- ", errors.ToArray()) + "\n\nBạn vẫn muốn lưu file?",
                    "Cảnh báo trước khi lưu",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes) return;
            }

            string defaultDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "macros");
            if (!Directory.Exists(defaultDir))
            {
                try { Directory.CreateDirectory(defaultDir); } catch { }
            }

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.InitialDirectory = defaultDir;
                sfd.Filter = "Macro JSON (*.json)|*.json";
                sfd.FileName = "macro.json";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    _currentProfile.Name = Path.GetFileNameWithoutExtension(sfd.FileName);
                    MacroStorage.SaveToFile(sfd.FileName, _currentProfile);
                    _lblStatusBadge.Text = "Đã lưu: " + Path.GetFileName(sfd.FileName);
                }
            }
        }

        private void LoadMacroFromJsonDialog()
        {
            if (_gridSteps.IsCurrentCellInEditMode) _gridSteps.EndEdit();
            string defaultDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "macros");
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                if (Directory.Exists(defaultDir)) ofd.InitialDirectory = defaultDir;
                ofd.Filter = "Macro JSON (*.json)|*.json";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        MacroProfile loaded = MacroStorage.LoadFromFile(ofd.FileName);
                        _currentProfile = loaded;

                        _numLoopCount.Value = Math.Max(_numLoopCount.Minimum, Math.Min(_numLoopCount.Maximum, loaded.LoopCount));
                        _numSpeed.Value = Math.Max(_numSpeed.Minimum, Math.Min(_numSpeed.Maximum, (decimal)loaded.SpeedMultiplier));
                        _numJitter.Value = Math.Max(_numJitter.Minimum, Math.Min(_numJitter.Maximum, loaded.RandomJitterMs));

                        switch (loaded.ParsedPlaybackMode)
                        {
                            case PlaybackMode.SnapAndRestore: _cboPlaybackMode.SelectedIndex = 1; break;
                            case PlaybackMode.BackgroundPostMessage: _cboPlaybackMode.SelectedIndex = 2; break;
                            default: _cboPlaybackMode.SelectedIndex = 0; break;
                        }

                        BindProfileToGrid();
                        _lblStatusBadge.Text = string.Format("Đã tải '{0}' ({1} bước)", loaded.Name, loaded.Steps.Count);

                        ScreenTopology currentScreen = ScreenTopology.CaptureCurrent();
                        if (loaded.ScreenTopology != null && !currentScreen.Matches(loaded.ScreenTopology))
                        {
                            MessageBox.Show(
                                string.Format("Lưu ý: File kịch bản này được ghi trên cấu hình màn hình khác:\n\n- Trong file: {0}\n- Máy hiện tại: {1}",
                                    loaded.ScreenTopology, currentScreen),
                                "Cảnh báo khác cấu hình màn hình",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Không thể đọc file JSON: " + ex.Message, "Lỗi đọc file", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        [STAThread]
        public static void Main()
        {
            NativeMethods.InitializeDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
