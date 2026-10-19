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
        private NumericUpDown numScore, numAngle, numMaxPos;
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
            // 1. SZAKASZ: SABLON INTERAKTÍV LAPOZÓ (MINI PICTUREBOX)
            Label lblSection1 = new Label { Text = "SABLONOK MENEDZSERE (MULTI-TEMPLATE)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true };

            picTemplatePreview = new PictureBox { Location = new Point(10, 25), Size = new Size(120, 100), BackColor = Color.FromArgb(15, 15, 15), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };

            btnPrev = CreateControlBtn("◀", 140, 25, 35, 30);
            lblTemplateCounter = new Label { Text = "0 / 0", ForeColor = Color_Text, Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(180, 32), AutoSize = true };
            btnNext = CreateControlBtn("▶", 230, 25, 35, 30);

            btnDeleteTemplate = CreateControlBtn("❌ DELETE", 140, 65, 125, 30, Color.FromArgb(255, 120, 10));

            btnPrev.Click += (s, e) => { if (_module.Templates.Count > 0) { _module.ActiveTemplateIndex = (_module.ActiveTemplateIndex - 1 + _module.Templates.Count) % _module.Templates.Count; UpdateTemplateUI(); } };
            btnNext.Click += (s, e) => { if (_module.Templates.Count > 0) { _module.ActiveTemplateIndex = (_module.ActiveTemplateIndex + 1) % _module.Templates.Count; UpdateTemplateUI(); } };
            btnDeleteTemplate.Click += BtnDeleteTemplate_Click;

            // 2. SZAKASZ: AKCIÓ GOMBOK (SELECT, ROI, TEST)
            btnSelectTemplate = CreateControlBtn("🎯 SELECT NEW TEMPLATE", 10, 135, 160, 32, Color_Cyan);

            // JAVÍTÁS: Amikor a mérnök megnyomja a gombot, a jobb hasáb utasítja a szülő ImageBoxEx-et az egér-kijelölés indítására!
            btnSelectTemplate.Click += (s, e) => {
                if (_mainEditor != null)
                {
                    // 1. Megkeressük az Editoron belüli ImageBoxEx vezérlőt egy belső segédfüggvénnyel,
                    // vagy ha az imageBoxExMain publikus/internal az Editorban, közvetlenül elérjük:
                    var imageBox = _mainEditor.Controls.Find("imageBoxExMain", true);
                    if (imageBox.Length > 0 && imageBox[0] is ImageBoxEx cyotekBox)
                    {
                        // 2. KÉNYSZERÍTJÜK A CYOTEK MOTOR BELSŐ INTERAKCIÓIT:
                        cyotekBox.SelectionMode =  Cyotek.Windows.Forms.ImageBoxSelectionMode.Rectangle; // Átváltunk négyszögre
                        cyotekBox.AutoPan = false; // Kikapcsoljuk az egérrel való vonszolást (Pan), hogy NE mozogjon a kép rajzolás közben!
                        cyotekBox.Cursor = Cursors.Cross; // Célkereszt kurzor

                        // Aktiváljuk a szülő editor belső flag-jét is
                        _mainEditor.StartTemplateSelection();
                    }
                }
            };

            btnSelectRoi = CreateControlBtn("🗺️ ROI SELECT", 180, 135, 150, 32);
            btnTest = CreateControlBtn("▶ TEST MATCH", 10, 175, 320, 35, Color.LightGreen);
            btnTest.ForeColor = Color.Black;

            // 3. SZAKASZ: MATCH PARAMETERS (Értékek állítása)
            Label lblSection2 = new Label { Text = "MATCH PARAMETERS", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 225), AutoSize = true };

            Label lblScore = new Label { Text = "Min Score:", ForeColor = Color_Text, Location = new Point(10, 250), AutoSize = true };
            numScore = new NumericUpDown { Location = new Point(90, 248), Width = 65, Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05M, Value = (decimal)_module.MatchParams.Score, BackColor = Color_Light, ForeColor = Color_Text };
            numScore.ValueChanged += (s, e) => { _module.MatchParams.Score = (double)numScore.Value; };

            Label lblAngle = new Label { Text = "Tolerance ∠:", ForeColor = Color_Text, Location = new Point(170, 250), AutoSize = true };
            numAngle = new NumericUpDown { Location = new Point(255, 248), Width = 65, Minimum = 0, Maximum = 360, Value = (decimal)_module.MatchParams.ToleranceAngle, BackColor = Color_Light, ForeColor = Color_Text };
            numAngle.ValueChanged += (s, e) => { _module.MatchParams.ToleranceAngle = (double)numAngle.Value; };

            checkPolarity = new CheckBox { Text = "Use Polarity Check", ForeColor = Color_Text, Location = new Point(10, 280), AutoSize = true, Checked = _module.MatchParams.PolarityCheck };
            checkPolarity.CheckedChanged += (s, e) => { _module.MatchParams.PolarityCheck = checkPolarity.Checked; };

            // 4. SZAKASZ: DIAGNOSZTIKA ÉS LED KIJELZŐ
            lblExecutionTime = new Label { Text = "TIME: 0 ms", ForeColor = Color_Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 315), AutoSize = true };

            // LED visszajelző szoftveres szimulációja (zöld/piros színváltós panel)
            panelLedIndicator = new Panel { Location = new Point(250, 312), Size = new Size(70, 24), BackColor = Color.DarkGray, BorderStyle = BorderStyle.FixedSingle };
            Label lblLedText = new Label { Text = "WAIT", ForeColor = Color.Black, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(15, 4), AutoSize = true };
            panelLedIndicator.Controls.Add(lblLedText);

            // 5. SZAKASZ: LISTVIEW AZ EREDMÉNYEKNEK
            Label lblSection3 = new Label { Text = "TARGET MATCH RESULTS", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 350), AutoSize = true };

            listViewResults = new ListView { Location = new Point(10, 370), Size = new Size(320, 160), BackColor = Color_Slate, ForeColor = Color_Text, BorderStyle = BorderStyle.None, View = View.Details, FullRowSelect = true, GridLines = true };
            listViewResults.Columns.Add("Index", 45);
            listViewResults.Columns.Add("Score", 65);
            listViewResults.Columns.Add("X", 55);
            listViewResults.Columns.Add("Y", 55);
            listViewResults.Columns.Add("Angle", 65);

            this.Controls.Add(lblSection1); this.Controls.Add(picTemplatePreview); this.Controls.Add(lblTemplateCounter);
            this.Controls.Add(btnPrev); this.Controls.Add(btnNext); this.Controls.Add(btnDeleteTemplate);
            this.Controls.Add(btnSelectTemplate); this.Controls.Add(btnSelectRoi); this.Controls.Add(btnTest);
            this.Controls.Add(lblSection2); this.Controls.Add(lblScore); this.Controls.Add(numScore);
            this.Controls.Add(lblAngle); this.Controls.Add(numAngle); this.Controls.Add(checkPolarity);
            this.Controls.Add(lblExecutionTime); this.Controls.Add(panelLedIndicator);
            this.Controls.Add(lblSection3); this.Controls.Add(listViewResults);

            // Bekötjük a teszt és kijelölő eseményeket
            btnTest.Click += BtnTest_Click;
        }

        private Button CreateControlBtn(string text, int x, int y, int w, int h, Color? customColor = null)
        {
            Button btn = new Button { Text = text, Location = new Point(x, y), Width = w, Height = h, FlatStyle = FlatStyle.Flat, ForeColor = customColor ?? Color_Text, BackColor = Color_Light, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btn.FlatAppearance.BorderSize = 0;
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
            }
            else
            {
                picTemplatePreview.Image = null;
                lblTemplateCounter.Text = "0 / 0";
                btnDeleteTemplate.Enabled = false;
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