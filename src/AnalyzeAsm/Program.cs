// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AnalyzeAsm
{
    class Program
    {
        //static string ngenFile = @"D:\git\runtime\ngen_out.txt";
        static string workingFolder = @"E:\armcompare\movz_movk";
        static Regex regex = new Regex(@"; Total bytes of code (\d+), prolog size (\d+),");
        static string armFile = Path.Combine(workingFolder, "ngen__arm64_out.txt");
        static string x64File = Path.Combine(workingFolder, "ngen__amd64_out.txt");
        static string workingFolderForJit = @"E:\armcompare\movz_movk\pmi";
        static string armFolderForJit = Path.Combine(workingFolderForJit, "arm64");
        static string x64FolderForJit = Path.Combine(workingFolderForJit, "x64");
        static Regex arm64InstrRegexForCrossGen = new Regex("^        [0-9A-F]{8}          (\\w*) ");
        static Regex x64InstrRegexForCrossGen = new Regex("^       ([0-9A-F])+\\s+(\\w*)");
        static Regex arm64InstrRegexForJit = new Regex("^            (\\w+) ");
        static Regex x64InstrRegexForJit = new Regex("^       (\\w+) ");

        static void PrintSyntax()
        {
            Console.WriteLine("AnalyzeAsm.exe <folder_path> <method_name>");
            Console.WriteLine("folder_path: Folder that contains ngen__amd64_out.txt and ngen__arm64_out.txt");
            Console.WriteLine("method_name: Method name in quotes to get assembly from.");
            Environment.Exit(0);
        }

        static void Main(string[] args)
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();
            #region Custom tool for GetAsm
            //if (args.Length != 2 || args[0] == "-?" || args[0] == "--?" || args[0] == "-help" || args[0] == "--help")
            //{
            //    PrintSyntax();
            //}

            //string workingFolder = args[0];
            //armFile = Path.Combine(workingFolder, "ngen__arm64_out.txt");
            //x64File = Path.Combine(workingFolder, "ngen__amd64_out.txt");
            //if (!Directory.Exists(workingFolder) || !File.Exists(armFile) || !File.Exists(x64File))
            //{
            //    PrintSyntax();
            //}
            //string methodName = args[1];
            //PrintAssembly(armFile, methodName);
            //Console.WriteLine("$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
            //Console.WriteLine("$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
            //PrintAssembly(x64File, methodName);
            #endregion

            //if (args.Length == 0)
            //{
            //    Summarize();
            //}
            //else
            //{
            //PrintAssembly(armFile, args[0]);
            //Console.WriteLine("$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
            //Console.WriteLine("$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
            //PrintAssembly(x64File, args[0]);
            //}
            //CalculateTotalSize();
            Distribution();
            //GetLoadStores();
            //OptimizeDmbs();
            //FindStrGroups_wzr($@"{workingFolder}\str_str_wzr_to_str_xzr-1.asm");
            //FindLdrGroups_1($@"{workingFolder}\ldr_ldr_x_to_ldp.asm");
            //FindLdrGroups_2($@"{workingFolder}\ldr_ldr_fp_to_ldp.asm");
            //FindStrGroups_1($@"{workingFolder}\str_str_x_to_stp.asm");
            //FindStrGroups_2($@"{workingFolder}\str_str_fp_to_stp.asm");
            //FindLdrThenStr($@"{workingFolder}\ldr-str.asm");
            //FindLdrThenStr($@"{workingFolder}\str-ldr.asm");
            //FindLdrLdrToMovGroups($@"{workingFolder}\ldr_to_mov.asm");
            //FindPostIndexAddrMode1($@"{workingFolder}\post-index-1.asm");
            //FindPreIndexAddrMode1($@"{workingFolder}\pre-index-1.asm");
            //FindPreIndexAddrMode2($@"{workingFolder}\pre-index-2.asm");
            //AdrpAddPairs($@"{workingFolder}\adrp-add.asm");
            //PrologEpilogInx64($@"{workingFolder}\pro-epi-x64.asm");
            //PrologEpilogInx64($@"{workingFolder}\temp.asm");
            //ArrayAccess($@"{workingFolder}\array-access.asm");
            //BasePlusRegisterOffset($@"{workingFolder}\base-register-offset.asm");
            //RedundantMovs1($@"{workingFolder}\redundant-mov-1.asm");
            //RedundantMovs2($@"{workingFolder}\redundant-mov-2.asm");
            //RedundantMovs3($@"{workingFolder}\redundant-mov-3.asm");

            watch.Stop();
            Console.WriteLine($"Hello World! took {watch.Elapsed.TotalSeconds} secs.");
        }

        static void PrintAssembly(string fileName, string name)
        {
            int lineNumber = 1;
            string line;
            bool found = false;
            StringBuilder strBuilder = new StringBuilder();
            using (FileStream fs = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (found)
                        {
                            Console.WriteLine(strBuilder.ToString());
                            strBuilder.Clear();
                            found = false;
                            //return;
                        }
                        if (line.Contains(name))
                        {
                            lineNumber = 1;
                            found = true;
                        }
                    }

                    if (found)
                    {
                        strBuilder.AppendLine($"[{lineNumber++:0000}]{line}");
                    }
                }
            }
        }

        public struct Numbers
        {
            public int ARM64CodeSize;
            public int x64CodeSize;
            public int ARM64InstrCount;
            public int x64InstrCount;

            public override string ToString()
            {
                return $"x64: {x64CodeSize} bytes, {x64InstrCount} instrs. arm64: {ARM64CodeSize} bytes, {ARM64InstrCount} instr.";
            }
        }

        /// <summary>
        /// Gets load/store instructions defined insinde instrsarms.h
        /// </summary>
        static void GetLoadStores()
        {
            List<string> loadStores = new List<string>();
            string line;
            var regex = new Regex(@"^INST\d+\((.*)\)");
            string fileName = @"D:\git\runtime\src\coreclr\src\jit\instrsarm64.h";
            using (FileStream fs = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                while ((line = sr.ReadLine()) != null)
                {
                    var match = regex.Match(line);
                    if (match.Success)
                    {
                        string value = match.Groups[1].Value;
                        var entries = value.Split(",", StringSplitOptions.RemoveEmptyEntries);
                        if (entries[3].Contains("LD") || entries[3].Contains("ST"))
                        {
                            loadStores.Add(entries[1].Replace("\"", "").Trim());
                        }
                    }
                }
            }
            Console.WriteLine(string.Join("\",\"", loadStores));
        }

        /// <summary>
        /// Motivation: https://github.com/llvm/llvm-project/blob/2946cd701067404b99c39fb29dc9c74bd7193eb3/llvm/lib/Target/ARM/ARMOptimizeBarriersPass.cpp
        /// </summary>
        static void OptimizeDmbs()
        {
            List<string> loadStores = new List<string>() { "ld1", "ld2", "ld3", "ld4", "st1", "st2", "st3", "st4", "ldr", "ldrsw", "ldrb", "ldrh", "ldrsb", "ldrsh", "str", "strb", "strh", "ld1", "ld1", "ld1", "st1", "st1", "st1", "ld1r", "ld2r", "ld3r", "ld4r", "ldp", "ldpsw", "stp", "ldnp", "stnp", "ldar", "ldarb", "ldarh", "ldxr", "ldxrb", "ldxrh", "ldaxr", "ldaxrb", "ldaxrh", "ldur", "ldurb", "ldurh", "ldursb", "ldursh", "ldursw", "stlr", "stlrb", "stlrh", "stxr", "stxrb", "stxrh", "stlxr", "stlxrb", "stlxrh", "stur", "sturb", "sturh", "casb", "casab", "casalb", "caslb", "cash", "casah", "casalh", "caslh", "cas", "casa", "casal", "casl", "ldaddb", "ldaddab", "ldaddalb", "ldaddlb", "ldaddh", "ldaddah", "ldaddalh", "ldaddlh", "ldadd", "ldadda", "ldaddal", "ldaddl", "staddb", "staddlb", "staddh", "staddlh", "stadd", "staddl", "swpb", "swpab", "swpalb", "swplb", "swph", "swpah", "swpalh", "swplh", "swp", "swpa", "swpal", "swpl" };
            List<string> branches = new List<string>() { "cbnz", "cbz", "tbnz", "tbz", "b", "bl", "blr", "br", "ret", "beq", "bne", "bhs", "blo", "bcc", "bcs", "bge", "bgt", "ble", "bls", "blt", "bmi", "bne", "bpl", "bvc", "bvs", "bhs", "bls" };
            var dmbArg = new Regex("dmb     (oshld|oshst|osh|nshld|nshst|nsh|ishld|isht|ish|sy|st|ld)");
            StringBuilder s = new StringBuilder();
            string line;
            string dmbType = null;
            string methodName = null;
            bool canRemoveNextDmb = false;
            List<string> removedDmbPerMethod = new List<string>();
            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                while ((line = sr.ReadLine()) != null)
                {
                    s.AppendLine(line);
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        // print result
                        if (removedDmbPerMethod.Count > 0)
                        {
                            Console.WriteLine($"{methodName} can remove {removedDmbPerMethod.Count}.");
                        }

                        // reset previous
                        removedDmbPerMethod.Clear();
                        canRemoveNextDmb = false;
                        dmbType = null;

                        methodName = line.Replace("; Assembly listing for method", "");
                    }
                    else
                    {
                        var instrMatch = arm64InstrRegexForCrossGen.Match(line);
                        if (instrMatch.Success)
                        {
                            string instr = instrMatch.Groups[1].Value;
                            if (instr == "dmb")
                            {
                                var match = dmbArg.Match(line);
                                string thisDmbType = match.Groups[1].Value;
                                if (canRemoveNextDmb)
                                {
                                    if (thisDmbType == dmbType)
                                    {
                                        removedDmbPerMethod.Add(thisDmbType);
                                    }
                                    else
                                    {
                                        dmbType = thisDmbType;
                                    }
                                }
                                else
                                {
                                    canRemoveNextDmb = true;
                                    dmbType = thisDmbType;
                                }
                            }
                            else if (loadStores.Contains(instr) /*|| branches.Contains(instr)*/)
                            {
                                if (canRemoveNextDmb)
                                {
                                    Console.WriteLine($"{instr} killed {dmbType}");
                                }
                                canRemoveNextDmb = false;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Prints the size diff of ARM64 vs. x64
        /// </summary>
        static void Distribution()
        {
            string sep = "|#|";
            Dictionary<string, Numbers> sizes = new Dictionary<string, Numbers>();

            var dasmFiles = Directory.GetFiles(armFolderForJit, "*.dasm", SearchOption.TopDirectoryOnly);
            foreach (var dasmFile in dasmFiles)
            {
                GetSize(dasmFile, sizes, false);
            }
            dasmFiles = Directory.GetFiles(x64FolderForJit, "*.dasm", SearchOption.TopDirectoryOnly);
            foreach (var dasmFile in dasmFiles)
            {
                GetSize(dasmFile, sizes, true);
            }
            StringBuilder strBuilder = new StringBuilder();
            strBuilder.AppendLine($"MethodName{sep}ARM64-CodeSize{sep}x64-CodeSize{sep}Diff-CodeSize{sep}RelativeDiff-CodeSize{sep}ARM64 - Instr{sep}x64-Instr{sep}Diff - Instr{sep}RelativeDiff-Instr{sep}");
            long totalCodeSizeArm64 = 0, totalCodeSizex64 = 0, totalInstrSizeArm64 = 0, totalInstrSizex64 = 0;
            foreach (var entry in sizes)
            {
                var value = entry.Value;
                if (value.ARM64InstrCount != 0 && value.x64InstrCount != 0)
                {
                    totalCodeSizeArm64 += value.ARM64CodeSize;
                    totalCodeSizex64 += value.x64CodeSize;
                    totalInstrSizeArm64 += value.ARM64InstrCount;
                    totalInstrSizex64 += value.x64InstrCount;
                    int sizeDiff = value.ARM64CodeSize - value.x64CodeSize;
                    int instrDiff = value.ARM64InstrCount - value.x64InstrCount;
                    float sizeDiffRel = (float)sizeDiff / value.x64CodeSize;
                    float instrDiffRel = (float)instrDiff / value.x64InstrCount;
                    strBuilder.Append($"{entry.Key}{sep}");
                    strBuilder.Append($"{value.ARM64CodeSize}{sep}{value.x64CodeSize}{sep}{sizeDiff}{sep}{sizeDiffRel}{sep}");
                    strBuilder.AppendLine($"{value.ARM64InstrCount}{sep}{value.x64InstrCount}{sep}{instrDiff}{sep}{instrDiffRel}{sep}");
                }
            }

            strBuilder.Append($"Total{sep}");
            strBuilder.Append($"{totalCodeSizeArm64}{sep}{totalCodeSizex64}{sep}{totalCodeSizeArm64 - totalCodeSizex64}{sep}{(totalCodeSizeArm64 - totalCodeSizex64) / totalCodeSizex64}{sep}");
            strBuilder.AppendLine($"{totalInstrSizeArm64}{sep}{totalCodeSizex64}{sep}{totalInstrSizeArm64 - totalInstrSizex64}{sep}{(totalInstrSizeArm64 - totalInstrSizex64) / totalInstrSizex64}{sep}");

            File.WriteAllText(Path.Combine(workingFolderForJit, "size-distribition.csv"), strBuilder.ToString());
            static void GetSize(string fileName, Dictionary<string, Numbers> sizeAccum, bool isX64)
            {
                StringBuilder s = new StringBuilder();
                var instrRegex = isX64 ? x64InstrRegexForJit : arm64InstrRegexForJit;
                //var instrRegex = isX64 ? x64InstrRegexForCrossGen : arm64InstrRegexForCrossGen;
                int instrCount = 0;
                string line;
                string methodName = null;
                using (FileStream fs = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BufferedStream bs = new BufferedStream(fs))
                using (StreamReader sr = new StreamReader(bs))
                {
                    while ((line = sr.ReadLine()) != null)
                    {
                        s.AppendLine(line);
                        if (line.StartsWith("; Assembly listing for method"))
                        {
                            instrCount = 0;
                            methodName = line.Replace("; Assembly listing for method", "");
                        }
                        if (line.StartsWith("; Total bytes of code "))
                        {
                            if (!sizeAccum.ContainsKey(methodName))
                            {
                                sizeAccum[methodName] = new Numbers();
                            }
                            var value = sizeAccum[methodName];

                            var match = regex.Match(line);
                            if (match.Success)
                            {
                                int sizeInBytes = int.Parse(match.Groups[1].Value);
                                if (isX64)
                                {
                                    value.x64CodeSize += sizeInBytes;
                                }
                                else
                                {
                                    value.ARM64CodeSize += sizeInBytes;
                                }
                            }
                            if (isX64)
                            {
                                value.x64InstrCount += instrCount;
                            }
                            else
                            {
                                value.ARM64InstrCount += instrCount;
                            }
                            sizeAccum[methodName] = value;

                            methodName = null;
                        }
                        else
                        {
                            var instrMatch = instrRegex.Match(line);
                            if (instrMatch.Success)
                            {
                                instrCount++;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Calculates total size
        /// </summary>
        static void CalculateTotalSize()
        {
            
            ulong totalSizeInBytes = 0;
            ulong prologSizeInBytes = 0;
            GetSize(armFile, ref totalSizeInBytes, ref prologSizeInBytes);
            Console.WriteLine($"ARM64 total bytes: {totalSizeInBytes}, total prolog size: {prologSizeInBytes}");

            totalSizeInBytes = 0;
            prologSizeInBytes = 0;
            GetSize(x64File, ref totalSizeInBytes, ref prologSizeInBytes);
            Console.WriteLine($"x64 total bytes: {totalSizeInBytes}, total prolog size: {prologSizeInBytes}");

            static void GetSize(string fileName, ref ulong totalSize, ref ulong prologSize)
            {
                string line;

                using (FileStream fs = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BufferedStream bs = new BufferedStream(fs))
                using (StreamReader sr = new StreamReader(bs))
                {
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.StartsWith("; Total bytes of code "))
                        {
                            var match = regex.Match(line);
                            if (match.Success)
                            {
                                totalSize += ulong.Parse(match.Groups[1].Value);
                                prologSize += ulong.Parse(match.Groups[2].Value);
                            }
                        }

                    }
                }
            }
        }

        /// <summary>
        /// Summarizes movz/movk per method.
        /// </summary>
        static void FindMovZMovKGroups()
        {
            bool foundMov = false;
            int total = 0;
            int hasMov = 0;
            int totalMovGroups = 0;
            int movGroup = 0;
            string header = "";
            int headerLine = 0;
            int lastMovLineNum = 0;
            int localLineNum = 0;
            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                int lineNum = 1;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {

                        if (foundMov)
                        {
                            hasMov++;
                            header = $"[1    ][{movGroup,2} groups]  {header}";
                            //header = $"[{headerLine,-10}][{movGroup,2} groups]  {header}";
                            Console.WriteLine("####################################################################################");
                            Console.WriteLine(header);
                            Console.WriteLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundMov = false;
                        movGroup = 0;

                        header = line.Replace("; Assembly listing for method ", "");
                        headerLine = lineNum;
                        localLineNum = 1;
                        total++;
                    }
                    if (line.Contains("movz") || line.Contains("movk"))
                    {
                        if (lastMovLineNum + 1 != lineNum)
                        {
                            movGroup++;
                            totalMovGroups++;
                            strBuilder.AppendLine("....................................................................................");
                        }
                        foundMov = true;
                        lastMovLineNum = lineNum;
                        strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                        //strBuilder.AppendLine($"[{lineNum,-10}]{line}");
                    }
                    lineNum++;
                    localLineNum++;
                }
            }
            Console.WriteLine($"Processed {total}. Found {hasMov} containing {totalMovGroups} movz/movk.");
        }

        /// <summary>
        /// Summarizes consecutive "str wzr, " and report if we can combine it to "str xzr"
        /// </summary>
        static void FindStrGroups_wzr(string fileName)
        {
            Regex strRegEx = new Regex("str     wzr, \\[x(\\d+)");
            bool foundStrWzr = false;
            int total = 0;
            int hasStrWzrPair = 0;
            int totalMovGroups = 0;
            string header = "";

            string prevMovRegName = "";
            int prevMovLineNum = 0;
            int localLineNum = 0;
            string prevMovInstr = string.Empty;
            
            bool existingGroup = false;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                int lineNum = 1;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundStrWzr)
                        {
                            hasStrWzrPair++;
                            //header = $"[{headerLine,-10}][{movGroup,2} groups]  {header}";
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundStrWzr = false;
                        existingGroup = false;

                        //header = line.Replace("; Assembly listing for method ", "");
                        header = line;
                        localLineNum = 1;
                        total++;
                    }
                    if (line.Contains("str     wzr,"))
                    {
                        var match = strRegEx.Match(line);
                        if (match.Success)
                        {
                            string currentRegName = match.Groups[1].Value;
                            if (prevMovLineNum + 1 == lineNum && prevMovRegName == currentRegName)
                            {
                                if (!existingGroup)
                                {
                                    strBuilder.AppendLine($"[{localLineNum - 1:0000}]{prevMovInstr}");
                                }

                                strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                                totalMovGroups++;
                                foundStrWzr = true;
                                existingGroup = true;
                            }
                            else
                            {
                                existingGroup = false;
                            }
                            prevMovInstr = line;
                            prevMovLineNum = lineNum;
                            prevMovRegName = currentRegName;
                        }
                    }
                    lineNum++;
                    localLineNum++;
                }
            }

            resultBuilder.AppendLine($"Processed {total} methods. Found {hasStrWzrPair} methods containing {totalMovGroups} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        static void FindLdrGroups_1(string fileName)
        {
            Regex ldrRegEx = new Regex("ldr     x(\\d+), \\[x(\\d+)");
            FindLdrStrGroups(ldrRegEx, fileName);
        }

        static void FindLdrGroups_2(string fileName)
        {
            Regex ldrRegEx = new Regex("ldr     x(\\d+), \\[fp,");
            FindLdrStrGroups(ldrRegEx, fileName);
        }

        static void FindStrGroups_1(string fileName)
        {
            Regex strRegEx = new Regex("str     x(\\d+), \\[x(\\d+)");
            FindLdrStrGroups(strRegEx, fileName, isLdr: false);
        }

        static void FindStrGroups_2(string fileName)
        {
            Regex strRegEx = new Regex("str     x(\\d+), \\[fp,");
            FindLdrStrGroups(strRegEx, fileName, isLdr: false);
        }

        /// <summary>
        /// Find the following where load/store happens from same source and our next to each other.
        ///     ldr x1, [source]
        ///     str x1, [source]
        ///  Also can find other way round by flipping the firstInstr and secondInstr variable values.
        /// </summary>
        /// <param name="fileName"></param>
        static void FindLdrThenStr(string fileName)
        {
            Regex secondInstrRegex = new Regex("ldr     ([x|w](\\d+)), \\[(.*)\\]");
            Regex firstInstrRegex = new Regex("str     ([x|w](\\d+)), \\[(.*)\\]");
            string secondInstrToFind = "ldr     ";
            string firstInstrToFind = "str     ";
            bool foundLdrStrPair = false;
            int total = 0;
            int hasStrWzrPair = 0;
            int totalPairs = 0;
            string header = "";

            bool prevWasMov = false;
            string prevDstReg = "";
            string prevSrcReg = "";
            int prevMovLineNum = 0;
            int localLineNum = 0;
            string prevMovInstr = string.Empty;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                int lineNum = 1;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundLdrStrPair)
                        {
                            hasStrWzrPair++;
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                            //Console.WriteLine("####################################################################################");
                            //Console.WriteLine(header);
                            //Console.WriteLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundLdrStrPair = false;

                        //header = line.Replace("; Assembly listing for method ", "");
                        header = line;
                        localLineNum = 1;
                        total++;
                    }
                    if (line.Contains(firstInstrToFind))
                    {
                        var match = firstInstrRegex.Match(line);
                        if (match.Success)
                        {
                            prevSrcReg = match.Groups[1].Value;
                            prevDstReg = match.Groups[3].Value;

                            if (prevSrcReg != prevDstReg)
                            {
                                prevMovInstr = line;
                                prevMovLineNum = lineNum;
                                prevWasMov = true;
                            }
                        }
                    }
                    else if (line.Contains(secondInstrToFind) && prevWasMov)
                    {

                        var match = secondInstrRegex.Match(line);
                        if (match.Success)
                        {
                            string currentSrcReg = match.Groups[1].Value;
                            string currentDstReg = match.Groups[3].Value;

                            // Make sure current source and dst are not same.
                            if (prevSrcReg == currentSrcReg && prevDstReg == currentDstReg && prevMovLineNum + 1 == lineNum)
                            {
                                strBuilder.AppendLine($"[{localLineNum - 1:0000}]{prevMovInstr}");
                                strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                                strBuilder.AppendLine("...................................................");
                                totalPairs++;
                                foundLdrStrPair = true;
                            }
                        }
                        prevWasMov = false;
                    }
                    lineNum++;
                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {hasStrWzrPair} methods containing {totalPairs} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        /// Summarizes "ldr wzr
        /// </summary>
        static void FindLdrStrGroups(Regex ldrRegEx, string fileName, bool isLdr = true)
        {
            string instrToFind = isLdr ? "ldr     x" : "str     x";
            bool foundStrWzr = false;
            int total = 0;
            int hasStrWzrPair = 0;
            int totalMovGroups = 0;
            string header = "";

            string prevDstReg = "";
            string prevSrcReg = "";
            int prevMovLineNum = 0;
            int localLineNum = 0;
            string prevMovInstr = string.Empty;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                int lineNum = 1;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundStrWzr)
                        {
                            hasStrWzrPair++;
                            //header = $"[{headerLine,-10}][{movGroup,2} groups]  {header}";
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundStrWzr = false;

                        //header = line.Replace("; Assembly listing for method ", "");
                        header = line;
                        localLineNum = 1;
                        total++;
                    }
                    if (line.Contains(instrToFind))
                    {
                        var match = ldrRegEx.Match(line);
                        if (match.Success)
                        {
                            string currentDstReg = match.Groups[1].Value;
                            string currentSrcReg = match.Groups[2].Value;

                            // Make sure current source and dst are not same.
                            if (currentSrcReg != currentDstReg)
                            {
                                // there was a previous mov and  prev source and current source reg are same
                                if (prevMovLineNum + 1 == lineNum && prevSrcReg == currentSrcReg)
                                {
                                    strBuilder.AppendLine($"[{localLineNum - 1:0000}]{prevMovInstr}");
                                    strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                                    strBuilder.AppendLine("...................................................");
                                    totalMovGroups++;
                                    foundStrWzr = true;
                                }
                                prevMovInstr = line;
                                prevMovLineNum = lineNum;
                                prevDstReg = currentDstReg;
                                prevSrcReg = currentSrcReg;
                            }

                        }
                    }
                    lineNum++;
                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {hasStrWzrPair} methods containing {totalMovGroups} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        ///       ldr x0, [fp, #24]
        ///       ldr x11, [fp, #24]
        ///       ; becomes
        ///       mov x11, x0
        /// </summary>
        /// <param name="fileName"></param>
        static void FindLdrLdrToMovGroups(string fileName)
        {
            Regex ldrRegEx = new Regex("ldr     (\\w\\d+), \\[(.*?)\\]");
            string instrToFind = "ldr     ";
            bool foundStrWzr = false;
            int total = 0;
            int hasStrWzrPair = 0;
            int totalMovGroups = 0;
            string header = "";

            string prevDstReg = "";
            string prevSrcReg = "";
            int prevMovLineNum = 0;
            int localLineNum = 0;
            string prevMovInstr = string.Empty;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                int lineNum = 1;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundStrWzr)
                        {
                            hasStrWzrPair++;
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundStrWzr = false;

                        //header = line.Replace("; Assembly listing for method ", "");
                        header = line;
                        localLineNum = 1;
                        total++;
                    }
                    if (line.Contains(instrToFind))
                    {
                        var match = ldrRegEx.Match(line);
                        if (match.Success)
                        {
                            string currentDstReg = match.Groups[1].Value;
                            string currentSrcReg = match.Groups[2].Value;

                            // Make sure current dst is different than previous dst.
                            if (!currentSrcReg.Contains(prevDstReg))
                            {
                                // 1. there was a previous ldr.
                                // 2. previous dst doesn't participate in current src.
                                // 3. previous src and current src are same.
                                if (prevMovLineNum + 1 == lineNum && currentDstReg != prevDstReg && prevSrcReg == currentSrcReg)
                                {
                                    strBuilder.AppendLine($"[{localLineNum - 1:0000}]{prevMovInstr}");
                                    strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                                    strBuilder.AppendLine("...................................................");
                                    totalMovGroups++;
                                    foundStrWzr = true;
                                }
                            }
                            prevMovInstr = line;
                            prevMovLineNum = lineNum;
                            prevDstReg = currentDstReg;
                            prevSrcReg = currentSrcReg;
                        }
                    }
                    lineNum++;
                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {hasStrWzrPair} methods containing {totalMovGroups} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        ///       ldr x0, [x2]
        ///       add x2, x2, #4
        ///       ; becomes
        ///       ldr x0, [x2], #4
        /// </summary>
        /// <param name="fileName"></param>
        static void FindPostIndexAddrMode1(string fileName)
        {
            Regex ldrRegEx = new Regex("ldr     (\\w\\d+), \\[(.*?)\\]");
            Regex addRegEx = new Regex("add     (\\w\\d+), (\\w\\d+), #(\\d+)");
            string ldrToFind = "ldr     ";
            string addToFind = "add     ";
            bool foundStrWzr = false;
            int total = 0;
            int hasStrWzrPair = 0;
            int totalMovGroups = 0;
            string header = "";

            bool prevWasLdr = false;
            string prevDstReg = "";
            string prevSrcReg = "";
            int prevMovLineNum = 0;
            int localLineNum = 0;
            string prevMovInstr = string.Empty;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                int lineNum = 1;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundStrWzr)
                        {
                            hasStrWzrPair++;
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                            //Console.WriteLine("####################################################################################");
                            //Console.WriteLine(header);
                            //Console.WriteLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundStrWzr = false;

                        //header = line.Replace("; Assembly listing for method ", "");
                        header = line;
                        localLineNum = 1;
                        total++;
                    }

                    if (line.Contains(ldrToFind))
                    {
                        var match = ldrRegEx.Match(line);
                        if (match.Success)
                        {
                            prevDstReg = match.Groups[1].Value;
                            prevSrcReg = match.Groups[2].Value;

                            // Make sure we are not loading memory contents in same register
                            if (prevDstReg != prevSrcReg)
                            {
                                prevMovInstr = line;
                                prevMovLineNum = lineNum;

                                prevWasLdr = true;
                            }
                        }
                    }
                    else if (line.Contains(addToFind))
                    {
                        if (prevWasLdr)
                        {
                            var match = addRegEx.Match(line);
                            if (match.Success && match.Groups.Count == 4) // make sure we got the immediate as well
                            {
                                string currDstReg = match.Groups[1].Value;
                                string currOper1 = match.Groups[2].Value;

                                // 1. add x2, x2, #4 <== dst == oper1
                                // 2. prev source == current dst
                                if (currDstReg == currOper1 && prevSrcReg == currDstReg && prevMovLineNum + 1 == lineNum)
                                {
                                    strBuilder.AppendLine($"[{localLineNum - 1:0000}]{prevMovInstr}");
                                    strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                                    strBuilder.AppendLine("...................................................");
                                    totalMovGroups++;
                                    foundStrWzr = true;
                                }
                            }
                            prevWasLdr = false;
                        }
                    }
                    else
                    {
                        prevWasLdr = false;
                    }
                    lineNum++;
                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {hasStrWzrPair} methods containing {totalMovGroups} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        ///       ldr x0, [x2, #4]
        ///       add x2, x2, #4
        ///       ; becomes
        ///       ldr x0, [x2, #4]!
        /// </summary>
        /// <param name="fileName"></param>
        static void FindPreIndexAddrMode1(string fileName)
        {
            Regex ldrRegEx = new Regex("ldr     ((w|x)\\d+), \\[((x|w)\\d+),#([0-9a-fx]*)\\]");
            Regex addRegEx = new Regex("add     ((w|x)\\d+), ((w|x)\\d+), #([0-9a-fx]*)");
            string ldrToFind = "ldr     ";
            string addToFind = "add     ";
            bool foundStrWzr = false;
            int total = 0;
            int hasStrWzrPair = 0;
            int totalMovGroups = 0;
            string header = "";

            bool prevWasLdr = false;
            string prevDstReg = "";
            string prevSrcReg = "";
            string prevImm = "";
            int prevMovLineNum = 0;
            int localLineNum = 0;
            string prevMovInstr = string.Empty;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                int lineNum = 1;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundStrWzr)
                        {
                            hasStrWzrPair++;
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                            //Console.WriteLine("####################################################################################");
                            //Console.WriteLine(header);
                            //Console.WriteLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundStrWzr = false;

                        //header = line.Replace("; Assembly listing for method ", "");
                        header = line;
                        localLineNum = 1;
                        total++;
                    }

                    if (line.Contains(ldrToFind))
                    {
                        var match = ldrRegEx.Match(line);
                        if (match.Success)
                        {
                            prevDstReg = match.Groups[1].Value;
                            prevSrcReg = match.Groups[3].Value;
                            prevImm = match.Groups[5].Value;

                            // Make sure we are not loading memory contents in same register
                            if (prevDstReg != prevSrcReg)
                            {
                                prevMovInstr = line;
                                prevMovLineNum = lineNum;

                                prevWasLdr = true;
                            }
                        }
                    }
                    else if (line.Contains(addToFind))
                    {
                        if (prevWasLdr)
                        {
                            var match = addRegEx.Match(line);
                            if (match.Success && match.Groups.Count == 6) // make sure we got the immediate as well
                            {
                                string currDstReg = match.Groups[1].Value;
                                string currOper1 = match.Groups[3].Value;
                                string currImm = match.Groups[5].Value;

                                // 1. add x2, x2, #4 <== dst == oper1
                                // 2. prev source == current dst
                                if (currDstReg == currOper1 && prevSrcReg == currDstReg && prevImm == currImm && prevMovLineNum + 1 == lineNum)
                                {
                                    strBuilder.AppendLine($"[{localLineNum - 1:0000}]{prevMovInstr}");
                                    strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                                    strBuilder.AppendLine("...................................................");
                                    totalMovGroups++;
                                    foundStrWzr = true;
                                }
                            }
                            prevWasLdr = false;
                        }
                    }
                    else
                    {
                        prevWasLdr = false;
                    }
                    lineNum++;
                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {hasStrWzrPair} methods containing {totalMovGroups} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        ///     add x2, x2, #4
        ///     ldr x0, [x2, #4]
        ///       ; becomes
        ///     ldr x0, [x2, #4]!
        /// </summary>
        /// <param name="fileName"></param>
        static void FindPreIndexAddrMode2(string fileName)
        {
            Regex ldrRegEx = new Regex("ldr     ((w|x)\\d+), \\[((x|w)\\d+),#([0-9a-fx]*)\\]");
            Regex addRegEx = new Regex("add     ((w|x)\\d+), ((w|x)\\d+), #([0-9a-fx]*)");
            string ldrToFind = "ldr     ";
            string addToFind = "add     ";
            bool foundStrWzr = false;
            int total = 0;
            int hasStrWzrPair = 0;
            int totalMovGroups = 0;
            string header = "";

            bool prevWasAdd = false;
            string prevDestReg = "";
            string prevSrcReg = "";
            string prevImm = "";
            int prevAddLineNum = 0;
            int localLineNum = 0;
            string prevAddInstr = string.Empty;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                int lineNum = 1;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundStrWzr)
                        {
                            hasStrWzrPair++;
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                            //Console.WriteLine("####################################################################################");
                            //Console.WriteLine(header);
                            //Console.WriteLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundStrWzr = false;

                        //header = line.Replace("; Assembly listing for method ", "");
                        header = line;
                        localLineNum = 1;
                        total++;
                    }


                    if (line.Contains(addToFind))
                    {
                        var match = addRegEx.Match(line);
                        if (match.Success && match.Groups.Count == 6) // make sure we got the immediate as well
                        {
                            prevDestReg = match.Groups[1].Value;
                            prevSrcReg = match.Groups[3].Value;
                            prevImm = match.Groups[5].Value;

                            if (prevDestReg == prevSrcReg)
                            {
                                prevAddInstr = line;
                                prevAddLineNum = lineNum;
                                prevWasAdd = true;
                            }

                            
                        }
                    }
                    else if (line.Contains(ldrToFind))
                    {
                        if (prevWasAdd)
                        {
                            var match = ldrRegEx.Match(line);
                            if (match.Success)
                            {
                                string currDstReg = match.Groups[1].Value;
                                string currSrcReg = match.Groups[3].Value;
                                string currImm = match.Groups[5].Value;

                                // 1. curr src != curr dst
                                // 2. prev src == curr src
                                // 3. prev imm = curr imm
                                if (currDstReg != currSrcReg && currSrcReg == prevSrcReg && currImm == prevImm && prevAddLineNum + 1 == lineNum)
                                {
                                    strBuilder.AppendLine($"[{localLineNum - 1:0000}]{prevAddInstr}");
                                    strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                                    strBuilder.AppendLine("...................................................");
                                    totalMovGroups++;
                                    foundStrWzr = true;
                                }
                            }
                            prevWasAdd = false;
                        }
                    }
                    else
                    {
                        prevWasAdd = false;
                    }
                    lineNum++;
                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {hasStrWzrPair} methods containing {totalMovGroups} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        ///       mov x0, x0
        /// </summary>
        /// <param name="fileName"></param>
        static void RedundantMovs1(string fileName)
        {
            Regex movRegex = new Regex("mov     ((x|w)\\d+), ((x|w)\\d+)");
            string movToFind = "mov     ";
            bool foundRedundantMov = false;
            int total = 0;
            int hasRedundantMov = 0;
            int totalMovGroups = 0;
            string header = "";

            string src = "";
            string dst = "";
            int localLineNum = 0;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundRedundantMov)
                        {
                            hasRedundantMov++;
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                            //Console.WriteLine("####################################################################################");
                            //Console.WriteLine(header);
                            //Console.WriteLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundRedundantMov = false;

                        //header = line.Replace("; Assembly listing for method ", "");
                        header = line;
                        localLineNum = 1;
                        total++;
                    }

                    if (line.Contains(movToFind))
                    {
                        var match = movRegex.Match(line);
                        if (match.Success)
                        {
                            dst = match.Groups[1].Value;
                            src = match.Groups[3].Value;

                            // Make sure we are not loading memory contents in same register
                            if (dst == src)
                            {
                                foundRedundantMov = true;
                                totalMovGroups++;
                                strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                            }
                        }
                    }

                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {hasRedundantMov} methods containing {totalMovGroups} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        /// mov x0, x20
        /// mov x20, x0
        /// </summary>
        static void RedundantMovs2(string fileName)
        {
            Regex movRegex = new Regex("mov     ((x|w)\\d+), ((x|w)\\d+)");
            string instrToFind = "mov     ";
            bool foundStrWzr = false;
            int total = 0;
            int hasStrWzrPair = 0;
            int totalMovGroups = 0;
            string header = "";

            string prevDstReg = "";
            string prevSrcReg = "";
            int prevMovLineNum = 0;
            int localLineNum = 0;
            string prevMovInstr = string.Empty;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                int lineNum = 1;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundStrWzr)
                        {
                            hasStrWzrPair++;
                            //header = $"[{headerLine,-10}][{movGroup,2} groups]  {header}";
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundStrWzr = false;

                        //header = line.Replace("; Assembly listing for method ", "");
                        header = line;
                        localLineNum = 1;
                        total++;
                    }
                    if (line.Contains(instrToFind))
                    {
                        var match = movRegex.Match(line);
                        if (match.Success)
                        {
                            string currentDstReg = match.Groups[1].Value;
                            string currentSrcReg = match.Groups[3].Value;

                            // Make sure current source and dst are not same.
                            if (currentSrcReg != currentDstReg)
                            {
                                // there was a previous mov and  prev source and current source reg are same
                                if (prevMovLineNum + 1 == lineNum && prevSrcReg == currentDstReg && prevDstReg == currentSrcReg)
                                {
                                    strBuilder.AppendLine($"[{localLineNum - 1:0000}]{prevMovInstr}");
                                    strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                                    strBuilder.AppendLine("...................................................");
                                    totalMovGroups++;
                                    foundStrWzr = true;
                                }
                                prevMovInstr = line;
                                prevMovLineNum = lineNum;
                                prevDstReg = currentDstReg;
                                prevSrcReg = currentSrcReg;
                            }

                        }
                    }
                    lineNum++;
                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {hasStrWzrPair} methods containing {totalMovGroups} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        ///       ldr w0, [x0] // <-- this should zero extend the register so next mov is not needed.
        ///       mov w0, w0
        /// </summary>
        /// <param name="fileName"></param>
        static void RedundantMovs3(string fileName)
        {
            Regex movRegex = new Regex("mov     ((x|w)\\d+), ((x|w)\\d+)");
            Regex ldrRegex = new Regex("ldr     (w\\d+), ");
            string movToFind = "mov     ";
            bool foundRedundantMov = false;
            int total = 0;
            int hasRedundantMov = 0;
            int totalMovGroups = 0;
            string header = "";

            string src = "";
            string dst = "";
            string prevLdrInstr = "";
            int prevLineNum = 0;
            int localLineNum = 0;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundRedundantMov)
                        {
                            hasRedundantMov++;
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                            //Console.WriteLine("####################################################################################");
                            //Console.WriteLine(header);
                            //Console.WriteLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundRedundantMov = false;

                        //header = line.Replace("; Assembly listing for method ", "");
                        header = line;
                        localLineNum = 1;
                        total++;
                    }

                    if (line.Contains(movToFind))
                    {
                        var match = movRegex.Match(line);
                        if (match.Success)
                        {
                            var ldrMatch = ldrRegex.Match(prevLdrInstr);
                            if (ldrMatch.Success)
                            {
                                string prevDest = ldrMatch.Groups[1].Value;
                                dst = match.Groups[1].Value;
                                src = match.Groups[3].Value;

                                // Make sure we are not loading memory contents in same register
                                if (dst == src && prevDest == dst && prevLineNum + 1 == localLineNum)
                                {
                                    foundRedundantMov = true;
                                    totalMovGroups++;
                                    strBuilder.AppendLine($"[{prevLineNum:0000}]{prevLdrInstr}");
                                    strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                                }
                            }
                            
                        }
                    }
                    prevLineNum = localLineNum;
                    prevLdrInstr = line;
                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {hasRedundantMov} methods containing {totalMovGroups} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        ///       Finds groups of adrp/add instructions
        /// </summary>
        /// <param name="fileName"></param>
        static void AdrpAddPairs(string fileName)
        {
            Regex adrp = new Regex("adrp    (x\\d+), \\[RELOC #(0x[0-9a-f]+)\\]");
            Regex add = new Regex("add     (x\\d+), (x\\d+), #0");
            Regex ldr = new Regex("ldr     (x\\d+), \\[(x\\d+)\\]");

            bool foundInstrGroup = false;
            int total = 0;
            int hasAdrpAddGroup = 0;
            int totalAdrpAddGroups = 0;
            string header = "";

            int localLineNum = 0;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundInstrGroup)
                        {
                            hasAdrpAddGroup++;
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                            //Console.WriteLine("####################################################################################");
                            //Console.WriteLine(header);
                            //Console.WriteLine(strBuilder.ToString());
                        }

                        foundInstrGroup = false;
                        strBuilder.Clear();

                        header = line;
                        localLineNum = 1;
                        total++;
                    }

                    while (line.Contains("adrp    x11,"))
                    {
                        StringBuilder localBuilder = new StringBuilder();

                        // adrp x11, 
                        var match = adrp.Match(line);
                        if (!match.Success) break;

                        localBuilder.AppendLine($"[{localLineNum++:0000}]{line}");
                        string relocAddr = match.Groups[2].Value;

                        // add x11, x11
                        line = sr.ReadLine();
                        match = add.Match(line);
                        if (!match.Success) continue; // continue to see if this was "adrp x11"
                        localBuilder.AppendLine($"[{localLineNum++:0000}]{line}");

                        // adrp x...
                        line = sr.ReadLine();
                        match = adrp.Match(line);
                        if (!match.Success) continue; // continue to see if this was "adrp x11"

                        string reg = match.Groups[1].Value;
                        string relocAddrForReg = match.Groups[2].Value;
                        if (relocAddr != relocAddrForReg) continue; // continue to see if this was "adrp x11"
                        localBuilder.AppendLine($"[{localLineNum++:0000}]{line}");

                        // add x...
                        line = sr.ReadLine();
                        match = add.Match(line);
                        if (!match.Success) continue; // continue to see if this was "adrp x11"

                        string addDst = match.Groups[1].Value;
                        string addOper1 = match.Groups[2].Value;

                        if (addDst != reg || addOper1 != reg) continue; // continue to see if this was "adrp x11"
                        localBuilder.AppendLine($"[{localLineNum++:0000}]{line}");

                        // ldr x...
                        line = sr.ReadLine();
                        match = ldr.Match(line);
                        if (!match.Success) continue; // continue to see if this was "adrp x11"

                        string ldrDst = match.Groups[1].Value;
                        string ldrSrc = match.Groups[2].Value;
                        if (ldrDst != reg || ldrSrc != reg) continue; // continue to see if this was "adrp x11"

                        localBuilder.AppendLine($"[{localLineNum:0000}]{line}");

                        // Note: Not considering final "ldr" instruction because there can be an instruction to setup argument 
                        // like storing into w0/w1
                        localBuilder.AppendLine("....................................................................................");
                        foundInstrGroup = true;
                        totalAdrpAddGroups++;
                        strBuilder.Append(localBuilder.ToString());
                        break;
                    }
                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {hasAdrpAddGroup} methods containing {totalAdrpAddGroups} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        ///       Finds groups of sxtw, lsl, add for array element access
        /// </summary>
        /// <param name="fileName"></param>
        static void ArrayAccess(string fileName)
        {
            string instrToFind = "sxtw    ";
            Regex sxtwRegex = new Regex("sxtw    ((x|w)\\d+),");

            string[] instrsToFind = new string[]
            {
                "sxtw    ",
                "lsl     ",
                "add     ",
            };
            bool foundInstrGroup = false;
            int total = 0;
            int hasAdrpAddGroup = 0;
            int totalArrayAccessElems = 0;
            string header = "";

            int localLineNum = 0;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                int lineNum = 1;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundInstrGroup)
                        {
                            hasAdrpAddGroup++;
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                            //Console.WriteLine("####################################################################################");
                            //Console.WriteLine(header);
                            //Console.WriteLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundInstrGroup = false;

                        header = line;
                        localLineNum = 1;
                        total++;
                        if (total % 500 == 0)
                        {
                            Console.WriteLine($"Processed {total} methods.");
                        }
                    }

                    while (line.Contains(instrToFind))
                    {
                        var localBuilder = new StringBuilder();
                        var match = sxtwRegex.Match(line);
                        if (!match.Success) break;

                        localBuilder.AppendLine($"[{localLineNum++:0000}]{line}");
                        string reg = match.Groups[1].Value;

                        // lsl
                        line = sr.ReadLine();
                        if (!line.Contains($"lsl     {reg}")) continue;  // continue to see if this was "sxtw"
                        localBuilder.AppendLine($"[{localLineNum++:0000}]{line}");

                        // add
                        line = sr.ReadLine();
                        if (!line.Contains($"add     {reg}, {reg}, #")) continue;  // continue to see if this was "sxtw"
                        localBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                        localBuilder.AppendLine("....................................................................................");

                        // Note: Not considering final instruction where "reg" is used because it might not be the next instruction after
                        // the sequence.
                        foundInstrGroup = true;
                        totalArrayAccessElems++;
                        strBuilder.Append(localBuilder.ToString());
                        break;
                    }
                    lineNum++;
                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {hasAdrpAddGroup} methods containing {totalArrayAccessElems} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        ///       Finds prologs/epilogs in x64
        /// </summary>
        /// <param name="fileName"></param>
        static void PrologEpilogInx64(string fileName)
        {
            bool foundProlog = false;
            int total = 0;
            long hasPrologEpilog = 0;
            long hasNoPrologEpilog = 0;
            string header = "";

            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(x64File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundProlog)
                        {
                            hasPrologEpilog++;
                        }
                        else
                        {
                            hasNoPrologEpilog++;
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                            //Console.WriteLine("####################################################################################");
                            //Console.WriteLine(header);
                            //Console.WriteLine(resultBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundProlog = false;

                        header = line;
                        total++;
                    }

                    if (line.Contains("sub      rsp,"))
                    {
                        if (foundProlog)
                        {
                            Console.WriteLine($"Already: {header}");
                        }
                        foundProlog = true;
                    }
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. {hasPrologEpilog} methods have prolog/epilog. {hasNoPrologEpilog} don't have.");
            Console.WriteLine($"Processed {total} methods. {hasPrologEpilog} methods have prolog/epilog. {hasNoPrologEpilog} don't have.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        /// <summary>
        ///       Finds ldr x1, [x1, x2, LSL #2] patterns
        /// </summary>
        /// <param name="fileName"></param>
        static void BasePlusRegisterOffset(string fileName)
        {
            string instrToFind = "ldr     ";
            Regex ldrRegEx = new Regex("ldr     ((x|w)\\d+), \\[(x|w)\\d+, (x|w)\\d+, LSL #\\d+\\]");
            int total = 0;
            long ldrWithRegOffsetCount = 0;
            string header = "";
            bool foundLdrWithRegOffset = false;
            int localLineNum = 0;
            StringBuilder resultBuilder = new StringBuilder();

            using (FileStream fs = File.Open(armFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                StringBuilder strBuilder = new StringBuilder();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("; Assembly listing for method"))
                    {
                        if (foundLdrWithRegOffset)
                        {
                            ldrWithRegOffsetCount++;
                            resultBuilder.AppendLine("####################################################################################");
                            resultBuilder.AppendLine(header);
                            resultBuilder.AppendLine(strBuilder.ToString());
                            //Console.WriteLine("####################################################################################");
                            //Console.WriteLine(header);
                            //Console.WriteLine(strBuilder.ToString());
                        }

                        strBuilder.Clear();
                        foundLdrWithRegOffset = false;

                        header = line;
                        localLineNum = 1;
                        total++;
                    }

                    if (line.Contains(instrToFind))
                    {
                        if (ldrRegEx.IsMatch(line))
                        {
                            strBuilder.AppendLine($"[{localLineNum:0000}]{line}");
                            foundLdrWithRegOffset = true;
                        }
                    }
                    localLineNum++;
                }
            }
            resultBuilder.AppendLine($"Processed {total} methods. Found {ldrWithRegOffsetCount} groups.");
            Console.WriteLine($"Processed {total} methods. Found {ldrWithRegOffsetCount} groups.");
            WriteResults(fileName, resultBuilder.ToString());
        }

        private static void WriteResults(string fileName, string contents)
        {
            // So we don't accidently write to input files
            if (fileName == armFile || fileName == x64File)
            {
                throw new Exception("Can't write to input files.");
            }
            File.WriteAllText(fileName, contents);
        }
    }
}
