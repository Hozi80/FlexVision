using System;
using System.Drawing;
using System.ComponentModel;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using static Slider.Methods.Math;
using static Slider.Methods.Drawing;
using static Slider.Methods.System;
using Slider.Classes;

namespace Slider.Controls
{
	[DefaultEvent("Scroll")]
	public class Slider : Control
	{
		#region Events
		[Category("Events")]
		[Description("Events")]
		public event EventHandler ValueChanged; 
		private void OnValueChanged()
		{
			ValueChanged?.Invoke(this, EventArgs.Empty);
		}
		[Category("Events")]
		[Description("Events")]	
		public event ScrollEventHandler Scroll; 
		private void OnScroll(int newValue)
		{
			Scroll?.Invoke(this, new ScrollEventArgs(ScrollEventType.ThumbPosition, newValue));
		}
		#endregion

		#region Fields

		private UDLROrientation orientation = UDLROrientation.Down;

        [Category("Appearance")]
		[Description("Appearance")]
		public UDLROrientation Orientation
		{
			get { return orientation; }
			set
			{
				if (((orientation == UDLROrientation.Up || orientation == UDLROrientation.Down) &&
					(value == UDLROrientation.Right || value == UDLROrientation.Left)) ||
					((orientation == UDLROrientation.Right || orientation == UDLROrientation.Left) &&
					(value == UDLROrientation.Down || value == UDLROrientation.Up)))
				{
					int tmp = Width;
					Width = Height;
					Height = tmp;
				}
				orientation = value;
			}
		}
		[Category("Appearance")]
		[Description("Appearance")]
		public bool CustomBackground { get; set; } = false;
		[Category("Appearance")]
		[Description("Appearance")]
		public bool Reverse { get; set; } = false;
    private int _Value = 25;
		[Category("Data")]
		[Description("Data")]
		public int Value
    {
      get
            { return _Value; }
      set
      {
        _Value = Clamp(value, Maximum, Minimum);
      }
    }
		[Category("Data")]
		[Description("Data")]
		public int Minimum { get; set; } = 0;
		[Category("Data")]
		[Description("Data")]
		public int Maximum { get; set; } = 100;

		#endregion
		
		#region Constructor

		public Slider()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint |
							 ControlStyles.OptimizedDoubleBuffer |
							 ControlStyles.ResizeRedraw |
							 ControlStyles.Selectable |
							 ControlStyles.SupportsTransparentBackColor |
							 ControlStyles.UserMouse |
							 ControlStyles.UserPaint, true);

			
			SetProperties();
		}

		private void SetProperties()
		{
			HoverTimer = new Timer();
			HoverTimer.Tick += HoverTimer_Tick;
			HoverTimer.Interval = 10;

      MoveTimer = new Timer();
      MoveTimer.Tick += MoveTimer_Tick;
      MoveTimer.Interval = 5;

      BackColor = Color.Transparent;

      this.Width = 200;
      this.Height = 10;
    }

    #endregion

    #region Paint Methods
    [Category("Appearance")]
    public Color Color { get; set; } = Color.FromKnownColor(KnownColor.MenuHighlight);

    private Color backColor => (CustomBackground) ? BackColor : Parent.BackColor;
		private Color thumbEdgeColor;
		private Color barColor;
		private Color textColor;
    private Color thumbColor;
    protected override void OnPaint(PaintEventArgs e)
		{
			if (!Enabled)
			{
        thumbColor = WriteOut(Color, 50);
				thumbEdgeColor = ShadeColors.Thumb.Normal;
				barColor = ShadeColors.Bar.Disabled;
				textColor = ShadeColors.Text.Disabled;
			}
			else if (IsFocus)
			{
        thumbColor = Color;
				thumbEdgeColor = ShadeColors.Thumb.Focus;
				barColor = ShadeColors.Bar.Focus;
				textColor = ShadeColors.Text.Focus;
			}
			else if (IsPressed)
			{
        thumbColor = Color;
        thumbEdgeColor = ShadeColors.Thumb.Pressed;
				barColor = ShadeColors.Bar.Pressed;
				textColor = ShadeColors.Text.Pressed;
			}
			else
			{
        thumbColor = Color;
        thumbEdgeColor = ShadeColors.Thumb.Normal;
				barColor = ShadeColors.Bar.Normal;
				textColor = ShadeColors.Text.Normal;
			}

			if (HoverTimer.Enabled)
			{
				barColor = Interpolate(ShadeColors.Bar.Normal, ShadeColors.Bar.Focus, HoverRatio);
				thumbEdgeColor = Interpolate(ShadeColors.Thumb.Normal, ShadeColors.Thumb.Focus, HoverRatio);
				textColor = Interpolate(ShadeColors.Text.Normal, ShadeColors.Text.Focus, HoverRatio);
			}

			e.Graphics.Clear(backColor);
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      DrawSlider(e.Graphics);

			if (false && IsFocus)
				ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
		}

    private int OrientWidth
    {
      get
      {
        return (orientation == UDLROrientation.Down ||
          orientation == UDLROrientation.Up) ? Width : Height;
      }
    }
    private int OrientHeight
    {
      get
      {
        return (orientation == UDLROrientation.Down ||
          orientation == UDLROrientation.Up) ? Height : Width;
      }
    }
    private int ThumbEdgeWidth => 
      Clamp(OrientHeight * (1.5f / 20f), int.MaxValue, 1);
    private int ThumbHeight => 
      Clamp(OrientHeight * (10f / 20f), int.MaxValue, 6);
    private int ThumbWidth => ThumbHeight;
    private int BarHeightY => Clamp(OrientHeight * (5f / 20f), int.MaxValue, 4);
    private int BarY => (int)(OrientHeight * 0.5);
    private float FontSize => ThumbWidth * 0.4f;

    private float ValuePosRatio => (float)ValueLength / BarLength;
    private float PosValueRatio => (float)BarLength / ValueLength;
    private int OffsetBoundaryX => ThumbWidth / 2 + ThumbEdgeWidth;

    private int BarMaxPos => (!Reverse) ? OrientWidth - OffsetBoundaryX : OffsetBoundaryX;
    private int BarMinPos => (!Reverse) ? OffsetBoundaryX : OrientWidth - OffsetBoundaryX;
    private int ValueLength => Maximum - Minimum;
    private int BarLength => BarMaxPos - BarMinPos;
    private int ThumbPos => Value2Pos(Value);
    private int Pos2Value(int pixel) => LinConvert(pixel, BarMaxPos, BarMinPos, Maximum, Minimum);
    private int Value2Pos(int ax) => LinConvert(ax, Maximum, Minimum, BarMaxPos, BarMinPos);

  
    private int[] ThumbXs
    {
      get
      {
        switch (orientation)
        {
          case UDLROrientation.Down:
            return new int[] { -1, 1, 1, 0, -1 };
          case UDLROrientation.Right:
            return new int[] { -1, 1, 2, 1, -1 };
          case UDLROrientation.Up:
            return new int[] { -1, 0, 1, 1, -1 };
          case UDLROrientation.Left:
            return new int[] { -1, 1, 1, -1, -2 };
          default:
            return null;
        }
      }
    }
    private int[] ThumbYs
    {
      get
      {
        switch (orientation)
        {
          case UDLROrientation.Down:
            return new int[] { -1, -1, 1, 2, 1 };
          case UDLROrientation.Right:
            return new int[] { -1, -1, 0, 1, 1 };
          case UDLROrientation.Up:
            return new int[] { -1, -2, -1, 1, 1 };
          case UDLROrientation.Left:
            return new int[] { -1, -1, 1, 1, 0 };
          default:
            return null;
        }
      }
    }
    private Point[] getThumbPts(int x, int y, int scaleX, int scaleY)
    {
      int[] xs = ThumbXs;
      int[] ys = ThumbYs;
      Point[] thumbPts = new Point[5];

      for (int i = 0; i < 5; i++)
      {
        xs[i] *= scaleX;
        xs[i] += x;
        ys[i] *= scaleY;
        ys[i] += y;
        thumbPts[i] = new Point(xs[i], ys[i]);
      }
      return thumbPts;
    }

    private void DrawSlider(Graphics g)
		{
      SolidBrush thumbBrush = new SolidBrush(Color);
			Pen thumbPen = new Pen(thumbEdgeColor) { Width = ThumbEdgeWidth };
			Pen barLPen = new Pen(thumbColor) { StartCap = LineCap.Round, EndCap = LineCap.Round, Width = BarHeightY };
			Pen barRPen = new Pen(barColor) { StartCap = LineCap.Round, EndCap = LineCap.Round, Width = BarHeightY };
			Font font = new Font("Segoe UI", FontSize, FontStyle.Bold, GraphicsUnit.Pixel);

			Point barLPtL, barLPtR, barRPtL, barRPtR;
			Point[] thumbPts;
			Rectangle txtRect;
			int thumbY;
			if (orientation == UDLROrientation.Down || 
        orientation == UDLROrientation.Right)
				thumbY = (int)(BarY * 0.7);
			else
				thumbY = (int)(BarY * 1.3);
			if (orientation == UDLROrientation.Down || 
        orientation == UDLROrientation.Up)
			{
				barLPtL = new Point(BarMinPos, BarY);
				barLPtR = new Point(ThumbPos, BarY);
				barRPtL = new Point(ThumbPos, BarY);
				barRPtR = new Point(BarMaxPos, BarY);
				
				thumbPts = getThumbPts(ThumbPos, thumbY, ThumbWidth / 2, ThumbHeight / 2);
			}
			else
			{
				barLPtL = new Point(BarY, BarMinPos);
				barLPtR = new Point(BarY, ThumbPos);
				barRPtL = new Point(BarY, ThumbPos);
				barRPtR = new Point(BarY, BarMaxPos);
				
				thumbPts = getThumbPts(thumbY, ThumbPos, ThumbWidth / 2, ThumbHeight / 2);
			}

			g.DrawLine(barLPen, barLPtL, barLPtR);
			g.DrawLine(barRPen, barRPtL, barRPtR);
      DrawPolygonShadow(g, thumbPts, ThumbEdgeWidth);
			g.FillPolygon(thumbBrush, thumbPts);
			txtRect = new Rectangle(thumbPts[0].X, thumbPts[0].Y, ThumbWidth, ThumbHeight);
			TextRenderer.DrawText(g, Value.ToString(), font, txtRect, textColor, Color.Transparent, GetTextFormatFlags(ContentAlignment.MiddleCenter));
		}

    #endregion

    #region Animation
    private Timer HoverTimer;
    private float HoverRatio; // 0~1
    private bool IsIncreasing;
    private void HoverTimer_Tick(object sender, EventArgs e)
    {
      HoverRatio = (IsIncreasing) ? HoverRatio + 0.05f : HoverRatio - 0.05f;
      if (!IsIn(HoverRatio, 1, 0, true))
      {
        HoverTimer.Stop();
      }
      Invalidate();
    }

    private Timer MoveTimer;
    private float MoveRatio; //0~1
    private int StartValue;
    private int EndValue;
    private void MoveTimer_Tick(object sender, EventArgs e)
    {
      MoveRatio += 0.5f;
      if (!IsIn(MoveRatio, 1, 0, true))
      {
        MoveTimer.Stop();
            //    OnValueChanged();
            }
//  int temp = (int)Interpolate(StartValue, EndValue, MoveRatio);
            Value = (int)Interpolate(StartValue, EndValue, MoveRatio);
    //  OnValueChanged();
      OnScroll(Value);
      Invalidate();
    }
    #endregion

    #region Focus Methods
    private bool IsPressed = false;
    private bool IsFocus = false;
    protected override void OnGotFocus(EventArgs e)
    {
      IsFocus = true;
      IsIncreasing = true;
      HoverRatio = 0;
      HoverTimer.Start();
      base.OnGotFocus(e);
    }
    protected override void OnLostFocus(EventArgs e)
    {
      IsFocus = false;
      IsIncreasing = false;
      if (!HoverTimer.Enabled)
      {
        HoverRatio = 1;
        HoverTimer.Start();
      }
      base.OnLostFocus(e);
    }
    protected override void OnMouseEnter(EventArgs e)
    {
      if (!IsFocus)
      {
        Focus();
        IsFocus = true;
        IsIncreasing = true;
        HoverRatio = 0;
        HoverTimer.Start();
      }
      base.OnMouseEnter(e);
    }
    #endregion

    #region Keyboard Methods
    private int SmallChange => Clamp(ValueLength / 20f, int.MaxValue, 1);
    private int LargeChange => Clamp(ValueLength / 10f, int.MaxValue, 1);
    protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (!IsFocus)
        return;

			int delta = 0;
			switch (e.KeyCode)
			{
				case Keys.Up:
					delta = (orientation == UDLROrientation.Down || orientation == UDLROrientation.Up) ? 0 : -SmallChange;
					delta = (!Reverse) ? delta : -delta;
					break;
				case Keys.Down:
					delta = (orientation == UDLROrientation.Down || orientation == UDLROrientation.Up) ? 0 : SmallChange;
					delta = (!Reverse) ? delta : -delta;
					break;
				case Keys.Left:
					delta = (orientation == UDLROrientation.Down || orientation == UDLROrientation.Up) ? -SmallChange : 0;
					delta = (!Reverse) ? delta : -delta;
					break;
				case Keys.Right:
					delta = (orientation == UDLROrientation.Down || orientation == UDLROrientation.Up) ? SmallChange : 0;
					delta = (!Reverse) ? delta : -delta;
					break;
				case Keys.Home:
					delta = -ValueLength;
					break;
				case Keys.End:
					delta = ValueLength;
					break;
				case Keys.PageDown:
					delta = -LargeChange;
					break;
				case Keys.PageUp:
					delta = +LargeChange;
					break;
			}

      StartValue = Value;
      EndValue = Value + delta;
      MoveRatio = 0;
      MoveTimer.Start();

			OnValueChanged();
			OnScroll(Value);

			Invalidate();
		}

    protected override bool ProcessDialogKey(Keys keyData)
    {
      if (keyData == Keys.Tab | ModifierKeys == Keys.Shift)
        return base.ProcessDialogKey(keyData);
      else
      {
        OnKeyDown(new KeyEventArgs(keyData));
        return true;
      }
    }
    #endregion

    #region Mouse Methods
    private int MousePos;
    private void Mouse2Value(MouseEventArgs e)
		{
			MousePos = (orientation == UDLROrientation.Down || orientation == UDLROrientation.Up) ? e.Location.X : e.Location.Y;
			MousePos = Clamp(MousePos, BarMaxPos, BarMinPos);
			Value = Pos2Value(MousePos);
		}
    protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (e.Button != MouseButtons.Left) { return; }
			IsPressed = true;

      MousePos = (orientation == UDLROrientation.Down || orientation == UDLROrientation.Up) ? e.Location.X : e.Location.Y;
      MousePos = Clamp(MousePos, BarMaxPos, BarMinPos);

      StartValue = Value;
      EndValue = Pos2Value(MousePos);
      MoveRatio = 0;
      MoveTimer.Start();

      OnScroll(Value);
			HoverTimer.Stop();
			Invalidate();
		}
		protected override void OnMouseUp(MouseEventArgs e)
		{
			IsPressed = false;
			base.OnMouseUp(e);
			OnValueChanged();
			Invalidate();
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (!(IsPressed & e.Button == MouseButtons.Left)) return;
			Mouse2Value(e);
            OnValueChanged();

            OnScroll(Value);
			Invalidate();
		}

    private int MouseWheelBarPartitions = 20;
    protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);
			if (!IsFocus) return;

			int delta = (int)(e.Delta / 60f * ValueLength / MouseWheelBarPartitions);
			Value += delta;

			OnValueChanged();
			OnScroll(Value);
			Invalidate(); 
		}
		#endregion

		#region Overridden Methods

		protected override void OnEnabledChanged(EventArgs e)
		{
			base.OnEnabledChanged(e);
			Invalidate();
		}

        #endregion

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ResumeLayout(false);

        }
    }
}
