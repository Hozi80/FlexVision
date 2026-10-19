namespace Cyotek.Windows.Forms
{
  // Cyotek ImageBox
  // Copyright (c) 2010-2015 Cyotek Ltd.
  // http://cyotek.com
  // http://cyotek.com/blog/tag/imagebox

  // Licensed under the MIT License. See license.txt for the full text.

  // If you use this control in your applications, attribution, donations or contributions are welcome.

  /// <summary>
  ///   Specifies the selection mode.
  /// </summary>
  public enum ImageBoxSelectionMode
  {
    /// <summary>
    ///   No selection.
    /// </summary>
    None,

    /// <summary>
    ///   Rectangle selection.
    /// </summary>
    Rectangle,

        /// <summary>
        ///   Rectangle selection.
        /// </summary>
      RotatedRectangle,


        /// <summary>
        ///   Circle selection.
        /// </summary>
        Circle,

        /// <summary>
        ///   Ring selection.
        /// </summary>
        Ring,

        /// <summary>
        ///   Zoom selection.
        /// </summary>
        Zoom
    }
}
