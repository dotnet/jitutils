#!/usr/bin/env python3
#
# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.
#
import argparse
import io
import os
import sys
import tarfile
from urllib import request
from urllib.error import URLError, HTTPError

Release_urls = {
  'llvmorg-13.0.1': {
      'linux': 'https://github.com/llvm/llvm-project/releases/download/llvmorg-13.0.1/clang+llvm-13.0.1-x86_64-linux-gnu-ubuntu-18.04.tar.xz',
      'macos': 'https://github.com/llvm/llvm-project/releases/download/llvmorg-13.0.1/clang+llvm-13.0.1-x86_64-apple-darwin.tar.xz'
  }
}

def download_llvm_release(release_url, output_dir):
    try:
        with request.urlopen(release_url) as response:
            downloaded_bytes = response.read()
    except (URLError, HTTPError) as err:
        print(err)
        sys.exit(1)

    with io.BytesIO(downloaded_bytes) as downloaded_fileobj:
        with tarfile.open(fileobj=downloaded_fileobj, mode='r:xz') as archive:
            def is_within_directory(directory, target):
                
                abs_directory = os.path.abspath(directory)
                abs_target = os.path.abspath(target)
            
                prefix = os.path.commonprefix([abs_directory, abs_target])
                
                return prefix == abs_directory
            
            def safe_extract(tar, path=".", members=None, *, numeric_owner=False):
            
                for member in tar.getmembers():
                    member_path = os.path.join(path, member.name)
                    if not is_within_directory(path, member_path):
                        raise Exception("Attempted Path Traversal in Tar File")
            
                tar.extractall(path, members, numeric_owner=numeric_owner) 
                
            
            safe_extract(archive, path=output_dir)

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('-release', required=True, choices=Release_urls.keys())
    parser.add_argument('-os', required=True, choices=['linux', 'macos'])
    parser.add_argument('-output-dir', dest='output_dir', default=os.getcwd())
    args = parser.parse_args()

    release_url = Release_urls[args.release][args.os]
    download_llvm_release(release_url, args.output_dir)
