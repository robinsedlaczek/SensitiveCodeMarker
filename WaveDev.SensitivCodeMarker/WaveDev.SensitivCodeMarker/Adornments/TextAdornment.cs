//------------------------------------------------------------------------------
// <copyright file="TextAdornment.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Text;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace WaveDev.SensitivCodeMarker.Adornments
{
    /// <summary>
    /// TextAdornment places red boxes behind all the "a"s in the editor window
    /// </summary>
    internal sealed class TextAdornment
    {
        /// <summary>
        /// The layer of the adornment.
        /// </summary>
        private readonly IAdornmentLayer _layer;

        /// <summary>
        /// Text view where the adornment is created.
        /// </summary>
        private readonly IWpfTextView _view;

        /// <summary>
        /// Adornment brush.
        /// </summary>
        private readonly Brush _brush;

        /// <summary>
        /// Adornment pen.
        /// </summary>
        private readonly Pen _pen;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextAdornment"/> class.
        /// </summary>
        /// <param name="view">Text view to create the adornment for</param>
        public TextAdornment(IWpfTextView view)
        {
            if (view == null)
            {
                throw new ArgumentNullException("view");
            }

            _layer = view.GetAdornmentLayer("TextAdornment");

            _view = view;
            _view.LayoutChanged += OnLayoutChanged;
            _view.MouseHover += OnViewMouseHover;

            // Create the pen and brush to color the box behind the a's
            _brush = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x00, 0xff));
            _brush.Freeze();

            var penBrush = new SolidColorBrush(Colors.Red);
            penBrush.Freeze();
            _pen = new Pen(penBrush, 0.5);
            _pen.Freeze();
        }

        /// <summary>
        /// Handles whenever the text displayed in the view changes by adding the adornment to any reformatted lines
        /// </summary>
        /// <remarks><para>This event is raised whenever the rendered text displayed in the <see cref="ITextView"/> changes.</para>
        /// <para>It is raised whenever the view does a layout (which happens when DisplayTextLineContainingBufferPosition is called or in response to text or classification changes).</para>
        /// <para>It is also raised whenever the view scrolls horizontally or when its size changes.</para>
        /// </remarks>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        internal void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            var sensitiveSyntaxNodes = CollectSensitiveSyntaxNodes(e.NewSnapshot);

            CreateSensitiveCodeMarkerVisuals(sensitiveSyntaxNodes, e.NewSnapshot);
        }

        private static IList<SyntaxNode> CollectSensitiveSyntaxNodes(ITextSnapshot newSnapshot)
        {
            var sensitiveSyntaxNodes = new List<SyntaxNode>();

            // [RS] NuGet package Microsoft.CodeAnalysis.EditorFeatures.Text needed for method 'GetOpenDocumentInCurrentContextWithChanges'.
            //      There it is defined in Microsoft.CodeAnalysis.Text.
            var currentDocument = newSnapshot.GetOpenDocumentInCurrentContextWithChanges();

            if (currentDocument == null)
                return sensitiveSyntaxNodes;

            var trees = currentDocument.Project.Documents
                .Select(document => document.GetSyntaxTreeAsync().Result)
                .SkipWhile(syntaxTree => syntaxTree.Length == 0);

            var references = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.CodeBase.Substring(8)),
                MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5.2\System.Core.dll")
            };

            var compilation = CSharpCompilation
                .Create("CodeInCurrentProject")
                .AddReferences(references)
                .AddSyntaxTrees(trees);

            var tree = currentDocument.GetSyntaxTreeAsync().Result;
            var semanticModel = compilation.GetSemanticModel(tree);
            var treeRoot = tree.GetRoot() as CompilationUnitSyntax;

            // [RS] Collect member access expressions.
            var memberAccessExpressions = treeRoot.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
            foreach (var expression in memberAccessExpressions)
            {
                var typeInfo = semanticModel.GetTypeInfo(expression);

                if (typeInfo.Type != null)
                {
                    if (typeInfo.Type.AllInterfaces.Where(namedInterfaceType => namedInterfaceType.Name == "ISensitiveObject").Any())
                        sensitiveSyntaxNodes.Add(expression);
                }
            }

            // [RS] Collect identifier names.
            var identifiers = treeRoot.DescendantNodes().OfType<IdentifierNameSyntax>();
            foreach (var identifier in identifiers)
            {
                if (identifier.IsVar)
                    continue;

                var typeInfo = semanticModel.GetTypeInfo(identifier);

                if (typeInfo.Type != null)
                {
                    if (typeInfo.Type.AllInterfaces.Where(namedInterfaceType => namedInterfaceType.Name == "ISensitiveObject").Any())
                        sensitiveSyntaxNodes.Add(identifier);
                }
                else
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(identifier);

                    if (symbolInfo.Symbol != null)
                    {
                        var namedType = symbolInfo.Symbol as INamedTypeSymbol;

                        if (namedType != null && namedType.AllInterfaces.Where(namedInterfaceType => namedInterfaceType.Name == "ISensitiveObject").Any())
                            sensitiveSyntaxNodes.Add(identifier);
                    }
                }
            }

            return sensitiveSyntaxNodes;
        }

        private void CreateSensitiveCodeMarkerVisuals(IList<SyntaxNode> sensitiveSyntaxNodes, ITextSnapshot newSnapshot)
        {
            _layer.RemoveAllAdornments();

            foreach (var node in sensitiveSyntaxNodes)
            {
                var length = node.Span.End - node.Span.Start;
                var span = new SnapshotSpan(newSnapshot, node.Span.Start, length);

                IWpfTextViewLineCollection textViewLines = _view.TextViewLines;

                var geometry = textViewLines.GetMarkerGeometry(span);

                if (geometry != null)
                {
                    var drawing = new GeometryDrawing(_brush, _pen, geometry);
                    drawing.Freeze();

                    var drawingImage = new DrawingImage(drawing);
                    drawingImage.Freeze();

                    var image = new Image
                    {
                        Source = drawingImage,
                    };

                    // Align the image with the top of the bounds of the text geometry
                    Canvas.SetLeft(image, geometry.Bounds.Left);
                    Canvas.SetTop(image, geometry.Bounds.Top);

                    _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, image, null);
                }
            }
        }

        private void OnViewMouseHover(object sender, MouseHoverEventArgs e)
        {

        }
    }
}
