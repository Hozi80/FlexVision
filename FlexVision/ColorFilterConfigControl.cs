using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Slider.Controls;
using Slider.Classes;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace FlexVision
{
    public class ColorFilterConfigControl : UserControl, IColorPickable
    {
        private readonly Color Color_Bg = Color.FromArgb(30, 30, 30);
        private readonly Color Color_Slate = Color.FromArgb(45, 45, 45);
        private readonly Color Color_Light = Color.FromArgb(60, 60, 60);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        private ColorInRangeModule _module;
        private Action _onDataChanged;
        private PipelineEditorControl _mainEditor;

        private RadioButton rbBgr, rbHsv, rbLab;
        private Button btnPickColor;
        private Button btnReset;
        private CheckBox chkInvert;

        // --- ÚJ: OSZTÁLYSZINTŰ CHECKBOX DEFINÍCIÓ AZ ONLYMASK PARAMÉTERHEZ ---
        private CheckBox chkOnlyMask;

        public RangeSlider sliderCh1, sliderCh2, sliderCh3;
        private Label lblCh1Title, lblCh2Title, lblCh3Title;
        private Label lblCh1Values, lblCh2Values, lblCh3Values;

        private NumericUpDown numR, numG, numB;

        public ColorFilterConfigControl(ColorInRangeModule module, Action onDataChanged, PipelineEditorControl parentEditor)
        {
            this._module = module;
            this._onDataChanged = onDataChanged;
            this._mainEditor = parentEditor;
            this.BackColor = Color_Bg;
            this.Dock = DockStyle.Fill;

            InitializeComponent();
            UpdateLabels();
        }

        private void InitializeComponent()
        {
            Label lblTitle1 = new Label { Text = "AKTÍV SZÍNTÉR KIVÁLASZTÁSA", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true };

            rbBgr = CreateRadio("BGR", 10, 22, _module.ActiveColorSpace == ColorSpaceType.BGR);
            rbHsv = CreateRadio("HSV", 95, 22, _module.ActiveColorSpace == ColorSpaceType.HSV);
            rbLab = CreateRadio("LAB", 180, 22, _module.ActiveColorSpace == ColorSpaceType.LAB);

            rbBgr.CheckedChanged += (s, e) => { if (rbBgr.Checked) { _module.ActiveColorSpace = ColorSpaceType.BGR; ResetSlidersToCenter(); } };
            rbHsv.CheckedChanged += (s, e) => { if (rbHsv.Checked) { _module.ActiveColorSpace = ColorSpaceType.HSV; ResetSlidersToCenter(); } };
            rbLab.CheckedChanged += (s, e) => { if (rbLab.Checked) { _module.ActiveColorSpace = ColorSpaceType.LAB; ResetSlidersToCenter(); } };

            btnPickColor = new Button { Text = "🎯 PICK COLOR (FINOMÍTÁS)", Location = new Point(10, 48), Width = 180, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color_Light, ForeColor = Color_Cyan, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnPickColor.FlatAppearance.BorderSize = 0;
            btnPickColor.Click += (s, e) => { if (_mainEditor != null) _mainEditor.StartColorPicking(); };

            btnReset = new Button { Text = "🔄 RESET", Location = new Point(195, 48), Width = 135, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color_Light, ForeColor = Color.FromArgb(255, 120, 10), Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnReset.FlatAppearance.BorderSize = 0;
            btnReset.Click += (s, e) => { ResetFilterToDefault(); };

            // ====================================================================
            // --- ÚJ CHECKBOX: ONLY MASK FINOMHANGOLÁS (A MEGLÉVŐ INVERT FÖLÉ) ---
            // ====================================================================
            chkOnlyMask = new CheckBox
            {
                Text = "ONLY MASK (TISZTA BINÁRIS)",
                Location = new Point(20, 468), // Tűpontosan a te Invert gombod (500) felé illesztve
                AutoSize = true,
                ForeColor = Color_Text,
                Checked = _module.OnlyMask,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold)
            };

            // Leköveti a te bevált, golyóálló kettős frissítési parancsláncodat
            chkOnlyMask.CheckedChanged += (s, e) => {
                _module.OnlyMask = chkOnlyMask.Checked;
                _mainEditor.RunPipelineDebug();
                _onDataChanged?.Invoke();
            };

            // A te meglévő, eredeti invertáló checkboxod (Változatlanul hagyva!)
            chkInvert = new CheckBox { Text = "INVERT MASK", Location = new Point(20, 500), AutoSize = true, ForeColor = Color_Text, Checked = _module.IsInverted, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold) };
            chkInvert.CheckedChanged += (s, e) => { _module.IsInverted = chkInvert.Checked; _mainEditor.RunPipelineDebug(); _onDataChanged?.Invoke(); };

            // 3 darab függőleges csúszka
            lblCh1Title = new Label { Text = "CH1", ForeColor = Color_Text, Location = new Point(25, 85), Width = 70, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold) };
            sliderCh1 = new RangeSlider { Location = new Point(45, 105), Width = 270, Height = 30, Orientation = HVOrientation.Vertical, BarMin = 0, BarMax = 255, RangeMin = _module.MinCh1, RangeMax = _module.MaxCh1, CustomBackground = true, BackColor = Color_Slate };
            sliderCh1.Scroll += (s, e) => { _module.MinCh1 = (byte)sliderCh1.RangeMin; _module.MaxCh1 = (byte)sliderCh1.RangeMax; UpdateLabels(); _mainEditor.RunPipelineDebug(); _onDataChanged?.Invoke(); };
            lblCh1Values = new Label { ForeColor = Color_Text, Location = new Point(20, 380), Width = 80, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 7.5f) };

            lblCh2Title = new Label { Text = "CH2", ForeColor = Color_Text, Location = new Point(135, 85), Width = 70, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold) };
            sliderCh2 = new RangeSlider { Location = new Point(155, 105), Width = 270, Height = 30, Orientation = HVOrientation.Vertical, BarMin = 0, BarMax = 255, RangeMin = _module.MinCh2, RangeMax = _module.MaxCh2, CustomBackground = true, BackColor = Color_Slate };
            sliderCh2.Scroll += (s, e) => { _module.MinCh2 = (byte)sliderCh2.RangeMin; _module.MaxCh2 = (byte)sliderCh2.RangeMax; UpdateLabels(); _mainEditor.RunPipelineDebug(); _onDataChanged?.Invoke(); };
            lblCh2Values = new Label { ForeColor = Color_Text, Location = new Point(130, 380), Width = 80, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 7.5f) };

            lblCh3Title = new Label { Text = "CH3", ForeColor = Color_Text, Location = new Point(245, 85), Width = 70, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold) };
            sliderCh3 = new RangeSlider { Location = new Point(265, 105), Width = 270, Height = 30, Orientation = HVOrientation.Vertical, BarMin = 0, BarMax = 255, RangeMin = _module.MinCh3, RangeMax = _module.MaxCh3, CustomBackground = true, BackColor = Color_Slate };
            sliderCh3.Scroll += (s, e) => { _module.MinCh3 = (byte)sliderCh3.RangeMin; _module.MaxCh3 = (byte)sliderCh3.RangeMax; UpdateLabels(); _mainEditor.RunPipelineDebug(); _onDataChanged?.Invoke(); };
            lblCh3Values = new Label { ForeColor = Color_Text, Location = new Point(240, 380), Width = 80, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 7.5f) };

            UpdateSliderColors();

            Label lblTitle2 = new Label { Text = "KIJELÖLÉS OVERLAY SZÍNE (BGR)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Location = new Point(10, 425), AutoSize = true };
            Label lblB = new Label { Text = "B:", ForeColor = Color_Text, Location = new Point(15, 450), AutoSize = true };
            numB = new NumericUpDown { Location = new Point(35, 448), Width = 55, Minimum = 0, Maximum = 255, Value = _module.OverlayBlue, BackColor = Color_Light, ForeColor = Color_Text, Font = new Font("Segoe UI", 8) };
            Label lblG = new Label { Text = "G:", ForeColor = Color_Text, Location = new Point(115, 450), AutoSize = true };
            numG = new NumericUpDown { Location = new Point(135, 448), Width = 55, Minimum = 0, Maximum = 255, Value = _module.OverlayGreen, BackColor = Color_Light, ForeColor = Color_Text, Font = new Font("Segoe UI", 8) };
            Label lblR = new Label { Text = "R:", ForeColor = Color_Text, Location = new Point(220, 450), AutoSize = true };
            numR = new NumericUpDown { Location = new Point(240, 448), Width = 55, Minimum = 0, Maximum = 255, Value = _module.OverlayRed, BackColor = Color_Light, ForeColor = Color_Text, Font = new Font("Segoe UI", 8) };

            numB.ValueChanged += (s, e) => { _module.OverlayBlue = (byte)numB.Value; _mainEditor.RunPipelineDebug(); _onDataChanged?.Invoke(); };
            numG.ValueChanged += (s, e) => { _module.OverlayGreen = (byte)numG.Value; _mainEditor.RunPipelineDebug(); _onDataChanged?.Invoke(); };
            numR.ValueChanged += (s, e) => { _module.OverlayRed = (byte)numR.Value; _mainEditor.RunPipelineDebug(); _onDataChanged?.Invoke(); };

            this.Controls.Add(lblTitle1); this.Controls.Add(rbBgr); this.Controls.Add(rbHsv); this.Controls.Add(rbLab);

            // Felpakoljuk az új OnlyMask gombot is a Controls listába!
            this.Controls.Add(btnPickColor); this.Controls.Add(btnReset);
            this.Controls.Add(chkOnlyMask); this.Controls.Add(chkInvert);

            this.Controls.Add(lblCh1Title); this.Controls.Add(sliderCh1); this.Controls.Add(lblCh1Values);
            this.Controls.Add(lblCh2Title); this.Controls.Add(sliderCh2); this.Controls.Add(lblCh2Values);
            this.Controls.Add(lblCh3Title); this.Controls.Add(sliderCh3); this.Controls.Add(lblCh3Values);
            this.Controls.Add(lblTitle2); this.Controls.Add(lblB); this.Controls.Add(numB);
            this.Controls.Add(lblG); this.Controls.Add(numG); this.Controls.Add(lblR); this.Controls.Add(numR);
        }
        private RadioButton CreateRadio(string text, int x, int y, bool checkedState)
        {
            return new RadioButton { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Color_Text, Checked = checkedState, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
        }
        private void ResetSlidersToCenter()
        {
            sliderCh1.RangeMin = 110; sliderCh1.RangeMax = 140;
            sliderCh2.RangeMin = 110; sliderCh2.RangeMax = 140;
            sliderCh3.RangeMin = 110; sliderCh3.RangeMax = 140;
            _module.MinCh1 = 110; _module.MaxCh1 = 140;
            _module.MinCh2 = 110; _module.MaxCh2 = 140;
            _module.MinCh3 = 110; _module.MaxCh3 = 140;
            UpdateLabels(); UpdateSliderColors();
            this.Invalidate(); sliderCh1.Invalidate(); sliderCh2.Invalidate(); sliderCh3.Invalidate();
            _mainEditor.RunPipelineDebug();
            _onDataChanged?.Invoke();
        }
        private void ResetFilterToDefault()
        {
            _module.IsFilterActivated = false;
            _module.IsInverted = false;
            _module.OnlyMask = false; // Reseteléskor a tiszta bináris módot is kikapcsoljuk
            chkInvert.Checked = false;
            if (chkOnlyMask != null) chkOnlyMask.Checked = false;
            sliderCh1.RangeMin = 0; sliderCh1.RangeMax = 255;
            sliderCh2.RangeMin = 0; sliderCh2.RangeMax = 255;
            sliderCh3.RangeMin = 0; sliderCh3.RangeMax = 255;
            _module.MinCh1 = 0; _module.MaxCh1 = 255;
            _module.MinCh2 = 0; _module.MaxCh2 = 255;
            _module.MinCh3 = 0; _module.MaxCh3 = 255;
            UpdateLabels(); UpdateSliderColors();
            this.Invalidate(); sliderCh1.Invalidate(); sliderCh2.Invalidate(); sliderCh3.Invalidate();
            _mainEditor.RunPipelineDebug();
            _onDataChanged?.Invoke();
        }
        private void UpdateSliderColors()
        {
            if (_module.ActiveColorSpace == ColorSpaceType.BGR)
            {
                sliderCh1.Color = EMyColors.Blue; sliderCh2.Color = EMyColors.Green; sliderCh3.Color = EMyColors.Red;
            }
            else
            {
                sliderCh1.Color = EMyColors.Teal; sliderCh2.Color = EMyColors.Silver; sliderCh3.Color = EMyColors.Orange;
            }
            sliderCh1.Invalidate(); sliderCh2.Invalidate(); sliderCh3.Invalidate();
        }
        private void UpdateLabels()
        {
            if (_module.ActiveColorSpace == ColorSpaceType.BGR)
            {
                lblCh1Title.Text = "BLUE"; lblCh2Title.Text = "GREEN"; lblCh3Title.Text = "RED";
            }
            else if (_module.ActiveColorSpace == ColorSpaceType.HSV)
            {
                lblCh1Title.Text = "HUE (H)"; lblCh2Title.Text = "SAT (S)"; lblCh3Title.Text = "VAL (V)";
            }
            else if (_module.ActiveColorSpace == ColorSpaceType.LAB)
            {
                lblCh1Title.Text = "LUMIN (L)"; lblCh2Title.Text = "A-CH (A)"; lblCh3Title.Text = "B-CH (B)";
            }
            lblCh1Values.Text = $"{sliderCh1.RangeMin} - {sliderCh1.RangeMax}";
            lblCh2Values.Text = $"{sliderCh2.RangeMin} - {sliderCh2.RangeMax}";
            lblCh3Values.Text = $"{sliderCh3.RangeMin} - {sliderCh3.RangeMax}";
        }
        public void HandlePickedColor(Bgr bgrColor, Point pixelLocation)
        {
            int toleranceCh1 = 20; int toleranceCh2 = 20; int toleranceCh3 = 20;
            double targetCh1 = bgrColor.Blue;
            double targetCh2 = bgrColor.Green;
            double targetCh3 = bgrColor.Red;
            if (_module.ActiveColorSpace == ColorSpaceType.HSV || _module.ActiveColorSpace == ColorSpaceType.LAB)
            {
                using (Image<Bgr, byte> src1x1 = new Image<Bgr, byte>(1, 1, bgrColor))
                using (Mat dstMat = new Mat())
                {
                    if (_module.ActiveColorSpace == ColorSpaceType.HSV)
                    {
                        CvInvoke.CvtColor(src1x1, dstMat, ColorConversion.Bgr2Hsv);
                        using (Image<Hsv, byte> hsvImage = dstMat.ToImage<Hsv, byte>())
                        {
                            targetCh1 = hsvImage.Data[0, 0, 0]; targetCh2 = hsvImage.Data[0, 0, 1]; targetCh3 = hsvImage.Data[0, 0, 2];
                        }
                        toleranceCh1 = 12; toleranceCh2 = 30; toleranceCh3 = 30;
                    }
                    else if (_module.ActiveColorSpace == ColorSpaceType.LAB)
                    {
                        CvInvoke.CvtColor(src1x1, dstMat, ColorConversion.Bgr2Lab);
                        using (Image<Lab, byte> labImage = dstMat.ToImage<Lab, byte>())
                        {
                            targetCh1 = labImage.Data[0, 0, 0]; targetCh2 = labImage.Data[0, 0, 1]; targetCh3 = labImage.Data[0, 0, 2];
                        }
                        toleranceCh1 = 25; toleranceCh2 = 15; toleranceCh3 = 15;
                    }
                }
            }
            int clickMin1 = Math.Max(0, (int)targetCh1 - toleranceCh1); int clickMax1 = Math.Min(255, (int)targetCh1 + toleranceCh1);
            int clickMin2 = Math.Max(0, (int)targetCh2 - toleranceCh2); int clickMax2 = Math.Min(255, (int)targetCh2 + toleranceCh2);
            int clickMin3 = Math.Max(0, (int)targetCh3 - toleranceCh3); int clickMax3 = Math.Min(255, (int)targetCh3 + toleranceCh3);
            if (!_module.IsFilterActivated)
            {
                sliderCh1.RangeMin = clickMin1; sliderCh1.RangeMax = clickMax1;
                sliderCh2.RangeMin = clickMin2; sliderCh2.RangeMax = clickMax2;
                sliderCh3.RangeMin = clickMin3; sliderCh3.RangeMax = clickMax3;
                _module.IsFilterActivated = true;
            }
            else
            {
                sliderCh1.RangeMin = Math.Min(sliderCh1.RangeMin, clickMin1);
                sliderCh1.RangeMax = Math.Max(sliderCh1.RangeMax, clickMax1);
                sliderCh2.RangeMin = Math.Min(sliderCh2.RangeMin, clickMin2);
                sliderCh2.RangeMax = Math.Max(sliderCh2.RangeMax, clickMax2);
                sliderCh3.RangeMin = Math.Min(sliderCh3.RangeMin, clickMin3);
                sliderCh3.RangeMax = Math.Max(sliderCh3.RangeMax, clickMax3);
            }
            _module.MinCh1 = (byte)sliderCh1.RangeMin; _module.MaxCh1 = (byte)sliderCh1.RangeMax;
            _module.MinCh2 = (byte)sliderCh2.RangeMin; _module.MaxCh2 = (byte)sliderCh2.RangeMax;
            _module.MinCh3 = (byte)sliderCh3.RangeMin; _module.MaxCh3 = (byte)sliderCh3.RangeMax;
            UpdateLabels();
            this.Invalidate();
            sliderCh1.Invalidate(); sliderCh2.Invalidate(); sliderCh3.Invalidate();
            _mainEditor.RunPipelineDebug();
            _onDataChanged?.Invoke();
        }
    }
}