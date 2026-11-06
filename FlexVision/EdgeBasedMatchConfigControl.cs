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
        private Button btnPrev, btnNext, btnDeleteTemplate;
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
            // ====================================================================
            // 1. ADAG: SABLON MENEDZSER (MULTI-TEMPLATE CASCADE)
            // ====================================================================
            Label lblSection1 = new Label { Text = "ÉLALAPÚ MULTI-SABLON MENEDZSER", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true };

            picTemplatePreview = new PictureBox { Location = new Point(10, 25), Size = new Size(120, 100), BackColor = Color.FromArgb(15, 15, 15), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };

            btnPrev = CreateControlBtn("◀", 140, 25, 35, 30);
            lblTemplateCounter = new Label { Text = "0 / 0", ForeColor = Color_Text, Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(180, 32), AutoSize = true };
            btnNext = CreateControlBtn("▶", 230, 25, 35, 30);
            btnDeleteTemplate = CreateControlBtn("❌ DELETE", 140, 65, 125, 30, Color.FromArgb(255, 120, 10));

            btnPrev.Click += (s, e) => { if (_module.Templates.Count > 0) { _module.ActiveTemplateIndex = (_module.ActiveTemplateIndex - 1 + _module.Templates.Count) % _module.Templates.Count; UpdateTemplateUI(); _mainEditor?.RunPipelineDebug(); } };
            btnNext.Click += (s, e) => { if (_module.Templates.Count > 0) { _module.ActiveTemplateIndex = (_module.ActiveTemplateIndex + 1) % _module.Templates.Count; UpdateTemplateUI(); _mainEditor?.RunPipelineDebug(); } };
            btnDeleteTemplate.Click += BtnDeleteTemplate_Click;

            // ====================================================================
            // 2. ADAG: INTERAKTÍV AKCIÓGOMBOK
            // ====================================================================
            btnSelectTemplate = CreateControlBtn("🎯 TEACH EDGE TEMPLATE", 10, 135, 160, 32, Color_Cyan);
            btnSelectTemplate.Click += (s, e) => {
                if (_mainEditor != null)
                {
                    var imageBox = _mainEditor.Controls.Find("imageBoxExMain", true);
                    if (imageBox.Length > 0 && imageBox[0] is ImageBoxEx cyotekBox)
                    {
                        cyotekBox.SelectionMode = ImageBoxSelectionMode.Rectangle;
                        cyotekBox.AutoPan = false;
                        cyotekBox.Cursor = Cursors.Cross;
                        _mainEditor.StartTemplateSelection();
                    }
                }
            };

            btnSelectRoi = CreateControlBtn("🗺️ ROI SELECT", 180, 135, 150, 32);
            btnSelectRoi.Click += (s, e) => {
                MessageBox.Show("Húzza ki a kívánt keresési zónát (ROI) a fő Live View képernyőn!", "ROI Kijelölés", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            btnTest = CreateControlBtn("▶ TEST EDGE MATCH", 10, 175, 320, 35, Color.LightGreen);
            btnTest.ForeColor = Color.Black;
            btnTest.Click += BtnTest_Click;

            // ====================================================================
            // 3. ADAG: ALGORITMUS KORLÁTOK ÉS PARAMÉTEREK
            // ====================================================================
            Label lblSection2 = new Label { Text = "EDGE ALGORITMUS PARAMÉTEREK", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 222), AutoSize = true };

            // Küszöbértékek (Low / High)
            Label lblLowThresh = new Label { Text = "Low Thresh:", ForeColor = Color_Text, Location = new Point(10, 245), Width = 75, Font = new Font("Segoe UI", 8) };
            numLowThreshold = new NumericUpDown { Location = new Point(90, 243), Width = 65, Minimum = 0, Maximum = 255, Value = (decimal)_module.LowThreshold, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numLowThreshold.ValueChanged += (s, e) => { _module.LowThreshold = (float)numLowThreshold.Value; _onDataChanged?.Invoke(); };

            Label lblHighThresh = new Label { Text = "High Thresh:", ForeColor = Color_Text, Location = new Point(170, 245), Width = 75, Font = new Font("Segoe UI", 8) };
            numHighThreshold = new NumericUpDown { Location = new Point(255, 243), Width = 65, Minimum = 0, Maximum = 255, Value = (decimal)_module.HighThreshold, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numHighThreshold.ValueChanged += (s, e) => { _module.HighThreshold = (float)numHighThreshold.Value; _onDataChanged?.Invoke(); };

            // Min Score & Szög tolerancia
            Label lblScore = new Label { Text = "Min Score:", ForeColor = Color_Text, Location = new Point(10, 275), Width = 75, Font = new Font("Segoe UI", 8) };
            numScore = new NumericUpDown { Location = new Point(90, 273), Width = 65, Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05M, Value = (decimal)_module.MinScore, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numScore.ValueChanged += (s, e) => { _module.MinScore = (float)numScore.Value; _onDataChanged?.Invoke(); };

            Label lblAngle = new Label { Text = "Tolerance ∠:", ForeColor = Color_Text, Location = new Point(170, 275), Width = 75, Font = new Font("Segoe UI", 8) };
            numAngle = new NumericUpDown { Location = new Point(255, 273), Width = 65, Minimum = 0, Maximum = 180, Value = (decimal)_module.AngleDetectionRangeSimmetric, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numAngle.ValueChanged += (s, e) => { _module.AngleDetectionRangeSimmetric = (int)numAngle.Value; _onDataChanged?.Invoke(); };

            // Min Edge Points & Elvárt darabszám
            Label lblMinPoints = new Label { Text = "Min Edge Pts:", ForeColor = Color_Text, Location = new Point(10, 305), Width = 75, Font = new Font("Segoe UI", 8) };
            numMinEdgePoints = new NumericUpDown { Location = new Point(90, 303), Width = 65, Minimum = 1, Maximum = 5000, Value = _module.MinEdgePoints, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numMinEdgePoints.ValueChanged += (s, e) => { _module.MinEdgePoints = (int)numMinEdgePoints.Value; _onDataChanged?.Invoke(); };

            Label lblExpected = new Label { Text = "Obj. Count:", ForeColor = Color_Text, Location = new Point(170, 305), Width = 75, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            numExpectedCount = new NumericUpDown { Location = new Point(255, 303), Width = 65, Minimum = 1, Maximum = 100, Value = _module.ExpectedObjectCount, BackColor = Color_Light, ForeColor = Color_Cyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle };
            numExpectedCount.ValueChanged += (s, e) => { _module.ExpectedObjectCount = (int)numExpectedCount.Value; _mainEditor?.RunPipelineDebug(); _onDataChanged?.Invoke(); };

            // Engine Precíziós szint választó (Accuracy Stride)
            Label lblAccuracy = new Label { Text = "GPU Precision:", ForeColor = Color_Text, Location = new Point(10, 335), Width = 80, Font = new Font("Segoe UI", 8) };
            comboAccuracy = new ComboBox { Location = new Point(90, 333), Width = 110, BackColor = Color_Light, ForeColor = Color_Text, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8) };
            comboAccuracy.DropDownStyle = ComboBoxStyle.DropDownList;
            comboAccuracy.Items.AddRange(new string[] { "1 - Very Fast", "2 - Balanced", "3 - High Precision" });
            comboAccuracy.SelectedIndex = Math.Max(0, Math.Min(2, _module.AccuracyLevel - 1));
            comboAccuracy.SelectedIndexChanged += (s, e) => { _module.AccuracyLevel = comboAccuracy.SelectedIndex + 1; _onDataChanged?.Invoke(); };
            // OpenCL hardver architektúra kijelző
            lblHardwareStatus = new Label { Text = $"Engine: {_module.AccelerationEngine}", ForeColor = HardwareAccelerator.IsOpenClAvailable ? Color.LightGreen : Color.Orange, Font = new Font("Segoe UI", 7.5f, FontStyle.Italic), Location = new Point(210, 336), Width = 120, TextAlign = ContentAlignment.MiddleRight };
            // ====================================================================
            // 4. ADAG: KÖRNYEZETI JELZŐK ÉS DIGITÁLIS LED
            // ====================================================================
            lblExecutionTime = new Label { Text = "TIME: 0 ms", ForeColor = Color_Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 368), AutoSize = true };
            panelLedIndicator = new Panel { Location = new Point(250, 365), Size = new Size(70, 24), BackColor = Color.DarkGray, BorderStyle = BorderStyle.FixedSingle };
            lblLedText = new Label { Text = "WAIT", ForeColor = Color.Black, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(0, 0), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            panelLedIndicator.Controls.Add(lblLedText);
            // ====================================================================
            // 5. ADAG: TALÁLATI LISTA (VIEW)
            // ====================================================================
            Label lblSection3 = new Label { Text = "ÉLILLESZTÉSI SUB-PIXEL EREDMÉNYEK", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 400), AutoSize = true };
            listViewResults = new ListView { Location = new Point(10, 420), Size = new Size(320, 115), BackColor = Color_Slate, ForeColor = Color_Text, BorderStyle = BorderStyle.None, View = View.Details, FullRowSelect = true, GridLines = true, Font = new Font("Segoe UI", 8.5f) };
            listViewResults.Columns.Add("Index", 45, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Score", 65, HorizontalAlignment.Center);
            listViewResults.Columns.Add("X (Col)", 65, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Y (Row)", 65, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Angle", 60, HorizontalAlignment.Center);
            // Vezérlők felpakolása az ablakra
            this.Controls.Add(lblSection1); this.Controls.Add(picTemplatePreview); this.Controls.Add(lblTemplateCounter);
            this.Controls.Add(btnPrev); this.Controls.Add(btnNext); this.Controls.Add(btnDeleteTemplate);
            this.Controls.Add(btnSelectTemplate); this.Controls.Add(btnSelectRoi); this.Controls.Add(btnTest);
            this.Controls.Add(lblSection2);
            this.Controls.Add(lblLowThresh); this.Controls.Add(numLowThreshold);
            this.Controls.Add(lblHighThresh); this.Controls.Add(numHighThreshold);
            this.Controls.Add(lblScore); this.Controls.Add(numScore);
            this.Controls.Add(lblAngle); this.Controls.Add(numAngle);
            this.Controls.Add(lblMinPoints); this.Controls.Add(numMinEdgePoints);
            this.Controls.Add(lblExpected); this.Controls.Add(numExpectedCount);
            this.Controls.Add(lblAccuracy); this.Controls.Add(comboAccuracy); this.Controls.Add(lblHardwareStatus);
            this.Controls.Add(lblExecutionTime); this.Controls.Add(panelLedIndicator);
            this.Controls.Add(lblSection3); this.Controls.Add(listViewResults);
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
            // Elindítjuk a lánc újrafuttatását
            _onDataChanged?.Invoke();
            // Riasztási és ciklusidő feliratok frissítése
            lblExecutionTime.Text = $"TIME: {_module.LastExecutionTime}";
            lblHardwareStatus.Text = $"Engine: {_module.AccelerationEngine}";
            // IPARI LED SZIMULÁCIÓS PANEL ÉS SZÖVEG ÁTVÁLTÁS
            if (_module.LastTestResult.Contains("PASS"))
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