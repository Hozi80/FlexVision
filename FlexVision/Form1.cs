using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlexVision
{
    public partial class MainVisionForm : Form
    {
        private readonly Color Color_Bg_Dark = Color.FromArgb(30, 30, 30);
        private readonly Color Color_Panel_Slate = Color.FromArgb(45, 45, 45);
        private readonly Color Color_Control_Light = Color.FromArgb(60, 60, 60);
        private readonly Color Color_Text_White = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Accent_Cyan = Color.FromArgb(0, 215, 255);
        private readonly Color Color_Accent_Orange = Color.FromArgb(255, 120, 10);

        private Panel panelTopBar;
        private Button btnRunMode;
        private Button btnSetupMode;
        private Button btnGlobalSettings;
        private Button btnUserLogin;
        private Button btnGlobalOpen;
        private Label lblRecipeName; // A megnövelt, centrált felirat

        private TabControl tabControlMain;
        private TabPage tabRunMode;
        private TabPage tabSetupMode;

        private PipelineEditorControl pipelineEditor;

        public MainVisionForm()
        {
            InitializeComponent();

            this.Text = "Vision Inspection Framework - Saturated Pipeline Engine";
            this.Width = 1450;
            this.Height = 850;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color_Bg_Dark;

            InitializeDarkThemeUI();

            // Ablak átméretezés feliratkozás a tökéletes középvonal tartásához
            this.Resize += MainVisionForm_Resize;

            UserSession.Instance.UserChanged += UserSession_UserChanged;
            UserSession_UserChanged(null, null);
        }

        private void InitializeDarkThemeUI()
        {
            panelTopBar = new Panel { Dock = DockStyle.Top, Height = 65, BackColor = Color_Panel_Slate, Padding = new Padding(10) };

            // BAL CSOPORT
            btnRunMode = new Button { Text = "▶ RUN MODE", Width = 130, Height = 45, Location = new Point(15, 10), FlatStyle = FlatStyle.Flat, BackColor = Color_Accent_Cyan, ForeColor = Color.Black, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand };
            btnRunMode.FlatAppearance.BorderSize = 0;
            btnRunMode.Click += BtnRunMode_Click;

            btnGlobalOpen = new Button { Text = "📂 OPEN RECIPE", Width = 140, Height = 45, Location = new Point(155, 10), FlatStyle = FlatStyle.Flat, BackColor = Color_Control_Light, ForeColor = Color_Text_White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnGlobalOpen.FlatAppearance.BorderColor = Color_Accent_Cyan;
            btnGlobalOpen.Click += BtnGlobalOpen_Click;

            // --- JAVÍTÁS: UKTRABOLD, MEGNÖVELT BETŰMÉRETŰ RECEPT FELIRAT (Font size 12-ről 14-re emelve) ---
            lblRecipeName = new Label
            {
                Text = "RECIPE: NO RECIPE LOADED",
                ForeColor = Color_Accent_Cyan,
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                AutoSize = true
            };

            // JOBB CSOPORT AUTOMATA IGAZÍTÁSSAL
            FlowLayoutPanel panelRightGroup = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 560,
                Height = 45,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 0)
            };

            btnUserLogin = new Button { Text = "👤 USER: OPERATOR (LOGIN)", Width = 230, Height = 45, FlatStyle = FlatStyle.Flat, BackColor = Color_Control_Light, ForeColor = Color_Text_White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(10, 0, 0, 0) };
            btnUserLogin.FlatAppearance.BorderColor = Color_Accent_Cyan;
            btnUserLogin.Click += BtnUserLogin_Click;

            btnGlobalSettings = new Button { Text = "⚙ SYSTEM SETTINGS", Width = 140, Height = 45, FlatStyle = FlatStyle.Flat, BackColor = Color_Control_Light, ForeColor = Color_Text_White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(10, 0, 0, 0) };
            btnGlobalSettings.FlatAppearance.BorderColor = Color_Accent_Orange;
            btnGlobalSettings.Click += BtnGlobalSettings_Click;

            btnSetupMode = new Button { Text = "🛠 SETUP MODE", Width = 130, Height = 45, FlatStyle = FlatStyle.Flat, BackColor = Color_Control_Light, ForeColor = Color_Text_White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 0) };
            btnSetupMode.FlatAppearance.BorderColor = Color_Accent_Cyan;
            btnSetupMode.Click += BtnSetupMode_Click;

            panelRightGroup.Controls.Add(btnUserLogin);
            panelRightGroup.Controls.Add(btnGlobalSettings);
            panelRightGroup.Controls.Add(btnSetupMode);

            panelTopBar.Controls.Add(btnRunMode);
            panelTopBar.Controls.Add(btnGlobalOpen);
            panelTopBar.Controls.Add(lblRecipeName);
            panelTopBar.Controls.Add(panelRightGroup);
            this.Controls.Add(panelTopBar);

            // 2. FŐ KONTÉNER
            tabControlMain = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, ItemSize = new Size(0, 1), SizeMode = TabSizeMode.Fixed };
            tabRunMode = new TabPage { BackColor = Color_Bg_Dark, Text = "Run" };
            tabSetupMode = new TabPage { BackColor = Color_Bg_Dark, Text = "Setup" };

            tabControlMain.TabPages.Add(tabRunMode);
            tabControlMain.TabPages.Add(tabSetupMode);
            this.Controls.Add(tabControlMain);
            panelTopBar.SendToBack();

            // RUN MODE BELSŐ
            Label lblRunTitle = new Label { Text = "PRODUCTION INSPECTION ACTIVE", ForeColor = Color_Text_White, Font = new Font("Segoe UI", 16, FontStyle.Bold), Location = new Point(30, 30), AutoSize = true };
            tabRunMode.Controls.Add(lblRunTitle);

            // SETUP MODE BELSŐ
            pipelineEditor = new PipelineEditorControl();
            tabSetupMode.Controls.Add(pipelineEditor);
            pipelineEditor.RefreshUI();

            // Középre igazítás kényszerítése az első induláskor
            MainVisionForm_Resize(null, null);
        }

        // --- JAVÍTÁS: DINAMIKUS MATEMATIKAI KÖZÉPRE IGAZÍTÁS ---
        private void MainVisionForm_Resize(object sender, EventArgs e)
        {
            if (lblRecipeName == null || panelTopBar == null) return;

            // Kiszámoljuk a bal oldali gombok végét és a jobb oldali csoport elejét
            int leftLimit = 310;
            int rightLimit = this.Width - 580;

            // Meghatározzuk a két csoport közötti tiszta folyosó mértani közepét
            int centerSpace = leftLimit + ((rightLimit - leftLimit) / 2);

            // Eltoljuk az X koordinátát a felirat saját szélességének a felével balra, hogy a súlypontja üljön középen
            int finalX = centerSpace - (lblRecipeName.Width / 2);

            // Biztonsági guardrail, hogy kis ablakméretnél se lógjon rá a bal oldali gombokra
            if (finalX < leftLimit) finalX = leftLimit;

            // Beállítjuk a tűpontos Y és X helyzetet (Y = 18 px, hogy a nagyobb betűk is szépen üljenek)
            lblRecipeName.Location = new Point(finalX, 18);
        }

        private void BtnGlobalOpen_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog { Filter = "Vision Recipe (*.vrec)|*.vrec", InitialDirectory = @"C:\Test" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    var loadedSteps = VisionRecipe.LoadFromFile(ofd.FileName);
                    pipelineEditor.PipelineSteps.Clear();
                    foreach (var step in loadedSteps) pipelineEditor.PipelineSteps.Add(step);
                    pipelineEditor.RefreshUI();

                    string fileNameOnly = System.IO.Path.GetFileNameWithoutExtension(ofd.FileName);
                    lblRecipeName.Text = $"RECIPE: {fileNameOnly.ToUpper()}";

                    // Frissítjük a középre igazítást, mert a szöveg szélessége megváltozott!
                    MainVisionForm_Resize(null, null);
                }
            }
        }

        private void BtnUserLogin_Click(object sender, EventArgs e)
        {
            if (UserSession.Instance.CurrentRole != UserRole.Operator)
            {
                UserSession.Instance.Login("Gepkezelo_01", UserRole.Operator);
                MessageBox.Show("Rendszer visszaállt Operator (Gépkezelő) szintre.", "Kijelentkezés", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (LoginForm loginBox = new LoginForm())
            {
                loginBox.ShowDialog(this);
            }
        }

        private void UserSession_UserChanged(object sender, EventArgs e)
        {
            bool hasAccess = UserSession.Instance.CanEditPipeline();
            btnSetupMode.Enabled = hasAccess;
            btnGlobalSettings.Enabled = hasAccess;

            if (UserSession.Instance.CurrentRole == UserRole.Operator)
            {
                btnUserLogin.Text = "👤 USER: OPERATOR (LOGIN)";
                btnUserLogin.FlatAppearance.BorderColor = Color_Accent_Cyan;
                if (tabControlMain.SelectedTab == tabSetupMode) BtnRunMode_Click(null, null);
            }
            else if (UserSession.Instance.CurrentRole == UserRole.Technician)
            {
                btnUserLogin.Text = "⚙️ TECH: " + UserSession.Instance.CurrentUserName.ToUpper() + " (LOGOUT)";
                btnUserLogin.FlatAppearance.BorderColor = Color_Accent_Orange;
            }
            else if (UserSession.Instance.CurrentRole == UserRole.Administrator)
            {
                btnUserLogin.Text = "👑 ADMIN: " + UserSession.Instance.CurrentUserName.ToUpper() + " (LOGOUT)";
                btnUserLogin.FlatAppearance.BorderColor = Color.Red;
            }
            MainVisionForm_Resize(null, null);
        }
        private void BtnRunMode_Click(object sender, EventArgs e)
        {
            tabControlMain.SelectedTab = tabRunMode;
            lblRecipeName.ForeColor = Color_Accent_Cyan;
            MainVisionForm_Resize(null, null);
        }
        private void BtnSetupMode_Click(object sender, EventArgs e)
        {
            if (!UserSession.Instance.CanEditPipeline()) return;
            tabControlMain.SelectedTab = tabSetupMode;
            lblRecipeName.ForeColor = Color_Accent_Orange;
            pipelineEditor.RefreshUI();
            MainVisionForm_Resize(null, null);
        }
        private void BtnGlobalSettings_Click(object sender, EventArgs e)
        {
            if (!UserSession.Instance.CanEditPipeline()) return;
            BtnSetupMode_Click(null, null);
        }
    }
}