#!/usr/bin/env python
################################################################################
################################################################################
#
# Module: superpmi-blob.py
#
# Notes:
#
# Python script to facilitate uploading and downloading superpmi mch files
# from Azure Blob Storage
#
################################################################################
################################################################################

import argparse
import json
import os as _os
import sys
import zipfile

from azure.storage.blob import BlockBlobService
from azure.storage.blob import ContentSettings

from collections import defaultdict

################################################################################
# Globals
################################################################################

# To add a new supported arch or os, add it to this map.
valid_configs = defaultdict(lambda: None, {
   "osx": ["x64"],
   "ubuntu": ["x64"],
   "windows": ["x64", "x86"]
})

account = "clrjit"
key = "O+wtoQ+DUZM1FWy1kKgTpkXKRBWCYWwCGjAxsYXObVDrHAUpPyVF+KosqiKum5e0Mp1/40e0J3eptDNyAnnzvA=="

block_blob_service = BlockBlobService(account_name=account, account_key=key)

################################################################################
# Argument parser
################################################################################

description = """Python script to facilitate uploading and downloading 
                 superpmi mch files from Azure Blob Storage.
              """

parser = argparse.ArgumentParser(description=description)

parser.add_argument("--download", dest="download", action="store_true", default=False)
parser.add_argument("--upload", dest="upload", action="store_true", default=False)
parser.add_argument("--setup", dest="setup", action="store_true", default=False)
parser.add_argument("--clear-blobs", dest="clear_blobs", action="store_true", default=False)
parser.add_argument("--get-uploads", dest="get_uploads", action="store_true", default=False)

parser.add_argument("-arch", dest="arch", nargs='?', default=None)
parser.add_argument("-os", dest="os", nargs='?', default=None)
parser.add_argument("-build_type", dest="build_type", nargs='?', default=None)
parser.add_argument("-hash", dest="hash", nargs='?', default=None)
parser.add_argument("-core_root", dest="core_root", nargs='?', default=None)
parser.add_argument("-mch_files", dest="mch_files", nargs='*')

################################################################################
# Helper Functions
################################################################################

def validate_args(args):
   """Validate all of the arguments passed to the program
   
   If the program does not validate the arguments then this function will exit
   the program with the errorcode 1. Note after this function is called the
   following assuptions are guarenteed.

   1) The variables args.download and args.upload will not both be False or both
      True.

   2) If upload is True, the amount of mch files passed to the program to upload
      can be assumed to be non-zero.

   Args:

      args (obj: argparse): argparse object

   Returns:

      None

   """
   def validate(expr, msg):
      """Validate a given expression

      Pass a lambda which returns True or False and a message to print if the 
      expression does not validate.

      Args:

         expr (lambda: True|False): expression to validate
         msg (str): message to print on error

      Returns:

         None

      """
      
      try:
         assert expr()
      except:
         print msg
         sys.exit(1)

   if args.setup is True or args.clear_blobs is True:
      return

   if args.get_uploads is True:
      validate (lambda: args.get_uploads != args.download and
                        args.get_uploads != args.upload,
                "Error, download or upload cannot be set when getting the "
                "current superpmi uploads.")

      validate (lambda: args.os != None and args.arch != None,
                "Error. The arch and os must both be passed.")
      
      validate(lambda: valid_configs[args.os] is not None and
                       args.arch in valid_configs[args.os],
               "The os, or the arch is not valid. Please double check input. "
               "Valid inputs are:\n\n "
               "Operating Systems %s" % (json.dumps(valid_configs, indent=4)))

      return

   # Download and upload cannot both be true and cannot both be false.
   validate(lambda: args.download != args.upload,
            "Error. Either download or upload must be specified. "
            "Having both specified is not permitted.")

   if args.upload is True:
      # Uploading requires an os, arch, build type and a hash.
      validate(lambda: args.arch is not None and 
                       args.os is not None and 
                       args.build_type is not None and
                       args.core_root is not None and
                       args.hash is not "",
               "To upload, the os, arch, build type, core_root and hash are required.")

      validate(lambda: valid_configs[args.os] is not None and
                       args.arch in valid_configs[args.os],
               "The os, or the arch is not valid. Please double check input. "
               "Valid inputs are:\n\n "
               "Operating Systems %s" % (json.dumps(valid_configs, indent=4)))

      # When uploading there must be a non zero amount of mch files specified
      validate(lambda: args.mch_files != None and len(args.mch_files) != 0,
               "Upload is specified, but there were no mch files passed. "
               "To specify which mch files to upload pass -mch_files a b c ...")

   elif args.download is True:
      # Downloading requires an os, arch, build type and a hash.
      validate(lambda: args.arch is not None and 
                       args.os is not None and 
                       args.build_type is not None and
                       args.core_root is not None and
                       args.hash is not "",
               "To download, the os, arch, build type, core_root and hash are required.")

      validate(lambda: valid_configs[args.os] is not None and
                       args.arch in valid_configs[args.os],
               "The os, or the arch is not valid. Please double check input. "
               "Valid inputs are:\n\n "
               "Operating Systems %s" % (json.dumps(valid_configs, indent=4)))

################################################################################
# General Helper Function
################################################################################

def zip_dir(path, file_handle):
   """Zip a directory

   Zip the directory passed to the function

   Args:

      path (str): path to the directory to zip

   Output:

      None

   """

   for file in _os.listdir(path):
      if _os.path.isdir(_os.path.join(path, file)):
         zip_dir(_os.path.join(path, file), file_handle)

      else:
         file_handle.write(_os.path.join(path, file))

################################################################################
# Azure Helper Functions
################################################################################

def clear_blobs():
   """Clear all of the blobs

   Clear all of the blobs in blob storage.

   Args:
      None

   Return:
      None

   """

   uploads = get_uploads()

   for item in uploads:
      block_blob_service.delete_blob("spmi", item)

def get_uploads():
   """Get all of the uploads currently in blob storage for a certain os and arch

   For a given arch and os return all the list of hashes in upload order that
   are currently stored in blob storage.

   Args:
      None

   Returns:
      [str]: list of objects stored in blob storage for a given arch and os

   """

   blobs = block_blob_service.list_blobs("spmi")

   return [blob.name for blob in blobs]

def setup():
   """Setup the blob storage account with a superpmi container.

   Create the superpmi container. Assume the container does not already exist.

   Args:
      None

   Returns:
      None

   """

   block_blob_service.create_container('spmi')

def upload(hash, os, arch, build_type, mch_files, core_root):
   """Upload a set of mch files to blob storage.

   Upload the mch_files to the following layout:

   |--Git Hash
             |--OS
                 |--Arch
                        |-- MCH
                              |--Mchfile1.zip
                              |--Mchfile2.zip
                              |-- ...
                        |--MCH_ASM
                                 |--Mchfile1.zip
                                 |--MchFile2.zip
                                 |-- ...
                        |--CORE_ROOT.zip

   Args:

      hash (str)        : Git hash for the mchs
      os (str)          : Operating system run on
      arch (str)        : Arch of the run
      build_type (str)  : Build type of the run
      mch_files ([str]) : List of mch files to upload

   Returns:

      None

   """

   for mch_file in mch_files:
      if _os.path.isfile(mch_file):
         filename, file_extension = _os.path.splitext(mch_file)

         print "zip %s" % (filename + ".zip") 
         with zipfile.ZipFile(filename + ".zip", 'w', zipfile.ZIP_DEFLATED) as file_handle:
            file_handle.write(mch_file)

         blob_name = "/".join([hash, os, arch, "MCH", _os.path.basename(filename + ".zip")])

         print "Uploading %s to %s" % (filename + ".zip", blob_name)
         block_blob_service.create_blob_from_path("spmi",
                                                  blob_name,
                                                  filename + ".zip")

         print

   print "zip CORE_ROOT"
   with zipfile.ZipFile("core_root.zip", 'w', zipfile.ZIP_DEFLATED) as file_handle:
      # Upload the core root
      zip_dir(core_root, file_handle)

   blob_name = "/".join([hash, os, arch, "core_root.zip"])
   dir_name = "core_root.zip"

   print "Upload %s to %s" % ("core_root.zip", blob_name)
   block_blob_service.create_blob_from_path("spmi",
                                            blob_name,
                                            dir_name)
 
   print

################################################################################
# Main
################################################################################

def main(args):
   args = parser.parse_args(args)

   args.arch = args.arch.lower() if args.arch is not None else None
   args.build_type = args.build_type.lower() if args.build_type is not None else None
   args.os = args.os.lower() if args.os is not None else None
   args.hash = args.hash.lower() if args.hash is not None else None

   validate_args(args)

   download = args.download
   mch_files = args.mch_files
   do_setup = args.setup
   do_get_uploads = args.get_uploads
   arch = args.arch
   build_type = args.build_type
   os = args.os
   hash = args.hash
   do_clear_blobs = args.clear_blobs
   core_root = args.core_root

   if do_setup:
      setup()
      return

   elif do_clear_blobs:
      clear_blobs()
      return

   elif do_get_uploads:
      uploads = get_uploads()

      for item in uploads:
         print item

   elif download:
      raise NotImplementedError

   # Upload
   else:
      upload(hash, os, arch, build_type, mch_files, core_root)

################################################################################
# Entry Point
################################################################################

if __name__ == "__main__":
   main(sys.argv[1:])