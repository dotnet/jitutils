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
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CommandResult = Microsoft.DotNet.Cli.Utils.CommandResult;
using System.CommandLine.Invocation;
using Process = System.Diagnostics.Process;

namespace ManagedCodeGen
{
    public class jitformat
    {
        // Define options to be parsed 
        public class Config
        {
            private static string s_configFileName = "config.json";
            private static string s_configFileRootKey = "format";
            private bool _rewriteCompileCommands = false;
            private JObject _jObj;
            private string _jitUtilsRoot = null;

            void ReportError(string message)
            {
                Console.WriteLine(message);
                Error = true;
            }

            private void SetPlatform()
            {
                // Extract system RID from dotnet cli
                List<string> commandArgs = new List<string> { "--info" };
                CommandResult result = Utility.TryCommand("dotnet", commandArgs, true);

                if (result.ExitCode != 0)
                {
                    ReportError("dotnet --info returned non-zero");
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
                            OS = "Windows_NT";
                        }
                        else if (match.Groups[1].Value.Trim() == "Darwin")
                        {
                            OS = "OSX";
                        }
                        else if (match.Groups[1].Value.Trim() == "Linux")
                        {
                            // Assuming anything other than Windows or OSX is a Linux flavor
                            OS = "Linux";
                        }
                        else
                        {
                            ReportError("Unknown operating system. Please specify with --os");
                        }
                    }
                }
            }

            public void Validate()
            {
                if (Arch == null)
                {
                    if (Verbose)
                    {
                        Console.WriteLine("Defaulting architecture to x64.");
                    }
                    Arch = "x64";
                }

                if (Build == null)
                {
                    if (Verbose)
                    {
                        Console.WriteLine("Defaulting build to Debug.");
                    }

                    Build = "Debug";
                }

                if (OS == null)
                {
                    if (Verbose)
                    {
                        Console.WriteLine("Discovering operating system.");
                    }

                    SetPlatform();

                    if (Error)
                    {
                        return;
                    }

                    if (Verbose)
                    {
                        Console.WriteLine("Operating system is {0}", OS);
                    }
                }

                if (OS == "Windows")
                {
                    OS = "Windows_NT";
                }

                if (SourceDirectory == null)
                {
                    if (Verbose)
                    {
                        Console.WriteLine("Formatting jit directory.");
                    }
                    SourceDirectory = "jit";
                }

                if (Projects.Count == 0 && Verbose)
                {
                    Console.WriteLine("Formatting dll project.");
                }

                if (!Untidy && ( (Arch == null) || (OS == null) || (Build == null)))
                {
                    ReportError("Specify --arch, --plaform, and --build for clang-tidy run.");
                }

                if (CoreCLR == null)
                {
                    if (Verbose)
                    {
                        Console.WriteLine("Discovering --coreclr.");
                    }
                    CoreCLR = Utility.GetRepoRoot(Verbose);
                    if (CoreCLR == null)
                    {
                        ReportError("Specify --coreclr");
                    }
                    else
                    {
                        Console.WriteLine("Using --coreclr={0}", CoreCLR);
                    }
                }

                if (!Directory.Exists(CoreCLR))
                {
                    // If _rootPath doesn't exist, it is an invalid path
                    ReportError("Invalid path to coreclr directory. Specify with --coreclr");
                }
                else if (!File.Exists(Path.Combine(CoreCLR, "build.cmd")) || !File.Exists(Path.Combine(CoreCLR, "build.sh")))
                {
                    // If _rootPath\build.cmd or _rootPath\build.sh do not exist, it is an invalid path to a coreclr repo
                    ReportError("Invalid path to coreclr directory. Specify with --coreclr");
                }

                // Check that we can find compile_commands.json on windows
                if (OS == "Windows_NT")
                {
                    // If the user didn't specify a compile_commands.json, we need to see if one exists, and if not, create it.
                    if (!Untidy && CompileCommands == null)
                    {
                        string[] compileCommandsPath = { CoreCLR, "bin", "nmakeobj", "Windows_NT." + Arch + "." + Build, "compile_commands.json" };
                        CompileCommands = Path.Combine(compileCommandsPath);
                        _rewriteCompileCommands = true;

                        if (!File.Exists(CompileCommands))
                        {
                            // We haven't done a build, so we need to do one.
                            if (Verbose)
                            {
                                ReportError("Neither compile_commands.json exists, nor is there a build log. Running CMake to generate compile_commands.json.");
                            }

                            string[] commandArgs = { Arch, Build, "usenmakemakefiles" };
                            string buildPath = Path.Combine(CoreCLR, "build.cmd");
                            CommandResult result = Utility.TryCommand(buildPath, commandArgs, !Verbose, CoreCLR);

                            if (result.ExitCode != 0)
                            {
                                ReportError("There was an error running CMake to generate compile_commands.json. Please do a full build to generate a build log.");
                            }
                        }
                    }
                }

                // Check that we can find the compile_commands.json file on other platforms
                else
                {
                    // If the user didn't specify a compile_commands.json, we need to see if one exists, and if not, create it.
                    if (!Untidy && CompileCommands == null)
                    {
                        string[] compileCommandsPath = { CoreCLR, "bin", "obj", OS + "." + Arch + "." + Build, "compile_commands.json" };
                        CompileCommands = Path.Combine(compileCommandsPath);
                        _rewriteCompileCommands = true;

                        if (!File.Exists(Path.Combine(compileCommandsPath)))
                        {
                            Console.WriteLine("Can't find compile_commands.json file. Running configure.");
                            string[] commandArgs = { Arch, Build, "configureonly", "-cmakeargs", "-DCMAKE_EXPORT_COMPILE_COMMANDS=1" };
                            string buildPath = Path.Combine(CoreCLR, "build.sh");
                            CommandResult result = Utility.TryCommand(buildPath, commandArgs, true, CoreCLR);

                            if (result.ExitCode != 0)
                            {
                                ReportError("There was an error running CMake to generate compile_commands.json. Please run build.sh configureonly");
                            }
                        }
                    }
                }
            }

            public void LoadFileConfig()
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
                            Arch = (found) ? arch : Arch;

                            // Set up build
                            var build = ExtractDefault<string>("build", out found);
                            Build = (found) ? build : Build;

                            // Set up os
                            var os = ExtractDefault<string>("os", out found);
                            OS = (found) ? os : OS;

                            // Set up core_root.
                            var rootPath = ExtractDefault<string>("coreclr", out found);
                            CoreCLR = (found) ? rootPath : CoreCLR;

                            // Set up compileCommands
                            var compileCommands = ExtractDefault<string>("compile-commands", out found);
                            CompileCommands = (found) ? compileCommands : CompileCommands;

                            // Set flag from default for verbose.
                            var verbose = ExtractDefault<bool>("verbose", out found);
                            Verbose = (found) ? verbose : Verbose;

                            // Set up untidy
                            var untidy = ExtractDefault<bool>("untidy", out found);
                            Untidy = (found) ? untidy : Untidy;

                            // Set up noformat
                            var noformat = ExtractDefault<bool>("noformat", out found);
                            NoFormat = (found) ? noformat : NoFormat;

                            // Set up fix
                            var fix = ExtractDefault<bool>("fix", out found);
                            Fix = (found) ? fix : Fix;
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

            public bool IsWindows { get { return (OS == "Windows_NT"); } }
            public bool Verbose { get; set; }
            public bool Untidy { get; set; }
            public bool DoClangTidy { get { return !Untidy; } }
            public bool NoFormat { get; set; }
            public bool DoClangFormat { get { return !NoFormat; } }
            public bool Fix { get; set; }
            public bool IgnoreErrors { get; set; }
            public bool RewriteCompileCommands { get { return _rewriteCompileCommands; } }
            public string CoreCLR { get; set; }
            public string Arch { get; set; }
            public string OS { get; set; }
            public string Build { get; set; }
            public string CompileCommands { get; set; }
            public IReadOnlyList<string> Filenames { get; set; }
            public IReadOnlyList<string> Projects { get; set; }
            public string SourceDirectory { get; set; }
            public bool Error { get; set; }
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
            RootCommand rootCommand = new RootCommand();

            Option archOption = new Option("--arch", "The architecture of the build (options: x64, x86)", new Argument<string>());
            archOption.AddAlias("-a");
            Option osOption = new Option("--os", "The operating system of the build (options: Windows, OSX, Ubuntu, Fedora, etc.)", new Argument<string>());
            osOption.AddAlias("-o");
            Option buildOption = new Option("--build", "The build type of the build (options: Release, Checked, Debug)", new Argument<string>());
            buildOption.AddAlias("-b");
            Option coreclrOption = new Option("--coreclr", "Full path to base coreclr directory", new Argument<string>());
            coreclrOption.AddAlias("-c");
            Option compileCommandsOption = new Option("--compile-commands", "Full path to compile_commands.json", new Argument<string>());
            Option verboseOption = new Option("--verbose", "Enable verbose output.", new Argument<bool>());
            verboseOption.AddAlias("-v");
            Option untidyOption = new Option("--untidy", "Do not run clang-tidy", new Argument<bool>());
            Option noFormatOption = new Option("--noformat", "Do not run clang-format", new Argument<bool>());
            Option fixOption = new Option("--fix", "Fix formatting errors discovered by clang-format and clang-tidy.", new Argument<bool>());
            fixOption.AddAlias("-f");
            Option ignoreOption = new Option("--ignore-errors", "Ignore clang-tidy errors", new Argument<bool>());
            ignoreOption.AddAlias("-i");
            Option projectsOption = new Option("--projects", "List of build projects clang-tidy should consider (e.g. dll, standalone, protojit, etc.). Default: dll", 
                new Argument<string>() { Arity = ArgumentArity.OneOrMore });

            Argument fileNameList = new Argument<string>() { Arity = ArgumentArity.OneOrMore };
            fileNameList.Name = "filenames";
            fileNameList.Description = "Optional list of files that should be formatted.";

            rootCommand.AddOption(archOption);
            rootCommand.AddOption(osOption);
            rootCommand.AddOption(buildOption);
            rootCommand.AddOption(coreclrOption);
            rootCommand.AddOption(compileCommandsOption);
            rootCommand.AddOption(verboseOption);
            rootCommand.AddOption(untidyOption);
            rootCommand.AddOption(noFormatOption);
            rootCommand.AddOption(fixOption);
            rootCommand.AddOption(ignoreOption);
            rootCommand.AddOption(projectsOption);

            rootCommand.AddArgument(fileNameList);

            rootCommand.Handler = CommandHandler.Create<Config>((config) =>
            {
                config.LoadFileConfig();
                config.Validate();

                if (config.Error)
                {
                    return -1;
                }

                return Process(config);
            });

            return rootCommand.InvokeAsync(args).Result;
        }

        public static int Process(Config config)
        { 
            int returncode = 0;
            bool verbose = config.Verbose;

            List<string> filenames = new List<string>();

            // Run clang-format over specified files, or all files if none were specified
            if (config.Filenames.Count() == 0)
            {
                // add all files to a list of files
                foreach (string filename in Directory.GetFiles(Path.Combine(config.CoreCLR, "src", config.SourceDirectory)))
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
                    if (!filename.Contains(config.CoreCLR))
                    {
                        prefix = Path.Combine(config.CoreCLR, "src", config.SourceDirectory);
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
                string[] newCompileCommandsDirPath = { config.CoreCLR, "bin", "obj", config.OS + "." + config.Arch + "." + config.Build };
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
                    if (config.IsWindows && config.RewriteCompileCommands)
                    {
                        compileCommands = rewriteCompileCommands(newCompileCommands, project);
                    }
                    else
                    {
                        compileCommands = rewriteCompileCommands(newCompileCommands, project);
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
                        string[] compileCommandsSplit = compileCommand.Split(new[] {" ", Environment.NewLine}, StringSplitOptions.None);
                        compileCommand = "";

                        foreach (string option in compileCommandsSplit)
                        {
                            if (option.ToLower().Contains("cl.exe"))
                            {
                                compileCommand = compileCommand + option + " -target x86_64-pc-windows-msvc -fms-extensions -fms-compatibility -fmsc-version=1900 -fexceptions -fcxx-exceptions -DSOURCE_FORMATTING=1 ";
                            }
                            else if (option.Contains("-isystem"))
                            {
                                compileCommand = compileCommand + option + " ";
                            }
                            else if (option.Contains("-D"))
                            {
                                compileCommand = compileCommand + option + " ";
                            }
                            else if (option.Contains("src/jit"))
                            {
                                compileCommand = compileCommand + option;
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

            if (verbose)
            {
                Console.WriteLine("Running: ");
            }

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
                        formatOk &= DoClangTidyInnerLoop(fix, ignoreErrors, checks, compileCommands, filename, verbose);
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
                if (verbose)
                {
                    Console.WriteLine("\tclang-tidy {0} -checks=-*,{1} {2} -header-filter=src/jit/.* -p={3} {4}", tidyFix, checks, fixErrors, compileCommands, filename);
                }

                List<string> commandArgs = new List<string> { tidyFix, "-checks=-*," + checks, fixErrors, "-header-filter=src/jit/.*", "-p=" + compileCommands, filename };
                CommandResult result = Utility.TryCommand("clang-tidy", commandArgs, true);

                if (!fix && (result.StdOut.Contains("warning:") || (!ignoreErrors && result.StdOut.Contains("error:"))))
                {
                    if (verbose)
                    {
                        Console.WriteLine("");
                        Console.WriteLine("clang-tidy: there are formatting errors in {0}", filename);
                    }

                    formatOk = false;
                }

                if (!ignoreErrors && result.StdErr.Contains("error:"))
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

                    if (verbose)
                    {
                        Console.WriteLine("Running: clang-format {0} -style=file {1}", formatFix, filename);
                    }

                    // Run clang-format
                    List<string> commandArgs = new List<string> { formatFix, "-style=file", outputReplacementXml, filename };
                    CommandResult result = Utility.TryCommand("clang-format", commandArgs, true);

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
