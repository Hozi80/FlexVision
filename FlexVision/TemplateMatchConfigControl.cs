using Cyotek.Windows.Forms.Demo;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlexVision
{
    public class TemplateMatchConfigControl : UserControl
    {
        private readonly Color Color_Bg = Color.FromArgb(30, 30, 30);
        private readonly Color Color_Slate = Color.FromArgb(45, 45, 45);
        private readonly Color Color_Light = Color.FromArgb(60, 60, 60);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        private TemplateMatchModule _module;
        private Action _onDataChanged;
        private PipelineEditorControl _mainEditor;

        // UI elemek
        private PictureBox picTemplatePreview;
        private Label lblTemplateCounter;
        private Button btnPrev, btnNext, btnDeleteTemplate;
        private Button btnSelectTemplate, btnSelectRoi, btnTest;

        // Paraméter beviteli mezők
        private NumericUpDown numScore, numAngle, numExpectedCount;
        private CheckBox checkPolarity;

        // Eredményjelzők
        private ListView listViewResults;
        private Label lblExecutionTime;
        private Panel panelLedIndicator; // A te gyári Led visszajelződ helye

        public TemplateMatchConfigControl(TemplateMatchModule module, Action onDataChanged, PipelineEditorControl parentEditor)
        {
            this._module = module;
            this._onDataChanged = onDataChanged;
            this._mainEditor = parentEditor; // Itt adjuk át a hidat!

            this.BackColor = Color_Bg;
            this.Dock = DockStyle.Fill;

            InitializeComponent();
            UpdateTemplateUI();
        }

        private void InitializeComponent()
        {
            // Görgetősáv engedélyezése a megnövelt magasság miatt
            this.AutoScroll = true;

            // 1. SZAKASZ: SABLON INTERAKTÍV LAPOZÓ ÉS NAGY NÉGYZETES ELŐNÉZET
            Label lblSection1 = new Label { Text = "SABLONOK MENEDZSERE (MULTI-TEMPLATE)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true };

            // 320x320 pixeles négyzet alakú előnézeti ablak a sablonnak
            picTemplatePreview = new PictureBox { Location = new Point(10, 25), Size = new Size(320, 320), BackColor = Color.FromArgb(15, 15, 15), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };

            // Alatta a lapozó gombok szimmetrikusan elosztva (Y: 352)
            btnPrev = CreateControlBtn("◀", 10, 352, 100, 28);
            lblTemplateCounter = new Label { Text = "0 / 0", ForeColor = Color_Text, Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(115, 352), Size = new Size(110, 28), TextAlign = ContentAlignment.MiddleCenter };
            btnNext = CreateControlBtn("▶", 230, 352, 100, 28);

            // Alatta a törlés gomb teljes szélességben (Y: 386)
            btnDeleteTemplate = CreateControlBtn("❌ DELETE TEMPLATE", 10, 386, 320, 28, Color.FromArgb(255, 120, 10));

            btnPrev.Click += (s, e) => { if (_module.Templates.Count > 0) { _module.ActiveTemplateIndex = (_module.ActiveTemplateIndex - 1 + _module.Templates.Count) % _module.Templates.Count; UpdateTemplateUI(); } };
            btnNext.Click += (s, e) => { if (_module.Templates.Count > 0) { _module.ActiveTemplateIndex = (_module.ActiveTemplateIndex + 1) % _module.Templates.Count; UpdateTemplateUI(); } };
            btnDeleteTemplate.Click += BtnDeleteTemplate_Click;

            // 2. SZAKASZ: AKCIÓ GOMBOK (SELECT, ROI, TEST)
            // Select New Template teljes szélességben (Y: 424)
            btnSelectTemplate = CreateControlBtn("🎯 SELECT NEW TEMPLATE", 10, 424, 320, 32, Color_Cyan);
            btnSelectTemplate.Click += (s, e) => {
                if (_mainEditor != null)
                {
                    var imageBox = _mainEditor.Controls.Find("imageBoxExMain", true);
                    if (imageBox.Length > 0 && imageBox[0] is ImageBoxEx cyotekBox)
                    {
                        cyotekBox.SelectionMode = Cyotek.Windows.Forms.ImageBoxSelectionMode.Rectangle;
                        cyotekBox.AutoPan = false;
                        cyotekBox.Cursor = Cursors.Cross;
                        _mainEditor.StartTemplateSelection();
                    }
                }
            };

            // ROI Select és ROI Reset egymás mellett (Y: 462)
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

            // Test Match teljes szélességben (Y: 500)
            btnTest = CreateControlBtn("▶ TEST MATCH", 10, 500, 320, 35, Color.LightGreen);
            btnTest.Click += BtnTest_Click;
            btnTest.ForeColor = Color.Black;

            // 3. SZAKASZ: MATCH PARAMETERS (Y: 550-től)
            Label lblSection2 = new Label { Text = "MATCH PARAMETERS", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 550), AutoSize = true };

            Label lblScore = new Label { Text = "Min Score:", ForeColor = Color_Text, Location = new Point(10, 575), AutoSize = true };
            numScore = new NumericUpDown { Location = new Point(90, 573), Width = 65, Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05M, Value = (decimal)_module.MatchParams.Score, BackColor = Color_Light, ForeColor = Color_Text };
            numScore.ValueChanged += (s, e) => { _module.MatchParams.Score = (double)numScore.Value; };

            Label lblAngle = new Label { Text = "Tolerance ∠:", ForeColor = Color_Text, Location = new Point(170, 575), AutoSize = true };
            numAngle = new NumericUpDown { Location = new Point(255, 573), Width = 65, Minimum = 0, Maximum = 360, Value = (decimal)_module.MatchParams.ToleranceAngle, BackColor = Color_Light, ForeColor = Color_Text };
            numAngle.ValueChanged += (s, e) => { _module.MatchParams.ToleranceAngle = (double)numAngle.Value; };

            checkPolarity = new CheckBox { Text = "Use Polarity Check", ForeColor = Color_Text, Location = new Point(10, 605), AutoSize = true, Checked = _module.MatchParams.PolarityCheck };
            checkPolarity.CheckedChanged += (s, e) => { _module.MatchParams.PolarityCheck = checkPolarity.Checked; };

            Label lblExpected = new Label { Text = "Obj. Count:", ForeColor = Color_Text, Location = new Point(170, 605), AutoSize = true, Font = new Font("Segoe UI", 8, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            numExpectedCount = new NumericUpDown
            {
                Location = new Point(255, 603),
                Width = 65,
                Minimum = 1,
                Maximum = 100,
                Value = _module.ExpectedObjectCount,
                BackColor = Color_Light,
                ForeColor = Color_Cyan,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center
            };
            numExpectedCount.ValueChanged += (s, e) => {
                _module.ExpectedObjectCount = (int)numExpectedCount.Value;
                _mainEditor?.RunPipelineDebug();
            };

            // 4. SZAKASZ: DIAGNOSZTIKA ÉS LED KIJELZŐ (Y: 640)
            lblExecutionTime = new Label { Text = "TIME: 0 ms", ForeColor = Color_Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 640), AutoSize = true };

            panelLedIndicator = new Panel { Location = new Point(250, 637), Size = new Size(70, 24), BackColor = Color.DarkGray, BorderStyle = BorderStyle.FixedSingle };
            Label lblLedText = new Label { Text = "WAIT", ForeColor = Color.Black, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(15, 4), AutoSize = true };
            panelLedIndicator.Controls.Add(lblLedText);

            // 5. SZAKASZ: LISTVIEW AZ EREDMÉNYEKNEK (Y: 675)
            Label lblSection3 = new Label { Text = "TARGET MATCH RESULTS", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 675), AutoSize = true };

            listViewResults = new ListView { Location = new Point(10, 695), Size = new Size(320, 150), BackColor = Color_Slate, ForeColor = Color_Text, BorderStyle = BorderStyle.None, View = View.Details, FullRowSelect = true, GridLines = true };
            listViewResults.Columns.Add("Index", 45);
            listViewResults.Columns.Add("Score", 65);
            listViewResults.Columns.Add("X", 55);
            listViewResults.Columns.Add("Y", 55);
            listViewResults.Columns.Add("Angle", 65);

            // Alsó margó biztosítása görgetéshez
            Label lblBottomSpacer = new Label { Location = new Point(10, 855), Size = new Size(10, 15) };

            this.Controls.AddRange(new Control[] {
        lblSection1, picTemplatePreview, btnPrev, lblTemplateCounter, btnNext, btnDeleteTemplate,
        btnSelectTemplate, btnSelectRoi, btnResetRoi, btnTest,
        lblSection2, lblScore, numScore, lblAngle, numAngle, checkPolarity, lblExpected, numExpectedCount,
        lblExecutionTime, panelLedIndicator, lblSection3, listViewResults, lblBottomSpacer
    });
        }



        /*
        private Button CreateControlBtn(string text, int x, int y, int w, int h, Color? customColor = null)
        {
            Button btn = new Button { Text = text, Location = new Point(x, y), Width = w, Height = h, FlatStyle = FlatStyle.Flat, ForeColor = customColor ?? Color_Text, BackColor = Color_Light, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }
        
        // ====================================================================
        // --- JAVÍTÁS: TISZTA COLOR ALAPÚ UNIVERZÁLIS GOMB GYÁRTÓ RETESZ ---
        // ====================================================================
        private Button CreateControlBtn(string text, int x, int y, int w, int h, Color customColor = default(Color))
        {
            // Megvizsgáljuk, hogy átadtunk-e egyedi színt. Ha üres (default), a Color_Text-et használjuk
            Color finalTextColor = customColor.IsEmpty ? Color_Text : customColor;

            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Width = w,
                Height = h,
                FlatStyle = FlatStyle.Flat,
                ForeColor = finalTextColor, // Garantáltan tiszta, nem-nullable struktúrát kap a WinForms!
                BackColor = Color_Light,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };

            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }
        */

        // ====================================================================
        // --- VÉGLEGES JAVÍTÁS: GOLYÓÁLLÓ NATÍV PAINT OVERRIDE RETESZ ---
        // ====================================================================
        private Button CreateControlBtn(string text, int x, int y, int w, int h, Color customColor = default(Color))
        {
            Color finalTextColor = customColor.IsEmpty ? Color_Text : customColor;

            Button btn = new Button
            {
                Text = "", // KIKOMMENTEZZÜK A GYÁRI FELIRATOT, hogy ne rajzolja alá duplán a Windows!
                Location = new Point(x, y),
                Width = w,
                Height = h,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color_Light,
                Cursor = Cursors.Hand
            };

            btn.FlatAppearance.BorderSize = 0;

            // --- HARDVERES GRAFIKUS FELÜLBÍRÁLÓ KAPCSOLÓ ---
            btn.Paint += (s, ev) => {
                // Megkérjük a grafikus kártyát, hogy használjon élsimítást a tiszta szövegért
                ev.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                ev.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // Létrehozzuk a garantáltan a kért színű (pl. LightGreen) egyedi ipari ecsetet
                using (SolidBrush textBrush = new SolidBrush(finalTextColor))
                {
                    // Beállítjuk a katonás, középre igazított szövegformázást
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;

                        // Tűpontosan a gomb mértani közepére rajzoljuk a szöveget és a ▶ ikont!
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
                btnDeleteTemplate.Enabled = true;
                btnTest.Enabled = true;
            }
            else
            {
                picTemplatePreview.Image = null;
                lblTemplateCounter.Text = "0 / 0";
                btnDeleteTemplate.Enabled = false;
                btnTest.Enabled = false;
            }
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

        private void BtnTest_Click(object sender, EventArgs e)
        {
            _onDataChanged?.Invoke(); // Elindítja a Pipeline újrafuttatását!

            // Frissítjük a LED-et, az időt és a ListView-t a modul kimenetei alapján
            lblExecutionTime.Text = $"TIME: {_module.LastExecutionTime}";

            panelLedIndicator.Controls[0].Text = _module.LastTestResult.Contains("PASS") ? "PASS" : "FAIL";
            panelLedIndicator.BackColor = _module.LastTestResult.Contains("PASS") ? Color.LimeGreen : Color.Red;

            listViewResults.Items.Clear();
            for (int i = 0; i < _module.LastMatches.Count; i++)
            {
                var match = _module.LastMatches[i];
                ListViewItem item = new ListViewItem(i.ToString());
                item.SubItems.Add(match.Score.ToString("F2"));
                item.SubItems.Add(match.CenterPoint.X.ToString());
                item.SubItems.Add(match.CenterPoint.Y.ToString());
                item.SubItems.Add(match.Angle.ToString("F1") + "°");
                listViewResults.Items.Add(item);
            }
        }

        // Publikus kampó, amivel a főablakból az egérrel kivágott képet közvetlenül be tudjuk dobni sablonnak!
        public void AddNewTemplateFromBmp(Bitmap croppedBmp)
        {
            if (croppedBmp == null) return;
            _module.Templates.Add(new Bitmap(croppedBmp));
            _module.ActiveTemplateIndex = _module.Templates.Count - 1;
            UpdateTemplateUI();
        }
    }
}