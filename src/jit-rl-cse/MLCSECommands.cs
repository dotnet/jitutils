using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.WebSockets;

public class MLCSECommands : CliRootCommand
{
    public CliOption<string> SPMICollection { get; } =
        new("--spmi", "-s") { Description = "SPMI collection to use" };
    public CliOption<string> CheckedCoreRoot { get; } =
        new("--core_root", "-c") { Description = "Checked Core Root to use" };
    public CliOption<string> OutputDir { get; } =
        new("--outputDir", "-o") { Description = "directory for dumps and logs" };

    // Method selection
    //
    public CliOption<uint> NumMethods { get; } =
        new("--numMethods", "-n") { Description = "number of methods to use for learning", DefaultValueFactory = (ArgumentResult x) => 5 };
    public CliOption<uint> MinCandidates { get; } =
        new("--minCandidates") { Description = "minimum number of CSE candidates for randomly chosen method", DefaultValueFactory = (ArgumentResult x) => 1 };
    public CliOption<uint> MaxCandidates { get; } =
        new("--maxCandidates") { Description = "maximum number of CSE candidates for randomly chosen method", DefaultValueFactory = (ArgumentResult x) => 10 };
    public CliOption<bool> UseRandomSample { get; } =
        new("--randomSample") { Description = "use random sample of methods", DefaultValueFactory = (ArgumentResult x) => true };
    public CliOption<int> RandomSampleSeed { get; } =
         new("--randomSampleSeed") { Description = "seed for random sample of methods", DefaultValueFactory = (ArgumentResult x) => 42 };
    public CliOption<List<string>> UseSpecificMethods { get; } =
        new("--useSpecificMethods", "-u") { Description = "only use these methods (via spmi index)", Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = true };
    public CliOption<List<string>> UseAdditionalMethods { get; } =
        new("--useAdditionalMethods", "-a") { Description = "also use these methods (via spmi index)", Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = true };

    // Gather Features
    //
    public CliOption<bool> GatherFeatures { get; } =
        new("--doFeatures", "-f") { Description = "gather and describe features of the CSE candidates in the methods" };

    // MCMC
    // 
    public CliOption<bool> DoMCMC { get; } =
        new("--doMCMC", "-m") { Description = "run a Monte Carlo Markov Chain (MCMC) exploration for each method" };
    public CliOption<bool> RememberMCMC { get; } =
        new("--rememberMCMC") { Description = "remember MCMC results when running Policy Gradient" };
    public CliOption<bool> ShowEachMethod { get; } =
        new("--showEachMethod") { Description = "show per-method summary of MCMC results" };
    public CliOption<bool> ShowEachMCMCRun { get; } =
        new("--showEachMCMCRun") { Description = "show per-run details of MCMC results" };
    public CliOption<bool> ShowMarkovChain { get; } =
        new("--showMarkovChain") { Description = "show Markov Chain (tree) of CSE decisions" };
    public CliOption<bool> ShowMarkovChainDot { get; } =
        new ("--showMarkovChainDot") { Description = "show Markov Chain (tree) of CSE decision in dot format" };
    public CliOption<bool> DoRandomTrials { get; } =
        new("--doRandomTrials") { Description = "explore randomly once candidate count threshold has reached", DefaultValueFactory = (ArgumentResult) => true };
    public CliOption<uint> MinCandidateCountForRandomTrials { get; } =
        new("-minCandidateCountForRandomTrials") { Description = "threshold for random exploration", DefaultValueFactory = (ArgumentResult) => 10 };
    public CliOption<int> NumRandomTrials { get; } =
        new("-numRandomTrials") { Description = "number of random trials", DefaultValueFactory = (ArgumentResult) => 512 };

    // Policy Gradient
    //
    public CliOption<bool> DoPolicyGradient { get; } =
        new("--doPolicyGradient", "-g") { Description = "build optimal policy via Policy Gradient", DefaultValueFactory = (ArgumentResult x) => true };
    public CliOption<bool> ShowEachRun { get; } =
        new("--showEachRun") { Description = "show details of policy evaluations or updates" };
    public CliOption<int> NumberOfRounds { get; } =
        new("--numberOfRounds") { Description = "number of rounds of training for Policy Gradient", DefaultValueFactory = (ArgumentResult x) => 10_000 };
    public CliOption<int> MinibatchSize { get; } =
        new("--minibatchSize") { Description = "minibatch size -- number of trials per method per round", DefaultValueFactory = (ArgumentResult x) => 25 };
    public CliOption<bool> ShowTabular { get; } =
      new("--showTabular") { Description = "show results in a table", DefaultValueFactory = (ArgumentResult) => true };
    public CliOption<bool> ShowRounds { get; } =
        new("--showRounds") { Description = "show per-method per-round policy average perf scores", DefaultValueFactory = (ArgumentResult) => true };
    public CliOption<uint> ShowRoundsInterval { get; } =
        new("--showRoundsInterval") { Description = "if showing per-round results, number of rounds between updates", DefaultValueFactory = (ArgumentResult) => 1 };
    public CliOption<bool> ShowPolicyEvaluations { get; } =
        new("--showPolicyEvaluations") { Description = "show details of stochastic policy makes its decisions (very detailed)" };
    public CliOption<bool> ShowPolicyUpdates { get; } =
        new("--showPolicyUpdates") { Description = "show details of how policy parameters get updated (very detailed)" };
    public CliOption<bool> ShowSequences { get; } =
        new("--showSequences") { Description = "show CSE sequences per method" };
    public CliOption<bool> ShowParameters { get; } =
        new("--showParameters") { Description = "show policy parameters at end of each round" };
    public CliOption<bool> ShowLikelihoods { get; } =
        new("--showLikelihoods") { Description = "show per method the likelihood for each sequence element" };
    public CliOption<bool> ShowBaselineLikelihoods { get; } =
        new("--showBaselineLikelihoods") { Description = "show per method the initial likelihood for each CSE and stopping" };
    public CliOption<bool> ShowRewards { get; } =
        new("--showRewards") { Description = "show per method the reward sequence computations" };
    public CliOption<int> Salt { get; } =
        new("--salt") { Description = "initial salt value for stochastic policy RNG", DefaultValueFactory = (ArgumentResult) => 6 };
    public CliOption<double> Alpha { get; } =
        new("--alpha") { Description = "step size for learning", DefaultValueFactory = (ArgumentResult) => 0.02 };
    public CliOption<int> SummaryInterval { get; } =
        new("--summaryInterval") { Description = "summarize progress after this many rounds", DefaultValueFactory = (ArgumentResult) => 25 };
    public CliOption<bool> ShowGreedy { get; } =
        new("--showGreedy") { Description = "show greedy policy results for method subset in the summary", DefaultValueFactory = (ArgumentResult) => true };
    public CliOption<bool> ShowFullGreedy { get; } =
        new("--showFullGreedy") { Description = "show greedy policy results for all methods in the summary", DefaultValueFactory = (ArgumentResult) => true };
    public CliOption<bool> SaveQVDot { get; } =
        new("--saveQVDot") { Description = "save MC diagrams for each method each summary interval" };
    public CliOption<string> SaveDumps { get; } =
        new("--saveDumps") { Description = "save dumps for various CSE sequences for the indicated method" };
    public CliOption<string> InitialParameters { get; } =
        new("--initialParameters")
        {
            Description = "Initial model parameters (comma delimited string, padded with zeros if too few)",
            DefaultValueFactory = (ArgumentResult) => ""
        };

    // Crosscutting
    //
    public CliOption<bool> ShowSPMIRuns { get; } =
        new("--showSPMIRuns") { Description = "show each SPMI invocation" };

    public CliOption<bool> StreamSPMI { get; } =
        new("--streamSPMI") { Description = "use streaming mode for per-method SPMI requests" };
    public CliOption<bool> LogSPMI { get; } =
        new("--logSPMI") { Description = "write log of spmi activity to output dir" };
    public CliOption<bool> StatsSPMI { get; } =
        new("--statsSPMI") { Description = "dump server stats each summary interval" };

    public ParseResult? Result;

    public MLCSECommands() { }

    public MLCSECommands(string[] args) : base("Use ML to explore JIT CSE Heuristics")
    {
        
        Options.Add(SPMICollection);
        Options.Add(CheckedCoreRoot);
        Options.Add(OutputDir);
        Options.Add(NumMethods);
        Options.Add(UseRandomSample);
        Options.Add(RandomSampleSeed);
        Options.Add(MinCandidates);
        Options.Add(MaxCandidates);
        Options.Add(UseSpecificMethods);
        Options.Add(UseAdditionalMethods);

        Options.Add(GatherFeatures);

        Options.Add(DoMCMC);
        Options.Add(RememberMCMC);
        Options.Add(ShowEachMethod);
        Options.Add(ShowEachMCMCRun);
        Options.Add(ShowMarkovChain);
        Options.Add(ShowMarkovChainDot);
        Options.Add(DoRandomTrials);
        Options.Add(MinCandidateCountForRandomTrials);
        Options.Add(NumRandomTrials);
        
        Options.Add(DoPolicyGradient);
        Options.Add(ShowEachRun);
        Options.Add(ShowSPMIRuns);
        Options.Add(NumberOfRounds);
        Options.Add(MinibatchSize);
        Options.Add(ShowTabular);
        Options.Add(ShowRounds);
        Options.Add(ShowRoundsInterval);
        Options.Add(ShowPolicyEvaluations);
        Options.Add(ShowPolicyUpdates);
        Options.Add(ShowSequences);
        Options.Add(ShowParameters);
        Options.Add(ShowLikelihoods);
        Options.Add(ShowBaselineLikelihoods);
        Options.Add(ShowRewards);
        Options.Add(Salt);
        Options.Add(Alpha);
        Options.Add(SummaryInterval);
        Options.Add(ShowGreedy);
        Options.Add(ShowFullGreedy);
        Options.Add(InitialParameters);
        Options.Add(SaveQVDot);
        Options.Add(SaveDumps);

        Options.Add(ShowSPMIRuns);
        Options.Add(StreamSPMI);
        Options.Add(LogSPMI);
        Options.Add(StatsSPMI);

        SetAction(result =>
        {
            Result = result;

            try
            {
                List<string> errors = new();

                if (errors.Count > 0)
                {
                    throw new Exception(string.Join(Environment.NewLine, errors));
                }

                MLCSE.s_commands = this;
                MLCSE.Run();
                return 0;
            }
            catch (Exception e)
            {
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Red;

                Console.Error.WriteLine("Error: " + e.Message);
                Console.Error.WriteLine(e.ToString());

                Console.ResetColor();

                return 1;
            }
        });
    }
}

