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

        private NumericUpDown numMinArea;
        private NumericUpDown numMaxArea;
        private NumericUpDown numMinCirc;
        private NumericUpDown numMaxCirc;
        private NumericUpDown numExpectedCount;
        private CheckBox chkCreateSub;

        private Label lblExecutionTime;
        private Panel panelLedIndicator;
        private Label lblLedText;
        private ListView listViewResults;

        public BlobAnalysisConfigControl(BlobAnalysisModule module, PipelineEditorControl mainEditor, Action onDataChanged)
        {
            this._module = module;
            this._mainEditor = mainEditor;
            this._onDataChanged = onDataChanged;

            this.BackColor = Color_Bg;
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;

            InitializeComponent();
            RefreshResultsFromModule();
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
            Label lblSection2 = new Label { Text = "FOLT SZŰRÉSI FELTÉTELEK", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 120), AutoSize = true };

            // Min / Max Terület
            Label lblMinArea = new Label { Text = "Min Area (px):", ForeColor = Color_Text, Location = new Point(10, 145), Width = 85, Font = new Font("Segoe UI", 8) };
            numMinArea = new NumericUpDown { Location = new Point(95, 143), Width = 70, Minimum = 1, Maximum = 10000000, Increment = 50, Value = (decimal)_module.MinArea, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMinArea.ValueChanged += (s, e) => { _module.MinArea = (double)numMinArea.Value; _onDataChanged?.Invoke(); };

            Label lblMaxArea = new Label { Text = "Max Area:", ForeColor = Color_Text, Location = new Point(175, 145), Width = 75, Font = new Font("Segoe UI", 8) };
            numMaxArea = new NumericUpDown { Location = new Point(255, 143), Width = 75, Minimum = 10, Maximum = 50000000, Increment = 500, Value = (decimal)_module.MaxArea, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMaxArea.ValueChanged += (s, e) => { _module.MaxArea = (double)numMaxArea.Value; _onDataChanged?.Invoke(); };

            // Min / Max Körszerűség (Circularity)
            Label lblMinCirc = new Label { Text = "Min Circ (0-1):", ForeColor = Color_Text, Location = new Point(10, 175), Width = 85, Font = new Font("Segoe UI", 8) };
            numMinCirc = new NumericUpDown { Location = new Point(95, 173), Width = 70, Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05M, Value = (decimal)_module.MinCircularity, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMinCirc.ValueChanged += (s, e) => { _module.MinCircularity = (double)numMinCirc.Value; _onDataChanged?.Invoke(); };

            Label lblMaxCirc = new Label { Text = "Max Circ:", ForeColor = Color_Text, Location = new Point(175, 175), Width = 75, Font = new Font("Segoe UI", 8) };
            numMaxCirc = new NumericUpDown { Location = new Point(255, 173), Width = 75, Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05M, Value = (decimal)_module.MaxCircularity, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMaxCirc.ValueChanged += (s, e) => { _module.MaxCircularity = (double)numMaxCirc.Value; _onDataChanged?.Invoke(); };

            // Elvárt darabszám és Sub-Pipeline kapcsoló
            Label lblExpected = new Label { Text = "Expected Count:", ForeColor = Color_Text, Location = new Point(10, 207), Width = 100, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            numExpectedCount = new NumericUpDown { Location = new Point(115, 205), Width = 60, Minimum = 0, Maximum = 500, Value = _module.ExpectedBlobCount, BackColor = Color_Light, ForeColor = Color_Cyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle };
            numExpectedCount.ValueChanged += (s, e) => { _module.ExpectedBlobCount = (int)numExpectedCount.Value; _onDataChanged?.Invoke(); };

            chkCreateSub = new CheckBox { Text = "Start Sub-Loop", ForeColor = Color_Text, Location = new Point(190, 206), AutoSize = true, Checked = _module.CreateSubContexts, Font = new Font("Segoe UI", 8) };
            chkCreateSub.CheckedChanged += (s, e) => { _module.CreateSubContexts = chkCreateSub.Checked; _onDataChanged?.Invoke(); };

            // --- SZÍNVÁLASZTÓ CHECKBOXOK (Y: 235) ---
            Label lblColorFilter = new Label { Text = "Blob Color:", ForeColor = Color_Text, Location = new Point(10, 236), Width = 80, Font = new Font("Segoe UI", 8, FontStyle.Bold) };

            CheckBox chkWhiteBlobs = new CheckBox
            {
                Text = "White",
                ForeColor = Color_Text,
                Location = new Point(95, 235),
                AutoSize = true,
                Checked = _module.SearchWhiteBlobs,
                Font = new Font("Segoe UI", 8.5f)
            };
            chkWhiteBlobs.CheckedChanged += (s, e) =>
            {
                _module.SearchWhiteBlobs = chkWhiteBlobs.Checked;
                _onDataChanged?.Invoke();
            };

            CheckBox chkBlackBlobs = new CheckBox
            {
                Text = "Black",
                ForeColor = Color_Text,
                Location = new Point(175, 235),
                AutoSize = true,
                Checked = _module.SearchBlackBlobs,
                Font = new Font("Segoe UI", 8.5f)
            };
            chkBlackBlobs.CheckedChanged += (s, e) =>
            {
                _module.SearchBlackBlobs = chkBlackBlobs.Checked;
                _onDataChanged?.Invoke();
            };



            // --- 3. SZAKASZ: DIAGNOSZTIKA ÉS LED (Lejjebb csúsztatva: Y: 270) ---
            lblExecutionTime = new Label { Text = "TIME: 0 ms", ForeColor = Color_Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 270), AutoSize = true };
            panelLedIndicator = new Panel { Location = new Point(260, 267), Size = new Size(70, 24), BackColor = Color.DarkGray, BorderStyle = BorderStyle.FixedSingle };
            lblLedText = new Label { Text = "WAIT", ForeColor = Color.Black, Font = new Font("Segoe UI", 8, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            panelLedIndicator.Controls.Add(lblLedText);

            // --- 4. SZAKASZ: TALÁLATI LISTA (Lejjebb csúsztatva: Y: 305) ---
            Label lblSection3 = new Label { Text = "MEGTALÁLT FOLTOK (BLOB RESULTS)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 305), AutoSize = true };
            listViewResults = new ListView { Location = new Point(10, 325), Size = new Size(320, 220), BackColor = Color_Slate, ForeColor = Color_Text, BorderStyle = BorderStyle.None, View = View.Details, FullRowSelect = true, GridLines = true, Font = new Font("Segoe UI", 8.5f) }; listViewResults.Columns.Add("#", 35, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Area (px)", 75, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Circ.", 55, HorizontalAlignment.Center);
            listViewResults.Columns.Add("X", 75, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Y", 75, HorizontalAlignment.Center);

            this.Controls.AddRange(new Control[] {
                lblSection1, btnSelectRoi, btnResetRoi, btnTest,
                lblSection2, lblMinArea, numMinArea, lblMaxArea, numMaxArea,
                lblMinCirc, numMinCirc, lblMaxCirc, numMaxCirc,
                lblExpected, numExpectedCount, chkCreateSub,
                lblExecutionTime, panelLedIndicator, lblSection3, listViewResults, lblColorFilter, chkWhiteBlobs, chkBlackBlobs
            });
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