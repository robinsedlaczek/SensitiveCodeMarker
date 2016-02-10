﻿//------------------------------------------------------------------------------
// <copyright file="ViewportAdornment.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Editor;

namespace WaveDev.SensitivCodeMarker.Adornments
{
    /// <summary>
    /// Adornment class that draws a square box in the top right hand corner of the viewport
    /// </summary>
    internal sealed class ViewportAdornment
    {
        /// <summary>
        /// The width of the square box.
        /// </summary>
        private const double AdornmentWidth = 30;

        /// <summary>
        /// The height of the square box.
        /// </summary>
        private const double AdornmentHeight = 30;

        /// <summary>
        /// Distance from the viewport top to the top of the square box.
        /// </summary>
        private const double TopMargin = 30;

        /// <summary>
        /// Distance from the viewport right to the right end of the square box.
        /// </summary>
        private const double RightMargin = 30;

        /// <summary>
        /// Text view to add the adornment on.
        /// </summary>
        private readonly IWpfTextView _view;

        /// <summary>
        /// Adornment image
        /// </summary>
        private readonly Image _image;

        /// <summary>
        /// The layer for the adornment.
        /// </summary>
        private readonly IAdornmentLayer _adornmentLayer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ViewportAdornment"/> class.
        /// Creates a square image and attaches an event handler to the layout changed event that
        /// adds the the square in the upper right-hand corner of the TextView via the adornment layer
        /// </summary>
        /// <param name="view">The <see cref="IWpfTextView"/> upon which the adornment will be drawn</param>
        public ViewportAdornment(IWpfTextView view)
        {
            if (view == null)
            {
                throw new ArgumentNullException("view");
            }

            _view = view;

            var brush = new SolidColorBrush(Colors.BlueViolet);
            brush.Freeze();
            var penBrush = new SolidColorBrush(Colors.Red);
            penBrush.Freeze();
            var pen = new Pen(penBrush, 0.5);
            pen.Freeze();

            // Draw a square with the created brush and pen
            System.Windows.Rect r = new System.Windows.Rect(0, 0, AdornmentWidth, AdornmentHeight);
            var geometry = new RectangleGeometry(r);

            var drawing = new GeometryDrawing(brush, pen, geometry);
            drawing.Freeze();

            var drawingImage = new DrawingImage(drawing);
            drawingImage.Freeze();

            _image = new Image
            {
                Source = drawingImage,
            };

            _adornmentLayer = view.GetAdornmentLayer("ViewportAdornment");

            _view.ViewportHeightChanged += OnSizeChanged;
            _view.ViewportWidthChanged += OnSizeChanged;
        }

        /// <summary>
        /// Event handler for viewport height or width changed events. Adds adornment at the top right corner of the viewport.
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        private void OnSizeChanged(object sender, EventArgs e)
        {
            // Clear the adornment layer of previous adornments
            _adornmentLayer.RemoveAllAdornments();

            // Place the image in the top right hand corner of the Viewport
            Canvas.SetLeft(_image, _view.ViewportRight - RightMargin - AdornmentWidth);
            Canvas.SetTop(_image, _view.ViewportTop + TopMargin);

            // Add the image to the adornment layer and make it relative to the viewport
            _adornmentLayer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, null, null, _image, null);
        }
    }
}
