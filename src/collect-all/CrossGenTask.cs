////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////
//
// Module: CrossGenTask.cs
//
// Notes:
// 
// SuperPMI task responsible for collecting over a set of managed assemblies
// passed.
//
////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////

public class CrossGenTask : ISuperPMITask
{
   private class DllSet
   {
      public string Directory;
      public string[] Items;
   }

   private class DllSets
   {
      public DllSet[] Dllsets;
   }

   public CrossGenTask(string settingsFile = null)
   {
      Utilities instance = Utilities.GetInstance();

      bool success = instance.ParseSettingFile(settingsFile);
      
      // If the setting file was incorrect or missing information
      if (!success)
      {
         Console.Error.WriteLine("Settings file incorrect");
         
         string errorMessage = "Error, setting file incorrect or missing info";
         throw new System.Exception(errorMessage);
      }
      
      this.m_productDir = instance.m_productLocation;
      this.m_testDir = instance.m_testsLocation;
      this.m_binDir = instance.m_binLocation;
      this.m_arch = instance.m_arch;
      this.m_buildType = instance.m_buildType;
      this.m_os = instance.m_os;
      this.m_coreRoot = instance.m_coreRoot;
      this.m_crossgenDlls = new List<string>();

      string name = this.m_os + "." + this.m_arch + "." + this.m_buildType;
      string defaultMchLocation = Path.Combine(instance.m_testRoot, name + "crossgen" + ".mch");

      this.m_command = this.m_command = $"-mch {defaultMchLocation} ";
      // tempPath should be set on windows to deal with the 255 char limit
      // in paths.
      if (instance.m_tempPath != null)
      {
         this.m_command += $"-temp {instance.m_tempPath} ";
      }

      string path = "crossgen_items.json";
      var stream = new FileStream(path, FileMode.Open);
      
      using (StreamReader input = new StreamReader(stream))
      {
         string json = input.ReadToEnd();
         DllSets parsedDllSets = JsonConvert.DeserializeObject<DllSets>(json);

         foreach (DllSet dllSet in parsedDllSets.Dllsets)
         {
            string dir_name = dllSet.Directory;
            if (dir_name.IndexOf("env:") != -1)
            {
               dir_name = dir_name.Split(new string[] {"env:"}, StringSplitOptions.None)[1];

               // CORE_ROOT is special, we can inject it instead of getting it
               // from the environment.
               if (dir_name == "CORE_ROOT")
               {
                  dir_name = this.m_coreRoot;
               }
               else
               {
                  dir_name = System.Environment.GetEnvironmentVariable(dir_name);
               }
            }

            foreach (string item in dllSet.Items)
            {
               this.m_crossgenDlls.Add(Path.Combine(dir_name, item));
            }
         }
      }

      string assemblyArgs = String.Join(" ", this.m_crossgenDlls);
      string crossgenExe = this.m_os == "Windows_NT" ? "crossgen.exe" : "crossgen";

      // Call crossgen helper. This requires the crossgen path.
      string crossGenPath = Path.Combine(this.m_coreRoot, crossgenExe);

      string shimJitName = "";
      if (this.m_os == "Windows_NT")
      {
         shimJitName = "superpmi-shim-collector.dll";
      }
      else if (this.m_os == "Linux")
      {
         shimJitName = "libsuperpmi-shim-collector.so";
      }
      else if (this.m_os == "OSX")
      {
         shimJitName = "libsuperpmi-shim-collector.dylib";
      }

      Debug.Assert(shimJitName != "");

      string shimJitPath = Path.Combine(this.m_coreRoot, shimJitName);
      this.m_command += $"-run superpmicrossgen.cmd crossgen -v -c {crossGenPath} --shimJit {shimJitPath} {assemblyArgs}";
   }

   /////////////////////////////////////////////////////////////////////////////
   // Member Methods
   /////////////////////////////////////////////////////////////////////////////
   
   public void RunTask()
   {
      Process proc = new Process();

      proc.StartInfo.FileName = "dotnet";
      proc.StartInfo.Arguments = "superpmi-collect.dll " + this.m_command;
      proc.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();

      if (Utilities.GetInstance().m_loggingVerbose)
      {
         Console.WriteLine("superpmi-collect" + " " + this.m_command);
         Console.WriteLine(" ");
      }
      
      proc.Start();
      proc.WaitForExit();
   }

   /////////////////////////////////////////////////////////////////////////////
   // Member Variables
   /////////////////////////////////////////////////////////////////////////////
   
   public string m_command { get; set; }
   public string m_productDir { get; set; }
   public string m_testDir { get; set; }
   public string m_binDir { get; set; }
   public string m_arch { get; set; }
   public string m_buildType { get; set; }
   public string m_os { get; set; }
   public string m_coreRoot { get; set; }
   public List<string> m_crossgenDlls { get; set;}
}