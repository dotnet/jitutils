using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

// Code to orchestrate Machine Learning for CSEs.
// Leverages SPMI and PerfScores.
// Optimizes via "vanilla" Policy Gradient w/baseline.
//
// Has 3 main modes of operation
// * GatherFeatures: describe the distributions of CSE features
//   Useful for normalizing these distributions, and for looking for candidates with unusual feature combinations.
// * MCMC: Markov Chain Monte Carlo
//   (roughly) CSES at random, to discover the distribution of perf scores, and potential advantage vs current JIT behavior.
// * PolicyGradient: evolve a policy to improve CSE optimization
//   Iteratively update model parameters to reach a local minimum (for perf score, lower is better).
//   Can "minibatch" and average updates
//   Can work on a single method, set of methods, or randomly collected sample
//
// Todo: 
//   Add better way of specifying all the various options
//   Continue streamlining the various diagnostic bits so they don't obscure the computations
//   Allow the Q/V to reflect recent behavior rather than all behavior
//   Record best result from MCMC rather than just best found with stochastic policy exploration
//   Make it work with release builds
//   Keep side log of all activity
//   Consolidate Experiment data into Q/V
//
// Longer Term:  
//   Consider off-policy approaches where MCMC does exploration
//   SPMI batch mode where we give it a set of runs (methods indices + config settings) and it handles the parallelism.
//   (or SPMI server mode where we can just send requests to a long-running instance?)
public class MLCSE
{
    public static string spmiCollection = @"d:\spmi\mch\b8a05f18-503e-47e4-9193-931c50b151d1.windows.x64\aspnet.run.windows.x64.checked.mch";
    public static string checkedCoreRoot = @"c:\repos\runtime0\artifacts\tests\coreclr\Windows.x64.Checked\Tests\Core_Root";
    public static string dumpDir = @"d:\bugs\cse-metrics";

    public static bool showEachRun = false;

    // As we do runs keep track of the sequences and rewards we've seen in this global table.
    // This is Q(s,a)
    //
    public static Dictionary<StateAndAction, StateAndActionData> Q = new Dictionary<StateAndAction, StateAndActionData>();

    // From that we can derive the value of a state V(s) by examining all the Q(s,a)
    //
    public static Dictionary<State, StateData> V = new Dictionary<State, StateData>();

    // We often want to know what the current jit behavior is; this tells us the (terminal)
    // state to use to look up that data.
    //
    public static Dictionary<Method, State> Baseline = new Dictionary<Method, State>();

    // Also keep track of the (terminal) state with the best (lowest) perf score
    //
    public static Dictionary<Method, State> Best = new Dictionary<Method, State>();

    private static int Main(string[] args) =>
    new CliConfiguration(new MLCSECommands(args).UseVersion())
    {
        EnableParseErrorReporting = true
    }.Invoke(args);

    public static MLCSECommands s_commands = new MLCSECommands();

    private static T? Get<T>(CliOption<T> option)
    {
        if (s_commands.Result == null)
        {
            throw new Exception("no parsed result?");
        }
        return s_commands.Result.GetValue(option);
    }

    public static void Run()
    {
        if (s_commands == null) return;

        SPMI.spmiCollection = Get(s_commands.SPMICollection) ?? spmiCollection;
        SPMI.checkedCoreRoot = Get(s_commands.CheckedCoreRoot) ?? checkedCoreRoot;
        SPMI.showLaunch = Get(s_commands.ShowSPMIRuns);
        dumpDir = Get(s_commands.OutputDir) ?? dumpDir;

        Console.WriteLine("RL CSE Experiment");
        Console.WriteLine($"CoreRoot {SPMI.checkedCoreRoot}");
        Console.WriteLine($"Collection {SPMI.spmiCollection}");

        // Find methods in collections with CSE
        // This also fills in Q and BaselineSequence info for the default jit behavior.
        //
        CollectionData.BuildMethodList(SPMI.spmiCollection, SPMI.checkedCoreRoot);

        // Select the methods to use in this experiment.
        //
        IEnumerable<Method> methodsToUse = new List<Method>();
        List<string>? specificMethods = Get(s_commands.UseSpecificMethods);

        if (specificMethods == null || !specificMethods.Any())
        {
            methodsToUse = GetMethodSample();
        }
        else
        {
            methodsToUse = specificMethods.Select(x => new Method(x));
        }

        List<string>? additionalMethods = Get(s_commands.UseAdditionalMethods);
        if (additionalMethods != null && additionalMethods.Any())
        {
            methodsToUse = methodsToUse.Concat(additionalMethods.Select(x => new Method(x)));
        }

        // Optionally build a data set that describes the features of the CSE candidates
        // (useful for normalizing things)
        bool doGatherFeatures = Get(s_commands.GatherFeatures);

        if (doGatherFeatures)
        {
            GatherFeatures(methodsToUse);
        }

        // Optionally use an MCMC model to find the optimal
        // sets of CSEs for each method, and see how well
        // the (default) policy compares to optimal.
        bool doMCMC = Get(s_commands.DoMCMC);
        bool forgetMCMC = !Get(s_commands.RememberMCMC);

        if (doMCMC)
        {
            MCMC(methodsToUse);

            if (forgetMCMC)
            {
                Forget();
            }
        }

        // Optionally use the PolicyGradient algorithm to
        // try and craft an optimal policy.
        //
        bool doPolicyGradient = Get(s_commands.DoPolicyGradient);

        if (doPolicyGradient)
        {
            PolicyGradient(methodsToUse);
        }
    }

    static void ComputeBaseline(Method m)
    {
        string baseline = SPMI.Run(m.spmiIndex);
        double baselineScore = MetricsParser.GetPerfScore(baseline);
        string baselineSeq = MetricsParser.GetSequence(baseline);
        uint baselineCse = MetricsParser.GetNumCse(baseline);
        uint baselineCand = MetricsParser.GetNumCand(baseline);

        // Fill in V while we're here....
        //
        State baselineState = new State() { method = m, seq = baselineSeq, isBaseline = true };
        StateData data = new StateData() { bestPerfScore = baselineScore, averagePerfScore = baselineScore, basePerfScore = baselineScore, numVisits = 0, numCses = baselineCse, numCand = baselineCand, howFound = "baseline" };

        Baseline[m] = baselineState;
        V[baselineState] = data;
    }

    static void Forget()
    {
        Best.Clear();
        foreach (State s in V.Keys)
        {
            StateData sd = V[s]; sd.bestPerfScore = sd.basePerfScore; sd.averagePerfScore = 0; sd.numVisits = 0; V[s] = sd;
        }
        foreach (StateAndAction sa in Q.Keys)
        {
            StateAndActionData sad = Q[sa]; sad.count = 0; sad.perfScore = 0; Q[sa] = sad;
        }
    }

    // Get or compute the baseline state for a method. This is the terminal state
    // reached by the default jit CSE policy.
    static State BaselineState(Method m)
    {
        // If we don't know this, compute it.
        if (!Baseline.ContainsKey(m))
        {
            ComputeBaseline(m);
        }

        return Baseline[m];
    }

    static State BestState(Method m)
    {
        // If we don't know this, use the baseline
        if (!Best.ContainsKey(m))
        {
            State baselineState = BaselineState(m);
            Best[m] = baselineState;
        }

        return Best[m];
    }
    static uint BaselineNumCses(Method m)
    {
        return V[BaselineState(m)].numCses;
    }

    static uint BaselineNumCand(Method m)
    {
        return V[BaselineState(m)].numCand;
    }

    public static string MakePretty(string seq)
    {
        if (seq == "0")
        {
            return "";
        }
        return seq.Replace(",0", "");
    }

    // Optionally save dumps for certain sequences or default.
    // Do this as separate run to not mess up metrics parsing...
    //
    public static void SaveDump(Method method, string? sequence = null, List<string>? otherOptions = null)
    {
        string dumpFileName = $"dump-{method.spmiIndex}";
        List<string> updateOptions = new List<string>();
        if (sequence != null)
        {
            updateOptions.Add($"JitReplayCSE={sequence}");
            dumpFileName += $"{sequence.Replace(',', '_')}";
        }
        if (otherOptions != null)
        {
            updateOptions.Concat(otherOptions);
        }
        string dumpFile = Path.Combine(dumpDir, dumpFileName + ".d");
        if (!File.Exists(dumpFile))
        {
            updateOptions.Add($"JitDump=*");
            updateOptions.Add($"JitStdOutFile={dumpFile}");
            string dumpRun = SPMI.Run(method.spmiIndex, updateOptions);
            Console.WriteLine($" ---> saved dump of {method.spmiIndex} sequence {(sequence ?? "n/a")} to {dumpFile}");
        }
    }

    static IEnumerable<Method> GetMethodSample()
    {
        // smallest num cand to consider
        uint minCandidatesToExplore = Get(s_commands!.MinCandidates);
        // largest num cand to consider
        uint maxCandidatesToExplore = Get(s_commands.MaxCandidates);
        // number of methods to choose
        uint maxMethodsToExplore = Get(s_commands.NumMethods);
        // do we want a random sample of the first N
        bool randomSample = Get(s_commands.UseRandomSample);
        // random seed
        int randomSeed = Get(s_commands.RandomSampleSeed);

        Random rnd = new Random(randomSeed);

        // Todo: eventually we should look at cases where there are
        // candidates but no baseline CSEs.
        //
        IEnumerable<Method> methods = V.Keys.Where(m =>
            {
                uint numCand = V[m].numCand;
                uint numCse = V[m].numCses;
                return (numCse > 0) && (minCandidatesToExplore <= numCand) && (numCand <= maxCandidatesToExplore);
            }).Select(s => s.method);

        Console.WriteLine($"{methods.Count()} methods with between {minCandidatesToExplore} and {maxCandidatesToExplore} cses, {(randomSample ? "randomly " : "")}choosing {maxMethodsToExplore}.");

        // optionally randomly shuffle
        if (randomSample)
        {
            methods = methods.OrderBy(x => rnd.NextDouble());
        }

        return methods.Take((int)maxMethodsToExplore).ToList();
    }

    // Collect all method perf scores via greedy policy with indicated
    // parameters, and compare to default jit behavior.
    static void EvaluateGreedyPolicy(string parameters, int runNumber = 0)
    {
        Stopwatch s = Stopwatch.StartNew();
        Console.Write("\nCollecting greedy policy data via SPMI... ");
        string greedyContents = SPMI.Run(null, new List<string> { $"JitRLCSE={parameters}", $"JitRLCSEGreedy=1" });
        s.Stop();
        Console.WriteLine($"done ({s.ElapsedMilliseconds} ms)");

        // Filter output to just per-method metrics lines.
        //
        var metricLines = greedyContents.Split(Environment.NewLine).Where(l => l.StartsWith(@"; Total bytes of code", StringComparison.Ordinal));

        // Parse each of these. Ignore methods with 0 cse candidates.
        //
        var methodsAndScores = metricLines.Where(l => MetricsParser.GetNumCand(l) > 0).Select(l => { return (MetricsParser.GetMethodIndex(l), MetricsParser.GetPerfScore(l)); });
        // var methodsAndScoresAndBaselines = methodsAndScores.Select(x => { return (x.Item1, x.Item2, V[BaselineState(x.Item1)].basePerfScore); });

        uint count = (uint)methodsAndScores.Count();
        double logSum = 0;
        uint nBetter = 0;
        uint nWorse = 0;
        uint nSame = 0;
        double eps = 1e-4;

        double worst = 1000;
        double best = 0;
        Method worstMethod = "-1";
        Method bestMethod = "-1";
        uint nRatio = 0;

        foreach (var methodAndScore in methodsAndScores)
        {
            Method method = methodAndScore.Item1;
            double score = methodAndScore.Item2;
            double baseScore = V[BaselineState(method)].basePerfScore;
            double ratio = baseScore / score;

            if (Double.IsNaN(ratio)) continue;
            if (ratio == 0) continue;

            if (ratio > 1 + eps)
            {
                if (ratio > best)
                {
                    best = ratio;
                    bestMethod = method;
                }
                nBetter++;
            }
            else if (ratio < 1 - eps)
            {
                if (ratio < worst)
                {
                    worst = ratio;
                    worstMethod = method;
                }
                nWorse++;
            }
            else
            {
                nSame++;
            }

            nRatio++;
            logSum += Math.Log(ratio);
        }

        Console.WriteLine($"Greedy/Base: {count} methods, {nBetter} better, {nSame} same, {nWorse} worse, {Math.Exp(logSum / nRatio),7:F4} geomean");
        Console.WriteLine($"Best:  {bestMethod.spmiIndex,6} @ {best,7:F4}");
        Console.WriteLine($"Worst: {worstMethod.spmiIndex,6} @ {worst,7:F4}");
        Console.WriteLine();


        //Console.WriteLine(metricLines.Where(l => MetricsParser.GetMethodIndex(l) == bestMethod.spmiIndex).First());
        //Console.WriteLine(metricLines.Where(l => MetricsParser.GetMethodIndex(l) == worstMethod.spmiIndex).First());

        // dump jit behavior on best method (need to automate finding hash)
        //
        //string dumpFile = Path.Combine(dumpDir, $"dump-{bestMethod.spmiIndex}-run-{runNumber}-greedy.d");
        //SPMI.Run(bestMethod.spmiIndex, new List<string> { $"JitRLCSE={parameters}", $"JitRLCSEGreedy=1", $"JitDump=*", $"JitStdOutFile={dumpFile}" });
        //Console.WriteLine($" ---> saved dump to {dumpFile}");
    }

    static void PolicyGradient(IEnumerable<Method> methods)
    {
        // number of times we cycle through the methods
        int nRounds = Get(s_commands.NumberOfRounds);
        // how many trials per method each cycle (minibatch)
        int nIter = Get(s_commands.MinibatchSize);
        // how often to show results
        bool showEvery = Get(s_commands.ShowRounds);
        uint showEveryInterval = Get(s_commands.ShowRoundsInterval);
        // show jit internal evaluations (preferences, likelihoods, etc)
        bool showPolicyEvaluations = Get(s_commands.ShowPolicyEvaluations);
        // show jit internal updates 
        bool showPolicyUpdates = Get(s_commands.ShowPolicyUpdates);
        // show sequences
        bool showSequences = Get(s_commands.ShowSequences);
        // show parameters
        bool showParameters = Get(s_commands.ShowParameters);
        // show likelihoods
        bool showLikelihoods = Get(s_commands.ShowLikelihoods);
        // show baseline likelihoods
        bool showBaselineLikelihoods = Get(s_commands.ShowBaselineLikelihoods);
        // show reward computation
        bool showRewards = Get(s_commands.ShowRewards);
        // random salt
        int salt = Get(s_commands.Salt);
        // learning rate
        double alpha = Get(s_commands.Alpha);
        // just show tabular results
        bool showTabular = Get(s_commands.ShowTabular);
        // how often to recap baseline/best/greedy
        int summaryInterval = Get(s_commands.SummaryInterval);
        // show greedy policy in summary intervals
        bool showGreedy = Get(s_commands.ShowGreedy);
        // save QV dot files each summary interval?
        bool saveQVdot = Get(s_commands.SaveQVDot);

        // Initial parameter set. Must be non-empty. Jit will fill in 0 for any missing params.
        string parameters = Get(s_commands.InitialParameters) ?? "0.0";
        string prevParameters = parameters;
        int nSameParams = 0;

        int nMethods = methods.Count();

        string? dumpMethod = Get(s_commands.SaveDumps);
        if (dumpMethod != null)
        {
            Console.WriteLine($"Saving dumps for {dumpMethod}");
        }

        if (showTabular)
        {
            Console.WriteLine($"\nPolicy Gradient: {nMethods} methods, {nRounds} rounds, {nIter} runs per minibatch, {salt} salt, {alpha} alpha");
            Console.Write($"Rnd ");
            foreach (var method in methods)
            {
                if (showSequences)
                {
                    string csesAndCands = $"{BaselineNumCses(method)}/{BaselineNumCand(method)}";
                    Console.Write($" {method.spmiIndex,10} | {csesAndCands,-20}");
                }
                else
                {
                    Console.Write($" {method.spmiIndex,10}");
                }
            }
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Base");
            foreach (var method in methods)
            {
                State baselineState = BaselineState(method);
                StateData baselineData = V[baselineState];
                if (showSequences)
                {
                    Console.Write($" {baselineData.basePerfScore,10:F2} | {baselineState.PrettySeq,-20}");
                }
                else
                {
                    Console.Write($" {baselineData.basePerfScore,10:F2}");
                }
            }
            Console.WriteLine();
            Console.ResetColor();
        }

        // We run nRounds of the algorithm
        //   Each round processes all methods
        //   Each method is evaluated nIter times as a "minibatch"

        for (int r = 0; r < nRounds; r++)
        {
            if ((r > 0) && (summaryInterval > 0) && (r % summaryInterval == 0))
            {
                if (showTabular)
                {
                    DumpPolicyGradientStatus(methods, showPolicyUpdates, showSequences, summaryInterval, showGreedy, parameters, r);
                }

                if (saveQVdot)
                {
                    foreach (var method in methods)
                    {
                        string dotPath = Path.Combine(dumpDir, $"QV-{method.spmiIndex}-{r}.dot");
                        using StreamWriter sw = new(dotPath);
                        QVDumpDot(method, sw);
                    }
                }
            }

            if (showTabular && showEvery)
            {
                // Introduce the next round 
                //
                Console.Write($"{r,4}");
            }

            foreach (var method in methods)
            {
                State baselineState = BaselineState(method);
                double baselineScore = V[baselineState].basePerfScore;
                uint baselineCses = V[baselineState].numCses;

                State bestState = BestState(method);
                double bestScore = V[bestState].bestPerfScore;

                double[] batchPerfScores = new double[nIter];
                string[] batchSeqs = new string[nIter];
                string[] batchDetails = new string[nIter];
                string[] batchNewParams = new string[nIter];
                string[] batchRuns = new string[nIter];

                Parallel.For(0, nIter, i =>
                {
                    {
                        using StringWriter sw = new StringWriter();

                        // Gather current policy behavior
                        // Use iteration number plus salt for RNG
                        //
                        int iterSalt = salt * nIter * nRounds + r * nIter + i;

                        List<string> policyOptions = new List<string>() { $"JitRLCSE={parameters}", $"JitRLCSEAlpha={alpha}", $"JitRandomCSE={iterSalt}" };

                        if (showPolicyEvaluations)
                        {
                            policyOptions.Add($"JitRLCSEVerbose=1");
                        }

                        string policyRun = SPMI.Run(method.spmiIndex, policyOptions);
                        double policyScore = MetricsParser.GetPerfScore(policyRun);
                        uint policyCSE = MetricsParser.GetNumCse(policyRun);

                        batchPerfScores[i] = policyScore;
                        batchRuns[i] = "POLICY\n" + policyRun;

                        if (policyScore == -1)
                        {
                            // SPMI can fail if we vary CSEs, if so ignore this run
                            batchRuns[i] += "\nGACK!\n";
                            return;
                        }

                        string policySequence = MetricsParser.GetSequence(policyRun);
                        batchSeqs[i] = policySequence;

                        // Run this result through our modelling for V.
                        // It will return a sequence of perf average scores for each sub sequence.
                        //
                        List<double> subScores = SequenceToValues(V, method, policySequence);
                        if (showEachRun || showPolicyEvaluations)
                        {
                            sw.WriteLine($"\nPolicy: {policyRun}");
                            sw.WriteLine($"    V scores {String.Join(",", subScores)}");
                            sw.Write("    ");
                        }

                        // Figure out the "reward" values to use. If the sequence is 2,3
                        // there are 4 values available from V:
                        //   V[""], V["2"], V["2,3"], and V["2,3,0"]
                        // We use those to form rewards + baselines
                        //   
                        // R(t) = policyScore
                        // V(St) =  V[s].
                        //   
                        // and normalize by the baseline score.
                        // Since smaller is better we compute the per-step as (V[s] - R)/base
                        //
                        // This is the "Policy Gradient with Baseline" algorithm.
                        //
                        List<double> rewards = new List<double>();

                        for (int s = 0; s < subScores.Count() - 1; s++)
                        {
                            rewards.Add((subScores[s] - subScores[s + 1]) / baselineScore);
                        }

                        string rewardString = String.Join(",", rewards);

                        if (showRewards)
                        {
                            sw.Write($" values: {String.Join(",", subScores.Select(x => $"{x,7:F4}")),-40}");
                            sw.Write($" rewards: {String.Join(",", rewards.Select(x => $"{x,7:F4}")),-30}");
                        }

                        List<string> updateOptions = new List<string>() { $"JitRLCSE={parameters}", $"JitRLCSEAlpha={alpha}", $"JitRandomCSE={iterSalt}", $"JitReplayCSE={policySequence}", $"JitReplayCSEReward={rewardString}" };

                        if (showPolicyUpdates)
                        {
                            updateOptions.Add($"JitRLCSEVerbose=1");
                        }
                        string updateRun = SPMI.Run(method.spmiIndex, updateOptions);
                        double updateScore = MetricsParser.GetPerfScore(updateRun);
                        string updateSequence = MetricsParser.GetSequence(updateRun);

                        batchRuns[i] += "UPDATE\n" + updateRun;

                        // We expect the update run to behave the same as the policy run. Verify.
                        if (updateScore != policyScore)
                        {
                            sw.WriteLine($"\n\nupdate replay diverged from policy {method.spmiIndex} :: {parameters} :: '{policySequence}' => '{updateSequence}' :: {policyScore} ==> {updateScore}");
                            sw.WriteLine(policyRun);
                            sw.WriteLine(updateRun);
                            return;
                        }

                        if (showEachRun || showPolicyUpdates)
                        {
                            sw.WriteLine($"Update: {updateRun}");
                        }

                        // Harvest the new parameters...
                        //
                        string newParams = MetricsParser.GetParams(updateRun);
                        batchNewParams[i] = newParams;

                        // Optionally save dumps for certain sequences
                        // We do this as separate run to not mess up metrics parsing...
                        //
                        if (method.spmiIndex == dumpMethod)
                        {
                            lock (dumpMethod)
                            {
                                // Always dump updated "decision tree"
                                //
                                string dotFile = Path.Combine(dumpDir, $"qv-{method.spmiIndex}.dot");
                                using (StreamWriter s = new StreamWriter(dotFile))
                                {
                                    QVDumpDot(method, s);
                                }

                                // Write out dasm/dump for method with this sequence, and baseline.
                                // Overwrite method dumps every so often, so we see fresh likelihood computations.
                                // Dasm and baselines should not change so initial ones are fine.
                                //
                                bool shouldOverwriteDump = (r > 0) && (summaryInterval > 0) && (r % (4 * summaryInterval) == summaryInterval);

                                string cleanSequence = updateSequence.Replace(',', '_');
                                string dumpFile = Path.Combine(dumpDir, $"dump-{method.spmiIndex}-{cleanSequence}.d");

                                if (shouldOverwriteDump && File.Exists(dumpFile))
                                {
                                    File.Delete(dumpFile);
                                }

                                if (!File.Exists(dumpFile))
                                {
                                    List<string> dumpOptions = new List<string>(updateOptions);
                                    dumpOptions.Add($"JitDump=*");
                                    dumpOptions.Add($"JitStdOutFile={dumpFile}");
                                    string dumpRun = SPMI.Run(method.spmiIndex, dumpOptions);
                                    sw.WriteLine($" ---> saved dump to {dumpFile}");
                                }

                                string dasmFile = Path.Combine(dumpDir, $"dump-{method.spmiIndex}-{cleanSequence}.dasm");

                                if (shouldOverwriteDump && File.Exists(dasmFile))
                                {
                                    File.Delete(dasmFile);
                                }

                                if (!File.Exists(dasmFile))
                                {
                                    List<string> dasmOptions = new List<string>(updateOptions);
                                    updateOptions.Add($"JitDisasm=*");
                                    updateOptions.Add($"JitStdOutFile={dasmFile}");
                                    string dasmRun = SPMI.Run(method.spmiIndex, updateOptions);
                                    sw.WriteLine($" ---> saved dasm to {dasmFile}");
                                }

                                string baseSequence = "baseline";
                                string baseDumpFile = Path.Combine(dumpDir, $"dump-{method.spmiIndex}-{baseSequence}.d");
                                if (!File.Exists(baseDumpFile))
                                {
                                    List<string> dumpOptions = new List<string>();
                                    dumpOptions.Add($"JitDump=*");
                                    dumpOptions.Add($"JitStdOutFile={baseDumpFile}");
                                    string dumpRun = SPMI.Run(method.spmiIndex, dumpOptions);
                                    sw.WriteLine($" ---> saved baseline dump to {baseDumpFile}");
                                }

                                string baseDasmFile = Path.Combine(dumpDir, $"dump-{method.spmiIndex}-{baseSequence}.dasm");
                                if (!File.Exists(baseDasmFile))
                                {
                                    List<string> dasmOptions = new List<string>();
                                    dasmOptions.Add($"JitDisasm=*");
                                    dasmOptions.Add($"JitStdOutFile={baseDasmFile}");
                                    string dasmRun = SPMI.Run(method.spmiIndex, dasmOptions);
                                    sw.WriteLine($" ---> saved baseline dasm to {baseDasmFile}");
                                }

                                string greedySequence = "greedy";
                                string greedyDumpFile = Path.Combine(dumpDir, $"dump-{method.spmiIndex}-{greedySequence}-{r}.d");

                                if (File.Exists(greedyDumpFile))
                                {
                                    File.Delete(greedyDumpFile);
                                }

                                if (!File.Exists(greedyDumpFile))
                                {
                                    List<string> dumpOptions = new List<string>();
                                    dumpOptions.Add($"JitRLCSE={parameters}");
                                    dumpOptions.Add($"JitRLCSEGreedy=1");
                                    dumpOptions.Add($"JitDump=*");
                                    dumpOptions.Add($"JitStdOutFile={greedyDumpFile}");
                                    string dumpRun = SPMI.Run(method.spmiIndex, dumpOptions);
                                    sw.WriteLine($" ---> saved greedy dump to {greedyDumpFile}");
                                }

                                string greedyDasmFile = Path.Combine(dumpDir, $"dump-{method.spmiIndex}-{greedySequence}-{r}.dasm");

                                if (File.Exists(greedyDasmFile))
                                {
                                    File.Delete(greedyDasmFile);
                                }

                                if (!File.Exists(greedyDasmFile))
                                {
                                    List<string> dasmOptions = new List<string>();
                                    dasmOptions.Add($"JitRLCSE={parameters}");
                                    dasmOptions.Add($"JitRLCSEGreedy=1");
                                    dasmOptions.Add($"JitDisasm=*");
                                    dasmOptions.Add($"JitStdOutFile={greedyDasmFile}");
                                    string dasmRun = SPMI.Run(method.spmiIndex, dasmOptions);
                                    sw.WriteLine($" ---> saved greedy dasm to {greedyDasmFile}");
                                }
                            }
                        }

                        batchDetails[i] = sw.ToString();
                    }
                });


                // Post-process the batch
                //
                // Update the Q/V estimates
                //  (todo: reset for this method Q/V first?)
                // Compute the average param update
                //
                bool newBest = false;
                double newBestScore = 0;
                double[]? averageParams = null;
                List<double> validPerfScores = new List<double>();
                double averagePerfScore = 0;
                int lastValidRun = 0;
                for (int i = 0; i < nIter; i++)
                {
                    // Console.WriteLine($"\n*** RUN {i} ***\n{batchRuns[i]}\n");
                    if (batchPerfScores[i] < 0)
                    {
                        continue;
                    }

                    double[] batchParam = MetricsParser.ToDoubles(batchNewParams[i]).ToArray();
                    if (averageParams == null)
                    {
                        averageParams = batchParam;
                    }
                    else
                    {
                        for (int j = 0; j < averageParams.Length; j++)
                        {
                            averageParams[j] += batchParam[j];
                        }
                    }
                    validPerfScores.Add(batchPerfScores[i]);
                    newBest = QVUpdate(Q, V, method, batchSeqs[i], batchPerfScores[i]);
                    if (newBest)
                    {
                        newBestScore = batchPerfScores[i];
                    }
                    lastValidRun = i;
                }

                int numValid = validPerfScores.Count();

                if (averageParams != null)
                {
                    if (numValid > 1)
                    {
                        for (int j = 0; j < averageParams.Length; j++)
                        {
                            averageParams[j] /= numValid;
                        }
                    }

                    parameters = String.Join(",", averageParams);
                    averagePerfScore = validPerfScores.Average();
                }

                if (showEvery && (r % showEveryInterval == (showEveryInterval - 1)))
                {
                    if (numValid == 0)
                    {
                        // all SPMI replays failed (typically missing write barrier helper)
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write($"{"n/a",11}");
                        if (showSequences)
                        {
                            Console.Write($" | {"",-20}");
                        }
                    }
                    else
                    {
                        string blip = " ";
                        if (averagePerfScore < baselineScore)
                        {
                            if (averagePerfScore == bestScore)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.DarkGreen;
                            }
                        }
                        else if (averagePerfScore == baselineScore)
                        {
                            if (averagePerfScore == bestScore)
                            {
                                Console.ForegroundColor = ConsoleColor.Blue;
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                            }
                        }
                        else if (averagePerfScore == bestScore)
                        {
                            if (newBest)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.DarkYellow;
                            }
                        }
                        if (newBest)
                        {
                            blip = "*";
                        }
                        string lhs = $"{blip} {averagePerfScore:F2}";
                        Console.Write($"{lhs,11}");

                        // For batching there are many such... sigh


                        if (showSequences)
                        {
                            Console.Write($" | {MakePretty(batchSeqs[lastValidRun]),-20}");
                        }
                        if (showLikelihoods)
                        {
                            Console.Write($" L:{MetricsParser.GetLikelihoods(batchRuns[lastValidRun]),-60}");
                        }
                        if (showBaselineLikelihoods)
                        {
                            Console.Write($" B:{MetricsParser.GetBaseLikelihoods(batchRuns[lastValidRun]),-60}");
                        }
                    }
                    Console.ResetColor();
                }
            }
            if (showParameters)
            {
                Console.Write($"  params: {String.Join(",", MetricsParser.ToDoubles(parameters).Select(x => $"{x,7:F4}"))}");
            }

            if (showEvery)
            {
                Console.WriteLine();
            }

            // If parameters stay same for 50 iterations, stop.
            //
            // (todo): replace with something more robust.
            //
            if (parameters == prevParameters)
            {
                nSameParams++;

                if (nSameParams > 50)
                {
                    Console.WriteLine("Converged, sorta");
                    break;
                }
            }
            else
            {
                prevParameters = parameters;
                nSameParams = 0;
            }
        }
    }

    static void DumpPolicyGradientStatus(IEnumerable<Method> methods, bool showPolicyUpdates, bool showSequences, int summaryInterval, bool showGreedy, string parameters, int r)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Indx {r,10}");
        Console.Write("Meth");
        foreach (var method in methods)
        {
            string csesAndCands = $"{BaselineNumCses(method)}/{BaselineNumCand(method)}";
            if (showSequences)
            {
                Console.Write($" {method.spmiIndex,10} | {csesAndCands,-20}");
            }
            else
            {
                Console.Write($" {method.spmiIndex,10}");
            }
        }
        Console.WriteLine();
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Base");
        foreach (var m in methods)
        {
            double baseScore = V[BaselineState(m)].basePerfScore;
            string baseSeq = BaselineState(m).PrettySeq;
            if (showSequences)
            {
                Console.Write($" {baseScore,10:F2} | {baseSeq,-20}", " ");
            }
            else
            {

                Console.Write($" {baseScore,10:F2}");
            }

        }
        Console.WriteLine();
        Console.ResetColor();

        Console.Write("Best");
        foreach (var m in methods)
        {
            double bestScore = V[BestState(m)].bestPerfScore;
            double baseScore = V[BaselineState(m)].basePerfScore;
            string bestSeq = BestState(m).PrettySeq;

            if (bestScore < baseScore)
            {
                Console.ForegroundColor = ConsoleColor.Green;

            }
            else if (bestScore == baseScore)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }

            if (showSequences)
            {
                Console.Write($" {bestScore,10:F2} | {bestSeq,-20}", " ");
            }
            else
            {

                Console.Write($" {bestScore,10:F2}");
            }
            Console.ResetColor();
        }
        Console.WriteLine();

        if (showGreedy)
        {
            Console.Write("Grdy");

            double greedyBaseGeomean = 1;
            double greedyBestGeomean = 1;
            double bestBaseGeomean = 1;
            uint nMeth = 0;
            uint nBetterThanBase = 0;
            uint nSameAsBase = 0;
            uint nWorseThanBase = 0;
            uint nBetterThanBest = 0;
            uint nSameAsBest = 0;
            uint nWorseThanBest = 0;

            foreach (var method in methods)
            {
                // Todo: record these as they may be unique...
                //
                List<string> greedyOptions = new List<string> { $"JitRLCSE={parameters}", $"JitRLCSEGreedy=1" };

                if (showPolicyUpdates)
                {
                    greedyOptions.Add($"JitRLCSEVerbose=1");
                }
                string greedyRun = SPMI.Run(method.spmiIndex, greedyOptions);
                double greedyScore = MetricsParser.GetPerfScore(greedyRun);
                string greedySequence = MetricsParser.GetSequence(greedyRun);

                double bestScore = V[BestState(method)].bestPerfScore;
                double baseScore = V[BaselineState(method)].basePerfScore;

                greedyBaseGeomean *= baseScore / greedyScore;
                greedyBestGeomean *= bestScore / greedyScore;
                bestBaseGeomean *= baseScore / bestScore;
                nMeth++;

                if (greedyScore < baseScore)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    nBetterThanBase++;
                }
                else if (greedyScore == baseScore)
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    nSameAsBase++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    nWorseThanBase++;
                }

                if (greedyScore < bestScore)
                {
                    nBetterThanBest++;
                }
                else if (greedyScore == bestScore)
                {
                    nSameAsBest++;
                }
                else
                {
                    nWorseThanBest++;
                }

                if (showSequences)
                {
                    Console.Write($" {greedyScore,10:F2} | {MakePretty(greedySequence),-20}", " ");
                }
                else
                {

                    Console.Write($" {greedyScore,10:F2}");
                }
                Console.ResetColor();
            }
            Console.WriteLine();

            Console.WriteLine($"Best/base: {Math.Pow(bestBaseGeomean, 1.0 / nMeth):F4}");
            Console.WriteLine($"vs Base    {Math.Pow(greedyBaseGeomean, 1.0 / nMeth):F4} Better {nBetterThanBase} Same {nSameAsBase} Worse {nWorseThanBase}");
            Console.WriteLine($"vs Best    {Math.Pow(greedyBestGeomean, 1.0 / nMeth):F4} Better {nBetterThanBest} Same {nSameAsBest} Worse {nWorseThanBest}");

        }
        Console.WriteLine();
        Console.WriteLine($"Params    {String.Join(",", MetricsParser.ToDoubles(parameters).Select(x => $"{x,7:F4}"))}");

        bool showFullGreedy = Get(s_commands.ShowFullGreedy);
        if (showFullGreedy)
        {
            EvaluateGreedyPolicy(parameters, r);
        }
    }

    // Use Monte-Carlo sampling
    // Treat CSE sequence as a Markov Chain
    // Hence MCMC
    //
    // Options:
    //   Build sequences incrementally (assumes order-independence)
    //   Build sequences randomly
    //   Build for just a subset of methods
    //
    // For each sequence:
    //   Track best perf score for that method vs current score vs worst
    static void MCMC(IEnumerable<Method> methods)
    {
        // Show each method's summary
        bool showEachCase = Get(s_commands.ShowEachMethod);
        // show each particular trial result
        bool showEachRun = Get(s_commands.ShowEachMCMCRun);
        // Show the Markov Chain
        bool showMC = Get(s_commands.ShowMarkovChain);
        // Draw the Markov Chain (tree)
        bool showMCDot = Get(s_commands.ShowMarkovChainDot);

        // Enable random MCMC mode
        bool doRandomTrials = Get(s_commands.DoRandomTrials);
        // We only do random sampling for cases where there are large numbers of CSEs
        // This is the threshold where we switch
        uint minCasesForRandomTrial = Get(s_commands.MinCandidateCountForRandomTrials);
        // How many random trials to run
        int numCasesRandom = Get(s_commands.NumRandomTrials);

        Stopwatch s = new Stopwatch();

        if (showEachCase)
        {
            Console.WriteLine($"INDEX   N      BEST       BASE      WORST      NOCSE     RATIO    RANK ");
        }

        uint nOptimal = 0;
        double nRatio = 1.0;
        double bRatio = 1.0;
        double mRatio = 1.0;
        int methodsToExplore = methods.Count();
        int nRuns = 0;

        s.Restart();

        foreach (var method in methods)
        {
            // Update Q based on baseline data
            string methodIndex = method.spmiIndex;
            State baselineState = BaselineState(method);
            string baselineSeq = baselineState.seq;
            StateData baselineData = V[baselineState];
            double baselineScore = baselineData.basePerfScore;
            uint baselineNumCses = baselineData.numCses;
            QVUpdate(Q, V, method, baselineSeq, baselineScore, isBaseline: true);

            if (showEachRun)
            {
                Console.WriteLine($"Method {methodIndex} base score {baselineScore}");
            }

            // Determine if we'll try exhaustive exploration or random exploration.
            // todo: num cand will overestimate how many cses are possible...
            //
            uint numCandidates = V[baselineState].numCand;

            // We will fill this in later
            double nocseScore = 0;

            // Todo: Find right criteria for random
            bool doRandom = doRandomTrials && numCandidates >= minCasesForRandomTrial;
            int maxCase = doRandom ? numCasesRandom : 1 << (int)numCandidates;
            Experiment[] experiments = new Experiment[maxCase + 1];

            uint nGacked = 0;
            nRuns += maxCase;

            //for (int i = 0; i < maxCase; i++)
            Parallel.For(0, maxCase, i =>
            {
                List<string> policyOptions = new List<string>() { $"JitCSEHash=0" };

                if (doRandom && (i != 0))
                {
                    policyOptions.Add($"JitRandomCSE={i:x}");

                }
                else
                {
                    policyOptions.Add($"JitCSEMask={i:x}");
                }

                string run = SPMI.Run(method.spmiIndex, policyOptions);
                experiments[i].run = run;
                double runScore = MetricsParser.GetPerfScore(run);
                string seq = MetricsParser.GetSequence(run);
                uint numActualCses = MetricsParser.GetNumCse(run);

                if (showEachRun)
                {
                    if (doRandom && (i != 0))
                    {
                        Console.WriteLine($"Method {methodIndex} salt 0x{i + 1:x} score {runScore} seq {seq} [{numActualCses}]");
                    }
                    else
                    {
                        Console.WriteLine($"Method {methodIndex} mask 0x{i:x} score {runScore} seq {seq}");
                    }
                }

                if (runScore == -1)
                {
                    // put in something plausible
                    runScore = baselineScore;
                    experiments[i].seq = "gacked";
                    experiments[i].numCse = 100;
                    nGacked++;
                }
                else
                {
                    // Monte carlo update
                    QVUpdate(Q, V, method, seq, runScore);
                }

                experiments[i].perfScore = runScore;
                experiments[i].seq = seq;
                experiments[i].numCse = numActualCses;

                // Make sure we have the nocse case saved off
                //
                if (i == 0)
                {
                    nocseScore = runScore;
                }
            });

            experiments[maxCase].perfScore = baselineScore;
            experiments[maxCase].seq = baselineSeq;
            experiments[maxCase].numCse = baselineNumCses;

            // Determine best/worst/etc
            // 
            double bestScore = experiments.Min(x => x.perfScore);
            double worstScore = experiments.Max(x => x.perfScore);

            // Secondary criteria
            var bestScores = experiments.Where(x => (x.perfScore - bestScore) < 100 * Double.Epsilon);
            int nBestScore = bestScores.Count();

            uint minCsesForBestScore = bestScores.Min(x => x.numCse);
            var bestOverall = bestScores.Where(x => x.numCse == minCsesForBestScore);

            int nBetterThanBase = experiments.Where(x => x.perfScore < baselineScore).Count();

            // flatten based on identical seqs?

            if (nBetterThanBase == 0)
            {
                nOptimal++;
            }

            nRatio *= baselineScore / bestScore;
            bRatio *= baselineScore / nocseScore;
            mRatio *= bestScore / nocseScore;

            if (!showEachCase) continue;

            if (baselineScore <= bestScore)
            {
                Console.ForegroundColor = ConsoleColor.Green;
            }
            else if (baselineScore >= bestScore * 1.01)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }

            Console.Write($"{methodIndex,6} {numCandidates,2}{bestScore,10:F2} {baselineScore,10:F2} {worstScore,10:F2} {nocseScore,10:F2} ");
            Console.Write($"    {baselineScore / bestScore:F3}  {1 + nBetterThanBase,3}/{maxCase - nGacked,-3} {(doRandom ? "r" : " ")}");
            Console.Write($" best [{bestOverall.First().seq.Replace(",0", "")}]/{nBestScore}");
            Console.Write($" base [{experiments[maxCase].seq.Replace(",0", "")}]");

            if (nGacked > 0)
            {
                Console.Write($" [{nGacked} gacked]");
            }

            if (bestOverall.First().seq == "unknown")
            {
                Console.Write($" -- best run was {bestOverall.First().run}");
            }

            Console.ResetColor();
            Console.WriteLine();

            if (showMC)
            {
                QVDump(Q, bestScore);
            }
            if (showMCDot)
            {
                QVDumpDot(method);
            }
        }

        s.Stop();

        double geomean = Math.Pow(nRatio, 1.0 / methodsToExplore);
        double basegeomean = Math.Pow(bRatio, 1.0 / methodsToExplore);
        double bestgeomean = Math.Pow(mRatio, 1.0 / methodsToExplore);

        // Todo: restore grading by number of CSEs?

        Console.WriteLine($"\n  ---baseline heuristic was optimal in {nOptimal} of {methodsToExplore} cases {nOptimal / (double)methodsToExplore:P}; "
            + $"geomean {geomean:F3} baseline win from CSE {basegeomean:F3} max win {bestgeomean:F3} ({nRuns} runs in {s.ElapsedMilliseconds}ms)\n");
    }

    // Get the "current" value of a state for a method.
    //
    static double GetValue(Dictionary<State, StateData> V, Method method, State state)
    {
        if (!V.ContainsKey(state))
        {
            return V[BaselineState(method)].basePerfScore;
        }
        else
        {
            // this might be too passive as it
            // remembers things from all earlier policies
            // a moving average might work out better.
            //
            // return V[state].averagePerfScore;

            // This might be too aggressive as it may not reflect on-policy behavior
            //
            return V[state].bestPerfScore;
        }
    }

    // Get the current value of each state in a sequence
    //
    [MethodImpl(MethodImplOptions.Synchronized)]
    static List<double> SequenceToValues(Dictionary<State, StateData> V, Method method, string sequence)
    {
        List<double> result = new List<double>();
        string[] subSeqs = sequence.Split(new char[] { ',' });
        State state = new State() { method = method, seq = "" };
        result.Add(GetValue(V, method, state));
        foreach (string subSeq in subSeqs)
        {
            state = state.TakeAction(subSeq).NextState();
            result.Add(GetValue(V, method, state));
        }
        return result;
    }

    // Update the Q,V model based on this "rollout"
    //
    [MethodImpl(MethodImplOptions.Synchronized)]
    static bool QVUpdate(Dictionary<StateAndAction, StateAndActionData> Q, Dictionary<State, StateData> V, Method method, string seq, double perfScore, bool isBaseline = false, List<double>? subSequenceScores = null)
    {
        // This is an undiscounted return model, so each sub-sequence gets "credit" for
        // the overall perf score (lower is better)
        string[] subSeqs = seq.Split(new char[] { ',' });
        State state = new State() { method = method, seq = "" };
        foreach (string subSeq in subSeqs)
        {
            StateAndAction sa = state.TakeAction(subSeq);
            State nextState = QVUpdateStep(Q, V, sa, perfScore, isBaseline);
            subSequenceScores?.Add(V[state].averagePerfScore);
            state = nextState;
        }

        // Create or update V[state] -- we can do this here for terminal states as they have no children.
        //
        StateData? sd = null;
        if (!V.TryGetValue(state, out sd))
        {
            sd = new StateData();
            V.Add(state, sd);
        }

        if (sd.numVisits == 0)
        {
            sd.bestPerfScore = perfScore;
            sd.averagePerfScore = perfScore;
        }

        sd.numVisits++;

        // See if this is a new best state.
        //
        State best = BestState(method);

        if (perfScore < V[best].bestPerfScore)
        {
            Best[method] = state;
            return true;
        }

        return false;
    }

    // Update Q,V for one step in a rollout
    //
    static State QVUpdateStep(Dictionary<StateAndAction, StateAndActionData> Q, Dictionary<State, StateData> V, StateAndAction sa, double perfScore, bool isBaseline)
    {
        StateAndActionData? d = null;
        if (!Q.TryGetValue(sa, out d))
        {
            d = new StateAndActionData();
            Q.Add(sa, d);
        }

        State s = sa.state;
        Action a = sa.action;

        // See if this is a new minimum for sa
        // 
        d.count += 1;
        if (d.count == 1)
        {
            d.perfScore = perfScore;
        }
        else
        {
            d.perfScore = Math.Min(d.perfScore, perfScore);
        }
        d.isBaseline |= isBaseline;

        // Update V[sa.state]
        //
        StateData? sd = null;
        if (!V.TryGetValue(s, out sd))
        {
            // todo -- fill in more state?
            sd = new StateData();
            V[s] = sd;
        }

        if (sd.children == null)
        {
            // First child
            sd.children = new Dictionary<Action, State>();
        }

        if (!sd.children.ContainsKey(a))
        {
            // New child
            sd.children[a] = sa.NextState();
        }

        // Walk all child Q looking for lowest perf score
        //
        double bestChildScore = sd.children.Keys.Min(x => Q[new StateAndAction() { state = s, action = x }].perfScore);
        sd.bestPerfScore = bestChildScore;

        // update the average
        sd.numVisits++;
        sd.averagePerfScore = (sd.averagePerfScore * (sd.numVisits - 1) + perfScore) / sd.numVisits;

        return sa.NextState();
    }

    // Textually describe QV
    //
    static void QVDump(Dictionary<StateAndAction, StateAndActionData> Q, double best, TextWriter? tw = null)
    {
        tw ??= Console.Out;
        foreach (StateAndAction sa in Q.Keys.OrderBy(x => x.state.seq.Length))
        {
            string seq = $"{sa.state.seq} | {sa.action.action,2}";
            tw.WriteLine($"{sa.state.method.spmiIndex} |{seq,20}| {Q[sa].PerfScore,7:F17}  [{Q[sa].count}] {((best == Q[sa].PerfScore) ? "**best**" : "")}");
        }
    }

    // Graphically describe QV
    //
    static void QVDumpDot(Method method, TextWriter? tw = null)
    {
        tw ??= Console.Out;
        tw.WriteLine($"digraph G");
        tw.WriteLine($"{{");
        tw.WriteLine($"  rankdir = LR;");
        tw.WriteLine($"  label=\"{method.spmiIndex}\"");

        // For coloring we also need the min... note perf score lower is better.
        //
        double best = V.Keys.Where(s => s.method.Equals(method)).Max(s => V[s].bestPerfScore);
        double worst = V.Keys.Where(s => s.method.Equals(method)).Min(s => V[s].bestPerfScore);

        // Describe States (nodes)
        //
        foreach (State s in V.Keys.Where(v => v.method.Equals(method)))
        {
            uint numVisits = V[s].numVisits;
            tw.Write($"   \"{s.seq}\" [");
            string label = $"\\N\\n{V[s].bestPerfScore,7:F2}\\n{V[s].averagePerfScore,7:F2}\\n{numVisits}";
            tw.Write($" label=\"{label}\";");

            // Color indicates potential
            //
            byte[] color = Viridis.GetColor(V[s].bestPerfScore, best, worst);
            tw.Write($" style = filled; fillcolor = \"#{color[0]:x2}{color[1]:x2}{color[2]:x2}{80}\";");

            // States encountered by the default jit heuristic
            //
            if (s.isBaseline)
            {
                tw.Write(" peripheries = 2;");
            }

            // font size is log10 number of visits 
            //
            int logNumVisits = (int)Math.Log(numVisits + 1, 10);
            tw.Write($" fontsize = {14 + 4 * logNumVisits};");

            // Terminal states
            //
            if (s.seq != s.PrettySeq)
            {
                tw.Write(" shape = box");
            }

            tw.WriteLine("];");
        }

        // Describe Actions (edges)
        //
        foreach (StateAndAction sa in Q.Keys.Where(x => x.state.method.Equals(method)).OrderBy(x => x.state.seq.Length))
        {
            string sourceName = sa.state.seq;
            string actionName = sa.action.action;
            string targetName = sa.state.seq + (sa.state.seq == "" ? "" : ",") + actionName;
            double value = Q[sa].PerfScore;
            bool isBaseline = Q[sa].isBaseline;

            tw.Write($"   \"{sourceName}\" -> \"{targetName}\" [label = \"{actionName}\\n{value,7:F2}\";");
            byte[] color = Viridis.GetColor(value, best, worst);
            tw.Write($" color = \"#{color[0]:x2}{color[1]:x2}{color[2]:x2}{80}\"");
            if (value == worst)
            {
                tw.Write(" style = bold; ");
            }
            tw.WriteLine("];");
        }

        tw.WriteLine($"}}\n");
    }
    static void GatherFeatures(IEnumerable<Method> methods)
    {
        Console.WriteLine($"Gathering CSE features...");
        Stopwatch s = new Stopwatch();
        s.Start();
        foreach (var method in methods)
        {
            List<string> policyOptions = new List<string> { $"JitCSEHash=0" };
            policyOptions.Add($"JitRLCSE=0");
            policyOptions.Add($"JitReplayCSE=1");
            policyOptions.Add($"JitReplayCSEReward=1");
            policyOptions.Add($"JitRLCSECandidateFeatures=1");
            string run = SPMI.Run(method.spmiIndex, policyOptions);
            string features = MetricsParser.GetFeatures(run);
            Console.Write($"{features}");
        }
        s.Stop();
        Console.WriteLine($"{methods.Count()} in {s.ElapsedMilliseconds} ms");
    }
}

public static class CollectionData
{
    public static IEnumerable<string>? cseLines;
    public static bool showStats;
    public static uint maxCand;
    public static uint totCand;
    public static uint maxCse;
    public static uint totCse;
    public static Dictionary<uint, uint[]> stats = new Dictionary<uint, uint[]>();
    public static IEnumerable<Method> BuildMethodList(string spmiCollection, string checkedCoreRoot)
    {
        if (!File.Exists(spmiCollection))
        {
            Console.WriteLine($"Unable to find SPMI collection '{spmiCollection}'");
            return new List<Method>();
        }

        if (!Directory.Exists(checkedCoreRoot))
        {
            Console.WriteLine($"Unable to find core root '{checkedCoreRoot}'");
            return new List<Method>();
        }

        // Look for a CSE index summary of the collection -- if not found, build one
        //
        string cseIndexFile = spmiCollection + ".cse";

        if (!File.Exists(cseIndexFile))
        {
            Console.Write("building CSE index for collection ... ");
            // Sun SPMI (parallel) across entire collection
            // and gather the metrics
            string indexContents = SPMI.Run(null, null);
            Console.WriteLine("done");
            File.WriteAllText(cseIndexFile, indexContents);
        }

        // Filter output to just per-method metrics line.
        //
        var metricLines = File.ReadLines(cseIndexFile).Where(l => l.StartsWith(@"; Total bytes of code", StringComparison.Ordinal));

        // Parse each of these. Ignore methods with 0 cse candidates.
        //
        IEnumerable<Method> methods = metricLines.Where(l =>
        {
            uint baselineNumCand = MetricsParser.GetNumCand(l);
            if (baselineNumCand == 0)
            {
                return false;
            }
            maxCand = Math.Max(maxCand, baselineNumCand);
            if (!stats.ContainsKey(baselineNumCand))
            {
                stats[baselineNumCand] = new uint[baselineNumCand + 1];
            }
            totCand += baselineNumCand;

            uint numCse = MetricsParser.GetNumCse(l);
            maxCse = Math.Max(maxCse, numCse);
            stats[baselineNumCand][numCse]++;
            totCse += numCse;

            // Since we have the baseline data, fill in V and BaselineSequence too.
            //
            string methodIndex = MetricsParser.GetMethodIndex(l);
            double baselineScore = MetricsParser.GetPerfScore(l);
            string baselineSeq = MetricsParser.GetSequence(l);
            uint baselineCse = MetricsParser.GetNumCse(l);
            Method method = new Method(methodIndex);
            State baselineState = new State() { method = method, seq = baselineSeq, isBaseline = true };
            StateData data = new StateData() { bestPerfScore = baselineScore, averagePerfScore = baselineScore, basePerfScore = baselineScore, numVisits = 0, numCses = baselineCse, numCand = baselineNumCand, howFound = "baseline" };
            MLCSE.V[baselineState] = data;
            MLCSE.Baseline[method] = baselineState;

            return true;
        }
        ).Select(l => new Method(MetricsParser.GetMethodIndex(l)));

        Console.WriteLine($"{metricLines.Count()} methods, {methods.Count()} methods with cses; {totCand} cse candidates, {totCse} cses");

        if (showStats)
        {
            Console.WriteLine($"Count: Cses Candidates");
            for (uint i = 1; i <= maxCand; i++)
            {
                if (stats.ContainsKey(i))
                {
                    uint[] details = stats[i];
                    uint cases = 0;
                    uint sum = 0;
                    for (uint j = 0; j < details.Length; j++)
                    {
                        cases += details[j];
                        sum += j * details[j];
                    }

                    Console.Write($"{i,2} [n: {cases,5} o: {sum / (double)(cases * i),4:F2}]: ");

                    bool first = true;
                    for (uint j = 0; j < details.Length; j++)
                    {
                        if (details[j] == 0) continue;
                        if (!first) Console.Write("; ");
                        first = false;
                        Console.Write($"{j,2}:{details[j],5}");
                    }
                    Console.WriteLine();
                }
            }
        }

        return methods;
    }
}

public struct Experiment
{
    // List<string> options;
    public string run;
    public double perfScore;
    public uint numCse;
    public string seq;
    // uint codeSize;
}

// A method is specified by an collection and an SPMI index
//
public struct Method
{
    public Method(string index, string? collection = null)
    {
        spmiIndex = index;
        spmiCollection = collection ?? MLCSE.spmiCollection;
    }

    public static implicit operator Method(string input)
    {
        return new Method(input, MLCSE.spmiCollection);
    }

    public string spmiCollection;
    public string spmiIndex;

    public override int GetHashCode()
    {
        return spmiCollection.GetHashCode() ^ spmiIndex.GetHashCode();
    }

    public override bool Equals(object? obj)
    {
        if (obj == null) return false;
        if (obj is not Method) return false;
        Method o = (Method)obj;
        return o.spmiCollection.Equals(spmiCollection) && o.spmiIndex.Equals(spmiIndex);
    }
}

// A state is a method and then a sequence of CSEs
// that has been performed in the method (using comma separated candidate indices).
// We use 0 to represent "stopping" or no cses.
//
// Thus "0" represents no CSEs.
// "1" represents doing just CSE #01 (nonterminal state)
// "1,0" represents doing just CSE #01 and then stopping ("terminal" state),
//
// "1,2,0" and "2,1,0" are different sequences tha both do CSE #01 and CSE #02. I suspect
// order doesn't actually matter but am keeping things order sensitive for now just in case.
//
public struct State
{
    public Method method;
    public string seq;
    public bool isBaseline;
    public readonly bool IsTerminal => seq.Equals("0") || seq.EndsWith(",0");

    public readonly string PrettySeq => MLCSE.MakePretty(seq);

    public StateAndAction TakeAction(Action a)
    {
        return new StateAndAction() { state = this, action = a };
    }

    public StateAndAction TakeAction(string s)
    {
        return TakeAction(new Action() { action = s });
    }

    public override int GetHashCode()
    {
        return method.GetHashCode() ^ seq.GetHashCode();
    }

    public override readonly bool Equals(object? obj)
    {
        if (obj == null) return false;
        if (obj is not State) return false;
        State o = (State)obj;
        return o.method.Equals(method) && o.seq.Equals(seq);
    }
}

// Each state has a value Q(s) which is a PerfScore (lower is better)
// The value must be discovered by exploration.
//
// States are related.
//
// The parent is the sequence with its last non-stopping action removed (eg parent of "1,2,0" is "1,0").
// The children are sequences with one unique non-stopping action appended. (eg children of "1,0" might be "1,2,0" or "1,11,0").
//
// These relationships are filled in on demand. Child relationships may be incomplete.
//
// We only know perf scores for terminal states (which we can get by running the jit).
// Perf scores for nonterminal states (sequences that do not end in 0) are computed as
// the minimum perf score of any descendant. They may be over-estimates: it may not be possible
// to run the jit on all possible terminal descendants.
//
public class StateData
{
    // Min over all children
    public double bestPerfScore;
    // Average over all children (numVisits)
    public double averagePerfScore;
    public double basePerfScore;
    public string? howFound;
    public uint numCses;
    public uint numCand;
    // Number of times we've seen this state in a sequence
    public uint numVisits;

    public State parent;
    public Dictionary<Action, State>? children;
}

// An action is just a single CSE number, or 0 to indicate stopping.
//
public struct Action
{
    public string action;
}

// The Q table is indexed by a state and action taken from that state.
public struct StateAndAction
{
    public State state;
    public Action action;

    public override int GetHashCode()
    {
        return action.GetHashCode() ^ state.GetHashCode();
    }

    public State NextState()
    {
        if (state.seq == "")
        {
            return new State() { method = state.method, seq = action.action };
        }
        else
        {
            return new State() { method = state.method, seq = state.seq + "," + action.action };
        }
    }
}

public class StateAndActionData
{
    public double PerfScore => perfScore; //  / (double)count;
    public double perfScore;
    public uint count;
    public bool isBaseline;
}


// Run SPMI on a method or methods
public static class SPMI
{
    public static string? spmiCollection;
    public static string? checkedCoreRoot;
    public static bool showLaunch;
    public static string? lastLaunch;
    public static string Run(string? spmiIndex, List<string>? options = null)
    {
        List<string> args = new();
        args.Add($"-v");
        args.Add($"q");

        if (spmiIndex != null)
        {
            args.Add($"-c");
            args.Add(spmiIndex);
        }
        else
        {
            args.Add("-p");
        }

        args.Add("-jitoption");
        args.Add("JitMetrics=1");
        if (options != null)
        {
            foreach (string option in options)
            {
                args.Add("-jitoption");
                args.Add(option);
            }
        }

        string jitName = "clrjit.dll";
        if (OperatingSystem.IsLinux()) jitName = "libclrjit.so";
        else if (OperatingSystem.IsMacOS()) jitName = "libclrjit.dylib";
        args.Add(Path.Combine(checkedCoreRoot!, jitName));
        args.Add($"{spmiCollection}");

        string spmiBinary = Path.Combine(checkedCoreRoot!, "superpmi");
        if (OperatingSystem.IsWindows()) spmiBinary += ".exe";

        return Invoke(@$"{spmiBinary}", checkedCoreRoot, args.ToArray(), false, code => code == 0 || code == 3);
    }

    static string Invoke(string fileName, string? workingDir, string[] args, bool printOutput, Func<int, bool>? checkExitCode = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            FileName = fileName,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
            psi.ArgumentList.Add(a);

        string command = fileName + " " + string.Join(" ", args.Select(a => OperatingSystem.IsWindows() ? "\"" + a + "\"" : a));

        if (showLaunch)
        {
            Console.WriteLine($"Launching {command}");
        }

        using Process? p = Process.Start(psi);
        if (p == null)
            throw new Exception("Could not start child process " + fileName);

        StringBuilder stdout = new();
        StringBuilder stderr = new();
        p.OutputDataReceived += (sender, args) =>
        {
            if (printOutput)
            {
                Console.WriteLine(args.Data);
            }
            stdout.AppendLine(args.Data);
        };
        p.ErrorDataReceived += (sender, args) =>
        {
            if (printOutput)
            {
                Console.Error.WriteLine(args.Data);
            }
            stderr.AppendLine(args.Data);
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();

        string all = command + Environment.NewLine + Environment.NewLine + "STDOUT:" + Environment.NewLine + stdout + Environment.NewLine + Environment.NewLine + "STDERR:" + Environment.NewLine + stderr;
        lastLaunch = command;

        if (checkExitCode == null ? p.ExitCode != 0 : !checkExitCode(p.ExitCode))
        {
            Console.WriteLine($"From {command}");

            throw new Exception(
                $@"
Child process '{fileName}' exited with error code {p.ExitCode}
stdout:
{stdout.ToString().Trim()}

stderr:
{stderr}".Trim());


        }

        return stdout.ToString();
    }
}

// Parse the "metrics" text emitted by the jit when DOTNET_JitMetrics=1 is set
// Note some metrics are emitted by default, others require enabling via other config
public static class MetricsParser
{
    static Regex perfScorePattern = new Regex(@"(PerfScore|perf score) (\d+(\.\d+)?)");
    static Regex numCsePattern = new Regex(@"num cse ([0-9]{1,})");
    static Regex numCandPattern = new Regex(@"num cand ([0-9]{1,})");
    static Regex seqPattern = new Regex(@"seq ([0-9,]*)");
    static Regex spmiPattern = new Regex(@"spmi index ([0-9]{1,})");
    static Regex paramPattern = new Regex(@"updatedparams ([0-9\.,-e]{1,})");
    static Regex likelihoodPattern = new Regex(@"likelihoods ([0-9\.,-e]{1,})");
    static Regex baseLikelihoodPattern = new Regex(@"baseLikelihoods ([0-9\.,-e]{1,})");
    static Regex featurePattern = new Regex(@"features,([0-9]*,CSE #[0-9][0-9],[0-9\.,-e]{1,})");

    public static string GetMethodIndex(string data)
    {
        var spmiPatternMatch = spmiPattern.Match(data);
        if (spmiPatternMatch.Success)
        {
            return spmiPatternMatch.Groups[1].Value.Trim();
        }
        return "0";
    }

    public static double GetPerfScore(string data)
    {
        var perfScorePatternMatch = perfScorePattern.Match(data);
        if (perfScorePatternMatch.Success)
        {
            return double.Parse(perfScorePatternMatch.Groups[2].Value);
        }
        return -1.0;
    }

    public static string GetSequence(string data)
    {
        var seqPatternMatch = seqPattern.Match(data);
        if (seqPatternMatch.Success)
        {
            string trimmedSeq = seqPatternMatch.Groups[1].Value.Trim();
            return trimmedSeq;
        }
        return "-1";
    }

    public static uint GetNumCse(string data)
    {
        var numCsePatternMatch = numCsePattern.Match(data);
        if (numCsePatternMatch.Success)
        {
            return uint.Parse(numCsePatternMatch.Groups[1].Value);
        }
        return 0;
    }

    public static uint GetNumCand(string data)
    {
        var numCandPatternMatch = numCandPattern.Match(data);
        if (numCandPatternMatch.Success)
        {
            return uint.Parse(numCandPatternMatch.Groups[1].Value);
        }
        return 0;
    }

    public static string GetParams(string data)
    {
        var paramsPatternMatch = paramPattern.Match(data);
        if (paramsPatternMatch.Success)
        {
            return paramsPatternMatch.Groups[1].Value;
        }
        return "";
    }

    public static string GetLikelihoods(string data)
    {
        var likelihoodPatternMatch = likelihoodPattern.Match(data);
        if (likelihoodPatternMatch.Success)
        {
            return likelihoodPatternMatch.Groups[1].Value;
        }
        return "";
    }

    public static string GetBaseLikelihoods(string data)
    {
        var baseLikelihoodPatternMatch = baseLikelihoodPattern.Match(data);
        if (baseLikelihoodPatternMatch.Success)
        {
            return baseLikelihoodPatternMatch.Groups[1].Value;
        }
        return "";
    }

    public static string GetFeatures(string data)
    {
        string result = "";
        int index = 0;
        var featurePatternMatch = featurePattern.Match(data, index);
        while (featurePatternMatch.Success)
        {
            result += featurePatternMatch.Groups[1].Value + Environment.NewLine;
            index += featurePatternMatch.Length;
            featurePatternMatch = featurePattern.Match(data, index);
        }
        return result;
    }

    public static IEnumerable<double> ToDoubles(string str)
    {
        if (String.IsNullOrEmpty(str))
        {
            yield break;
        }

        foreach (var s in str.Split(','))
        {
            double d = 0;
            double.TryParse(s, out d);
            yield return d;
        }
    }
}

public static class Viridis
{
    public static byte[] GetColor(double val, double min, double max)
    {
        double scale = (val - min) / (max - min);
        if (scale < 0) scale = 0;
        if (scale > 1) scale = 1;
        int index = (int)(255 * scale);
        if (index < 0) index = 0;
        if (index > 255) index = 255;
        byte r = (byte)(data[index, 0] * 255);
        byte g = (byte)(data[index, 1] * 255);
        byte b = (byte)(data[index, 2] * 255);
        return new byte[] { r, g, b };
    }

    private static readonly double[,] data =
    {{0.267004, 0.004874, 0.329415},
    {0.268510, 0.009605, 0.335427},
    {0.269944, 0.014625, 0.341379},
    {0.271305, 0.019942, 0.347269},
    {0.272594, 0.025563, 0.353093},
    {0.273809, 0.031497, 0.358853},
    {0.274952, 0.037752, 0.364543},
    {0.276022, 0.044167, 0.370164},
    {0.277018, 0.050344, 0.375715},
    {0.277941, 0.056324, 0.381191},
    {0.278791, 0.062145, 0.386592},
    {0.279566, 0.067836, 0.391917},
    {0.280267, 0.073417, 0.397163},
    {0.280894, 0.078907, 0.402329},
    {0.281446, 0.084320, 0.407414},
    {0.281924, 0.089666, 0.412415},
    {0.282327, 0.094955, 0.417331},
    {0.282656, 0.100196, 0.422160},
    {0.282910, 0.105393, 0.426902},
    {0.283091, 0.110553, 0.431554},
    {0.283197, 0.115680, 0.436115},
    {0.283229, 0.120777, 0.440584},
    {0.283187, 0.125848, 0.444960},
    {0.283072, 0.130895, 0.449241},
    {0.282884, 0.135920, 0.453427},
    {0.282623, 0.140926, 0.457517},
    {0.282290, 0.145912, 0.461510},
    {0.281887, 0.150881, 0.465405},
    {0.281412, 0.155834, 0.469201},
    {0.280868, 0.160771, 0.472899},
    {0.280255, 0.165693, 0.476498},
    {0.279574, 0.170599, 0.479997},
    {0.278826, 0.175490, 0.483397},
    {0.278012, 0.180367, 0.486697},
    {0.277134, 0.185228, 0.489898},
    {0.276194, 0.190074, 0.493001},
    {0.275191, 0.194905, 0.496005},
    {0.274128, 0.199721, 0.498911},
    {0.273006, 0.204520, 0.501721},
    {0.271828, 0.209303, 0.504434},
    {0.270595, 0.214069, 0.507052},
    {0.269308, 0.218818, 0.509577},
    {0.267968, 0.223549, 0.512008},
    {0.266580, 0.228262, 0.514349},
    {0.265145, 0.232956, 0.516599},
    {0.263663, 0.237631, 0.518762},
    {0.262138, 0.242286, 0.520837},
    {0.260571, 0.246922, 0.522828},
    {0.258965, 0.251537, 0.524736},
    {0.257322, 0.256130, 0.526563},
    {0.255645, 0.260703, 0.528312},
    {0.253935, 0.265254, 0.529983},
    {0.252194, 0.269783, 0.531579},
    {0.250425, 0.274290, 0.533103},
    {0.248629, 0.278775, 0.534556},
    {0.246811, 0.283237, 0.535941},
    {0.244972, 0.287675, 0.537260},
    {0.243113, 0.292092, 0.538516},
    {0.241237, 0.296485, 0.539709},
    {0.239346, 0.300855, 0.540844},
    {0.237441, 0.305202, 0.541921},
    {0.235526, 0.309527, 0.542944},
    {0.233603, 0.313828, 0.543914},
    {0.231674, 0.318106, 0.544834},
    {0.229739, 0.322361, 0.545706},
    {0.227802, 0.326594, 0.546532},
    {0.225863, 0.330805, 0.547314},
    {0.223925, 0.334994, 0.548053},
    {0.221989, 0.339161, 0.548752},
    {0.220057, 0.343307, 0.549413},
    {0.218130, 0.347432, 0.550038},
    {0.216210, 0.351535, 0.550627},
    {0.214298, 0.355619, 0.551184},
    {0.212395, 0.359683, 0.551710},
    {0.210503, 0.363727, 0.552206},
    {0.208623, 0.367752, 0.552675},
    {0.206756, 0.371758, 0.553117},
    {0.204903, 0.375746, 0.553533},
    {0.203063, 0.379716, 0.553925},
    {0.201239, 0.383670, 0.554294},
    {0.199430, 0.387607, 0.554642},
    {0.197636, 0.391528, 0.554969},
    {0.195860, 0.395433, 0.555276},
    {0.194100, 0.399323, 0.555565},
    {0.192357, 0.403199, 0.555836},
    {0.190631, 0.407061, 0.556089},
    {0.188923, 0.410910, 0.556326},
    {0.187231, 0.414746, 0.556547},
    {0.185556, 0.418570, 0.556753},
    {0.183898, 0.422383, 0.556944},
    {0.182256, 0.426184, 0.557120},
    {0.180629, 0.429975, 0.557282},
    {0.179019, 0.433756, 0.557430},
    {0.177423, 0.437527, 0.557565},
    {0.175841, 0.441290, 0.557685},
    {0.174274, 0.445044, 0.557792},
    {0.172719, 0.448791, 0.557885},
    {0.171176, 0.452530, 0.557965},
    {0.169646, 0.456262, 0.558030},
    {0.168126, 0.459988, 0.558082},
    {0.166617, 0.463708, 0.558119},
    {0.165117, 0.467423, 0.558141},
    {0.163625, 0.471133, 0.558148},
    {0.162142, 0.474838, 0.558140},
    {0.160665, 0.478540, 0.558115},
    {0.159194, 0.482237, 0.558073},
    {0.157729, 0.485932, 0.558013},
    {0.156270, 0.489624, 0.557936},
    {0.154815, 0.493313, 0.557840},
    {0.153364, 0.497000, 0.557724},
    {0.151918, 0.500685, 0.557587},
    {0.150476, 0.504369, 0.557430},
    {0.149039, 0.508051, 0.557250},
    {0.147607, 0.511733, 0.557049},
    {0.146180, 0.515413, 0.556823},
    {0.144759, 0.519093, 0.556572},
    {0.143343, 0.522773, 0.556295},
    {0.141935, 0.526453, 0.555991},
    {0.140536, 0.530132, 0.555659},
    {0.139147, 0.533812, 0.555298},
    {0.137770, 0.537492, 0.554906},
    {0.136408, 0.541173, 0.554483},
    {0.135066, 0.544853, 0.554029},
    {0.133743, 0.548535, 0.553541},
    {0.132444, 0.552216, 0.553018},
    {0.131172, 0.555899, 0.552459},
    {0.129933, 0.559582, 0.551864},
    {0.128729, 0.563265, 0.551229},
    {0.127568, 0.566949, 0.550556},
    {0.126453, 0.570633, 0.549841},
    {0.125394, 0.574318, 0.549086},
    {0.124395, 0.578002, 0.548287},
    {0.123463, 0.581687, 0.547445},
    {0.122606, 0.585371, 0.546557},
    {0.121831, 0.589055, 0.545623},
    {0.121148, 0.592739, 0.544641},
    {0.120565, 0.596422, 0.543611},
    {0.120092, 0.600104, 0.542530},
    {0.119738, 0.603785, 0.541400},
    {0.119512, 0.607464, 0.540218},
    {0.119423, 0.611141, 0.538982},
    {0.119483, 0.614817, 0.537692},
    {0.119699, 0.618490, 0.536347},
    {0.120081, 0.622161, 0.534946},
    {0.120638, 0.625828, 0.533488},
    {0.121380, 0.629492, 0.531973},
    {0.122312, 0.633153, 0.530398},
    {0.123444, 0.636809, 0.528763},
    {0.124780, 0.640461, 0.527068},
    {0.126326, 0.644107, 0.525311},
    {0.128087, 0.647749, 0.523491},
    {0.130067, 0.651384, 0.521608},
    {0.132268, 0.655014, 0.519661},
    {0.134692, 0.658636, 0.517649},
    {0.137339, 0.662252, 0.515571},
    {0.140210, 0.665859, 0.513427},
    {0.143303, 0.669459, 0.511215},
    {0.146616, 0.673050, 0.508936},
    {0.150148, 0.676631, 0.506589},
    {0.153894, 0.680203, 0.504172},
    {0.157851, 0.683765, 0.501686},
    {0.162016, 0.687316, 0.499129},
    {0.166383, 0.690856, 0.496502},
    {0.170948, 0.694384, 0.493803},
    {0.175707, 0.697900, 0.491033},
    {0.180653, 0.701402, 0.488189},
    {0.185783, 0.704891, 0.485273},
    {0.191090, 0.708366, 0.482284},
    {0.196571, 0.711827, 0.479221},
    {0.202219, 0.715272, 0.476084},
    {0.208030, 0.718701, 0.472873},
    {0.214000, 0.722114, 0.469588},
    {0.220124, 0.725509, 0.466226},
    {0.226397, 0.728888, 0.462789},
    {0.232815, 0.732247, 0.459277},
    {0.239374, 0.735588, 0.455688},
    {0.246070, 0.738910, 0.452024},
    {0.252899, 0.742211, 0.448284},
    {0.259857, 0.745492, 0.444467},
    {0.266941, 0.748751, 0.440573},
    {0.274149, 0.751988, 0.436601},
    {0.281477, 0.755203, 0.432552},
    {0.288921, 0.758394, 0.428426},
    {0.296479, 0.761561, 0.424223},
    {0.304148, 0.764704, 0.419943},
    {0.311925, 0.767822, 0.415586},
    {0.319809, 0.770914, 0.411152},
    {0.327796, 0.773980, 0.406640},
    {0.335885, 0.777018, 0.402049},
    {0.344074, 0.780029, 0.397381},
    {0.352360, 0.783011, 0.392636},
    {0.360741, 0.785964, 0.387814},
    {0.369214, 0.788888, 0.382914},
    {0.377779, 0.791781, 0.377939},
    {0.386433, 0.794644, 0.372886},
    {0.395174, 0.797475, 0.367757},
    {0.404001, 0.800275, 0.362552},
    {0.412913, 0.803041, 0.357269},
    {0.421908, 0.805774, 0.351910},
    {0.430983, 0.808473, 0.346476},
    {0.440137, 0.811138, 0.340967},
    {0.449368, 0.813768, 0.335384},
    {0.458674, 0.816363, 0.329727},
    {0.468053, 0.818921, 0.323998},
    {0.477504, 0.821444, 0.318195},
    {0.487026, 0.823929, 0.312321},
    {0.496615, 0.826376, 0.306377},
    {0.506271, 0.828786, 0.300362},
    {0.515992, 0.831158, 0.294279},
    {0.525776, 0.833491, 0.288127},
    {0.535621, 0.835785, 0.281908},
    {0.545524, 0.838039, 0.275626},
    {0.555484, 0.840254, 0.269281},
    {0.565498, 0.842430, 0.262877},
    {0.575563, 0.844566, 0.256415},
    {0.585678, 0.846661, 0.249897},
    {0.595839, 0.848717, 0.243329},
    {0.606045, 0.850733, 0.236712},
    {0.616293, 0.852709, 0.230052},
    {0.626579, 0.854645, 0.223353},
    {0.636902, 0.856542, 0.216620},
    {0.647257, 0.858400, 0.209861},
    {0.657642, 0.860219, 0.203082},
    {0.668054, 0.861999, 0.196293},
    {0.678489, 0.863742, 0.189503},
    {0.688944, 0.865448, 0.182725},
    {0.699415, 0.867117, 0.175971},
    {0.709898, 0.868751, 0.169257},
    {0.720391, 0.870350, 0.162603},
    {0.730889, 0.871916, 0.156029},
    {0.741388, 0.873449, 0.149561},
    {0.751884, 0.874951, 0.143228},
    {0.762373, 0.876424, 0.137064},
    {0.772852, 0.877868, 0.131109},
    {0.783315, 0.879285, 0.125405},
    {0.793760, 0.880678, 0.120005},
    {0.804182, 0.882046, 0.114965},
    {0.814576, 0.883393, 0.110347},
    {0.824940, 0.884720, 0.106217},
    {0.835270, 0.886029, 0.102646},
    {0.845561, 0.887322, 0.099702},
    {0.855810, 0.888601, 0.097452},
    {0.866013, 0.889868, 0.095953},
    {0.876168, 0.891125, 0.095250},
    {0.886271, 0.892374, 0.095374},
    {0.896320, 0.893616, 0.096335},
    {0.906311, 0.894855, 0.098125},
    {0.916242, 0.896091, 0.100717},
    {0.926106, 0.897330, 0.104071},
    {0.935904, 0.898570, 0.108131},
    {0.945636, 0.899815, 0.112838},
    {0.955300, 0.901065, 0.118128},
    {0.964894, 0.902323, 0.123941},
    {0.974417, 0.903590, 0.130215},
    {0.983868, 0.904867, 0.136897},
    {0.993248, 0.906157, 0.143936}};
}