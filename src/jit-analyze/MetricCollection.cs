// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace ManagedCodeGen
{
    public class MetricCollection
    {
        private static Dictionary<string, int> s_metricNameToIndex;
        private static Metric[] s_metrics;

        static MetricCollection()
        {
            var derivedType = typeof(Metric);
            var currentAssembly = Assembly.GetAssembly(derivedType);
            s_metrics = currentAssembly.GetTypes()
                .Where(t => t != derivedType && derivedType.IsAssignableFrom(t))
                .Select(t => currentAssembly.CreateInstance(t.FullName)).Cast<Metric>().ToArray();

            s_metricNameToIndex = new Dictionary<string, int>(s_metrics.Length);

            for (int i = 0; i < s_metrics.Length; i++)
            {
                Metric m = s_metrics[i];
                s_metricNameToIndex[m.Name] = i;
            }
        }

        private readonly double[] _values;

        [JsonInclude]
        private Metric[] metrics => s_metrics.Select(m => GetMetric(m.Name)).ToArray();

        public MetricCollection()
        {
            _values = new double[s_metrics.Length];
        }

        public MetricCollection(MetricCollection other) : this()
        {
            this.SetValueFrom(other);
        }

        public static IEnumerable<Metric> AllMetrics => s_metrics;

        // Materialize display metadata only for reports; analysis uses the compact values directly.
        public Metric GetMetric(string metricName)
        {
            int index;
            if (s_metricNameToIndex.TryGetValue(metricName, out index))
            {
                Metric metric = s_metrics[index].Clone();
                metric.Value = _values[index];
                return metric;
            }
            return null;
        }

        public double GetValue(string metricName) => _values[s_metricNameToIndex[metricName]];

        public void AddInt(string metricName, int value)
        {
            int index = s_metricNameToIndex[metricName];
            _values[index] = checked((int)_values[index] + value);
        }

        public static bool ValidateMetric(string name)
        {
            return s_metricNameToIndex.TryGetValue(name, out _);
        }

        public static string DisplayName(string metricName)
        {
            int index;
            if (s_metricNameToIndex.TryGetValue(metricName, out index))
            {
                return s_metrics[index].DisplayName;
            }
            return "Unknown metric";
        }

        public static string ListMetrics()
        {
            StringBuilder sb = new StringBuilder();
            bool isFirst = true;
            foreach (string s in s_metricNameToIndex.Keys)
            {
                if (!isFirst) sb.Append(", ");
                sb.Append(s);
                isFirst = false;
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            bool isFirst = true;
            foreach (Metric m in metrics)
            {
                if (!isFirst) sb.Append(", ");
                sb.Append($"{m.Name} {m.Unit} {m.ValueString}");
                isFirst = false;
            }
            return sb.ToString();
        }

        public void Add(MetricCollection other)
        {
            for (int i = 0; i < _values.Length; i++)
            {
                _values[i] += other._values[i];
            }
        }

        public void Add(string metricName, double value)
        {
            _values[s_metricNameToIndex[metricName]] += value;
        }

        public void Sub(MetricCollection other)
        {
            for (int i = 0; i < _values.Length; i++)
            {
                _values[i] -= other._values[i];
            }
        }

        public void Rel(MetricCollection other)
        {
            for (int i = 0; i < _values.Length; i++)
            {
                _values[i] = (_values[i] - other._values[i]) / other._values[i];
            }
        }

        public void SetValueFrom(MetricCollection other)
        {
            other._values.CopyTo(_values, 0);
        }

        public bool IsZero()
        {
            for (int i = 0; i < _values.Length; i++)
            {
                if (_values[i] != 0) return false;
            }
            return true;
        }
    }
}