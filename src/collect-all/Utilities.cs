////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////
//
// Module: Utilities.cs
//
// Notes:
// 
// Utility class to help with setup and give common global methods. Note, this
// class is a singleton.
//
////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using System.Runtime.InteropServices;

////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////

public class GeneralException : Exception
{
   public GeneralException() { }
   
   public GeneralException(string message) : base(message) { }
}

////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////

public class Utilities
{
   /////////////////////////////////////////////////////////////////////////////
   // Constructor(s)
   /////////////////////////////////////////////////////////////////////////////
   private Utilities(string tempPath = null, bool crossGen = false, bool loggingVerbose = false, string arch = null, string buildType = null, string coreclrRepoLocation = null) 
   {  
      // Find out what Operating system we are on. This is mostly just to
      // make sure the OS is supported for SPMI Collection
      string os = this.GetOs();
      
      this.m_tempPath = tempPath;
      this.m_crossGen = crossGen;
      this.m_loggingVerbose = loggingVerbose;
      
      if (string.IsNullOrEmpty(arch) && string.IsNullOrEmpty(buildType) && string.IsNullOrEmpty(coreclrRepoLocation))
      {
         ParseSettingFile();

         buildType = this.m_buildType;
         coreclrRepoLocation = this.m_coreclrRepoLocation;
         arch = this.m_arch;
      }
     
      coreclrRepoLocation = this.m_coreclrRepoLocation;
      string[] supportedArches = new string[] { "x64" }

      // Set Default Values.
      //
      // jit: ryujit
      // arch: x64
      // buildType: Debug

      if (string.IsNullOrEmpty(buildType))
      {
        buildType = "Debug";
      }

      if (string.IsNullOrEmpty(arch)) 
      {
         arch = RuntimeInformation.OSArchitecture.ToString();
 
         // Check for a supported architecture        
         bool supported = false;
         foreach (string item in supprtedArches)
         {
            if (arch.ToLower() == item) 
            {
               arch = item.ToLower();
               supported = True;
            }

         }

         if (!supported)
         {
            throw new GeneralException($"Unsupported arch. {arch}");
         }
      }
      
      if (string.IsNullOrEmpty(coreclrRepoLocation))
      {
        throw new GeneralException("Error coreclrRepoLocation is empty.");
      }

      else
      {
         this.m_os = os;
         this.m_arch = arch;
         this.m_buildType = buildType;
      }
            
      this.m_binLocation = Path.Combine(coreclrRepoLocation, "bin");
      this.m_productLocation = Path.Combine(this.m_binLocation, "Product", this.m_os + "." + this.m_arch + "." + this.m_buildType);
      this.m_testsLocation = Path.Combine(coreclrRepoLocation, "tests");
      this.m_testRoot = Path.Combine(coreclrRepoLocation, "bin", "tests", this.m_os + "." + this.m_arch + "." + this.m_buildType);
      
      string coreRootDirName = this.m_os == "Windows_NT" ? "Core_Root" : "coreoverlay";
      this.m_coreRoot = Path.Combine(this.m_binLocation, "tests", this.m_os + "." + this.m_arch + "." + this.m_buildType, "Tests", coreRootDirName);

      if (!Directory.Exists(this.m_testRoot))
      {
         if (this.m_os == "Windows_NT")
         {
            Console.WriteLine("Error, Tests were not built. Please build CoreCLR");
            throw new GeneralException("Please build tests and run again.");
         }

         this.DownloadTests();
      }
      
      if (!IsValidOS(this.m_os)) throw new GeneralException(this.m_os + " is not a valid OS.");
      if (!IsValidArch(this.m_arch)) throw new GeneralException(this.m_arch + " is not a valid arch");
      if (!IsValidBuildType(this.m_buildType)) throw new GeneralException(this.m_buildType + " is not a valid build type");
      
      if (!Directory.Exists(this.m_binLocation)) throw new GeneralException($"Error coreclr/bin location: {this.m_binLocation} does not exist.");
      if (!Directory.Exists(this.m_coreRoot)) throw new GeneralException($"Error core_root location: {this.m_coreRoot} does not exist.");
      if (!Directory.Exists(this.m_productLocation)) throw new GeneralException($"Error bin/product location: {this.m_productLocation} does not exist.");
      
      // Core root is expected to be set; however, just in case
      // copy over the binaries from the bin directory.
      
      this.CopyDir(this.m_productLocation, this.m_coreRoot);
      
      // On Linux and mac, get the most recent corefx build.
      if (this.m_os != "Windows_NT")
      {
         if (this.m_loggingVerbose)
         {
            Console.WriteLine($"{this.m_os} requires corefx to be built. Pulling down an lkg of corefx");
            Console.WriteLine(" ");
         }
         
         // Get a tempPath in which to pull down the most recent build of corefx.
      
         string tempDir = Path.GetTempPath();
         
         string corefxDirectory = Path.Combine(tempDir, "CoreFx");
         
         // Delete the old CoreFX directory if exists
         if (Directory.Exists(corefxDirectory))
         {
            Directory.Delete(corefxDirectory, true);
         }
         
         Directory.CreateDirectory(corefxDirectory);
         
         // Find the last successful build based on OS.
         string base_url = "http://dotnet-ci.cloudapp.net/job/dotnet_corefx/job/master/job/";
         
         string job_name = "";
         if (this.m_os == "OSX")
         {
            job_name = "osx_release";
         }
         
         else if (this.m_os == "Linux")
         {
            job_name = "ubuntu14.04_release";
         }
         
         string url = base_url + job_name + "/lastSuccessfulBuild/artifact/bin/build.tar.gz";
         string args = $"{url} -P {corefxDirectory}"; 
         
         // Download the tar.gz
         Process proc = new Process();
      
         proc.StartInfo.FileName = "wget";
         proc.StartInfo.Arguments = args;
         proc.StartInfo.RedirectStandardOutput = true;
         proc.StartInfo.RedirectStandardError = true;
         
         // Throw away the output
         proc.OutputDataReceived += (sender, arg) => { var output = arg.Data; };
         proc.ErrorDataReceived += (sender, arg) => { var err = arg.Data; };
         
         if (this.m_loggingVerbose)
         {
            Console.WriteLine($"wget {args}");
         }
         
         proc.Start();
                  
         proc.BeginOutputReadLine();
         proc.BeginErrorReadLine();
         proc.WaitForExit();
         
         // Check for wget failure. Make sure several tests are there.

         string buildGzLocation = Path.Combine(corefxDirectory, "build.tar.gz");
         if (!File.Exists(buildGzLocation))
         {
            throw new FileNotFoundException($"{buildGzLocation} not found.");
         }
         
         args = $"xzf {corefxDirectory}/build.tar.gz -C {corefxDirectory}";
         
         // untar, unzip
         proc = new Process();
      
         proc.StartInfo.FileName = "tar";
         proc.StartInfo.Arguments = args;
         proc.StartInfo.RedirectStandardOutput = true;
         proc.StartInfo.RedirectStandardError = true;
         
         if (this.m_loggingVerbose)
         {
            Console.WriteLine($"tar {args}");
         }
         
         // Throw away the output
         proc.OutputDataReceived += (sender, arg) => { var output = arg.Data; };
         proc.ErrorDataReceived += (sender, arg) => { var err = arg.Data; };
         
         // Check tar's output
         proc.Start();
         proc.BeginOutputReadLine();
         proc.BeginErrorReadLine();

         proc.WaitForExit();
         
         string anyCpuRelease = ".AnyCPU.Release";
         string osComboName = this.m_os + anyCpuRelease;
         string osComboNameNative = this.m_os + "." + this.m_arch + ".Release";
         
         string coreFxBinDir = Path.Combine(corefxDirectory, "bin", osComboName); 
         coreFxBinDir += ";" + Path.Combine(corefxDirectory, "bin", "Unix" + anyCpuRelease);
         coreFxBinDir += ";" + Path.Combine(corefxDirectory, "bin", "AnyOS" + anyCpuRelease);
         string coreFxNativeBinDir = Path.Combine(corefxDirectory, "bin", osComboNameNative);

         foreach (string directory in coreFxBinDir.Split(new char[1] {';'}))
         {
            if (!Directory.Exists(directory))
            {
               throw new DirectoryNotFoundException($"{directory} not found.");
            }
         }         

         if (!Directory.Exists(coreFxNativeBinDir))
         {
            throw new DirectoryNotFoundException($"{coreFxNativeBinDir} not found.");
         }
         
         this.m_corefxBinDir = coreFxBinDir;
         this.m_nativeCoreFxBinDir = coreFxNativeBinDir;
      }
      
      // Set Core_Root for superPMI.
     
      if (this.m_loggingVerbose)
      {
         Console.WriteLine(" ");
         Console.WriteLine($"CORE_ROOT: {this.m_coreRoot}");
         Console.WriteLine(" ");
      }
     
      Environment.SetEnvironmentVariable("CORE_ROOT", this.m_coreRoot);

   }
   
   /////////////////////////////////////////////////////////////////////////////
   // Public Methods
   /////////////////////////////////////////////////////////////////////////////
   
   public static Utilities GetInstance(string tempPath = null, bool crossGen = false, bool loggingVerbose = false, string arch = null, string buildType = null, string coreclrRepoLocation = null)
   {
      if (Utilities.m_Instance == null)
      {
         Utilities.m_Instance = new Utilities(tempPath, crossGen, loggingVerbose, arch, buildType, coreclrRepoLocation);
      }
      
      return Utilities.m_Instance;
   }
   
   /////////////////////////////////////////////////////////////////////////////
   // Utility Methods
   /////////////////////////////////////////////////////////////////////////////
   
   public void CopyDir(string source, string dest)
   {
      DirectoryInfo dir = new DirectoryInfo(source);
      
      if (!dir.Exists)
      {
         string err_msg = $"Source directory {source} does not exit";
         throw new DirectoryNotFoundException(err_msg);
      }
      
      Directory.CreateDirectory(dest);
      FileInfo[] files = dir.GetFiles();
      
      foreach (var file in files)
      {
         // Path to the new location.
         string tempPath = Path.Combine(dest, file.Name);
         
         // Copy, overwrite if found.
         file.CopyTo(tempPath, true);
      }
      
      // Copy all sub directories.
      DirectoryInfo[] dirs = dir.GetDirectories();
      foreach (var subDir in dirs)
      {
         // Path to dest subdir
         string tempPath = Path.Combine(dest, subDir.Name);
         
         // Recursive call to copy all files in the subDir
         CopyDir(subDir.FullName, tempPath);
      }
   }

   public void DownloadTests()
   {
      Directory.CreateDirectory(this.m_testRoot);

      // Find the last successful test build based on OS.
      string url = "http://dotnet-ci.cloudapp.net/job/dotnet_coreclr/job/master/job/release_windows_nt/lastSuccessfulBuild/artifact/bin/tests/tests.zip";
      string args = $"{url} -P {this.m_testRoot}";
         
      // Download the zip
      Process proc = new Process();
      
      proc.StartInfo.FileName = "wget";
      proc.StartInfo.Arguments = args;
      proc.StartInfo.RedirectStandardOutput = true;
      proc.StartInfo.RedirectStandardError = true;
         
      // Throw away the output
      proc.OutputDataReceived += (sender, arg) => { var output = arg.Data; };
      proc.ErrorDataReceived += (sender, arg) => { var err = arg.Data; };
      
      if (this.m_loggingVerbose)
      {
         Console.WriteLine($"wget {args}");
      }
 
      // Check wget's response value        
      proc.Start();
                  
      proc.BeginOutputReadLine();
      proc.BeginErrorReadLine();

      proc.WaitForExit();

      string zipLocation = Path.Combine(this.m_testRoot, "tests.zip");

      // Go over each archive entry making sure the path contains the correct
      // system's directory specifier.
      using (ZipArchive archive = ZipFile.OpenRead(zipLocation))
      {
         foreach (ZipArchiveEntry entry in archive.Entries)
         {
            string entryName = this.m_os == "Windows_NT" ? entry.FullName : entry.FullName.Replace("\\", "/");

            // This is a directory mkdir -p
            if (entryName.Contains("/"))
            {
               Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(this.m_testRoot, entryName)));
            }

            entry.ExtractToFile(Path.Combine(this.m_testRoot, entryName));
         }
      }

      Directory.CreateDirectory($"{this.m_coreRoot}");  
   }

   public string GetOs()
   {
      string os = "";
      string runtimeInfo = RuntimeInformation.OSDescription;
      string osSplit = runtimeInfo.Split(' ')[0];
      
      if (osSplit == "Darwin")
      {
         os = "OSX";
      }

      else if (osSplit == "Linux") 
      {
        os = "Linux";
      }
      
      else if (osSplit == "Microsoft")
      {
         os = "Windows_NT";
      }

      else 
      {
         throw new NotImplementedException($"Unsupported OS: {osSplit}");
      }

      return os;
   }
   
   private class Setting
   {
      public string CoreClrRepoLocation;
      public string BuildType;
      public string Arch;
   }
   
   public bool ParseSettingFile(string location = null)
   {
      // Make sure that the spmi_settings.json file exists
      string path = Path.Combine(System.IO.Directory.GetCurrentDirectory(), "spmi_settings.json");
      var stream = new FileStream(path, FileMode.Open);
      
      using (StreamReader input = new StreamReader(stream))
      {
         string json = input.ReadToEnd();
         Setting setting = JsonConvert.DeserializeObject<Setting>(json);
         
         Action<string> throwException = (passedSetting) => { throw new System.Exception("Error: " + passedSetting + " is null or empty."); };
         
         if (string.IsNullOrEmpty(setting.Arch)) throwException("Arch");
         if (string.IsNullOrEmpty(setting.BuildType)) throwException("BuildType");
         if (string.IsNullOrEmpty(setting.CoreClrRepoLocation)) throwException("CoreClrRepoLocation");
         
         this.m_arch = setting.Arch;
         this.m_buildType = setting.BuildType;
         this.m_coreclrRepoLocation = setting.CoreClrRepoLocation;
         
         return true;
      }
   }
   
   public bool IsValidArch(string arch)
   {
      return arch == "x64" || arch == "x86" || arch == "arm" || arch == "arm64";
   }
   
   public bool IsValidBuildType(string buildType)
   {
      return buildType == "Debug" || buildType == "Checked" || buildType == "Release";
   }
   
   public bool IsValidOS(string os)
   {
      return os == "OSX" || os == "Windows_NT" || os == "Linux";
   }
   
   /////////////////////////////////////////////////////////////////////////////
   // Member Variables
   ////////////////////////////////////////////////////////////////////////////
   
   public string m_arch { get; set; }
   public string m_buildType { get; set; }
   public string m_binLocation { get; set; }
   public string m_coreclrRepoLocation { get; set; }
   public string m_os { get; set; }
   public string m_productLocation { get; set; }
   public string m_coreRoot { get; set; }
   public string m_testsLocation { get; set; }
   public string m_tempPath { get; set; }
   public bool m_crossGen { get; set; }
   public bool m_loggingVerbose { get; set; }
   public string m_corefxBinDir { get; set; }
   public string m_nativeCoreFxBinDir { get; set; }
   public string m_testRoot { get; set; }
   
   private static Utilities m_Instance;
   
}
