// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;

// This set of classes provides a way to forcibly jit a large number of methods.
// It can be used as is or included as a component in jit measurement and testing
// tools.
//
// In .Net Core, PrepareMethod should give codegen that is very similar to
// the code one would see if the method were actually called (the same is not
// as true in .Net Framework -- in particular the jit may make very different
// inlining decisions).
//
// Assemblies defining generic types and generic methods require special handling.
// Methods in generic types and generic methods can inspire the jit to create
// numerous different method bodies depending on the type parameters used
// for instantation.
// 
// The code below uses a very simple generic instantiation strategy. It currently
// only handles one- and two-parameter generic types with simple constraints.

// Base class for visiting types and methods in an assembly.
class Visitor
{
    protected DateTime startTime;
    protected string assemblyName;

    public void Init(string assemblyPath)
    {
        assemblyName = assemblyPath;
    }

    public virtual void StartAssembly(Assembly assembly)
    {
        startTime = DateTime.Now;
    }

    public virtual void FinishAssembly(Assembly assembly)
    {
    }

    public virtual void StartType(Type type)
    {
    }

    public virtual void FinishType(Type type)
    {
    }

    public virtual void StartMethod(Type type, MethodBase method)
    {
    }

    public virtual bool FinishMethod(Type type, MethodBase method)
    {
        return true;
    }

    public TimeSpan ElapsedTime()
    {
        return DateTime.Now - startTime;
    }
}

// Support for counting types and methods.
class CounterBase : Visitor
{
    protected int typeCount;
    protected int methodCount;

    public override void StartAssembly(Assembly assembly)
    {
        base.StartAssembly(assembly);
    }

    public override void FinishType(Type type)
    {
        typeCount++;
        base.FinishType(type);
    }

    public override bool FinishMethod(Type type, MethodBase method)
    {
        methodCount++;
        return base.FinishMethod(type, method);
    }
}

// Counts types and methods
class Counter : CounterBase
{
    public override void StartAssembly(Assembly assembly)
    {
        base.StartAssembly(assembly);
        Console.WriteLine($"Computing Count for {assemblyName}");
    }

    public override void StartType(Type type)
    {
        base.StartType(type);
        Console.WriteLine($"#types: {typeCount}, #methods: {methodCount}, before type {type.FullName}");
    }

    public override void FinishAssembly(Assembly assembly)
    {
        base.FinishAssembly(assembly);
        TimeSpan elapsed = ElapsedTime();
        Console.WriteLine(
            $"Counts {assemblyName} - #types: {typeCount}, #methods: {methodCount}, " +
            $"elapsed time: {elapsed}, elapsed ms: {elapsed.TotalMilliseconds}");
    }
}

// Invoke the jit on some methods
abstract class PrepareBase : CounterBase
{
    protected int firstMethod;
    protected int methodsPrepared;
    protected DateTime startType;

    public PrepareBase(int f = 0)
    {
        firstMethod = f;
    }

    public override void StartAssembly(Assembly assembly)
    {
        base.StartAssembly(assembly);
    }

    public override void FinishAssembly(Assembly assembly)
    {
        base.FinishAssembly(assembly);

        TimeSpan elapsed = ElapsedTime();
        Console.WriteLine(
            $"Completed assembly {assemblyName} - #types: {typeCount}, #methods: {methodsPrepared}, " +
            $"elapsed time: {elapsed}, elapsed ms: {elapsed.TotalMilliseconds}");
    }

    public override void StartType(Type type)
    {
        base.StartType(type);
        Console.WriteLine("Start type {0}", type.FullName);
        startType = DateTime.Now;
    }

    public override void FinishType(Type type)
    {
        TimeSpan elapsedType = DateTime.Now - startType;
        Console.WriteLine("Completed type {0}, type elapsed time: {1}, elapsed ms: {2}",
            type.FullName, elapsedType, elapsedType.TotalMilliseconds);

        base.FinishType(type);
    }

    public override void StartMethod(Type type, MethodBase method)
    {
        base.StartMethod(type, method);
        AttemptMethod(type, method);
    }

    public abstract void AttemptMethod(Type type, MethodBase method);

    protected TimeSpan PrepareMethod(Type type, MethodBase method)
    {
        TimeSpan elapsedFunc = TimeSpan.MinValue;

        try
        {
            DateTime startFunc = DateTime.Now;
            System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(method.MethodHandle);
            elapsedFunc = DateTime.Now - startFunc;
        }
        catch (System.EntryPointNotFoundException)
        {
            Console.WriteLine();
            Console.WriteLine("EntryPointNotFoundException {0}::{1}", type.FullName, method.Name);
        }
        catch (System.BadImageFormatException)
        {
            Console.WriteLine();
            Console.WriteLine("BadImageFormatException {0}::{1}", type.FullName, method.Name);
        }
        catch (System.MissingMethodException)
        {
            Console.WriteLine();
            Console.WriteLine("MissingMethodException {0}::{1}", type.FullName, method.Name);
        }
        catch (System.ArgumentException e)
        {
            Console.WriteLine();
            Console.WriteLine("ArgumentException {0}::{1} {2}",
                type.FullName, method.Name, e.Message.Split(new char[] { '\r', '\n' })[0]);
        }
        catch (System.IO.FileNotFoundException eFileNotFound)
        {
            Console.WriteLine();
            Console.WriteLine("FileNotFoundException {0}::{1} - {2} ({3})",
                type.FullName, method.Name, eFileNotFound.FileName, eFileNotFound.Message);
        }
        catch (System.DllNotFoundException eDllNotFound)
        {
            Console.WriteLine();
            Console.WriteLine("DllNotFoundException {0}::{1} ({2})", type.FullName, method.Name, eDllNotFound.Message);
        }
        catch (System.TypeInitializationException eTypeInitialization)
        {
            Console.WriteLine();
            Console.WriteLine("TypeInitializationException {0}::{1} - {2} ({3})",
                type.FullName, method.Name, eTypeInitialization.TypeName, eTypeInitialization.Message);
        }
        catch (System.Runtime.InteropServices.MarshalDirectiveException)
        {
            Console.WriteLine();
            Console.WriteLine("MarshalDirectiveException {0}::{1}", type.FullName, method.Name);
        }
        catch (System.TypeLoadException)
        {
            Console.WriteLine();
            Console.WriteLine("TypeLoadException {0}::{1}", type.FullName, method.Name);
        }
        catch (System.OverflowException)
        {
            Console.WriteLine();
            Console.WriteLine("OverflowException {0}::{1}", type.FullName, method.Name);
        }
        catch (System.InvalidProgramException)
        {
            Console.WriteLine();
            Console.WriteLine("InvalidProgramException {0}::{1}", type.FullName, method.Name);
        }
        catch (Exception e)
        {
            Console.WriteLine();
            Console.WriteLine("Unknown exception");
            Console.WriteLine(e);
        }

        return elapsedFunc;
    }

    private void WriteAndFlushNextMethodToPrepMarker()
    {
        int nextMethodToPrep = (methodCount + 1);

        using (var writer = new StreamWriter(File.Create("NextMethodToPrep.marker")))
        {
            writer.Write("{0}", nextMethodToPrep);
        }
    }
}

// Invoke the jit on all methods starting from an initial method.
// By default the initial method is the first one visisted.
class PrepareAll : PrepareBase
{
    string pmiFullLogFileName;
    string pmiPartialLogFileName;

    public PrepareAll(int f = 0) : base(f)
    {
    }

    public override void StartAssembly(Assembly assembly)
    {
        base.StartAssembly(assembly);
        Console.WriteLine($"Prepall for {assemblyName}");
        pmiFullLogFileName = String.Format("{0}.pmi", Path.GetFileNameWithoutExtension(assemblyName));
        pmiPartialLogFileName = String.Format("{0}.pmiPartial", Path.GetFileNameWithoutExtension(assemblyName));
    }

    public override void AttemptMethod(Type type, MethodBase method)
    {
        WriteAndFlushNextMethodToPrepMarker();

        if (methodCount >= firstMethod)
        {
            methodsPrepared++;

            if (method.IsAbstract)
            {
                Console.WriteLine("PREPALL type# {0} method# {1} {2}::{3} - skipping (abstract)",
                    typeCount, methodCount, type.FullName, method.Name);
            }
            else if (method.ContainsGenericParameters)
            {
                Console.WriteLine("PREPALL type# {0} method# {1} {2}::{3} - skipping (generic parameters)",
                    typeCount, methodCount, type.FullName, method.Name);
            }
            else
            {
                Console.Write("PREPALL type# {0} method# {1} {2}::{3}", typeCount, methodCount, type.FullName, method.Name);
                TimeSpan elapsedFunc = PrepareMethod(type, method);
                if (elapsedFunc != TimeSpan.MinValue)
                {
                    Console.WriteLine(", elapsed time: {0}, elapsed ms: {1}",
                        elapsedFunc, elapsedFunc.TotalMilliseconds);
                }
            }
        }
    }

    private void WriteAndFlushNextMethodToPrepMarker()
    {
        int nextMethodToPrep = (methodCount + 1);

        using (var writer = new StreamWriter(File.Create("NextMethodToPrep.marker")))
        {
            writer.Write("{0}", nextMethodToPrep);
        }
    }
}

// Invoke the jit on exactly one method.
class PrepareOne : PrepareBase
{
    public PrepareOne(int firstMethod) : base(firstMethod)
    {
    }

    public override void StartAssembly(Assembly assembly)
    {
        base.StartAssembly(assembly);
        Console.WriteLine($"Prepone for {assemblyName} method {firstMethod} ");
    }

    public override void AttemptMethod(Type type, MethodBase method)
    {
        if (methodCount >= firstMethod)
        {
            methodsPrepared++;

            if (method.IsAbstract)
            {
                Console.WriteLine("PREPONE type# {0} method# {1} {2}::{3} - skipping (abstract)",
                    typeCount, methodCount, type.FullName, method.Name);
            }
            else if (method.ContainsGenericParameters)
            {
                Console.WriteLine("PREPONE type# {0} method# {1} {2}::{3} - skipping (generic parameters)",
                    typeCount, methodCount, type.FullName, method.Name);
            }
            else
            {
                Console.Write("PREPONE type# {0} method# {1} {2}::{3}", typeCount, methodCount, type.FullName, method.Name);
                TimeSpan elapsedFunc = PrepareMethod(type, method);
                if (elapsedFunc != TimeSpan.MinValue)
                {
                    Console.WriteLine(", elapsed time: {0}, elapsed ms: {1}",
                        elapsedFunc, elapsedFunc.TotalMilliseconds);
                }
            }
        }
    }

    public override bool FinishMethod(Type type, MethodBase method)
    {
        bool baseResult = base.FinishMethod(type, method);
        return baseResult && (methodCount <= firstMethod);
    }
}

static class GlobalMethodHolder
{
    public static MethodBase[] GlobalMethodInfoSet;

    public static void PopulateGlobalMethodInfoSet(MethodBase[] globalMethods)
    {
        GlobalMethodInfoSet = globalMethods;
    }
}

// The worker is responsible for driving the visitor through the
// types and methods of an assembly.
//
// It includes the generic instantiation strategy.
class Worker
{
    Visitor visitor;

    public Worker(Visitor v)
    {
        visitor = v;
    }

    private static BindingFlags BindingFlagsForCollectingAllMethodsOrCtors = (
        BindingFlags.DeclaredOnly |
        BindingFlags.Instance |
        BindingFlags.NonPublic |
        BindingFlags.Public |
        BindingFlags.Static
    );

    private static Assembly LoadAssembly(string assemblyPath)
    {
        Assembly result = null;

        // The core library needs special handling as it often is in fragile ngen format
        if (assemblyPath.EndsWith("System.Private.CoreLib.dll") || assemblyPath.EndsWith("mscorlib.dll"))
        {
            result = typeof(object).Assembly;
        }
        else
        {
            try
            {
                result = Assembly.LoadFrom(assemblyPath);
            }
            catch (ArgumentException)
            {
                Console.WriteLine("Assembly load failure ({0}): ArgumentException", assemblyPath);
            }
            catch (BadImageFormatException e)
            {
                Console.WriteLine("Assembly load failure ({0}): BadImageFormatException (is it a managed assembly?)", assemblyPath);
                Console.WriteLine(e);
            }
            catch (FileLoadException)
            {
                Console.WriteLine("Assembly load failure ({0}): FileLoadException", assemblyPath);
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("Assembly load failure ({0}): file not found", assemblyPath);
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("Assembly load failure ({0}): UnauthorizedAccessException", assemblyPath);
            }
        }

        return result;
    }

    static MethodBase[] GetMethods(Type t)
    {
        if (Object.ReferenceEquals(t, typeof(GlobalMethodHolder)))
        {
            return GlobalMethodHolder.GlobalMethodInfoSet;
        }

        MethodInfo[] mi = t.GetMethods(BindingFlagsForCollectingAllMethodsOrCtors);
        ConstructorInfo[] ci = t.GetConstructors(BindingFlagsForCollectingAllMethodsOrCtors);
        MethodBase[] mMI = new MethodBase[mi.Length + ci.Length];

        for (int i = 0; i < mi.Length; i++)
        {
            mMI[i] = mi[i];
        }

        for (int i = 0; i < ci.Length; i++)
        {
            mMI[i + mi.Length] = ci[i];
        }

        return mMI;
    }

    private static List<Type> LoadTypes(Assembly assembly)
    {
        List<Type> result = new List<Type>();

        var globalMethods = assembly.ManifestModule.GetMethods(BindingFlagsForCollectingAllMethodsOrCtors);

        if (globalMethods.Length > 0)
        {
            GlobalMethodHolder.PopulateGlobalMethodInfoSet(globalMethods);
            result.Add(typeof(GlobalMethodHolder));
        }

        try
        {
            result.AddRange(assembly.GetTypes());
            return result;
        }
        catch (ReflectionTypeLoadException e)
        {
            Console.WriteLine("ReflectionTypeLoadException {0}", assembly);
            Exception[] ea = e.LoaderExceptions;
            foreach (Exception e2 in ea)
            {
                string[] ts = e2.ToString().Split('\'');
                string temp = ts[1];
                ts = temp.Split(',');
                temp = ts[0];
                Console.WriteLine("ReflectionTypeLoadException {0} {1}", temp, assembly);
            }
            return null;
        }
        catch (FileLoadException)
        {
            Console.WriteLine("FileLoadException {0}", assembly);
            return null;
        }
        catch (FileNotFoundException e)
        {
            string temp = e.ToString();
            string[] ts = temp.Split('\'');
            temp = ts[1];
            Console.WriteLine("FileNotFoundException {1} {0}", temp, assembly);
            return null;
        }
    }

    public int Work(string assemblyName)
    {
        Assembly assembly = LoadAssembly(Path.GetFullPath(assemblyName));

        if (assembly == null)
        {
            return 102;
        }

        List<Type> types = LoadTypes(assembly);

        if (types == null)
        {
            return 103;
        }

        visitor.Init(assemblyName);
        visitor.StartAssembly(assembly);

        bool keepGoing = true;

        foreach (Type t in types)
        {
            // Skip types with no jittable methods
            if (t.IsInterface)
            {
                continue;
            }

            // Likewise there are no methods of interest in delegates.
            if (t.IsSubclassOf(typeof(System.Delegate)))
            {
                continue;
            }

            if (t.IsGenericType)
            {
                List<Type> instances = GetInstances(t);

                foreach (Type ti in instances)
                {
                    keepGoing = Work(ti);
                    if (!keepGoing)
                    {
                        break;
                    }
                }
            }
            else
            {
                keepGoing = Work(t);
            }

            if (!keepGoing)
            {
                break;
            }
        }

        visitor.FinishAssembly(assembly);

        return 0;
    }

    bool Work(Type type)
    {
        visitor.StartType(type);
        bool keepGoing = true;
        foreach (MethodBase methodBase in GetMethods(type))
        {
            visitor.StartMethod(type, methodBase);
            keepGoing = visitor.FinishMethod(type, methodBase);
            if (!keepGoing)
            {
                break;
            }
        }

        visitor.FinishType(type);

        return keepGoing;
    }

    private List<Type> GetInstances(Type type)
    {
        List<Type> results = new List<Type>();

        // Get the args for this generic
        Type[] genericArguments = type.GetGenericArguments();

        // Only handle the very simplest cases for now
        if (genericArguments.Length > 2)
        {
            return results;
        }

        // Types we will use for instantiation attempts.
        Type[] typesToTry = new Type[] { typeof(object), typeof(int), typeof(double), typeof(Vector<float>) };

        // To keep things sane, we won't try and instantiate too many copies
        int instantiationLimit = genericArguments.Length * typesToTry.Length;
        int instantiationCount = 0;

        foreach (Type firstType in typesToTry)
        {
            bool areConstraintsSatisfied = AreConstraintsSatisfied(firstType, genericArguments[0]);

            Type secondType = null;

            if (genericArguments.Length == 2)
            {
                foreach (Type secondTypeX in typesToTry)
                {
                    areConstraintsSatisfied &= AreConstraintsSatisfied(secondTypeX, genericArguments[1]);
                    secondType = secondTypeX;
                }
            }

            if (!areConstraintsSatisfied)
            {
                continue;
            }

            // Now try and instantiate.
            try
            {
                Type newType = null;

                if (genericArguments.Length == 1)
                {
                    newType = type.MakeGenericType(firstType);
                }
                else if (genericArguments.Length == 2)
                {
                    newType = type.MakeGenericType(firstType, secondType);
                }

                // If we can instantiate, prepare the methods.
                if (newType != null)
                {
                    results.Add(newType);
                }
            }
            catch (Exception)
            {
                // Probably missing a constraint check
            }

            if (instantiationCount >= instantiationLimit)
            {
                break;
            }
        }

        return results;
    }

    // Try and identify obviously invalid type substitutions.
    //
    // It is ok if we miss some, as we catch the exception that will
    // arise when instantating with an invalid type.
    static bool AreConstraintsSatisfied(Type type, Type parameterType)
    {
        bool areConstraintsSatisfied = true;
        GenericParameterAttributes gpa = parameterType.GenericParameterAttributes;

        if ((gpa & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
        {
            if (type.IsValueType)
            {
                areConstraintsSatisfied = false;
            }
        }
        else
        {
            Type[] constraints = parameterType.GetGenericParameterConstraints();

            foreach (Type c in constraints)
            {
                if (c.IsClass)
                {
                    // Base Type Constraint
                    if (!type.IsSubclassOf(c)) // variance probably needs other checks
                    {
                        areConstraintsSatisfied = false;
                        break;
                    }
                }
                else
                {
                    // Interface constraint
                    if (!type.IsInstanceOfType(c))
                    {
                        areConstraintsSatisfied = false;
                        break;
                    }
                }
            }
        }

        return areConstraintsSatisfied;
    }
}

class PrepareMethodinator
{
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

        Visitor v = null;

        switch (command)
        {
            case "DRIVEALL":
            case "COUNT":
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

                if (command == "DRIVEALL")
                {
                    return PMIDriver.PMIDriver.Drive(assemblyName);
                }

                v = new Counter();
                break;

            case "PREPALL":
            case "PREPONE":

                if (args.Length < 3)
                {
                    methodToPrep = 0;
                }
                else if (args.Length > 3)
                {
                    Console.WriteLine("ERROR: too many arguments");
                    return Usage();
                }
                else
                {
                    try
                    {
                        methodToPrep = Convert.ToInt32(args[2]);
                    }
                    catch (System.FormatException)
                    {
                        Console.WriteLine("ERROR: illegal method number");
                        return Usage();
                    }
                }

                if (command == "PREPALL")
                {
                    v = new PrepareAll(methodToPrep);
                }
                else
                {
                    v = new PrepareOne(methodToPrep);
                }
                break;

            default:
                Console.WriteLine("ERROR: Unknown command {0}", command);
                return Usage();
        }

        Worker w = new Worker(v);
        int result = w.Work(assemblyName);
        return result;
    }
}
