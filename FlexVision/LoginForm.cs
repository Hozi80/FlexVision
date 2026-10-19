using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlexVision
{
    public class LoginForm : Form
    {
        private readonly Color Color_Bg = Color.FromArgb(45, 45, 45);
        private readonly Color Color_Input = Color.FromArgb(60, 60, 60);
        private readonly Color Color_Text = Color.FromArgb(240, 240, 240);
        private readonly Color Color_Cyan = Color.FromArgb(0, 215, 255);

        private TextBox txtUser;
        private TextBox txtPassword;
        private Button btnOk;
        private Button btnCancel;

        public LoginForm()
        {
            this.Text = "SYSTEM LOGIN";
            this.Size = new Size(320, 220);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color_Bg;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Label lblUser = new Label { Text = "USERNAME:", ForeColor = Color_Text, Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(20, 20), AutoSize = true };
            txtUser = new TextBox { Location = new Point(20, 42), Width = 260, BackColor = Color_Input, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) };

            Label lblPass = new Label { Text = "PASSWORD:", ForeColor = Color_Text, Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(20, 80), AutoSize = true };
            txtPassword = new TextBox { Location = new Point(20, 102), Width = 260, BackColor = Color_Input, ForeColor = Color_Text, BorderStyle = BorderStyle.FixedSingle, PasswordChar = '*', Font = new Font("Segoe UI", 10) };

            btnOk = new Button { Text = "LOGIN", Location = new Point(60, 142), Width = 100, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color_Cyan, ForeColor = Color.Black, Font = new Font("Segoe UI", 9, FontStyle.Bold), DialogResult = DialogResult.OK };
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Click += BtnOk_Click;

            btnCancel = new Button { Text = "CANCEL", Location = new Point(180, 142), Width = 100, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color_Input, ForeColor = Color_Text, Font = new Font("Segoe UI", 9, FontStyle.Bold), DialogResult = DialogResult.Cancel };
            btnCancel.FlatAppearance.BorderSize = 0;

            this.Controls.Add(lblUser); this.Controls.Add(txtUser);
            this.Controls.Add(lblPass); this.Controls.Add(txtPassword);
            this.Controls.Add(btnOk); this.Controls.Add(btnCancel);

            this.AcceptButton = btnOk; // Enter gombra elküldi
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            string user = txtUser.Text.Trim().ToLower();
            string pass = txtPassword.Text;

            // IPARI MOCK JELSZÓKEZELÉS (igazítsd a saját igényeidhez)
            if (user == "tech" && pass == "1234")
            {
                UserSession.Instance.Login("Technikus_01", UserRole.Technician);
                this.Close();
            }
            else if (user == "admin" && pass == "admin")
            {
                UserSession.Instance.Login("Mérnök_Admin", UserRole.Administrator);
                this.Close();
            }
            else
            {
                MessageBox.Show("Hibás felhasználónév vagy jelszó!", "Sikertelen belépés", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.None; // Nem engedi bezárni az ablakot
            }
        }
    }
}
