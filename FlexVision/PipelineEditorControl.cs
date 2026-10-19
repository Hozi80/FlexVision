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

        // KÉTOSZTATÚ PARAMÉTER JELENTŐK
        private PropertyGrid propertyGridCommon;
        private Panel panelSpecificContainer;

        private ImageBoxEx imageBoxExMain;
        private SplitContainer splitLeft;
        private SplitContainer splitRight;
        private SplitContainer splitParamsContainer;

        private Panel panelToolbar;
        private Button btnNew, btnSave, btnRunAll;
        private Button btnAdd, btnDelete;

        // ÚJ: Felugró menü a modulok kiválasztásához beszúráskor
        private ContextMenuStrip menuAddModule;

        private bool _isSelectingTemplate = false;
        private bool _isSelectingRoi = false;

        public List<IInspectionModule> PipelineSteps { get; private set; } = new List<IInspectionModule>();

        public PipelineEditorControl()
        {
            this.BackColor = Color_Bg;
            this.Dock = DockStyle.Fill;
            InitializeComponent();
            InitializeAddMenu(); // Menü felépítése
            CreateDefaultPipeline(); // Kezdőállapot: Csak Source Capture
        }

        private void InitializeComponent()
        {
            // HASÁBOK STRUKTÚRÁJA
            splitLeft = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 270, BackColor = Color_Bg, FixedPanel = FixedPanel.Panel1 };
            splitRight = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 600, BackColor = Color_Bg, FixedPanel = FixedPanel.Panel2 };

            this.Controls.Add(splitLeft);
            splitLeft.Panel2.Controls.Add(splitRight);

            // Dinamikus méretezés és kényszerített minimumok
            this.Resize += (s, e) => {
                if (this.Width > 650)
                {
                    splitLeft.Panel1MinSize = 270;
                    splitRight.Panel2MinSize = 350;
                    int targetDistance = splitLeft.Panel2.Width - 350;
                    if (targetDistance > splitRight.Panel1MinSize)
                    {
                        splitRight.SplitterDistance = targetDistance;
                    }
                }
            };

            // BAL OLDALI BAR GOMBOK
            panelToolbar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color_Slate };

            btnNew = new Button { Text = "📄 NEW", Location = new Point(6, 10), Width = 83, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color_Cyan, BackColor = Color_Light, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnNew.FlatAppearance.BorderSize = 0;
            // JAVÍTÁS 1: A NEW gomb most már teljesen letakarítja a láncot és CSAK a Source Image Capture marad!
            btnNew.Click += (s, e) => { CreateDefaultPipeline(); RefreshUI(); };

            btnSave = new Button { Text = "💾 SAVE", Location = new Point(93, 10), Width = 83, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color_Text, BackColor = Color_Light, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += OnSaveClick;

            btnRunAll = new Button { Text = "▶ RUN", Location = new Point(180, 10), Width = 84, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color.LightGreen, BackColor = Color_Light, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnRunAll.FlatAppearance.BorderSize = 0;
            btnRunAll.Click += (s, e) => { if (listBoxPipeline.Items.Count > 0) listBoxPipeline.SelectedIndex = listBoxPipeline.Items.Count - 1; };

            panelToolbar.Controls.Add(btnNew); panelToolbar.Controls.Add(btnSave); panelToolbar.Controls.Add(btnRunAll);

            // BAL OLDALI LISTA FOLYAMAT
            Panel pListHeader = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color_Light };
            pListHeader.Controls.Add(new Label { Text = "PIPELINE STEPS", ForeColor = Color_Text, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 8) });

            listBoxPipeline = new ListBox { Dock = DockStyle.Fill, BackColor = Color_Bg, ForeColor = Color_Text, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10), ItemHeight = 24, DrawMode = DrawMode.OwnerDrawFixed };
            listBoxPipeline.DrawItem += ListBoxPipeline_DrawItem;
            listBoxPipeline.SelectedIndexChanged += ListBoxPipeline_SelectedIndexChanged;

            // ALSÓ FUNKCIÓGOMBOK
            Panel pListButtons = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Color_Slate };
            btnAdd = new Button { Text = "[ + ADD STEP ]", Width = 125, Height = 28, Location = new Point(6, 6), FlatStyle = FlatStyle.Flat, BackColor = Color_Light, ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };
            btnDelete = new Button { Text = "[ - REMOVE ]", Width = 125, Height = 28, Location = new Point(135, 6), FlatStyle = FlatStyle.Flat, BackColor = Color_Light, ForeColor = Color.FromArgb(255, 120, 10), Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand };

            // JAVÍTÁS 2: Az ADD gombra kattintva feldobjuk a választható modulok listáját
            btnAdd.Click += (s, e) => { menuAddModule.Show(btnAdd, new Point(0, btnAdd.Height)); };

            // JAVÍTÁS 4: A REMOVE gomb törli az éppen kijelölt lépést a láncból (biztonsági guardraillel)
            btnDelete.Click += BtnDelete_Click;

            pListButtons.Controls.Add(btnAdd); pListButtons.Controls.Add(btnDelete);

            splitLeft.Panel1.Controls.Add(listBoxPipeline);
            splitLeft.Panel1.Controls.Add(pListHeader);
            splitLeft.Panel1.Controls.Add(panelToolbar);
            splitLeft.Panel1.Controls.Add(pListButtons);

            // KÖZÉPSŐ NAGY KÉP (TE IMAGEBOXEX VEZÉRLŐD A SÖTÉTÍTETT GRID-DEL)
            Panel pImgHeader = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color_Light };
            pImgHeader.Controls.Add(new Label { Text = "LIVE VIEW DEBUG", ForeColor = Color_Text, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 8) });

            imageBoxExMain = new ImageBoxEx { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 15), Name = "imageBoxExMain" };
            imageBoxExMain.GridColor = Color.FromArgb(55, 55, 55);
            imageBoxExMain.GridColorAlternate = Color.FromArgb(45, 45, 45);
            imageBoxExMain.Selected += ImageBoxExMain_SelectionChanged;

            splitRight.Panel1.Controls.Add(imageBoxExMain);
            splitRight.Panel1.Controls.Add(pImgHeader);

            // JOBB HASÁB: KÉTOSZTATÚ PARAMÉTER PANEL
            splitParamsContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 160, BackColor = Color_Bg, FixedPanel = FixedPanel.Panel1 };
            splitRight.Panel2.Controls.Add(splitParamsContainer);

            Panel pCommonHeader = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Color_Light };
            pCommonHeader.Controls.Add(new Label { Text = "⚙️ COMMON CONFIGURATION (BEMENET / OFFSET)", ForeColor = Color_Cyan, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Location = new Point(10, 6), AutoSize = true });
            propertyGridCommon = new PropertyGrid { Dock = DockStyle.Fill, BackColor = Color_Bg, ViewBackColor = Color_Bg, ViewForeColor = Color_Text, LineColor = Color_Light, ToolbarVisible = false, HelpVisible = false, PropertySort = PropertySort.Categorized };
            propertyGridCommon.PropertyValueChanged += (s, args) => RunPipelineDebug();
            splitParamsContainer.Panel1.Controls.Add(propertyGridCommon);
            splitParamsContainer.Panel1.Controls.Add(pCommonHeader);

            panelSpecificContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color_Bg };
            splitParamsContainer.Panel2.Controls.Add(panelSpecificContainer);
        }

        private void InitializeAddMenu()
        {
            menuAddModule = new ContextMenuStrip { BackColor = Color_Slate, ForeColor = Color_Text, ShowImageMargin = false };

            // --- KATEGÓRIA 1: TÉRBELI KÉPJAVÍTÁS (PIXEL-ALAPÚ) ---
            var catSpatial = new ToolStripMenuItem("1. Pixel-alapú Képjavítás") { BackColor = Color_Slate, ForeColor = Color_Cyan };
            catSpatial.DropDown.BackColor = Color_Slate; catSpatial.DropDown.ForeColor = Color_Text;

            var mHist = new ToolStripMenuItem("Hisztogram Kiegyenlítés") { ForeColor = Color_Text }; mHist.Click += (s, e) => InsertModuleAtSelected(new HistogramEqualizationModule());
            var mGamma = new ToolStripMenuItem("Gamma Korrekció") { ForeColor = Color_Text }; mGamma.Click += (s, e) => InsertModuleAtSelected(new GammaCorrectionModule());
            var mThresh = new ToolStripMenuItem("Fix Küszöbölés (Threshold)") { ForeColor = Color_Text }; mThresh.Click += (s, e) => InsertModuleAtSelected(new SimpleThresholdModule());

            catSpatial.DropDownItems.AddRange(new ToolStripItem[] { mHist, mGamma, mThresh });

            // --- KATEGÓRIA 2: SIMÍTÓ ÉS ZAJCSÖKKENTŐ SZŰRŐK ---
            var catBlur = new ToolStripMenuItem("2. Simító & Zajcsökkentő Szűrők") { BackColor = Color_Slate, ForeColor = Color_Cyan };
            catBlur.DropDown.BackColor = Color_Slate; catBlur.DropDown.ForeColor = Color_Text;

            var mBox = new ToolStripMenuItem("Box Filter (Átlagoló)") { ForeColor = Color_Text }; mBox.Click += (s, e) => InsertModuleAtSelected(new BoxFilterModule());
            var mGauss = new ToolStripMenuItem("Gaussian Blur (Haranggörbe)") { ForeColor = Color_Text }; mGauss.Click += (s, e) => InsertModuleAtSelected(new GaussianBlurModule());
            var mMedian = new ToolStripMenuItem("Median Filter (Só-bors szűrő)") { ForeColor = Color_Text }; mMedian.Click += (s, e) => InsertModuleAtSelected(new MedianFilterModule());
            var mBilat = new ToolStripMenuItem("Bilateral Filter (Élmegőrző)") { ForeColor = Color_Text }; mBilat.Click += (s, e) => InsertModuleAtSelected(new BilateralFilterModule());

            catBlur.DropDownItems.AddRange(new ToolStripItem[] { mBox, mGauss, mMedian, mBilat });

            // --- KATEGÓRIA 3: ÉLESÍTŐ ÉS ÉLKERESŐK ---
            var catEdges = new ToolStripMenuItem("3. Élesítő & Élkereső Szűrők") { BackColor = Color_Slate, ForeColor = Color_Cyan };
            catEdges.DropDown.BackColor = Color_Slate; catEdges.DropDown.ForeColor = Color_Text;

            var mSobel = new ToolStripMenuItem("Sobel Gradiens élkereső") { ForeColor = Color_Text }; mSobel.Click += (s, e) => InsertModuleAtSelected(new SobelFilterModule());
            var mLaplace = new ToolStripMenuItem("Laplace másodrendű élkereső") { ForeColor = Color_Text }; mLaplace.Click += (s, e) => InsertModuleAtSelected(new LaplacianFilterModule());
            var mCanny = new ToolStripMenuItem("Canny komplex élkereső") { ForeColor = Color_Text }; mCanny.Click += (s, e) => InsertModuleAtSelected(new CannyEdgeModule());
            var mUnsharp = new ToolStripMenuItem("Unsharp Masking (Élesítés)") { ForeColor = Color_Text }; mUnsharp.Click += (s, e) => InsertModuleAtSelected(new UnsharpMaskingModule());

            catEdges.DropDownItems.AddRange(new ToolStripItem[] { mSobel, mLaplace, mCanny, mUnsharp });

            // --- KATEGÓRIA 4: MORFOLÓGIA ---
            var catMorph = new ToolStripMenuItem("4. Morfológiai Műveletek") { BackColor = Color_Slate, ForeColor = Color_Cyan };
            catMorph.DropDown.BackColor = Color_Slate; catMorph.DropDown.ForeColor = Color_Text;

            var mFusionOld = new ToolStripMenuItem("Otsu Super Fusion (Hegesztés célszűrő)") { ForeColor = Color_Text }; mFusionOld.Click += (s, e) => InsertModuleAtSelected(new OtsuSuperFusionModule());
            var mMorphAdv = new ToolStripMenuItem("Alakzat-módosítás (Open/Close/Erode)") { ForeColor = Color_Text }; mMorphAdv.Click += (s, e) => InsertModuleAtSelected(new AdvancedMorphologyModule());

            catMorph.DropDownItems.AddRange(new ToolStripItem[] { mFusionOld, mMorphAdv });

            // --- KATEGÓRIA 5: FREKVENCIA ÉS DETEKTÁLÁS ---
            var catFreq = new ToolStripMenuItem("5. Frekvenciatartomány (FFT)") { BackColor = Color_Slate, ForeColor = Color_Cyan };
            catFreq.DropDown.BackColor = Color_Slate; catFreq.DropDown.ForeColor = Color_Text;

   //         var mFourier = new ToolStripMenuItem("Fourier interferencia csíkszűrő") { ForeColor = Color_Text }; mFourier.Click += (s, e) => InsertModuleAtSelected(new FourierFilterModule());
            var mMatch = new ToolStripMenuItem("Template Matching (Multi-Sablon)") { ForeColor = Color_Text }; mMatch.Click += (s, e) => InsertModuleAtSelected(new TemplateMatchModule());

            catFreq.DropDownItems.AddRange(new ToolStripItem[] {  mMatch });

            // Fő akciósáv összeállítása
            menuAddModule.Items.AddRange(new ToolStripItem[] { catSpatial, catBlur, catEdges, catMorph, catFreq });
        }
        // --- JAVÍTÁS 3: BESZÚRÁS PONTOSAN A KIJELÖLT ELEM ALÁ ---
        private void InsertModuleAtSelected(IInspectionModule newModule)
        {
            int currentIdx = listBoxPipeline.SelectedIndex;

            // Ha van kijelölt elem, beszúrjuk ALÁ, különben a lista legvégére fűzzük
            if (currentIdx >= 0 && currentIdx < PipelineSteps.Count)
            {
                PipelineSteps.Insert(currentIdx + 1, newModule);
                RefreshUI();
                listBoxPipeline.SelectedIndex = currentIdx + 1; // Azonnal az új lépésre ugrunk
            }
            else
            {
                PipelineSteps.Add(newModule);
                RefreshUI();
                listBoxPipeline.SelectedIndex = PipelineSteps.Count - 1;
            }
        }
        // --- JAVÍTÁS 4: ELEM ELTÁVOLÍTÁSA VÉDELMI FÉKKEL ---
        private void BtnDelete_Click(object sender, EventArgs e)
        {
            int currentIdx = listBoxPipeline.SelectedIndex;
            // Biztonsági fék 1: Ne lehessen törölni, ha semmi sincs kijelölve
            if (currentIdx < 0 || currentIdx >= PipelineSteps.Count) return;
            // Biztonsági fék 2: Az első Image Capture lépést iparilag TILTOTT törölni, mert az a lánc forrása!
            if (currentIdx == 0)
            {
                MessageBox.Show("A kiinduló 'Source Image Capture' lépés nem távolítható el, mert ez adja a képet a teljes Pipeline számára!",
"Művelet Letiltva", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // Törlés végrehajtása
            PipelineSteps.RemoveAt(currentIdx);
            RefreshUI();
            // A kijelölést az előző elemre pozicionáljuk vissza
            listBoxPipeline.SelectedIndex = Math.Max(0, currentIdx - 1);
        }
        // --- JAVÍTÁS 1: ALAPÉRTELMEZETT TISZTA INDÍTÁS (CSAK CAPTURE) ---
        private void CreateDefaultPipeline()
        {
            PipelineSteps.Clear();
            PipelineSteps.Add(new ImageLoadModule()); // Kizárólag a kiinduló képbeolvasó marad
        }
   
        /*
        private void ListBoxPipeline_SelectedIndexChanged(object sender, EventArgs e)
        {
            int idx = listBoxPipeline.SelectedIndex;
            if (idx < 0) return;
            IInspectionModule selectedModule = PipelineSteps[idx];
            splitRight.Panel2.Controls.Clear();
            propertyGridCommon.SelectedObject = selectedModule;
            panelSpecificContainer.Controls.Clear();
            if (selectedModule is TemplateMatchModule tmm)
            {
                TemplateMatchConfigControl matchUi = new TemplateMatchConfigControl(tmm, () => RunPipelineDebug(), this);
                panelSpecificContainer.Controls.Add(matchUi);
            }
            else
            {
                Panel pSpecHeader = new Panel { Dock = DockStyle.Top, Height = 25, BackColor = Color_Light };
                pSpecHeader.Controls.Add(new Label { Text = "🔧 MODULE SPECIFIC PARAMETERS", ForeColor = Color_Text, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true });
                PropertyGrid pgSpec = new PropertyGrid { Dock = DockStyle.Fill, BackColor = Color_Bg, ViewBackColor = Color_Bg, ViewForeColor = Color_Text, LineColor = Color_Light, ToolbarVisible = false, HelpVisible = false, PropertySort = PropertySort.Categorized };
                pgSpec.PropertyValueChanged += (s, args) => RunPipelineDebug();
                pgSpec.SelectedObject = selectedModule;
                panelSpecificContainer.Controls.Add(pgSpec);
                panelSpecificContainer.Controls.Add(pSpecHeader);
            }
            RunPipelineDebug();
        }

        */
        public void StartTemplateSelection()
        {
            if (imageBoxExMain.Image == null) return;
            _isSelectingTemplate = true; _isSelectingRoi = false;
            imageBoxExMain.SelectionMode = ImageBoxSelectionMode.Rectangle;
            imageBoxExMain.Cursor = Cursors.Cross;
        }
        public void StartRoiSelection()
        {
            if (imageBoxExMain.Image == null) return;
            _isSelectingRoi = true; _isSelectingTemplate = false;
            imageBoxExMain.SelectionMode = ImageBoxSelectionMode.Rectangle;
            imageBoxExMain.Cursor = Cursors.SizeNWSE;
        }
        private void ImageBoxExMain_SelectionChanged(object sender, EventArgs e)
        {
            if (imageBoxExMain.SelectionRegion == RectangleF.Empty) return;
            int idx = listBoxPipeline.SelectedIndex;
            if (idx >= 0 && PipelineSteps[idx] is TemplateMatchModule tmm)
            {
                RectangleF selF = imageBoxExMain.SelectionRegion;
                Rectangle sourceRect = new Rectangle((int)selF.X, (int)selF.Y, (int)selF.Width, (int)selF.Height);
                if (sourceRect.Width > 5 && sourceRect.Height > 5)
                {
                    if (_isSelectingTemplate)
                    {
                        using (Bitmap fullBmp = new Bitmap(imageBoxExMain.Image))
                        {
                            Bitmap croppedTemplate = CropImage(fullBmp, sourceRect);
                            foreach (Control ctrl in panelSpecificContainer.Controls)
                            {
                                if (ctrl is TemplateMatchConfigControl matchUi) { matchUi.AddNewTemplateFromBmp(croppedTemplate); break; }
                            }
                        }
                    }
                    else if (_isSelectingRoi)
                    {
                        tmm.ROI = sourceRect;
                        MessageBox.Show($"Keresési ROI rögzítve!\nX: {tmm.ROI.X}, Y: {tmm.ROI.Y}, W: {tmm.ROI.Width}, H: {tmm.ROI.Height}", "ROI Beállítva");
                    }
                }
            }
            _isSelectingTemplate = false; _isSelectingRoi = false;
            imageBoxExMain.SelectionRegion = RectangleF.Empty;
            imageBoxExMain.SelectionMode = ImageBoxSelectionMode.None;
            imageBoxExMain.Cursor = Cursors.Default;
            RefreshUI();
        }
        private void RunPipelineDebug()
        {
            int idx = listBoxPipeline.SelectedIndex;
            if (idx < 0) return;
            using (InspectionContext context = new InspectionContext())
            {
                for (int i = 0; i <= idx; i++)
                {
                    IInspectionModule step = PipelineSteps[i];
                    step.Process(context);
                    if (context.SourceImage != null) context.ColorHistory[step.Name] = context.SourceImage.Clone();
                    if (context.WorkingMask != null) context.GrayHistory[step.Name] = context.WorkingMask.Clone();
                }
                if (imageBoxExMain.Image != null) imageBoxExMain.Image = null;
                IInspectionModule currentModule = PipelineSteps[idx];
                if ((idx == 0 || currentModule is TemplateMatchModule) && context.SourceImage != null)
                    imageBoxExMain.Image = context.SourceImage.ToBitmap();
                else if (context.WorkingMask != null)
                    imageBoxExMain.Image = context.WorkingMask.ToBitmap();
            }
        }
        // --- JAVÍTÁS: AUTOMATA IPARI SORSZÁMOZÁS A LISTA FRISSÍTÉSEKOR ---
        public void RefreshUI()
        {
            int savedIndex = listBoxPipeline.SelectedIndex;
            listBoxPipeline.Items.Clear();

            // Végigmegyünk a láncon, és dinamikusan beírjuk a valós sorszámokat (i + 1)
            for (int i = 0; i < PipelineSteps.Count; i++)
            {
                if (PipelineSteps[i] is BaseInspectionModule baseMod)
                {
                    baseMod.StepIndex = i + 1; // Automatikusan kiosztja: 01, 02, 03...
                }

                // Felrakjuk a dinamikusan felsorszámozott nevet a bal oldali listába
                listBoxPipeline.Items.Add(PipelineSteps[i].Name);
            }

            if (savedIndex >= 0 && savedIndex < listBoxPipeline.Items.Count) listBoxPipeline.SelectedIndex = savedIndex;
            else if (listBoxPipeline.Items.Count > 0) listBoxPipeline.SelectedIndex = 0;
        }

        // --- JAVÍTÁS: GOLYÓÁLLÓ JOBB OLDALI PANEL MEGJELENÍTÉS ---
        private void ListBoxPipeline_SelectedIndexChanged(object sender, EventArgs e)
        {
            int idx = listBoxPipeline.SelectedIndex;
            if (idx < 0) return;

            IInspectionModule selectedModule = PipelineSteps[idx];

            // Fixen ráültetjük a felső Common rácsra az objektumot
            propertyGridCommon.SelectedObject = selectedModule;

            // Teljesen letakarítjuk és újra kényszerítjük az alsó specifikus konténert
            panelSpecificContainer.Controls.Clear();
            panelSpecificContainer.SuspendLayout(); // Leállítjuk a villódzást a felépítés alatt

            // Típus-azonosítás a megújult öröklési hierarchiához igazítva
            if (selectedModule.GetType() == typeof(TemplateMatchModule) || selectedModule is TemplateMatchModule)
            {
                TemplateMatchModule tmm = (TemplateMatchModule)selectedModule;

                // Létrehozzuk a te nagy, gombos, táblázatos Multi-Sablon pultodat
                TemplateMatchConfigControl matchUi = new TemplateMatchConfigControl(tmm, () => RunPipelineDebug(), this);
                matchUi.Dock = DockStyle.Fill; // Kényszerítjük, hogy teljesen töltse ki az alsó részt!

                panelSpecificContainer.Controls.Add(matchUi);
            }
            else
            {
                // Minden egyéb szűrőhöz (Blur, Otsu) kitesszük a specifikus paraméter-rácsot
                Panel pSpecHeader = new Panel { Dock = DockStyle.Top, Height = 25, BackColor = Color_Light };
                pSpecHeader.Controls.Add(new Label { Text = "🔧 MODULE SPECIFIC PARAMETERS", ForeColor = Color_Text, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true });

                PropertyGrid pgSpec = new PropertyGrid { Dock = DockStyle.Fill, BackColor = Color_Bg, ViewBackColor = Color_Bg, ViewForeColor = Color_Text, LineColor = Color_Light, ToolbarVisible = false, HelpVisible = false, PropertySort = PropertySort.Categorized };
                pgSpec.PropertyValueChanged += (s, args) => RunPipelineDebug();
                pgSpec.SelectedObject = selectedModule;

                panelSpecificContainer.Controls.Add(pgSpec);
                panelSpecificContainer.Controls.Add(pSpecHeader);
            }

            panelSpecificContainer.ResumeLayout(true);
            panelSpecificContainer.Refresh(); // Kényszerítjük a Windows Forms-ot az azonnali kirajzolásra!

            RunPipelineDebug();
        }

        private void ListBoxPipeline_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (SolidBrush bg = new SolidBrush(isSelected ? Color_Cyan : Color_Bg))
            using (SolidBrush fg = new SolidBrush(isSelected ? Color.Black : Color_Text))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
                e.Graphics.DrawString(listBoxPipeline.Items[e.Index].ToString(), e.Font, fg, e.Bounds.X + 10, e.Bounds.Y + 4);
            }
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog { Filter = "Vision Recipe (.vrec)|.vrec", InitialDirectory = @"C:\Test" })
            {
                if (sfd.ShowDialog() == DialogResult.OK) VisionRecipe.SaveToFile(sfd.FileName, PipelineSteps);
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


