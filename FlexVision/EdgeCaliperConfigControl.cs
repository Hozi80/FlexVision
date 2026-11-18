using Emgu.CV;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FlexVision
{
    public class EdgeCaliperConfigControl : UserControl
    {
        private readonly Color Color_Bg = Color.FromArgb(30, 30, 30);
        private readonly Color Color_Slate = Color.FromArgb(42, 42, 42);
        private readonly Color Color_Light = Color.FromArgb(55, 55, 55);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        private EdgeCaliperModule _module;
        private PipelineEditorControl _mainEditor;
        private Action _onDataChanged;

        private ComboBox comboDirection;
        private ComboBox comboPolarity;
        private ComboBox comboSelection;
        private ComboBox comboChannel;
        private NumericUpDown numContrast;
        private NumericUpDown numFilter;
        private NumericUpDown numExpected;

        private ComboBox comboMode;
        private ComboBox comboPolarity2;
        private NumericUpDown numPairWidth;
        private NumericUpDown numPairTol;
        private CheckBox chkStraddle;
        private Label lblUsedChannel;

        private Label lblExecutionTime;
        private Panel panelLedIndicator;
        private Label lblLedText;
        private ListView listViewResults;


        private Panel panelTeachPair;
        private Label lblMeasuredDist;
        private Button btnApplyPair;
        private double _calculatedPairDist = 0;
        private EdgePolarity _detectedPol1 = EdgePolarity.Any;
        private EdgePolarity _detectedPol2 = EdgePolarity.Any;
        private bool _isUpdatingList = false;


        public EdgeCaliperConfigControl(EdgeCaliperModule module, PipelineEditorControl mainEditor, Action onDataChanged)
        {
            this._module = module;
            this._mainEditor = mainEditor;
            this._onDataChanged = onDataChanged;

            this.BackColor = Color_Bg;
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;

            InitializeComponent();

            // Akkor töltjük be a ComboBox és NumericUpDown értékeket, amikor a panel már valóban megjelent!
            this.Load += (s, e) =>
            {
                LoadValuesFromModule();
                RefreshResultsFromModule();
            };
        }


        private void InitializeComponent()
        {
            // 1. SZAKASZ: CALIPER ZÓNA ÉS TESZT
            Label lblSection1 = new Label { Text = "1D CALIPER MÉRŐVONAL VEZÉRLŐ", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 10), AutoSize = true };

            Button btnSelectRoi = CreateControlBtn("📏 DRAW CALIPER ROI", 10, 32, 320, 32, Color_Cyan);
            btnSelectRoi.Click += (s, e) => { _mainEditor?.StartRoiSelection(); };

            Button btnTest = CreateControlBtn("▶ RUN 1D EDGE DETECT", 10, 70, 320, 35, Color.LightGreen);
            btnTest.ForeColor = Color.Black;
            btnTest.Click += (s, e) => {
                _onDataChanged?.Invoke();
                RefreshResultsFromModule();
            };

            // 2. SZAKASZ: ÉLKERESÉSI PARAMÉTEREK
            Label lblSection2 = new Label { Text = "KERESÉSI IRÁNY ÉS MÓD", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 118), AutoSize = true };

            // Módválasztó (SingleEdge / EdgePair)
            Label lblMode = new Label { Text = "Mode:", ForeColor = Color_Text, Location = new Point(10, 142), Width = 85, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            comboMode = new ComboBox { Location = new Point(100, 139), Width = 230, BackColor = Color_Light, ForeColor = Color_Cyan, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            comboMode.DataSource = Enum.GetValues(typeof(CaliperMeasurementMode));
            comboMode.SelectedItem = _module.MeasurementMode;
            comboMode.SelectedIndexChanged += (s, e) => { _module.MeasurementMode = (CaliperMeasurementMode)comboMode.SelectedItem; _onDataChanged?.Invoke(); };

            // Irány (Direction)
            Label lblDir = new Label { Text = "Direction:", ForeColor = Color_Text, Location = new Point(10, 172), Width = 85, Font = new Font("Segoe UI", 8.5f) };
            comboDirection = new ComboBox { Location = new Point(100, 169), Width = 230, BackColor = Color_Light, ForeColor = Color_Text, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList };
            comboDirection.DataSource = Enum.GetValues(typeof(CaliperDirection));
            comboDirection.SelectedItem = _module.Direction;
            comboDirection.SelectedIndexChanged += (s, e) => { _module.Direction = (CaliperDirection)comboDirection.SelectedItem; _onDataChanged?.Invoke(); };

            // Polaritás 1 és 2 egymás mellett
            Label lblPol = new Label { Text = "Pol 1 / Pol 2:", ForeColor = Color_Text, Location = new Point(10, 202), Width = 85, Font = new Font("Segoe UI", 8.5f) };
            comboPolarity = new ComboBox { Location = new Point(100, 199), Width = 112, BackColor = Color_Light, ForeColor = Color_Text, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList };
            comboPolarity.DataSource = Enum.GetValues(typeof(EdgePolarity));
            comboPolarity.SelectedItem = _module.Polarity;
            comboPolarity.SelectedIndexChanged += (s, e) => { _module.Polarity = (EdgePolarity)comboPolarity.SelectedItem; _onDataChanged?.Invoke(); };

            comboPolarity2 = new ComboBox { Location = new Point(218, 199), Width = 112, BackColor = Color_Light, ForeColor = Color_Text, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList };
            comboPolarity2.DataSource = Enum.GetValues(typeof(EdgePolarity));
            comboPolarity2.SelectedItem = _module.PairSecondPolarity;
            comboPolarity2.SelectedIndexChanged += (s, e) => { _module.PairSecondPolarity = (EdgePolarity)comboPolarity2.SelectedItem; _onDataChanged?.Invoke(); };

            // Kiválasztási mód (First, Last, Strongest, All)
            Label lblSel = new Label { Text = "Selection:", ForeColor = Color_Text, Location = new Point(10, 232), Width = 85, Font = new Font("Segoe UI", 8.5f) };
            comboSelection = new ComboBox { Location = new Point(100, 229), Width = 230, BackColor = Color_Light, ForeColor = Color_Text, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList };
            comboSelection.DataSource = Enum.GetValues(typeof(EdgeSelectionMode));
            comboSelection.SelectedItem = _module.SelectionMode;
            comboSelection.SelectedIndexChanged += (s, e) => { _module.SelectionMode = (EdgeSelectionMode)comboSelection.SelectedItem; _onDataChanged?.Invoke(); };

            // Színcsatorna választó + Mátrix gomb
            Label lblChan = new Label { Text = "Channel:", ForeColor = Color_Text, Location = new Point(10, 262), Width = 85, Font = new Font("Segoe UI", 8.5f) };
            comboChannel = new ComboBox { Location = new Point(100, 259), Width = 175, BackColor = Color_Light, ForeColor = Color_Cyan, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            comboChannel.DataSource = Enum.GetValues(typeof(CaliperColorChannel));
            comboChannel.SelectedItem = _module.ColorChannel;
            comboChannel.SelectedIndexChanged += (s, e) => { _module.ColorChannel = (CaliperColorChannel)comboChannel.SelectedItem; _onDataChanged?.Invoke(); };

            Button btnMatrix = CreateControlBtn("👁️", 280, 259, 50, 24, Color_Cyan);
            btnMatrix.Click += (s, e) =>
            {
                if (_module.LastCleanInput == null || _module.LastCleanInput.Ptr == IntPtr.Zero) return;
                using (Image<Bgr, byte> currentImg = _module.LastCleanInput.Copy())
                {
                    Rectangle box = new Rectangle(0, 0, currentImg.Width, currentImg.Height);
                    if (_module.UseROI && _module.ROI != Rectangle.Empty && _module.ROI.Width > 4 && _module.ROI.Height > 4)
                        box = Rectangle.Intersect(box, _module.ROI);

                    currentImg.ROI = box;
                    using (Image<Bgr, byte> roiCopy = currentImg.Copy())
                    {
                        currentImg.ROI = Rectangle.Empty;
                        using (ColorMatrixSelectorForm matrixForm = new ColorMatrixSelectorForm(roiCopy, _module.ColorChannel))
                        {
                            if (matrixForm.ShowDialog() == DialogResult.OK)
                            {
                                _module.ColorChannel = matrixForm.SelectedChannel;
                                comboChannel.SelectedItem = _module.ColorChannel;
                                _onDataChanged?.Invoke();
                            }
                        }
                    }
                }
            };

            // ÉLPÁR BEÁLLÍTÁSOK (Pair Width, Tolerance, Straddle)
            Label lblPairW = new Label { Text = "Pair Width (px):", ForeColor = Color_Text, Location = new Point(10, 294), Width = 85, Font = new Font("Segoe UI", 8) };
            numPairWidth = new NumericUpDown { Location = new Point(100, 292), Width = 65, Minimum = 1, Maximum = 5000, DecimalPlaces = 1, Value = (decimal)_module.ExpectedPairWidth, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numPairWidth.ValueChanged += (s, e) => { _module.ExpectedPairWidth = (double)numPairWidth.Value; _onDataChanged?.Invoke(); };

            Label lblPairTol = new Label { Text = "Tol (+/- px):", ForeColor = Color_Text, Location = new Point(180, 294), Width = 80, Font = new Font("Segoe UI", 8) };
            numPairTol = new NumericUpDown { Location = new Point(265, 292), Width = 65, Minimum = 0, Maximum = 2000, DecimalPlaces = 1, Value = (decimal)_module.PairWidthTolerance, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numPairTol.ValueChanged += (s, e) => { _module.PairWidthTolerance = (double)numPairTol.Value; _onDataChanged?.Invoke(); };

            chkStraddle = new CheckBox { Text = "Straddle Center (Prefer middle of ROI)", ForeColor = Color_Cyan, Location = new Point(10, 322), AutoSize = true, Checked = _module.PreferStraddleCenter, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            chkStraddle.CheckedChanged += (s, e) => { _module.PreferStraddleCenter = chkStraddle.Checked; _onDataChanged?.Invoke(); };

            // Kontraszt, Simítás és Elvárt darabszám
            Label lblContrast = new Label { Text = "Min Contrast:", ForeColor = Color_Text, Location = new Point(10, 352), Width = 85, Font = new Font("Segoe UI", 8) };
            numContrast = new NumericUpDown { Location = new Point(100, 350), Width = 65, Minimum = 1, Maximum = 255, Value = (decimal)_module.ContrastThreshold, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numContrast.ValueChanged += (s, e) => { _module.ContrastThreshold = (double)numContrast.Value; _onDataChanged?.Invoke(); };

            Label lblFilter = new Label { Text = "Smooth (px):", ForeColor = Color_Text, Location = new Point(180, 352), Width = 80, Font = new Font("Segoe UI", 8) };
            numFilter = new NumericUpDown { Location = new Point(265, 350), Width = 65, Minimum = 0, Maximum = 15, Value = _module.FilterHalfWidth, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numFilter.ValueChanged += (s, e) => { _module.FilterHalfWidth = (int)numFilter.Value; _onDataChanged?.Invoke(); };

            Label lblExpected = new Label { Text = "Expected Count:", ForeColor = Color_Text, Location = new Point(10, 382), Width = 100, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            numExpected = new NumericUpDown { Location = new Point(115, 380), Width = 65, Minimum = 1, Maximum = 100, Value = _module.ExpectedEdgeCount, BackColor = Color_Light, ForeColor = Color_Cyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle };
            numExpected.ValueChanged += (s, e) => { _module.ExpectedEdgeCount = (int)numExpected.Value; _onDataChanged?.Invoke(); };

            // 3. SZAKASZ: DIAGNOSZTIKA ÉS LED
            lblExecutionTime = new Label { Text = "TIME: 0 ms", ForeColor = Color_Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 418), AutoSize = true };
            panelLedIndicator = new Panel { Location = new Point(260, 415), Size = new Size(70, 24), BackColor = Color.DarkGray, BorderStyle = BorderStyle.FixedSingle };
            lblLedText = new Label { Text = "WAIT", ForeColor = Color.Black, Font = new Font("Segoe UI", 8, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            panelLedIndicator.Controls.Add(lblLedText);
            // 4. SZAKASZ: TALÁLATI LISTA
            Label lblSection3 = new Label { Text = "SUB-PIXEL ÉL / ÉLPÁR EREDMÉNYEK", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 452), AutoSize = true };
            listViewResults = new ListView { Location = new Point(10, 472), Size = new Size(320, 190), BackColor = Color_Slate, ForeColor = Color_Text, BorderStyle = BorderStyle.None, View = View.Details, FullRowSelect = true, GridLines = true, Font = new Font("Segoe UI", 8.5f) };
            listViewResults.MultiSelect = true;
            listViewResults.HideSelection = false; // Így akkor is látszik a kijelölés a listában, ha másra kattintasz

            Timer selectionDebounceTimer = new Timer { Interval = 15 };
            selectionDebounceTimer.Tick += (s, e) =>
            {
                selectionDebounceTimer.Stop();
                if (_isUpdatingList) return;

                if (_module.SelectedEdgeIndices == null) _module.SelectedEdgeIndices = new List<int>();
                _module.SelectedEdgeIndices.Clear();

                foreach (int idx in listViewResults.SelectedIndices)
                {
                    _module.SelectedEdgeIndices.Add(idx);
                }

                EvaluatePairTeachOption();
                _onDataChanged?.Invoke();
            };

            listViewResults.SelectedIndexChanged += (s, e) =>
            {
                if (_isUpdatingList) return;
                // Újraindítjuk a 15 ms-os várakozást, így megvárja a kattintás mindkét fázisát
                selectionDebounceTimer.Stop();
                selectionDebounceTimer.Start();
            };




            listViewResults.Columns.Clear();
            listViewResults.Columns.Add("#", 25, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Center X", 58, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Center Y", 58, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Width/Dist", 65, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Contrast", 62, HorizontalAlignment.Center);
            listViewResults.Columns.Add("Pol", 48, HorizontalAlignment.Center);

            // ====================================================================
            // --- INTELLIGENS "TEACH PAIR" SÁV (SINGLE + ALL EDGES MÓDBAN) ---
            // ====================================================================
            panelTeachPair = new Panel
            {
                Location = new Point(10, 668),
                Size = new Size(320, 34),
                BackColor = Color.FromArgb(0, 75, 95),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false // Csak akkor jelenik meg, ha pontosan 2 él van!
            };

            lblMeasuredDist = new Label
            {
                Text = "Dist: 0.0 px",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(6, 7),
                AutoSize = true
            };

            btnApplyPair = new Button
            {
                Text = "📥 USE AS PAIR",
                Location = new Point(195, 3),
                Size = new Size(118, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.LimeGreen,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnApplyPair.FlatAppearance.BorderSize = 0;

            btnApplyPair.Click += (s, e) =>
            {
                if (_calculatedPairDist <= 0) return;

                // 1. Átváltunk EdgePair módra és StrongestEdge kiválasztásra
                _module.MeasurementMode = CaliperMeasurementMode.EdgePair;
                _module.SelectionMode = EdgeSelectionMode.StrongestEdge;

                // 2. Beírjuk a mért távolságot és a 10%-os toleranciát (minimum 2.0 px)
                double tol = Math.Max(2.0, Math.Round(_calculatedPairDist * 0.10, 1));
                _module.ExpectedPairWidth = Math.Round(_calculatedPairDist, 1);
                _module.PairWidthTolerance = tol;

                // 3. Átmentjük a két él automatikusan felismert polaritását is
                _module.Polarity = _detectedPol1;
                _module.PairSecondPolarity = _detectedPol2;

                // 4. Szinkronizáljuk a vezérlőpult elemeit
                comboMode.SelectedItem = _module.MeasurementMode;
                comboSelection.SelectedItem = _module.SelectionMode;
                comboPolarity.SelectedItem = _module.Polarity;
                comboPolarity2.SelectedItem = _module.PairSecondPolarity;
                numPairWidth.Value = (decimal)_module.ExpectedPairWidth;
                numPairTol.Value = (decimal)_module.PairWidthTolerance;

                // 5. Elrejtjük a tanító sávot és azonnal lefuttatjuk az éles élpár-mérést!
                panelTeachPair.Visible = false;
                _onDataChanged?.Invoke();
                RefreshResultsFromModule();
            };

            panelTeachPair.Controls.Add(lblMeasuredDist);
            panelTeachPair.Controls.Add(btnApplyPair);

            // Alsó margó biztosítása görgetéshez
            Label lblBottomSpacer = new Label { Location = new Point(10, 710), Size = new Size(10, 15) };

            lblUsedChannel = new Label
            {
                Text = "Ch: -",
                ForeColor = Color_Cyan,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(125, 420),
                AutoSize = true
            };
            // Ne felejtsd el hozzáadni a Controls.AddRange(...) listához!

            // Ne felejtsd el hozzáadni a panelTeachPair és lblBottomSpacer vezérlőket a Controls.AddRange-hez!


            this.Controls.AddRange(new Control[] {
lblSection1, btnSelectRoi, btnTest,
lblSection2, lblMode, comboMode, lblDir, comboDirection, lblPol, comboPolarity, comboPolarity2,
lblSel, comboSelection, lblChan, comboChannel, btnMatrix,
lblPairW, numPairWidth, lblPairTol, numPairTol, chkStraddle,
lblContrast, numContrast, lblFilter, numFilter, lblExpected, numExpected,
lblExecutionTime, panelLedIndicator, lblSection3, listViewResults,  panelTeachPair , lblBottomSpacer, lblUsedChannel
});
        }




        private bool _isLoadingUI = false;

        public void LoadValuesFromModule()
        {
            if (_module == null) return;
            _isLoadingUI = true;

            try
            {
                // Biztonságos ComboBox kiválasztás index alapján is
                SelectEnumInCombo(comboMode, _module.MeasurementMode);
                SelectEnumInCombo(comboDirection, _module.Direction);
                SelectEnumInCombo(comboPolarity, _module.Polarity);
                SelectEnumInCombo(comboPolarity2, _module.PairSecondPolarity);
                SelectEnumInCombo(comboSelection, _module.SelectionMode);
                SelectEnumInCombo(comboChannel, _module.ColorChannel);

                numPairWidth.Value = ClampDecimal((decimal)_module.ExpectedPairWidth, numPairWidth);
                numPairTol.Value = ClampDecimal((decimal)_module.PairWidthTolerance, numPairTol);
                numContrast.Value = ClampDecimal((decimal)_module.ContrastThreshold, numContrast);
                numFilter.Value = ClampDecimal(_module.FilterHalfWidth, numFilter);
                numExpected.Value = ClampDecimal(_module.ExpectedEdgeCount, numExpected);

                chkStraddle.Checked = _module.PreferStraddleCenter;
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
                if (cb.Items[i].Equals(enumValue))
                {
                    cb.SelectedIndex = i;
                    return;
                }
            }
        }

        private decimal ClampDecimal(decimal val, NumericUpDown num)
        {
            if (val < num.Minimum) return num.Minimum;
            if (val > num.Maximum) return num.Maximum;
            return val;
        }


        private void EvaluatePairTeachOption()
        {
            // Csak SingleEdge módban van értelme tanítani
            if (_module.MeasurementMode != CaliperMeasurementMode.SingleEdge || _module.LastEdges == null)
            {
                panelTeachPair.Visible = false;
                return;
            }

            CaliperEdgeResult e1 = null;
            CaliperEdgeResult e2 = null;

            // A) Ha a felhasználó manuálisan kijelölt pontosan 2 sort a táblázatban:
            if (listViewResults.SelectedIndices.Count == 2)
            {
                e1 = _module.LastEdges[listViewResults.SelectedIndices[0]];
                e2 = _module.LastEdges[listViewResults.SelectedIndices[1]];
            }
            // B) Ha nincs kijelölés, de összesen pontosan 2 élt talált a rendszer:
            else if (_module.LastEdges.Count == 2)
            {
                e1 = _module.LastEdges[0];
                e2 = _module.LastEdges[1];
            }

            if (e1 != null && e2 != null)
            {
                double dx = e2.Position.X - e1.Position.X;
                double dy = e2.Position.Y - e1.Position.Y;
                _calculatedPairDist = Math.Sqrt(dx * dx + dy * dy);

                _detectedPol1 = (e1.Polarity == "Dark -> Light") ? EdgePolarity.DarkToLight : EdgePolarity.LightToDark;
                _detectedPol2 = (e2.Polarity == "Dark -> Light") ? EdgePolarity.DarkToLight : EdgePolarity.LightToDark;

                lblMeasuredDist.Text = $"Dist: {_calculatedPairDist:F1} px (Tol: ±10%)";
                panelTeachPair.Visible = true;
            }
            else
            {
                panelTeachPair.Visible = false;
            }
        }



        public void RefreshResultsFromModule()
        {
            lblExecutionTime.Text = $"TIME: {_module.LastExecutionTime}";
            lblUsedChannel.Text = $"Ch: {_module.LastUsedChannel}";

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

            // 1. Elmentjük a jelenlegi kijelölést, és bekapcsoljuk az esemény-reteszt
            _isUpdatingList = true;

            List<int> savedSelection = _module.SelectedEdgeIndices != null
    ? new List<int>(_module.SelectedEdgeIndices)
    : new List<int>();


            try
            {
                listViewResults.Items.Clear();
                if (_module.LastEdges != null)
                {
                    foreach (var edge in _module.LastEdges)
                    {
                        ListViewItem item = new ListViewItem(edge.Index.ToString());
                        item.SubItems.Add(edge.Position.X.ToString("F1"));
                        item.SubItems.Add(edge.Position.Y.ToString("F1"));
                        item.SubItems.Add(edge.IsPair ? $"{edge.PairDistance:F1} px" : "-");
                        item.SubItems.Add(edge.IsPair ? $"{edge.Edge1Contrast:F0}/{edge.Edge2Contrast:F0}" : $"{edge.Contrast:F1}");
                        item.SubItems.Add(edge.Polarity);
                        listViewResults.Items.Add(item);
                    }
                }

                // 2. Visszaállítjuk a sorok kék kijelölését a táblázatban
                foreach (int selIdx in savedSelection)
                {
                    if (selIdx >= 0 && selIdx < listViewResults.Items.Count)
                    {
                        listViewResults.Items[selIdx].Selected = true;
                    }
                }
            }
            finally
            {
                // Kikapcsoljuk a reteszt
                _isUpdatingList = false;
            }

            EvaluatePairTeachOption();
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