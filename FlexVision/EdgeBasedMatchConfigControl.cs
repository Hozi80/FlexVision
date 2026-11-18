using System;
using System.Drawing;
using System.Windows.Forms;
using Cyotek.Windows.Forms.Demo;
using Cyotek.Windows.Forms;
using AOI.Inspections.EdgeBasedTemplateDetection; // A te egyedi OpenCL névtered!
using Emgu.CV.Structure;
using Emgu.CV;

namespace FlexVision
{
    public class EdgeBasedMatchConfigControl : UserControl
    {
        // Ipari sötét színpaletta szinkronizáció
        private readonly Color Color_Bg = Color.FromArgb(30, 30, 30);
        private readonly Color Color_Slate = Color.FromArgb(45, 45, 45);
        private readonly Color Color_Light = Color.FromArgb(60, 60, 60);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        private EdgeBasedMatchModule _module;
        private Action _onDataChanged;
        private PipelineEditorControl _mainEditor;

        // UI Kezelőelemek
        private PictureBox picTemplatePreview;
        private Label lblTemplateCounter;
        private Button btnPrev, btnNext, btnDeleteTemplate, btnEditTemplate;
        private Button btnSelectTemplate, btnSelectRoi, btnTest;

        // Algoritmus paraméterek beviteli mezői
        private NumericUpDown numLowThreshold, numHighThreshold;
        private NumericUpDown numScore, numAngle, numMinEdgePoints, numExpectedCount;
        private ComboBox comboAccuracy;

        // Élő diagnosztikai visszajelzők
        private ListView listViewResults;
        private Label lblExecutionTime;
        private Label lblHardwareStatus;
        private Panel panelLedIndicator;
        private Label lblLedText;

        public EdgeBasedMatchConfigControl(EdgeBasedMatchModule module, Action onDataChanged, PipelineEditorControl parentEditor)
        {
            this._module = module;
            this._onDataChanged = onDataChanged;
            this._mainEditor = parentEditor;

            this.BackColor = Color_Bg;
            this.Dock = DockStyle.Fill;

            InitializeComponent();
            UpdateTemplateUI();
        }

        private void InitializeComponent()
        {
            this.AutoScroll = true;

            // ====================================================================
            // 1. ADAG: SABLON MENEDZSER (MULTI-TEMPLATE CASCADE)
            // ====================================================================
            Label lblSection1 = new Label { Text = "ÉLALAPÚ MULTI-SABLON MENEDZSER", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true };

            picTemplatePreview = new PictureBox { Location = new Point(10, 25), Size = new Size(320, 320), BackColor = Color.FromArgb(15, 15, 15), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };

            btnPrev = CreateControlBtn("◀", 10, 352, 100, 28);
            lblTemplateCounter = new Label { Text = "0 / 0", ForeColor = Color_Text, Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(115, 352), Size = new Size(110, 28), TextAlign = ContentAlignment.MiddleCenter };
            btnNext = CreateControlBtn("▶", 230, 352, 100, 28);
            btnDeleteTemplate = CreateControlBtn("❌ DELETE TEMPLATE", 10, 386, 157, 28, Color.FromArgb(255, 120, 10));

            btnPrev.Click += (s, e) => { if (_module.Templates.Count > 0) { _module.ActiveTemplateIndex = (_module.ActiveTemplateIndex - 1 + _module.Templates.Count) % _module.Templates.Count; UpdateTemplateUI(); _mainEditor?.RunPipelineDebug(); } };
            btnNext.Click += (s, e) => { if (_module.Templates.Count > 0) { _module.ActiveTemplateIndex = (_module.ActiveTemplateIndex + 1) % _module.Templates.Count; UpdateTemplateUI(); _mainEditor?.RunPipelineDebug(); } };
            btnDeleteTemplate.Click += BtnDeleteTemplate_Click;


            btnEditTemplate = CreateControlBtn("✏️ EDIT TEMPLATE", 173, 386, 157, 28, Color_Cyan);
            btnEditTemplate.Click += (s, e) => {
                // 1. Ellenőrizzük, hogy van-e kiválasztott sablon a listában
                if (_module.Templates == null || _module.Templates.Count == 0)
                {
                    MessageBox.Show("Nincs szerkeszthető sablon! Előbb taníts be egyet a TEACH gombbal.", "Nincs sablon", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int idx = Math.Max(0, Math.Min(_module.ActiveTemplateIndex, _module.Templates.Count - 1));

                // 2. Készítünk egy tiszta másolatot a meglévő sablonképből a szerkesztőnek
                using (Bitmap currentTemplateCopy = new Bitmap(_module.Templates[idx]))
                {
                    // 3. Megnyitjuk a Paint/Élszerkesztő ablakot a meglévő képpel
                    using (TemplateTrainerForm trainer = new TemplateTrainerForm(_module, currentTemplateCopy))
                    {
                        if (trainer.ShowDialog() == DialogResult.OK)
                        {
                            // 4. Ha a mentésre kattintottál, frissítjük a felületet és újrafuttatjuk a mérést
                            UpdateTemplateUI();
                            _mainEditor?.RunPipelineDebug();
                            _onDataChanged?.Invoke();
                        }
                    }
                }
            };

            // ====================================================================
            // 2. ADAG: INTERAKTÍV AKCIÓGOMBOK
            // ====================================================================
            btnSelectTemplate = CreateControlBtn("🎯 TEACH EDGE TEMPLATE", 10, 424, 320, 32, Color_Cyan);
            btnSelectTemplate.Click += (s, e) => {
                if (_mainEditor != null)
                {
                    var imageBox = _mainEditor.Controls.Find("imageBoxExMain", true);
                    // JAVÍTVA: imageBox[0] indexelés visszaállítva!
                    if (imageBox.Length > 0 && imageBox[0] is ImageBoxEx cyotekBox)
                    {
                        cyotekBox.SelectionMode = ImageBoxSelectionMode.Rectangle;
                        cyotekBox.AutoPan = false;
                        cyotekBox.Cursor = Cursors.Cross;
                        _mainEditor.StartTemplateSelection();
                    }
                }
            };

            btnSelectRoi = CreateControlBtn("🗺️ ROI SELECT", 10, 462, 157, 30);
            btnSelectRoi.Click += (s, e) => {
                if (_mainEditor != null) _mainEditor.StartRoiSelection();
            };

            Button btnResetRoi = CreateControlBtn("✖ RESET ROI", 173, 462, 157, 30, Color.Tomato);
            btnResetRoi.Click += (s, e) => {
                _module.UseROI = false;
                _module.ROI = Rectangle.Empty;
                _mainEditor?.RunPipelineDebug();
                _onDataChanged?.Invoke();
            };

            btnTest = CreateControlBtn("▶ TEST EDGE MATCH", 10, 500, 320, 35, Color.LightGreen);
            btnTest.ForeColor = Color.Black;
            btnTest.Click += BtnTest_Click;

            // ====================================================================
            // 3. ADAG: ALGORITMUS KORLÁTOK ÉS PARAMÉTEREK
            // ====================================================================
            Label lblSection2 = new Label { Text = "EDGE ALGORITMUS PARAMÉTEREK", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 550), AutoSize = true };

            Label lblLowThresh = new Label { Text = "Low Thresh:", ForeColor = Color_Text, Location = new Point(10, 575), Width = 75, Font = new Font("Segoe UI", 8) };
            numLowThreshold = new NumericUpDown { Location = new Point(90, 573), Width = 65, Minimum = 0, Maximum = 255, Value = (decimal)_module.LowThreshold, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numLowThreshold.ValueChanged += (s, e) => { _module.LowThreshold = (float)numLowThreshold.Value; _onDataChanged?.Invoke(); };

            Label lblHighThresh = new Label { Text = "High Thresh:", ForeColor = Color_Text, Location = new Point(170, 575), Width = 75, Font = new Font("Segoe UI", 8) };
            numHighThreshold = new NumericUpDown { Location = new Point(255, 573), Width = 65, Minimum = 0, Maximum = 255, Value = (decimal)_module.HighThreshold, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numHighThreshold.ValueChanged += (s, e) => { _module.HighThreshold = (float)numHighThreshold.Value; _onDataChanged?.Invoke(); };

            Label lblScore = new Label { Text = "Min Score:", ForeColor = Color_Text, Location = new Point(10, 605), Width = 75, Font = new Font("Segoe UI", 8) };
            numScore = new NumericUpDown { Location = new Point(90, 603), Width = 65, Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05M, Value = (decimal)_module.MinScore, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numScore.ValueChanged += (s, e) => { _module.MinScore = (float)numScore.Value; _onDataChanged?.Invoke(); };

            Label lblAngle = new Label { Text = "Tolerance ∠:", ForeColor = Color_Text, Location = new Point(170, 605), Width = 75, Font = new Font("Segoe UI", 8) };
            numAngle = new NumericUpDown { Location = new Point(255, 603), Width = 65, Minimum = 0, Maximum = 180, Value = (decimal)_module.AngleDetectionRangeSimmetric, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numAngle.ValueChanged += (s, e) => { _module.AngleDetectionRangeSimmetric = (int)numAngle.Value; _onDataChanged?.Invoke(); };

            Label lblMinPoints = new Label { Text = "Min Edge Pts:", ForeColor = Color_Text, Location = new Point(10, 635), Width = 75, Font = new Font("Segoe UI", 8) };
            numMinEdgePoints = new NumericUpDown { Location = new Point(90, 633), Width = 65, Minimum = 1, Maximum = 5000, Value = _module.MinEdgePoints, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMinEdgePoints.ValueChanged += (s, e) => { _module.MinEdgePoints = (int)numMinEdgePoints.Value; _onDataChanged?.Invoke(); };

            Label lblExpected = new Label { Text = "Obj. Count:", ForeColor = Color_Text, Location = new Point(170, 635), Width = 75, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            numExpectedCount = new NumericUpDown { Location = new Point(255, 633), Width = 65, Minimum = 1, Maximum = 100, Value = _module.ExpectedObjectCount, BackColor = Color_Light, ForeColor = Color_Cyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle };
            numExpectedCount.ValueChanged += (s, e) => { _module.ExpectedObjectCount = (int)numExpectedCount.Value; _mainEditor?.RunPipelineDebug(); _onDataChanged?.Invoke(); };

            Label lblAccuracy = new Label { Text = "GPU Precision:", ForeColor = Color_Text, Location = new Point(10, 665), Width = 80, Font = new Font("Segoe UI", 8) };
            comboAccuracy = new ComboBox { Location = new Point(90, 663), Width = 110, BackColor = Color_Light, ForeColor = Color_Text, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8) };
            comboAccuracy.DropDownStyle = ComboBoxStyle.DropDownList;
            comboAccuracy.Items.AddRange(new string[] { "1 - Very Fast", "2 - Balanced", "3 - High Precision" });
            comboAccuracy.SelectedIndex = Math.Max(0, Math.Min(2, _module.AccuracyLevel - 1));
            comboAccuracy.SelectedIndexChanged += (s, e) => { _module.AccuracyLevel = comboAccuracy.SelectedIndex + 1; _onDataChanged?.Invoke(); };

            lblHardwareStatus = new Label { Text = $"Engine: {_module.AccelerationEngine}", ForeColor = HardwareAccelerator.IsOpenClAvailable ? Color.LightGreen : Color.Orange, Font = new Font("Segoe UI", 7.5f, FontStyle.Italic), Location = new Point(210, 666), Width = 120, TextAlign = ContentAlignment.MiddleRight };

            // ====================================================================
            // 4. ADAG: KÖRNYEZETI JELZŐK ÉS DIGITÁLIS LED
            // ====================================================================
            lblExecutionTime = new Label { Text = "TIME: 0 ms", ForeColor = Color_Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 698), AutoSize = true };
            panelLedIndicator = new Panel { Location = new Point(250, 695), Size = new Size(70, 24), BackColor = Color.DarkGray, BorderStyle = BorderStyle.FixedSingle };
            lblLedText = new Label { Text = "WAIT", ForeColor = Color.Black, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(0, 0), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            panelLedIndicator.Controls.Add(lblLedText);

            // ====================================================================
            // 5. ADAG: TALÁLATI LISTA (VIEW)
            // ====================================================================
            Label lblSection3 = new Label { Text = "ÉLILLESZTÉSI SUB-PIXEL EREDMÉNYEK", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 730), AutoSize = true };
            listViewResults = new ListView { Location = new Point(10, 750), Size = new Size(320, 120), BackColor = Color_Slate, ForeColor = Color_Text, BorderStyle = BorderStyle.None, View = View.Details, FullRowSelect = true, GridLines = true, Font = new Font("Segoe UI", 8.5f) };
            listViewResults.Columns.Add("Index", 45, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Score", 65, HorizontalAlignment.Center);
            listViewResults.Columns.Add("X (Col)", 65, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Y (Row)", 65, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Angle", 60, HorizontalAlignment.Center);

            Label lblBottomSpacer = new Label { Location = new Point(10, 880), Size = new Size(10, 15) };

            this.Controls.AddRange(new Control[] {
                lblSection1, picTemplatePreview, btnPrev, lblTemplateCounter, btnNext, btnDeleteTemplate,btnEditTemplate,
btnSelectTemplate, btnSelectRoi, btnResetRoi, btnTest,
lblSection2, lblLowThresh, numLowThreshold, lblHighThresh, numHighThreshold,
lblScore, numScore, lblAngle, numAngle, lblMinPoints, numMinEdgePoints,
lblExpected, numExpectedCount, lblAccuracy, comboAccuracy, lblHardwareStatus,
lblExecutionTime, panelLedIndicator, lblSection3, listViewResults, lblBottomSpacer
});
        }

        // Villódzásmentes, katonás élsimított gombgyártó rekesz
        private Button CreateControlBtn(string text, int x, int y, int w, int h, Color customColor = default(Color))
        {
            Color finalTextColor = customColor.IsEmpty ? Color_Text : customColor;
            Button btn = new Button { Text = "", Location = new Point(x, y), Width = w, Height = h, FlatStyle = FlatStyle.Flat, BackColor = Color_Light, Cursor = Cursors.Hand };
            btn.FlatAppearance.BorderSize = 0;
            btn.Paint += (s, ev) => {
                ev.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                ev.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (SolidBrush textBrush = new SolidBrush(finalTextColor))
                {
                    using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        Rectangle textRect = new Rectangle(0, 0, btn.Width, btn.Height);
                        ev.Graphics.DrawString(text, new Font("Segoe UI", 7.5f, FontStyle.Bold), textBrush, textRect, sf);
                    }
                }
            };
            return btn;
        }
        private void UpdateTemplateUI()
        {
            if (_module.Templates.Count > 0)
            {
                if (_module.ActiveTemplateIndex >= _module.Templates.Count) _module.ActiveTemplateIndex = 0;
                picTemplatePreview.Image = _module.Templates[_module.ActiveTemplateIndex];
                lblTemplateCounter.Text = $"{_module.ActiveTemplateIndex + 1} / {_module.Templates.Count}";
                btnDeleteTemplate.Enabled = true; btnTest.Enabled = true;
            }
            else
            {
                picTemplatePreview.Image = null; lblTemplateCounter.Text = "0 / 0";
                btnDeleteTemplate.Enabled = false; btnTest.Enabled = false;
            }
            lblHardwareStatus.Text = $"Engine: {_module.AccelerationEngine}";
            lblHardwareStatus.ForeColor = HardwareAccelerator.IsOpenClAvailable ? Color.LightGreen : Color.Orange;
        }
        private void BtnDeleteTemplate_Click(object sender, EventArgs e)
        {
            if (_module.Templates.Count > 0)
            {
                _module.Templates.RemoveAt(_module.ActiveTemplateIndex);
                if (_module.ActiveTemplateIndex > 0) _module.ActiveTemplateIndex--;
                UpdateTemplateUI();
                _onDataChanged?.Invoke();
            }
        }
        // ====================================================================
        // --- JAVÍTÁS: MULTI-MATCH ADAPTÍV TESZTELŐ ÉS LED VEZÉRLŐ ---
        // ====================================================================
        private void BtnTest_Click(object sender, EventArgs e)
        {
            // Elindítjuk a lánc újrafuttatását (ami a végén meghívja a RefreshResultsFromModule-t is)
            _onDataChanged?.Invoke();

            // Biztonsági azonnali frissítés
            RefreshResultsFromModule();
        }

        // Ezt a publikus metódust hívja meg a PipelineEditorControl is, amikor lapozol a képek között!
        public void RefreshResultsFromModule()
        {
            lblExecutionTime.Text = $"TIME: {_module.LastExecutionTime}";
            lblHardwareStatus.Text = $"Engine: {_module.AccelerationEngine}";

            // IPARI LED SZIMULÁCIÓS PANEL ÉS SZÖVEG ÁTVÁLTÁS
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

            // Tizedespontosságú találati lista ListView feltöltése (Tengely-korrigált Shiju rács szerint)
            listViewResults.Items.Clear();
            if (_module.LastMatches != null)
            {
                for (int i = 0; i < _module.LastMatches.Count; i++)
                {
                    var match = _module.LastMatches[i];
                    ListViewItem item = new ListViewItem(i.ToString());
                    item.SubItems.Add(match.Score.ToString("F2"));
                    item.SubItems.Add(match.Y.ToString("F1")); // Valós képernyős X koordináta (Oszlop)
                    item.SubItems.Add(match.X.ToString("F1")); // Valós képernyős Y koordináta (Sor)
                    item.SubItems.Add(match.Angle.ToString("F1") + "°");
                    listViewResults.Items.Add(item);
                }
            }
        }

        // ====================================================================
        // --- EXKLUZÍV JAVÍTÁS: AUTOMATIKUS OFFLINE MODELMENTŐ (TE LOGIKÁD) ---
        // ====================================================================
        public void AddNewTemplateFromBmp(Bitmap croppedBmp)
        {
            if (croppedBmp == null) return;

            // ====================================================================
            // --- JAVÍTÁS: A HÁTTÉRFUTTATÁS HELYETT FELNYITJUK A TRAINER ABLAKOT ---
            // ====================================================================
            // Példányosítjuk az interaktív ablakot, átadva neki a modult és a kivágott képet
            using (TemplateTrainerForm trainerForm = new TemplateTrainerForm(_module, croppedBmp))
            {
                // Megnyitjuk modális (felugró, fókuszált) formában a felületet
                if (trainerForm.ShowDialog() == DialogResult.OK)
                {
                    // Ha a mérnök rákattintott a mentés gombra az ablakban, az adatok már a modulban vannak!
                    // Frissítjük a kis PictureBox preview-t a memóriából visszafejtett gradiens élekkel
                    picTemplatePreview.Image = _module.GeneratedTemplatePreview;

                    // UI és pipeline újrarajzolás kényszerítése
                    UpdateTemplateUI();
                    _mainEditor?.RefreshUI();
                    _mainEditor?.RunPipelineDebug();
                }
            }
        }

    }
}