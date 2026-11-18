using System;
using System.Drawing;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.Structure;

namespace FlexVision
{
    public class ColorMatrixSelectorForm : Form
    {
        public CaliperColorChannel SelectedChannel { get; private set; }

        public ColorMatrixSelectorForm(Image<Bgr, byte> roiImage, CaliperColorChannel currentChannel)
        {
            this.SelectedChannel = currentChannel;
            this.Text = "Color Space & Channel Matrix Selector (Kattints a választáshoz)";
            // Megnövelt, nagy felbontású ablakméret
            this.Size = new Size(1265, 985);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(25, 25, 25);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            BuildMatrixUI(roiImage);
        }

        private void BuildMatrixUI(Image<Bgr, byte> roiImage)
        {
            CaliperColorChannel[,] matrix = new CaliperColorChannel[,]
            {
                { CaliperColorChannel.Blue,  CaliperColorChannel.Green,      CaliperColorChannel.Red },
                { CaliperColorChannel.Hue,   CaliperColorChannel.Saturation, CaliperColorChannel.Value },
                { CaliperColorChannel.Lab_L, CaliperColorChannel.Lab_A,      CaliperColorChannel.Lab_B }
            };

            string[,] titles = new string[,]
            {
                { "RGB - Blue (B)",  "RGB - Green (G)",      "RGB - Red (R)" },
                { "HSV - Hue (H)",   "HSV - Saturation (S)", "HSV - Value (V)" },
                { "Lab - Lightness (L*)", "Lab - Green/Red (a*)", "Lab - Blue/Yellow (b*)" }
            };

            // Megnövelt kártyaméretek a jobb láthatóságért
            int cardW = 400;
            int cardH = 280;
            int margin = 14;

            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    CaliperColorChannel ch = matrix[r, c];
                    string title = titles[r, c];

                    Panel card = new Panel
                    {
                        Location = new Point(margin + c * (cardW + margin), margin + r * (cardH + margin)),
                        Size = new Size(cardW, cardH),
                        BackColor = (ch == SelectedChannel) ? Color.FromArgb(0, 120, 150) : Color.FromArgb(40, 40, 40),
                        Padding = new Padding(4),
                        Cursor = Cursors.Hand
                    };

                    Label lbl = new Label
                    {
                        Text = title,
                        Dock = DockStyle.Top,
                        Height = 26,
                        ForeColor = Color.White,
                        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                        TextAlign = ContentAlignment.MiddleCenter
                    };

                    PictureBox pic = new PictureBox
                    {
                        Dock = DockStyle.Fill,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.Black,
                        Cursor = Cursors.Hand
                    };

                    using (Image<Gray, byte> grayCh = EdgeCaliperModule.ExtractChannelImage(roiImage, ch))
                    {
                        pic.Image = grayCh.ToBitmap();
                    }

                    EventHandler clickAction = (s, e) =>
                    {
                        this.SelectedChannel = ch;
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    };

                    pic.Click += clickAction;
                    lbl.Click += clickAction;
                    card.Click += clickAction;

                    card.Controls.Add(pic);
                    card.Controls.Add(lbl);
                    this.Controls.Add(card);
                }
            }

            // Alsó vezérlősáv igazítása a szélesebb ablakhoz
            int bottomY = margin + 3 * (cardH + margin) + 6;
            int totalWidth = 3 * cardW + 2 * margin;
            int btnWidth = (totalWidth - margin) / 2;

            Button btnAuto = new Button
            {
                Text = "⚡ AUTO BEST CHANNEL (Legnagyobb kontraszt)",
                Location = new Point(margin, bottomY),
                Size = new Size(btnWidth, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 180, 215),
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnAuto.Click += (s, e) => { this.SelectedChannel = CaliperColorChannel.AutoBest; this.DialogResult = DialogResult.OK; this.Close(); };

            Button btnGray = new Button
            {
                Text = "📷 GRAYSCALE (Eredeti átlagolt)",
                Location = new Point(margin + btnWidth + margin, bottomY),
                Size = new Size(btnWidth, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(65, 65, 65),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnGray.Click += (s, e) => { this.SelectedChannel = CaliperColorChannel.Grayscale; this.DialogResult = DialogResult.OK; this.Close(); };

            this.Controls.Add(btnAuto);
            this.Controls.Add(btnGray);
        }
    }
}
