// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;

// Drive PMI through jitting all the methods in an assembly.
//
// Run the jitting in a subprocess so that if there are jit assertions
// then the parent can recover and continue on with the next method.

namespace PMIDriver
{
    class PMIDriver
    {
        const string PMI_FILE_MARKER = "NextMethodToPrep.marker";
        const string PREVIOUS_PMI_FILE_MARKER = "PreviousNextMethodToPrep.marker";

        const int PREPALL_TIMEOUT = 1800000; //30 minutes
        const int PREPALL_MAX_RETRY_COUNT = 5;

        public static int Drive(string assemblyName)
        {
            if (File.Exists(PMI_FILE_MARKER))
            {
                File.Delete(PMI_FILE_MARKER);
            }
            if (File.Exists(PREVIOUS_PMI_FILE_MARKER))
            {
                File.Delete(PREVIOUS_PMI_FILE_MARKER);
            }

            PrepAllInfo pi = new PrepAllInfo();
            pi.assemblyName = assemblyName;
            pi.methodToPrep = 0;

            bool isFileCompleted = false;
            int prepallRetryCount = 0;

            List<string> errors = new List<string>();
            while (!isFileCompleted && prepallRetryCount <= PREPALL_MAX_RETRY_COUNT)
            {
                prepallRetryCount++;
                PrepAllResult pr = Compute(pi);
                if (pr.success)
                {
                    isFileCompleted = true;
                }
                else
                {
                    if (pr.assert)
                    {
                        errors.Add(String.Format("{0}###{1}", pr.errorMessage, pr.errorDetail));
                        try
                        {
                            int nextMethodToPrep = Int32.Parse(File.ReadAllLines(PMI_FILE_MARKER)[0]);
                            Console.WriteLine("Current Method: " + pi.methodToPrep + " nextMethodToPrep: " + nextMethodToPrep);
                            if (pi.methodToPrep == nextMethodToPrep)
                            {
                                // We failed, in the same spot as last time. So there's no point continuing.
                                isFileCompleted = true;
                            }
                            else
                            {
                                pi.methodToPrep = nextMethodToPrep + 1;
                                using (var writer = new StreamWriter(File.Create(PREVIOUS_PMI_FILE_MARKER)))
                                {
                                    writer.Write("{0}", pi.methodToPrep + 1);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                            if (errors.Count > 0)
                            {
                                File.WriteAllLines(String.Format("{0}.err",
                                    Path.GetFileNameWithoutExtension(pi.assemblyName)), errors);
                            }
                            else
                            {
                                Console.WriteLine("ERROR: No errors to write");
                            }

                            // Try the previous one again
                            if (File.Exists(PREVIOUS_PMI_FILE_MARKER))
                            {
                                pi.methodToPrep = Int32.Parse(File.ReadAllLines(PREVIOUS_PMI_FILE_MARKER)[0]);
                            }
                            else
                            {
                                // There is some problem with this file. Abondon this file.
                                isFileCompleted = true;
                                File.AppendAllText(String.Format("{0}.err",
                                    Path.GetFileNameWithoutExtension(pi.assemblyName)),
                                    String.Format("PROBLEM with file - {0} cant be found", PREVIOUS_PMI_FILE_MARKER));
                            }

                            if (!File.Exists(PMI_FILE_MARKER))
                            {
                                // There is some problem with this file. Abondon this file.
                                isFileCompleted = true;
                                File.AppendAllText(String.Format("{0}.err",
                                    Path.GetFileNameWithoutExtension(pi.assemblyName)),
                                    String.Format("PROBLEM with file - {0} cant be found", PMI_FILE_MARKER));
                            }
                        }
                    }
                    else
                    {
                        errors.Add(pr.errorMessage);
                        isFileCompleted = true;
                    }
                }
            }

            // Clean up our temporary files
            try
            {
                if (File.Exists(PMI_FILE_MARKER))
                {
                    File.Delete(PMI_FILE_MARKER);
                }
                if (File.Exists(PREVIOUS_PMI_FILE_MARKER))
                {
                    File.Delete(PREVIOUS_PMI_FILE_MARKER);
                }
            }
            catch (Exception)
            {
                // Ignore failures
            }

            if (errors.Count > 0)
            {
                File.WriteAllLines(String.Format("{0}.err", Path.GetFileNameWithoutExtension(pi.assemblyName)), errors);
                return 200; // error return
            }
            else
            {
                return 0; // success return
            }
        }

        private static PrepAllResult Compute(PrepAllInfo pi)
        {
            PrepAllResult temp = new PrepAllResult();
            temp.success = false;
            temp.assert = false;

            string szOutput = null;
            string szError = null;

            try
            {
                Process p = new Process();
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                // Fetch our command line. Split off the arguments.
#if NETCOREAPP2_1
                // For .Net Core the PMI assembly is an argument to dotnet.
                string newCommandLine = Environment.CommandLine.Replace("DRIVEALL", "PREPALL");
#else
                // For .Net Framework the OS knows how to bootstrap .Net when
                // passed the PMI assembly as an executable.
                string newCommandLine = "PREPALL \"" + pi.assemblyName + "\"";
#endif
                newCommandLine += " " + pi.methodToPrep;

                Process thisProcess = Process.GetCurrentProcess();
                string driverName = thisProcess.MainModule.FileName;

                p.StartInfo.FileName = driverName;
                p.StartInfo.Arguments = newCommandLine;

                p.Start();
                szOutput = p.StandardOutput.ReadToEnd();
                szError = p.StandardError.ReadToEnd();

                if (!p.WaitForExit(PREPALL_TIMEOUT))
                {
                    try
                    {
                        KillProcessAndChildren(p.Id);
                    }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                }

                szOutput = szOutput + szError;
                Console.WriteLine(szOutput);
                int idx = szOutput.IndexOf("Completed assembly ");
                if (idx == -1)
                {
                    //File.WriteAllText(pi.assemblyName + ".prepall", szOutput);
                    idx = szOutput.IndexOf("]): Assertion failed '");
                    if (idx != -1)
                    {
                        temp.assert = true;
                        //Assert failure(PID 440 [0x000001b8], Thread: 3220 [0xc94]): Assertion failed 'genActualType(tree->gtType) == TYP_INT || genActualType(tree->gtType) == TYP_I_IMPL || genActualType(tree->gtType) == TYP_REF || tree->gtType == TYP_BYREF' in 'ELP.Program:Main(ref):int'
                        //File: c:\dd\arm_1\src\ndp\clr\src\jit\il\codegen.cpp, Line: 945 Image:
                        int idx2 = szOutput.IndexOf("Image:", idx);
                        string szTemp = szOutput.Substring(idx, idx2 - idx);
                        //]): Assertion failed 'genActualType(tree->gtType) == TYP_INT || genActualType(tree->gtType) == TYP_I_IMPL || genActualType(tree->gtType) == TYP_REF || tree->gtType == TYP_BYREF' in 'ELP.Program:Main(ref):int'
                        //File: c:\dd\arm_1\src\ndp\clr\src\jit\il\codegen.cpp, Line: 945 Image:
                        string[] szTemp2 = szTemp.Split('\'');
                        temp.errorMessage = szTemp2[1];
                        temp.errorDetail = szTemp2[3];
                        temp.assemblyName = pi.assemblyName;
                    }
                    else
                    {
                        idx = szOutput.IndexOf("Assert failure(PID ");
                        if (idx != -1)
                        {
                            temp.assert = true;

                            //Assert failure(PID 12020 [0x00002ef4], Thread: 13276 [0x33dc]): (entry == NULL) || (entry == GetSlot(numGenericArgs,i)) || IsCompilationProcess()
                            //CLR! Dictionary::PrepopulateDictionary + 0x135 (0x603435b8)
                            //CLR! MethodCallGraphPreparer::PrepareMethods + 0x24B (0x602e7e45)
                            //CLR! MethodCallGraphPreparer::Run + 0x45E (0x602e7308)
                            //CLR! PrepareMethodDesc + 0x11B (0x602e9559)
                            //CLR! ReflectionInvocation::PrepareMethod + 0x596 (0x60494ac0)
                            //MSCORLIB.NI! <no symbol> + 0x0 (0x5e4aff9d)
                            //<no module>! <no symbol> + 0x0 (0x005821b7)
                            //CLR! CallDescrWorkerInternal + 0x34 (0x5fddcb2d)
                            //CLR! CallDescrWorker + 0xD5 (0x600da980)
                            //CLR! CallDescrWorkerWithHandler + 0x1B9 (0x600daba1)
                            //    File: c:\clr2\src\ndp\clr\src\vm\genericdict.cpp, Line: 933 Image:
                            //c:\pmi\pmi.exe

                            idx = szOutput.IndexOf("]): ", idx);
                            int idx2 = szOutput.IndexOf("\r\n", idx);
                            string szTemp = szOutput.Substring(idx + 4, idx2 - idx - 4);
                            //(entry == NULL) || (entry == GetSlot(numGenericArgs,i)) || IsCompilationProcess()
                            temp.errorMessage = szTemp;
                            //temp.errorDetail = szTemp2[3];
                            temp.assemblyName = pi.assemblyName;
                        }
                        else
                        {
                            Console.WriteLine("Failed PREPALL on {0}", pi.assemblyName);
                            temp.errorMessage = "General error, no assert seen.";
                        }
                    }
                }
                else
                {
                    temp.success = true;
                }
            }
            catch (Exception e)
            {
                temp.errorMessage = e.ToString();
            }
            return temp;
        }

        /// <summary>
        /// Kill a process, and all of its children.
        /// </summary>
        /// <param name="pid">Process ID.</param>
        private static void KillProcessAndChildren(int pid)
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("Select * From Win32_Process Where ParentProcessID=" + pid);
                ManagementObjectCollection moc = searcher.Get();
                foreach (ManagementObject mo in moc)
                {
                    if (Convert.ToInt32(mo["ProcessID"]) != pid)
                        KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
                }
                Process proc = Process.GetProcessById(pid);
                proc.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
        }
    }

    struct PrepAllResult
    {
        public bool success;
        public bool assert;
        public string errorMessage;
        public string errorDetail;
        public string assemblyName;

        public override string ToString()
        {
            return String.Format("{0} {1} {2} {3}", success, assert, errorMessage, errorDetail);
        }
    }

    struct PrepAllInfo
    {
        public string assemblyName;
        public int methodToPrep;

        public override string ToString()
        {
            return String.Format("{0}", assemblyName);
        }
    }

}
