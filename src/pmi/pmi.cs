// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

struct MicroMethodInfo
{
    public RuntimeMethodHandle RMH;
    public string name;
    public bool IsAbstract;
    public bool ContainsGenericParameters;
}

// Jit all the methods in an assembly via PrepareMethod.
class PrepareMethodinator
{

    private static TimeSpan PrepareMethod(Type t, MicroMethodInfo mMI)
    {
        TimeSpan elapsedFunc = TimeSpan.MinValue;
        try
        {
            DateTime startFunc = DateTime.Now;
            System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(mMI.RMH);
            elapsedFunc = DateTime.Now - startFunc;
        }
        catch (System.EntryPointNotFoundException)
        {
            Console.WriteLine();
            Console.WriteLine("EntryPointNotFoundException {0}::{1}", t.FullName, mMI.name);
        }
        catch (System.BadImageFormatException)
        {
            Console.WriteLine();
            Console.WriteLine("BadImageFormatException {0}::{1}", t.FullName, mMI.name);
        }
        catch (System.MissingMethodException)
        {
            Console.WriteLine();
            Console.WriteLine("MissingMethodException {0}::{1}", t.FullName, mMI.name);
        }
        catch (System.ArgumentException e)
        {
            Console.WriteLine();
            Console.WriteLine("ArgumentException {0}::{1} {2}", t.FullName, mMI.name, e.Message.Split(new char[] { '\r', '\n' })[0]);
        }
        catch (System.IO.FileNotFoundException eFileNotFound)
        {
            Console.WriteLine();
            Console.WriteLine("FileNotFoundException {0}::{1} - {2} ({3})", t.FullName, mMI.name, eFileNotFound.FileName, eFileNotFound.Message);
        }
        catch (System.DllNotFoundException eDllNotFound)
        {
            Console.WriteLine();
            Console.WriteLine("DllNotFoundException {0}::{1} ({2})", t.FullName, mMI.name, eDllNotFound.Message);
        }
        catch (System.TypeInitializationException eTypeInitialization)
        {
            Console.WriteLine();
            Console.WriteLine("TypeInitializationException {0}::{1} - {2} ({3})", t.FullName, mMI.name, eTypeInitialization.TypeName, eTypeInitialization.Message);
        }
        catch (System.Runtime.InteropServices.MarshalDirectiveException)
        {
            Console.WriteLine();
            Console.WriteLine("MarshalDirectiveException {0}::{1}", t.FullName, mMI.name);
        }
        catch (System.TypeLoadException)
        {
            Console.WriteLine();
            Console.WriteLine("TypeLoadException {0}::{1}", t.FullName, mMI.name);
        }
        catch (System.OverflowException)
        {
            Console.WriteLine();
            Console.WriteLine("OverflowException {0}::{1}", t.FullName, mMI.name);
        }
        catch (System.InvalidProgramException)
        {
            Console.WriteLine();
            Console.WriteLine("InvalidProgramException {0}::{1}", t.FullName, mMI.name);
        }
        catch (Exception e)
        {
            Console.WriteLine();
            Console.WriteLine("Unknown exception");
            Console.WriteLine(e);
        }
        return elapsedFunc;
    }

    private static BindingFlags BindingFlagsForCollectingAllMethodsOrCtors = (
        BindingFlags.DeclaredOnly |
        BindingFlags.Instance |
        BindingFlags.NonPublic |
        BindingFlags.Public |
        BindingFlags.Static
    );

    static class GlobalMethodHolder
    {
        public static MicroMethodInfo[] GlobalMethodInfoSet;

        public static void PopulateGlobalMethodInfoSet(MethodInfo[] globalMethods)
        {
            int index;
            int numberOfMethods;

            numberOfMethods = globalMethods.Length;

            GlobalMethodInfoSet = new MicroMethodInfo[numberOfMethods];

            for (index = 0; index < numberOfMethods; index++)
            {
                GlobalMethodInfoSet[index].RMH = globalMethods[index].MethodHandle;
                GlobalMethodInfoSet[index].name = globalMethods[index].Name;
                GlobalMethodInfoSet[index].IsAbstract = globalMethods[index].IsAbstract;
                GlobalMethodInfoSet[index].ContainsGenericParameters = globalMethods[index].ContainsGenericParameters;
            }
        }
    }

    private static int GetRMHCountOnly(Type t)
    {
        if (Object.ReferenceEquals(t, typeof(GlobalMethodHolder)))
        {
            return GlobalMethodHolder.GlobalMethodInfoSet.Length;
        }

        MethodInfo[] mi = t.GetMethods(BindingFlagsForCollectingAllMethodsOrCtors);

        ConstructorInfo[] ci = t.GetConstructors(BindingFlagsForCollectingAllMethodsOrCtors);

        return (mi.Length + ci.Length);
    }

    private static MicroMethodInfo[] GetRMH(Type t)
    {
        if (Object.ReferenceEquals(t, typeof(GlobalMethodHolder)))
        {
            return GlobalMethodHolder.GlobalMethodInfoSet;
        }

        MethodInfo[] mi = t.GetMethods(BindingFlagsForCollectingAllMethodsOrCtors);

        ConstructorInfo[] ci = t.GetConstructors(BindingFlagsForCollectingAllMethodsOrCtors);

        MicroMethodInfo[] mMI = new MicroMethodInfo[mi.Length + ci.Length];

        for (int i = 0; i < mi.Length; i++)
        {
            mMI[i].RMH = mi[i].MethodHandle;
            mMI[i].name = mi[i].Name;
            mMI[i].IsAbstract = mi[i].IsAbstract;
            mMI[i].ContainsGenericParameters = mi[i].ContainsGenericParameters;
        }

        for (int i = 0; i < ci.Length; i++)
        {
            mMI[i + mi.Length].RMH = ci[i].MethodHandle;
            mMI[i + mi.Length].name = ci[i].Name;
            mMI[i + mi.Length].IsAbstract = ci[i].IsAbstract;
            mMI[i + mi.Length].ContainsGenericParameters = ci[i].ContainsGenericParameters;
        }

        return mMI;
    }

    private static void WriteAndFlushNextMethodToPrepMarker(int methodBeingPrepped)
    {
        int nextMethodToPrep = (methodBeingPrepped + 1);

        using (var writer = new StreamWriter(File.Create("NextMethodToPrep.marker")))
        {
            writer.Write("{0}", nextMethodToPrep);
        }
    }

    private static Assembly MyResolveEventHandler(object sender, ResolveEventArgs args)
    {
        string pmiPath = Environment.GetEnvironmentVariable("PMIPATH");
        if (pmiPath == null)
        {
            return null;
        }

        string[] pmiPaths = pmiPath.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string path in pmiPaths)
        {
            // what is the format of this?
            int idx = args.Name.IndexOf(",");
            if (idx != -1)
            {
                string tmpPath = Path.GetFullPath(Path.Combine(path, args.Name.Substring(0, idx) + ".dll"));
                if (File.Exists(tmpPath))
                {
                    // Found it!
                    try
                    {
                        return Assembly.LoadFrom(tmpPath);
                    }
                    catch (Exception)
                    {
                        // Well, that didn't work!
                    }
                }
            }
        }

        return null;
    }

    private static int Usage()
    {
        string exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName; // get the current full path name of PMI.exe
        exeName = System.IO.Path.GetFileName(exeName); // strip off the path; just use the EXE name.
        Console.WriteLine(
            "Usage:\r\n"
            + "\r\n"
            + "  " + exeName + " Count PATH_TO_ASSEMBLY\r\n"
            + "      Count the number of types and methods in an assembly.\r\n"
            + "\r\n"
            + "  " + exeName + " PrepOne PATH_TO_ASSEMBLY INDEX_OF_TARGET_METHOD\r\n"
            + "      JIT a single method, specified by a method number.\r\n"
            + "\r\n"
            + "  " + exeName + " PrepAll PATH_TO_ASSEMBLY [INDEX_OF_FIRST_METHOD_TO_PROCESS]\r\n"
            + "      JIT all the methods in an assembly. If INDEX_OF_FIRST_METHOD_TO_PROCESS is specified, it is the first\r\n"
            + "      method compiled, followed by all subsequent methods.\r\n"
            + "\r\n"
            + "  " + exeName + " DriveAll PATH_TO_ASSEMBLY\r\n"
            + "      The same as PrepAll, but is more robust. While PrepAll will stop at the first JIT assert, DriveAll will\r\n"
            + "      continue by skipping that method.\r\n"
            + "\r\n"
            + "Environment variable PMIPATH is a semicolon-separated list of paths used to find dependent assemblies."
        );

        return 101;
    }

    // Return values:
    // 0 - success
    // >= 100 - failure
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage();
        }

        string command = args[0].ToUpper();
        string assemblyName = args[1];
        int methodToPrep = -1; // For PREPONE, PREPALL. For PREPALL, this is the first method to prep.

        switch (command)
        {
            case "COUNT":
            case "DRIVEALL":
                if (args.Length < 2)
                {
                    Console.WriteLine("ERROR: too few arguments");
                    return Usage();
                }
                else if (args.Length > 2)
                {
                    Console.WriteLine("ERROR: too many arguments");
                    return Usage();
                }
                break;

            case "PREPALL":
                if (args.Length < 3)
                {
                    methodToPrep = 0;
                }
                else if (args.Length > 3)
                {
                    Console.WriteLine("ERROR: too many arguments");
                    return Usage();
                }
                break;

            case "PREPONE":
                if (args.Length < 3)
                {
                    Console.WriteLine("ERROR: too few arguments");
                    return Usage();
                }
                else if (args.Length > 3)
                {
                    Console.WriteLine("ERROR: too many arguments");
                    return Usage();
                }
                break;

            default:
                Console.WriteLine("ERROR: Unknown command {0}", command);
                return Usage();
        }

        // Parse the method number.
        if (methodToPrep == -1)
        {
            switch (command)
            {
                case "PREPALL":
                case "PREPONE":
                    try
                    {
                        methodToPrep = Convert.ToInt32(args[2]);
                    }
                    catch (System.FormatException)
                    {
                        Console.WriteLine("ERROR: illegal method number");
                        return Usage();
                    }
                    if (methodToPrep < 0)
                    {
                        Console.WriteLine("ERROR: method number must be greater than 0");
                        return Usage();
                    }
                    break;
            }
        }

        // Done parsing arguments. Start loading the assembly and its types.
        if (command == "DRIVEALL")
        {
            return PMIDriver.PMIDriver.Drive(assemblyName);
        }

        // We want to handle specifying a "load path" where assemblies can be found.
        // The environment variable PMIPATH is a semicolon-separated list of paths. If the
        // Assembly can't be found by the usual mechanisms, our Assembly ResolveEventHandler
        // will be called, and we'll probe on the PMIPATH list.
        AppDomain currentDomain = AppDomain.CurrentDomain;
        currentDomain.AssemblyResolve += new ResolveEventHandler(MyResolveEventHandler);

        Assembly aAssem = null;
        List<Type> aTypes = null;
        DateTime start = DateTime.Now;

        assemblyName = System.IO.Path.GetFullPath(assemblyName); // Convert it to an absolute path before calling LoadFrom

        try
        {
            aAssem = Assembly.LoadFrom(assemblyName);
        }
        catch (System.ArgumentException)
        {
            Console.WriteLine("Assembly load failure ({0}): ArgumentException", assemblyName);
            return 102;
        }
        catch (BadImageFormatException e)
        {
            Console.WriteLine("Assembly load failure ({0}): BadImageFormatException (is it a managed assembly?)", assemblyName);
            Console.WriteLine(e);
            return 103;
        }
        catch (System.IO.FileLoadException)
        {
            Console.WriteLine("Assembly load failure ({0}): FileLoadException", assemblyName);
            return 104;
        }
        catch (System.IO.FileNotFoundException)
        {
            Console.WriteLine("Assembly load failure ({0}): file not found", assemblyName);
            return 105;
        }
        catch (System.UnauthorizedAccessException)
        {
            Console.WriteLine("Assembly load failure ({0}): UnauthorizedAccessException", assemblyName);
            return 106;
        }

        aTypes = new List<Type>();

        var globalMethods = aAssem.ManifestModule.GetMethods(BindingFlagsForCollectingAllMethodsOrCtors);
        if (globalMethods.Length > 0)
        {
            GlobalMethodHolder.PopulateGlobalMethodInfoSet(globalMethods);
            aTypes.Add(typeof(GlobalMethodHolder));
        }

        try
        {
            aTypes.AddRange(aAssem.GetTypes());
        }
        catch (System.Reflection.ReflectionTypeLoadException e)
        {
            Console.WriteLine("tReflectionTypeLoadException {0}", assemblyName);
            Exception[] ea = e.LoaderExceptions;
            foreach (Exception e2 in ea)
            {
                string[] ts = e2.ToString().Split('\'');
                string temp = ts[1];
                ts = temp.Split(',');
                temp = ts[0];

                Console.WriteLine("tReflectionTypeLoadException {0} {1}", temp, assemblyName);
            }
            return 107;
        }
        catch (System.IO.FileLoadException)
        {
            Console.WriteLine("tFileLoadException {0}", assemblyName);
            return 108;
        }
        catch (System.IO.FileNotFoundException e)
        {
            string temp = e.ToString();
            string[] ts = temp.Split('\'');
            temp = ts[1];
            Console.WriteLine("tFileNotFoundException {1} {0}", temp, assemblyName);
            return 109;
        }

        switch (command)
        {
            case "COUNT":
                {
                    int typeCount = 0;
                    int methodCount = 0;

                    Console.WriteLine("Computing Count for {0}", assemblyName);

                    foreach (Type t in aTypes)
                    {
                        Console.WriteLine("#types: {0}, #methods: {1}, before type {2}", typeCount, methodCount, t.FullName);
                        MicroMethodInfo[] rmh = GetRMH(t);
                        methodCount += rmh.Length;
                        typeCount++;
                    }

                    TimeSpan elapsed = DateTime.Now - start;
                    Console.WriteLine("Counts {0} - #types: {1}, #methods: {2}, elapsed time: {3}, elapsed ms: {4}",
                        assemblyName, typeCount, methodCount, elapsed, elapsed.TotalMilliseconds);
                    return 0;
                }

            case "PREPALL":
                {
                    // Call PrepareMethod on all methods in the given assembly
                    int typeCount = 0;
                    int methodCount = 0;
                    string pmiFullLogFileName = String.Format("{0}.pmi", Path.GetFileNameWithoutExtension(assemblyName));
                    string pmiPartialLogFileName = String.Format("{0}.pmiPartial", Path.GetFileNameWithoutExtension(assemblyName));

                    Console.WriteLine("Prepall for {0}", assemblyName);

                    foreach (Type t in aTypes)
                    {
                        Console.WriteLine("Start type {0}", t.FullName);
                        MicroMethodInfo[] amMI = GetRMH(t);
                        DateTime startType = DateTime.Now;

                        foreach (MicroMethodInfo mMI in amMI)
                        {
                            if (methodCount >= methodToPrep)
                            {
                                WriteAndFlushNextMethodToPrepMarker(methodCount);

                                if (mMI.IsAbstract)
                                {
                                    Console.WriteLine("PREPALL type# {0} method# {1} {2}::{3} - skipping (abstract)", typeCount, methodCount, t.FullName, mMI.name);
                                }
                                else if (mMI.ContainsGenericParameters)
                                {
                                    Console.WriteLine("PREPALL type# {0} method# {1} {2}::{3} - skipping (generic parameters)", typeCount, methodCount, t.FullName, mMI.name);
                                }
                                else
                                {
                                    Console.Write("PREPALL type# {0} method# {1} {2}::{3}", typeCount, methodCount, t.FullName, mMI.name);
                                    TimeSpan elapsedFunc = PrepareMethod(t, mMI);
                                    if (elapsedFunc != TimeSpan.MinValue)
                                    {
                                        Console.WriteLine(", elapsed time: {0}, elapsed ms: {1}",
                                            elapsedFunc, elapsedFunc.TotalMilliseconds);
                                    }
                                }
                            }

                            methodCount++;
                        }

                        TimeSpan elapsedType = DateTime.Now - startType;
                        Console.WriteLine("Completed type {0}, type elapsed time: {1}, elapsed ms: {2}",
                            t.FullName, elapsedType, elapsedType.TotalMilliseconds);

                        typeCount++;
                    }

                    TimeSpan elapsed = DateTime.Now - start;
                    Console.WriteLine("Completed assembly {0} - #types: {1}, #methods: {2}, elapsed time: {3}, elapsed ms: {4}",
                        assemblyName, typeCount, methodCount, elapsed, elapsed.TotalMilliseconds);
                    return 0;
                }

            case "PREPONE":
                {
                    // Call PrepareMethod on a single method

                    Console.WriteLine("PrepOne for {0} {1}", assemblyName, methodToPrep);

                    foreach (Type t in aTypes)
                    {
                        Console.WriteLine("Start type {0}", t.FullName);

                        int methodCount2 = GetRMHCountOnly(t);

                        if (methodCount2 > methodToPrep)
                        {
                            MicroMethodInfo[] amMI = GetRMH(t);

                            Console.Write("Preparing method {0}::{1}", t.FullName, amMI[methodToPrep].name);

                            TimeSpan elapsedFunc = PrepareMethod(t, amMI[methodToPrep]);
                            if (elapsedFunc != TimeSpan.MinValue)
                            {
                                Console.WriteLine(", elapsed time: {0}, elapsed ms: {1}",
                                    elapsedFunc, elapsedFunc.TotalMilliseconds);
                            }

                            TimeSpan elapsed = DateTime.Now - start;
                            Console.WriteLine("Completed assembly {0} - #types: {1}, #methods: {2}, elapsed time: {3}, elapsed ms: {4}",
                                assemblyName, 1, 1, elapsed, elapsed.TotalMilliseconds);
                            return 0;
                        }
                        else
                        {
                            methodToPrep -= methodCount2;
                        }
                    }

                    Console.WriteLine("Didn't find a method #{0} in {1}", args[2], assemblyName);
                    return 0;
                }

            default:
                {
                    Console.WriteLine("ERROR: Unknown command {0}", command);
                    return 101;
                }
        }
    }
}