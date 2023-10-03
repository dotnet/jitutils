// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.


using PerformanceExplorer;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System;
using System.Xml.Serialization;

public class CseExperiment
{
    public BenchmarkInfo Benchmark { get; set; }
    public CseExperiment Baseline { get; set; }
    public HotFunction Method { get; set; }
    public uint Mask { get; set; }
    public uint NumCse { get; set; }
    public uint CodeSize { get; set; }
    public double PerfScore { get; set; }
    public double Perf { get; set; }
    public bool Explored { get; set; }

    public string Hash { get; set; }

    public uint Index { get; set; }

    public bool IsImprovement { get { return Perf < Baseline.Perf; } }

    public static string Schema
    {
        get
        {
            return "Benchmark,Index,Mask,NumCse,CodeSize,PerfScore,PerfScoreRatio,Perf,PerfRatio";
        }
    }

    public string Info
    {
        get
        {
            double perfRatio = (Baseline == null) ? 1.0 : Perf / Baseline.Perf;
            double perfScoreRatio = (Baseline == null) ? 1.0 : PerfScore / Baseline.PerfScore;
            return $"{Benchmark.CsvName},{Index},{Mask:x8},{NumCse},{CodeSize},{PerfScore:F2},{perfScoreRatio:F3},{Perf:F4},{perfRatio:F3}";
        }
    }
}