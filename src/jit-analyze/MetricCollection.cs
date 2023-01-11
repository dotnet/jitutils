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

        [JsonInclude]
        private Metric[] metrics;

        public MetricCollection()
        {
            metrics = new Metric[s_metrics.Length];
            for (int i = 0; i < s_metrics.Length; i++)
            {
                metrics[i] = s_metrics[i].Clone();
            }
        }

        public MetricCollection(MetricCollection other) : this()
        {
            this.SetValueFrom(other);
        }

        public static IEnumerable<Metric> AllMetrics => s_metrics;

        public Metric GetMetric(string metricName)
        {
            int index;
            if (s_metricNameToIndex.TryGetValue(metricName, out index))
            {
                return metrics[index];
            }
            return null;
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
            for (int i = 0; i < metrics.Length; i++)
            {
                metrics[i].Add(other.metrics[i]);
            }
        }

        public void Add(string metricName, double value)
        {
            Metric m = GetMetric(metricName);
            m.Value += value;
        }

        public void Sub(MetricCollection other)
        {
            for (int i = 0; i < metrics.Length; i++)
            {
                metrics[i].Sub(other.metrics[i]);
            }
        }

        public void Rel(MetricCollection other)
        {
            for (int i = 0; i < metrics.Length; i++)
            {
                metrics[i].Rel(other.metrics[i]);
            }
        }

        public void SetValueFrom(MetricCollection other)
        {
            for (int i = 0; i < metrics.Length; i++)
            {
                metrics[i].SetValueFrom(other.metrics[i]);
            }
        }

        public bool IsZero()
        {
            for (int i = 0; i < metrics.Length; i++)
            {
                if (metrics[i].Value != 0) return false;
            }
            return true;
        }
    }
}