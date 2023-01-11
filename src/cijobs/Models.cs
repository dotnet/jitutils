// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace ManagedCodeGen
{
    internal class Artifact
    {
        public string fileName { get; set; }
        public string relativePath { get; set; }
    }

    internal class Revision
    {
        public string SHA1 { get; set; }
    }

    internal class Action
    {
        public Revision lastBuiltRevision { get; set; }
    }

    internal class BuildInfo
    {
        public List<Action> actions { get; set; }
        public List<Artifact> artifacts { get; set; }
        public string result { get; set; }
    }

    internal class Job
    {
        public string name { get; set; }
        public string url { get; set; }
    }

    internal class ProductJobs
    {
        public List<Job> jobs { get; set; }
    }

    internal class Build
    {
        public int number { get; set; }
        public string url { get; set; }
        public BuildInfo info { get; set; }
    }

    internal class JobBuilds
    {
        public List<Build> builds { get; set; }
        public Build lastSuccessfulBuild { get; set; }
    }
}
