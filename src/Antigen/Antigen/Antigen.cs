using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Antigen.Config;
using CommandLine;
using Utils;
using System.Linq;
using System.Runtime.CompilerServices;
using Antigen.Execution;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Antigen
{
    class Program
    {
        internal static readonly object s_spinLock = new object();
        private static int totalTestCount = 0;
        internal static readonly RunOptions s_runOptions = RunOptions.Initialize();
        private static readonly Dictionary<TestResult, int> s_stats = new()
        {
            { TestResult.RoslynException, 0 },
            { TestResult.CompileError, 0 },
            { TestResult.Assertion, 0 },
            { TestResult.DivideByZero, 0 },
            { TestResult.Overflow, 0 },
            { TestResult.OutputMismatch, 0 },
            { TestResult.Pass, 0 },
            { TestResult.OOM, 0 },
            { TestResult.OtherError, 0 },
            { TestResult.Timeout, 0 },
        };

        private static int s_testId = 0;
        private static readonly DateTime s_startTime = DateTime.Now;

        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<CommandLineOptions>(args).MapResult(Run, err => 1);
        }

        private static int Run(CommandLineOptions opts)
        {
            try
            {
                PRNG.Initialize(s_runOptions.Seed);
                s_runOptions.CoreRun = opts.CoreRunPath;
                s_runOptions.OutputDirectory = opts.IssuesFolder;
                if (opts.RunDuration > 0)
                {
                    s_runOptions.RunDuration = opts.RunDuration;
                }
                if (opts.NumTestCases > 0)
                {
                    s_runOptions.NumTestCases = opts.NumTestCases;
                }

                if (s_runOptions.RunDuration != -1)
                {
                    Console.WriteLine($"Starting Antigen for {s_runOptions.RunDuration} minutes.");
                }
                else
                {
                    Console.WriteLine($"Starting Antigen for {s_runOptions.NumTestCases} iterations.");
                }

                if (!File.Exists(s_runOptions.CoreRun))
                {
                    throw new FileNotFoundException($"{s_runOptions.CoreRun} doesn't exist");
                }

                if (!Directory.Exists(s_runOptions.OutputDirectory))
                {
                    Directory.CreateDirectory(s_runOptions.OutputDirectory);
                }

                // Disable trimmer for folder and instead start trimmer for a specific repro file
                // when it comes.
                //StartTrimmerForFolder(opts);

                TestCase.s_RunOptions = s_runOptions;
                TestCase.s_Driver = EEDriver.GetInstance(s_runOptions.CoreRun, Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "ExecutionEngine.dll"), () => EnvVarOptions.TestVars(includeOsrSwitches: PRNG.Decide(0.3), false));
                TestCase.s_TestRunner = TestRunner.GetInstance(TestCase.s_Driver, s_runOptions.CoreRun);

                // Generate vector methods
                VectorHelpers.RecordVectorMethods();

                Parallel.For(0, 4, (p) => RunTest());
                Console.WriteLine($"Executed {s_testId} test cases.");
                DisplayStats();
            }
            catch (OutOfMemoryException oom)
            {
                Console.WriteLine(oom.Message);
                var myProcess = Process.GetCurrentProcess();
                Console.WriteLine($"  Physical memory usage     : {myProcess.WorkingSet64}");
                Console.WriteLine($"  Base priority             : {myProcess.BasePriority}");
                Console.WriteLine($"  Priority class            : {myProcess.PriorityClass}");
                Console.WriteLine($"  User processor time       : {myProcess.UserProcessorTime}");
                Console.WriteLine($"  Privileged processor time : {myProcess.PrivilegedProcessorTime}");
                Console.WriteLine($"  Total processor time      : {myProcess.TotalProcessorTime}");
                Console.WriteLine($"  Paged system memory size  : {myProcess.PagedSystemMemorySize64}");
                Console.WriteLine($"  Paged memory size         : {myProcess.PagedMemorySize64}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Got following exception, but will return exitcode= 0 so issues folder gets copied.");
                Console.WriteLine(ex.Message);
            }
            return 0;
        }

        /// <summary>
        ///     Start trimmer for entire folder. Trimmer will monitor if there is any new UniqueIssue* folder
        ///     and if yes, trim the repro file.
        /// </summary>
        private static void StartTrimmerForFolder()
        {
            string trimmer_exe = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Trimmer");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                trimmer_exe += ".exe";
            }
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = trimmer_exe,
                Arguments = $"-c {s_runOptions.CoreRun} -o {s_runOptions.OutputDirectory} -p {Environment.ProcessId}",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            Process process = new Process
            {
                StartInfo = startInfo
            };
            process.Start();
            if (process.HasExited)
            {
                Console.WriteLine("Trimmer exited immediately.");
            }
            else
            {
                Console.WriteLine($"Started Trimmer with PID {process.Id}");
            }
        }

        /// <summary>
        ///     Start trimmer for a specific repro file.
        /// </summary>
        /// <param name="reproFile"></param>
        internal static void StartTrimmerForFile(string reproFile)
        {
            string trimmer_exe = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Trimmer");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                trimmer_exe += ".exe";
            }
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = trimmer_exe,
                Arguments = $"-c {s_runOptions.CoreRun} -f {reproFile} -p {Environment.ProcessId}",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            Process process = new Process
            {
                StartInfo = startInfo
            };
            process.Start();
            if (process.HasExited)
            {
                Console.WriteLine("Trimmer exited immediately.");
            }
            else
            {
                Console.WriteLine($"Started Trimmer with PID {process.Id}");
            }
        }

        private static int GetNextTestId()
        {
            lock (s_spinLock)
            {
                return ++s_testId;
            }
        }

        /// <summary>
        ///     Are we done yet?
        /// </summary>
        private static bool Done
        {
            get
            {
                // If RunDuration was specified, use that.
                if (s_runOptions.RunDuration != -1)
                {
                    return (DateTime.Now - s_startTime).TotalMinutes >= s_runOptions.RunDuration;
                }
                // Otherwise use number of test cases.
                else if (s_testId >= s_runOptions.NumTestCases)
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        ///     Save the result.
        /// </summary>
        /// <param name="localStats"></param>
        private static void SaveResult(Dictionary<TestResult, int> localStats, int localTestCount)
        {
            lock (s_spinLock)
            {
                totalTestCount += localTestCount;
                foreach (var resultStat in localStats)
                {
                    s_stats[resultStat.Key] += resultStat.Value;
                    localStats[resultStat.Key] = 0;
                }

                if ((totalTestCount % 100) == 0)
                {
                    DisplayStats();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DisplayStats()
        {
            var nonZeroStats = s_stats.Where(stat => stat.Value > 0);
            Console.WriteLine($"*** {string.Join(", ", nonZeroStats.Select(stat => $"{Enum.GetName(typeof(TestResult), stat.Key)}={stat.Value}"))}");
        }

        static void RunTest()
        {
            Dictionary<TestResult, int> localStats = new Dictionary<TestResult, int>()
            {
                { TestResult.RoslynException, 0 },
                { TestResult.CompileError, 0 },
                { TestResult.Assertion, 0 },
                { TestResult.DivideByZero, 0 },
                { TestResult.Overflow, 0 },
                { TestResult.OutputMismatch, 0 },
                { TestResult.Pass, 0 },
                { TestResult.OOM, 0 },
                { TestResult.OtherError, 0 },
                { TestResult.Timeout, 0 },
            };

            int testCount = 0;
            while (!Done)
            {
                var currTestId = GetNextTestId();
                using (var testCase = new TestCase(currTestId, s_runOptions))
                {
                    testCase.Generate();
                    string configName = testCase.Config.Name;
                    if (testCase.ContainsVectorData)
                    {
                        configName += " (Vector)";
                    }
                    if (testCase.Config.UseSve)
                    {
                        configName += " (SVE)";
                    }
                    var result = testCase.Verify();

                    Console.WriteLine("[{4}] Test# {0, -5} [{1, -25}] - {2, -15} {3, -10} MB ",
                        currTestId,
                        configName,
                        Enum.GetName(typeof(TestResult), result),
                        (double)Process.GetCurrentProcess().WorkingSet64 / 1000000,
                        (DateTime.Now - s_startTime).ToString());
                    localStats[result]++;
                }
                testCount++;
                if (testCount == 50)
                {
                    SaveResult(localStats, testCount);
                    testCount = 0;
                }
            }
        }
    }

    public class CommandLineOptions
    {
        [Option(shortName: 'c', longName: "CoreRun", Required = true, HelpText = "Full path to CoreRun/CoreRun.exe.")]
        public string CoreRunPath { get; set; }

        [Option(shortName: 'o', longName: "IssuesFolder", Required = true, HelpText = "Full path to folder where issues will be copied.")]
        public string IssuesFolder { get; set; }

        [Option(shortName: 'n', longName: "NumTestCases", Required = false, HelpText = "Number of test cases to execute. By default, 1000.")]
        public int NumTestCases { get; set; }

        [Option(shortName: 'd', longName: "RunDuration", Required = false, HelpText = "Duration in minutes to run. By default until NumTestCases, but if Duration is given, will override the NumTestCases.")]
        public int RunDuration { get; set; }
    }
}
