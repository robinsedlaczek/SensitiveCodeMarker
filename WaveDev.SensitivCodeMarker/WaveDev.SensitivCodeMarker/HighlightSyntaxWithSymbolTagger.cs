using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WaveDev.SensitivCodeMarker
{
    internal class HighlightSyntaxWithSymbolTagger : ITagger<HighlightSyntaxWithSymbolTag>
    {
        private object updateLock = new object();

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        #region Interface ITagger

        public IEnumerable<ITagSpan<HighlightSyntaxWithSymbolTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            NormalizedSnapshotSpanCollection sensitiveWordSpans = SensitiveWordSpans;

            if (spans.Count == 0 || sensitiveWordSpans.Count == 0)
                yield break;

            // [RS] If the requested snapshot isn't the same as the one where sensitive code was found, 
            //      translate the sensitive code spans to the expected snapshot.
            if (spans[0].Snapshot != sensitiveWordSpans[0].Snapshot)
            {
                sensitiveWordSpans = new NormalizedSnapshotSpanCollection(
                    sensitiveWordSpans.Select(
                        span => span.TranslateTo(spans[0].Snapshot, SpanTrackingMode.EdgeExclusive)));
            }

            // Yield all the other words in the file 
            foreach (SnapshotSpan span in NormalizedSnapshotSpanCollection.Overlap(spans, sensitiveWordSpans))
            {
                yield return new TagSpan<HighlightSyntaxWithSymbolTag>(span, new HighlightSyntaxWithSymbolTag());
            }
        }

        #endregion

        #region Construction 

        public HighlightSyntaxWithSymbolTagger(ITextView view, ITextBuffer sourceBuffer)
        {
            View = view;
            SourceBuffer = sourceBuffer;
            SensitiveWordSpans = new NormalizedSnapshotSpanCollection();

            View.LayoutChanged += ViewLayoutChanged;
        }

        #endregion

        #region Private Members

        private ITextView View { get; set; }

        private ITextBuffer SourceBuffer { get; set; }

        private NormalizedSnapshotSpanCollection SensitiveWordSpans { get; set; }

        #endregion

        #region Private Methods

        private void ViewLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            UpdateSensitiveSyntaxTags(e.NewSnapshot);
        }

        private void UpdateSensitiveSyntaxTags(ITextSnapshot newSnapshot)
        {
            var wordSpans = new List<SnapshotSpan>();
            var sensitiveSyntaxNodes = CollectSensitiveSyntaxNodes(newSnapshot);

            foreach (var node in sensitiveSyntaxNodes)
            {
                try
                {
                    var length = node.Span.End - node.Span.Start;
                    var span = new SnapshotSpan(newSnapshot, node.Span.Start, length);

                    wordSpans.Add(span);
                }
                catch
                {

                }
            }

            lock (updateLock)
            {
                SensitiveWordSpans = new NormalizedSnapshotSpanCollection(wordSpans);

                // [RS] Tags (Adornments) changed for the text in the source buffer. So raise appropriate event.
                var tempEvent = TagsChanged;

                if (tempEvent != null)
                    tempEvent(this, new SnapshotSpanEventArgs(new SnapshotSpan(SourceBuffer.CurrentSnapshot, 0, SourceBuffer.CurrentSnapshot.Length)));
            }
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

        #endregion

    }
}
