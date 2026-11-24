using Avalonia.Media;
using Runix.Core.Models;

namespace Runix.Desktop.Models;

public class DatabaseEngineOption
{
    public DatabaseType Type { get; init; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Badge { get; init; } = string.Empty;
    public Geometry? IconData { get; init; }
    public IBrush IconBackground { get; init; } = Brushes.Transparent;
    public IBrush IconForeground { get; init; } = Brushes.White;
    public IBrush BadgeBackground { get; init; } = Brushes.Transparent;
    public IBrush BadgeForeground { get; init; } = Brushes.White;
}
