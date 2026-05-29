namespace ScottPlot.DataSources;

/// <summary>
/// Incremental MinMax cache for dynamically growing data.
/// Only processes newly completed blocks when <see cref="Update"/> is called.
/// Block-aligned queries reduce <see cref="GetMinMax"/> cost to O(CachePeriod) instead of O(N).
/// </summary>
public class IncrementalMinMaxCache
{
    private readonly IReadOnlyList<double> Data;
    private readonly List<SignalRangeY> Cache = [];

    /// <summary>
    /// Number of data points per cached block
    /// </summary>
    public int CachePeriod { get; }

    /// <summary>
    /// Number of data points covered by completed cache blocks
    /// </summary>
    public int CachedCount => Cache.Count * CachePeriod;

    public IncrementalMinMaxCache(IReadOnlyList<double> data, int cachePeriod = 1024)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        if (cachePeriod < 1)
            throw new ArgumentOutOfRangeException(nameof(cachePeriod), "Cache period must be at least 1.");

        CachePeriod = cachePeriod;
        Update();
    }

    /// <summary>
    /// Process any new complete blocks that have appeared since the last call.
    /// Call this before rendering to pick up newly appended data.
    /// </summary>
    public void Update()
    {
        int totalBlocks = Data.Count / CachePeriod;

        for (int b = Cache.Count; b < totalBlocks; b++)
        {
            int blockStart = b * CachePeriod;
            double min = Data[blockStart];
            double max = Data[blockStart];

            for (int j = 1; j < CachePeriod; j++)
            {
                double v = Data[blockStart + j];
                if (v < min) min = v;
                else if (v > max) max = v;
            }

            Cache.Add(new SignalRangeY(min, max));
        }
    }

    /// <summary>
    /// Clear all cached blocks. Call after the underlying data is cleared.
    /// </summary>
    public void Reset()
    {
        Cache.Clear();
    }

    /// <summary>
    /// Get the min/max over a half-open range [start, end).
    /// Uses cached blocks for aligned interior and linear scan for head/tail.
    /// </summary>
    public SignalRangeY GetMinMax(int start, int end)
    {
        if (start >= end)
            return new SignalRangeY(double.NaN, double.NaN);

        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        int period = CachePeriod;

        // First aligned block boundary after start
        int blockStart = ((start + period - 1) / period); // ceiling division
        int alignedStart = blockStart * period;

        // Last aligned block boundary before end
        int blockEnd = end / period;
        int alignedEnd = blockEnd * period;

        if (alignedStart >= end || alignedEnd <= start || blockStart >= blockEnd || blockEnd > Cache.Count)
        {
            // Range too small for cache or cache doesn't cover — linear scan
            for (int i = start; i < end; i++)
            {
                double v = Data[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
            return new SignalRangeY(min, max);
        }

        // Head: [start, alignedStart)
        for (int i = start; i < alignedStart; i++)
        {
            double v = Data[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        // Cached blocks: [blockStart, blockEnd)
        for (int b = blockStart; b < blockEnd; b++)
        {
            SignalRangeY cached = Cache[b];
            if (cached.Min < min) min = cached.Min;
            if (cached.Max > max) max = cached.Max;
        }

        // Tail: [alignedEnd, end)
        for (int i = alignedEnd; i < end; i++)
        {
            double v = Data[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        return new SignalRangeY(min, max);
    }
}
