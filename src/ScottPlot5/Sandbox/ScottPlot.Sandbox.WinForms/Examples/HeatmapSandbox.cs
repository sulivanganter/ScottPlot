﻿using ScottPlot;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinForms.Examples
{
    public partial class HeatmapSandbox : Form, IExampleForm
    {
        public string SandboxTitle => "Heatmap Sandbox";

        public string SandboxDescription => "Heatmap Sandbox";

        public HeatmapSandbox()
        {
            InitializeComponent();

            double[,] data = Generate.Sin2D(3, 2);
            var hm = formsPlot1.Plot.Plottables.AddHeatmap(data);

            formsPlot1.Refresh();
        }
    }
}