### Performance Explorer

Performance Explorer is a tool to examine the impact of changing JIT behavior on key methods in a benchmark.
It is currently specialized to explore varying the CSEs in the most dominant Tier-1 method of a benchmark.

### Setup

This tool currently only works on Windows.

To run Peformance Explorer, you must have local enlistments of:
* [the runtime repo](https://github.com/dotnet/runtime)
* [the performance repo](https://github.com/dotnet/performance)
* [instructions retired explorer](https://github.com/AndyAyersMS/InstructionsRetiredExplorer)

You will need to do both release and checked builds of the runtime repo, and create the associated
test directories (aka Core_Roots).

You will need to build the instructions retired explorer.

You will need to modify file paths in the performance explorer code to refer to the locations
of the above repos and builds, an to specify a results directory.

Finally, you will likely want to customize the list of benchmarks to explore; the names of these
are the names used in the performance repo. Note the names often contain quotes or other special
characters so you will likely need to read up on how to handle these when they appear in C# literal strings.

Once you have made these modifications, you can then build the performance explorer.

The tool must be run as admin, in order to perform the necessary profiling.

### How It Works

For each benchmark in the list, performance explorer will:
* run the benchmark from the perf directory, with `-p ETW` so that profile data is collected
* parse the profile data using instructions retired explorer to find the hot methods
* also parse the BenchmarkDotNet json to determine the peformance of the benchmark
* determine if there's a hot method that would be a good candidate for exploration. Currently we look for a Tier-1 method that accounts for at least 20% of the benchmark time.
* if there is a suitable hot method:
  * run an SPMI collection for that benchmark
  * use that SPMI to get an assembly listing for the hot method
  * determine from that listing how many CSEs were performed (the "default set" of N CSEs)
  * if there were any CSEs, start the experimentation process:
    * run the benchmark with all CSEs disabled (0 CSEs), and measure perf. Add to the exploration queue.
    * then, repeatedly, until we have run out of experiment to try, or hit some predetermined limit
      * pick the best performing experiment from the queue
      * Determine which CSEs in the default set were not done in the experement. Say ther are M (<=N) of these
      * Run M more experiments, each adding one of the missing CSEs

Each benchmark's data is stored in a subfolder in the results directory; we also create disassembly for all the 
experiemnts tried, and copies of all the intermediate files.

There is also a master results.csv that has data from all experiments in all benchmarks, suitable for use
in excel or as input to a machine learning algorithm.

If you re-run the tool with the same benchmark list and results directory, it will use the cached copies of
data and won't re-run the experiments.

If along the way anything goes wrong then an "error.txt" file is added to the results subdirectory for
that benchmark, and future runs will skip that benchmark.

So say there are 2 CSEs by default. The explorer will run:
* one experiment with 0 CSEs
* two experiments each with 1 CSE
* one experiment with 2 CSEs
and then stop as all possibilities have been explored.

For larger values of N the number of possible experiments 2^N grows rapidly and we cannot hope to explore
the full space. The exploration process is intended to prioritize for those experiments that likely have
the largest impact on performance.

### Future Enhancements

* add option to offload benchmark runs to the perf lab
* capture more details about CSEs so we can use the data to develop better CSE heuristics
* generalize the experiment processing to allow other kinds of experiments
* parameterize the config settings so we don't need to modify the sources
* add options to characterize the noise level of benchmarks and (perhaps) do more runs if noisy
* leverage SPMI instead of perf runs, if we can trust perf scores

