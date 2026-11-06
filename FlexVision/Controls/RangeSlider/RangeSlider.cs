using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Windows.Forms;
using static Slider.Methods.Math;
using static Slider.Methods.Drawing;
using static Slider.Methods.System;
using Slider.Classes;

namespace Slider.Controls
{
  [DefaultEvent("Scroll")]
  public class RangeSlider : Control
  {
    #region Events
    [Category("Events")]
    [Description("Value Changed")]
    public event EventHandler ValueChanged;
    private void OnValueChanged()
    {
      ValueChanged?.Invoke(this, EventArgs.Empty);
    }
    [Category("Events")]
    [Description("on Scroll")]
    public event EventHandler Scroll;
    private void OnScroll()
    {
      Scroll?.Invoke(this, EventArgs.Empty);
    }
    #endregion

    #region Fields

    private HVOrientation orientation = HVOrientation.Horizontal;
    [Category("Appearance")]
    [Description("Orientation")]
    public HVOrientation Orientation
    {
      get { return orientation; }
      set
      {
        if (value != orientation)
        {
          int tmp = Width;
          Width = Height;
          Height = tmp;
        }
        orientation = value;
      }
    }
    [Category("Appearance")]
    [Description("Backgroung")]
    public bool CustomBackground { get; set; } = false;
    [Category("Appearance")]
    [Description("Reserve")]
    public bool Reverse { get; set; } = false;

    private int _RangeMin = 50;
    [Category("Data")]
    [Description("Range minimum")]
    public int RangeMin
    {
      get
            { return _RangeMin; }
      set
      {
        _RangeMin = Clamp(value, RangeMax - OffsetMaxMinValue, BarMin);
      }
    }

    private int _RangeMax = 200;
    [Category("Data")]
    [Description("Range maximum")]
    public int RangeMax
    {
      get
            { return _RangeMax; }
      set
      {
        _RangeMax = Clamp(value, BarMax, RangeMin + OffsetMaxMinValue);
      }
    }

    [Category("Data")]
    [Description("Bar Minimum")]
    public int BarMin { get; set; } = 0;
    
    [Category("Data")]
    [Description("Bar Maximum")]
    public int BarMax { get; set; } = 255;

    private Color ThumbColor
    {
      get
      {
        switch (Color)
        {
          case EMyColors.Blue:
            return MyColors.Blue;
          case EMyColors.Green:
            return MyColors.Green;
          case EMyColors.Red:
            return MyColors.Red;
          case EMyColors.Black:
            return MyColors.Black;
          case EMyColors.White:
            return MyColors.White;
          case EMyColors.Silver:
            return MyColors.Silver;
          case EMyColors.Lime:
            return MyColors.Lime;
          case EMyColors.Teal:
            return MyColors.Teal;
          case EMyColors.Orange:
            return MyColors.Orange;
          case EMyColors.Brown:
            return MyColors.Brown;
          case EMyColors.Pink:
            return MyColors.Pink;
          case EMyColors.Magenta:
            return MyColors.Magenta;
          case EMyColors.Purple:
            return MyColors.Purple;
          case EMyColors.Yellow:
            return MyColors.Yellow;
          default:
            return MyColors.Blue;
        }
      }
    }
    [Category("Appearance")]
    [Description("Color")]
    public EMyColors Color { get; set; } = EMyColors.Blue;
    #endregion

    #region Constructor

    public RangeSlider()
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

      this.Width = 200;
      this.Height = 10;
      base.BackColor = System.Drawing.Color.Transparent;
    }

    #endregion

    #region Paint Methods
    
    private Color backColor => (CustomBackground) ? BackColor : Parent.BackColor;
    private Color thumbEdgeColor;
    private Color barColor;
    private Color textColor;
    private Color thumbColor;
    protected override void OnPaint(PaintEventArgs e)
    {
      if (!Enabled)
      {
        thumbColor = WriteOut(ThumbColor,50);
        thumbEdgeColor = ShadeColors.Thumb.Normal;
        barColor = ShadeColors.Bar.Disabled;
        textColor = ShadeColors.Text.Disabled;
      }
      else if (IsPressed)
      {
        thumbColor = ThumbColor;
        thumbEdgeColor = ShadeColors.Thumb.Pressed;
        barColor = ShadeColors.Bar.Pressed;
        textColor = ShadeColors.Text.Pressed;
      }
      else if (IsFocus)
      {
        thumbColor = ThumbColor;
        thumbEdgeColor = ShadeColors.Thumb.Focus;
        barColor = ShadeColors.Bar.Focus;
        textColor = ShadeColors.Text.Focus;
      }
      else
      {
        thumbColor = ThumbColor;
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
      DrawRangeSlider(e.Graphics);

      if (false && IsFocus)
        ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }

    private int ClientLength => (orientation == HVOrientation.Horizontal) ? 
      Width : Height;
    private int ClientThick => (orientation == HVOrientation.Horizontal) ? 
      Height : Width;
    private int ThumbRadius => (int)(ClientThick * 0.4);
    private int ThumbEdgeWidth => (int)(ClientThick * 0.05);
    private int BarPosY => ClientThick / 2;
    private int BarThick => (int)(ThumbRadius * 1.2f);
    private float FontSize => ThumbRadius * 0.7f;
    private int OffsetMaxMinPos => (!Reverse) ? 
      ThumbRadius * 2 : -ThumbRadius * 2;
    private int OffsetMaxMinValue => (int)(OffsetMaxMinPos * ValuePosRatio);
    private int OffsetBoundaryPos => ThumbRadius + ThumbEdgeWidth;
    private int BarPosMax => (!Reverse) ? 
      ClientLength - OffsetBoundaryPos : OffsetBoundaryPos;
    private int BarPosMin => (!Reverse) ? 
      OffsetBoundaryPos : ClientLength - OffsetBoundaryPos;
    private int ValueLength => BarMax - BarMin;
    private int BarLength => BarPosMax - BarPosMin;
    private int RangeMaxPos => Value2Pos(RangeMax);
    private int RangeMinPos => Value2Pos(RangeMin);
    private int RangeValueLength => RangeMax - RangeMin;
    private int RangePosLength => RangeMaxPos - RangeMinPos;
    private float ValuePosRatio => (float)ValueLength / BarLength;
    private float PosValueRatio => (float)BarLength / ValueLength;
    private int Pos2Value(int pos) => 
      LinConvert(pos, BarPosMax, BarPosMin, BarMax, BarMin);
    private int Value2Pos(int value) => 
      LinConvert(value, BarMax, BarMin, BarPosMax, BarPosMin);
    private void DrawRangeSlider(Graphics g)
    {     
      Point BarPoint1, BarPoint2, RangePoint1, RangePoint2;
      Rectangle ThumbMinRect, ThumbMaxRect;
      if (Orientation == HVOrientation.Horizontal)
      {
        BarPoint1 = new Point(BarPosMin, BarPosY);
        BarPoint2 = new Point(BarPosMax, BarPosY);
        RangePoint1 = new Point(RangeMinPos, BarPosY);
        RangePoint2 = new Point(RangeMaxPos, BarPosY);
        ThumbMinRect = new Rectangle(RangeMinPos - ThumbRadius, BarPosY - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
        ThumbMaxRect = new Rectangle(RangeMaxPos - ThumbRadius, BarPosY - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
      }
      else
      {
        BarPoint1 = new Point(BarPosY, BarPosMin);
        BarPoint2 = new Point(BarPosY, BarPosMax);
        RangePoint1 = new Point(BarPosY, RangeMinPos);
        RangePoint2 = new Point(BarPosY, RangeMaxPos);
        ThumbMinRect = new Rectangle(BarPosY - ThumbRadius, RangeMinPos - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
        ThumbMaxRect = new Rectangle(BarPosY - ThumbRadius, RangeMaxPos - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
      }

      using (Pen barPen = new Pen(barColor) { StartCap = LineCap.Round, EndCap = LineCap.Round, Width = BarThick })
      {
        g.DrawLine(barPen, BarPoint1, BarPoint2);
      }
      using (Pen rangePen = new Pen(thumbColor) { Width = BarThick })
      {
        g.DrawLine(rangePen, RangePoint1, RangePoint2);
      }
      using (Pen thumbPen = new Pen(thumbEdgeColor) { Width = ThumbEdgeWidth })
      {
        using (SolidBrush thumbBrushInner = new SolidBrush(thumbColor))
        {
          DrawRoundShadow(g, ThumbMinRect, ThumbEdgeWidth);
          g.FillEllipse(thumbBrushInner, ThumbMinRect);
          DrawRoundShadow(g, ThumbMaxRect, ThumbEdgeWidth);
          g.FillEllipse(thumbBrushInner, ThumbMaxRect);
        }
      }

      Font font = new Font("Segoe UI", FontSize, FontStyle.Bold, GraphicsUnit.Pixel);
      TextRenderer.DrawText(g, " " + RangeMin.ToString(), font, ThumbMinRect, textColor, System.Drawing.Color.Transparent, GetTextFormatFlags(ContentAlignment.MiddleCenter));
      TextRenderer.DrawText(g, " " + RangeMax.ToString(), font, ThumbMaxRect, textColor, System.Drawing.Color.Transparent, GetTextFormatFlags(ContentAlignment.MiddleCenter));
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
    private float MoveRatio; // 0~1
    private int MaxStartValue;
    private int MaxEndValue;
    private int MinStartValue;
    private int MinEndValue;
    private void MoveTimer_Tick(object sender, EventArgs e)
    {
      MoveRatio += 0.2f;
      if (!IsIn(MoveRatio, 1, 0, true))
      {
        MoveTimer.Stop();
      }
      switch (selectOn)
      {
        case "Max":
          RangeMax = (int)Interpolate(MaxStartValue, MaxEndValue, MoveRatio);
          break;
        case "Min":
          RangeMin = (int)Interpolate(MinStartValue, MinEndValue, MoveRatio);
          break;
        case null:
        case "Range":
          RangeMax = (int)Interpolate(MaxStartValue, MaxEndValue, MoveRatio);
          RangeMin = (int)Interpolate(MinStartValue, MinEndValue, MoveRatio);
          break;
        default:
          break;
      }
      OnValueChanged();
      OnScroll();
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
          delta = (Orientation == HVOrientation.Horizontal) ? 0 : -SmallChange;
          delta = (!Reverse) ? delta : -delta;
          break;
        case Keys.Down:
          delta = (Orientation == HVOrientation.Horizontal) ? 0 : +SmallChange;
          delta = (!Reverse) ? delta : -delta;
          break;
        case Keys.Left:
          delta = (Orientation == HVOrientation.Horizontal) ? -SmallChange : 0;
          delta = (!Reverse) ? delta : -delta;
          break;
        case Keys.Right:
          delta = (Orientation == HVOrientation.Horizontal) ? +SmallChange : 0;
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

      MoveRatio = 0;
      switch (selectOn)
      {
        case "Max":
          MaxStartValue = RangeMax;
          MaxEndValue = RangeMax + delta;
          break;
        case "Min":
          MinStartValue = RangeMin;
          MinEndValue = RangeMin + delta;
          break;
        case "Range":
        case null:
          MaxStartValue = RangeMax;
          MaxEndValue = RangeMax + delta;
          MinStartValue = RangeMin;
          MinEndValue = RangeMin + delta; 
          break;
      }
      MoveTimer.Start();

      OnValueChanged();
      OnScroll();

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
    private string selectOn;
    private int pressX;
    private int mouseX;
    private int disMinMin;
    private int disMaxMax;
    private int disMinPress;
    private int disMaxPress;
    private void Mouse2Value(MouseEventArgs e)
    {
      mouseX = (Orientation == HVOrientation.Horizontal) ? e.Location.X : e.Location.Y;
      switch (selectOn)
      {
        case ("Max"):
          mouseX = Clamp(mouseX, BarPosMax, RangeMinPos + OffsetMaxMinPos);
          RangeMax = Pos2Value(mouseX);
          break;
        case ("Min"):
          mouseX = Clamp(mouseX, RangeMaxPos - OffsetMaxMinPos, BarPosMin);
          RangeMin = Pos2Value(mouseX);
          break;
        case ("Range"):
          mouseX = Clamp(mouseX, pressX + disMaxMax, pressX - disMinMin);
          RangeMin = Pos2Value(mouseX - disMinPress);
          RangeMax = Pos2Value(mouseX + disMaxPress);
          break;
        default:
          break;
      }
    }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!(IsPressed && e.Button == MouseButtons.Left))
                return;

            // --- JAVÍTÁS: KÖZVETLEN EGÉR-KÖVETÉS ANIMÁCIÓS AKADÁS NÉLKÜL ---
            // Ideiglenesen leállítjuk az animációs timert, hogy ne rángassa vissza a gombot
            if (MoveTimer.Enabled)
            {
                MoveTimer.Stop();
            }

            Mouse2Value(e);   // Azonnal átszámolja a gomb új érvényes helyzetét
            OnValueChanged(); // Kiszórja a változás eseményét
            OnScroll();       // Kiszórja a görgetés eseményét
            Invalidate();     // Kényszeríti a Windows-t a csúszka azonnali, sima újrarajzolására
        }


        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            IsPressed = true;

            pressX = (Orientation == HVOrientation.Horizontal) ? e.Location.X : e.Location.Y;
            disMinMin = RangeMinPos - BarPosMin;
            disMaxMax = BarPosMax - RangeMaxPos;
            disMinPress = pressX - RangeMinPos;
            disMaxPress = RangeMaxPos - pressX;

            // Megvizsgáljuk, mit talált el az egér
            if (Math.Abs(disMaxPress) < ThumbRadius)
                selectOn = "Max";
            else if (Math.Abs(disMinPress) < ThumbRadius)
                selectOn = "Min";
            else if (IsIn(pressX, RangeMaxPos, RangeMinPos))
                selectOn = "Range";
            else
                selectOn = null;

            // --- JAVÍTÁS: CSAK AKKOR INDÍTJUK AZ ANIMÁCIÓT, HA A SÁV ÜRES RÉSZÉRE KATTINTOTTUNK ---
            // Ha közvetlenül a gombot fogtuk meg (Min, Max vagy magát a sávtartományt), 
            // NEM indítjuk el a MoveTimer-t, így a gomb mozgása teljesen sima és azonnali lesz húzás közben!
            if (selectOn == "Max" || selectOn == "Min" || selectOn == "Range")
            {
                Mouse2Value(e);
            }
            else
            {
                // --- JAVÍTÁS: RangeSlider specifikus animációs célok beállítása ---
                // Ha az üres sávra kattintunk, eldöntjük, hogy a Min vagy a Max gomb van-e közelebb a kattintáshoz,
                // és azt a gombot úsztatjuk finom animációval a célpozícióra (EndValue helyett a saját változókkal!)
                int clickValue = Pos2Value(pressX);
                MoveRatio = 0;

                if (Math.Abs(clickValue - RangeMax) < Math.Abs(clickValue - RangeMin))
                {
                    selectOn = "Max";
                    MaxStartValue = RangeMax;
                    MaxEndValue = clickValue;
                }
                else
                {
                    selectOn = "Min";
                    MinStartValue = RangeMin;
                    MinEndValue = clickValue;
                }

                MoveTimer.Start();
            }

            Invalidate();
        }


        protected override void OnMouseUp(MouseEventArgs e)
    {
      base.OnMouseUp(e);
      IsPressed = false;

      OnValueChanged();
      //OnScroll();
      Invalidate();
    }

    private int MouseWheelBarPartitions = 20;
    protected override void OnMouseWheel(MouseEventArgs e)
    {
      base.OnMouseWheel(e);
      if (!IsFocus) return;

      int delta = (int)(e.Delta / 60f * ValueLength / MouseWheelBarPartitions);
      switch (selectOn)
      {
        case ("Max"):
          RangeMax = Clamp(RangeMax + delta, BarMax, RangeMin + OffsetMaxMinValue);
          break;
        case ("Min"):
          RangeMin = Clamp(RangeMin + delta, RangeMax - OffsetMaxMinValue, BarMin);
          break;
        case null:
        case ("Range"):
          RangeMax = Clamp(RangeMax + delta, BarMax, RangeMin + OffsetMaxMinValue);
          RangeMin = Clamp(RangeMin + delta, RangeMax - OffsetMaxMinValue, BarMin);
          break;
      }

      OnValueChanged();
      OnScroll();
      Invalidate(); 
    }
    #endregion

    #region Management Methods
    protected override void OnEnabledChanged(EventArgs e)
    {
      base.OnEnabledChanged(e);
      Invalidate();
    }
    #endregion
  }
}
