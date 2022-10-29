// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace ManagedCodeGen
{
    // Allow Linq to be able to sum up MetricCollections
    public static class IEnumerableExtensions
    {
        public static MetricCollection Sum(this IEnumerable<MetricCollection> source)
        {
            MetricCollection result = new MetricCollection();

            foreach (MetricCollection s in source)
            {
                result.Add(s);
            }

            return result;
        }

        public static MetricCollection Sum<T>(this IEnumerable<T> source, Func<T, MetricCollection> selector)
        {
            return source.Select(x => selector(x)).Sum();
        }
    }
}
