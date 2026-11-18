using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FlexVision
{
    public class GeometryMeasureConfigControl : UserControl, IInteractiveGeometryTool
    {
        private readonly Color Color_Bg = Color.FromArgb(30, 30, 30);
        private readonly Color Color_Slate = Color.FromArgb(42, 42, 42);
        private readonly Color Color_Light = Color.FromArgb(55, 55, 55);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        private GeometryMeasureModule _module;
        private Control _imageBoxRef;
        private Action _onDataChanged;

        private CheckedListBox chkListFeatures;
        private ListView listViewMeasures;
        private CheckBox chkUseMm;
        private CheckBox chkExpandSub;
        private NumericUpDown numNominal;
        private NumericUpDown numTol;
        private CheckBox chkEnableTol;
        private bool _isLoading = false;
        private GeometryEvaluatedResult _draggedLabelResult = null;
        private Point _dragStartImgPt = Point.Empty;
        private PointF _dragInitialOffset = PointF.Empty;



        public GeometryMeasureConfigControl(GeometryMeasureModule module, Control imageBoxRef, Action onDataChanged)
        {
            this._module = module;
            this._imageBoxRef = imageBoxRef;
            this._onDataChanged = onDataChanged;

            this.BackColor = Color_Bg;
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;

            InitializeComponent();
            this.Load += (s, e) => RefreshAllLists();
        }

        private void InitializeComponent()
        {
            Label lbl1 = new Label { Text = "1. MEGJELENÍTENDŐ ELEMEK (PIPA = LÁTHATÓ)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 10), AutoSize = true };

            chkListFeatures = new CheckedListBox
            {
                Location = new Point(10, 30),
                Size = new Size(320, 140),
                BackColor = Color_Slate,
                ForeColor = Color_Text,
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true,
                Font = new Font("Segoe UI", 8.5f)
            };
            chkListFeatures.ItemCheck += (s, e) =>
            {
                if (_isLoading) return;
                string key = chkListFeatures.Items[e.Index].ToString();
                if (e.NewValue == CheckState.Checked && !_module.VisibleFeatureKeys.Contains(key))
                    _module.VisibleFeatureKeys.Add(key);
                else if (e.NewValue == CheckState.Unchecked)
                    _module.VisibleFeatureKeys.Remove(key);

                this.BeginInvoke(new Action(() => _onDataChanged?.Invoke()));
            };

            // Két kapcsoló egymás mellett: Milliméter és Sub-loop kiterjesztés
            chkUseMm = new CheckBox { Text = "Use mm", ForeColor = Color_Cyan, Location = new Point(10, 178), AutoSize = true, Checked = _module.UseMillimeters, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            chkUseMm.CheckedChanged += (s, e) => { _module.UseMillimeters = chkUseMm.Checked; _onDataChanged?.Invoke(); RefreshMeasurementsList(); };

            chkExpandSub = new CheckBox { Text = "Expand Sub-Loops (Sub1..N)", ForeColor = Color_Text, Location = new Point(105, 178), AutoSize = true, Checked = _module.ExpandSubLoops, Font = new Font("Segoe UI", 8.5f) };
            chkExpandSub.CheckedChanged += (s, e) =>
            {
                _module.ExpandSubLoops = chkExpandSub.Checked;
                _onDataChanged?.Invoke();
                RefreshAllLists();
            };

            Label lblHint = new Label { Text = "💡 Tipp: Vidd az egeret egy látható élre/pontra,\n    majd kattints kettőre (csoportos módban mindegyikre alkalmazza)!", ForeColor = Color.LightGray, Font = new Font("Segoe UI", 7.8f, FontStyle.Italic), Location = new Point(10, 205), AutoSize = true };

            Label lbl2 = new Label { Text = "2. KIÉRTÉKELT GEOMETRIAI MÉRÉSEK", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 245), AutoSize = true };

            listViewMeasures = new ListView
            {
                Location = new Point(10, 265),
                Size = new Size(320, 165),
                BackColor = Color_Slate,
                ForeColor = Color_Text,
                BorderStyle = BorderStyle.None,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 8.5f)
            };
            listViewMeasures.Columns.Add("Measure", 95);
            listViewMeasures.Columns.Add("Operation", 105);
            listViewMeasures.Columns.Add("Value", 65, HorizontalAlignment.Right);
            listViewMeasures.Columns.Add("Status", 50, HorizontalAlignment.Center);
            listViewMeasures.SelectedIndexChanged += (s, e) => LoadSelectedMeasureTolerance();

            // Tolerancia beállító sáv
            chkEnableTol = new CheckBox { Text = "Tol Check", ForeColor = Color_Text, Location = new Point(10, 440), AutoSize = true, Font = new Font("Segoe UI", 8) };
            chkEnableTol.CheckedChanged += (s, e) => SaveSelectedMeasureTolerance();

            Label lblNom = new Label { Text = "Nom:", ForeColor = Color_Text, Location = new Point(95, 442), AutoSize = true, Font = new Font("Segoe UI", 8) };
            numNominal = new NumericUpDown { Location = new Point(130, 439), Width = 65, Minimum = -10000, Maximum = 10000, DecimalPlaces = 2, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numNominal.ValueChanged += (s, e) => SaveSelectedMeasureTolerance();

            Label lblTol = new Label { Text = "+/-:", ForeColor = Color_Text, Location = new Point(202, 442), AutoSize = true, Font = new Font("Segoe UI", 8) };
            numTol = new NumericUpDown { Location = new Point(230, 439), Width = 55, Minimum = 0, Maximum = 5000, DecimalPlaces = 2, Value = 10, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };
            numTol.ValueChanged += (s, e) => SaveSelectedMeasureTolerance();

            Button btnDelMeasure = new Button { Text = "✖", Location = new Point(292, 438), Size = new Size(38, 24), FlatStyle = FlatStyle.Flat, BackColor = Color_Light, ForeColor = Color.Tomato, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            btnDelMeasure.Click += (s, e) =>
            {
                var rule = GetSelectedRule();
                if (rule != null)
                {
                    _module.Measurements.Remove(rule);
                    _onDataChanged?.Invoke();
                    RefreshMeasurementsList();
                }
            };

            this.Controls.AddRange(new Control[] { lbl1, chkListFeatures, chkUseMm, chkExpandSub, lblHint, lbl2, listViewMeasures, chkEnableTol, lblNom, numNominal, lblTol, numTol, btnDelMeasure });
        }

        private GeometryMeasurementItem GetSelectedRule()
        {
            foreach (ListViewItem item in listViewMeasures.SelectedItems)
            {
                if (item.Index >= 0 && item.Index < _module.EvaluatedResults.Count)
                {
                    return _module.EvaluatedResults[item.Index].SourceRule;
                }
            }
            return null;
        }


        public void HandleMouseDown(Point imagePt)
        {
            // Megnézzük, hogy valamelyik felirat dobozára kattintott-e a felhasználó
            foreach (var eval in _module.EvaluatedResults)
            {
                if (eval.LabelBounds != Rectangle.Empty && eval.LabelBounds.Contains(imagePt))
                {
                    _draggedLabelResult = eval;
                    _dragStartImgPt = imagePt;
                    _dragInitialOffset = eval.SourceRule.LabelOffset;

                    // Ha eddig automatikus pozíción volt (0,0), kiszámoljuk az aktuális eltolását kiindulópontnak
                    if (Math.Abs(_dragInitialOffset.X) < 0.5f && Math.Abs(_dragInitialOffset.Y) < 0.5f)
                    {
                        float curCenterX = eval.LabelBounds.X + eval.LabelBounds.Width / 2f;
                        float curCenterY = eval.LabelBounds.Y + eval.LabelBounds.Height / 2f;
                        _dragInitialOffset = new PointF(curCenterX - eval.CalculatedPoint.X, curCenterY - eval.CalculatedPoint.Y);
                    }
                    return;
                }
            }
        }

        public void HandleMouseUp(Point imagePt)
        {
            if (_draggedLabelResult != null)
            {
                _draggedLabelResult = null;
                _onDataChanged?.Invoke();
            }
        }

        public void RefreshAllLists()
        {
            _isLoading = true;
            chkExpandSub.Checked = _module.ExpandSubLoops;
            chkUseMm.Checked = _module.UseMillimeters;

            chkListFeatures.Items.Clear();
            foreach (var key in _module.GroupedFeatures.Keys)
            {
                chkListFeatures.Items.Add(key, _module.VisibleFeatureKeys.Contains(key));
            }
            _isLoading = false;
            RefreshMeasurementsList();
        }

        public void RefreshMeasurementsList()
        {
            listViewMeasures.Items.Clear();
            string unit = _module.UseMillimeters ? "mm" : "px";
            foreach (var eval in _module.EvaluatedResults)
            {
                ListViewItem item = new ListViewItem(eval.DisplayName);
                item.SubItems.Add(eval.Operation.ToString());
                item.SubItems.Add(eval.Operation == GeometryOperationType.IntersectionLineToLine ? $"({eval.CalculatedPoint.X:F0},{eval.CalculatedPoint.Y:F0})" : $"{eval.MeasuredValue:F2} {unit}");
                item.SubItems.Add(eval.IsPass ? "PASS" : "FAIL");
                listViewMeasures.Items.Add(item);
            }
        }

        private void LoadSelectedMeasureTolerance()
        {
            var rule = GetSelectedRule();
            if (rule == null) return;
            _isLoading = true;
            chkEnableTol.Checked = rule.CheckTolerance;
            numNominal.Value = (decimal)Math.Max((double)numNominal.Minimum, Math.Min((double)numNominal.Maximum, rule.NominalValue));
            numTol.Value = (decimal)Math.Max((double)numTol.Minimum, Math.Min((double)numTol.Maximum, rule.TolerancePlusMinus));
            _isLoading = false;
        }

        private void SaveSelectedMeasureTolerance()
        {
            if (_isLoading) return;
            var rule = GetSelectedRule();
            if (rule == null) return;

            rule.CheckTolerance = chkEnableTol.Checked;
            rule.NominalValue = (double)numNominal.Value;
            rule.TolerancePlusMinus = (double)numTol.Value;
            _onDataChanged?.Invoke();
            RefreshMeasurementsList();
        }

        // ====================================================================
        // --- INTERAKTÍV EGÉR KEZELÉS (CSOPORTOSÍTOTT ÉS KITERJESZTETT MÓDBAN IS) ---
        // ====================================================================
        public void HandleMouseMove(Point imagePt)
        {
            // 1. Ha éppen egy feliratot húzunk az egérrel:
            if (_draggedLabelResult != null)
            {
                float dx = imagePt.X - _dragStartImgPt.X;
                float dy = imagePt.Y - _dragStartImgPt.Y;
                _draggedLabelResult.SourceRule.LabelOffset = new PointF(_dragInitialOffset.X + dx, _dragInitialOffset.Y + dy);
                _onDataChanged?.Invoke();
                return;
            }

            // 2. Egyébként normál Hover élkiemelés:
            string hitKey = FindNearestFeature(imagePt, 12.0);
            if (hitKey != _module.HoveredFeatureKey)
            {
                _module.HoveredFeatureKey = hitKey;
                _onDataChanged?.Invoke();
            }
        }

        public void HandleMouseClick(Point imagePt, Point screenPt)
        {
            string hitKey = FindNearestFeature(imagePt, 12.0);
            if (string.IsNullOrEmpty(hitKey))
            {
                _module.SelectedFeature1Key = null;
                _onDataChanged?.Invoke();
                return;
            }
            if (string.IsNullOrEmpty(_module.SelectedFeature1Key) || _module.SelectedFeature1Key == hitKey)
            {
                _module.SelectedFeature1Key = hitKey;
                _onDataChanged?.Invoke();
            }
            else
            {
                ShowOperationContextMenu(_module.SelectedFeature1Key, hitKey, screenPt);
            }
        }
        private void ShowOperationContextMenu(string key1, string key2, Point screenPt)
        {
            if (!_module.GroupedFeatures.ContainsKey(key1) || !_module.GroupedFeatures.ContainsKey(key2)) return;
            if (_module.GroupedFeatures[key1].Count == 0 || _module.GroupedFeatures[key2].Count == 0) return;
            BaseFeature f1 = _module.GroupedFeatures[key1].First();
            BaseFeature f2 = _module.GroupedFeatures[key2].First();
            ContextMenuStrip menu = new ContextMenuStrip { BackColor = Color_Slate, ForeColor = Color_Text };
            if (f1 is PointFeature && f2 is PointFeature)
            {
                menu.Items.Add("📏 2D Távolság (Point-to-Point)", null, (s, e) => AddMeasurement(key1, key2, GeometryOperationType.DistancePointToPoint));
                menu.Items.Add("↔️ X irányú távolság (Delta X)", null, (s, e) => AddMeasurement(key1, key2, GeometryOperationType.DeltaXPointToPoint));
                menu.Items.Add("↕️ Y irányú távolság (Delta Y)", null, (s, e) => AddMeasurement(key1, key2, GeometryOperationType.DeltaYPointToPoint));
            }
            else if (f1 is LineFeature && f2 is LineFeature)
            {
                menu.Items.Add("📏 Két egyenes távolsága (Line-to-Line)", null, (s, e) => AddMeasurement(key1, key2, GeometryOperationType.DistanceLineToLine));
                menu.Items.Add("↔️ X irányú távolság (Delta X)", null, (s, e) => AddMeasurement(key1, key2, GeometryOperationType.DeltaXLineToLine));
                menu.Items.Add("↕️ Y irányú távolság (Delta Y)", null, (s, e) => AddMeasurement(key1, key2, GeometryOperationType.DeltaYLineToLine));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("➖ Középvonal létrehozása (Centerline)", null, (s, e) => AddMeasurement(key1, key2, GeometryOperationType.CenterlineLineToLine));
                menu.Items.Add("✖️ Metszéspont létrehozása (Intersection)", null, (s, e) => AddMeasurement(key1, key2, GeometryOperationType.IntersectionLineToLine));
                menu.Items.Add("📐 Két egyenes bezárt szöge (Angle)", null, (s, e) => AddMeasurement(key1, key2, GeometryOperationType.AngleLineToLine));
            }
            else if ((f1 is PointFeature && f2 is LineFeature) || (f1 is LineFeature && f2 is PointFeature))
            {
                menu.Items.Add("📐 Merőleges távolság (Point-to-Line)", null, (s, e) => AddMeasurement(key1, key2, GeometryOperationType.DistancePointToLine));
            }
            if (menu.Items.Count > 0 && _imageBoxRef != null)
            {
                menu.Show(_imageBoxRef, screenPt);
            }
            _module.SelectedFeature1Key = null;
        }
        private void AddMeasurement(string k1, string k2, GeometryOperationType op)
        {
            var newItem = new GeometryMeasurementItem
            {
                Name = $"M{_module.Measurements.Count + 1}",
                Feature1Key = k1,
                Feature2Key = k2,
                Operation = op
            };
            _module.Measurements.Add(newItem);
            _onDataChanged?.Invoke();
            RefreshAllLists();
        }
        private string FindNearestFeature(Point pt, double maxDist)
        {
            string bestKey = null;
            double bestDist = maxDist;
            foreach (var kvp in _module.GroupedFeatures)
            {
                if (!_module.VisibleFeatureKeys.Contains(kvp.Key)) continue;
                // Csoportosított módban mind a 6 alkatrész példányát ellenőrizzük!
                foreach (var feat in kvp.Value)
                {
                    double d = double.MaxValue;
                    if (feat is PointFeature pf)
                        d = Math.Sqrt(Math.Pow(pt.X - pf.Position.X, 2) + Math.Pow(pt.Y - pf.Position.Y, 2));
                    else if (feat is LineFeature lf)
                        d = PointToSegmentDistance(pt, lf.StartPoint, lf.EndPoint);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestKey = kvp.Key;
                    }
                }
            }
            return bestKey;
        }
        private double PointToSegmentDistance(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq == 0) return Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2));
            float t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq));
            float projX = a.X + t * dx, projY = a.Y + t * dy;
            return Math.Sqrt(Math.Pow(p.X - projX, 2) + Math.Pow(p.Y - projY, 2));
        }
    }
}