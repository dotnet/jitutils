using System;
using System.IO;
using System.Collections.Generic;

namespace SuperPMI
{
    public class SuperPMIDriver
    {
        public static Dictionary<string, string> Options { get; set; }
        public static Dictionary<string, bool> SupportedArches { get; set; }
        public static Dictionary<string, bool> SupportedBuildTypes { get; set; }
        
        static SuperPMIDriver()
        {
           SupportedArches = new Dictionary<string, bool>();
           SupportedBuildTypes = new Dictionary<string, bool>();
           
           SupportedArches.Add("arm", true);
           SupportedArches.Add("arm64", true);
           SupportedArches.Add("x86", true);
           SupportedArches.Add("amd64", true);
           
           SupportedBuildTypes.Add("debug", true);
           SupportedBuildTypes.Add("checked", true);
           SupportedBuildTypes.Add("release", true);
        }
        
        public static void PrintHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("SuperPMIDriver.exe <arch> <buildType> <asmDiffs> <coreRootDir>");
        }
        
        public static bool ParseArgs(string[] args)
        {
            Options = new Dictionary<string, string>();
            
            if (args.Length < 4)
            {
                return false; 
            }
            
            string arch = args[1];
            string buildType = args[2];
            string asmDiffs = "";
            
            int coreRootIndex = 3;
            if (args.Length == 5)
            {
                asmDiffs = "asmDiffs";
                coreRootIndex = 4;
            }
            
            string coreRootDir = args[coreRootIndex];
            bool archSupported, buildSupported, dirExists;
            
            SupportedArches.TryGetValue(arch, out archSupported);
            SupportedBuildTypes.TryGetValue(buildType, out buildSupported);
            dirExists = Directory.Exists(coreRootDir);
            
            if (!archSupported || !buildSupported || !dirExists)
            {
               return false;
            }
            
            Options["arch"] = arch;
            Options["buildType"] = buildType;
            Options["asmDiffs"] = asmDiffs;
            Options["coreRootDir"] = coreRootDir;
            
            return true;
        }
        
        public static void Main(string[] args)
        {
            if (!ParseArgs(args))
            {
                PrintHelp();
                return;
            }
        }
    }
}
