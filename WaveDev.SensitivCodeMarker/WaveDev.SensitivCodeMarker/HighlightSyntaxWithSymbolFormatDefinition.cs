using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using System.Windows.Media;

namespace WaveDev.SensitivCodeMarker
{
    [Export(typeof(EditorFormatDefinition))]
    [Name("MarkerFormatDefinition/HighlightSyntaxWithSymbolFormatDefinition")]
    [UserVisible(true)]
    internal class HighlightSyntaxWithSymbolFormatDefinition : MarkerFormatDefinition
    {
        public HighlightSyntaxWithSymbolFormatDefinition()
        {
            //BackgroundColor = Colors.Red;
            //ForegroundColor = Colors.White;
            Border = new Pen(Brushes.Red, 2);
            DisplayName = "Highlight Syntax with Symbol";
            ZOrder = 5;
            //ForegroundBrush = Brushes.White;
        }
    }
}
