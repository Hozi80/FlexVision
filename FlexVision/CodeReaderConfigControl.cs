using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlexVision
{
    public class CodeReaderConfigControl : UserControl
    {
        private readonly Color Color_Bg = Color.FromArgb(30, 30, 30);
        private readonly Color Color_Slate = Color.FromArgb(42, 42, 42);
        private readonly Color Color_Light = Color.FromArgb(55, 55, 55);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        private CodeReaderModule _module;
        private PipelineEditorControl _mainEditor;
        private Action _onDataChanged;

        private ComboBox comboFormat;
        private CheckBox chkDpmEnable;
        private NumericUpDown numBlur;
        private NumericUpDown numMorph;
        private CheckBox chkInvert;
        private ComboBox comboValidation;
        private TextBox txtExpected;

        private Label lblExecutionTime;
        private Panel panelLedIndicator;
        private Label lblLedText;
        private TextBox txtDecodedOutput;
        private Label lblStrategyInfo;
        private bool _isLoadingUI = false;
        private PictureBox picDpmPreview;

        public CodeReaderConfigControl(CodeReaderModule module, PipelineEditorControl mainEditor, Action onDataChanged)
        {
            this._module = module;
            this._mainEditor = mainEditor;
            this._onDataChanged = onDataChanged;

            this.BackColor = Color_Bg;
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;

            InitializeComponent();

            this.Load += (s, e) =>
            {
                LoadValuesFromModule();
                RefreshResultsFromModule();
            };
        }

        private void InitializeComponent()
        {
            // 1. ROI ÉS TESZT
            Label lbl1 = new Label { Text = "1. KÓDOLVASÓ ZÓNA (ROI) ÉS TESZT", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 10), AutoSize = true };

            Button btnSelectRoi = CreateBtn("🗺️ ROI SELECT", 10, 32, 157, 30, Color_Light, Color_Text);
            btnSelectRoi.Click += (s, e) => _mainEditor?.StartRoiSelection();

            Button btnResetRoi = CreateBtn("✖ RESET ROI", 173, 32, 157, 30, Color_Light, Color.Tomato);
            btnResetRoi.Click += (s, e) =>
            {
                _module.UseROI = false;
                _module.ROI = Rectangle.Empty;
                _mainEditor?.RunPipelineDebug();
                _onDataChanged?.Invoke();
            };

            Button btnTest = CreateBtn("▶ DECODE CODE NOW", 10, 70, 320, 34, Color.LightGreen, Color.Black);
            btnTest.Click += (s, e) =>
            {
                _onDataChanged?.Invoke();
                RefreshResultsFromModule();
            };

            // 2. DPM (LÉZERGRAVÍROZÁS) BEÁLLÍTÁSOK
            Label lbl2 = new Label { Text = "2. KÓDTÍPUS ÉS DPM (LÉZERGRAVÍR) OPTIMALIZÁLÁS", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 118), AutoSize = true };

            Label lblFmt = new Label { Text = "Code Format:", ForeColor = Color_Text, Location = new Point(10, 143), Width = 95, Font = new Font("Segoe UI", 8) };
            comboFormat = new ComboBox { Location = new Point(110, 140), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color_Light, ForeColor = Color_Text, FlatStyle = FlatStyle.Flat, DataSource = Enum.GetValues(typeof(CodeFormatSelection)) };
            comboFormat.SelectedIndexChanged += (s, e) => { if (_isLoadingUI) return; _module.FormatSelection = (CodeFormatSelection)comboFormat.SelectedItem; _onDataChanged?.Invoke(); };

            chkDpmEnable = new CheckBox { Text = "Enable Laser DPM Enhancement (CLAHE + Morph)", ForeColor = Color_Cyan, Location = new Point(10, 172), AutoSize = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            chkDpmEnable.CheckedChanged += (s, e) => { if (_isLoadingUI) return; _module.EnableDpmEnhancement = chkDpmEnable.Checked; _onDataChanged?.Invoke(); };

            Label lblBlur = new Label { Text = "Dot Smooth:", ForeColor = Color_Text, Location = new Point(10, 202), Width = 80, Font = new Font("Segoe UI", 8) };
            numBlur = new NumericUpDown { Location = new Point(92, 200), Width = 60, Minimum = 0, Maximum = 4, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numBlur.ValueChanged += (s, e) => { if (_isLoadingUI) return; _module.DpmBlurSize = (int)numBlur.Value; _onDataChanged?.Invoke(); };

            Label lblMorph = new Label { Text = "Dot Connect:", ForeColor = Color_Text, Location = new Point(170, 202), Width = 85, Font = new Font("Segoe UI", 8) };
            numMorph = new NumericUpDown { Location = new Point(260, 200), Width = 70, Minimum = 0, Maximum = 4, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMorph.ValueChanged += (s, e) => { if (_isLoadingUI) return; _module.DpmMorphSize = (int)numMorph.Value; _onDataChanged?.Invoke(); };

            chkInvert = new CheckBox { Text = "Try Inverted / Mirrored Codes", ForeColor = Color_Text, Location = new Point(10, 230), AutoSize = true, Font = new Font("Segoe UI", 8.5f) };
            chkInvert.CheckedChanged += (s, e) => { if (_isLoadingUI) return; _module.TryInvertedAndMirrored = chkInvert.Checked; _onDataChanged?.Invoke(); };

            // 3. VALIDÁCIÓ (TARTALOM ELLENŐRZÉS)
            Label lbl3 = new Label { Text = "3. TARTALOM ELLENŐRZÉS (PASS / FAIL)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 265), AutoSize = true };

            Label lblVal = new Label { Text = "Validation:", ForeColor = Color_Text, Location = new Point(10, 290), Width = 85, Font = new Font("Segoe UI", 8) };
            comboValidation = new ComboBox { Location = new Point(100, 287), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color_Light, ForeColor = Color_Text, FlatStyle = FlatStyle.Flat, DataSource = Enum.GetValues(typeof(CodeValidationMode)) };
            comboValidation.SelectedIndexChanged += (s, e) => { if (_isLoadingUI) return; _module.ValidationMode = (CodeValidationMode)comboValidation.SelectedItem; _onDataChanged?.Invoke(); };

            Label lblExp = new Label { Text = "Expected Text:", ForeColor = Color_Text, Location = new Point(10, 320), Width = 85, Font = new Font("Segoe UI", 8) };
            txtExpected = new TextBox { Location = new Point(100, 318), Width = 230, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            txtExpected.TextChanged += (s, e) => { if (_isLoadingUI) return; _module.ExpectedText = txtExpected.Text; };

            // 4. EREDMÉNYEK ÉS KIOLVASOTT SZÖVEG
            lblExecutionTime = new Label { Text = "TIME: 0 ms", ForeColor = Color_Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 358), AutoSize = true };
            panelLedIndicator = new Panel { Location = new Point(260, 355), Size = new Size(70, 24), BackColor = Color.DarkGray, BorderStyle = BorderStyle.FixedSingle };
            lblLedText = new Label { Text = "WAIT", ForeColor = Color.Black, Font = new Font("Segoe UI", 8, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            panelLedIndicator.Controls.Add(lblLedText);

            lblStrategyInfo = new Label { Text = "Format: - | Strategy: -", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8f, FontStyle.Italic), Location = new Point(10, 388), AutoSize = true };

            Label lblDec = new Label { Text = "DECODED STRING:", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 412), AutoSize = true };
            txtDecodedOutput = new TextBox
            {
                Location = new Point(10, 432),
                Size = new Size(320, 65),
                Multiline = true,
                ReadOnly = true,
                BackColor = Color_Slate,
                ForeColor = Color.Gold,
                Font = new Font("Consolas", 10.5f, FontStyle.Bold),
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblPrev = new Label { Text = "DPM PREPROCESS PREVIEW:", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 505), AutoSize = true };
            picDpmPreview = new PictureBox
            {
                Location = new Point(10, 525),
                Size = new Size(320, 320),
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };


            this.Controls.AddRange(new Control[] {
                lbl1, btnSelectRoi, btnResetRoi, btnTest,
                lbl2, lblFmt, comboFormat, chkDpmEnable, lblBlur, numBlur, lblMorph, numMorph, chkInvert,
                lbl3, lblVal, comboValidation, lblExp, txtExpected,
                lblExecutionTime, panelLedIndicator, lblStrategyInfo, lblDec, txtDecodedOutput,lblPrev, picDpmPreview
            });
        }

        public void LoadValuesFromModule()
        {
            if (_module == null) return;
            _isLoadingUI = true;
            try
            {
                SelectEnumInCombo(comboFormat, _module.FormatSelection);
                SelectEnumInCombo(comboValidation, _module.ValidationMode);
                chkDpmEnable.Checked = _module.EnableDpmEnhancement;
                numBlur.Value = Math.Max(numBlur.Minimum, Math.Min(numBlur.Maximum, _module.DpmBlurSize));
                numMorph.Value = Math.Max(numMorph.Minimum, Math.Min(numMorph.Maximum, _module.DpmMorphSize));
                chkInvert.Checked = _module.TryInvertedAndMirrored;
                txtExpected.Text = _module.ExpectedText ?? "";
            }
            finally
            {
                _isLoadingUI = false;
            }
        }

        private void SelectEnumInCombo<T>(ComboBox cb, T enumValue) where T : Enum
        {
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i].Equals(enumValue)) { cb.SelectedIndex = i; return; }
            }
        }
        public void RefreshResultsFromModule()
        {
            lblExecutionTime.Text = $"TIME: {_module.LastExecutionTime}";
            lblStrategyInfo.Text = $"Format: {_module.DetectedFormat} | Strategy: {_module.UsedPreprocessStrategy}";
            txtDecodedOutput.Text = string.IsNullOrEmpty(_module.DecodedText) ? "" : _module.DecodedText;
            if (_module.LastTestResult != null && _module.LastTestResult.Contains("PASS"))
            {
                lblLedText.Text = "PASS";
                panelLedIndicator.BackColor = Color.LimeGreen;
            }
            else
            {
                lblLedText.Text = "FAIL";
                panelLedIndicator.BackColor = Color.Tomato;
            }

            if (_module.LastDebugPreview != null)
            {
                Image old = picDpmPreview.Image;
                picDpmPreview.Image = new Bitmap(_module.LastDebugPreview);
                old?.Dispose();
            }

        }
        private Button CreateBtn(string text, int x, int y, int w, int h, Color bg, Color fg)
        {
            Button b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = fg, Font = new Font("Segoe UI", 8f, FontStyle.Bold), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }
    }
}