using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using Emgu.CV;
using Emgu.CV.Structure;
using Cyotek.Windows.Forms.Demo;
using Cyotek.Windows.Forms;

namespace FlexVision
{
    public class PipelineEditorControl : UserControl
    {
        private readonly Color Color_Bg = Color.FromArgb(30, 30, 30);
        private readonly Color Color_Slate = Color.FromArgb(45, 45, 45);
        private readonly Color Color_Light = Color.FromArgb(60, 60, 60);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        private ListBox listBoxPipeline;

        // --- ÚJ: TISZTA, GÖRGETÉSMENTES COMMON VEZÉRLŐK (PROPERTGRID HELYETT) ---
        private Panel panelCommonControls;
        private ComboBox comboInputSrc;
        private ComboBox comboXSrc;
        private ComboBox comboYSrc;
        private ComboBox comboRotSrc;
        private TextBox txtCustomName;

        // --- ÚJ: UNIVERZÁLIS AL-PIPELINE / SUB-CONTEXT LAPOZÓ SÁV ---
        private Panel panelSubLoopNavigator;
        private Button btnSubPrev, btnSubNext;
        private Label lblSubCounter;
        private int _activeSubLoopIndex = 0; // Az éppen kijelzett al-kép indexe (0..N)

        private Panel panelSpecificContainer;

        private ImageBoxEx imageBoxExMain;
        private SplitContainer splitLeft;
        private SplitContainer splitRight;

        private Panel panelToolbar;
        private Button btnNew, btnSave, btnRunAll;
        private Button btnAdd, btnDelete;
        private ContextMenuStrip menuAddModule;
        private Label lblPipelineTitle;

        private bool _isSelectingTemplate = false;
        private bool _isSelectingRoi = false;
        private bool _isColorPicking = false;
        private InspectionContext _lastInspectionCache = null;

        public List<IInspectionModule> PipelineSteps { get; private set; } = new List<IInspectionModule>();

        public PipelineEditorControl()
        {
            this.BackColor = Color_Bg;
            this.Dock = DockStyle.Fill;
            InitializeComponent();
            InitializeAddMenu();
            CreateDefaultPipeline();
        }

        private void InitializeComponent()
        {
            // HASÁBOK ALAPSTRUKTÚRÁJA
            splitLeft = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 270, BackColor = Color_Bg, FixedPanel = FixedPanel.Panel1 };
            splitRight = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 600, BackColor = Color_Bg, FixedPanel = FixedPanel.Panel2 };

            this.Controls.Add(splitLeft);
            splitLeft.Panel2.Controls.Add(splitRight);

            this.Resize += (s, e) => {
                if (this.Width > 650)
                {
                    splitLeft.Panel1MinSize = 270;
                    splitRight.Panel2MinSize = 350;
                    int targetDistance = splitLeft.Panel2.Width - 350;
                    if (targetDistance > splitRight.Panel1MinSize) splitRight.SplitterDistance = targetDistance;
                }
            };

            // BAL OLDALI SALV GOMBOK
            panelToolbar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color_Slate };
            btnNew = new Button { Text = "📄 NEW", Location = new Point(6, 10), Width = 83, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color_Cyan, BackColor = Color_Light, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnNew.Click += (s, e) => { CreateDefaultPipeline(); RefreshUI(); };
            btnSave = new Button { Text = "💾 SAVE", Location = new Point(93, 10), Width = 83, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color_Text, BackColor = Color_Light, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSave.Click += OnSaveClick;
            btnRunAll = new Button { Text = "▶ RUN", Location = new Point(180, 10), Width = 84, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color.LightGreen, BackColor = Color_Light, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
  //          btnRunAll.Click += (s, e) => { if (listBoxPipeline.Items.Count > 0) listBoxPipeline.SelectedIndex = listBoxPipeline.Items.Count - 1; };
            btnRunAll.Click += (s, e) => {
                // Amikor megnyomják a Run gombot, kényszerítjük a teljes lánc lefutását az utolsó elemig!
                RunPipelineDebug(stepDirection: 0, runAll: true);
            };


            panelToolbar.Controls.Add(btnNew); panelToolbar.Controls.Add(btnSave); panelToolbar.Controls.Add(btnRunAll);

            // BAL OLDALI LISTBOX FOLYAMAT
            Panel pListHeader = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color_Light };
            lblPipelineTitle = new Label
            {
                Text = "PIPELINE STEPS",
                ForeColor = Color_Text,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Location = new Point(10, 8),
                AutoSize = true // Fontos, hogy ne vágja le a szöveget, ha hozzáfűzzük az ms adatokat!
            };

            // Hozzáadjuk a listadoboz feletti sötétszürke fejléc panelhez
            pListHeader.Controls.Add(lblPipelineTitle);
            //            pListHeader.Controls.Add(new Label { Text = "PIPELINE STEPS", ForeColor = Color_Text, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 8) });
            listBoxPipeline = new ListBox { Dock = DockStyle.Fill, BackColor = Color_Bg, ForeColor = Color_Text, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10), ItemHeight = 24, DrawMode = DrawMode.OwnerDrawFixed };
            listBoxPipeline.DrawItem += ListBoxPipeline_DrawItem;
            listBoxPipeline.SelectedIndexChanged += ListBoxPipeline_SelectedIndexChanged;

            Panel pListButtons = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Color_Slate };
            btnAdd = new Button { Text = "[ + ADD STEP ]", Width = 125, Height = 28, Location = new Point(6, 6), FlatStyle = FlatStyle.Flat, BackColor = Color_Light, ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
            btnDelete = new Button { Text = "[ - REMOVE ]", Width = 125, Height = 28, Location = new Point(135, 6), FlatStyle = FlatStyle.Flat, BackColor = Color_Light, ForeColor = Color.FromArgb(255, 120, 10), Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
            btnAdd.Click += (s, e) => { menuAddModule.Show(btnAdd, new Point(0, btnAdd.Height)); };
            btnDelete.Click += BtnDelete_Click;
            pListButtons.Controls.Add(btnAdd); pListButtons.Controls.Add(btnDelete);

            splitLeft.Panel1.Controls.Add(listBoxPipeline); splitLeft.Panel1.Controls.Add(pListHeader); splitLeft.Panel1.Controls.Add(panelToolbar); splitLeft.Panel1.Controls.Add(pListButtons);

            // KÖZÉPSŐ NAGY KÉP (TE IMAGEBOXEX VEZÉRLŐD)
            Panel pImgHeader = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color_Light };
            pImgHeader.Controls.Add(new Label { Text = "LIVE VIEW DEBUG", ForeColor = Color_Text, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 8) });
            imageBoxExMain = new ImageBoxEx { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 15), Name = "imageBoxExMain" };
            imageBoxExMain.GridColor = Color.FromArgb(55, 55, 55); imageBoxExMain.GridColorAlternate = Color.FromArgb(45, 45, 45);
            imageBoxExMain.Selected += ImageBoxExMain_SelectionChanged;
            imageBoxExMain.MouseClick += ImageBoxExMain_MouseClick;
            imageBoxExMain.MouseMove += (s, e) => {
                // Ha éppen színt választ a mérnök, felülbíráljuk a Cyotek belső SetCursor metódusát,
                // és rákényszerítjük a precíz célkeresztet az ablakra, amíg a kattintás meg nem történik!
                if (_isColorPicking)
                {
                    imageBoxExMain.Cursor = Cursors.Cross;
                }
            };


            splitRight.Panel1.Controls.Add(imageBoxExMain); splitRight.Panel1.Controls.Add(pImgHeader);

            // ====================================================================
            // JOBB HASÁB: ADAT-ELRENDEZÉS (COMBOBOXOK PANEL + AL-PIPELINE LAPOZÓ)
            // ====================================================================
            Panel panelRightMaster = new Panel { Dock = DockStyle.Fill, BackColor = Color_Bg };
            splitRight.Panel2.Controls.Add(panelRightMaster);

            // --- 1. DEDIKÁLT COMBOBOX PANEL (FIX 185 PX MAGAS, GÖRGETÉSMENTES) ---
            panelCommonControls = new Panel { Dock = DockStyle.Top, Height = 195, BackColor = Color_Bg, BorderStyle = BorderStyle.FixedSingle };
            Panel pCommonHeader = new Panel { Dock = DockStyle.Top, Height = 25, BackColor = Color_Light };
            pCommonHeader.Controls.Add(new Label { Text = "⚙️ COMMON CONFIGURATION (BEMENET / OFFSET)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Location = new Point(8, 5), AutoSize = true });
            panelCommonControls.Controls.Add(pCommonHeader);

            // Szép, sötét tónusú ComboBoxok és feliratok elhelyezése
            AddCommonField("Input Image Source:", out comboInputSrc, 30);
            AddCommonField("X-Axis Offset Ref:", out comboXSrc, 62);
            AddCommonField("Y-Axis Offset Ref:", out comboYSrc, 94);
            AddCommonField("Rotation Offset Ref:", out comboRotSrc, 126);

            Label lblName = new Label { Text = "Custom Feature Name:", ForeColor = Color_Text, Location = new Point(10, 162), Width = 125, Font = new Font("Segoe UI", 8) };
            txtCustomName = new TextBox { Location = new Point(140, 160), Width = 190, BackColor = Color_Light, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 8.5f) };
            txtCustomName.TextChanged += (s, e) => { var m = GetSelectedModule(); if (m != null) { m.CustomFeatureName = txtCustomName.Text; RefreshUI(); } };
            panelCommonControls.Controls.Add(lblName); panelCommonControls.Controls.Add(txtCustomName);

            // Eseménykezelők a ComboBoxokhoz
            comboInputSrc.SelectedIndexChanged += OnCommonComboChanged;
            comboXSrc.SelectedIndexChanged += OnCommonComboChanged;
            comboYSrc.SelectedIndexChanged += OnCommonComboChanged;
            comboRotSrc.SelectedIndexChanged += OnCommonComboChanged;

            // --- 2. ÚJ: UNIVERZÁLIS AL-PIPELINE CIKLUS LAPOZÓ SÁV ---
            panelSubLoopNavigator = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color_Slate, Visible = false };
            Label lblSubTitle = new Label { Text = "SUB-LOOP VIEW:", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Location = new Point(10, 13), AutoSize = true };
            btnSubPrev = new Button { Text = "◀ PREV", Location = new Point(140, 6), Width = 55, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Color_Light, ForeColor = Color_Text, Font = new Font("Segoe UI", 7.0f, FontStyle.Bold) };
            lblSubCounter = new Label { Text = "0 / 0", ForeColor = Color_Text, Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(205, 11), Width = 60, TextAlign = ContentAlignment.MiddleCenter };
            btnSubNext = new Button { Text = "NEXT ▶", Location = new Point(275, 6), Width = 55, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Color_Light, ForeColor = Color_Text, Font = new Font("Segoe UI", 7.0f, FontStyle.Bold) };
            btnSubPrev.Click += (s, e) => { NavigateSubLoop(-1); };
            btnSubNext.Click += (s, e) => { NavigateSubLoop(1); };
            panelSubLoopNavigator.Controls.Add(lblSubTitle); panelSubLoopNavigator.Controls.Add(btnSubPrev); panelSubLoopNavigator.Controls.Add(lblSubCounter); panelSubLoopNavigator.Controls.Add(btnSubNext);
            // --- 3. ALSÓ SPECIFIKUS TÁROLÓ KONTÉNER ---
            panelSpecificContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color_Bg };
            panelRightMaster.Controls.Add(panelSpecificContainer);
            panelRightMaster.Controls.Add(panelSubLoopNavigator); // Beágyazva középre!
            panelRightMaster.Controls.Add(panelCommonControls);
        }
        private void AddCommonField(string labelText, out ComboBox combo, int y)
        {
            Label lbl = new Label { Text = labelText, ForeColor = Color_Text, Location = new Point(10, y + 2), Width = 125, Font = new Font("Segoe UI", 8) };
            combo = new ComboBox { Location = new Point(140, y), Width = 190, BackColor = Color_Light, ForeColor = Color_Text, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8.5f) };
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            panelCommonControls.Controls.Add(lbl); panelCommonControls.Controls.Add(combo);
        }
        // --- DINAMIKUS MEGJELENÍTÉS ÉS COMBOBOX TÖLTÉS A LISTAVÁLTÁSKOR ---
        private void ListBoxPipeline_SelectedIndexChanged(object sender, EventArgs e)
        {
            int idx = listBoxPipeline.SelectedIndex;
            if (idx < 0) return;
            BaseInspectionModule selectedModule = PipelineSteps[idx] as BaseInspectionModule;
            if (selectedModule == null) return;
            // 1. LÉPÉS: DINAMIKUSAN FELTÖLTJÜK A COMBOBOXOKAT AZ ADDIG ELÉRHETŐ REFERENCIÁKKAL
            FillCommonComboBoxes(idx, selectedModule);
            panelSpecificContainer.Controls.Clear();
            panelSpecificContainer.SuspendLayout();
            if (selectedModule is TemplateMatchModule tmm)
            {
                TemplateMatchConfigControl matchUi = new TemplateMatchConfigControl(tmm, () => RunPipelineDebug(), this);
                matchUi.Dock = DockStyle.Fill;
                panelSpecificContainer.Controls.Add(matchUi);
            }
            else if (selectedModule is EdgeBasedMatchModule emm)
            {
                EdgeBasedMatchConfigControl edgeUi = new EdgeBasedMatchConfigControl(emm, () => RunPipelineDebug(), this);
                edgeUi.Dock = DockStyle.Fill;
                panelSpecificContainer.Controls.Add(edgeUi);
            }
            else if (selectedModule is ColorInRangeModule cim)
            {
                ColorFilterConfigControl colorUi = new ColorFilterConfigControl(cim, () => RunPipelineDebug(), this);
                colorUi.Dock = DockStyle.Fill;
                panelSpecificContainer.Controls.Add(colorUi);
            }
            else
            {
                Panel pSpecHeader = new Panel { Dock = DockStyle.Top, Height = 25, BackColor = Color_Light };
                pSpecHeader.Controls.Add(new Label { Text = "🔧 MODULE SPECIFIC PARAMETERS", ForeColor = Color_Text, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true });
                PropertyGrid pgSpec = new PropertyGrid { Dock = DockStyle.Fill, BackColor = Color_Bg, ViewBackColor = Color_Bg, ViewForeColor = Color_Text, LineColor = Color_Light, ToolbarVisible = false, HelpVisible = false, PropertySort = PropertySort.Categorized };
                pgSpec.PropertyValueChanged += (s, args) => RunPipelineDebug(); pgSpec.SelectedObject = selectedModule;
                panelSpecificContainer.Controls.Add(pgSpec); panelSpecificContainer.Controls.Add(pSpecHeader);
            }
            panelSpecificContainer.ResumeLayout(true); panelSpecificContainer.Refresh();
            _activeSubLoopIndex = 0; // Visszaállítjuk a lapozót az első elemre
            RunPipelineDebug();
        }
        private void FillCommonComboBoxes(int currentIdx, BaseInspectionModule mod)
        {
            // Kikapcsoljuk az eseménykezelést a feltöltés idejére
            comboInputSrc.SelectedIndexChanged -= OnCommonComboChanged;
            comboXSrc.SelectedIndexChanged -= OnCommonComboChanged;
            comboYSrc.SelectedIndexChanged -= OnCommonComboChanged;
            comboRotSrc.SelectedIndexChanged -= OnCommonComboChanged;
            comboInputSrc.Items.Clear(); comboXSrc.Items.Clear(); comboYSrc.Items.Clear(); comboRotSrc.Items.Clear();
            comboInputSrc.Items.Add("Default (Előző lépés)");
            comboXSrc.Items.Add("None (Fix koordináta)");
            comboYSrc.Items.Add("None (Fix koordináta)");
            comboRotSrc.Items.Add("None (Fix elfordulás)");
            // Csak azokat a modulokat listázzuk ki, amik a Pipeline láncban FIZIKAILEG EZELŐTT futottak le!
            for (int i = 0; i < currentIdx; i++)
            {
                if (PipelineSteps[i] is BaseInspectionModule prevMod)
                {
                    string key = prevMod.EffectiveFeatureName;
                    comboInputSrc.Items.Add(prevMod.Name);
                    comboXSrc.Items.Add(key);
                    comboYSrc.Items.Add(key);
                    comboRotSrc.Items.Add(key);
                    // Ha az előző modul egy sablonkereső vagy Sub-ROI, felkínáljuk a te Bond-welding al-ágait is!
                    if (prevMod is TemplateMatchModule || prevMod is SubRoiGeneratorModule || prevMod is EdgeBasedMatchModule)
                    {
                        comboXSrc.Items.Add($"{key}.EdgeBased");
                        comboXSrc.Items.Add($"{key}.EdgeBased.Edge1");
                        comboXSrc.Items.Add($"{key}.EdgeBased.Edge2");
                        comboYSrc.Items.Add($"{key}.EdgeBased");
                        comboYSrc.Items.Add($"{key}.EdgeBased.Edge1");
                        comboYSrc.Items.Add($"{key}.EdgeBased.Edge2");
                        comboRotSrc.Items.Add($"{key}.EdgeBased");
                    }
                }
            }
            // Kiválasztjuk a jelenleg elmentett értékeket
            SetComboValue(comboInputSrc, mod.InputStepLink);
            SetComboValue(comboXSrc, mod.ReferenceXSrc);
            SetComboValue(comboYSrc, mod.ReferenceYSrc);
            SetComboValue(comboRotSrc, mod.ReferenceRotSrc);
            txtCustomName.Text = mod.CustomFeatureName;
            comboInputSrc.SelectedIndexChanged += OnCommonComboChanged;
            comboXSrc.SelectedIndexChanged += OnCommonComboChanged;
            comboYSrc.SelectedIndexChanged += OnCommonComboChanged;
            comboRotSrc.SelectedIndexChanged += OnCommonComboChanged;
        }
        private void SetComboValue(ComboBox cb, string val)
        {
            if (cb.Items.Contains(val)) cb.SelectedItem = val;
            else if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }
        private void OnCommonComboChanged(object sender, EventArgs e)
        {
            BaseInspectionModule m = GetSelectedModule();
            if (m == null) return;
            m.InputStepLink = comboInputSrc.SelectedItem.ToString();
            m.ReferenceXSrc = comboXSrc.SelectedItem.ToString();
            m.ReferenceYSrc = comboYSrc.SelectedItem.ToString();
            m.ReferenceRotSrc = comboRotSrc.SelectedItem.ToString();
            RunPipelineDebug();
        }
        // --- JAVÍTÁS 5: UNIVERZÁLIS AL-PIPELINE LAPOZÓ RUTIN ---
        private void NavigateSubLoop(int direction)
        {
            // Ezt a háttérben futó kontextus subContext listájából frissítjük az újra-kirajzoláskor
            RunPipelineDebug(direction);
        }
        // ====================================================================
        // --- JAVÍTÁS: ADAPTÍV RUN MODELL TELJES CIKLUSIDŐ MÉRÉSSEL ---
        // ====================================================================
        public void RunPipelineDebug(int stepDirection = 0, bool runAll = false)
        {
            // Ha runAll=true, akkor a teljes listát futtatjuk, egyébként csak a kijelölt elemig
            int idx = runAll ? PipelineSteps.Count - 1 : listBoxPipeline.SelectedIndex;
            if (idx < 0) return;

            using (InspectionContext mainContext = new InspectionContext())
            {
                bool inSubPipelineLoop = false;

                // Végigfuttatjuk a láncot a meghatározott végpontig
                for (int i = 0; i <= idx; i++)
                {
                    IInspectionModule step = PipelineSteps[i];
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    if (step is SubPipelineEndModule)
                    {
                        inSubPipelineLoop = false;
                        step.Process(mainContext);
                        sw.Stop();
                        if (step is BaseInspectionModule bEnd)
                        {
                            bEnd.ProcessingTimeMs = sw.Elapsed.TotalMilliseconds;
                            bEnd.Status = ModuleStatus.Success;
                        }
                        continue;
                    }

                    if (inSubPipelineLoop && mainContext.SubContexts != null && mainContext.SubContexts.Count > 0)
                    {
                        foreach (var subContext in mainContext.SubContexts)
                        {
                            foreach (var feat in mainContext.Features) subContext.Features[feat.Key] = feat.Value;
                            step.Process(subContext);
                        }
                    }
                    else
                    {
                        step.Process(mainContext);
                    }

                    sw.Stop();

                    if (step is BaseInspectionModule baseMod && !(step is TemplateMatchModule))
                    {
                        baseMod.ProcessingTimeMs = sw.Elapsed.TotalMilliseconds;
                        baseMod.Status = ModuleStatus.Success;
                    }

                    if (step is TemplateMatchModule || step is SubRoiGeneratorModule || step is EdgeBasedMatchModule)
                    {
                        inSubPipelineLoop = true;
                    }

                    // Történelemmentés .Copy() alapon
                    if (mainContext.SubContexts != null && mainContext.SubContexts.Count > 0)
                    {
                        int activeIndex = Math.Min(_activeSubLoopIndex, mainContext.SubContexts.Count - 1);
                        var activeSub = mainContext.SubContexts[activeIndex];
                        if (activeSub.SourceImage != null && activeSub.SourceImage.Ptr != IntPtr.Zero)
                            mainContext.ColorHistory[step.Name] = activeSub.SourceImage.Copy();
                        if (activeSub.WorkingMask != null && activeSub.WorkingMask.Ptr != IntPtr.Zero)
                            mainContext.GrayHistory[step.Name] = activeSub.WorkingMask.Copy();
                    }
                    else
                    {
                        if (mainContext.SourceImage != null && mainContext.SourceImage.Ptr != IntPtr.Zero)
                            mainContext.ColorHistory[step.Name] = mainContext.SourceImage.Copy();
                        if (mainContext.WorkingMask != null && mainContext.WorkingMask.Ptr != IntPtr.Zero)
                            mainContext.GrayHistory[step.Name] = mainContext.WorkingMask.Copy();
                    }
                }

                // --- CIKLUSIDŐ ÖSSZEGZÉS ÉS KIÍRÁS A FELIRATRA ---
                double totalCycleTimeMs = 0;
                // Csak az aktuálisan lefutott lépések idejét adjuk össze a tiszta diagnosztikához
                for (int i = 0; i <= idx; i++)
                {
                    totalCycleTimeMs += PipelineSteps[i].ProcessingTimeMs;
                }

                // Megkeressük a "Pipeline Steps" feliratot hordozó Label-t (pl. lblPipelineTitle)
                // Ha nálad más a neve, írd át arra, ami az InitializeComponent-ben szerepel!
                if (lblPipelineTitle != null)
                {
                    lblPipelineTitle.Text = $"PIPELINE STEPS  ->  Total Time: {totalCycleTimeMs:F1} ms";

                    // Ha a ciklusidő kritikus (pl. több mint 150 ms), sárgával jelezzük az ergonomikus UI érdekében
                    lblPipelineTitle.ForeColor = totalCycleTimeMs > 1000 ? Color.Orange : Color_Cyan;
                }

                // --- AL-PIPELINE VEZÉRLŐ PANEL LAPOZÁSA ---
                if (mainContext.SubContexts != null && mainContext.SubContexts.Count > 1)
                {
                    panelSubLoopNavigator.Visible = true;
                    _activeSubLoopIndex = (_activeSubLoopIndex + stepDirection + mainContext.SubContexts.Count) % mainContext.SubContexts.Count;
                    lblSubCounter.Text = $"{_activeSubLoopIndex + 1} / {mainContext.SubContexts.Count}";
                }
                else
                {
                    panelSubLoopNavigator.Visible = false;
                    _activeSubLoopIndex = 0;
                }

                // --- LIVE VIEW KIJELZÉS ---
                IInspectionModule currentModule = PipelineSteps[idx];
                Bitmap oldBmp = imageBoxExMain.Image as Bitmap;
                imageBoxExMain.Image = null;

                if (_isSelectingTemplate || _isSelectingRoi)
                {
                    string firstStepName = PipelineSteps[0].Name;
                    if (mainContext.ColorHistory.Count > 0 && mainContext.ColorHistory.ContainsKey(firstStepName))
                    {
                        var firstImg = mainContext.ColorHistory[firstStepName];
                        if (firstImg != null && firstImg.Ptr != IntPtr.Zero)
                        {
                            using (Image<Bgr, byte> img = firstImg.Copy()) imageBoxExMain.Image = img.ToBitmap();
                        }
                    }
                    else if (mainContext.SourceImage != null && mainContext.SourceImage.Ptr != IntPtr.Zero)
                    {
                        imageBoxExMain.Image = mainContext.SourceImage.ToBitmap();
                    }
                }
                else if (currentModule is TemplateMatchModule || currentModule is EdgeBasedMatchModule)
                {
                    if (mainContext.SourceImage != null && mainContext.SourceImage.Ptr != IntPtr.Zero)
                        imageBoxExMain.Image = mainContext.SourceImage.ToBitmap();
                }
                else if (mainContext.SubContexts != null && mainContext.SubContexts.Count > 0)
                {
                    int targetIdx = Math.Min(_activeSubLoopIndex, mainContext.SubContexts.Count - 1);
                    var targetSub = mainContext.SubContexts[targetIdx];

                    if (currentModule is ColorInRangeModule && targetSub.SourceImage != null && targetSub.SourceImage.Ptr != IntPtr.Zero)
                        imageBoxExMain.Image = targetSub.SourceImage.ToBitmap();
                    else if (targetSub.WorkingMask != null && targetSub.WorkingMask.Ptr != IntPtr.Zero)
                        imageBoxExMain.Image = targetSub.WorkingMask.ToBitmap();
                }
                else
                {
                    if (!(currentModule is ImageLoadModule || currentModule is TemplateMatchModule || currentModule is ColorInRangeModule) && mainContext.WorkingMask != null && mainContext.WorkingMask.Ptr != IntPtr.Zero)
                        imageBoxExMain.Image = mainContext.WorkingMask.ToBitmap();
                    else if (mainContext.SourceImage != null && mainContext.SourceImage.Ptr != IntPtr.Zero)
                        imageBoxExMain.Image = mainContext.SourceImage.ToBitmap();
                }

                if (oldBmp != null) oldBmp.Dispose();

                _lastInspectionCache?.Dispose();

                // Létrehozunk egy új, tartós kontextust a betanító ablak számára
                _lastInspectionCache = new InspectionContext();
                if (mainContext.SourceImage != null)
                {
                    _lastInspectionCache.SourceImage = mainContext.SourceImage.Copy();
                }

                // Átmásoljuk a képtörténeteket (ColorHistory, GrayHistory), hogy a szűrt képek megmaradjanak
                foreach (var kvp in mainContext.ColorHistory)
                {
                    _lastInspectionCache.ColorHistory[kvp.Key] = kvp.Value.Copy();
                }
                foreach (var kvp in mainContext.GrayHistory)
                {
                    _lastInspectionCache.GrayHistory[kvp.Key] = kvp.Value.Copy();
                }
            }

            // Frissítjük a ListBox-ot, hogy a pipák/X-ek és a rész-ms idők azonnal megjelenjenek a sorokon!
            listBoxPipeline.Invalidate();
        }

        // Kis belső segédfüggvény a lezáró modul státuszának kényszerítésére
        private void bModRestoreStatus(BaseInspectionModule b)
        {
            b.Status = ModuleStatus.Success;
        }


        // --- SEGÉDFÜGGVÉNYEK A SZEKVENCIA-SZERKESZTŐHÖZ ---
        private BaseInspectionModule GetSelectedModule()
        {
            int idx = listBoxPipeline.SelectedIndex;
            if (idx >= 0 && idx < PipelineSteps.Count) return PipelineSteps[idx] as BaseInspectionModule;
            return null;
        }
        private void InitializeAddMenu()
        {
            menuAddModule = new ContextMenuStrip { BackColor = Color_Slate, ForeColor = Color_Text, ShowImageMargin = false };
            var catSpatial = new ToolStripMenuItem("1. Pixel-alapú Képjavítás") { BackColor = Color_Slate, ForeColor = Color_Cyan };
            catSpatial.DropDown.BackColor = Color_Slate; catSpatial.DropDown.ForeColor = Color_Text;
            var mHist = new ToolStripMenuItem("Histogram balance") { ForeColor = Color_Text }; mHist.Click += (s, e) => InsertModuleAtSelected(new HistogramEqualizationModule());
            var mGamma = new ToolStripMenuItem("Gamma Correction") { ForeColor = Color_Text }; mGamma.Click += (s, e) => InsertModuleAtSelected(new GammaCorrectionModule());
            var mThresh = new ToolStripMenuItem("Threshold") { ForeColor = Color_Text }; mThresh.Click += (s, e) => InsertModuleAtSelected(new SimpleThresholdModule());
            var mInRange = new ToolStripMenuItem("Color Filter") { ForeColor = Color_Text }; mInRange.Click += (s, e) => InsertModuleAtSelected(new ColorInRangeModule());
            catSpatial.DropDownItems.AddRange(new ToolStripItem[] { mHist, mGamma, mThresh, mInRange });
            var catBlur = new ToolStripMenuItem("2. Simító & Zajcsökkentő Szűrők") { BackColor = Color_Slate, ForeColor = Color_Cyan };
            catBlur.DropDown.BackColor = Color_Slate; catBlur.DropDown.ForeColor = Color_Text;
            var mBox = new ToolStripMenuItem("Box Filter") { ForeColor = Color_Text }; mBox.Click += (s, e) => InsertModuleAtSelected(new BoxFilterModule());
            var mGauss = new ToolStripMenuItem("Gaussian Blur") { ForeColor = Color_Text }; mGauss.Click += (s, e) => InsertModuleAtSelected(new GaussianBlurModule());
            var mMedian = new ToolStripMenuItem("Median Filter") { ForeColor = Color_Text }; mMedian.Click += (s, e) => InsertModuleAtSelected(new MedianFilterModule());
            var mBilat = new ToolStripMenuItem("Bilateral Filter") { ForeColor = Color_Text }; mBilat.Click += (s, e) => InsertModuleAtSelected(new BilateralFilterModule());
            catBlur.DropDownItems.AddRange(new ToolStripItem[] { mBox, mGauss, mMedian, mBilat });
            var catEdges = new ToolStripMenuItem("3. Élesítő & Élkereső Szűrők") { BackColor = Color_Slate, ForeColor = Color_Cyan };
            catEdges.DropDown.BackColor = Color_Slate; catEdges.DropDown.ForeColor = Color_Text;
            var mSobel = new ToolStripMenuItem("Sobel Gradiens") { ForeColor = Color_Text }; mSobel.Click += (s, e) => InsertModuleAtSelected(new SobelFilterModule());
            var mLaplace = new ToolStripMenuItem("Laplace") { ForeColor = Color_Text }; mLaplace.Click += (s, e) => InsertModuleAtSelected(new LaplacianFilterModule());
            var mCanny = new ToolStripMenuItem("Canny") { ForeColor = Color_Text }; mCanny.Click += (s, e) => InsertModuleAtSelected(new CannyEdgeModule());
            var mUnsharp = new ToolStripMenuItem("Unsharp Masking") { ForeColor = Color_Text }; mUnsharp.Click += (s, e) => InsertModuleAtSelected(new UnsharpMaskingModule());
            catEdges.DropDownItems.AddRange(new ToolStripItem[] { mSobel, mLaplace, mCanny, mUnsharp });
            var catMorph = new ToolStripMenuItem("4. Morfológiai Műveletek") { BackColor = Color_Slate, ForeColor = Color_Cyan };
            catMorph.DropDown.BackColor = Color_Slate; catMorph.DropDown.ForeColor = Color_Text;
            var mFusionOld = new ToolStripMenuItem("Otsu") { ForeColor = Color_Text }; mFusionOld.Click += (s, e) => InsertModuleAtSelected(new OtsuSuperFusionModule());
            var mMorphAdv = new ToolStripMenuItem("Open/Close/Erode") { ForeColor = Color_Text }; mMorphAdv.Click += (s, e) => InsertModuleAtSelected(new AdvancedMorphologyModule());
            catMorph.DropDownItems.AddRange(new ToolStripItem[] { mFusionOld, mMorphAdv });
            var catFreq = new ToolStripMenuItem("5. Speciális Binarizálók & Keresők") { BackColor = Color_Slate, ForeColor = Color_Cyan };
            catFreq.DropDown.BackColor = Color_Slate; catFreq.DropDown.ForeColor = Color_Text;
            var mAdaptive = new ToolStripMenuItem("Adaptive Threshold") { ForeColor = Color_Text }; mAdaptive.Click += (s, e) => InsertModuleAtSelected(new AdaptiveThresholdModule());
            var mSubRoi = new ToolStripMenuItem("Sub-ROI Generator") { ForeColor = Color_Text }; mSubRoi.Click += (s, e) => InsertModuleAtSelected(new SubRoiGeneratorModule());
            var mMatch = new ToolStripMenuItem("Template Matching") { ForeColor = Color_Text }; mMatch.Click += (s, e) => InsertModuleAtSelected(new TemplateMatchModule());
            var mEdgeMatch = new ToolStripMenuItem("Edge-Based Template Matching") { ForeColor = Color_Text }; mEdgeMatch.Click += (s, e) => InsertModuleAtSelected(new EdgeBasedMatchModule());

            catFreq.DropDownItems.AddRange(new ToolStripItem[] { mAdaptive,  mSubRoi, mMatch, mEdgeMatch });
            menuAddModule.Items.AddRange(new ToolStripItem[] { catSpatial, catBlur, catEdges, catMorph, catFreq });
        }
        private void InsertModuleAtSelected(IInspectionModule newModule)
        {
            int currentIdx = listBoxPipeline.SelectedIndex;

            if (currentIdx >= 0 && currentIdx < PipelineSteps.Count)
                PipelineSteps.Insert(currentIdx + 1, newModule);
            else
                PipelineSteps.Add(newModule);

            // --- JAVÍTÁS: AUTOMATA SUB-PIPELINE LEZÁRÓ BESZÚRÁS ---
            if (newModule is TemplateMatchModule || newModule is SubRoiGeneratorModule)
            {
                // Ha al-pipeline nyitó modult szúrunk be, automatikusan mögé rakjuk az End lezárót!
                PipelineSteps.Insert(listBoxPipeline.SelectedIndex + 2, new SubPipelineEndModule());
            }

            RefreshUI();
            listBoxPipeline.SelectedIndex = Math.Min(PipelineSteps.Count - 1, currentIdx + 1);
        }
        private void BtnDelete_Click(object sender, EventArgs e)
        {
            int currentIdx = listBoxPipeline.SelectedIndex;
            if (currentIdx <= 0 || currentIdx >= PipelineSteps.Count) return; // Első lépés védett
            PipelineSteps.RemoveAt(currentIdx); RefreshUI();
            listBoxPipeline.SelectedIndex = Math.Max(0, currentIdx - 1);
        }
        private void CreateDefaultPipeline()
        {
            PipelineSteps.Clear();
            PipelineSteps.Add(new ImageLoadModule());
        }
        public void RefreshUI()
        {
            ReceptRegistry.ActiveSteps = this.PipelineSteps;
            int savedIndex = listBoxPipeline.SelectedIndex;
            listBoxPipeline.Items.Clear();
            for (int i = 0; i < PipelineSteps.Count; i++)
            {
                if (PipelineSteps[i] is BaseInspectionModule baseMod) baseMod.StepIndex = i + 1;
                listBoxPipeline.Items.Add(PipelineSteps[i].Name);
            }
            if (savedIndex >= 0 && savedIndex < listBoxPipeline.Items.Count) listBoxPipeline.SelectedIndex = savedIndex;
            else if (listBoxPipeline.Items.Count > 0) listBoxPipeline.SelectedIndex = 0;
        }
        private void ListBoxPipeline_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || PipelineSteps == null || e.Index >= PipelineSteps.Count) return;
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            IInspectionModule step = PipelineSteps[e.Index];

            // Alapértelmezett háttérszínek az ipari sötét dizájnodhoz
            Color rowBg = isSelected ? Color_Cyan : Color_Bg;
            Color rowFg = isSelected ? Color.Black : Color_Text;

            using (SolidBrush bg = new SolidBrush(rowBg))
            using (SolidBrush fg = new SolidBrush(rowFg))
            {
                // 1. Háttér kifestése
                e.Graphics.FillRectangle(bg, e.Bounds);

                // ====================================================================
                // --- ÚJ: SUB-PIPELINE KAPOCS (SCOPE VIZUALIZÁCIÓ) RAJZOLÁSA ---
                // ====================================================================
                // Megvizsgáljuk, hogy az aktuális sor vagy a környezete al-pipeline-ban van-e
                bool isLoopStart = step is TemplateMatchModule || step is SubRoiGeneratorModule;
                bool isLoopEnd = step is SubPipelineEndModule;

                // Megnézzük, hogy a hurok közepén vagyunk-e (keressük a határokat a múltban és jövőben)
                bool isInMiddle = false;
                if (!isLoopStart && !isLoopEnd)
                {
                    bool hasStartBefore = false;
                    bool hasEndAfter = false;

                    // Van-e hurokindító lépés előttünk a listában?
                    for (int k = e.Index - 1; k >= 0; k--)
                    {
                        if (PipelineSteps[k] is TemplateMatchModule || PipelineSteps[k] is SubRoiGeneratorModule) { hasStartBefore = true; break; }
                        if (PipelineSteps[k] is SubPipelineEndModule) break; // Ha útközben véget ért egy másik hurok, megállunk
                    }

                    // Van-e lezáró lépés utánunk a listában?
                    for (int k = e.Index + 1; k < PipelineSteps.Count; k++)
                    {
                        if (PipelineSteps[k] is SubPipelineEndModule) { hasEndAfter = true; break; }
                        if (PipelineSteps[k] is TemplateMatchModule || PipelineSteps[k] is SubRoiGeneratorModule) break; // Újabb hurok nyílik, megállunk
                    }

                    isInMiddle = hasStartBefore && hasEndAfter;
                }

                // Ha a lépés bármilyen módon érintett az al-pipeline-ban, felrajzoljuk a kapcsot
                if (isLoopStart || isLoopEnd || isInMiddle)
                {
                    // A kapocs színe kijelölve fekete (a kontrasztért), egyébként a te szép ciánkéked halványított változata vagy maga a cián
                    Color braceColor = isSelected ? Color.Black : Color.FromArgb(0, 180, 220);

                    using (Pen bracePen = new Pen(braceColor, 2))
                    {
                        int bracketX = e.Bounds.X + 6; // A kapocs fix X pozíciója a sor bal szélén

                        if (isLoopStart)
                        {
                            // HUROK KEZDETE: Egy derékszögű pipa lefelé (Behorgonyzás a nyitó modulnál)
                            e.Graphics.DrawLine(bracePen, bracketX, e.Bounds.Y + (e.Bounds.Height / 2), bracketX + 8, e.Bounds.Y + (e.Bounds.Height / 2));
                            e.Graphics.DrawLine(bracePen, bracketX, e.Bounds.Y + (e.Bounds.Height / 2), bracketX, e.Bounds.Bottom);
                        }
                        else if (isLoopEnd)
                        {
                            // HUROK VÉGE: Egy derékszögű pipa felfelé (Bezárás az End modulnál)
                            e.Graphics.DrawLine(bracePen, bracketX, e.Bounds.Y, bracketX, e.Bounds.Y + (e.Bounds.Height / 2));
                            e.Graphics.DrawLine(bracePen, bracketX, e.Bounds.Y + (e.Bounds.Height / 2), bracketX + 8, e.Bounds.Y + (e.Bounds.Height / 2));
                        }
                        else if (isInMiddle)
                        {
                            // HUROK KÖZEPE: Egy egyenes, folytonos függőleges vonal, ami átível a belső modulon
                            e.Graphics.DrawLine(bracePen, bracketX, e.Bounds.Y, bracketX, e.Bounds.Bottom);
                        }
                    }
                }

                // ====================================================================
                // --- 2. MODUL STÁTUSZ IKONOK ÉS SZÖVEGEK RAJZOLÁSA (ELTOLT X-EL) ---
                // ====================================================================
                // Mivel a kapocs elfoglalt egy kis helyet, a szövegeket és ikonokat 10 pixellel jobbra toljuk (+10 offset)
                int contentOffsetX = (isLoopStart || isLoopEnd || isInMiddle) ? 12 : 0;

                string statusIcon = "⚪";
                Color iconColor = Color.Gray;

                if (step.Status == ModuleStatus.Success) { statusIcon = "✔"; iconColor = Color.LightGreen; }
                else if (step.Status == ModuleStatus.Failed) { statusIcon = "❌"; iconColor = Color.Tomato; }
                else if (step.Status == ModuleStatus.Warning) { statusIcon = "⚠️"; iconColor = Color.Orange; }

                if (isSelected) iconColor = Color.Black;

                // Státusz ikon kirajzolása
                using (SolidBrush ib = new SolidBrush(iconColor))
                {
                    e.Graphics.DrawString(statusIcon, new Font("Segoe UI", 9, FontStyle.Bold), ib, e.Bounds.X + 16 + contentOffsetX, e.Bounds.Y + 4);
                }

                // Modul sorszám és név kirajzolása
                e.Graphics.DrawString(step.Name, e.Font, fg, e.Bounds.X + 36 + contentOffsetX, e.Bounds.Y + 4);

                // Futási idő (ms) kirajzolása a jobb szélre
                if (step.Status != ModuleStatus.NotExecuted)
                {
                    string timeText = $"{step.ProcessingTimeMs:F1} ms";
                    using (SolidBrush tb = new SolidBrush(isSelected ? Color.Black : Color.Gray))
                    {
                        e.Graphics.DrawString(timeText, new Font("Segoe UI", 8, FontStyle.Italic), tb, e.Bounds.Right - 65, e.Bounds.Y + 5);
                    }
                }
            }
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog { Filter = "Vision Recipe (.vrec)|.vrec", InitialDirectory = @"C:\Test" })
            {
                if (sfd.ShowDialog() == DialogResult.OK) VisionRecipe.SaveToFile(sfd.FileName, PipelineSteps);
            }
        }
        public void StartColorPicking()
        {
            if (imageBoxExMain.Image == null) return;

            // --- KRITIKUS JAVÍTÁS: AZ ÁLLAPOTGÉP AUTOMATIKUS AKTIVÁLÁSA ---
            _isColorPicking = true;
            _isSelectingTemplate = false;
            _isSelectingRoi = false;

            // Átváltunk tiszta célkereszt kurzorra, és letiltjuk a kijelölő négyzetet, mert most csak kattintást várunk
            imageBoxExMain.SelectionMode = Cyotek.Windows.Forms.ImageBoxSelectionMode.None;
            imageBoxExMain.AutoPan = false; // Kikapcsoljuk a Pan-t, hogy ne mozduljon el a kép kattintáskor
            imageBoxExMain.Cursor = Cursors.Cross;
        }



        // ====================================================================
        // --- 3. FÁZIS: INTERAKTÍV SABLON-KIJELÖLÉS INDÍTÓ RUTIN ---
        // ====================================================================
        public void StartTemplateSelection()
        {
            if (imageBoxExMain.Image == null) return;

            _isSelectingTemplate = true;
            _isSelectingRoi = false;

            // Kényszerítjük a Cyotek motort a négyszöges szabad kijelölésre
            imageBoxExMain.SelectionMode = ImageBoxSelectionMode.Rectangle;
            imageBoxExMain.AutoPan = false; // Kikapcsoljuk a Pan-t, hogy rajzolás közben ne másszon el a kép
            imageBoxExMain.Cursor = Cursors.Cross; // Precíz célkereszt kurzor

            int idx = listBoxPipeline.SelectedIndex;
            if (idx >= 0) listBoxPipeline.Items[idx] = "🎯 DRAG MOUSE TO TEACH TEMPLATE...";
        }

        // ====================================================================
        // --- 3. FÁZIS: CYOTEK EGÉR-KIJELÖLÉS VÉGE (SELECTION CHANGED) ---
        // ====================================================================
        private void ImageBoxExMain_SelectionChanged(object sender, EventArgs e)
        {
            Console.WriteLine("SelectionChanged");
            if (imageBoxExMain.SelectionRegion == RectangleF.Empty) return;

            int idx = listBoxPipeline.SelectedIndex;
            if (idx >= 0 && PipelineSteps[idx] is BaseInspectionModule currentMod)
            {
                RectangleF selF = imageBoxExMain.SelectionRegion;
                Rectangle sourceRect = new Rectangle((int)selF.X, (int)selF.Y, (int)selF.Width, (int)selF.Height);

                if (sourceRect.Width > 5 && sourceRect.Height > 5)
                {
                    // --- A) HA AZ ÚJ ÉLALAPÚ MOTOR SABLONJÁT TANÍTJUK BE ---
                    if (_isSelectingTemplate && currentMod is EdgeBasedMatchModule emm)
                    {
                        // ====================================================================
                        // --- JAVÍTÁS: A TARTÓS OSZTÁLYSZINTŰ GYORSÍTÓTÁR MEGHÍVÁSA ---
                        // ====================================================================
                        Image<Bgr, byte> processedPipelineImage = null;

                        if (_lastInspectionCache != null)
                        {
                            // Kérjük az emm előtti modul (pl. ColorInRange) által elmentett képet a cache-ből
                            processedPipelineImage = _lastInspectionCache.GetInputColorImage(emm.InputStepLink);
                        }

                        if (processedPipelineImage != null)
                        {
                            using (Bitmap processedBmp = processedPipelineImage.ToBitmap())
                            {
                                Bitmap croppedTemplate = CropImage(processedBmp, sourceRect);
                                if (croppedTemplate != null)
                                {
                                    foreach (Control ctrl in panelSpecificContainer.Controls)
                                    {
                                        if (ctrl is EdgeBasedMatchConfigControl edgeUi)
                                        {
                                            edgeUi.AddNewTemplateFromBmp(croppedTemplate);
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Fallback: Ha még nem futott a pipeline, a nyers főképet használjuk
                            using (Bitmap fullBmp = new Bitmap(imageBoxExMain.Image))
                            {
                                Bitmap croppedTemplate = CropImage(fullBmp, sourceRect);
                                if (croppedTemplate != null)
                                {
                                    foreach (Control ctrl in panelSpecificContainer.Controls)
                                    {
                                        if (ctrl is EdgeBasedMatchConfigControl edgeUi)
                                        {
                                            edgeUi.AddNewTemplateFromBmp(croppedTemplate);
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }                    // --- B) HA A RÉGI HAGYOMÁNYOS SABLONT TANÍTJUK BE ---
                    else if (_isSelectingTemplate && currentMod is TemplateMatchModule tmm)
                    {
                        using (Bitmap fullBmp = new Bitmap(imageBoxExMain.Image))
                        {
                            Bitmap croppedTemplate = CropImage(fullBmp, sourceRect);
                            if (croppedTemplate != null)
                            {
                                foreach (Control ctrl in panelSpecificContainer.Controls)
                                {
                                    if (ctrl is TemplateMatchConfigControl matchUi)
                                    {
                                        matchUi.AddNewTemplateFromBmp(croppedTemplate);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    // --- C) HA A KERESÉSI MÉRŐHÁLÓT (ROI) ÁLLÍTJUK BE ---
                    else if (_isSelectingRoi)
                    {
                        if (currentMod is TemplateMatchModule oldTmm)
                        {
                            oldTmm.ROI = sourceRect;
                            MessageBox.Show($"Keresési ROI rögzítve!\nX: {oldTmm.ROI.X}, Y: {oldTmm.ROI.Y}", "ROI Beállítva", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else if (currentMod is EdgeBasedMatchModule edgeEmm)
                        {
                            edgeEmm.ROI = sourceRect;
                            MessageBox.Show($"Élalapú Keresési ROI rögzítve!\nX: {edgeEmm.ROI.X}, Y: {edgeEmm.ROI.Y}", "ROI Beállítva", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                }
            }

            // RESETELJÜK A CYOTEK INTERAKTÍV FELÜLETÉT NORMAL MÓDBA
            _isSelectingTemplate = false;
            _isSelectingRoi = false;

            imageBoxExMain.SelectionRegion = RectangleF.Empty;
            imageBoxExMain.SelectionMode = ImageBoxSelectionMode.None;
            imageBoxExMain.AutoPan = true;
            imageBoxExMain.Cursor = Cursors.Default;

            RefreshUI();
        }

        private void ImageBoxExMain_MouseClick(object sender, MouseEventArgs e)
        {
            // Ellenőrizzük a golyóálló belső flaget és a kép jelenlétét
            Console.WriteLine("Imagebox_MouseClick " + _isColorPicking);
            if (_isColorPicking && imageBoxExMain.Image != null)
            {
                // Átszámoljuk az egér koordinátáját valós kép-koordinátává a Cyotek konverterével
                Point imgPt = imageBoxExMain.PointToImage(e.Location);

                using (Bitmap bmp = new Bitmap(imageBoxExMain.Image))
                {
                    // Biztonsági guardrail: Csak akkor mintavételezünk, ha a kép fizikai határain belül kattintottunk
                    if (imgPt.X >= 0 && imgPt.X < bmp.Width && imgPt.Y >= 0 && imgPt.Y < bmp.Height)
                    {
                        Color pixelColor = bmp.GetPixel(imgPt.X, imgPt.Y);

                        // Átkonvertáljuk az unmanaged Emgu CV struktúrára (B, G, R sorrendben!)
                        Bgr pickedBgr = new Bgr(pixelColor.B, pixelColor.G, pixelColor.R);

                        // --- ARCHITEKTÚRAI FRISSÍTÉS: INTERFÉSZ-ALAPÚ UNIVERZÁLIS ÁTADÁS ---
                        // Megkeressük a jobb alsó panelen lévő aktív vezérlőt
                        foreach (Control ctrl in panelSpecificContainer.Controls)
                        {
                            // Ha a kontrol megvalósítja az IColorPickable interfészt (függetlenül a típusától!)
                            if (ctrl is IColorPickable pickableUi)
                            {
                                // Átadjuk az univerzális színt és a pontos pixel helyét
                                pickableUi.HandlePickedColor(pickedBgr, imgPt);
                                break;
                            }
                        }
                    }
                }

                // Lezárjuk a mintavételi állapotot, visszaállítjuk a kurzort és frissítünk
                _isColorPicking = false;
                imageBoxExMain.Cursor = Cursors.Default;
                RefreshUI();
            }
        }


        static public Bitmap CropImage(Bitmap source, Rectangle section)
        {
            // An empty bitmap which will hold the cropped image
            //          if (section.Width > 1 && section.Height > 1)
            //           {
            try
            {

                Bitmap bmp = new Bitmap(section.Width, section.Height);

                Graphics g = Graphics.FromImage(bmp);

                // Draw the given area (section) of the source image
                // at location 0,0 on the empty bitmap (bmp)
                object bmplocker = new object();
                lock (bmplocker)
                {
                    g.DrawImage(source, 0, 0, section, GraphicsUnit.Pixel);
                }
                g.Dispose();
                return bmp;
            }
            catch
            {
                return null;
            }

            /*            }
                        else
                        {
                            return null;
                        }
            */
        }


        
    }
}
