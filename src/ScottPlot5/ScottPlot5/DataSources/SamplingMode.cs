namespace ScottPlot.DataSources;

/// <summary>
/// Determines the downsampling strategy used when rendering pixel columns
/// </summary>
public enum SamplingMode
{
    /// <summary>
    /// Preserves peaks and valleys by emitting enter/min/max/exit points per pixel column (4 points max).
    /// Best for waveforms and data where extremes matter.
    /// </summary>
    MinMax,

    /// <summary>
    /// Emits a single averaged point per pixel column.
    /// Best for smoothed trend visualization.
    /// </summary>
    Average,

    /// <summary>
    /// Emits only the minimum value per pixel column.
    /// Useful for visualizing lower bounds.
    /// </summary>
    Min,

    /// <summary>
    /// Emits only the maximum value per pixel column.
    /// Useful for visualizing upper bounds.
    /// </summary>
    Max,

    /// <summary>
    /// Emits the sum of values per pixel column.
    /// Useful for aggregate/cumulative visualizations.
    /// </summary>
    Sum,
}
