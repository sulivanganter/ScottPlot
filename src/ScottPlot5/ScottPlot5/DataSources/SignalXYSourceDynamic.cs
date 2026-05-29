using System;

namespace ScottPlot.DataSources;

/// <summary>
/// A dynamic SignalXY data source backed by <see cref="List{T}"/> that supports
/// appending data at runtime with incremental MinMax caching and selectable sampling modes.
/// </summary>
public class SignalXYSourceDynamic : ISignalXYSource, IDataSource, IGetNearest
{
    private readonly List<double> Xs;
    private readonly List<double> Ys;
    private readonly IncrementalMinMaxCache MinMaxCache;

    public int Count => Xs.Count;

    public bool Rotated { get; set; }

    public double XOffset { get; set; }
    public double YOffset { get; set; }
    public double YScale { get; set; } = 1;
    public double XScale { get; set; } = 1;

    public int MinimumIndex { get; set; }
    public int MaximumIndex { get; set; }

    bool IDataSource.PreferCoordinates => false;
    int IDataSource.Length => Xs.Count;
    int IDataSource.MinRenderIndex => MinimumIndex;
    int IDataSource.MaxRenderIndex => MaximumIndex;

    /// <summary>
    /// Determines pixel-column downsampling strategy
    /// </summary>
    public SamplingMode SamplingMode { get; set; } = SamplingMode.MinMax;

    /// <summary>
    /// Indicates whether the most recent render drew all visible data points directly (no downsampling).
    /// True when zoomed in enough that visible data points ≤ pixel columns.
    /// </summary>
    public bool LastRenderUsedDirectDraw { get; private set; }

    /// <summary>
    /// Optional reference to the plottable's <see cref="MarkerStyle"/>.
    /// When set together with <see cref="DirectDrawMarkerSize"/> &gt; 0, marker visibility
    /// is automatically toggled: visible during direct draw, hidden during downsampling.
    /// </summary>
    public MarkerStyle? MarkerStyle { get; set; }

    /// <summary>
    /// Marker size to use when rendering actual data points (direct draw).
    /// Only used when <see cref="MarkerStyle"/> is set.
    /// </summary>
    public float DirectDrawMarkerSize { get; set; }

    /// <summary>
    /// Create an empty dynamic data source
    /// </summary>
    public SignalXYSourceDynamic(int cachePeriod = 1024)
    {
        Xs = [];
        Ys = [];
        MinMaxCache = new IncrementalMinMaxCache(Ys, cachePeriod);
        MaximumIndex = -1;
    }

    /// <summary>
    /// Create a dynamic data source pre-seeded with data
    /// </summary>
    public SignalXYSourceDynamic(double[] xs, double[] ys, int cachePeriod = 1024)
    {
        if (xs is null) throw new ArgumentNullException(nameof(xs));
        if (ys is null) throw new ArgumentNullException(nameof(ys));
        if (xs.Length != ys.Length)
            throw new ArgumentException($"{nameof(xs)} and {nameof(ys)} must have equal length");

        Xs = new List<double>(xs);
        Ys = new List<double>(ys);
        MinMaxCache = new IncrementalMinMaxCache(Ys, cachePeriod);
        MaximumIndex = Xs.Count - 1;
    }

    /// <summary>
    /// Append a single data point. X values must remain ascending.
    /// </summary>
    public void Add(double x, double y)
    {
        Xs.Add(x);
        Ys.Add(y);
        MaximumIndex = Xs.Count - 1;
    }

    /// <summary>
    /// Append a batch of data points. X values must remain ascending.
    /// </summary>
    public void AddRange(double[] xs, double[] ys)
    {
        if (xs is null) throw new ArgumentNullException(nameof(xs));
        if (ys is null) throw new ArgumentNullException(nameof(ys));
        if (xs.Length != ys.Length)
            throw new ArgumentException($"{nameof(xs)} and {nameof(ys)} must have equal length");

        Xs.AddRange(xs);
        Ys.AddRange(ys);
        MaximumIndex = Xs.Count - 1;
    }

    /// <summary>
    /// Remove all data and reset the cache
    /// </summary>
    public void Clear()
    {
        Xs.Clear();
        Ys.Clear();
        MinMaxCache.Reset();
        MinimumIndex = 0;
        MaximumIndex = -1;
    }

    public AxisLimits GetAxisLimits()
    {
        if (Xs.Count == 0)
            return AxisLimits.NoLimits;

        double xMin = Xs[MinimumIndex] * XScale + XOffset;
        double xMax = Xs[MaximumIndex] * XScale + XOffset;
        CoordinateRange xRange = new(xMin, xMax);

        // Use the incremental cache for fast Y range
        MinMaxCache.Update();
        SignalRangeY yRaw = MinMaxCache.GetMinMax(MinimumIndex, MaximumIndex + 1);
        CoordinateRange yRange = new(yRaw.Min * YScale + YOffset, yRaw.Max * YScale + YOffset);

        return Rotated
            ? new AxisLimits(yRange, xRange)
            : new AxisLimits(xRange, yRange);
    }

    public IReadOnlyList<Pixel> GetPixelsToDraw(RenderPack rp, IAxes axes, ConnectStyle connectStyle)
    {
        MinMaxCache.Update();

        IReadOnlyList<Pixel> pixels = Rotated
            ? GetPixelsToDrawVertically(rp, axes, connectStyle)
            : GetPixelsToDrawHorizontally(rp, axes, connectStyle);

        if (MarkerStyle is not null)
            MarkerStyle.Size = LastRenderUsedDirectDraw ? DirectDrawMarkerSize : 0;

        return pixels;
    }

    private Pixel[] GetPixelsToDrawHorizontally(RenderPack rp, IAxes axes, ConnectStyle connectStyle)
    {
        (Pixel[] PointBefore, int dataIndexFirst) = GetFirstPointX(axes);
        (Pixel[] PointAfter, int dataIndexLast) = GetLastPointX(axes);
        IndexRange visibleRange = new(dataIndexFirst, dataIndexLast);

        if (visibleRange.IsValid && (Xs[dataIndexFirst] > Xs[dataIndexLast]))
            throw new InvalidDataException("Xs must contain only ascending values. " +
                $"The value at index {dataIndexFirst} ({Xs[dataIndexFirst]}) is greater than the value at index {dataIndexLast} ({Xs[dataIndexLast]})");

        Pixel[] leftOutsidePoint = PointBefore, rightOutsidePoint = PointAfter;
        if (axes.XAxis.Range.Span < 0)
        {
            leftOutsidePoint = PointAfter;
            rightOutsidePoint = PointBefore;
        }

        int columnCount = (int)Math.Ceiling(rp.DataRect.Width);
        List<Pixel> pixelList = new(leftOutsidePoint.Length + columnCount * 4 + rightOutsidePoint.Length);

        for (int i = 0; i < leftOutsidePoint.Length; i++)
            pixelList.Add(leftOutsidePoint[i]);

        if (visibleRange.Length > 0)
        {
            // Fast-path: when zoomed in enough that visible data points fit the pixel columns,
            // iterate data directly — no sampling, no binary searches per column.
            if (visibleRange.Length <= columnCount)
            {
                LastRenderUsedDirectDraw = true;
                for (int i = dataIndexFirst; i <= dataIndexLast; i++)
                {
                    float px = axes.GetPixelX(Xs[i] * XScale + XOffset);
                    float py = axes.GetPixelY(Ys[i] * YScale + YOffset);
                    pixelList.Add(new Pixel(px, py));
                }
            }
            else
            {
                LastRenderUsedDirectDraw = false;
                double unitsPerPixelX = axes.XAxis.Width / rp.DataRect.Width;
                double axisMin = axes.XAxis.Min;
                float dataRectLeft = rp.DataRect.Left;

                for (int pxColumn = 0; pxColumn < columnCount; pxColumn++)
                {
                    float xPixel = pxColumn + dataRectLeft;
                    double start = axisMin + unitsPerPixelX * pxColumn;
                    double end = start + unitsPerPixelX;

                    var (startIndex, _) = SearchIndex(start, visibleRange);
                    var (endIndex, _) = SearchIndex(end, visibleRange);
                    int pointsInRange = Math.Abs(endIndex - startIndex);

                    if (pointsInRange == 0)
                        continue;

                    int firstIndex = startIndex < endIndex ? startIndex : startIndex - 1;
                    int lastIndex = startIndex < endIndex ? endIndex - 1 : endIndex;
                    int minIdx = Math.Min(firstIndex, lastIndex);
                    int maxIdx = Math.Max(firstIndex, lastIndex);

                    EmitPixelColumnHorizontal(pixelList, axes, xPixel, minIdx, maxIdx, firstIndex, lastIndex, pointsInRange);
                }
            }
        }

        for (int i = 0; i < rightOutsidePoint.Length; i++)
            pixelList.Add(rightOutsidePoint[i]);

        Pixel[] points = pixelList.ToArray();

        if (leftOutsidePoint.Length > 0)
            SignalInterpolation.InterpolateBeforeX(rp, points, connectStyle);

        if (rightOutsidePoint.Length > 0)
            SignalInterpolation.InterpolateAfterX(rp, points, connectStyle);

        return points;
    }

    private Pixel[] GetPixelsToDrawVertically(RenderPack rp, IAxes axes, ConnectStyle connectStyle)
    {
        (Pixel[] PointBefore, int dataIndexFirst) = GetFirstPointY(axes);
        (Pixel[] PointAfter, int dataIndexLast) = GetLastPointY(axes);
        IndexRange visibleRange = new(dataIndexFirst, dataIndexLast);

        if (visibleRange.IsValid && (Xs[dataIndexFirst] > Xs[dataIndexLast]))
            throw new InvalidDataException("Xs must contain only ascending values. " +
                $"The value at index {dataIndexFirst} ({Xs[dataIndexFirst]}) is greater than the value at index {dataIndexLast} ({Xs[dataIndexLast]})");

        Pixel[] bottomOutsidePoint = PointBefore, topOutsidePoint = PointAfter;
        if (axes.YAxis.Range.Span < 0)
        {
            bottomOutsidePoint = PointAfter;
            topOutsidePoint = PointBefore;
        }

        int rowCount = (int)Math.Ceiling(rp.DataRect.Height);
        List<Pixel> pixelList = new(bottomOutsidePoint.Length + rowCount * 4 + topOutsidePoint.Length);

        for (int i = 0; i < bottomOutsidePoint.Length; i++)
            pixelList.Add(bottomOutsidePoint[i]);

        if (visibleRange.Length > 0)
        {
            // Fast-path: when zoomed in enough that visible data points fit the pixel rows,
            // iterate data directly — no sampling, no binary searches per row.
            if (visibleRange.Length <= rowCount)
            {
                LastRenderUsedDirectDraw = true;
                for (int i = dataIndexFirst; i <= dataIndexLast; i++)
                {
                    float px = axes.GetPixelX(Ys[i] * YScale + YOffset);
                    float py = axes.GetPixelY(Xs[i] * XScale + XOffset);
                    pixelList.Add(new Pixel(px, py));
                }
            }
            else
            {
                LastRenderUsedDirectDraw = false;
                double unitsPerPixelY = axes.YAxis.Height / rp.DataRect.Height;
                double axisMin = axes.YAxis.Min;
                float dataRectBottom = rp.DataRect.Bottom;

                for (int pxRow = 0; pxRow < rowCount; pxRow++)
                {
                    float yPixel = dataRectBottom - pxRow;
                    double start = axisMin + unitsPerPixelY * pxRow;
                    double end = start + unitsPerPixelY;

                    var (startIndex, _) = SearchIndex(start, visibleRange);
                    var (endIndex, _) = SearchIndex(end, visibleRange);
                    int pointsInRange = Math.Abs(endIndex - startIndex);

                    if (pointsInRange == 0)
                        continue;

                    int firstIndex = startIndex < endIndex ? startIndex : startIndex - 1;
                    int lastIndex = startIndex < endIndex ? endIndex - 1 : endIndex;
                    int minIdx = Math.Min(firstIndex, lastIndex);
                    int maxIdx = Math.Max(firstIndex, lastIndex);

                    EmitPixelColumnVertical(pixelList, axes, yPixel, minIdx, maxIdx, firstIndex, lastIndex, pointsInRange);
                }
            }
        }

        for (int i = 0; i < topOutsidePoint.Length; i++)
            pixelList.Add(topOutsidePoint[i]);

        Pixel[] points = pixelList.ToArray();

        if (bottomOutsidePoint.Length > 0)
            SignalInterpolation.InterpolateBeforeY(rp, points, connectStyle);

        if (topOutsidePoint.Length > 0)
            SignalInterpolation.InterpolateAfterY(rp, points, connectStyle);

        return points;
    }

    /// <summary>
    /// Emit pixel(s) for a single pixel column in horizontal mode using the current sampling strategy
    /// </summary>
    private void EmitPixelColumnHorizontal(List<Pixel> pixelList, IAxes axes, float xPixel, int minIdx, int maxIdx, int firstIndex, int lastIndex, int pointsInRange)
    {
        switch (SamplingMode)
        {
            case SamplingMode.Average:
            {
                double sum = 0;
                for (int i = minIdx; i <= maxIdx; i++)
                    sum += Ys[i];
                double avg = sum / (maxIdx - minIdx + 1);
                pixelList.Add(new Pixel(xPixel, axes.GetPixelY(avg * YScale + YOffset)));
                break;
            }

            case SamplingMode.Min:
            {
                SignalRangeY range = MinMaxCache.GetMinMax(minIdx, maxIdx + 1);
                pixelList.Add(new Pixel(xPixel, axes.GetPixelY(range.Min * YScale + YOffset)));
                break;
            }

            case SamplingMode.Max:
            {
                SignalRangeY range = MinMaxCache.GetMinMax(minIdx, maxIdx + 1);
                pixelList.Add(new Pixel(xPixel, axes.GetPixelY(range.Max * YScale + YOffset)));
                break;
            }

            case SamplingMode.Sum:
            {
                double sum = 0;
                for (int i = minIdx; i <= maxIdx; i++)
                    sum += Ys[i];
                pixelList.Add(new Pixel(xPixel, axes.GetPixelY(sum * YScale + YOffset)));
                break;
            }

            default: // MinMax: enter / min / max / exit
            {
                pixelList.Add(new Pixel(xPixel, axes.GetPixelY(Ys[firstIndex] * YScale + YOffset)));

                if (pointsInRange > 2)
                {
                    SignalRangeY range = MinMaxCache.GetMinMax(minIdx, maxIdx + 1);
                    double scaledMin = range.Min * YScale + YOffset;
                    double scaledMax = range.Max * YScale + YOffset;

                    if (Ys[firstIndex] > Ys[lastIndex])
                    {
                        pixelList.Add(new Pixel(xPixel, axes.GetPixelY(scaledMax)));
                        pixelList.Add(new Pixel(xPixel, axes.GetPixelY(scaledMin)));
                    }
                    else
                    {
                        pixelList.Add(new Pixel(xPixel, axes.GetPixelY(scaledMin)));
                        pixelList.Add(new Pixel(xPixel, axes.GetPixelY(scaledMax)));
                    }
                }

                if (pointsInRange > 1)
                {
                    pixelList.Add(new Pixel(xPixel, axes.GetPixelY(Ys[lastIndex] * YScale + YOffset)));
                }
                break;
            }
        }
    }

    /// <summary>
    /// Emit pixel(s) for a single pixel row in vertical (rotated) mode using the current sampling strategy
    /// </summary>
    private void EmitPixelColumnVertical(List<Pixel> pixelList, IAxes axes, float yPixel, int minIdx, int maxIdx, int firstIndex, int lastIndex, int pointsInRange)
    {
        switch (SamplingMode)
        {
            case SamplingMode.Average:
            {
                double sum = 0;
                for (int i = minIdx; i <= maxIdx; i++)
                    sum += Ys[i];
                double avg = sum / (maxIdx - minIdx + 1);
                pixelList.Add(new Pixel(axes.GetPixelX(avg * YScale + YOffset), yPixel));
                break;
            }

            case SamplingMode.Min:
            {
                SignalRangeY range = MinMaxCache.GetMinMax(minIdx, maxIdx + 1);
                pixelList.Add(new Pixel(axes.GetPixelX(range.Min * YScale + YOffset), yPixel));
                break;
            }

            case SamplingMode.Max:
            {
                SignalRangeY range = MinMaxCache.GetMinMax(minIdx, maxIdx + 1);
                pixelList.Add(new Pixel(axes.GetPixelX(range.Max * YScale + YOffset), yPixel));
                break;
            }

            case SamplingMode.Sum:
            {
                double sum = 0;
                for (int i = minIdx; i <= maxIdx; i++)
                    sum += Ys[i];
                pixelList.Add(new Pixel(axes.GetPixelX(sum * YScale + YOffset), yPixel));
                break;
            }

            default: // MinMax: enter / min / max / exit
            {
                pixelList.Add(new Pixel(axes.GetPixelX(Ys[firstIndex] * YScale + YOffset), yPixel));

                if (pointsInRange > 2)
                {
                    SignalRangeY range = MinMaxCache.GetMinMax(minIdx, maxIdx + 1);
                    double scaledMin = range.Min * YScale + YOffset;
                    double scaledMax = range.Max * YScale + YOffset;

                    if (Ys[firstIndex] > Ys[lastIndex])
                    {
                        pixelList.Add(new Pixel(axes.GetPixelX(scaledMax), yPixel));
                        pixelList.Add(new Pixel(axes.GetPixelX(scaledMin), yPixel));
                    }
                    else
                    {
                        pixelList.Add(new Pixel(axes.GetPixelX(scaledMin), yPixel));
                        pixelList.Add(new Pixel(axes.GetPixelX(scaledMax), yPixel));
                    }
                }

                if (pointsInRange > 1)
                {
                    pixelList.Add(new Pixel(axes.GetPixelX(Ys[lastIndex] * YScale + YOffset), yPixel));
                }
                break;
            }
        }
    }

    private (Pixel[] pointBefore, int firstIndex) GetFirstPointX(IAxes axes)
    {
        if (Xs.Count <= 1)
            return ([], MinimumIndex);

        var (firstPointIndex, _) = SearchIndex(axes.XAxis.Range.Span > 0 ? axes.XAxis.Min : axes.XAxis.Max);

        if (firstPointIndex > MinimumIndex)
        {
            float beforeX = axes.GetPixelX(Xs[firstPointIndex - 1] * XScale + XOffset);
            float beforeY = axes.GetPixelY(Ys[firstPointIndex - 1] * YScale + YOffset);
            return ([new Pixel(beforeX, beforeY)], firstPointIndex);
        }

        return ([], MinimumIndex);
    }

    private (Pixel[] pointBefore, int firstIndex) GetFirstPointY(IAxes axes)
    {
        if (Xs.Count <= 1)
            return ([], MinimumIndex);

        var (firstPointIndex, _) = SearchIndex(axes.YAxis.Range.Span > 0 ? axes.YAxis.Min : axes.YAxis.Max);

        if (firstPointIndex > MinimumIndex)
        {
            float beforeY = axes.GetPixelY(Xs[firstPointIndex - 1] * XScale + XOffset);
            float beforeX = axes.GetPixelX(Ys[firstPointIndex - 1] * YScale + YOffset);
            return ([new Pixel(beforeX, beforeY)], firstPointIndex);
        }

        return ([], MinimumIndex);
    }

    private (Pixel[] pointAfter, int lastIndex) GetLastPointX(IAxes axes)
    {
        if (Xs.Count <= 1)
            return ([], MaximumIndex);

        var (lastPointIndex, _) = SearchIndex(axes.XAxis.Range.Span > 0 ? axes.XAxis.Max : axes.XAxis.Min);

        if (lastPointIndex <= MaximumIndex)
        {
            float afterX = axes.GetPixelX(Xs[lastPointIndex] * XScale + XOffset);
            float afterY = axes.GetPixelY(Ys[lastPointIndex] * YScale + YOffset);
            return ([new Pixel(afterX, afterY)], lastPointIndex - 1);
        }

        return ([], MaximumIndex);
    }

    private (Pixel[] pointAfter, int lastIndex) GetLastPointY(IAxes axes)
    {
        if (Xs.Count <= 1)
            return ([], MaximumIndex);

        var (lastPointIndex, _) = SearchIndex(axes.YAxis.Range.Span > 0 ? axes.YAxis.Max : axes.YAxis.Min);

        if (lastPointIndex <= MaximumIndex)
        {
            float afterY = axes.GetPixelY(Xs[lastPointIndex] * XScale + XOffset);
            float afterX = axes.GetPixelX(Ys[lastPointIndex] * YScale + YOffset);
            return ([new Pixel(afterX, afterY)], lastPointIndex - 1);
        }

        return ([], MaximumIndex);
    }

    /// <summary>
    /// Search the index associated with the given X position
    /// </summary>
    private (int SearchedPosition, int LimitedIndex) SearchIndex(double x)
    {
        IndexRange range = new(MinimumIndex, MaximumIndex);
        return SearchIndex(x, range);
    }

    /// <summary>
    /// Search the index associated with the given X position limited to the given range
    /// </summary>
    private (int SearchedPosition, int LimitedIndex) SearchIndex(double x, IndexRange indexRange)
    {
        if (indexRange.Length <= 0)
            return (indexRange.Min, indexRange.Min);

        int index = Xs.BinarySearch(indexRange.Min, indexRange.Length, (x - XOffset) / XScale, BinarySearchComparer.Instance);

        if (index < 0)
        {
            index = ~index;
        }

        return (SearchedPosition: index, LimitedIndex: index > indexRange.Max ? indexRange.Max : index);
    }

    private int GetIndex(double x)
    {
        IndexRange range = new(MinimumIndex, MaximumIndex);
        var (_, index) = SearchIndex(x, range);
        return index;
    }

    public DataPoint GetNearest(Coordinates mouseLocation, RenderDetails renderInfo, float maxDistance = 15)
        => DataSourceUtilities.GetNearestFast(this, mouseLocation, renderInfo, maxDistance);

    public DataPoint GetNearestX(Coordinates mouseLocation, RenderDetails renderInfo, float maxDistance = 15)
        => DataSourceUtilities.GetNearestXFast(this, mouseLocation, renderInfo, maxDistance);

    Coordinates IDataSource.GetCoordinate(int index)
    {
        double x = NumericConversion.GenericToDouble(Xs, index);
        double y = NumericConversion.GenericToDouble(Ys, index);
        return Rotated ? new Coordinates(y, x) : new Coordinates(x, y);
    }

    Coordinates IDataSource.GetCoordinateScaled(int index)
    {
        double x = DataSourceUtilities.ScaleXY(Xs, index, XScale, XOffset);
        double y = DataSourceUtilities.ScaleXY(Ys, index, YScale, YOffset);
        return Rotated ? new Coordinates(y, x) : new Coordinates(x, y);
    }

    int IDataSource.GetXClosestIndex(Coordinates mouseLocation)
    {
        return Rotated
            ? GetIndex(mouseLocation.Y)
            : GetIndex(mouseLocation.X);
    }

    double IDataSource.GetX(int index)
    {
        return Rotated
            ? NumericConversion.GenericToDouble(Ys, index)
            : NumericConversion.GenericToDouble(Xs, index);
    }

    double IDataSource.GetXScaled(int index)
    {
        return Rotated
            ? DataSourceUtilities.ScaleXY(Ys, index, YScale, YOffset)
            : DataSourceUtilities.ScaleXY(Xs, index, XScale, XOffset);
    }

    double IDataSource.GetY(int index)
    {
        return Rotated
            ? NumericConversion.GenericToDouble(Xs, index)
            : NumericConversion.GenericToDouble(Ys, index);
    }

    double IDataSource.GetYScaled(int index)
    {
        return Rotated
            ? DataSourceUtilities.ScaleXY(Xs, index, XScale, XOffset)
            : DataSourceUtilities.ScaleXY(Ys, index, YScale, YOffset);
    }

    bool IDataSource.IsSorted() => true;
}
