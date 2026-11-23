using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FlexVision
{
    public class BlobAnalysisConfigControl : UserControl
    {
        private readonly Color Color_Bg = Color.FromArgb(30, 30, 30);
        private readonly Color Color_Slate = Color.FromArgb(42, 42, 42);
        private readonly Color Color_Light = Color.FromArgb(55, 55, 55);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        private BlobAnalysisModule _module;
        private PipelineEditorControl _mainEditor;
        private Action _onDataChanged;

        private NumericUpDown numMinArea, numMaxArea;

        private CheckBox chkUseCirc;
        private NumericUpDown numMinCirc, numMaxCirc;

        private CheckBox chkUseAspect;
        private NumericUpDown numMinAspect, numMaxAspect;

        private CheckBox chkUsePerim;
        private NumericUpDown numMinPerim, numMaxPerim;

        private NumericUpDown numExpectedCount;
        private CheckBox chkCreateSub;
        private CheckBox chkWhiteBlobs, chkBlackBlobs;

        private Label lblExecutionTime;
        private Panel panelLedIndicator;
        private Label lblLedText;
        private ListView listViewResults;

        private bool _isLoadingUI = false;

        public BlobAnalysisConfigControl(BlobAnalysisModule module, PipelineEditorControl mainEditor, Action onDataChanged)
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
            // 1. SZAKASZ: ROI ÉS TESZT GOMBOK
            Label lblSection1 = new Label { Text = "BLOB KERESÉSI ZÓNA ÉS TESZT", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 10), AutoSize = true };

            Button btnSelectRoi = CreateControlBtn("🗺️ ROI SELECT", 10, 32, 157, 30);
            btnSelectRoi.Click += (s, e) => { _mainEditor?.StartRoiSelection(); };

            Button btnResetRoi = CreateControlBtn("✖ RESET ROI", 173, 32, 157, 30, Color.Tomato);
            btnResetRoi.Click += (s, e) => {
                _module.UseROI = false;
                _module.ROI = Rectangle.Empty;
                _mainEditor?.RunPipelineDebug();
                _onDataChanged?.Invoke();
            };

            Button btnTest = CreateControlBtn("▶ RUN BLOB ANALYSIS", 10, 70, 320, 35, Color.LightGreen);
            btnTest.ForeColor = Color.Black;
            btnTest.Click += (s, e) => {
                _onDataChanged?.Invoke();
                RefreshResultsFromModule();
            };

            // 2. SZAKASZ: GEOMETRIAI SZŰRÉSI PARAMÉTEREK
            Label lblSection2 = new Label { Text = "FOLT SZŰRÉSI FELTÉTELEK", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 118), AutoSize = true };

            // 2/A. Area (Mindig aktív)
            Label lblArea = new Label { Text = "✔ Area (px):", ForeColor = Color_Cyan, Location = new Point(10, 144), Width = 105, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            numMinArea = new NumericUpDown { Location = new Point(120, 142), Width = 95, Minimum = 1, Maximum = 10000000, Increment = 50, Value = 100, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMinArea.ValueChanged += (s, e) => { if (_isLoadingUI) return; _module.MinArea = (double)numMinArea.Value; _onDataChanged?.Invoke(); };

            Label lblDash1 = new Label { Text = "-", ForeColor = Color_Text, Location = new Point(220, 144), AutoSize = true };
            numMaxArea = new NumericUpDown { Location = new Point(233, 142), Width = 97, Minimum = 10, Maximum = 50000000, Increment = 500, Value = 500000, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMaxArea.ValueChanged += (s, e) => { if (_isLoadingUI) return; _module.MaxArea = (double)numMaxArea.Value; _onDataChanged?.Invoke(); };

            // 2/B. Circularity (Kapcsolható CheckBox-szal)
            chkUseCirc = new CheckBox { Text = "Circularity:", ForeColor = Color_Text, Location = new Point(10, 173), Width = 105, Font = new Font("Segoe UI", 8) };
            numMinCirc = new NumericUpDown { Location = new Point(120, 171), Width = 95, Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05M, Enabled = false, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMinCirc.ValueChanged += (s, e) => { if (_isLoadingUI) return; _module.MinCircularity = (double)numMinCirc.Value; _onDataChanged?.Invoke(); };

            Label lblDash2 = new Label { Text = "-", ForeColor = Color_Text, Location = new Point(220, 173), AutoSize = true };
            numMaxCirc = new NumericUpDown { Location = new Point(233, 171), Width = 97, Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05M, Value = 1, Enabled = false, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMaxCirc.ValueChanged += (s, e) => { if (_isLoadingUI) return; _module.MaxCircularity = (double)numMaxCirc.Value; _onDataChanged?.Invoke(); };

            chkUseCirc.CheckedChanged += (s, e) =>
            {
                numMinCirc.Enabled = numMaxCirc.Enabled = chkUseCirc.Checked;
                if (_isLoadingUI) return;
                _module.UseCircularityFilter = chkUseCirc.Checked;
                _onDataChanged?.Invoke();
            };

            // 2/C. Aspect Ratio W/H (Kapcsolható CheckBox-szal)
            chkUseAspect = new CheckBox { Text = "Aspect (W/H):", ForeColor = Color_Text, Location = new Point(10, 202), Width = 105, Font = new Font("Segoe UI", 8) };
            numMinAspect = new NumericUpDown { Location = new Point(120, 200), Width = 95, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Increment = 0.1M, Enabled = false, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMinAspect.ValueChanged += (s, e) => { if (_isLoadingUI) return; _module.MinAspectRatio = (double)numMinAspect.Value; _onDataChanged?.Invoke(); };

            Label lblDash3 = new Label { Text = "-", ForeColor = Color_Text, Location = new Point(220, 202), AutoSize = true };
            numMaxAspect = new NumericUpDown { Location = new Point(233, 200), Width = 97, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Increment = 0.1M, Value = 100, Enabled = false, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMaxAspect.ValueChanged += (s, e) => { if (_isLoadingUI) return; _module.MaxAspectRatio = (double)numMaxAspect.Value; _onDataChanged?.Invoke(); };

            chkUseAspect.CheckedChanged += (s, e) =>
            {
                numMinAspect.Enabled = numMaxAspect.Enabled = chkUseAspect.Checked;
                if (_isLoadingUI) return;
                _module.UseAspectRatioFilter = chkUseAspect.Checked;
                _onDataChanged?.Invoke();
            };

            // 2/D. Perimeter px (Kapcsolható CheckBox-szal)
            chkUsePerim = new CheckBox { Text = "Perimeter:", ForeColor = Color_Text, Location = new Point(10, 231), Width = 105, Font = new Font("Segoe UI", 8) };
            numMinPerim = new NumericUpDown { Location = new Point(120, 229), Width = 95, Minimum = 0, Maximum = 100000, DecimalPlaces = 0, Increment = 10, Enabled = false, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMinPerim.ValueChanged += (s, e) => { if (_isLoadingUI) return; _module.MinPerimeter = (double)numMinPerim.Value; _onDataChanged?.Invoke(); };

            Label lblDash4 = new Label { Text = "-", ForeColor = Color_Text, Location = new Point(220, 231), AutoSize = true };
            numMaxPerim = new NumericUpDown { Location = new Point(233, 229), Width = 97, Minimum = 0, Maximum = 100000, DecimalPlaces = 0, Increment = 10, Value = 100000, Enabled = false, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMaxPerim.ValueChanged += (s, e) => { if (_isLoadingUI) return; _module.MaxPerimeter = (double)numMaxPerim.Value; _onDataChanged?.Invoke(); };

            chkUsePerim.CheckedChanged += (s, e) =>
            {
                numMinPerim.Enabled = numMaxPerim.Enabled = chkUsePerim.Checked;
                if (_isLoadingUI) return;
                _module.UsePerimeterFilter = chkUsePerim.Checked;
                _onDataChanged?.Invoke();
            };

            // Elvárt darabszám és Sub-Pipeline kapcsoló
            Label lblExpected = new Label { Text = "Expected Count:", ForeColor = Color_Text, Location = new Point(10, 265), Width = 100, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            numExpectedCount = new NumericUpDown { Location = new Point(115, 263), Width = 60, Minimum = 0, Maximum = 500, Value = 1, BackColor = Color_Light, ForeColor = Color_Cyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle };
            numExpectedCount.ValueChanged += (s, e) => { if (_isLoadingUI) return; _module.ExpectedBlobCount = (int)numExpectedCount.Value; _onDataChanged?.Invoke(); };
            chkCreateSub = new CheckBox { Text = "Start Sub-Loop", ForeColor = Color_Text, Location = new Point(190, 264), AutoSize = true, Font = new Font("Segoe UI", 8) };
            chkCreateSub.CheckedChanged += (s, e) => { if (_isLoadingUI) return; _module.CreateSubContexts = chkCreateSub.Checked; _onDataChanged?.Invoke(); };
            // Színválasztó Checkboxok
            Label lblColorFilter = new Label { Text = "Blob Color:", ForeColor = Color_Text, Location = new Point(10, 294), Width = 80, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            chkWhiteBlobs = new CheckBox { Text = "White", ForeColor = Color_Text, Location = new Point(95, 293), AutoSize = true, Font = new Font("Segoe UI", 8.5f) };
            chkWhiteBlobs.CheckedChanged += (s, e) => { if (_isLoadingUI) return; _module.SearchWhiteBlobs = chkWhiteBlobs.Checked; _onDataChanged?.Invoke(); };
            chkBlackBlobs = new CheckBox { Text = "Black", ForeColor = Color_Text, Location = new Point(175, 293), AutoSize = true, Font = new Font("Segoe UI", 8.5f) };
            chkBlackBlobs.CheckedChanged += (s, e) => { if (_isLoadingUI) return; _module.SearchBlackBlobs = chkBlackBlobs.Checked; _onDataChanged?.Invoke(); };
            // 3. SZAKASZ: DIAGNOSZTIKA ÉS LED
            lblExecutionTime = new Label { Text = "TIME: 0 ms", ForeColor = Color_Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 326), AutoSize = true };
            panelLedIndicator = new Panel { Location = new Point(260, 323), Size = new Size(70, 24), BackColor = Color.DarkGray, BorderStyle = BorderStyle.FixedSingle };
            lblLedText = new Label { Text = "WAIT", ForeColor = Color.Black, Font = new Font("Segoe UI", 8, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            panelLedIndicator.Controls.Add(lblLedText);
            // 4. SZAKASZ: TALÁLATI LISTA
            Label lblSection3 = new Label { Text = "MEGTALÁLT FOLTOK (BLOB RESULTS)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 360), AutoSize = true };
            listViewResults = new ListView
            {
                Location = new Point(10, 380),
                Size = new Size(320, 210),
                BackColor = Color_Slate,
                ForeColor = Color_Text,
                BorderStyle = BorderStyle.None,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 8f)
            };
            listViewResults.Columns.Add("#", 25, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Area", 55, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Aspect", 48, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Perim", 50, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Circ", 42, HorizontalAlignment.Center);
            listViewResults.Columns.Add("X", 48, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Y", 48, HorizontalAlignment.Center);
            this.Controls.AddRange(new Control[] {
lblSection1, btnSelectRoi, btnResetRoi, btnTest,
lblSection2, lblArea, numMinArea, lblDash1, numMaxArea,
chkUseCirc, numMinCirc, lblDash2, numMaxCirc,
chkUseAspect, numMinAspect, lblDash3, numMaxAspect,
chkUsePerim, numMinPerim, lblDash4, numMaxPerim,
lblExpected, numExpectedCount, chkCreateSub,
lblColorFilter, chkWhiteBlobs, chkBlackBlobs,
lblExecutionTime, panelLedIndicator, lblSection3, listViewResults
});
        }
        public void LoadValuesFromModule()
        {
            if (_module == null) return;
            _isLoadingUI = true;
            try
            {
                numMinArea.Value = ClampDecimal((decimal)_module.MinArea, numMinArea);
                numMaxArea.Value = ClampDecimal((decimal)_module.MaxArea, numMaxArea);
                chkUseCirc.Checked = _module.UseCircularityFilter;
                numMinCirc.Enabled = numMaxCirc.Enabled = _module.UseCircularityFilter;
                numMinCirc.Value = ClampDecimal((decimal)_module.MinCircularity, numMinCirc);
                numMaxCirc.Value = ClampDecimal((decimal)_module.MaxCircularity, numMaxCirc);
                chkUseAspect.Checked = _module.UseAspectRatioFilter;
                numMinAspect.Enabled = numMaxAspect.Enabled = _module.UseAspectRatioFilter;
                numMinAspect.Value = ClampDecimal((decimal)_module.MinAspectRatio, numMinAspect);
                numMaxAspect.Value = ClampDecimal((decimal)_module.MaxAspectRatio, numMaxAspect);
                chkUsePerim.Checked = _module.UsePerimeterFilter;
                numMinPerim.Enabled = numMaxPerim.Enabled = _module.UsePerimeterFilter;
                numMinPerim.Value = ClampDecimal((decimal)_module.MinPerimeter, numMinPerim);
                numMaxPerim.Value = ClampDecimal((decimal)_module.MaxPerimeter, numMaxPerim);
                numExpectedCount.Value = ClampDecimal(_module.ExpectedBlobCount, numExpectedCount);
                chkCreateSub.Checked = _module.CreateSubContexts;
                chkWhiteBlobs.Checked = _module.SearchWhiteBlobs;
                chkBlackBlobs.Checked = _module.SearchBlackBlobs;
            }
            finally
            {
                _isLoadingUI = false;
            }
        }
        private decimal ClampDecimal(decimal val, NumericUpDown num)
        {
            if (val < num.Minimum) return num.Minimum;
            if (val > num.Maximum) return num.Maximum;
            return val;
        }
        public void RefreshResultsFromModule()
        {
            lblExecutionTime.Text = $"TIME: {_module.LastExecutionTime}";
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
            listViewResults.Items.Clear();
            if (_module.LastBlobs != null)
            {
                foreach (var b in _module.LastBlobs)
                {
                    ListViewItem item = new ListViewItem(b.Index.ToString());
                    item.SubItems.Add(b.Area.ToString("F0"));
                    item.SubItems.Add(b.AspectRatio.ToString("F2"));
                    item.SubItems.Add(b.Perimeter.ToString("F0"));
                    item.SubItems.Add(b.Circularity.ToString("F2"));
                    item.SubItems.Add(b.Center.X.ToString("F1"));
                    item.SubItems.Add(b.Center.Y.ToString("F1"));
                    listViewResults.Items.Add(item);
                }
            }
        }
        private Button CreateControlBtn(string text, int x, int y, int w, int h, Color? foreColor = null)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color_Light,
                ForeColor = foreColor ?? Color_Text,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }
    }
}