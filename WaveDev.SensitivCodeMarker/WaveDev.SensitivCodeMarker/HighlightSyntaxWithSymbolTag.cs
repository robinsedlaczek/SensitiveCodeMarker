using Microsoft.VisualStudio.Text.Tagging;

namespace WaveDev.SensitivCodeMarker
{
    internal class HighlightSyntaxWithSymbolTag : TextMarkerTag
    {
        public HighlightSyntaxWithSymbolTag()
             : base("MarkerFormatDefinition/HighlightSyntaxWithSymbolFormatDefinition")
        {

        }
    }
}
