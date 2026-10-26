using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace PhotoFramer.App.Controls;

/// <summary>Ruční výběr čtvercového výřezu nad fotkou. Výřez se drží v pixelech originálu.</summary>
public partial class CropView : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(CropView),
        new PropertyMetadata(null, (d, _) => ((CropView)d).OnSourceChanged()));

    public static readonly DependencyProperty CropRectProperty = DependencyProperty.Register(
        nameof(CropRect), typeof(SKRectI), typeof(CropView),
        new FrameworkPropertyMetadata(default(SKRectI), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty DoubleClickCommandProperty = DependencyProperty.Register(
        nameof(DoubleClickCommand), typeof(ICommand), typeof(CropView));

    private const double MinSize = 50;
    private const double HandleHit = 18;

    private int _imageWidth;
    private int _imageHeight;
    private double _scale = 1;

    // výřez v pixelech originálu
    private double _x, _y, _size;

    private enum Drag { None, Move, TopLeft, TopRight, BottomLeft, BottomRight }
    private Drag _drag;
    private Point _dragStart;
    private (double X, double Y, double Size) _dragOrigin;

    public CropView() => InitializeComponent();

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Spustí se dvojklikem v okně ořezu.</summary>
    public ICommand? DoubleClickCommand
    {
        get => (ICommand?)GetValue(DoubleClickCommandProperty);
        set => SetValue(DoubleClickCommandProperty, value);
    }

    public SKRectI CropRect
    {
        get => (SKRectI)GetValue(CropRectProperty);
        set => SetValue(CropRectProperty, value);
    }

    private void OnSourceChanged()
    {
        Photo.Source = Source;
        if (Source is null)
            return;

        _imageWidth = Source.PixelWidth;
        _imageHeight = Source.PixelHeight;
        _size = Math.Min(_imageWidth, _imageHeight);
        _x = (_imageWidth - _size) / 2;
        _y = (_imageHeight - _size) / 2;
        Layout();
    }

    private void Host_SizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    private void Layout()
    {
        if (_imageWidth == 0 || Host.ActualWidth < 1 || Host.ActualHeight < 1)
            return;
        _scale = Math.Min(Host.ActualWidth / _imageWidth, Host.ActualHeight / _imageHeight);
        Surface.Width = _imageWidth * _scale;
        Surface.Height = _imageHeight * _scale;
        Photo.Width = Surface.Width;
        Photo.Height = Surface.Height;
        UpdateOverlay();
    }

    private void UpdateOverlay()
    {
        double left = _x * _scale, top = _y * _scale, side = _size * _scale;
        Canvas.SetLeft(Frame, left); Canvas.SetTop(Frame, top);
        Frame.Width = side; Frame.Height = side;

        Place(HandleTl, left, top);
        Place(HandleTr, left + side, top);
        Place(HandleBl, left, top + side);
        Place(HandleBr, left + side, top + side);

        Dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, Surface.Width, Surface.Height)),
            new RectangleGeometry(new Rect(left, top, side, side)));

        var rect = new SKRectI((int)Math.Round(_x), (int)Math.Round(_y), (int)Math.Round(_x + _size), (int)Math.Round(_y + _size));
        Info.Text = $"Výřez: {rect.Width}×{rect.Height} px (zdroj {_imageWidth}×{_imageHeight}) · šipky = posun, +/- = velikost, Shift = krok 20";
        CropRect = rect;
    }

    private static void Place(FrameworkElement handle, double cx, double cy)
    {
        Canvas.SetLeft(handle, cx - handle.Width / 2);
        Canvas.SetTop(handle, cy - handle.Height / 2);
    }

    private Drag HitTest(Point p)
    {
        double left = _x * _scale, top = _y * _scale, side = _size * _scale;
        bool Near(double ax, double ay) => Math.Abs(p.X - ax) <= HandleHit && Math.Abs(p.Y - ay) <= HandleHit;
        if (Near(left, top)) return Drag.TopLeft;
        if (Near(left + side, top)) return Drag.TopRight;
        if (Near(left, top + side)) return Drag.BottomLeft;
        if (Near(left + side, top + side)) return Drag.BottomRight;
        if (p.X >= left && p.X <= left + side && p.Y >= top && p.Y <= top + side) return Drag.Move;
        return Drag.None;
    }

    private void Surface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // klik do ořezu přebere klávesnici (např. z textového pole), aby fungovaly šipky a +/-
        Keyboard.Focus(this);

        if (e.ClickCount == 2)
        {
            _drag = Drag.None;
            if (DoubleClickCommand?.CanExecute(null) == true)
                DoubleClickCommand.Execute(null);
            return;
        }

        _dragStart = e.GetPosition(Surface);
        _drag = HitTest(_dragStart);
        _dragOrigin = (_x, _y, _size);
        if (_drag != Drag.None)
            Surface.CaptureMouse();
    }

    /// <summary>Pravé tlačítko myši spustí stejnou akci jako dvojklik (zvyk ze Zoner Photo Studia).</summary>
    private void Surface_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (DoubleClickCommand?.CanExecute(null) == true)
            DoubleClickCommand.Execute(null);
        e.Handled = true;
    }

    private void Surface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _drag = Drag.None;
        Surface.ReleaseMouseCapture();
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Surface);
        if (_drag == Drag.None)
        {
            Surface.Cursor = HitTest(p) switch
            {
                Drag.Move => Cursors.SizeAll,
                Drag.TopLeft or Drag.BottomRight => Cursors.SizeNWSE,
                Drag.TopRight or Drag.BottomLeft => Cursors.SizeNESW,
                _ => Cursors.Arrow,
            };
            return;
        }

        var dx = (p.X - _dragStart.X) / _scale;
        var dy = (p.Y - _dragStart.Y) / _scale;
        var (ox, oy, os) = _dragOrigin;

        if (_drag == Drag.Move)
        {
            _x = Math.Clamp(ox + dx, 0, _imageWidth - os);
            _y = Math.Clamp(oy + dy, 0, _imageHeight - os);
        }
        else
        {
            // protilehlý roh zůstává na místě; velikost podle většího posunu
            var (fixX, fixY, sx, sy) = _drag switch
            {
                Drag.TopLeft => (ox + os, oy + os, -1, -1),
                Drag.TopRight => (ox, oy + os, 1, -1),
                Drag.BottomLeft => (ox + os, oy, -1, 1),
                _ => (ox, oy, 1, 1),
            };
            var delta = Math.Abs(dx) > Math.Abs(dy) ? dx * sx : dy * sy;
            var maxSize = Math.Min(sx > 0 ? _imageWidth - fixX : fixX, sy > 0 ? _imageHeight - fixY : fixY);
            _size = Math.Clamp(os + delta, MinSize, Math.Max(MinSize, maxSize));
            _x = sx > 0 ? fixX : fixX - _size;
            _y = sy > 0 ? fixY : fixY - _size;
        }
        UpdateOverlay();
    }

    /// <summary>
    /// Klávesy: šipky posouvají výřez, numerické +/- mění velikost; Shift = krok 20 px (jinak 1 px).
    /// Vrací true, pokud klávesu zpracoval.
    /// </summary>
    public bool HandleKey(Key key)
    {
        if (_imageWidth == 0)
            return false;

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 20 : 1;
        var maxSize = Math.Min(_imageWidth, _imageHeight);

        switch (key)
        {
            case Key.Left: _x -= step; break;
            case Key.Right: _x += step; break;
            case Key.Up: _y -= step; break;
            case Key.Down: _y += step; break;
            case Key.Add:
            case Key.Subtract:
            {
                var newSize = Math.Clamp(_size + (key == Key.Add ? step : -step), Math.Min(MinSize, maxSize), maxSize);
                var delta = newSize - _size;
                _x = Math.Round(_x - delta / 2, MidpointRounding.AwayFromZero);
                _y = Math.Round(_y - delta / 2, MidpointRounding.AwayFromZero);
                _size = newSize;
                break;
            }
            default:
                return false;
        }

        _x = Math.Clamp(_x, 0, _imageWidth - _size);
        _y = Math.Clamp(_y, 0, _imageHeight - _size);
        UpdateOverlay();
        return true;
    }

    private void Surface_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_imageWidth == 0)
            return;
        var factor = e.Delta > 0 ? 0.95 : 1 / 0.95;
        var maxSize = Math.Min(_imageWidth, _imageHeight);
        var newSize = Math.Clamp(_size * factor, Math.Min(MinSize, maxSize), maxSize);
        var cx = _x + _size / 2;
        var cy = _y + _size / 2;
        _size = newSize;
        _x = Math.Clamp(cx - newSize / 2, 0, _imageWidth - newSize);
        _y = Math.Clamp(cy - newSize / 2, 0, _imageHeight - newSize);
        UpdateOverlay();
    }
}
