using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.Structure;
using Cyotek.Windows.Forms;
using AOI.Inspections.EdgeBasedTemplateDetection;
using Emgu.CV.CvEnum;

namespace FlexVision
{
    public class TemplateTrainerForm : Form
    {
        private readonly Color Color_Bg = Color.FromArgb(35, 35, 35);
        private readonly Color Color_Input = Color.FromArgb(50, 50, 50);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        private EdgeBasedMatchModule _module;
        private Bitmap _rawTemplateBmp;
        private Bitmap _straightenedTemplateBmp;
        private EdgeBasedTemplate _localEngine;

        // UI Kezelőelemek
        private ImageBox imageBoxTrainer;
        private TrackBar trackBarRotation;
        private TrackBar trackBarLowThresh;
        private TrackBar trackBarHighThresh;

        private Label lblRotationValue;
        private Label lblLowThreshValue;
        private Label lblHighThreshValue;
        private Label lblPointCounter;

        private Button btnSave;
        private Button btnCancel;
        private CheckBox chkEraserMode;

        // --- ÚJ: PAINT SZERKESZTŐ PANEL ELEMEI ---
        private RadioButton rbToolNone;
        private RadioButton rbToolBrush;
        private RadioButton rbToolRect;
        private RadioButton rbColorBlack;
        private RadioButton rbColorWhite;
        private NumericUpDown numBrushSize;

        private int _eraserRadius = 8;
        private bool _isErasing = false;
        private bool _isPainting = false;
        private Point _paintStartPoint;

        private List<int> _erasedIndices = new List<int>();
        private EdgeBasedModelInfo _currentZeroDegreeModel = null;

        // --- MODUL SZINTŰ MEZŐ A KÖZVETLEN ELÉRÉSHEZ ---
        private EdgeBasedTemplate _moduleEngineRef;

        public TemplateTrainerForm(EdgeBasedMatchModule module, Bitmap croppedBmp)
        {
            this._module = module;
            this._rawTemplateBmp = new Bitmap(croppedBmp);
            this._straightenedTemplateBmp = new Bitmap(croppedBmp);

            this.Text = "📐 INTERAKTÍV ÉLALAPÚ SABLON KIEGYENESÍTŐ ÉS CAD/PAINT SZERKESZTŐ";
            this.Size = new Size(820, 680);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color_Bg;
            this.MinimizeBox = false;

            InitializeComponent();

            // ====================================================================
            // --- JAVÍTÁS: ÁTVESSZÜK A MODUL ÉLES MOTORJÁNAK REFERENCIÁJÁT ---
            // ====================================================================
            // Ha a modul motorja még nem létezik, itt inicializáljuk először
            if (module._edgeEngine == null)
            {
                module._edgeEngine = new EdgeBasedTemplate(HardwareAccelerator.OpenClContext);
            }

            // Átvesszük a tartós, éles motor referenciáját az ablak számára
            this._moduleEngineRef = module._edgeEngine;

            RecalculateEdgesFromStraightImage();
        }


        private void InitializeComponent()
        {
            // Oldalsó konfigurációs panel (Kicsit kiszélesítve a Paint vezérlőknek)
            Panel panelSidebar = new Panel { Dock = DockStyle.Right, Width = 240, BackColor = Color.FromArgb(25, 25, 25), BorderStyle = BorderStyle.FixedSingle };
            this.Controls.Add(panelSidebar);

            // Nagy interaktív képnézegető (Cyotek ImageBox)
            imageBoxTrainer = new ImageBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 15), SelectionMode = ImageBoxSelectionMode.None, AutoPan = true };
            imageBoxTrainer.MouseDown += ImageBoxTrainer_MouseDown;
            imageBoxTrainer.MouseMove += ImageBoxTrainer_MouseMove;
            imageBoxTrainer.MouseUp += ImageBoxTrainer_MouseUp;
            imageBoxTrainer.Paint += ImageBoxTrainer_Paint;
            this.Controls.Add(imageBoxTrainer);

            imageBoxTrainer.BringToFront();

            int currentY = 10;

            // 1. SZAKASZ: TERMÉK KIEGYENESÍTÉSE
            Label lblRotTitle = new Label { Text = "1. NULLA POZÍCIÓBA HOZÁSA (∠)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, currentY), AutoSize = true };
            currentY += 20;
            trackBarRotation = new TrackBar { Location = new Point(10, currentY), Width = 215, Minimum = -180, Maximum = 180, Value = 0, TickFrequency = 45, BackColor = Color.FromArgb(25, 25, 25) };
            currentY += 40;
            lblRotationValue = new Label { Text = "Forgatási korrekció: 0°", ForeColor = Color_Text, Location = new Point(15, currentY), Size = new Size(210, 18), Font = new Font("Segoe UI", 8) };
            currentY += 24;

            trackBarRotation.Scroll += (s, e) => {
                lblRotationValue.Text = $"Forgatási korrekció: {trackBarRotation.Value}°";
                _erasedIndices.Clear();
                RecalculateEdgesFromStraightImage();
            };

            // 2. SZAKASZ: THRESHOLD CSÚSZKÁK
            Label lblThreshTitle = new Label { Text = "2. ÉLDETEKTÁLÁSI CSÚSZKÁK", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, currentY), AutoSize = true };
            currentY += 20;

            lblLowThreshValue = new Label { Text = $"Low Thresh (MinContrast): {(int)_module.LowThreshold}", ForeColor = Color_Text, Location = new Point(15, currentY), Size = new Size(210, 16), Font = new Font("Segoe UI", 7.5f) };
            currentY += 16;
            trackBarLowThresh = new TrackBar { Location = new Point(10, currentY), Width = 215, Minimum = 0, Maximum = 255, Value = (int)_module.LowThreshold, TickFrequency = 32, BackColor = Color.FromArgb(25, 25, 25) };
            currentY += 40;

            lblHighThreshValue = new Label { Text = $"High Threshold: {(int)_module.HighThreshold}", ForeColor = Color_Text, Location = new Point(15, currentY), Size = new Size(210, 16), Font = new Font("Segoe UI", 7.5f) };
            currentY += 16;
            trackBarHighThresh = new TrackBar { Location = new Point(10, currentY), Width = 215, Minimum = 0, Maximum = 255, Value = (int)_module.HighThreshold, TickFrequency = 32, BackColor = Color.FromArgb(25, 25, 25) };
            currentY += 42;

            trackBarLowThresh.Scroll += (s, e) => { if (trackBarLowThresh.Value > trackBarHighThresh.Value) trackBarHighThresh.Value = trackBarLowThresh.Value; lblLowThreshValue.Text = $"Low Thresh (MinContrast): {trackBarLowThresh.Value}"; lblHighThreshValue.Text = $"High Threshold: {trackBarHighThresh.Value}"; _erasedIndices.Clear(); RecalculateEdgesFromStraightImage(); };
            trackBarHighThresh.Scroll += (s, e) => { if (trackBarHighThresh.Value < trackBarLowThresh.Value) trackBarLowThresh.Value = trackBarHighThresh.Value; lblLowThreshValue.Text = $"Low Thresh (MinContrast): {trackBarLowThresh.Value}"; lblHighThreshValue.Text = $"High Threshold: {trackBarHighThresh.Value}"; _erasedIndices.Clear(); RecalculateEdgesFromStraightImage(); };

            // 3. SZAKASZ: BINÁRIS MORFOLÓGIA
            Label lblMorphTitle = new Label { Text = "3. AUTOMATA ZAJ TISZTÍTÁS", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, currentY), AutoSize = true };
            currentY += 20;
            Button btnOpening = CreateBtn("🧹 PORSZEMEK AUTOMATA TÖRLÉSE", 15, currentY, 210, 24, Color_Text); currentY += 28;
            Button btnClosing = CreateBtn("🔗 SZAGGATOTT ÉLEK ÖSSZEZÁRÁSA", 15, currentY, 210, 24, Color_Text); currentY += 32;
            btnOpening.Click += (s, e) => ApplyMorphologyFilter(MorphOp.Open);
            btnClosing.Click += (s, e) => ApplyMorphologyFilter(MorphOp.Close);

            // 4. SZAKASZ: VÉGLEGES ÉL-RADÍR
            Label lblTools = new Label { Text = "4. VÉGLEGES ÉL-REDUKÁLÓ (RADÍR)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, currentY), AutoSize = true };
            currentY += 20;
            chkEraserMode = new CheckBox { Text = "🧹 ÉL-RADÍR ÜZEMMÓD", Location = new Point(15, currentY), Size = new Size(210, 24), ForeColor = Color_Text, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter };
            currentY += 32;
            chkEraserMode.CheckedChanged += (s, e) => {
                if (chkEraserMode.Checked) { rbToolNone.Checked = true; imageBoxTrainer.SelectionMode = ImageBoxSelectionMode.None; }
                imageBoxTrainer.AutoPan = !chkEraserMode.Checked;
                imageBoxTrainer.Cursor = chkEraserMode.Checked ? Cursors.Cross : Cursors.Default;
            };

            // ====================================================================
            // 5. SZAKASZ: INTERAKTÍV PAINT PIXEL SZERKESZTŐ (JAVÍTOTT CSOPORTOSÍTÁS)
            // ====================================================================
            Label lblPaintTitle = new Label { Text = "5. GRAFIKUS PIXEL SZERKESZTŐ (PAINT)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, currentY), AutoSize = true };
            currentY += 20;

            // --- A) ESZKÖZÖK CSOPORT (LÁTHATATLAN PANEL A SEPARÁCIÓHOZ) ---
            Panel panelToolGroup = new Panel { Location = new Point(5, currentY), Size = new Size(220, 45), BackColor = Color.Transparent };
            panelSidebar.Controls.Add(panelToolGroup);

            rbToolNone = new RadioButton { Text = "✋ Mozgatás / Pan", Location = new Point(10, 0), Size = new Size(110, 20), ForeColor = Color_Text, Font = new Font("Segoe UI", 7.5f), Checked = true };
            rbToolBrush = new RadioButton { Text = "🖌️ Ecset / Brush", Location = new Point(120, 0), Size = new Size(100, 20), ForeColor = Color_Text, Font = new Font("Segoe UI", 7.5f) };
            rbToolRect = new RadioButton { Text = "⬛ Négyzet / Rect", Location = new Point(10, 22), Size = new Size(110, 20), ForeColor = Color_Text, Font = new Font("Segoe UI", 7.5f) };

            Label lblBrushSize = new Label { Text = "Méret:", ForeColor = Color_Text, Location = new Point(120, 24), AutoSize = true, Font = new Font("Segoe UI", 7) };
            numBrushSize = new NumericUpDown { Location = new Point(160, 22), Width = 55, Minimum = 2, Maximum = 100, Value = 12, BackColor = Color_Input, ForeColor = Color_Text, Font = new Font("Segoe UI", 7.5f) };

            panelToolGroup.Controls.AddRange(new Control[] { rbToolNone, rbToolBrush, rbToolRect, lblBrushSize, numBrushSize });
            currentY += 50;

            // --- B) SZÍNEK CSOPORT (FÜGGETLEN LÁTHATATLAN PANEL) ---
            Panel panelColorGroup = new Panel { Location = new Point(5, currentY), Size = new Size(220, 25), BackColor = Color.Transparent };
            panelSidebar.Controls.Add(panelColorGroup);

            rbColorBlack = new RadioButton { Text = "⬛ FEKETE (Zaj)", Location = new Point(10, 0), Size = new Size(110, 20), ForeColor = Color.Tomato, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Checked = true };
            rbColorWhite = new RadioButton { Text = "⬜ FEHÉR (Pótlás)", Location = new Point(120, 0), Size = new Size(100, 20), ForeColor = Color.LightGreen, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold) };

            panelColorGroup.Controls.AddRange(new Control[] { rbColorBlack, rbColorWhite });
            currentY += 30;

            // Paint állapotgép eseménykezelők (Változatlanul hagyva!)
            EventHandler toolChanged = (s, e) => {
                if (rbToolBrush.Checked || rbToolRect.Checked)
                {
                    chkEraserMode.Checked = false;
                    imageBoxTrainer.AutoPan = false;
                    imageBoxTrainer.Cursor = Cursors.Default;
                    if (rbToolRect.Checked) imageBoxTrainer.SelectionMode = ImageBoxSelectionMode.Rectangle;
                }
                else
                {
                    imageBoxTrainer.AutoPan = true;
                    imageBoxTrainer.SelectionMode = ImageBoxSelectionMode.None;
                }
            };
            rbToolNone.CheckedChanged += toolChanged;
            rbToolBrush.CheckedChanged += toolChanged;
            rbToolRect.CheckedChanged += toolChanged;

            // Élő pontszámláló diagnosztika
            lblPointCounter = new Label { Text = "Élpontok száma: 0", ForeColor = Color_Text, Location = new Point(15, currentY), Size = new Size(210, 20), Font = new Font("Segoe UI", 9f, FontStyle.Italic | FontStyle.Bold) };

            // Gombok áthelyezése dinamikus Y követésre, hogy ne csússzanak össze
            btnSave = CreateBtn("💾 TRAIN & SAVE TO RECIPE", 15, currentY + 25, 210, 35, Color.LightGreen);
            btnSave.Click += BtnSave_Click;
            btnCancel = CreateBtn("❌ CANCEL", 15, currentY + 65, 210, 32, Color_Text);
            btnCancel.DialogResult = DialogResult.Cancel;

            // Figyelem: A panelToolGroup és panelColorGroup már fentebb hozzá lett adva a panelSidebar-hoz!
            panelSidebar.Controls.AddRange(new Control[] {
        lblRotTitle, trackBarRotation, lblRotationValue,
        lblThreshTitle, lblLowThreshValue, trackBarLowThresh, lblHighThreshValue, trackBarHighThresh,
        lblMorphTitle, btnOpening, btnClosing,
        lblTools, chkEraserMode,
        lblPaintTitle, // <--- A panelcsoportokat kiszedtük innen, mert külön adtuk hozzá őket!
        lblPointCounter, btnSave, btnCancel
    });
        }

        // ====================================================================
        // --- INTERAKTÍV MOUSE ÉS DRAW MŰVELETEK (SZABADKÉZ ÉS TÉGLALAP) ---
        // ====================================================================
        private void ImageBoxTrainer_MouseDown(object sender, MouseEventArgs e)
        {
            Point imgPt = imageBoxTrainer.PointToImage(e.Location);
            // A) HA ÉL-RADÍR VAN BEKAPCSOLVA
            if (chkEraserMode.Checked && e.Button == MouseButtons.Left)
            {
                _isErasing = true; ErasePointsAt(imgPt);
            }
            // B) HA SZABADKÉZI PIXEL ECSET VAN BEKAPCSOLVA
            else if (rbToolBrush.Checked && e.Button == MouseButtons.Left)
            {
                _isPainting = true;
                DrawPixelOnTemplate(imgPt, rbToolBrush.Text);
            }
            // C) HA FIX TÉGLALAP KITÖLTŐ VAN BEKAPCSOLVA
            else if (rbToolRect.Checked && e.Button == MouseButtons.Left)
            {
                _isPainting = true;
                _paintStartPoint = imgPt; // Megjegyezzük a négyszög kezdő sarki pixelét
            }
        }
        private void ImageBoxTrainer_MouseMove(object sender, MouseEventArgs e)
        {
            Point imgPt = imageBoxTrainer.PointToImage(e.Location);
            if (_isErasing)
            {
                ErasePointsAt(imgPt);
            }
            else if (_isPainting && rbToolBrush.Checked)
            {
                DrawPixelOnTemplate(imgPt, rbToolBrush.Text);
            }
        }
        private void ImageBoxTrainer_MouseUp(object sender, MouseEventArgs e)
        {
            _isErasing = false;
            if (_isPainting && rbToolRect.Checked)
            {
                Point imgPt = imageBoxTrainer.PointToImage(e.Location);
                DrawPixelOnTemplate(imgPt, rbToolRect.Text); // Az egér felengedésekor rajzoljuk meg a teli téglalapot
            }
            _isPainting = false;
        }
        // ====================================================================
        // --- GRAFIKUS PIXEL-ÁTFESTŐ MAG (A PAINT MINDSZENTJE) ---
        // ====================================================================
        private void DrawPixelOnTemplate(Point targetPt, string toolType)
        {
            if (_straightenedTemplateBmp == null) return;
            // Kiválasztjuk a mérnök által kért Paint színt (Tiszta Fekete vagy Tiszta Fehér)
            Color drawColor = rbColorBlack.Checked ? Color.Black : Color.White;
            int size = (int)numBrushSize.Value;
            using (Graphics g = Graphics.FromImage(_straightenedTemplateBmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None; // Bitre pontos rajzolás élsimítás nélkül!
                if (toolType.Contains("Brush"))
                {
                    // Szabadkézi ecset: Egy teli kört vagy négyzetet rajzolunk az egér alá
                    using (SolidBrush brush = new SolidBrush(drawColor))
                    {
                        g.FillEllipse(brush, targetPt.X - (size / 2), targetPt.Y - (size / 2), size, size);
                    }
                }
                else if (toolType.Contains("Rect"))
                {
                    // Kitöltött téglalap: Kiszámoljuk a húzás pontos geometriai határait
                    int x = Math.Min(_paintStartPoint.X, targetPt.X);
                    int y = Math.Min(_paintStartPoint.Y, targetPt.Y);
                    int w = Math.Abs(_paintStartPoint.X - targetPt.X);
                    int h = Math.Abs(_paintStartPoint.Y - targetPt.Y);
                    if (w > 1 && h > 1)
                    {
                        using (SolidBrush brush = new SolidBrush(drawColor))
                        {
                            g.FillRectangle(brush, x, y, w, h);
                        }
                    }
                }
            }
            // Az átfestés után kényszerítjük az OpenCL motort az élek ÚJRASZÁMOLÁSÁRA a módosított pixelekből!
            _erasedIndices.Clear(); // A pixel-átfestés felülírja a korábbi pontstruktúrát
            _localEngine = new EdgeBasedTemplate(HardwareAccelerator.OpenClContext);
            _localEngine.lowThreshold = trackBarLowThresh.Value;
            _localEngine.highThreshold = trackBarHighThresh.Value;
            using (Image<Gray, float> straightGray = new Image<Gray, float>(_straightenedTemplateBmp))
            {
                _currentZeroDegreeModel = _localEngine.CreateModel(straightGray, 1f, 0);
            }
            RefreshDisplay();
            imageBoxTrainer.Invalidate();
        }
        // ====================================================================
        // --- ADAPTÍV KIEGYENESÍTŐ ÉS ÉLÚJRASZÁMOLÓ MAG ---
        // ====================================================================
        private void RecalculateEdgesFromStraightImage()
        {
            try
            {
                double currentCorrectionAngle = trackBarRotation.Value;
                using (Image<Gray, float> rawGray = new Image<Gray, float>(_rawTemplateBmp))
                {
                    if (currentCorrectionAngle == 0)
                    {
                        _straightenedTemplateBmp = new Bitmap(_rawTemplateBmp);
                    }
                    else
                    {
                        using (Image<Gray, float> turned = rawGray.Rotate(currentCorrectionAngle, new Gray(0), false))
                        {
                            _straightenedTemplateBmp = turned.ToBitmap();
                        }
                    }
                }
                _localEngine = new EdgeBasedTemplate(HardwareAccelerator.OpenClContext);
                _localEngine.lowThreshold = (currentCorrectionAngle == 0) ? trackBarLowThresh.Value : (float)(trackBarLowThresh.Value * 0.85f);
                _localEngine.highThreshold = trackBarHighThresh.Value;
                using (Image<Gray, float> straightGray = new Image<Gray, float>(_straightenedTemplateBmp))
                {
                    _currentZeroDegreeModel = _localEngine.CreateModel(straightGray, 1f, 0);
                }
                RefreshDisplay();
            }
            catch (Exception ex) { Console.WriteLine("Hiba az élszámítás közben: " + ex.Message); }
        }
        private void RefreshDisplay()
        {
            if (_currentZeroDegreeModel == null) return;
            int activePointsCount = _currentZeroDegreeModel.noOfCordinates - _erasedIndices.Count;
            lblPointCounter.Text = $"Élpontok száma: {activePointsCount}";
            double currentCorrectionAngle = trackBarRotation.Value;
            using (Image<Bgr, byte> baseColorConverter = new Image<Bgr, byte>(_straightenedTemplateBmp))
            {
                using (Image<Bgr, byte> overlayImg = baseColorConverter.Copy())
                {
                    List<int> erasedList = _erasedIndices;
                    for (int a = 0; a < _currentZeroDegreeModel.noOfCordinates; a++)
                    {
                        if (erasedList.Contains(a)) continue;
                        // Tűpontos eltolás-korrekció a te saját súlypontos logikád szerint (Katonás Piros vonalak)
                        overlayImg.Draw(new LineSegment2D(
                        new Point(_currentZeroDegreeModel.cordinatesarrayY[a] + _currentZeroDegreeModel.centerOfGravity.Y,
                        _currentZeroDegreeModel.cordinatesarrayX[a] + _currentZeroDegreeModel.centerOfGravity.X),
                        new Point(_currentZeroDegreeModel.cordinatesarrayY[a] + _currentZeroDegreeModel.centerOfGravity.Y,
                        _currentZeroDegreeModel.cordinatesarrayX[a] + _currentZeroDegreeModel.centerOfGravity.X)),
                        new Bgr(240, 10, 10), 1);
                    }
                    Bitmap oldBmp = imageBoxTrainer.Image as Bitmap;
                    imageBoxTrainer.Image = overlayImg.ToBitmap();
                    oldBmp?.Dispose();
                }
            }
        }
        private void ApplyMorphologyFilter(MorphOp operation)
        {
            try
            {
                using (Image<Gray, byte> maskImage = new Image<Gray, byte>(_straightenedTemplateBmp))
                {
                    using (Mat kernel = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(3, 3), new Point(-1, -1)))
                    {
                        CvInvoke.MorphologyEx(maskImage, maskImage, operation, kernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar(0));
                    }
                    _straightenedTemplateBmp?.Dispose();
                    _straightenedTemplateBmp = maskImage.ToBitmap();
                }
                _erasedIndices.Clear();
                _localEngine = new EdgeBasedTemplate(HardwareAccelerator.OpenClContext);
                _localEngine.lowThreshold = trackBarLowThresh.Value;
                _localEngine.highThreshold = trackBarHighThresh.Value;
                using (Image<Gray, float> straightGray = new Image<Gray, float>(_straightenedTemplateBmp))
                {
                    _currentZeroDegreeModel = _localEngine.CreateModel(straightGray, 1f, 0);
                }
                RefreshDisplay();
            }
            catch (Exception ex) { MessageBox.Show($"Hiba a morfológia közben: {ex.Message}"); }
        }
        private void ErasePointsAt(Point imgPt)
        {
            if (_currentZeroDegreeModel == null) return;
            int offsetX = _straightenedTemplateBmp.Width / 2;
            int offsetY = _straightenedTemplateBmp.Height / 2;
            List<int> alreadyErased = _erasedIndices;
            for (int i = 0; i < _currentZeroDegreeModel.noOfCordinates; i++)
            {
                if (alreadyErased.Contains(i)) continue;
                Point pt = new Point(_currentZeroDegreeModel.cordinatesarrayY[i] + _currentZeroDegreeModel.centerOfGravity.Y, _currentZeroDegreeModel.cordinatesarrayX[i] + _currentZeroDegreeModel.centerOfGravity.X);
                double distance = Math.Sqrt(Math.Pow(pt.X - imgPt.X, 2) + Math.Pow(pt.Y - imgPt.Y, 2));
                if (distance <= _eraserRadius)
                {
                    _erasedIndices.Add(i);
                }
            }
            RefreshDisplay();
            imageBoxTrainer.Invalidate();
        }
        private void ImageBoxTrainer_Paint(object sender, PaintEventArgs e)
        {
            if (chkEraserMode.Checked)
            {
                Point mousePos = imageBoxTrainer.PointToClient(Cursor.Position);
                using (Pen p = new Pen(Color.Tomato, 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                {
                    e.Graphics.DrawEllipse(p, mousePos.X - _eraserRadius, mousePos.Y - _eraserRadius, _eraserRadius * 2, _eraserRadius * 2);
                }
            }
            else if (rbToolBrush.Checked)
            {
                // Kirajzoljuk az ecset méretét körvonallal az egér körül a precíz célzáshoz
                Point mousePos = imageBoxTrainer.PointToClient(Cursor.Position);
                int size = (int)numBrushSize.Value;
                using (Pen p = new Pen(rbColorBlack.Checked ? Color.Orange : Color.LimeGreen, 1.2f))
                {
                    e.Graphics.DrawEllipse(p, mousePos.X - (size / 2), mousePos.Y - (size / 2), size, size);
                }
            }
        }
        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (_currentZeroDegreeModel == null || _currentZeroDegreeModel.noOfCordinates < 5)
            {
                MessageBox.Show("Nincs elegendő érvényes élpont a sablon rögzítéséhez!", "Mérnöki korlát", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Console.WriteLine($"[FlexVision] {_module.Name} -> Gyári TrainTemplate élesítése közvetlenül a modul motorján...");

                _module.LowThreshold = trackBarLowThresh.Value;
                _module.HighThreshold = trackBarHighThresh.Value;

                // ====================================================================
                // --- JAVÍTÁS: KÖZVETLENÜL A MODUL ÉLES MOTORJÁT TANÍTJUK BE ---
                // ====================================================================
                _moduleEngineRef.lowThreshold = _module.LowThreshold;
                _moduleEngineRef.highThreshold = _module.HighThreshold;
                _moduleEngineRef.minScore = _module.MinScore;
                _moduleEngineRef.minEdgepoints = _module.MinEdgePoints;
                _moduleEngineRef.AngleDetectionRangeSimmetric = _module.AngleDetectionRangeSimmetric;
                _moduleEngineRef.Accuracy = _module.AccuracyLevel;

                using (Image<Gray, float> finalGrayImage = new Image<Gray, float>(_straightenedTemplateBmp))
                {
                    finalGrayImage.Save(@"C:\Test\template.png");
                    // A betanítás közvetlenül a modul motorjában történik, így a Models[] tömb NEM fog megsemmisülni!
                    _moduleEngineRef.TrainTemplate(finalGrayImage);
                }

                // Átmásoljuk az 1D tömböket a modulba, hogy a főablak SAVE gombja ki tudja menteni a .vrec-be
                _module.Scaled = _moduleEngineRef.Scaled;
                _module.Full = _moduleEngineRef.Full;

                _module.Templates.Clear();
                _module.Templates.Add(new Bitmap(_straightenedTemplateBmp));
                _module.ActiveTemplateIndex = 0;

                MessageBox.Show("A sablon sikeresen rögzítve a modul éles motorjában!", "Sikeres Teach", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Modell élesítési hiba:\n{ex.Message}", "Hiba", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private EdgeBasedModelInfo360 Create1DarrayForTrainer(EdgeBasedModelInfo[] infos)
        {
            EdgeBasedModelInfo360 res = new EdgeBasedModelInfo360();
            int count = 0;
            for (int i = 0; i < 360; i++) count += infos[i].noOfCordinates;
            res.cordinatesarrayX360 = new int[count]; res.cordinatesarrayY360 = new int[count];
            res.edgeMagnitude360 = new float[count]; res.edgeDerivativeX360 = new float[count]; res.edgeDerivativeY360 = new float[count];
            res.step360 = new int[360];
            res.positions = new int[360];
            res.noOfCordinates360 = count;
            res.normMinScore360 = new float[360];
            res.normGreediness360 = new float[360];
            int sum = 0;
            for (int i = 0; i < 360; i++)
            {
                res.normMinScore360[i] = _localEngine.minScore;
                res.normGreediness360[i] = (float)((1 - _localEngine.greediness * _localEngine.minScore) / (1 - _localEngine.greediness));
                res.positions[i] = sum;
                for (int j = 0; j < infos[i].noOfCordinates; j++)
                {
                    res.cordinatesarrayX360[sum + j] = infos[i].cordinatesarrayX[j];
                    res.cordinatesarrayY360[sum + j] = infos[i].cordinatesarrayY[j];
                    res.edgeMagnitude360[sum + j] = infos[i].edgeMagnitude[j];
                    res.edgeDerivativeX360[sum + j] = infos[i].edgeDerivativeX[j];
                    res.edgeDerivativeY360[sum + j] = infos[i].edgeDerivativeY[j];
                }
                res.step360[i] = infos[i].noOfCordinates;
                sum += infos[i].noOfCordinates;
            }
            return res;
        }
        private Button CreateBtn(string text, int x, int y, int w, int h, Color color)
        {
            Button btn = new Button { Text = text, Location = new Point(x, y), Width = w, Height = h, FlatStyle = FlatStyle.Flat, BackColor = Color_Input, ForeColor = color, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }
    }
}