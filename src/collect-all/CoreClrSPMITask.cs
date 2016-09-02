////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////
//
// Module: CoreCLRSPMITask.cs
//
// Notes:
// 
// SuperPMI task responsible for coreclr oss testing collection 
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

public class CoreCLRSPMITask : ISuperPMITask
{
   public CoreCLRSPMITask(string settingFile = null)
   {
      Utilities instance = Utilities.GetInstance();
      
      bool success = instance.ParseSettingFile(settingFile);
      
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
      
      string name = this.m_os + "." + this.m_arch + "." + this.m_buildType;
      
      string defaultMchLocation = Path.Combine(instance.m_testRoot, name + "coreclr" +".mch");
      if (instance.m_loggingVerbose)
      {
         Console.WriteLine($"Setting mch path to {defaultMchLocation}");
      }
      
      // We can either do a regular run over the tests, or we can ngen them
      // all.
      //
      // TODO add functionality to runtests.py to allow ngening the oss tests.
      // Note, to pass options to runtests.py add them after the -run command
      // and after the runtests.py script.
      
      this.m_command = $"-mch {defaultMchLocation} ";
     
      // tempPath should be set on windows to deal with the 255 char limit
      // in paths.
      if (instance.m_tempPath != null)
      {
         this.m_command += $"-temp {instance.m_tempPath} ";
      }
      
      string args = "";
      
      if (instance.m_crossGen) 
      {
         args += " ";
         
         args += "crossgen";
      }
      
      this.m_command += $"-run runtests.py{args}";
   }
   
   public void RunTask()
   {
      Utilities instance = Utilities.GetInstance();
      
      // Runtests.py will need a bunch of information in order to run correctly.
      // This is transfered through a json file named input.json.
      
      var info = new Dictionary<string, string>();
      info.Add("os", this.m_os);
      info.Add("arch", this.m_arch);
      info.Add("build_type", this.m_buildType);
      info.Add("test_root_dir", Path.Combine(instance.m_testRoot));
      info.Add("coreclr_dir", instance.m_coreclrRepoLocation);
      
      if (this.m_os != "Windows_NT")
      {
         info.Add("corefx_bin_dir", instance.m_corefxBinDir);
         info.Add("corefx_native_bin_dir", instance.m_nativeCoreFxBinDir);
      }
      
      string jsonString = JsonConvert.SerializeObject(info);
      
      // Create input.json for runtests.py
      File.WriteAllText("input.json", jsonString);
      
      Process proc = new Process();
      
      proc.StartInfo.FileName = "dotnet";
      proc.StartInfo.Arguments = "superpmi-collect.dll " + this.m_command;

      if (instance.m_loggingVerbose)
      {
         Console.WriteLine("superpmi-collect" + " " + this.m_command);
         Console.WriteLine(" ");
      }
      
      proc.Start();
      proc.WaitForExit();
      
      // Clean up input.json.
      File.Delete("input.json");
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

}