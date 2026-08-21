using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Checkmk.App.Services;

namespace Checkmk.App.Controls;

/// <summary>Ein Bereich, wie ihn die Karte zeichnet.</summary>
/// <param name="AreaId">Kennung für den Rückweg (Klick → Bereich).</param>
/// <param name="Outline">Farbe des Randes — kommt aus dem Status-Rollup.</param>
public sealed record MapShape(
    int AreaId,
    string Name,
    IReadOnlyList<GeoPoint> Points,
    Color Outline);

/// <summary>
/// Kachelkarte mit Polygon-Overlay. Bewusst ein eigenes Control statt einer
/// WebView: Was hier gebraucht wird — schieben, zoomen, Flächen zeichnen,
/// Treffer erkennen — ist überschaubar, und eine eingebettete Browser-Engine
/// in einem self-contained Single-File-EXE wäre der teurere Weg.
/// </summary>
public sealed class MapCanvas : Control
{
    // Potsdam, Alter Markt — sinnvoller Startpunkt für diese Stadtverwaltung.
    private static readonly GeoPoint DefaultCenter = new(13.0645, 52.3958);

    private MapTileLoader? _tiles;
    private GeoPoint _center = DefaultCenter;
    private double _zoom = 14;

    private bool _dragging;
    private Point _dragFrom;
    private GeoPoint _dragCenterAtStart;

    private readonly List<GeoPoint> _draft = [];
    private GeoPoint? _draftCursor;

    public MapCanvas()
    {
        ClipToBounds = true;
        Focusable = true;   // fuer Esc/Enter im Zeichenmodus
    }

    /// <summary>Bereiche mit Fläche. Wird bei jedem Rollup neu gesetzt.</summary>
    public IReadOnlyList<MapShape> Shapes { get; set; } = [];

    /// <summary>Hervorgehobener Bereich (Auswahl im Baum).</summary>
    public int? HighlightedAreaId { get; set; }

    /// <summary>true, solange der Anwender eine Fläche zeichnet.</summary>
    public bool IsDrawing { get; private set; }

    /// <summary>Klick auf eine Fläche (im Normalmodus).</summary>
    public event Action<int>? AreaClicked;

    /// <summary>Zeichnen abgeschlossen — liefert das fertige Polygon.</summary>
    public event Action<IReadOnlyList<GeoPoint>>? DrawingFinished;

    /// <summary>Zeichenmodus verlassen (fertig oder abgebrochen), für die Toolbar.</summary>
    public event Action? DrawingModeChanged;

    public void Attach(MapTileLoader tiles)
    {
        _tiles = tiles;
        InvalidateVisual();
    }

    // ------------------------------------------------------------------
    // Ansicht
    // ------------------------------------------------------------------

    public void CenterOn(GeoPoint center, double zoom)
    {
        _center = center;
        _zoom = Math.Clamp(zoom, 2, 20);
        InvalidateVisual();
    }

    /// <summary>Ansicht auf ein Polygon einpassen, mit etwas Luft am Rand.</summary>
    public void FitTo(IReadOnlyList<GeoPoint> points)
    {
        if (MapGeometry.Bounds(points) is not { } b) return;

        var zoom = WebMercator.FitZoom(b.Min, b.Max,
            Math.Max(32, Bounds.Width * 0.85), Math.Max(32, Bounds.Height * 0.85));
        CenterOn(new GeoPoint((b.Min.Lon + b.Max.Lon) / 2, (b.Min.Lat + b.Max.Lat) / 2), zoom);
    }

    // ------------------------------------------------------------------
    // Zeichenmodus
    // ------------------------------------------------------------------

    public void BeginDrawing()
    {
        _draft.Clear();
        _draftCursor = null;
        IsDrawing = true;
        Focus();
        DrawingModeChanged?.Invoke();
        InvalidateVisual();
    }

    public void CancelDrawing()
    {
        if (!IsDrawing) return;
        IsDrawing = false;
        _draft.Clear();
        _draftCursor = null;
        DrawingModeChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Schließt das Polygon ab. Unter drei Punkten passiert nichts —
    /// zwei Punkte sind eine Linie, keine Fläche.</summary>
    public void FinishDrawing()
    {
        if (!IsDrawing) return;

        var points = _draft.ToList();
        IsDrawing = false;
        _draft.Clear();
        _draftCursor = null;
        DrawingModeChanged?.Invoke();
        InvalidateVisual();

        if (points.Count >= 3) DrawingFinished?.Invoke(points);
    }

    /// <summary>Letzten gesetzten Punkt zurücknehmen.</summary>
    public void UndoLastPoint()
    {
        if (!IsDrawing || _draft.Count == 0) return;
        _draft.RemoveAt(_draft.Count - 1);
        InvalidateVisual();
    }

    // ------------------------------------------------------------------
    // Eingabe
    // ------------------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetCurrentPoint(this);
        if (!p.Properties.IsLeftButtonPressed) return;

        Focus();

        if (IsDrawing)
        {
            // Doppelklick schliesst die Flaeche — derselbe Griff wie in jedem
            // Zeichenprogramm.
            if (e.ClickCount >= 2) { FinishDrawing(); e.Handled = true; return; }

            _draft.Add(ToGeo(p.Position));
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _dragging = true;
        _dragFrom = p.Position;
        _dragCenterAtStart = _center;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (IsDrawing)
        {
            _draftCursor = ToGeo(pos);   // Gummiband zum Mauszeiger
            InvalidateVisual();
            return;
        }

        if (!_dragging) return;

        // Verschieben in Weltpixeln statt in Grad: In Mercator sind Grad je
        // nach Breite unterschiedlich breit, die Karte wuerde am Bildschirm
        // "rutschen" statt dem Zeiger zu folgen.
        var (cx, cy) = WebMercator.ToWorld(_dragCenterAtStart, _zoom);
        _center = WebMercator.ToGeo(cx - (pos.X - _dragFrom.X), cy - (pos.Y - _dragFrom.Y), _zoom);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);

        // Kaum bewegt? Dann war es ein Klick auf eine Flaeche, kein Schieben.
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragFrom.X) < 3 && Math.Abs(pos.Y - _dragFrom.Y) < 3)
        {
            if (HitTest(pos) is { } areaId) AreaClicked?.Invoke(areaId);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0) return;

        // Auf den Mauszeiger zoomen: der Punkt unter dem Zeiger bleibt stehen.
        // Ohne das rutscht bei jedem Rad-Schritt das Ziel aus dem Bild.
        var pos = e.GetPosition(this);
        var before = ToGeo(pos);

        _zoom = Math.Clamp(_zoom + (e.Delta.Y > 0 ? 1 : -1), 2, 20);

        var after = ToGeo(pos);
        _center = new GeoPoint(
            _center.Lon + (before.Lon - after.Lon),
            _center.Lat + (before.Lat - after.Lat));

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsDrawing) return;

        switch (e.Key)
        {
            case Key.Escape: CancelDrawing(); e.Handled = true; break;
            case Key.Enter: FinishDrawing(); e.Handled = true; break;
            case Key.Back: UndoLastPoint(); e.Handled = true; break;
        }
    }

    /// <summary>Welcher Bereich liegt unter dem Punkt? Kleinste Fläche gewinnt,
    /// damit ein Serverraum im Campus nicht vom Campus verdeckt wird.</summary>
    private int? HitTest(Point position)
    {
        var geo = ToGeo(position);
        int? best = null;
        var bestSize = double.MaxValue;

        foreach (var s in Shapes)
        {
            if (!MapGeometry.Contains(s.Points, geo)) continue;
            if (MapGeometry.Bounds(s.Points) is not { } b) continue;

            var size = (b.Max.Lon - b.Min.Lon) * (b.Max.Lat - b.Min.Lat);
            if (size >= bestSize) continue;
            bestSize = size;
            best = s.AreaId;
        }
        return best;
    }

    // ------------------------------------------------------------------
    // Zeichnen
    // ------------------------------------------------------------------

    private (double X, double Y) TopLeftWorld()
    {
        var (cx, cy) = WebMercator.ToWorld(_center, _zoom);
        return (cx - Bounds.Width / 2, cy - Bounds.Height / 2);
    }

    private Point ToScreen(GeoPoint p)
    {
        var (wx, wy) = WebMercator.ToWorld(p, _zoom);
        var (ox, oy) = TopLeftWorld();
        return new Point(wx - ox, wy - oy);
    }

    private GeoPoint ToGeo(Point screen)
    {
        var (ox, oy) = TopLeftWorld();
        return WebMercator.ToGeo(ox + screen.X, oy + screen.Y, _zoom);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Grundfarbe: ohne sie blitzt beim Schieben der Fensterhintergrund durch.
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), Bounds);

        DrawTiles(context);
        DrawShapes(context);
        DrawDraft(context);
        DrawAttribution(context);
    }

    private void DrawTiles(DrawingContext context)
    {
        if (_tiles is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var zoom = (int)Math.Round(_zoom);
        var (ox, oy) = TopLeftWorld();

        // Der Kachelindex gilt fuer ganzzahlige Zoomstufen; bei krummem _zoom
        // wird das Bild skaliert gezeichnet statt eine falsche Stufe zu laden.
        var scale = Math.Pow(2, _zoom - zoom);
        var tileOnScreen = WebMercator.TileSize * scale;

        var firstX = (int)Math.Floor(ox / tileOnScreen);
        var firstY = (int)Math.Floor(oy / tileOnScreen);
        var countX = (int)Math.Ceiling(Bounds.Width / tileOnScreen) + 1;
        var countY = (int)Math.Ceiling(Bounds.Height / tileOnScreen) + 1;

        var max = 1 << zoom;

        for (var dx = 0; dx < countX; dx++)
        for (var dy = 0; dy < countY; dy++)
        {
            var tx = firstX + dx;
            var ty = firstY + dy;
            if (tx < 0 || ty < 0 || tx >= max || ty >= max) continue;   // ausserhalb der Welt

            var key = new TileKey(zoom, tx, ty);
            var rect = new Rect(
                tx * tileOnScreen - ox, ty * tileOnScreen - oy,
                tileOnScreen + 0.5, tileOnScreen + 0.5);   // halbes Pixel gegen Fugen

            if (_tiles.Peek(key) is { } bitmap)
                context.DrawImage(bitmap, rect);
            else
                _tiles.Request(key, () => Dispatcher.UIThread.Post(InvalidateVisual));
        }
    }

    private void DrawShapes(DrawingContext context)
    {
        foreach (var shape in Shapes)
        {
            if (shape.Points.Count < 3) continue;

            var geometry = BuildGeometry(shape.Points, close: true);
            var highlighted = HighlightedAreaId == shape.AreaId;

            var fill = new SolidColorBrush(shape.Outline, highlighted ? 0.45 : 0.25);
            var pen = new Pen(new SolidColorBrush(shape.Outline), highlighted ? 3 : 2);
            context.DrawGeometry(fill, pen, geometry);

            // Beschriftung in die Mitte des umschliessenden Rechtecks.
            if (MapGeometry.Bounds(shape.Points) is not { } b) continue;
            var mid = ToScreen(new GeoPoint((b.Min.Lon + b.Max.Lon) / 2, (b.Min.Lat + b.Max.Lat) / 2));
            var text = new FormattedText(shape.Name, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.White);

            // Dunkler Kasten hinter der Schrift: auf einem Luftbild ist weisser
            // Text ueber hellen Flaechen sonst unlesbar.
            var box = new Rect(mid.X - text.Width / 2 - 4, mid.Y - text.Height / 2 - 2,
                text.Width + 8, text.Height + 4);
            context.FillRectangle(new SolidColorBrush(Colors.Black, 0.55), box, 3);
            context.DrawText(text, new Point(box.X + 4, box.Y + 2));
        }
    }

    private void DrawDraft(DrawingContext context)
    {
        if (!IsDrawing || _draft.Count == 0) return;

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)), 2,
            new DashStyle([4, 3], 0));

        var points = _draft.ToList();
        if (_draftCursor is { } cursor) points.Add(cursor);

        context.DrawGeometry(
            new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7), 0.2),
            pen,
            BuildGeometry(points, close: points.Count > 2));

        // Griffe auf den gesetzten Punkten — zeigt, was zaehlt und was nur
        // Gummiband zum Zeiger ist.
        foreach (var p in _draft)
        {
            var s = ToScreen(p);
            context.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), s, 4, 4);
        }
    }

    private StreamGeometry BuildGeometry(IReadOnlyList<GeoPoint> points, bool close)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToScreen(points[0]), isFilled: true);
            for (var i = 1; i < points.Count; i++)
                ctx.LineTo(ToScreen(points[i]));
            ctx.EndFigure(close);
        }
        return geometry;
    }

    /// <summary>
    /// Quellenvermerk. Pflicht nach dl-de/by-2.0 — deshalb fest im Bild und
    /// nicht in einem Menü, das niemand öffnet.
    /// </summary>
    private void DrawAttribution(DrawingContext context)
    {
        var label = _tiles?.Attribution;
        if (string.IsNullOrWhiteSpace(label)) return;

        var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 11,
            new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)));

        var box = new Rect(Bounds.Width - text.Width - 10, Bounds.Height - text.Height - 6,
            text.Width + 8, text.Height + 4);
        context.FillRectangle(new SolidColorBrush(Colors.Black, 0.55), box, 3);
        context.DrawText(text, new Point(box.X + 4, box.Y + 2));
    }
}
