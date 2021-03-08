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
using System.Xml;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ManagedCodeGen
{
    public class jitformat
    {
        // Define options to be parsed 
        public class Config
        {
            private static string s_configFileName = "config.json";
            private static string s_configFileRootKey = "format";

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
            private string _compileCommands = null;
            private bool _rewriteCompileCommands = false;

            private JObject _jObj;
            private string _jitUtilsRoot = null;

            public Config(string[] args)
            {
                LoadFileConfig();

                _syntaxResult = ArgumentSyntax.Parse(args, syntax =>
                {
                    syntax.DefineOption("a|arch", ref _arch, "The architecture of the build (options: x64, x86)");
                    syntax.DefineOption("o|os", ref _os, "The operating system of the build (options: Windows, OSX, Linux etc.)");
                    syntax.DefineOption("b|build", ref _build, "The build type of the build (options: Release, Checked, Debug)");
                    syntax.DefineOption("c|coreclr", ref _rootPath, "Full path to base runtime/src/coreclr directory");
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

                if (_verbose)
                {
                    Console.WriteLine("Running: {0} {1}", "dotnet", String.Join(" ", commandArgs));
                }

                // Running "dotnet" (the executable, not the .cmd/.sh wrapper script) when the current
                // directory is within a runtime repo clone does not give us the information we want:
                // it is missing the "OS Platform" and "RID" lines. So, pick some other directory that
                // is expected to not be in a repo clone, and run the "dotnet" command from that directory.
                string commandWorkingDirectory = "";
                OperatingSystem os = Environment.OSVersion;
                PlatformID pid = os.Platform;
                switch (pid)
                {
                    case PlatformID.Win32NT:
                        commandWorkingDirectory = Environment.SystemDirectory;
                        break;
                    case PlatformID.Unix:
                        commandWorkingDirectory = "/"; // Use the root directory
                        break;
                    default:
                        break;
                }

                ProcessResult result = Utility.ExecuteProcess("dotnet", commandArgs, true, commandWorkingDirectory);

                if (result.ExitCode != 0)
                {
                    Console.Error.WriteLine("dotnet --info returned non-zero");
                }

                var lines = result.StdOut.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                foreach (var line in lines)
                {
                    Regex pattern = new Regex(@"OS Platform:([\sA-Za-z0-9\.-]*)$");
                    Match match = pattern.Match(line);
                    if (match.Success)
                    {
                        if (match.Groups[1].Value.Trim() == "Windows")
                        {
                            _os = "Windows";
                        }
                        else if (match.Groups[1].Value.Trim() == "Darwin")
                        {
                            _os = "OSX";
                        }
                        else if (match.Groups[1].Value.Trim() == "Linux")
                        {
                            // Assuming anything other than Windows or OSX is a Linux flavor
                            _os = "Linux";
                        }
                        else
                        {
                            Console.WriteLine("Unknown operating system. Please specify with --os");
                            Environment.Exit(-1);
                        }
                    }
                }
            }

            private void validate()
            {
                if (_arch == null)
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
                    _syntaxResult.ReportError("Specify --arch, --os, and --build for clang-tidy run.");
                }

                if (_rootPath == null)
                {
                    if (_verbose)
                    {
                        Console.WriteLine("Discovering --coreclr.");
                    }
                    _rootPath = Utility.GetRepoRoot(_verbose);
                    _rootPath = Path.Combine(_rootPath, "src", "coreclr");
                    if (_rootPath == null)
                    {
                        _syntaxResult.ReportError("Specify --coreclr");
                    }
                    else
                    {
                        Console.WriteLine("Using --coreclr={0}", _rootPath);
                    }
                }

                if (!Directory.Exists(_rootPath))
                {
                    // If _rootPath doesn't exist, it is an invalid path
                    _syntaxResult.ReportError("Invalid path to coreclr directory. Specify with --coreclr");
                }
                else if (!File.Exists(Path.Combine(_rootPath, "build-runtime.cmd")) || !File.Exists(Path.Combine(_rootPath, "build-runtime.sh")) || !File.Exists(Path.Combine(_rootPath, "clr.featuredefines.props")))
                {
                    // Doesn't look like the coreclr directory.
                    _syntaxResult.ReportError("Invalid path to coreclr directory. Specify with --coreclr");
                }

                // Check that we can find compile_commands.json on windows
                if (_os.ToLower() == "windows")
                {
                    // If the user didn't specify a compile_commands.json, we need to see if one exists, and if not, create it.
                    if (!_untidy && _compileCommands == null)
                    {
                        string[] compileCommandsPath = { _rootPath, "..", "..", "artifacts", "nmakeobj", _os + "." + _arch + "." + _build, "compile_commands.json" };
                        _compileCommands = Path.Combine(compileCommandsPath);
                        _rewriteCompileCommands = true;

                        if (!File.Exists(_compileCommands))
                        {
                            // We haven't done a build, so we need to do one.
                            if (_verbose)
                            {
                                Console.WriteLine("Neither compile_commands.json exists, nor is there a build log. Running CMake to generate compile_commands.json.");
                            }

                            string[] commandArgs = { _arch, _build, "-configureonly", "-ninja" };
                            string buildPath = Path.Combine(_rootPath, "build-runtime.cmd");

                            if (_verbose)
                            {
                                Console.WriteLine("Running: {0} {1}", buildPath, String.Join(" ", commandArgs));
                            }

                            ProcessResult result = Utility.ExecuteProcess(buildPath, commandArgs, !_verbose, _rootPath);

                            if (result.ExitCode != 0)
                            {
                                Console.WriteLine("There was an error running CMake to generate compile_commands.json. Please do a full build to generate a build log.");
                                Environment.Exit(-1);
                            }
                        }
                    }
                }

                // Check that we can find the compile_commands.json file on other platforms
                else
                {
                    // If the user didn't specify a compile_commands.json, we need to see if one exists, and if not, create it.
                    if (!_untidy && _compileCommands == null)
                    {
                        string[] compileCommandsPath = { _rootPath, "..", "..", "artifacts", "obj", "coreclr", _os + "." + _arch + "." + _build, "compile_commands.json" };
                        _compileCommands = Path.Combine(compileCommandsPath);
                        _rewriteCompileCommands = true;

                        if (!File.Exists(Path.Combine(compileCommandsPath)))
                        {
                            Console.WriteLine("Can't find compile_commands.json file. Running configure.");
                            string[] commandArgs = { _arch, _build, "configureonly", "-cmakeargs", "-DCMAKE_EXPORT_COMPILE_COMMANDS=1" };
                            string buildPath = Path.Combine(_rootPath, "build-runtime.sh");

                            if (_verbose)
                            {
                                Console.WriteLine("Running: {0} {1}", buildPath, String.Join(" ", commandArgs));
                            }

                            ProcessResult result = Utility.ExecuteProcess(buildPath, commandArgs, true, _rootPath);

                            if (result.ExitCode != 0)
                            {
                                Console.WriteLine("There was an error running CMake to generate compile_commands.json. Please run build-runtime.sh configureonly");
                                Environment.Exit(-1);
                            }
                        }
                    }
                }
            }

            private void LoadFileConfig()
            {
                _jitUtilsRoot = Environment.GetEnvironmentVariable("JIT_UTILS_ROOT");

                if (_jitUtilsRoot != null)
                {
                    string path = Path.Combine(_jitUtilsRoot, s_configFileName);

                    if (File.Exists(path))
                    {
                        string configJson = File.ReadAllText(path);

                        _jObj = JObject.Parse(configJson);
                        
                        // Check if there is any default config specified.
                        if (_jObj[s_configFileRootKey]["default"] != null)
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

                            // Set up _rootPath.
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

                        Console.WriteLine("Environment variable JIT_UTILS_ROOT found - configuration loaded.");
                    }
                    else
                    {
                        Console.Error.WriteLine("Can't find {0}", path);
                    }
                }
            }

            public T ExtractDefault<T>(string name, out bool found)
            {
                var token = _jObj[s_configFileRootKey]["default"][name];

                if (token != null)
                {
                    found = true;

                    try
                    {
                        return token.Value<T>();
                    }
                    catch (System.FormatException e)
                    {
                        Console.Error.WriteLine("Bad format for default {0}.  See " + s_configFileName, name, e);
                    }
                }

                found = false;
                return default(T);
            }

            public bool IsWindows { get { return (_os.ToLower() == "windows"); } }
            public bool DoVerboseOutput { get { return _verbose; } }
            public bool DoClangTidy { get { return !_untidy; } }
            public bool DoClangFormat { get { return !_noformat; } }
            public bool Fix { get { return _fix; } }
            public bool IgnoreErrors { get { return _ignoreErrors; } }
            public bool RewriteCompileCommands { get { return _rewriteCompileCommands; } }
            public string CoreCLRRoot { get { return _rootPath; } }
            public string Arch { get { return _arch; } }
            public string OS { get { return _os; } }
            public string Build { get { return _build; } }
            public string CompileCommands { get { return _compileCommands; } }
            public IReadOnlyList<string> Filenames { get { return _filenames; } }
            public IReadOnlyList<string> Projects { get { return _projects.Count == 0 ? new List<string>{"dll"} : _projects; } }
            public string SourceDirectory { get { return _srcDirectory; } }
        }

        private class CompileCommand
        {
            public string directory;
            public string command;
            public string file;

            public CompileCommand(string dir, string cmd, string filename)
            {
                directory = dir;
                command = cmd;
                file = filename;
            }
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
                foreach (string filename in Directory.GetFiles(Path.Combine(config.CoreCLRRoot, config.SourceDirectory)))
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
                        prefix = Path.Combine(config.CoreCLRRoot, config.SourceDirectory);
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
                string[] newCompileCommandsDirPath = { config.CoreCLRRoot, "..", "..", "artifacts", "obj", "coreclr", config.OS + "." + config.Arch + "." + config.Build };
                string compileCommands = config.CompileCommands;
                string newCompileCommandsDir = Path.Combine(newCompileCommandsDirPath);
                string newCompileCommands = Path.Combine(newCompileCommandsDir, "compile_commands_full.json");

                if (config.RewriteCompileCommands)
                {
                    // Create the compile_commands directory. If it already exists, CreateDirectory will do nothing.
                    Directory.CreateDirectory(newCompileCommandsDir);

                    // Move original compile_commands file on non-windows
                    try
                    {
                        // Delete newCompileCommands if it exists. If it does not exist, no exception is thrown
                        File.Delete(newCompileCommands);
                    }
                    catch (DirectoryNotFoundException dirNotFound)
                    {
                        Console.WriteLine("Error deleting {0}", newCompileCommands);
                        Console.WriteLine(dirNotFound.Message);
                    }

                    File.Move(config.CompileCommands, newCompileCommands);
                }

                // Set up compile_commands.json. On Windows, we need to generate it. On other platforms,
                // it will be generated by cmake, and we just need to grab the path to it.

                foreach (string project in config.Projects)
                {
                    compileCommands = rewriteCompileCommands(newCompileCommands, project);

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

                // All generated compile_commands.json files should be deleted.
                if (config.RewriteCompileCommands)
                {
                    File.Delete(config.CompileCommands);
                }

                // In cases where a compile_commands.json already existed, and we rewrote it, move the original
                // compile_commands.json back to it original file now that we are done with running clang_tidy.
                if (config.RewriteCompileCommands)
                {
                    File.Move(newCompileCommands, config.CompileCommands);
                    File.Delete(newCompileCommands);
                }
            }

            if (config.DoClangFormat)
            {
                if (!RunClangFormat(filenames, config.Fix, verbose))
                {
                    Console.WriteLine("Clang-format found formatting errors.");
                    returncode -= 2;
                }
            }

            if (returncode != 0)
            {
                Console.WriteLine("jit-format found formatting errors. Run:");
                if (returncode == -2)
                {
                    // If returncode == 2, the only thing that found errors was clang-format
                    Console.WriteLine("\tjit-format --fix --untidy");
                }
                else
                {
                    // If returncode == -1, clang-tidy found errors and both tidy and format need to be rerun
                    // If returncode == -3, both clang-tidy and clang-format found errors
                    Console.WriteLine("\tjit-format --fix");
                }
            }

            return returncode;
        }

        // This method reads in a compile_command.json file, and writes a new json file with only the entries
        // commands for files found in the project specified. For example, if project is dll, it will write a
        // new compile_commands file (called compile_commands_dll.json) with only the entries whose directory
        // is jit/dll.
        public static string rewriteCompileCommands (string compileCommandFile, string project)
        {
            string allCommands = File.ReadAllText(compileCommandFile);
            JArray commands = (JArray)JArray.Parse(allCommands);
            List<CompileCommand> newCommands = new List<CompileCommand>();

            foreach (JObject command in commands.Children<JObject>())
            {
                // Search for directory entries containing jit/<project>
                if (command["directory"].Value<string>().Contains("jit/" + project))
                {
                    // Add the command to our list of new commands
                    string directory = command["directory"].Value<string>();
                    string compileCommand = command["command"].Value<string>().Replace("-I", "-isystem").Replace("\\","/");
                    if (compileCommand.Contains("cl.exe"))
                    {
                        // First extract cl.exe path: it may contain spaces.
                        int clExeIndex = compileCommand.IndexOf("cl.exe");
                        int spaceAfterClExeIndex = compileCommand.IndexOf(" ", clExeIndex);
                        string clExeCommand = compileCommand.Substring(0, spaceAfterClExeIndex);

                        string[] compileCommandsSplit = compileCommand.Substring(spaceAfterClExeIndex).Split(new[] {" ", Environment.NewLine}, StringSplitOptions.None);
                        compileCommand = clExeCommand + " -target x86_64-pc-windows-msvc -fms-extensions -fms-compatibility -fmsc-version=1900 -fexceptions -fcxx-exceptions " +
                                                        "-DSOURCE_FORMATTING=1 -D_ALLOW_COMPILER_AND_STL_VERSION_MISMATCH";

                        foreach (string option in compileCommandsSplit)
                        {
                            if (option.Contains("-isystem"))
                            {
                                compileCommand = compileCommand + " " + option;
                            }
                            else if (option.Contains("-D"))
                            {
                                compileCommand = compileCommand + " " + option;
                            }
                            // Include the path of the source file to check but don't include the option specifying the location 
                            // of the precompiled header file. It's not needed for clang-tidy and currently the precompiled
                            // header won't be found at the specified location: we run the build that generates compile_commands.json
                            // in ConfigureOnly mode so the precompiled headers are not generated.
                            else if (option.Contains("jit") && !option.StartsWith("/Fp"))
                            {
                                compileCommand = compileCommand + " " + option;
                            }
                        }
                    }
                    string file = command["file"].Value<string>();
                    newCommands.Add(new CompileCommand(directory, compileCommand, file));
                }

            }

            // write commands back to a file.
            string newCompileCommandsFileName = Path.Combine(Path.GetDirectoryName(compileCommandFile), "compile_commands.json");
            string json = JsonConvert.SerializeObject(newCommands.ToArray(), Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText(newCompileCommandsFileName, json);

            return newCompileCommandsFileName;
        }

        public static bool RunClangTidy(List<string> filenames, string compileCommands, bool ignoreErrors, bool fix, bool verbose)
        {
            bool formatOk = true;
            string checks = "readability-braces*,modernize-use-nullptr";

            if (fix)
            {
                foreach (string filename in filenames)
                {
                    formatOk &= DoClangTidyInnerLoop(fix, ignoreErrors, checks, compileCommands, filename, verbose);
                }
            }
            else
            {
                Parallel.ForEach(filenames, (filename) =>
                    {
                        if (!DoClangTidyInnerLoop(fix, ignoreErrors, checks, compileCommands, filename, verbose))
                        {
                            formatOk = false;
                        }
                    });
            }

            return formatOk;
        }

        public static bool DoClangTidyInnerLoop(bool fix, bool ignoreErrors, string checks, string compileCommands, string filename, bool verbose)
        {
            string tidyFix = fix ? "-fix" : "";
            string fixErrors = ignoreErrors && fix ? "-fix-errors" : "";

            bool formatOk = true;

            if (filename.EndsWith(".cpp"))
            {
                List<string> commandArgs = new List<string> { tidyFix, "-checks=-*," + checks, fixErrors, "-header-filter=jit/.*", "-p=" + compileCommands, filename };

                if (verbose)
                {
                    Console.WriteLine("Running: {0} {1}", "clang-tidy", String.Join(" ", commandArgs));
                }

                ProcessResult result = Utility.ExecuteProcess("clang-tidy", commandArgs, true);

                if (!fix && (result.StdOut.Contains("warning:") || (!ignoreErrors && (result.StdOut.Contains("error:") || result.StdOut.Contains("Error")))))
                {
                    if (verbose)
                    {
                        Console.WriteLine("");
                        Console.WriteLine("clang-tidy: there are formatting errors in {0}", filename);
                    }

                    formatOk = false;
                }

                if (!ignoreErrors && (result.StdErr.Contains("error:") || result.StdErr.Contains("Error")))
                {
                    Console.Error.WriteLine("Error in clang-tidy: {0}", result.StdErr);
                    formatOk = false;
                }

                if (verbose && !fix && (result.StdOut.Contains("warning:") || !ignoreErrors))
                {
                    Console.WriteLine(result.StdOut);
                }
            }

            return formatOk;
        }

        public static bool RunClangFormat(List<string> filenames, bool fix, bool verbose)
        {
            string formatFix = fix ? "-i" : "";
            string outputReplacementXml = fix ? "" : "-output-replacements-xml";
            bool formatOk = true;
            int quietErrorLimit = 10;

            List<string> clangFormatErrors = new List<string>();

            Parallel.ForEach(filenames, (filename) =>
                {
                    Process process = new Process();

                    // Run clang-format
                    List<string> commandArgs = new List<string> { formatFix, "-style=file", outputReplacementXml, filename };

                    if (verbose)
                    {
                        Console.WriteLine("Running: {0} {1}", "clang-format", String.Join(" ", commandArgs));
                    }

                    ProcessResult result = Utility.ExecuteProcess("clang-format", commandArgs, true);

                    if (result.StdOut.Contains("<replacement ") && !fix)
                    {
                        if (verbose)
                        {
                            // Read in the file
                            string fileContents = File.ReadAllText(filename);
                            string[] fileContentsList = fileContents.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                            int offsetChange = 0;

                            // Read in the xml generated by clang-format
                            var doc = new XmlDocument();
                            doc.LoadXml(result.StdOut);

                            XmlNodeList replacements = doc.DocumentElement.ChildNodes;

                            string output = "clang-format: there are formatting errors in " + filename + "\n";

                            foreach (XmlNode replacement in replacements)
                            {
                                // Figure out offset and length for each replacement
                                if (replacement.Name.Equals("replacement"))
                                {
                                    // We use offsetChange to calculate the new offset based on other formatting
                                    // changes that have already been made.
                                    int offset = Int32.Parse(replacement.Attributes["offset"].Value) + offsetChange;
                                    int length = Int32.Parse(replacement.Attributes["length"].Value);
                                    string replacementText = replacement.InnerText;

                                    // Undo the replacements clang-format makes when it prints the xml
                                    replacementText = replacementText.Replace("&#10;", "\\n");
                                    replacementText = replacementText.Replace("&#13;", "\\r");
                                    replacementText = replacementText.Replace("&lt;;", "<");
                                    replacementText = replacementText.Replace("&amp;", "&");

                                    // To calculate the cummulative amount of bytes of change we have made, we
                                    // subtract the length of text that we will be replacing, and add the
                                    // length of text that we will be inserting.
                                    offsetChange = offsetChange - length + replacementText.Length;

                                    // To calculate the line numbers, we scan to the offset in the file, and
                                    // count the number of new lines we have seen. To calculate the end line
                                    // number of the code snippet, we count the number of newlines up to the
                                    // location of offset + length
                                    var startLineNumber = fileContents.Take(offset).Count(c => c == '\n');
                                    var endLineNumber = fileContents.Take(offset + length).Count(c => c == '\n');

                                    output = output + "At Line " + (startLineNumber + 1) + " Before:\n";

                                    foreach (string line in fileContentsList.Skip(startLineNumber).Take(endLineNumber - startLineNumber + 1))
                                    {
                                        output = output + line + "\n";
                                    }

                                    output = output + "After:\n";

                                    // To do the replacement, we remove the old text between offset and offset + length
                                    // and insert the new text
                                    fileContents = fileContents.Remove(offset, length).Insert(offset, replacementText);
                                    fileContentsList = fileContents.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                                    startLineNumber = fileContents.Take(offset).Count(c => c == '\n');
                                    endLineNumber = fileContents.Take(offset+replacementText.Length).Count(c => c == '\n');

                                    foreach (string line in fileContentsList.Skip(startLineNumber).Take(endLineNumber - startLineNumber + 1))
                                    {
                                        output = output + line + "\n";
                                    }

                                }
                            }

                            clangFormatErrors.Add(output);
                        }

                        formatOk = false;
                    }
                });

            int quietErrorCount = 0;
            foreach (string failure in clangFormatErrors)
            {
                if (verbose || quietErrorCount < quietErrorLimit)
                {
                    Console.WriteLine("");
                    Console.WriteLine("{0}", failure);
                    quietErrorCount++;
                }
            }

            return formatOk;
        }
    }
}
