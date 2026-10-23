using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;

namespace TaskList.App;

/// <summary>
/// The header ring: a faint track with a round-capped accent arc that eases to its new value.
/// Drawn by hand because the stock ProgressRing can't change its stroke or caps.
/// </summary>
public sealed class ProgressArc
{
    private const double Stroke = 5;
    private readonly Ellipse _track;
    private readonly Path _arc;
    private readonly double _size;
    private readonly DispatcherQueueTimer _timer;
    private double _shown;
    private double _target;
    private double _from;
    private DateTime _start;

    public ProgressArc(Grid host)
    {
        _size = host.Width;
        _track = new Ellipse { Width = _size, Height = _size, StrokeThickness = Stroke };
        _arc = new Path
        {
            StrokeThickness = Stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        host.Children.Insert(0, _arc);
        host.Children.Insert(0, _track);

        _timer = host.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) => Step();
    }

    public void SetColors(Brush arc, Brush track)
    {
        _arc.Stroke = arc;
        _track.Stroke = track;
    }

    /// <param name="fraction">0..1</param>
    public void SetValue(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (Math.Abs(fraction - _target) < 0.0001) return;
        _from = _shown;
        _target = fraction;
        _start = DateTime.Now;
        _timer.Start();
    }

    private void Step()
    {
        double t = Math.Min(1, (DateTime.Now - _start).TotalMilliseconds / 520);
        double eased = 1 - Math.Pow(1 - t, 3);
        _shown = _from + (_target - _from) * eased;
        Draw(_shown);
        if (t >= 1) _timer.Stop();
    }

    private void Draw(double fraction)
    {
        double r = (_size - Stroke) / 2, c = _size / 2;
        if (fraction <= 0.001)
        {
            _arc.Data = null;
            return;
        }
        if (fraction >= 0.999)
        {
            _arc.Data = new EllipseGeometry { Center = new Point(c, c), RadiusX = r, RadiusY = r };
            return;
        }
        double angle = 2 * Math.PI * fraction;
        var figure = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle)),
            Size = new Size(r, r),
            IsLargeArc = fraction > 0.5,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _arc.Data = geometry;
    }
}
