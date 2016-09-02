#!/usr/bin/env python
################################################################################
################################################################################
#
# Module: collect_alltest.py
#
# Notes:
#  
# Simple script to automatically use superpmicollect.exe. This is a wrapper
# script to make collection and automation easy. In addition, it can be used
# to just run different things on linux/OSX without superPMI.
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

# Will return a dictionary with:
#
# dict["os"]
# dict["build_type"]
# dict["arch"]
# dict["log_path"]
#
def parse_args(args):
   options = None
   
   if len(args) == 1:
      return options
      
   options = defaultdict(lambda: None)
      
   if _platform == "linux" or _platform == "linux2":
      options["os"] = "Linux"
   elif _platform == "darwin":
      options["os"] = "OSX"
   elif _platform == "win32":
      options["os"] = "Windows_NT"
      
   for arg in args:
      if "Checked" in arg or "Debug" in arg or "Release" in arg:
         options["build_type"] = arg
      elif "arm" in arg or "arm64" in arg or "x86" in arg or "x64" in arg:
         options["arch"] = arg
      else:
         options["log_path"] = arg

   return options
   
def print_help():
   print "Usage <%s>:" % (__file__)
   print
   print "arch          :   arm, arm64, x86, x64"
   print "build_type    :   Debug, Checked, Release"
   print
   print "eg. to run a superPMI collection with x86:"
   print "%s x86 Checked" % (__file__)
   print

################################################################################
################################################################################

if __name__ == "__main__":
   options = parse_args(sys.argv)
   
   if options is None:
      print_help()
      sys.exit(1)
      
   host_os = None
   
   if _platform == "linux" or _platform == "linux2":
      host_os = "Linux"
   elif _platform == "darwin":
      host_os = "OSX"
   elif _platform == "win32":
      host_os = "Windows_NT"
      
   product_dir = os.path.join("..", "bin", "Product")
   test_dir = os.path.join("..", "bin", "tests")
   dir_name = host_os + "." + options["arch"] + "." + options["build_type"]
   
   log_path = None
   try:
      log_path = options["log_path"]
   
   except:
      pass

   bin_path = os.path.join(product_dir, dir_name)
   test_path = os.path.join(test_dir, dir_name)
   
   default_mch_file = os.path.join(test_path, dir_name + ".mch")
   print "Setting mch path to: %s" % (default_mch_file)
   print
   
   sys.stdout.write("Checking if SuperPMICollect is on the path.")
   try:
      proc = subprocess.Popen(["SuperPMICollect", "-h"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
      proc.communicate()
      print " -- Found"
   except OSError as error:
      if error.errno == os.errno.ENOENT:
         # SuperPMICollect collect not found.
         print " -- not Found"
         print "Error, SuperPMICollect collect not on the path. Please add it to the path."
         sys.exit(1)
      else:
         # Something else went wrong re-raise
         raise
         
   sys.stdout.write("Checking if runtest.py is on the path.")
   try:
      proc = subprocess.Popen(["python", "runtests.py", "-h"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
      proc.communicate()
      print " -- Found"
   except OSError as error:
      if error.errno == os.errno.ENOENT:
         # runtest.py not found.
         print " -- not Found"
         print "Error, runtest.py is not on the path. Please add it to the path."
         sys.exit(1)
      else:
         # Something else went wrong re-raise
         raise
    
   if g_is_windows:
      # CORE_ROOT is expected to be set.
      # copy over the directory from the bin directory
      
      core_root_dir = os.environ["CORE_ROOT"]
      if core_root_dir is None:
         print "Error, core root is expected to be set."
         sys.exit(1)

      args = ["ROBOCOPY", "/S", bin_path, core_root_dir]

      proc = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
      proc.communicate()


   args = ["SuperPMICollect", "-mch", default_mch_file]

   run_args = ["-run", "runtests.py"]
   
   if log_path is not None:
      args.append("-temp")
      args.append(log_path) 
     
   args = args + run_args

   print "Starting collection."
   print
   
   print " ".join(args)
   
   print
      
   proc = subprocess.Popen(args)
   proc.communicate()
