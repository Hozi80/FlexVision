using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using Emgu.CV;
using Emgu.CV.Structure;

using Cyotek.Windows.Forms.Demo;

namespace FlexVision
{
    public class PipelineEditorControl : UserControl
    {
        private readonly Color Color_Bg = Color.FromArgb(30, 30, 30);
        private readonly Color Color_Slate = Color.FromArgb(45, 45, 45);
        private readonly Color Color_Light = Color.FromArgb(60, 60, 60);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        // --- OSZTÁLY SZINTŰ DEKLARÁCIÓK (Így minden belső metódus garantáltan látja őket!) ---
        private ListBox listBoxPipeline;
        private PropertyGrid propertyGridModuleParams;
        private ImageBoxEx imageBoxExMain; // A te Cyotek alapú nagyítható képmeződ

        private SplitContainer splitLeft;
        private SplitContainer splitRight;

        private Panel panelToolbar;
        private Button btnNew, btnSave, btnRunAll;
        private Button btnAdd, btnDelete;

        public List<IInspectionModule> PipelineSteps { get; private set; } = new List<IInspectionModule>();

        public PipelineEditorControl()
        {
            this.BackColor = Color_Bg;
            this.Dock = DockStyle.Fill;
            InitializeComponent();
            CreateDefaultPipeline();
        }

        private void InitializeComponent()
        {
            // 1. HASÁBOK LÉTREHOZÁSA MINIMÁLIS MÉRETEK NÉLKÜL (A fagyás és InvalidOp ellen)
            splitLeft = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 270, BackColor = Color_Bg, FixedPanel = FixedPanel.Panel1 };
            splitRight = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 600, BackColor = Color_Bg, FixedPanel = FixedPanel.Panel2 };

            this.Controls.Add(splitLeft);
            splitLeft.Panel2.Controls.Add(splitRight);

            // Dinamikus méretezés indításkor és maximalizáláskor
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

            // BAL OLDALI 3-AS GOMBSOR
            panelToolbar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color_Slate };

            btnNew = new Button { Text = "📄 NEW", Location = new Point(6, 10), Width = 83, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color_Cyan, BackColor = Color_Light, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnNew.FlatAppearance.BorderSize = 0;
            btnNew.Click += (s, e) => { CreateDefaultPipeline(); RefreshUI(); };

            btnSave = new Button { Text = "💾 SAVE", Location = new Point(93, 10), Width = 83, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color_Text, BackColor = Color_Light, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += OnSaveClick;

            btnRunAll = new Button { Text = "▶ RUN", Location = new Point(180, 10), Width = 84, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color.LightGreen, BackColor = Color_Light, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnRunAll.FlatAppearance.BorderSize = 0;
            btnRunAll.Click += (s, e) => { if (listBoxPipeline.Items.Count > 0) listBoxPipeline.SelectedIndex = listBoxPipeline.Items.Count - 1; };

            panelToolbar.Controls.Add(btnNew); panelToolbar.Controls.Add(btnSave); panelToolbar.Controls.Add(btnRunAll);

            // BAL OLDALI FOLYAMATLISTA
            Panel pListHeader = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color_Light };
            pListHeader.Controls.Add(new Label { Text = "PIPELINE STEPS", ForeColor = Color_Text, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 8) });

            listBoxPipeline = new ListBox { Dock = DockStyle.Fill, BackColor = Color_Bg, ForeColor = Color_Text, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10), ItemHeight = 24, DrawMode = DrawMode.OwnerDrawFixed };
            listBoxPipeline.DrawItem += ListBoxPipeline_DrawItem;
            listBoxPipeline.SelectedIndexChanged += ListBoxPipeline_SelectedIndexChanged;

            Panel pListButtons = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Color_Slate };
            btnAdd = new Button { Text = "[ + ADD STEP ]", Width = 125, Height = 28, Location = new Point(6, 6), FlatStyle = FlatStyle.Flat, BackColor = Color_Light, ForeColor = Color_Cyan, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            btnDelete = new Button { Text = "[ - REMOVE ]", Width = 125, Height = 28, Location = new Point(135, 6), FlatStyle = FlatStyle.Flat, BackColor = Color_Light, ForeColor = Color.FromArgb(255, 120, 10), Font = new Font("Segoe UI", 8, FontStyle.Bold) };

            // BEKÖTÉS: Az új Sablonkereső modul golyóálló hozzáadása
            btnAdd.Click += (s, e) => {
                PipelineSteps.Add(new TemplateMatchModule());
                RefreshUI();
                listBoxPipeline.SelectedIndex = listBoxPipeline.Items.Count - 1;
            };
            pListButtons.Controls.Add(btnAdd); pListButtons.Controls.Add(btnDelete);

            splitLeft.Panel1.Controls.Add(listBoxPipeline);
            splitLeft.Panel1.Controls.Add(pListHeader);
            splitLeft.Panel1.Controls.Add(panelToolbar);
            splitLeft.Panel1.Controls.Add(pListButtons);

            // KÖZÉPSŐ NAGY KÉP (TE IMAGEBOXEX VEZÉRLŐD A SÖTÉTÍTETT GRID-DEL)
            Panel pImgHeader = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color_Light };
            pImgHeader.Controls.Add(new Label { Text = "LIVE VIEW DEBUG", ForeColor = Color_Text, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 8) });

            imageBoxExMain = new ImageBoxEx { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 15) };
            imageBoxExMain.Name = "imageBoxExMain";
            imageBoxExMain.GridColor = Color.FromArgb(55, 55, 55);
            imageBoxExMain.GridColorAlternate = Color.FromArgb(45, 45, 45);
            
            imageBoxExMain.Selected += ImageBoxExMain_SelectionChanged;
            splitRight.Panel1.Controls.Add(imageBoxExMain);
            splitRight.Panel1.Controls.Add(pImgHeader);
        }

        // --- DINAMIKUS HASÁB CSERE A LISTA KATTINTÁSKOR ---
        private void ListBoxPipeline_SelectedIndexChanged(object sender, EventArgs e)
        {
            int idx = listBoxPipeline.SelectedIndex;
            if (idx < 0) return;

            IInspectionModule selectedModule = PipelineSteps[idx];
            splitRight.Panel2.Controls.Clear(); // Tiszta lappal indulunk a jobb oldalon

            if (selectedModule is TemplateMatchModule tmm)
            {
                // Sablonkereső esetén betöltjük a te saját dizájnos vezérlőpultodat
                TemplateMatchConfigControl matchUi = new TemplateMatchConfigControl(tmm, () => RunPipelineDebug(), this);
                splitRight.Panel2.Controls.Add(matchUi);
            }
            else
            {
                // Sima szűrőknél visszaadjuk a standard PropertyGrid rácsot
                Panel pParamHeader = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color_Light };
                pParamHeader.Controls.Add(new Label { Text = "PROPERTIES", ForeColor = Color_Text, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(10, 8) });

                propertyGridModuleParams = new PropertyGrid { Dock = DockStyle.Fill, BackColor = Color_Bg, ViewBackColor = Color_Bg, ViewForeColor = Color_Text, LineColor = Color_Light, HelpBackColor = Color_Slate, HelpForeColor = Color_Text, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
                propertyGridModuleParams.PropertyValueChanged += (s, args) => RunPipelineDebug();
                propertyGridModuleParams.SelectedObject = selectedModule;

                splitRight.Panel2.Controls.Add(propertyGridModuleParams);
                splitRight.Panel2.Controls.Add(pParamHeader);
            }

            RunPipelineDebug();
        }


        // Osztály szintű jelző a sablon-tanításhoz
        private bool _isSelectingTemplate = false;

        // Publikus indító metódus, amit a jobb oldali gomb meg fog hívni
        public void StartTemplateSelection()
        {
            if (imageBoxExMain.Image == null) return;

            _isSelectingTemplate = true;
            // Átváltjuk a Cyotek ImageBox belső módját KIJELÖLÉSRE (Selection)
            // Megjegyzés: Ha a te ImageBoxEx-edben ez a tulajdonság másképp van elnevezve, igazítsd hozzá (pl. SelectionMode = true)
            imageBoxExMain.SelectionMode =  Cyotek.Windows.Forms.ImageBoxSelectionMode.Rectangle;
            imageBoxExMain.Cursor = Cursors.Cross;

            int idx = listBoxPipeline.SelectedIndex;
            if (idx >= 0) listBoxPipeline.Items[idx] = "🎯 DRAG MOUSE ON IMAGE...";
        }

        // --- CYOTEK EGÉR-KIJELÖLÉS VÉGE (Ezt kötjük be az InitializeComponent-ben) ---
        private void ImageBoxExMain_SelectionChanged(object sender, EventArgs e)
        {
            // Csak akkor vágunk, ha aktívan a bejelentett sablon-tanítás fázisban vagyunk és van érvényes kijelölés
            if (!_isSelectingTemplate || imageBoxExMain.SelectionRegion == RectangleF.Empty) return;

            try
            {
                // Lekérjük a Cyotek által pixelpontosan kiszámolt kép-koordinátás téglalapot
                RectangleF selF = imageBoxExMain.SelectionRegion;
                Rectangle sourceRect = new Rectangle((int)selF.X, (int)selF.Y, (int)selF.Width, (int)selF.Height);

                // Biztonsági minimum méret (ne vágjunk 2x2 pixeles parányi zajt)
                if (sourceRect.Width > 5 && sourceRect.Height > 5)
                {
                    // Kivágjuk a darabot a te Utils.CropImage logikáddal a nyers főképből!
                    int idx = listBoxPipeline.SelectedIndex;
                    if (idx >= 0 && PipelineSteps[idx] is TemplateMatchModule tmm)
                    {
                        using (Bitmap fullBmp = new Bitmap(imageBoxExMain.Image))
                        {
                            Bitmap croppedTemplate = CropImage(fullBmp, sourceRect);

                            // Megkeressük a jobb oldalon aktívan nyitva lévő konfigurációs panelt
                            foreach (Control ctrl in splitRight.Panel2.Controls)
                            {
                                if (ctrl is TemplateMatchConfigControl matchUi)
                                {
                                    // Közvetlenül átadjuk a kivágott Bitmap-et a jobb oldali Multi-Template lapozónak!
                                    matchUi.AddNewTemplateFromBmp(croppedTemplate);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Template mouse selection crop error: " + ex.Message);
            }
            finally
            {
                // Visszaállítjuk a normál görgetős/zoomolós kameramódot
                _isSelectingTemplate = false;
                imageBoxExMain.SelectionRegion = RectangleF.Empty; // Kijelölő négyszög eltüntetése
                imageBoxExMain.SelectionMode =  Cyotek.Windows.Forms.ImageBoxSelectionMode.None; // Vissza normál módba
                imageBoxExMain.Cursor = Cursors.Default;
                RefreshUI(); // Visszaírja a modul nevét a bal listában
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


        private void RunPipelineDebug()
        {
            int idx = listBoxPipeline.SelectedIndex;
            if (idx < 0) return;

            using (InspectionContext context = new InspectionContext())
            {
                for (int i = 0; i <= idx; i++)
                {
                    IInspectionModule step = PipelineSteps[i];

                    // 1. Lefuttatjuk az adott lépést
                    step.Process(context);

                    // 2. ÚJ: Elmentjük a keletkező kép pillanatnyi állapotát a történeti tárba a lépés nevével!
                    if (context.SourceImage != null)
                        context.ColorHistory[step.Name] = context.SourceImage.Clone();
                    if (context.WorkingMask != null)
                        context.GrayHistory[step.Name] = context.WorkingMask.Clone();
                }

                // Kijelzés frissítése a tegnapi okos debugger szerint...
                IInspectionModule currentModule = PipelineSteps[idx];
                if (currentModule is TemplateMatchModule && context.SourceImage != null)
                    imageBoxExMain.Image = context.SourceImage.ToBitmap();
                else if (context.WorkingMask != null)
                    imageBoxExMain.Image = context.WorkingMask.ToBitmap();
            }
        }

        private void CreateDefaultPipeline()
        {
            PipelineSteps.Clear();
            PipelineSteps.Add(new ImageLoadModule());
            PipelineSteps.Add(new OtsuSuperFusionModule());
        }

        public void RefreshUI()
        {
            // 1. Elmentjük, hogy a frissítés pillanatában hányadik elem volt kijelölve
            int savedIndex = listBoxPipeline.SelectedIndex;

            // 2. Újratöltjük a listát a modulnevekkel
            listBoxPipeline.Items.Clear();
            foreach (var step in PipelineSteps) listBoxPipeline.Items.Add(step.Name);

            // 3. JAVÍTÁS: Ha volt érvényes korábbi kijelölés, hajszálpontosan ODA UGRUNK VISSZA!
            if (savedIndex >= 0 && savedIndex < listBoxPipeline.Items.Count)
            {
                listBoxPipeline.SelectedIndex = savedIndex;
            }
            else if (listBoxPipeline.Items.Count > 0)
            {
                listBoxPipeline.SelectedIndex = 0; // Alapértelmezett fallback, ha nem volt kijelölés
            }
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
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    VisionRecipe.SaveToFile(sfd.FileName, PipelineSteps);
                }
            }
        }
    }
}