using System.Collections;
using System.Collections.Generic;
using System.Drawing;

namespace Cyotek.Windows.Forms.Demo
{
  // Cyotek ImageBox
  // Copyright (c) 2010-2014 Cyotek.
  // http://cyotek.com
  // http://cyotek.com/blog/tag/imagebox

  // Licensed under the MIT License. See imagebox-license.txt for the full text.

  // If you use this control in your applications, attribution, donations or contributions are welcome.

  internal class DragHandleCollection : IEnumerable<DragHandle>
  {
    #region Instance Fields

    private readonly IDictionary<DragHandleAnchor, DragHandle> _items;

    #endregion

    #region Public Constructors

    public DragHandleCollection()
    {
      _items = new Dictionary<DragHandleAnchor, DragHandle>();
      _items.Add(DragHandleAnchor.TopLeft, new DragHandle(DragHandleAnchor.TopLeft));
      _items.Add(DragHandleAnchor.TopCenter, new DragHandle(DragHandleAnchor.TopCenter));
      _items.Add(DragHandleAnchor.TopRight, new DragHandle(DragHandleAnchor.TopRight));
      _items.Add(DragHandleAnchor.MiddleLeft, new DragHandle(DragHandleAnchor.MiddleLeft));
      _items.Add(DragHandleAnchor.MiddleRight, new DragHandle(DragHandleAnchor.MiddleRight));
      _items.Add(DragHandleAnchor.BottomLeft, new DragHandle(DragHandleAnchor.BottomLeft));
      _items.Add(DragHandleAnchor.BottomCenter, new DragHandle(DragHandleAnchor.BottomCenter));
      _items.Add(DragHandleAnchor.BottomRight, new DragHandle(DragHandleAnchor.BottomRight));
      _items.Add(DragHandleAnchor.Rotate, new DragHandle(DragHandleAnchor.Rotate));
      _items.Add(DragHandleAnchor.Inner, new DragHandle(DragHandleAnchor.Inner));
        }

    #endregion

    #region Public Properties

       public void SetCorners()
        {
            _items[DragHandleAnchor.TopLeft].Visible = true;
            _items[DragHandleAnchor.TopRight].Visible = true;
            _items[DragHandleAnchor.BottomLeft].Visible = true;
            _items[DragHandleAnchor.BottomRight].Visible = true;
            _items[DragHandleAnchor.BottomCenter].Visible = false;
            _items[DragHandleAnchor.TopCenter].Visible = false;
            _items[DragHandleAnchor.MiddleLeft].Visible = false;
            _items[DragHandleAnchor.MiddleRight].Visible = false;
            _items[DragHandleAnchor.Rotate].Visible = false;
            _items[DragHandleAnchor.Inner].Visible = false;
        }

        public void SetCornersAndRotation()
        {
            _items[DragHandleAnchor.TopLeft].Visible = true;
            _items[DragHandleAnchor.TopRight].Visible = true;
            _items[DragHandleAnchor.BottomLeft].Visible = true;
            _items[DragHandleAnchor.BottomRight].Visible = true;
            _items[DragHandleAnchor.BottomCenter].Visible = false;
            _items[DragHandleAnchor.TopCenter].Visible = false;
            _items[DragHandleAnchor.MiddleLeft].Visible = false;
            _items[DragHandleAnchor.MiddleRight].Visible = false;
            _items[DragHandleAnchor.Rotate].Visible = true;
            _items[DragHandleAnchor.Inner].Visible = false;
        }

        public void SetMiddlesAndRotation()
        {
            _items[DragHandleAnchor.TopLeft].Visible = false;
            _items[DragHandleAnchor.TopRight].Visible = false;
            _items[DragHandleAnchor.BottomLeft].Visible = false;
            _items[DragHandleAnchor.BottomRight].Visible = false;
            _items[DragHandleAnchor.BottomCenter].Visible = true;
            _items[DragHandleAnchor.TopCenter].Visible = true;
            _items[DragHandleAnchor.MiddleLeft].Visible = true;
            _items[DragHandleAnchor.MiddleRight].Visible = true;
            _items[DragHandleAnchor.Rotate].Visible = true;
            _items[DragHandleAnchor.Inner].Visible = false;
        }


        public void SetAllMinusRotation()
        {
            _items[DragHandleAnchor.TopLeft].Visible = true;
            _items[DragHandleAnchor.TopRight].Visible = true;
            _items[DragHandleAnchor.BottomLeft].Visible = true;
            _items[DragHandleAnchor.BottomRight].Visible = true;
            _items[DragHandleAnchor.BottomCenter].Visible = true;
            _items[DragHandleAnchor.TopCenter].Visible = true;
            _items[DragHandleAnchor.MiddleLeft].Visible = true;
            _items[DragHandleAnchor.MiddleRight].Visible = true;
            _items[DragHandleAnchor.Rotate].Visible = false;
            _items[DragHandleAnchor.Inner].Visible = true;
        }

        public void SetAllMinusRotationAndInner()
        {
            _items[DragHandleAnchor.TopLeft].Visible = true;
            _items[DragHandleAnchor.TopRight].Visible = true;
            _items[DragHandleAnchor.BottomLeft].Visible = true;
            _items[DragHandleAnchor.BottomRight].Visible = true;
            _items[DragHandleAnchor.BottomCenter].Visible = true;
            _items[DragHandleAnchor.TopCenter].Visible = true;
            _items[DragHandleAnchor.MiddleLeft].Visible = true;
            _items[DragHandleAnchor.MiddleRight].Visible = true;
            _items[DragHandleAnchor.Rotate].Visible = false;
            _items[DragHandleAnchor.Inner].Visible = false;
        }

        public void SetAllMinusInner()
        {
            _items[DragHandleAnchor.TopLeft].Visible = true;
            _items[DragHandleAnchor.TopRight].Visible = true;
            _items[DragHandleAnchor.BottomLeft].Visible = true;
            _items[DragHandleAnchor.BottomRight].Visible = true;
            _items[DragHandleAnchor.BottomCenter].Visible = true;
            _items[DragHandleAnchor.TopCenter].Visible = true;
            _items[DragHandleAnchor.MiddleLeft].Visible = true;
            _items[DragHandleAnchor.MiddleRight].Visible = true;
            _items[DragHandleAnchor.Rotate].Visible = true;
            _items[DragHandleAnchor.Inner].Visible = false;
        }


        public void SetNone()
        {
            _items[DragHandleAnchor.TopLeft].Visible = false;
            _items[DragHandleAnchor.TopRight].Visible = false;
            _items[DragHandleAnchor.BottomLeft].Visible = false;
            _items[DragHandleAnchor.BottomRight].Visible = false;
            _items[DragHandleAnchor.BottomCenter].Visible = false;
            _items[DragHandleAnchor.TopCenter].Visible = false;
            _items[DragHandleAnchor.MiddleLeft].Visible = false;
            _items[DragHandleAnchor.MiddleRight].Visible = false;
            _items[DragHandleAnchor.Rotate].Visible = false;
            _items[DragHandleAnchor.Inner].Visible = false;
        }


        public int Count
    {
      get { return _items.Count; }
    }

    public DragHandle this[DragHandleAnchor index]
    {
      get { return _items[index]; }
    }

    #endregion

    #region Public Members

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    /// <returns>
    /// A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
    /// </returns>
    public IEnumerator<DragHandle> GetEnumerator()
    {
      return _items.Values.GetEnumerator();
    }

    public DragHandleAnchor HitTest(Point point)
    {
      DragHandleAnchor result;

      result = DragHandleAnchor.None;

      foreach (DragHandle handle in this)
      {
        if (handle.Visible && handle.Bounds.Contains(point))
        {
          result = handle.Anchor;
          break;
        }
      }

      return result;
    }

    #endregion

    #region IEnumerable<DragHandle> Members

    /// <summary>
    /// Returns an enumerator that iterates through a collection.
    /// </summary>
    /// <returns>
    /// An <see cref="T:System.Collections.IEnumerator"/> object that can be used to iterate through the collection.
    /// </returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
      return this.GetEnumerator();
    }

    #endregion
  }
}
