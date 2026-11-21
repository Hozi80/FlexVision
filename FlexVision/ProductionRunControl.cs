using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Cyotek.Windows.Forms;
using Cyotek.Windows.Forms.Demo;
using Emgu.CV;
using Emgu.CV.Structure;

namespace FlexVision
{
    public class ProductionRunControl : UserControl
    {
        private readonly Color Color_Bg = Color.FromArgb(24, 24, 24);
        private readonly Color Color_Panel = Color.FromArgb(35, 35, 35);
        private readonly Color Color_Light = Color.FromArgb(52, 52, 52);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        private PipelineEditorControl _editorRef;
        private ImageBoxEx imageBoxRun;

        // Állapotjelző és statisztika vezérlők
        private Panel panelMasterStatus;
        private Label lblMasterResult;
        private Label lblCycleTime;
        private Label lblTotalCount, lblPassCount, lblFailCount, lblYieldRate;
        private ListView listViewRunResults;

        // Vezérlőgombok
        private Button btnSingleTrigger;
        private Button btnRunFolder;
        private Button btnContinuous;
        private Button btnResetStats;
        private NumericUpDown numDelayMs;

        private int _totalInspected = 0;
        private int _passCount = 0;
        private int _failCount = 0;
        private CancellationTokenSource _cts = null;

        public ProductionRunControl(PipelineEditorControl editorRef)
        {
            this._editorRef = editorRef;
            this.BackColor = Color_Bg;
            this.Dock = DockStyle.Fill;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SplitContainer splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 850,
                BackColor = Color_Bg,
                FixedPanel = FixedPanel.Panel2
            };

            this.Resize += (s, e) =>
            {
                if (this.Width > 500)
                {
                    splitMain.Panel2MinSize = 380;
                    int dist = this.Width - 390;
                    if (dist > 200) splitMain.SplitterDistance = dist;
                }
            };

            // ====================================================================
            // --- BAL OLDAL: NAGY LIVE PRODUCTION KIJELZŐ ---
            // ====================================================================
            Panel pHeaderLeft = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Color_Light };
            pHeaderLeft.Controls.Add(new Label { Text = "🏭 PRODUCTION LIVE INSPECTION VIEW", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Location = new Point(10, 7), AutoSize = true });

            imageBoxRun = new ImageBoxEx
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(12, 12, 12),
                GridColor = Color.FromArgb(40, 40, 40),
                GridColorAlternate = Color.FromArgb(30, 30, 30)
            };

            splitMain.Panel1.Controls.Add(imageBoxRun);
            splitMain.Panel1.Controls.Add(pHeaderLeft);

            // ====================================================================
            // --- JOBB OLDAL: VEZÉRLÉS, PASS/FAIL LED, STATISZTIKA ÉS MÉRÉSEK ---
            // ====================================================================
            Panel rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color_Panel, Padding = new Padding(12) };
            splitMain.Panel2.Controls.Add(rightPanel);

            // 1. HATALMAS PASS / FAIL JELZŐTÁBLA
            panelMasterStatus = new Panel
            {
                Location = new Point(12, 12),
                Size = new Size(360, 85),
                BackColor = Color.FromArgb(55, 55, 55),
                BorderStyle = BorderStyle.FixedSingle
            };

            lblMasterResult = new Label
            {
                Text = "READY",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 26f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            panelMasterStatus.Controls.Add(lblMasterResult);

            lblCycleTime = new Label
            {
                Text = "Cycle Time: 0.0 ms",
                ForeColor = Color_Cyan,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Location = new Point(12, 105),
                AutoSize = true
            };

            // 2. VEZÉRLŐ GOMBOK (Single, Folder Batch, Continuous)
            btnSingleTrigger = CreateBtn("⚡ SINGLE TRIGGER", 12, 135, 360, 42, Color.LimeGreen, Color.Black);
            btnSingleTrigger.Click += (s, e) => ExecuteSingleCycle();

            btnRunFolder = CreateBtn("📂 RUN FOLDER (BATCH TEST)", 12, 185, 360, 38, Color_Cyan, Color.Black);
            btnRunFolder.Click += BtnRunFolder_Click;

            btnContinuous = CreateBtn("🔄 START CONTINUOUS LOOP", 12, 231, 230, 38, Color_Light, Color.White);
            btnContinuous.Click += BtnContinuous_Click;

            Label lblDelay = new Label { Text = "Delay (ms):", ForeColor = Color_Text, Location = new Point(250, 241), AutoSize = true, Font = new Font("Segoe UI", 8f) };
            numDelayMs = new NumericUpDown { Location = new Point(312, 238), Width = 60, Minimum = 0, Maximum = 5000, Value = 150, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle };

            // 3. GYÁRTÁSI STATISZTIKA PANEL
            Panel panelStats = new Panel
            {
                Location = new Point(12, 282),
                Size = new Size(360, 110),
                BackColor = Color_Bg,
                BorderStyle = BorderStyle.FixedSingle
            };

            lblTotalCount = new Label { Text = "TOTAL: 0", ForeColor = Color_Text, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Location = new Point(12, 12), AutoSize = true };
            lblPassCount = new Label { Text = "PASS: 0", ForeColor = Color.LimeGreen, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Location = new Point(12, 42), AutoSize = true };
            lblFailCount = new Label { Text = "FAIL: 0", ForeColor = Color.Tomato, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Location = new Point(185, 42), AutoSize = true };
            lblYieldRate = new Label { Text = "YIELD: 0.0 %", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Location = new Point(12, 74), AutoSize = true };

            btnResetStats = CreateBtn("RESET", 275, 10, 72, 26, Color_Light, Color.Orange);
            btnResetStats.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btnResetStats.Click += (s, e) =>
            {
                _totalInspected = _passCount = _failCount = 0;
                UpdateStatsUI();
            };

            panelStats.Controls.AddRange(new Control[] { lblTotalCount, lblPassCount, lblFailCount, lblYieldRate, btnResetStats });

            // 4. MÉRÉSI EREDMÉNYEK TÁBLÁZAT
            Label lblTableTitle = new Label { Text = "CURRENT CYCLE MEASUREMENTS", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Location = new Point(12, 405), AutoSize = true };

            listViewRunResults = new ListView
            {
                Location = new Point(12, 427),
                Size = new Size(360, 260),
                BackColor = Color_Bg,
                ForeColor = Color_Text,
                BorderStyle = BorderStyle.FixedSingle,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 8.5f)
            };
            listViewRunResults.Columns.Add("Step / Measure", 145);
            listViewRunResults.Columns.Add("Value", 115, HorizontalAlignment.Right);
            listViewRunResults.Columns.Add("Result", 85, HorizontalAlignment.Center);

            rightPanel.Controls.AddRange(new Control[] {
                panelMasterStatus, lblCycleTime,
                btnSingleTrigger, btnRunFolder, btnContinuous, lblDelay, numDelayMs,
                panelStats, lblTableTitle, listViewRunResults
            });

            this.Controls.Add(splitMain);
        }

        // ====================================================================
        // --- EGYETLEN CIKLUS VÉGREHAJTÁSA (CORE RUN ENGINE) ---
        // ====================================================================
        public void ExecuteSingleCycle(string overrideImagePath = null)
        {
            var steps = _editorRef?.PipelineSteps;
            if (steps == null || steps.Count == 0) return;

            // Ha mappából futtatunk (Batch), átírjuk az első ImageLoadModule elérési útját
            if (!string.IsNullOrEmpty(overrideImagePath) && steps.Count > 0 && steps[0] is ImageLoadModule loadMod)
            {
                loadMod.FilePath = overrideImagePath;
            }

            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            bool overallPass = true;

            using (InspectionContext runContext = new InspectionContext())
            {
                // Végrehajtjuk a teljes pipeline-t a rekurzív motorral
                ExecuteStepsRecursive(steps, runContext, 0, steps.Count - 1);

                totalSw.Stop();

                // Kiértékeljük az összes modul státuszát
                foreach (var step in steps)
                {
                    if (step.Status == ModuleStatus.Failed)
                    {
                        Console.WriteLine("Failed Step: " + step.Name);
                        if (step.Name != "Geometry & Overlay")
                        {
                            overallPass = false;
                            break;
                        }
                    }
                }

                // Kép kirajzolása a Production kijelzőre
                if (runContext.SourceImage != null && runContext.SourceImage.Ptr != IntPtr.Zero)
                {
                    Bitmap oldBmp = imageBoxRun.Image as Bitmap;
                    imageBoxRun.Image = runContext.SourceImage.ToBitmap();
                    oldBmp?.Dispose();
                    if (_totalInspected == 0) imageBoxRun.ZoomToFit();
                }
                // Részletes táblázat frissítése (Geometry mérések + modul státuszok)
                UpdateResultsTable(steps);
            }
            // Statisztika és nagy LED frissítése
            _totalInspected++;
            if (overallPass) _passCount++;
            else _failCount++;
            panelMasterStatus.BackColor = overallPass ? Color.FromArgb(0, 160, 60) : Color.FromArgb(200, 35, 35);
            lblMasterResult.Text = overallPass ? "✅ PASS" : "❌ FAIL";
            lblCycleTime.Text = $"Cycle Time: {totalSw.Elapsed.TotalMilliseconds:F1} ms";
            UpdateStatsUI();
        }
        private void ExecuteStepsRecursive(List<IInspectionModule> steps, InspectionContext currentContext, int startIdx, int maxIdx)
        {
            for (int i = startIdx; i <= maxIdx; i++)
            {
                IInspectionModule step = steps[i];
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (step is SubPipelineEndModule)
                {
                    step.Process(currentContext);
                    sw.Stop();
                    if (step is BaseInspectionModule bEnd)
                    {
                        bEnd.ProcessingTimeMs = sw.Elapsed.TotalMilliseconds;
                        bEnd.Status = ModuleStatus.Success;
                    }

                    // Al-kontextus lezárása: nem engedjük tovább a kisképeket a hurok utáni modulokra!
                    if (currentContext.Parent != null)
                    {
                        break;
                    }
                    continue;
                }

                step.Process(currentContext);
                sw.Stop();
                if (step is BaseInspectionModule baseMod && !(step is TemplateMatchModule) && !(step is EdgeBasedMatchModule) && !(step is BlobAnalysisModule) && !(step is EdgeCaliperModule) && !(step is GeometryMeasureModule) && !(step is ImageSaveModule))
                {
                    baseMod.ProcessingTimeMs = sw.Elapsed.TotalMilliseconds;
                    baseMod.Status = ModuleStatus.Success;
                }
                if (currentContext.SourceImage != null && currentContext.SourceImage.Ptr != IntPtr.Zero)
                    currentContext.ColorHistory[step.Name] = currentContext.SourceImage.Copy();
                if (currentContext.WorkingMask != null && currentContext.WorkingMask.Ptr != IntPtr.Zero)
                    currentContext.GrayHistory[step.Name] = currentContext.WorkingMask.Copy();
                if (currentContext.SubContexts != null && currentContext.SubContexts.Count > 0)
                {
                    foreach (var subContext in currentContext.SubContexts)
                    {
                        subContext.Parent = currentContext;
                        if (subContext.SourceImage != null && subContext.SourceImage.Ptr != IntPtr.Zero)
                            subContext.ColorHistory[step.Name] = subContext.SourceImage.Copy();
                        if (subContext.WorkingMask != null && subContext.WorkingMask.Ptr != IntPtr.Zero)
                            subContext.GrayHistory[step.Name] = subContext.WorkingMask.Copy();
                        ExecuteStepsRecursive(steps, subContext, i + 1, maxIdx);
                    }
                    // Hurok végének megkeresése
                    for (int j = i + 1; j < steps.Count; j++)
                    {
                        if (steps[j] is SubPipelineEndModule) { i = j; break; }
                    }
                }
            }
        }
        private void UpdateResultsTable(List<IInspectionModule> steps)
        {
            listViewRunResults.BeginUpdate();
            listViewRunResults.Items.Clear();
            foreach (var step in steps)
            {
                if (step is SubPipelineEndModule || step is ImageLoadModule) continue;
                // Ha GeometryMeasureModule, kilistázzuk az összes kiértékelt méretét!
                if (step is GeometryMeasureModule geomMod && geomMod.EvaluatedResults != null)
                {
                    string unit = geomMod.UseMillimeters ? "mm" : "px";
                    foreach (var eval in geomMod.EvaluatedResults)
                    {
                        ListViewItem gItem = new ListViewItem(eval.DisplayName);
                        gItem.SubItems.Add($"{eval.MeasuredValue:F2} {unit}");
                        gItem.SubItems.Add(eval.IsPass ? "PASS" : "FAIL");
                        gItem.ForeColor = eval.IsPass ? Color.LightGreen : Color.Tomato;
                        listViewRunResults.Items.Add(gItem);
                    }
                }
                else
                {
                    ListViewItem item = new ListViewItem(step.Name);
                    item.SubItems.Add($"{step.ProcessingTimeMs:F1} ms");
                    item.SubItems.Add(step.Status.ToString().ToUpper());
                    item.ForeColor = (step.Status == ModuleStatus.Failed) ? Color.Tomato : Color.White;
                    listViewRunResults.Items.Add(item);
                }
            }
            listViewRunResults.EndUpdate();
        }
        private void UpdateStatsUI()
        {
            lblTotalCount.Text = $"TOTAL: {_totalInspected}";
            lblPassCount.Text = $"PASS: {_passCount}";
            lblFailCount.Text = $"FAIL: {_failCount}";
            double yield = _totalInspected > 0 ? (_passCount * 100.0 / _totalInspected) : 0.0;
            lblYieldRate.Text = $"YIELD: {yield:F1} %";
        }
        // ====================================================================
        // --- MAPPA FUTTATÁS (BATCH FOLDER TEST) ---
        // ====================================================================
        private async void BtnRunFolder_Click(object sender, EventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                return;
            }
            using (FolderBrowserDialog fbd = new FolderBrowserDialog { Description = "Válaszd ki a tesztképeket tartalmazó mappát!", SelectedPath = @"C:\Test" })
            {
                if (fbd.ShowDialog() != DialogResult.OK) return;
                string[] extensions = { ".bmp", ".png", ".jpg", ".jpeg" };
                var files = Directory.GetFiles(fbd.SelectedPath)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .OrderBy(f => f)
                .ToArray();
                if (files.Length == 0)
                {
                    MessageBox.Show("A kiválasztott mappában nem található kép (.bmp, .png, .jpg)!", "Üres mappa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _cts = new CancellationTokenSource();
                btnRunFolder.Text = "⏹ STOP FOLDER RUN";
                btnRunFolder.BackColor = Color.Tomato;
                btnSingleTrigger.Enabled = false;
                btnContinuous.Enabled = false;
                try
                {
                    foreach (string file in files)
                    {
                        if (_cts.Token.IsCancellationRequested) break;
                        ExecuteSingleCycle(file);
                        int delay = (int)numDelayMs.Value;
                        if (delay > 0) await Task.Delay(delay, _cts.Token);
                        else Application.DoEvents();
                    }
                }
                catch (TaskCanceledException) { }
                finally
                {
                    _cts.Dispose();
                    _cts = null;
                    btnRunFolder.Text = "📂 RUN FOLDER (BATCH TEST)";
                    btnRunFolder.BackColor = Color_Cyan;
                    btnSingleTrigger.Enabled = true;
                    btnContinuous.Enabled = true;
                }
            }
        }
        // ====================================================================
        // --- FOLYAMATOS CIKLUS FUTTATÁS (CONTINUOUS LOOP) ---
        // ====================================================================
        private async void BtnContinuous_Click(object sender, EventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                return;
            }
            _cts = new CancellationTokenSource();
            btnContinuous.Text = "⏹ STOP LOOP";
            btnContinuous.BackColor = Color.Tomato;
            btnSingleTrigger.Enabled = false;
            btnRunFolder.Enabled = false;
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    ExecuteSingleCycle();
                    int delay = (int)numDelayMs.Value;
                    if (delay > 0) await Task.Delay(delay, _cts.Token);
                    else await Task.Delay(10, _cts.Token);
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnContinuous.Text = "🔄 START CONTINUOUS LOOP";
                btnContinuous.BackColor = Color_Light;
                btnSingleTrigger.Enabled = true;
                btnRunFolder.Enabled = true;
            }
        }
        private Button CreateBtn(string text, int x, int y, int w, int h, Color bg, Color fg)
        {
            Button b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = fg,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }
    }
}