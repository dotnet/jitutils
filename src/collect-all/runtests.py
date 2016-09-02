#!/usr/bin/env python
################################################################################
################################################################################
#
# Module: runtest.py
#
# Notes:
#  
# Simple script that essentially sets up and runs either runtest.cmd
# or runtests.sh. This wrapper is necessary to do all the setup work.
#
# Use the instructions here:
#    https://github.com/dotnet/coreclr/blob/master/Documentation/building/windows-test-instructions.md 
#    https://github.com/dotnet/coreclr/blob/master/Documentation/building/unix-test-instructions.md
#
################################################################################
################################################################################

import json
import os
import platform
import subprocess
import sys

from collections import defaultdict
from sys import platform as _platform

################################################################################
# Globals
################################################################################

g_is_windows = False

if os.name == "nt":
   g_is_windows = True

################################################################################
# Helper Functions
################################################################################

# If there are config options like jitstress, gcstress create a batch file to
# set up the complus variables and return a path to that location.
#
# If the path that is returned is None, then there are no configurations.
def create_config_file(configuration):
   if configuration is None or len(configuration) == 0:
      return None

   print "Configuration found"

   options = []
   home_path = os.environ["HOMEPATH"]

   for config in configuration:
      print config
      config = config.split(".")
   
      set_complus = "set "
      
      options.append("%s%s=%s" % (set_complus, config[0], ".".join(config[1:])))

   file_location = os.path.join("C:", home_path, "AppData", "Local", "Temp", "superpmi_complus_settings.cmd")
   with open(file_location, "w") as file_handle:
      for option in options:
         file_handle.write(option)
         file_handle.write("\n")
         print "Adding option: %s" % (option) 
      
      file_handle.close()

   print "TestEnv script location: %s" % (file_location)

   return file_location

# Will return a dictionary with:
#
# dict["os"]
# dict["build_type"]
# dict["arch"]
#
def parse_args(args):
   options = None
   
   # If passed anything return None
   if len(args) > 1:
      return options
      
   else:
      return defaultdict(lambda: False)
   
def print_help():
   print "Usage <%s>:" % (__file__)
   print 
   print "Script also expects a input.json file in the same directory."
   print "Please see input.json for usage instructions."
   print

################################################################################
################################################################################

if __name__ == "__main__":
   options = parse_args(sys.argv)
   
   if options is None:
      print_help()
      sys.exit(1)
      
   test_env_file = None
   if g_is_windows:

      complus_vars = []

      for key in os.environ:
         if "complus" in key.lower():
            complus_vars.append(".".join([key, os.environ[key]]))
            os.environ[key] = ''
         elif "superpmi" in key.lower():
            print key

      test_env_file = create_config_file(complus_vars)

   file_handle = open("input.json")
   json_object = None
      
   # Exception will be raised if incorrect json
   try:
      json_object = json.load(file_handle)
   except:
      print "Incorrect json"
      raise
      
   host_os = None
   arch = None
   build_type = None
   
   if _platform == "linux" or _platform == "linux2":
      host_os = "Linux"
   elif _platform == "darwin":
      host_os = "OSX"
   elif _platform == "win32":
      host_os = "Windows_NT"
      
   try:
      arch = json_object["arch"]
      build_type = json_object["build_type"]
      
   except KeyError as name:
      print "Error, %s not defined, please define this in input.json" % (name)
      
   if g_is_windows:
      args = ["runtest.cmd", arch, build_type]
      if test_env_file is not None:
         args.append("TestEnv")
         args.append(test_env_file)

      print " ".join(args)
      print

      # Call runtests.cmd, everything should work.
      proc = subprocess.Popen(args)
      proc.communicate()

   else:
      try:
         test_root_dir = json_object["test_root_dir"]
         corefx_bin_dir = json_object["corefx_bin_dir"]
         corefx_native_bin_dir = json_object["corefx_native_bin_dir"]
         coreclr_repo_location = json_object["coreclr_dir"]
      
      except KeyError as name:
         print "Error, %s not defined, please define this in input.json" % (name)
         
      coreclr_bin = os.path.join(coreclr_repo_location, "bin")
      coreclr_bin_dir = "--%s=%s" % ("coreClrBinDir", os.path.join(coreclr_bin, "Product", host_os + "." + arch + "." + build_type))
      mscorlib_dir = "--%s=%s" % ("mscorlibDir", os.path.join(coreclr_bin, "Product", host_os + "." + arch + "." + build_type))
      test_root_dir = "--%s=%s" % ("testRootDir", test_root_dir)

      test_native_bin_dir = os.path.join(coreclr_bin, "obj", host_os + "." + arch + "." + build_type)
      if not os.path.isdir(test_native_bin_dir):
         print "path: %s not valid, are you running from coreclr/test?" % (test_native_bin_dir)

      test_native_bin_dir = "--%s=%s" % ("testNativeBinDir", test_native_bin_dir)
      corefx_bin_dir = "--%s=%s" % ("coreFxBinDir", corefx_bin_dir)
      corefx_native_bin_dir = "--%s=%s" % ("coreFxNativeBinDir", corefx_native_bin_dir)
         
      print "Starting test run."
      print
         
      args = ["sh", os.path.join(coreclr_repo_location, "tests", "runtest.sh"), test_root_dir, test_native_bin_dir, coreclr_bin_dir, mscorlib_dir, corefx_bin_dir, corefx_native_bin_dir]
      
      print " ".join(args[1:])
      print
      
      try:
         proc = subprocess.Popen(args)
         proc.communicate()
      except OSError as error:
         if error.errno == os.errno.ENOENT:
           # runtest.sh not found.
           print "Error, runtest.sh not in the current directory."
           print os.listdir(".")
           sys.exit(1)
         else:
            # Something else went wrong re-raise
            raise
