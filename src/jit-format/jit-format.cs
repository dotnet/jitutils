// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

///////////////////////////////////////////////////////////////////////////////
//
//  jit-format -
//

using System;
using System.Diagnostics;
using System.CommandLine;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace ManagedCodeGen
{
    public class jitformat
    {
        // Define options to be parsed 
        public class Config
        {
            private ArgumentSyntax _syntaxResult;
            private string _arch = null;
            private string _os = null;
            private string _build = null;
            private string _rootPath = null;
            private IReadOnlyList<string> _filenames = Array.Empty<string>();
            private IReadOnlyList<string> _projects = Array.Empty<string>();
            private string _srcDirectory = null;
            private bool _untidy = false;
            private bool _noformat = false;
            private bool _fix = false;
            private bool _verbose = false;
            private bool _ignoreErrors = false;
            private string _buildLog = null;
            private string _compileCommands = null;
            private bool _buildCompileCommands = true;

            private JObject _jObj;
            private string _jitUtilsRoot = null;

            public Config(string[] args)
            {
                LoadFileConfig();

                _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
                {
                    syntax.DefineOption("a|arch", ref _arch, "The architecture of the build (options: x64, x86)");
                    syntax.DefineOption("o|os", ref _os, "The operating system of the build (options: Windows, OSX, Ubuntu, Fedora, etc.)");
                    syntax.DefineOption("b|build", ref _build, "The build type of the build (options: Release, Checked, Debug)");
                    syntax.DefineOption("c|coreclr", ref _rootPath, "Full path to base coreclr directory");
                    syntax.DefineOption("compile-commands", ref _compileCommands, "Full path to compile_commands.json");
                    syntax.DefineOption("v|verbose", ref _verbose, "Enable verbose output.");
                    syntax.DefineOption("untidy", ref _untidy, "Do not run clang-tidy");
                    syntax.DefineOption("noformat", ref _noformat, "Do not run clang-format");
                    syntax.DefineOption("f|fix", ref _fix, "Fix formatting errors discovered by clang-format and clang-tidy.");
                    syntax.DefineOption("i|ignore-errors", ref _ignoreErrors, "Ignore clang-tidy errors");
                    syntax.DefineOptionList("projects", ref _projects, "List of build projects clang-tidy should consider (e.g. dll, standalone, protojit, etc.). Default: dll");

                    syntax.DefineParameterList("filenames", ref _filenames, "Optional list of files that should be formatted.");
                });
                
                // Run validation code on parsed input to ensure we have a sensible scenario.

                validate();
            }

            private void SetPlatform()
            {
                // Extract system RID from dotnet cli
                List<string> commandArgs = new List<string> { "--info" };
                CommandResult result = TryCommand("dotnet", commandArgs, true);

                if (result.ExitCode != 0)
                {
                    Console.Error.WriteLine("dotnet --info returned non-zero");
                }

                Console.WriteLine("{0}", result.StdOut);

                var lines = result.StdOut.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                foreach (var line in lines)
                {
                    Regex pattern = new Regex(@"OS Name:([\sA-Za-z0-9\.-]*)$");
                    Match match = pattern.Match(line);
                    if (match.Success)
                    {
                        if (match.Groups[1].Value.Trim() == "Windows")
                        {
                            _os = "Windows_NT";
                        }
                        else if (match.Groups[1].Value.Trim() == "Mac OS X")
                        {
                            _os = "OSX";
                        }
                        else if (match.Groups[1].Value.Trim() == "ubuntu")
                        {
                            _os = "Ubuntu";
                        }
                        else
                        {
                            _os = match.Groups[1].Value.Trim();

                        }
                    }
                }
            }
            

            private void validate()
            {
                if ((_arch == null))
                {
                    if (_verbose)
                    {
                        Console.WriteLine("Defaulting architecture to x64.");
                    }
                    _arch = "x64";
                }

                if (_build == null)
                {
                    if (_verbose)
                    {
                        Console.WriteLine("Defaulting build to Debug.");
                    }

                    _build = "Debug";
                }

                if (_os == null)
                {
                    if (_verbose)
                    {
                        Console.WriteLine("Discovering operating system.");
                    }

                    SetPlatform();

                    if (_verbose)
                    {
                        Console.WriteLine("Operating system is {0}", _os);
                    }
                }

                if (_os == "Windows")
                {
                    _os = "Windows_NT";
                }

                if (_srcDirectory == null)
                {
                    if (_verbose)
                    {
                        Console.WriteLine("Formatting jit directory.");
                    }
                    _srcDirectory = "jit";
                }

                if (_projects.Count == 0 && _verbose)
                {
                    Console.WriteLine("Formatting dll project.");
                }

                if (!_untidy && ( (_arch == null) || (_os == null) || (_build == null)))
                {
                    _syntaxResult.ReportError("Specify --arch, --plaform, and --build for clang-tidy run.");
                }

                if (_rootPath == null)
                {
                    _syntaxResult.ReportError("Specify --coreclr");
                }

                // Check that we can find the build log for Windows.
                if (_os == "Windows_NT")
                {
                    string logFile = "CoreCLR_Windows_NT__" + _arch + "__" + _build + ".log";
                    string logFullPath = Path.Combine(_rootPath, "bin", "Logs", logFile);
                    if (_compileCommands != null)
                    {
                        _buildCompileCommands = false;
                    }
                    else if (!_untidy && !File.Exists(logFullPath))
                    {
                        _syntaxResult.ReportError("Can't find build log.");
                    }
                    else
                    {
                        _buildLog = logFullPath;
                        string[] compileCommandsPath = { _rootPath, "bin", "obj", "Windows_NT." + _arch + "." + _build, "compile_commands.json" };
                        _compileCommands = Path.Combine(compileCommandsPath);
                    }
                }

                // Check that we can find the compile_commands.json file on other platforms
                else
                {
                    string[] compileCommandsPath = { _rootPath, "bin", "obj", _os + "." + _arch + "." + _build, "compile_commands.json" };
                    if (!_untidy && !File.Exists(Path.Combine(compileCommandsPath)))
                    {
                        _syntaxResult.ReportError("Can't find compile_commands.json file. Please build coreclr first.");
                    }
                    else
                    {
                        _compileCommands = Path.Combine(compileCommandsPath);
                    }
                }
            }

            private void LoadFileConfig()
            {
                _jitUtilsRoot = Environment.GetEnvironmentVariable("JIT_UTILS_ROOT");

                if (_jitUtilsRoot != null)
                {
                    string path = Path.Combine(_jitUtilsRoot, "config.json");

                    if (File.Exists(path))
                    {
                        string configJson = File.ReadAllText(path);

                        _jObj = JObject.Parse(configJson);
                        
                        // Check if there is any default config specified.
                        if (_jObj["format"]["default"] != null)
                        {
                            bool found;

                            // Set up arch
                            var arch = ExtractDefault<string>("arch", out found);
                            _arch = (found) ? arch : _arch;

                            // Set up build
                            var build = ExtractDefault<string>("build", out found);
                            _build = (found) ? build : _build;

                            // Set up os
                            var os = ExtractDefault<string>("os", out found);
                            _os = (found) ? os : _os;

                            // Set up core_root.
                            var rootPath = ExtractDefault<string>("coreclr", out found);
                            _rootPath = (found) ? rootPath : _rootPath;

                            // Set up compileCommands
                            var compileCommands = ExtractDefault<string>("compile-commands", out found);
                            _compileCommands = (found) ? compileCommands : _compileCommands;

                            // Set flag from default for verbose.
                            var verbose = ExtractDefault<bool>("verbose", out found);
                            _verbose = (found) ? verbose : _verbose;

                            // Set up untidy
                            var untidy = ExtractDefault<bool>("untidy", out found);
                            _untidy = (found) ? untidy : _untidy;

                            // Set up noformat
                            var noformat = ExtractDefault<bool>("noformat", out found);
                            _noformat = (found) ? noformat : _noformat;

                            // Set up fix
                            var fix = ExtractDefault<bool>("fix", out found);
                            _fix = (found) ? fix : _fix;
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine("Can't find format.json on {0}", _jitUtilsRoot);
                    }
                }
                else
                {
                    Console.WriteLine("Environment variable JIT_FORMAT_ROOT not found - no configuration loaded.");
                }
            }

            public T ExtractDefault<T>(string name, out bool found)
            {
                var token = _jObj["format"]["default"][name];

                if (token != null)
                {
                    found = true;

                    try
                    {
                        return token.Value<T>();
                    }
                    catch (System.FormatException e)
                    {
                        Console.Error.WriteLine("Bad format for default {0}.  See config.json", name, e);
                    }
                }

                found = false;
                return default(T);
            }

            public bool IsWindows { get { return (_os == "Windows_NT"); } }
            public bool DoVerboseOutput { get { return _verbose; } }
            public bool DoClangTidy { get { return !_untidy; } }
            public bool DoClangFormat { get { return !_noformat; } }
            public bool Fix { get { return _fix; } }
            public bool IgnoreErrors { get { return _ignoreErrors; } }
            public bool BuildCompileCommands { get { return _buildCompileCommands; } }
            public string CoreCLRRoot { get { return _rootPath; } }
            public string Arch { get { return _arch; } }
            public string BuildLog { get { return _buildLog; } }
            public string CompileCommands { get { return _compileCommands; } }
            public IReadOnlyList<string> Filenames { get { return _filenames; } }
            public IReadOnlyList<string> Projects { get { return _projects.Count == 0 ? new List<string>{"dll"} : _projects; } }
            public string SourceDirectory { get { return _srcDirectory; } }
        }

        public static int Main(string[] args)
        {
            // Parse and store comand line options.
            var config = new Config(args);

            int returncode = 0;
            bool verbose = config.DoVerboseOutput;

            List<string> filenames = new List<string>();

            // Run clang-format over specified files, or all files if none were specified
            if (config.Filenames.Count() == 0)
            {
                // add all files to a list of files
                foreach (string filename in Directory.GetFiles(Path.Combine(config.CoreCLRRoot, "src", config.SourceDirectory)))
                {
                    // if it's not a directory, add it to our list
                    if (!Directory.Exists(filename) && (filename.EndsWith(".cpp") || filename.EndsWith(".h") || filename.EndsWith(".hpp")))
                    {
                        filenames.Add(filename);
                    }
                }
            }
            else
            {
                foreach (string filename in config.Filenames)
                {
                    string prefix = "";
                    if (!filename.Contains(config.CoreCLRRoot))
                    {
                        prefix = Path.Combine(config.CoreCLRRoot, "src", config.SourceDirectory);
                    }

                    if (File.Exists(Path.Combine(prefix, filename)))
                    {
                        filenames.Add(Path.Combine(prefix, filename));
                    }
                    else
                    {
                        Console.WriteLine(Path.Combine(prefix, filename) + " does not exist. Skipping.");
                    }

                }
            }

            if (config.DoClangTidy)
            {
                string compileCommands = config.CompileCommands;
                
                // Set up compile_commands.json. On Windows, we need to generate it. On other platforms,
                // it will be generated by cmake, and we just need to grab the path to it.

                foreach (string project in config.Projects)
                {
                    if (config.IsWindows && config.BuildCompileCommands)
                    {
                        // On Windows, parse the log file and create the compile commands database
                        if (verbose)
                        {
                            Console.WriteLine("Building compile_commands.json.");
                        }
                        ParseBuildLog(config.BuildLog, compileCommands, config.Arch, project, config.SourceDirectory, verbose);
                    }

                    if (verbose)
                    {
                        Console.WriteLine("Using compile_commands.json found at {0}", compileCommands);
                        Console.WriteLine("Running clang-tidy.");
                    }

                    if (!RunClangTidy(filenames, compileCommands, config.IgnoreErrors, config.Fix, verbose))
                    {
                        Console.WriteLine("Clang-Tidy needs to be rerun in fix mode for {0}/{1}.", project, config.SourceDirectory);
                        returncode = -1;
                    }
                }
            }

            if (config.DoClangFormat)
            {
                if (!RunClangFormat(filenames, config.Fix, verbose))
                {
                    Console.WriteLine("Clang-Format needs to be rerun in fix mode");
                    returncode = -1;
                }
            }

            if (returncode == -1)
            {
                Console.WriteLine("See https://github.com/dotnet/jitutils/blob/master/doc/getstarted.md for more information on running jit-format.");
            }

            return returncode;
        }

        public static CommandResult TryCommand (string name, IEnumerable<string> commandArgs, bool capture = false)
        {
            try 
            {
                Command command =  Command.Create(name, commandArgs);

                if (capture)
                {
                    // Capture stdout/stderr for consumption within tool.
                    command.CaptureStdOut();
                    command.CaptureStdErr();
                }
                else
                {
                    // Wireup stdout/stderr so we can see output.
                    command.ForwardStdOut();
                    command.ForwardStdErr();
                }

                return command.Execute();
            }
            catch (CommandUnknownException e)
            {
                Console.Error.WriteLine("\nError: {0} command not found!  Add {0} to the path.", name, e);
                Environment.Exit(-1);
                return CommandResult.Empty;
            }
        }

        public static void ParseBuildLog(string buildLog, string compileCommands, string arch, string project, string dir, bool verbose)
        {
            if (!File.Exists(buildLog))
            {
                Console.Error.WriteLine("Build log does not exist: {0}", buildLog);
            }
            using (var outputStream = System.IO.File.Create(compileCommands))
            {
                using (StreamWriter compileCommandsWriter = new StreamWriter(outputStream))
                {
                    compileCommandsWriter.WriteLine("[");
                }
            }

            bool first = true;

            foreach (string line in File.ReadLines(buildLog))
            {
                string clExe = "";
                List<string> iOptions = new List<string>();
                List<string> dOptions = new List<string>();
                List<string> uOptions = new List<string>();
                List<string> files = new List<string>();

                // For now, only create the compile_commands database for the dll build of coreclr.
                if (line.Contains("CL.exe") && line.Contains("src\\" + dir + "\\" + project))
                {
                    string[] splitLine = line.Trim().Split(new string[]{ "CL.exe " }, StringSplitOptions.None);
                    clExe = (splitLine[0] + "CL.exe").Replace("\\", "/");

                    string[] options = splitLine[1].Split(' ');

                    bool dOptionFound = false;

                    foreach (string option in options)
                    {
                        if (option.StartsWith("/I") || option.StartsWith("-I"))
                        {
                            if (option.Contains("src\\inc"))
                            {
                                iOptions.Add(option.Replace("/I", "-isystem").Replace("\\", "/"));
                            }
                            else
                            {
                                iOptions.Add(option.Replace("/I", "-I").Replace("\\", "/"));
                            }
                        }
                        else if (option.StartsWith("/D") || option.StartsWith("-D"))
                        {
                            dOptionFound = true;
                        }
                        else if (dOptionFound)
                        {
                            if (option.Contains("CMAKE_INTDIR"))
                            {
                                dOptions.Add("-D_" + option.Replace("\"", "").Replace("\\",""));
                            }
                            else
                            {
                                dOptions.Add("-D" + option.Replace("\\","/"));
                            }

                            dOptionFound = false;
                        }
                        else if (option.StartsWith("/U") || option.StartsWith("-U"))
                        {
                            uOptions.Add(option.Replace("/U", "-U"));
                        }
                        else if (option.Contains("src\\" + dir) && option.EndsWith(".cpp"))
                        {
                            files.Add(option);
                        }
                    }

                    using (StreamWriter compileCommandsWriter = File.AppendText(compileCommands))
                    {
                        foreach (string filename in files)
                        {
                            string file = filename.Replace("\\", "/");
                            if (!first)
                            {
                                compileCommandsWriter.WriteLine(",");
                            }

                            compileCommandsWriter.WriteLine("    {");

                            compileCommandsWriter.WriteLine("        \"directory\": \"" + iOptions[0].Replace("-I","") + "\",");
                            compileCommandsWriter.Write("        \"command\": \"\\\"" + clExe + "\\\" ");

                            string m32 = "";
                            if (arch == "x86")
                            {
                                m32 = " -m32";
                            }

                            compileCommandsWriter.Write("-target x86_64-pc-windows-msvc" + m32 + " -fms-extensions -fms-compatibility -fmsc-version=1900 -fexceptions -fcxx-exceptions -DSOURCE_FORMATTING=1");

                            foreach (string iOption in iOptions)
                            {
                                compileCommandsWriter.Write(iOption + " ");
                            }
                            
                            foreach (string dOption in dOptions)
                            {
                                compileCommandsWriter.Write(dOption + " ");
                            }
                            foreach (string uOption in uOptions)
                            {
                                compileCommandsWriter.Write(uOption + " ");
                            }

                            compileCommandsWriter.WriteLine(file + "\",");
                            compileCommandsWriter.WriteLine("        \"file\": \"" + file + "\"");
                            compileCommandsWriter.Write("    }");
                            first = false;
                        }
                    }
                }
            }

            using (StreamWriter compileCommandsWriter = File.AppendText(compileCommands))
            {
                compileCommandsWriter.WriteLine();
                compileCommandsWriter.WriteLine("]");
                compileCommandsWriter.WriteLine();
            }
        }

        public static bool RunClangTidy(List<string> filenames, string compileCommands, bool ignoreErrors, bool fix, bool verbose)
        {
            bool formatOk = true;
            string checks = "readability-braces*,modernize-use-nullptr";
            string tidyFix = fix ? "-fix" : "";

            if (verbose)
            {
                Console.WriteLine("Running: ");
            }

            foreach (string filename in filenames)
            {
                if (filename.EndsWith(".cpp"))
                {
                    string fixErrors = ignoreErrors && fix ? "-fix-errors" : "";
                    if (verbose)
                    {
                        Console.WriteLine("\tclang-tidy {0} -checks=-*,{1} {2} -header-filer=.* -p {3} {4}", tidyFix, checks, fixErrors, compileCommands, filename);
                    }
                    
                    List<string> commandArgs = new List<string> { tidyFix, "-checks=-*," + checks, fixErrors, "-header-filter=.*", "-p " + compileCommands, filename };
                    CommandResult result = TryCommand("clang-tidy", commandArgs, true);
                        
                    if ((result.StdOut.Contains("warning:") || (!ignoreErrors && result.StdOut.Contains("error:"))))
                    {
                        if (verbose)
                        {
                            Console.WriteLine("clang-tidy: there are formatting errors in {0}", filename);
                        }
                        
                        if (!fix)
                        {
                            formatOk = false;
                        }
                    }

                    if (!ignoreErrors && result.StdErr.Contains("error:"))
                    {
                        Console.Error.WriteLine("Error in clang-tidy: {0}", result.StdErr);
                        
                        if (!fix)
                        {
                            formatOk = false;
                        }
                    }
                        
                    if (verbose)
                    {
                        if (result.StdOut.Contains("warning:") || !ignoreErrors)
                        {
                            Console.WriteLine(result.StdOut);
                        }
                    }
                }
            }

            return formatOk;
        }

        public static bool RunClangFormat(List<string> filenames, bool fix, bool verbose)
        {
            string formatFix = fix ? "-i" : "";
            bool formatOk = true;
            if (verbose)
            {
                Console.WriteLine("Running: ");
            }

            foreach (string filename in filenames)
            {
                if (verbose)
                {
                    Console.WriteLine("\tclang-format {0} {1}", formatFix, filename);
                }

                List<string> commandArgs = new List<string> { formatFix, filename };
                CommandResult result = TryCommand("clang-format", commandArgs, true);
                string[] fixedContents = result.StdOut.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                if (!fix)
                {
                    int i = 0;
                    // Diff fixedContents and contents of actual file
                    foreach (string line in File.ReadLines(filename))
                    {
                        if (i >= fixedContents.Length || line != fixedContents[i])
                        {
                            if (verbose)
                            {
                                Console.WriteLine("Clang-format: There are formatting errors in " + filename);
                                Console.WriteLine("              First error at line {0}.", i);
                                Console.WriteLine("Before: {0}", line);
                                if (i < fixedContents.Length)
                                {
                                    Console.WriteLine("After:  {0}", fixedContents[i]);
                                }
                                else
                                {
                                    Console.WriteLine("After:");
                                }
                            }
                            formatOk = false;
                            break;
                        }
                        i++;
                    }
                }
            }

            return formatOk;
        }
    }
}
