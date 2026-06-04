// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace ExecutionEngine
{
    public class DynamicAssemblyLoader : AssemblyLoadContext
    {
        public DynamicAssemblyLoader() : base(isCollectible: true) { }

        public Assembly LoadFromBytes(byte[] assemblyBytes)
        {
            return LoadFromStream(new MemoryStream(assemblyBytes));
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // Default implementation does nothing.
            return null;
        }
    }
}
