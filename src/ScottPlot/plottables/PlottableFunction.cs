﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.Text;
using ScottPlot.Config;
using ScottPlot.Drawing;

namespace ScottPlot
{
    public class PlottableFunction : Plottable/*, IPlottable*/
    {
        public Func<double, double?> function;

        // TODO: Capitalize these fields
        public double lineWidth = 1;
        public LineStyle lineStyle = LineStyle.Solid;
        public string label;
        public Color color = Color.Black;

        public PlottableFunction(Func<double, double?> function)
        {
            this.function = function;
        }

        public override AxisLimits2D GetLimits()
        {
            double max = double.NegativeInfinity;
            double min = double.PositiveInfinity;

            foreach (double x in DataGen.Range(-10, 10, .1))
            {
                double? y = function(x);
                if (y != null)
                {
                    max = Math.Max(max, y.Value);
                    min = Math.Min(min, y.Value);
                }
            }

            // TODO: X limits should probably be null or NaN
            double[] limits = { -10, 10, min, max };

            return new AxisLimits2D(limits);
        }

        private int PointCount;

        public override int GetPointCount() => PointCount;

        // TODO: implement after scatter plots are refactored to use this render method
        /*
        public void Render(PlotDimensions dims, Bitmap bmp, bool lowQuality = false)
        {
            throw new NotImplementedException();
        }
        */

        public string ValidationErrorMessage { get; private set; }
        public bool IsValidData(bool deepValidation = false)
        {
            ValidationErrorMessage = (function is null) ? "function cannot be null" : "";
            return string.IsNullOrWhiteSpace(ValidationErrorMessage);
        }

        public override void Render(Settings settings)
        {
            double unitsPerColumn = settings.xAxisUnitsPerPixel;
            double minRenderedX = settings.axes.limits[0];
            double maxRenderedX = settings.axes.limits[1];
            PointCount = (int)Math.Ceiling((maxRenderedX - minRenderedX) / unitsPerColumn);

            List<double> xList = new List<double>();
            List<double> yList = new List<double>();

            for (int columnIndex = 0; columnIndex < PointCount; columnIndex++)
            {
                double x = columnIndex * unitsPerColumn + minRenderedX;
                try
                {
                    double? y = function(x);

                    if (y is null)
                        throw new NoNullAllowedException();

                    if (double.IsNaN(y.Value) || double.IsInfinity(y.Value))
                        throw new ArithmeticException("not a real number");

                    xList.Add(x);
                    yList.Add(y.Value);
                }
                catch (Exception e) //Domain error, such log(-1) or 1/0
                {
                    Debug.WriteLine($"Y({x}) failed because {e}");
                    continue;
                }
            }

            // create a temporary scatter plot and use it for rendering
            double[] xs = xList.ToArray();
            double[] ys = yList.ToArray();
            var scatter = new PlottableScatter(xs, ys, color, lineWidth, 0, label, null, null, 0, 0, false, MarkerShape.none, lineStyle);
            scatter.Render(settings);
        }

        public override string ToString()
        {
            string label = string.IsNullOrWhiteSpace(this.label) ? "" : $" ({this.label})";
            return $"PlottableFunction{label} displaying {GetPointCount()} points";
        }

        public override LegendItem[] GetLegendItems()
        {
            var singleLegendItem = new LegendItem(label, color, lineStyle, lineWidth, MarkerShape.none);
            return new LegendItem[] { singleLegendItem };
        }
    }
}
