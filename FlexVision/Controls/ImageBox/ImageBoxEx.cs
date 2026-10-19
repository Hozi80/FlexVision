using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Cyotek.Windows.Forms.Demo
{
  // Cyotek ImageBox
  // Copyright (c) 2010-2014 Cyotek.
  // http://cyotek.com
  // http://cyotek.com/blog/tag/imagebox

  // Licensed under the MIT License. See imagebox-license.txt for the full text.

  // If you use this control in your applications, attribution, donations or contributions are welcome.

  internal class ImageBoxEx : ImageBox
  {
    #region Instance Fields

    private readonly DragHandleCollection _dragHandles;

    private int _dragHandleSize;
        private Size _minimumSelectionSize;

    #endregion

    #region Public Constructors

    public ImageBoxEx()
    {
      _dragHandles = new DragHandleCollection();
      this.DragHandleSize = 10;
      this.MinimumSelectionSize = Size.Empty;
      this.PositionDragHandles();
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the DragHandleSize property value changes
    /// </summary>
    [Category("Property Changed")]
    public event EventHandler DragHandleSizeChanged;

    /// <summary>
    /// Occurs when the MinimumSelectionSize property value changes
    /// </summary>
    [Category("Property Changed")]
    public event EventHandler MinimumSelectionSizeChanged;

    [Category("Action")]
    public event EventHandler SelectionMoved;

    [Category("Action")]
    public event CancelEventHandler SelectionMoving;

    [Category("Action")]
    public event EventHandler SelectionResized;

    [Category("Action")]
    public event CancelEventHandler SelectionResizing;

    #endregion

    #region Overridden Methods

    /// <summary>
    ///   Raises the <see cref="System.Windows.Forms.Control.MouseDown" /> event.
    /// </summary>
    /// <param name="e">
    ///   A <see cref="T:System.Windows.Forms.MouseEventArgs" /> that contains the event data.
    /// </param>
    protected override void OnMouseDown(MouseEventArgs e)
    {
      Point imagePoint;
            PointF rpoint;
            if (this.SelectionMode == ImageBoxSelectionMode.RotatedRectangle)
                rpoint = RotatePointOnSelectionCenter(e.Location, 360 - SelectionRotation);
            else
                rpoint = new PointF(e.Location.X, e.Location.Y);

            imagePoint = this.PointToImage(Point.Round( rpoint));

      if (e.Button == MouseButtons.Left && (this.SelectionRegion.Contains(imagePoint) || this.HitTest(Point.Round(rpoint)) != DragHandleAnchor.None))
      {
        this.DragOrigin = Point.Round(rpoint);
        this.MouseDownOrigin = new Point(e.Location.X, e.Location.Y);
        this.DragOriginPercent = this.InnerRegionPercent;
        this.DragOriginOffset = new Point(imagePoint.X - (int)this.SelectionRegion.X, imagePoint.Y - (int)this.SelectionRegion.Y);
      }
      else
      {
        this.DragOriginOffset = Point.Empty;
        this.DragOrigin = Point.Empty;
 //      if (SelectionRegion == RectangleF.Empty)
  //           this.SelectionRotation = 0;
      }

      base.OnMouseDown(e);
    }

    /// <summary>
    ///   Raises the <see cref="System.Windows.Forms.Control.MouseMove" /> event.
    /// </summary>
    /// <param name="e">
    ///   A <see cref="T:System.Windows.Forms.MouseEventArgs" /> that contains the event data.
    /// </param>
    protected override void OnMouseMove(MouseEventArgs e)
    {
            PointF rpoint;
            if (this.SelectionMode == ImageBoxSelectionMode.RotatedRectangle)
                rpoint = RotatePointOnSelectionCenter(e.Location, 360 - SelectionRotation);
            else
                rpoint = new PointF(e.Location.X, e.Location.Y);

            // start either a move or a resize operation
      if (!this.IsSelecting && !this.IsMoving && !this.IsResizing && e.Button == MouseButtons.Left && !this.DragOrigin.IsEmpty && this.IsOutsideDragZone(Point.Round(rpoint)))
      {
        DragHandleAnchor anchor;

        anchor = this.HitTest(this.DragOrigin);

        if (anchor == DragHandleAnchor.None)
        {
          // move
          this.StartMove();
        }
        else if (this.DragHandles[anchor].Enabled && this.DragHandles[anchor].Visible)
        {
          // resize
          this.StartResize(anchor);
        }
      }
           
      // set the cursor
      this.SetCursor(Point.Round(rpoint));

      // perform operations
      this.ProcessSelectionMove(Point.Round(e.Location));
      this.ProcessSelectionResize(e.Location);

      base.OnMouseMove(e);
    }

    /// <summary>
    ///   Raises the <see cref="System.Windows.Forms.Control.MouseUp" /> event.
    /// </summary>
    /// <param name="e">
    ///   A <see cref="T:System.Windows.Forms.MouseEventArgs" /> that contains the event data.
    /// </param>
    protected override void OnMouseUp(MouseEventArgs e)
    {
      if (this.IsMoving)
      {
        this.CompleteMove();
      }
      else if (this.IsResizing)
      {
        this.CompleteResize();
       }
      
      base.OnMouseUp(e);
    }

    /// <summary>
    ///   Raises the <see cref="System.Windows.Forms.Control.Paint" /> event.
    /// </summary>
    /// <param name="e">
    ///   A <see cref="T:System.Windows.Forms.PaintEventArgs" /> that contains the event data.
    /// </param>
    protected override void OnPaint(PaintEventArgs e)
    {
      base.OnPaint(e);

      if (this.AllowPainting && !this.SelectionRegion.IsEmpty)
      {
        foreach (DragHandle handle in this.DragHandles)
        {
          if (handle.Visible)
          {
            this.DrawDragHandle(e.Graphics, handle);
          }
        }
      }
    }

    /// <summary>
    ///   Raises the <see cref="System.Windows.Forms.Control.Resize" /> event.
    /// </summary>
    /// <param name="e">
    ///   An <see cref="T:System.EventArgs" /> that contains the event data.
    /// </param>
    protected override void OnResize(EventArgs e)
    {
      base.OnResize(e);

      this.PositionDragHandles();
    }

    /// <summary>
    ///   Raises the <see cref="System.Windows.Forms.ScrollableControl.Scroll" /> event.
    /// </summary>
    /// <param name="se">
    ///   A <see cref="T:System.Windows.Forms.ScrollEventArgs" /> that contains the event data.
    /// </param>
    protected override void OnScroll(ScrollEventArgs se)
    {
      base.OnScroll(se);

      this.PositionDragHandles();
    }

    /// <summary>
    ///   Raises the <see cref="ImageBox.Selecting" /> event.
    /// </summary>
    /// <param name="e">
    ///   The <see cref="System.EventArgs" /> instance containing the event data.
    /// </param>
    protected override void OnSelecting(ImageBoxCancelEventArgs e)
    {
            PointF rpoint;
            if (this.SelectionMode == ImageBoxSelectionMode.RotatedRectangle)
                rpoint = RotatePointOnSelectionCenter(e.Location, 360 - SelectionRotation);
            else
                rpoint = new PointF(e.Location.X, e.Location.Y);

            e.Cancel = this.IsMoving || this.IsResizing || this.SelectionRegion.Contains(this.PointToImage(Point.Round(rpoint))) || this.HitTest(Point.Round(rpoint)) != DragHandleAnchor.None;

      base.OnSelecting(e);
    }

    /// <summary>
    ///   Raises the <see cref="ImageBox.SelectionRegionChanged" /> event.
    /// </summary>
    /// <param name="e">
    ///   The <see cref="System.EventArgs" /> instance containing the event data.
    /// </param>
    protected override void OnSelectionRegionChanged(EventArgs e)
    {
      base.OnSelectionRegionChanged(e);
      this.PositionDragHandles();
    }

    /// <summary>
    ///   Raises the <see cref="ImageBox.ZoomChanged" /> event.
    /// </summary>
    /// <param name="e">
    ///   The <see cref="System.EventArgs" /> instance containing the event data.
    /// </param>
    protected override void OnZoomChanged(EventArgs e)
    {
      base.OnZoomChanged(e);

      this.PositionDragHandles();
    }

    /// <summary>
    /// Processes a dialog key.
    /// </summary>
    /// <returns>
    /// true if the key was processed by the control; otherwise, false.
    /// </returns>
    /// <param name="keyData">One of the <see cref="T:System.Windows.Forms.Keys"/> values that represents the key to process. </param>
    protected override bool ProcessDialogKey(Keys keyData)
    {
      bool result;

      if (keyData == Keys.Escape && (this.IsResizing || this.IsMoving))
      {
        if (this.IsResizing)
        {
          this.CancelResize();
        }
        else
        {
          this.CancelMove();
        }

        result = true;
      }
      else
      {
        result = base.ProcessDialogKey(keyData);
      }

      return result;
    }

    #endregion

    #region Public Properties

    [Category("Appearance")]
    [DefaultValue(10)]
    public virtual int DragHandleSize
    {
      get { return _dragHandleSize; }
      set
      {
        if (this.DragHandleSize != value)
        {
          _dragHandleSize = value;

          this.OnDragHandleSizeChanged(EventArgs.Empty);
        }
      }
    }

    [Browsable(false)]
    public DragHandleCollection DragHandles
    {
      get { return _dragHandles; }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsMoving { get; protected set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsResizing { get; protected set; }



    [Category("Behavior")]
    [DefaultValue(typeof(Size), "0, 0")]
    public virtual Size MinimumSelectionSize
    {
      get { return _minimumSelectionSize; }
      set
      {
        if (this.MinimumSelectionSize != value)
        {
          _minimumSelectionSize = value;

          this.OnMinimumSelectionSizeChanged(EventArgs.Empty);
        }
      }
    }

    [Browsable(false)]
    public RectangleF PreviousSelectionRegion { get; protected set; }
    public PointF PreviousSelectionCenter { get; protected set; }
        #endregion

        #region Protected Properties

        protected Point DragOrigin { get; set; }

        protected Point MouseDownOrigin { get; set; }
        protected double DragOriginPercent { get; set; }
        protected Point DragOriginOffset { get; set; }

    protected DragHandleAnchor ResizeAnchor { get; set; }

    #endregion

    #region Public Members

    public void CancelResize()
    {
      this.SelectionRegion = this.PreviousSelectionRegion;
            this.SelectionCenter = this.PreviousSelectionCenter;
            this.CompleteResize();
    }

    public void StartMove()
    {
      CancelEventArgs e;

      if (this.IsMoving || this.IsResizing)
      {
        throw new InvalidOperationException("A move or resize action is currently being performed.");
      }

      e = new CancelEventArgs();

      this.OnSelectionMoving(e);

      if (!e.Cancel)
      {
        this.PreviousSelectionRegion = this.SelectionRegion;
                this.PreviousSelectionCenter = this.SelectionCenter;
                this.IsMoving = true;
      }
    }

    #endregion

    #region Protected Members

    protected virtual void DrawDragHandle(Graphics graphics, DragHandle handle)
    {
      int left;
      int top;
      int width;
      int height;
      Pen outerPen;
      Brush innerBrush;

      left = handle.Bounds.Left;
      top = handle.Bounds.Top;
      width = handle.Bounds.Width;
      height = handle.Bounds.Height;
 //     RectangleF rect = this.GetOffsetRectangle(this.SelectionRegion);

            if (handle.Enabled)
      {
                if (this.SelectionMode == ImageBoxSelectionMode.RotatedRectangle)
                {
                    var mat = new Matrix();
//                    mat.RotateAt(this.SelectionRotation,  new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
                    mat.RotateAt(this.SelectionRotation, this.GetOffsetPoint(this.SelectionCenter));

                    graphics.Transform = mat;
                }

                if (handle.Anchor == DragHandleAnchor.Rotate)
                {
                    outerPen = Pens.OrangeRed;
                    innerBrush = Brushes.Red;
                    graphics.FillEllipse(innerBrush, left + 1, top + 1, width - 2, height - 2);
                    graphics.DrawEllipse(outerPen, left + 1, top + 1, width - 2, height - 2);
                }
               else if (handle.Anchor == DragHandleAnchor.Inner)
                {
                    outerPen = Pens.Yellow;
                    innerBrush = Brushes.Orange; // SystemBrushes.ControlDarkDark;
                     graphics.FillEllipse(innerBrush, left + 1, top + 1, width - 2, height - 2);
                     graphics.DrawEllipse(outerPen, left + 1, top + 1, width - 2, height - 2);

                }
                else
                {
                    outerPen = SystemPens.WindowFrame;
                    innerBrush = SystemBrushes.Window;
                    graphics.FillRectangle(innerBrush, left + 1, top + 1, width - 2, height - 2);
                    graphics.DrawRectangle(outerPen, left + 1, top + 1, width - 2, height - 2);
                }

            }
            else
             {
             outerPen = SystemPens.ControlDark;
             innerBrush = SystemBrushes.Control;


            }


            graphics.ResetTransform();

            /*      graphics.FillRectangle(innerBrush, left + 1, top + 1, width - 2, height - 2);
                  graphics.DrawLine(outerPen, left + 1, top, left + width - 2, top);
                  graphics.DrawLine(outerPen, left, top + 1, left, top + height - 2);
                  graphics.DrawLine(outerPen, left + 1, top + height - 1, left + width - 2, top + height - 1);
                  graphics.DrawLine(outerPen, left + width - 1, top + 1, left + width - 1, top + height - 2);
              */

//            graphics.FillRectangle(innerBrush, left + 1, top + 1, width - 2, height - 2);
 //           graphics.DrawRectangle(outerPen, left + 1, top + 1, width - 2, height - 2);


           // graphics.FillEllipse(innerBrush, left + 1, top + 1, width - 2, height - 2);
            //graphics.DrawEllipse(outerPen, left + 1, top + 1, width - 2, height - 2);
    //        graphics.DrawString(handle.Anchor.ToString(), new Font("Arial", 5), innerBrush, left, top);
    }

    /// <summary>
    /// Raises the <see cref="DragHandleSizeChanged" /> event.
    /// </summary>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    protected virtual void OnDragHandleSizeChanged(EventArgs e)
    {
      EventHandler handler;

      this.PositionDragHandles();
      this.Invalidate();

      handler = this.DragHandleSizeChanged;

      if (handler != null)
      {
        handler(this, e);
      }
    }

    /// <summary>
    /// Raises the <see cref="MinimumSelectionSizeChanged" /> event.
    /// </summary>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    protected virtual void OnMinimumSelectionSizeChanged(EventArgs e)
    {
      EventHandler handler;

      handler = this.MinimumSelectionSizeChanged;

      if (handler != null)
      {
        handler(this, e);
      }
    }

    /// <summary>
    /// Raises the <see cref="SelectionMoved" /> event.
    /// </summary>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    protected virtual void OnSelectionMoved(EventArgs e)
    {
      EventHandler handler;

      handler = this.SelectionMoved;

      if (handler != null)
      {
        handler(this, e);
      }
    }

    /// <summary>
    /// Raises the <see cref="SelectionMoving" /> event.
    /// </summary>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    protected virtual void OnSelectionMoving(CancelEventArgs e)
    {
      CancelEventHandler handler;

      handler = this.SelectionMoving;

      if (handler != null)
      {
        handler(this, e);
      }
    }

    /// <summary>
    /// Raises the <see cref="SelectionResized" /> event.
    /// </summary>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    protected virtual void OnSelectionResized(EventArgs e)
    {
      EventHandler handler;

      handler = this.SelectionResized;

      if (handler != null)
      {
        handler(this, e);
      }
    }

    /// <summary>
    /// Raises the <see cref="SelectionResizing" /> event.
    /// </summary>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    protected virtual void OnSelectionResizing(CancelEventArgs e)
    {
      CancelEventHandler handler;

      handler = this.SelectionResizing;

      if (handler != null)
      {
        handler(this, e);
      }
    }

    #endregion

    #region Private Members

    private void CancelMove()
    {
      this.SelectionRegion = this.PreviousSelectionRegion;
            this.SelectionCenter = this.PreviousSelectionCenter;
            this.CompleteMove();
    }

    private void CompleteMove()
    {
      this.ResetDrag();
      this.OnSelectionMoved(EventArgs.Empty);
    }

    private void CompleteResize()
    {
      this.ResetDrag();
      this.OnSelectionResized(EventArgs.Empty);
    }

    private DragHandleAnchor HitTest(Point cursorPosition)
    {
      return this.DragHandles.HitTest(cursorPosition);
    }

    private bool IsOutsideDragZone(Point location)
    {
      Rectangle dragZone;
      int dragWidth;
      int dragHeight;

      dragWidth = SystemInformation.DragSize.Width;
      dragHeight = SystemInformation.DragSize.Height;
      dragZone = new Rectangle(this.DragOrigin.X - (dragWidth / 2), this.DragOrigin.Y - (dragHeight / 2), dragWidth, dragHeight);

      return !dragZone.Contains(location);
    }

    public void PositionDragHandles()
    {
      if (this.DragHandles != null && this.DragHandleSize > 0)
      {
        if (this.SelectionRegion.IsEmpty)
        {
          foreach (DragHandle handle in this.DragHandles)
          {
            handle.Bounds = Rectangle.Empty;
          }
        }
        else
        {
          int left;
          int top;
          int right;
          int innerright;
          int bottom;
          int halfWidth;
          int halfHeight;
          int halfDragHandleSize;
          Rectangle viewport;
          int offsetX;
          int offsetY;

          viewport = this.GetImageViewPort();
          offsetX = viewport.Left + this.Padding.Left + this.AutoScrollPosition.X;
          offsetY = viewport.Top + this.Padding.Top + this.AutoScrollPosition.Y;
          halfDragHandleSize = this.DragHandleSize / 2;


                    if (this.SelectionMode != ImageBoxSelectionMode.RotatedRectangle)
                    {
                        left = Convert.ToInt32((this.SelectionRegion.Left * this.ZoomFactor) + offsetX);
                        top = Convert.ToInt32((this.SelectionRegion.Top * this.ZoomFactor) + offsetY);
                        right = left + Convert.ToInt32(this.SelectionRegion.Width * this.ZoomFactor);
                        innerright = left + Convert.ToInt32(this.SelectionRegion.Width * InnerRegionPercent * this.ZoomFactor + (this.SelectionRegion.Width * this.ZoomFactor - (this.SelectionRegion.Width * InnerRegionPercent * this.ZoomFactor)) / 2);
                        bottom = top + Convert.ToInt32(this.SelectionRegion.Height * this.ZoomFactor);
                        halfWidth = Convert.ToInt32(this.SelectionRegion.Width * this.ZoomFactor) / 2;
                        halfHeight = Convert.ToInt32(this.SelectionRegion.Height * this.ZoomFactor) / 2;
                    }
                    else
                    {

                        left = Convert.ToInt32(((this.SelectionCenter.X - this.SelectionRegion.Width / 2) * this.ZoomFactor) + offsetX);
                        top = Convert.ToInt32(((this.SelectionCenter.Y - this.SelectionRegion.Height / 2) * this.ZoomFactor) + offsetY);
                        right = Convert.ToInt32(((this.SelectionCenter.X + this.SelectionRegion.Width / 2) * this.ZoomFactor) + offsetX);
                        bottom = top + Convert.ToInt32(this.SelectionRegion.Height * this.ZoomFactor);
                        halfWidth = Convert.ToInt32(this.SelectionRegion.Width * this.ZoomFactor) / 2;
                        halfHeight = Convert.ToInt32(this.SelectionRegion.Height * this.ZoomFactor) / 2;
                        innerright = left + Convert.ToInt32(this.SelectionRegion.Width * InnerRegionPercent * this.ZoomFactor + (this.SelectionRegion.Width * this.ZoomFactor - (this.SelectionRegion.Width * InnerRegionPercent * this.ZoomFactor)) / 2);

                    }


                    this.DragHandles[DragHandleAnchor.TopLeft].Bounds = new Rectangle(left - this.DragHandleSize, top - this.DragHandleSize, this.DragHandleSize, this.DragHandleSize);
                        this.DragHandles[DragHandleAnchor.TopCenter].Bounds = new Rectangle(left + halfWidth - halfDragHandleSize, top - this.DragHandleSize, this.DragHandleSize, this.DragHandleSize);
                        this.DragHandles[DragHandleAnchor.TopRight].Bounds = new Rectangle(right, top - this.DragHandleSize, this.DragHandleSize, this.DragHandleSize);
                        this.DragHandles[DragHandleAnchor.MiddleLeft].Bounds = new Rectangle(left - this.DragHandleSize, top + halfHeight - halfDragHandleSize, this.DragHandleSize, this.DragHandleSize);
                        this.DragHandles[DragHandleAnchor.MiddleRight].Bounds = new Rectangle(right, top + halfHeight - halfDragHandleSize, this.DragHandleSize, this.DragHandleSize);
                        this.DragHandles[DragHandleAnchor.BottomLeft].Bounds = new Rectangle(left - this.DragHandleSize, bottom, this.DragHandleSize, this.DragHandleSize);
                        this.DragHandles[DragHandleAnchor.BottomCenter].Bounds = new Rectangle(left + halfWidth - halfDragHandleSize, bottom, this.DragHandleSize, this.DragHandleSize);
                        this.DragHandles[DragHandleAnchor.BottomRight].Bounds = new Rectangle(right, bottom, this.DragHandleSize, this.DragHandleSize);
                        this.DragHandles[DragHandleAnchor.Inner].Bounds = new Rectangle(innerright - (int)(this.DragHandleSize * 0.75), top + halfHeight - halfDragHandleSize, (int)(this.DragHandleSize * 1.5), (int)(this.DragHandleSize * 1.5));
                        this.DragHandles[DragHandleAnchor.Rotate].Bounds = new Rectangle(right + this.DragHandleSize * 3, top + halfHeight - halfDragHandleSize, (int)(this.DragHandleSize * 1.5), (int)(this.DragHandleSize * 1.5));

                    

                }
            }
    }



 

  private void ProcessSelectionMove(Point cursorPosition)
    {
      if (this.IsMoving)
      {
        int x;
        int y;
        Point imagePoint;

        imagePoint = this.PointToImage(cursorPosition, true);

        x = Math.Max(0, imagePoint.X - this.DragOriginOffset.X);
        if (x + this.SelectionRegion.Width >= this.ViewSize.Width)
        {
          x = this.ViewSize.Width - (int)this.SelectionRegion.Width;
        }

        y = Math.Max(0, imagePoint.Y - this.DragOriginOffset.Y);
        if (y + this.SelectionRegion.Height >= this.ViewSize.Height)
        {
          y = this.ViewSize.Height - (int)this.SelectionRegion.Height;
        }

                this.SelectionCenter = new PointF(this.SelectionCenter.X - (this.SelectionRegion.X - x), this.SelectionCenter.Y - (this.SelectionRegion.Y - y));

                this.SelectionRegion = new RectangleF(x, y, this.SelectionRegion.Width, this.SelectionRegion.Height);


            }
    }

    private void ProcessSelectionResize(Point cursorPosition)
    {
      if (this.IsResizing)
      {
        Point imagePosition;
        float left;
        float top;
        float right;
        float bottom;
                float width;
                float height;
        bool resizingTopEdge;
        bool resizingBottomEdge;
        bool resizingLeftEdge;
        bool resizingRightEdge;

        imagePosition = this.PointToImage(cursorPosition, true);

/*
               RectangleF rect = this.GetOffsetRectangle(this.SelectionRegion);
                var mat = new Matrix();
                mat.RotateAt(SelectionRotation, new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
//                mat.Translate(_rectPos.X, _rectPos.Y);
 //               mat.Rotate(_rectRotation);
                mat.Invert();

                imagePosition = Point.Round( mat.TransformPoint(new PointF(imagePosition.X, imagePosition.Y)));
                */

                // get the current selection


                if (this.SelectionMode != ImageBoxSelectionMode.RotatedRectangle)
                {
                    left = this.SelectionRegion.Left;
                    top = this.SelectionRegion.Top;
                    right = this.SelectionRegion.Right;
                    bottom = this.SelectionRegion.Bottom;

                    


                    // decide which edges we're resizing
                    resizingTopEdge = this.ResizeAnchor >= DragHandleAnchor.TopLeft && this.ResizeAnchor <= DragHandleAnchor.TopRight;
                    resizingBottomEdge = this.ResizeAnchor >= DragHandleAnchor.BottomLeft && this.ResizeAnchor <= DragHandleAnchor.BottomRight;
                    resizingLeftEdge = this.ResizeAnchor == DragHandleAnchor.TopLeft || this.ResizeAnchor == DragHandleAnchor.MiddleLeft || this.ResizeAnchor == DragHandleAnchor.BottomLeft;
                    resizingRightEdge = this.ResizeAnchor == DragHandleAnchor.TopRight || this.ResizeAnchor == DragHandleAnchor.MiddleRight || this.ResizeAnchor == DragHandleAnchor.BottomRight;

                    // and resize!
                    if (resizingTopEdge)
                    {
                        top = imagePosition.Y;
                        if (bottom - top < this.MinimumSelectionSize.Height)
                        {
                            top = bottom - this.MinimumSelectionSize.Height;
                        }
                    }
                    else if (resizingBottomEdge)
                    {
                        bottom = imagePosition.Y;
                        if (bottom - top < this.MinimumSelectionSize.Height)
                        {
                            bottom = top + this.MinimumSelectionSize.Height;
                        }
                    }

                    if (resizingLeftEdge)
                    {
                        left = imagePosition.X;
                        if (right - left < this.MinimumSelectionSize.Width)
                        {
                            left = right - this.MinimumSelectionSize.Width;
                        }
                    }
                    else if (resizingRightEdge)
                    {
                        right = imagePosition.X;
                        if (right - left < this.MinimumSelectionSize.Width)
                        {
                            right = left + this.MinimumSelectionSize.Width;
                        }
                    }
                    else if (this.ResizeAnchor == DragHandleAnchor.Inner)
                    {

                        float diff = imagePosition.X - this.PointToImage(this.DragOrigin).X;
                        // bal - csokken +
                        // jobb - no -
                        double ratio = (this.SelectionRegion.Width * DragOriginPercent + diff) / this.SelectionRegion.Width;
                        //      MessageBox.Show(diff + " - " + ratio);
                        if (ratio < 0.1)
                            ratio = 0.1;
                        else if (ratio > 0.9)
                            ratio = 0.9;

                        InnerRegionPercent = ratio;
                        PositionDragHandles();
                        this.Invalidate();

                    }
                  
                    width = right - left;
                    height = bottom - top;

                    this.SelectionRegion = new RectangleF(left, top, width, height);
                }
                else
                {
/*
                    left = this.SelectionRegion.Left;
                    top = this.SelectionRegion.Top;
                    width = this.SelectionRegion.Width;
                    height = this.SelectionRegion.Height;
                    */
                    left = this.PreviousSelectionRegion.Left;
                    top = this.PreviousSelectionRegion.Top;
                    width = this.PreviousSelectionRegion.Width;
                    height = this.PreviousSelectionRegion.Height;
            //       PointF OldCenter = this.PreviousSelectionCenter;

                    

                    PointF tDest = new PointF();
                    PointF _ptMouseDown = new PointF(this.MouseDownOrigin.X, this.MouseDownOrigin.Y);

                    tDest = RotatePoint(cursorPosition, _ptMouseDown, 360 -SelectionRotation);
                    PointF tPt = new PointF((tDest.X - _ptMouseDown.X), (tDest.Y - _ptMouseDown.Y));
                    PointF NewCenter = new PointF( this.PreviousSelectionCenter.X , this.PreviousSelectionCenter.Y);


                //    PointF tPtImage = this.PointToImage(tPt.X, tPt.Y);
                    PointF tPtImage =  new PointF((cursorPosition.X - _ptMouseDown.X), (cursorPosition.Y - _ptMouseDown.Y));
                    tPtImage = new PointF((float)(tPtImage.X / this.ZoomFactor), (float)(tPtImage.Y / this.ZoomFactor));
                    tPt = new PointF((float)(tPt.X / this.ZoomFactor), (float)(tPt.Y / this.ZoomFactor));

                    //     PointF tPtImage = this.PointToImage((cursorPosition.X - _ptMouseDown.X), (cursorPosition.Y - _ptMouseDown.Y));
                    /*                                PointF tDest = new PointF();
                                                    PointF _ptMouseDown = new PointF(this.PointToImage(this.MouseDownOrigin).X, this.PointToImage(this.MouseDownOrigin).Y);

                                                    tDest = RotatePoint(imagePosition, _ptMouseDown, 360 - SelectionRotation);
                                                    PointF tPt = new PointF((tDest.X - _ptMouseDown.X), (tDest.Y - _ptMouseDown.Y));
                                */

                    //    this.SelectionCenter = RotatePoint(tPt, this.SelectionCenter, 360 - SelectionRotation);

                    switch (this.ResizeAnchor)
                    {


                        case DragHandleAnchor.Rotate:
//                            this.SelectionRotation = (int)TwoPointAngle(Point.Round(new PointF(this.SelectionRegion.X + this.SelectionRegion.Width / 2, this.SelectionRegion.Y + this.SelectionRegion.Height / 2)), imagePosition);

                            this.SelectionRotation = (int)TwoPointAngle(Point.Round(this.SelectionCenter), imagePosition);
                            tPtImage = new PointF(0,0);
                            //                           PositionDragHandles();
                            break;

                        case DragHandleAnchor.MiddleRight:
                            width += tPt.X;
                            //                  NewCenter.X = NewCenter.X +  tPtImage.X / 2;
      //                      tPtImage.Y = 0;
                            break;

                        case DragHandleAnchor.MiddleLeft:
                            left += tPt.X;
                            width -= tPt.X;
                            //                    NewCenter.X = NewCenter.X - tPtImage.X / 2 ;
  //                          tPtImage.Y = 0;
                            break;

                        case DragHandleAnchor.TopCenter:
                            top += tPt.Y;
                            height -= tPt.Y;
       //                     NewCenter.Y = NewCenter.Y - tPtImage.Y / 2;
                            break;

                        case DragHandleAnchor.BottomCenter:
                            height += tPt.Y;
      //                      NewCenter.Y = NewCenter.Y - tPtImage.Y / 2;
                            break;



                        case DragHandleAnchor.TopLeft:
                            left += tPt.X;
                            width -= tPt.X;
                            top += tPt.Y;
                            height -= tPt.Y;
                            break;


                        case DragHandleAnchor.TopRight:

                            width += tPt.X;
                            height -= tPt.Y;
                            top += tPt.Y;
                            break;

                        case DragHandleAnchor.BottomRight:
                            width += tPt.X;
                            height += tPt.Y;

                            break;

                        case DragHandleAnchor.BottomLeft:
                            left += tPt.X;
                            width -= tPt.X;
                            height += tPt.Y;

                            break;

                    }
  
       //             this.SelectionCenter = new PointF(NewCenter.X,NewCenter.Y);

                   this.SelectionCenter = new PointF((float)(this.PreviousSelectionCenter.X + tPtImage.X /2), (float)(this.PreviousSelectionCenter.Y + tPtImage.Y / 2));
//                    this.SelectionRegion = new RectangleF(left, top, width, height);
                    this.SelectionRegion = new RectangleF(this.SelectionCenter.X - width /2, this.SelectionCenter.Y - height / 2, width, height);
                    PositionDragHandles();
                }
          }

    }



        private PointF RotatePoint(PointF pointToRotate , PointF centerPoint , double angleInDegrees)
        {
            double angleInRadians = angleInDegrees * (Math.PI / 180);
            double cosTheta  = Math.Cos(angleInRadians);
            double sinTheta  = Math.Sin(angleInRadians);
        return new PointF((float)(cosTheta * (pointToRotate.X - centerPoint.X) - sinTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.X), (float)(sinTheta * (pointToRotate.X - centerPoint.X) + cosTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.Y));
        }

        public PointF PrevSelectionCenter()
        {
            RectangleF rect = this.PreviousSelectionRegion;
            PointF centerPoint = new PointF(this.PreviousSelectionRegion.X + this.PreviousSelectionRegion.Width / 2, this.PreviousSelectionRegion.Y + this.PreviousSelectionRegion.Height / 2);
            return centerPoint;
        }

        private void ResetDrag()
    {
      this.IsResizing = false;
      this.IsMoving = false;
      this.DragOrigin = Point.Empty;
      this.DragOriginOffset = Point.Empty;
    }

    private void SetCursor(Point point)
    {
      Cursor cursor;

            PointF rpoint;

                rpoint = new PointF(point.X, point.Y);

      if (this.IsSelecting)
      {
        cursor = Cursors.Default;
      }
      else
      {
        DragHandleAnchor handleAnchor;

        handleAnchor = this.IsResizing ? this.ResizeAnchor : this.HitTest(Point.Round( rpoint));
        if (handleAnchor != DragHandleAnchor.None && this.DragHandles[handleAnchor].Enabled)
        {
          switch (handleAnchor)
          {
            case DragHandleAnchor.TopLeft:
            case DragHandleAnchor.BottomRight:
              cursor = Cursors.SizeNWSE;
              break;
            case DragHandleAnchor.TopCenter:
            case DragHandleAnchor.BottomCenter:
              cursor = Cursors.SizeNS;
              break;
            case DragHandleAnchor.TopRight:
            case DragHandleAnchor.BottomLeft:
              cursor = Cursors.SizeNESW;
              break;
            case DragHandleAnchor.MiddleLeft:
            case DragHandleAnchor.MiddleRight:
            case DragHandleAnchor.Inner:
              cursor = Cursors.SizeWE;
              break;
            case DragHandleAnchor.Rotate:
             cursor = Cursors.Hand;
              break;
                        default:
              throw new ArgumentOutOfRangeException();
          }
        }
        else if (this.IsMoving || this.SelectionRegion.Contains(this.PointToImage(point)))
        {
          cursor = Cursors.SizeAll;
        }
        else
        {
          cursor = Cursors.Default;
        }
      }

      this.Cursor = cursor;
    }

    private void StartResize(DragHandleAnchor anchor)
    {
      CancelEventArgs e;

      if (this.IsMoving || this.IsResizing)
      {
        throw new InvalidOperationException("A move or resize action is currently being performed.");
      }

      e = new CancelEventArgs();

      this.OnSelectionResizing(e);

      if (!e.Cancel)
      {
        this.ResizeAnchor = anchor;
        this.PreviousSelectionRegion = this.SelectionRegion;
        this.PreviousSelectionCenter = this.SelectionCenter;

                this.IsResizing = true;
      }
    }

        #endregion

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ResumeLayout(false);

        }
    }
}
