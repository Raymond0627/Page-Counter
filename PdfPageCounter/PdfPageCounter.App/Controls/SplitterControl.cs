using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace PdfPageCounter_App;

/// <summary>
/// A control that automatically displays a SizeWestEast (horizontal resize) cursor
/// when hovered, allowing smooth visual feedback for the draggable splitter.
/// </summary>
public sealed class SplitterControl : ContentControl
{
    public SplitterControl()
    {
        IsTabStop = false;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}
