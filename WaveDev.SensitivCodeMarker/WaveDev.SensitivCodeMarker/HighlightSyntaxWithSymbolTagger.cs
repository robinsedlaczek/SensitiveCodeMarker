using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
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
            //if (CurrentWord == null)
            //    yield break;

            //// Hold on to a "snapshot" of the word spans and current word, so that we maintain the same
            //// collection throughout
            //SnapshotSpan currentWord = CurrentWord.Value;
            NormalizedSnapshotSpanCollection wordSpans = WordSpans;

            if (spans.Count == 0 || wordSpans.Count == 0)
                yield break;

            // If the requested snapshot isn't the same as the one our words are on, translate our spans to the expected snapshot 
            if (spans[0].Snapshot != wordSpans[0].Snapshot)
            {
                wordSpans = new NormalizedSnapshotSpanCollection(
                    wordSpans.Select(span => span.TranslateTo(spans[0].Snapshot, SpanTrackingMode.EdgeExclusive)));

                //currentWord = currentWord.TranslateTo(spans[0].Snapshot, SpanTrackingMode.EdgeExclusive);
            }

            // First, yield back the word the cursor is under (if it overlaps) 
            // Note that we'll yield back the same word again in the wordspans collection; 
            // the duplication here is expected. 
            //if (spans.OverlapsWith(new NormalizedSnapshotSpanCollection(currentWord)))
            //    yield return new TagSpan<HighlightSyntaxWithSymbolTag>(currentWord, new HighlightSyntaxWithSymbolTag());

            // Second, yield all the other words in the file 
            foreach (SnapshotSpan span in NormalizedSnapshotSpanCollection.Overlap(spans, wordSpans))
            {
                yield return new TagSpan<HighlightSyntaxWithSymbolTag>(span, new HighlightSyntaxWithSymbolTag());
            }
        }

        #endregion

        #region Construction 

        public HighlightSyntaxWithSymbolTagger(ITextView view, ITextBuffer sourceBuffer, ITextSearchService textSearchService, ITextStructureNavigator textStructureNavigator)
        {
            View = view;
            SourceBuffer = sourceBuffer;
            TextSearchService = textSearchService;
            TextStructureNavigator = textStructureNavigator;
            WordSpans = new NormalizedSnapshotSpanCollection();
            CurrentWord = null;

            View.Caret.PositionChanged += CaretPositionChanged;
            View.LayoutChanged += ViewLayoutChanged;
        }

        #endregion

        #region Private Members

        private ITextView View { get; set; }

        private ITextBuffer SourceBuffer { get; set; }

        private ITextSearchService TextSearchService { get; set; }

        private ITextStructureNavigator TextStructureNavigator { get; set; }

        private NormalizedSnapshotSpanCollection WordSpans { get; set; }

        private SnapshotSpan? CurrentWord { get; set; }

        private SnapshotPoint RequestedPoint { get; set; }

        #endregion

        #region Private Methods

        private void ViewLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            // If a new snapshot wasn't generated, then skip this layout 
            //if (e.NewSnapshot != e.OldSnapshot)
            //{
                //UpdateAtCaretPosition(View.Caret.Position);
                UpdateSyntaxWithSymbolTextAdornments(e.NewSnapshot);
            //}
        }

        private void UpdateSyntaxWithSymbolTextAdornments(ITextSnapshot newSnapshot)
        {
            var syntaxNodes = CollectSyntaxNodesWithFoundSymbols(newSnapshot);
            var wordSpans = new List<SnapshotSpan>();

            foreach (var node in syntaxNodes)
            {
                try
                {
                    var length = node.Span.End - node.Span.Start;
                    var span = new SnapshotSpan(newSnapshot, node.Span.Start, length);
                    // var wordExtent = TextStructureNavigator.GetExtentOfWord(span.Start);

                    wordSpans.Add(span);
                }
                catch
                {

                }
            }

            lock (updateLock)
            {
                WordSpans = new NormalizedSnapshotSpanCollection(wordSpans);

                var tempEvent = TagsChanged;

                if (tempEvent != null)
                    tempEvent(this, new SnapshotSpanEventArgs(new SnapshotSpan(SourceBuffer.CurrentSnapshot, 0, SourceBuffer.CurrentSnapshot.Length)));
            }
        }

        private static IList<SyntaxNode> CollectSyntaxNodesWithFoundSymbols(ITextSnapshot newSnapshot)
        {
            var syntaxNodesWithFoundSymbols = new List<SyntaxNode>();
            // [RS] NuGet package Microsoft.CodeAnalysis.EditorFeatures.Text needed for method 'GetOpenDocumentInCurrentContextWithChanges'.
            //      There it is defined in Microsoft.CodeAnalysis.Text.
            var currentDocument = newSnapshot.GetOpenDocumentInCurrentContextWithChanges();

            if (currentDocument == null)
                return syntaxNodesWithFoundSymbols;

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
            var sourceNode = tree.GetRoot() as CompilationUnitSyntax;

            foreach (var node in sourceNode.DescendantNodes())
            {
                var symbolInfo = semanticModel.GetSymbolInfo(node);

                if (symbolInfo.Symbol != null)
                {
                    var namedType = symbolInfo.Symbol as INamedTypeSymbol;

                    if (namedType != null && namedType.AllInterfaces.Where(namedInterfaceType => namedInterfaceType.Name == "ISensitiveObject").Any())
                        syntaxNodesWithFoundSymbols.Add(node);
                }
            }



            var memberAccessExpressions = sourceNode.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

            //memberAccessExpressions.First().Expression.
            

            return syntaxNodesWithFoundSymbols;
        }

        private void CaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            //UpdateAtCaretPosition(e.NewPosition);
        }

        private void UpdateAtCaretPosition(CaretPosition caretPosition)
        {
            SnapshotPoint? point = caretPosition.Point.GetPoint(SourceBuffer, caretPosition.Affinity);

            if (!point.HasValue)
                return;

            // If the new caret position is still within the current word (and on the same snapshot), we don't need to check it 
            if (CurrentWord.HasValue
                && CurrentWord.Value.Snapshot == View.TextSnapshot
                && point.Value >= CurrentWord.Value.Start
                && point.Value <= CurrentWord.Value.End)
            {
                return;
            }

            RequestedPoint = point.Value;
            UpdateWordAdornments();
        }

        private void UpdateWordAdornments()
        {
            SnapshotPoint currentRequest = RequestedPoint;
            List<SnapshotSpan> wordSpans = new List<SnapshotSpan>();
            //Find all words in the buffer like the one the caret is on
            TextExtent word = TextStructureNavigator.GetExtentOfWord(currentRequest);
            bool foundWord = true;
            //If we've selected something not worth highlighting, we might have missed a "word" by a little bit
            if (!WordExtentIsValid(currentRequest, word))
            {
                //Before we retry, make sure it is worthwhile 
                if (word.Span.Start != currentRequest
                     || currentRequest == currentRequest.GetContainingLine().Start
                     || char.IsWhiteSpace((currentRequest - 1).GetChar()))
                {
                    foundWord = false;
                }
                else
                {
                    // Try again, one character previous.  
                    //If the caret is at the end of a word, pick up the word.
                    word = TextStructureNavigator.GetExtentOfWord(currentRequest - 1);

                    //If the word still isn't valid, we're done 
                    if (!WordExtentIsValid(currentRequest, word))
                        foundWord = false;
                }
            }

            if (!foundWord)
            {
                //If we couldn't find a word, clear out the existing markers
                SynchronousUpdate(currentRequest, new NormalizedSnapshotSpanCollection(), null);
                return;
            }

            SnapshotSpan currentWord = word.Span;
            //If this is the current word, and the caret moved within a word, we're done. 
            if (CurrentWord.HasValue && currentWord == CurrentWord)
                return;

            //Find the new spans
            FindData findData = new FindData(currentWord.GetText(), currentWord.Snapshot);
            findData.FindOptions = FindOptions.WholeWord | FindOptions.MatchCase;

            wordSpans.AddRange(TextSearchService.FindAll(findData));

            //If another change hasn't happened, do a real update 
            if (currentRequest == RequestedPoint)
                SynchronousUpdate(currentRequest, new NormalizedSnapshotSpanCollection(wordSpans), currentWord);
        }

        private static bool WordExtentIsValid(SnapshotPoint currentRequest, TextExtent word)
        {
            return word.IsSignificant
                && currentRequest.Snapshot.GetText(word.Span).Any(c => char.IsLetter(c));
        }

        private void SynchronousUpdate(SnapshotPoint currentRequest, NormalizedSnapshotSpanCollection newSpans, SnapshotSpan? newCurrentWord)
        {
            lock (updateLock)
            {
                if (currentRequest != RequestedPoint)
                    return;

                WordSpans = newSpans;
                CurrentWord = newCurrentWord;

                var tempEvent = TagsChanged;
                if (tempEvent != null)
                    tempEvent(this, new SnapshotSpanEventArgs(new SnapshotSpan(SourceBuffer.CurrentSnapshot, 0, SourceBuffer.CurrentSnapshot.Length)));
            }
        }

        #endregion

    }
}
