#! /usr/bin/env python
################################################################################
################################################################################
#
# Module coreclr_config_download.py 
#
# Notes:
#
# Simple script to grab all of the config.xml files for jobs.
#
################################################################################
################################################################################

import argparse
import datetime
import json
import multiprocessing
import os
import re
import subprocess
import sys
import shutil
import tempfile
import threading
import time
import urllib2
import zipfile

from collections import defaultdict
from multiprocessing import Process, Queue, Pipe

################################################################################
# Argument Parser
################################################################################

description = """Simple script to grab all of the config.xml files for jobs.

              Note that the api_token can either be the token or the path to
              a file containing the token.

              If you are having a problem with connecting try passing the, download
              basline only then download the diff only after waiting a little.

              Github will unforuntately rate limit OAuth connections.
              """

parser = argparse.ArgumentParser(description=description)

parser.add_argument("-api_token", dest="api_token", nargs='?', default=None, help="Github API Token. Can be the path to a file or string.")
parser.add_argument("-branch", dest="branch", nargs='?', default="")
parser.add_argument("-output_location", dest="output_location", nargs='?', default="output")
parser.add_argument("-sim_connections", dest="sim_connections", nargs='?', default=multiprocessing.cpu_count(), help="The amount of parrallel conntections to launch.")
parser.add_argument("-username", dest="username", nargs='?', default=None, help="Github username for the API Token.")

parser.add_argument("--baseline_only", dest="baseline_only", action="store_true", default=False, help="Download the baseline config files only.")
parser.add_argument("--diff_only", dest="diff_only", action="store_true", default=False, help="Download the diff config files only.")

################################################################################
# Classes
################################################################################

class HttpBasic403AuthorizationHandler(urllib2.HTTPBasicAuthHandler):
    """Basic authorization hander to help with jenkins authorization
    """
    
    def http_error_403(self, request, handle, code, message, headers):
        page = request.get_full_url()
        host = request.get_host()

        realm = None
        return self.retry_http_basic_auth(host, request, realm)

class AuthenticatWithGithubApiToken:
    """Class to abstract connecting to jenkins via an authenticated github token
    """

    def __init__(self, username, api_token):
        self.username = username
        self.api_token = api_token
        self.auth_handler = None

    def authenticate(self):
        """Authenticate when we receive a error 403 response.

        Args:
            None

        Returns:
            None

        Notes:
            Can only be called once.
        
        """

        # Make sure we are only authenticating once
        assert self.auth_handler is None

        # Create and install the 403 handler
        self.auth_handler = HttpBasic403AuthorizationHandler()
        opener = urllib2.build_opener(self.auth_handler)
        urllib2.install_opener(opener)

        # Note at this point the opener is installed. However,
        # there still needs to be an authentication

        url = "https://ci.dot.net"
        self.auth_handler.add_password(realm=None, uri=url, user=self.username, passwd=self.api_token)

        # Try a quick connection
        auth_url = "%s/configure" % url
        try:
            result = urllib2.urlopen(auth_url)
            assert result.code == 200

        except urllib2.HTTPError, error:
            assert error.code != 401, 'Error: api-token was rejected.'

            # Else this was a generic error related to sending the HttpRequest
            raise error

class Job:
    """Class to abstract a jenkins job
    """

    def __init__(self, job_name, job_url):
        self.job_name = job_name
        self.job_url = job_url

    def get_config_file(self, output_location):
        """ For a job get the config file.

        Args:
            None
        
        Returns:
            config_xml (str): config.xml contents.
        """

        location = os.path.join(output_location, "%s.xml" % self.job_name)

        # We may be redownloading because the download was interupted.
        # skip this file because we already have a copy.
        if os.path.isfile(location):
            print "Skipping: %s.xml. Item exists. If this is unexpected please delete the output folder." % self.job_name
            config_xml = None
            
            with open(location) as file_handle:
                config_xml = file_handle.read().strip()

            return config_xml

        url = "%sconfig.xml" % self.job_url

        connection = urllib2.urlopen(url)
        config_xml = connection.read()
        connection.close()

        return config_xml

    def write_config_file(self, output_location, config_xml):
        """ Write the config xml output for a job.

        Args:
            output_location (str): Must be a valid output folder

        Returns
            None
        
        Notes:

            The file will be named <job_name>.xml
        """

        assert os.path.isdir(output_location)
        path = os.path.join(output_location, "%s.xml" % self.job_name)

        with open(path, 'w') as file_handle:
            file_handle.write(config_xml)

################################################################################
# Helper Functions
################################################################################

def get_jobs_from_json(json_obj):
    """ Given a jenkins api json string return the list of jobs

    Args:

        json (json_obj): json returned from a jenkins api call.

    Returns:

        jobs ([Job]): jobs that jenkins currently has

    """

    job_list = json_obj["jobs"]

    invalid_job_folders = ["GenPRTest"]

    new_job_list = []
    for job in job_list:
        if "Folder" in job["_class"] and job["name"] not in invalid_job_folders:
            new_job_list += get_jobs_from_json(read_api("%sapi/json" % job["url"]))
        else:
            new_job_list.append(Job(job["name"], job["url"]))

    return new_job_list

def read_api(url):
    """ Given a valid jenkins api url read the json returned

    Args:

        url (str): url to read. Must be a valid jenkins api url
    
    Returns:

        json (json_obj): json read from the connection

    """

    connection = urllib2.urlopen(url)
    json_str = connection.read()
    connection.close()

    return json.loads(json_str)

def main(args):
    api_token = args.api_token
    branch = args.branch
    output_location = args.output_location
    sim_connections = args.sim_connections
    username = args.username
    
    baseline_only = args.baseline_only
    diff_only = args.diff_only

    valid_branches = ["master", 
                      "release_1.0.0", 
                      "release_1.1.0", 
                      "release_2.0.0", 
                      "dev_unix_test_workflow"]
    assert branch in valid_branches, "Error branch: %s is invalid." % branch
    assert username is not None, "Error username expected."
    assert api_token is not None, "Error a valid api token is required."
    assert (baseline_only and diff_only) is False, "Error, both baseline only and diff only cannot be set."

    if os.path.isfile(api_token):
        with open(api_token) as file_handle:
            api_token = file_handle.read().strip()
    
    if not os.path.isdir(output_location):
        os.mkdir(output_location)
    
    authenticator = AuthenticatWithGithubApiToken(username, api_token)
    authenticator.authenticate()

    step = int(sim_connections)
    old_output_location = os.path.join(output_location, "base")
    new_output_location = os.path.join(output_location, "diff")

    main_rest_url = "https://ci.dot.net/job/dotnet_coreclr/job/%s/api/json" % branch
    prtest_url = "https://ci.dot.net/job/dotnet_coreclr/job/%s/job/GenPRTest/api/json" % branch

    locations = [main_rest_url, prtest_url]
    outputs = [old_output_location, new_output_location]

    if baseline_only is True:
        locations = [main_rest_url]
        outputs = [old_output_location]
            
    elif diff_only is True:
        locations = [prtest_url]
        outputs = [new_output_location]

    for index, output_dir in enumerate(outputs):
        if not os.path.isdir(output_dir):
            os.mkdir(output_dir)

        jobs = get_jobs_from_json(read_api(locations[index]))

        def write_config_file(output_location, job):
            """Worker function for the multithreading
            """

            job.write_config_file(output_location, job.get_config_file(output_location))


        def join_threads():
            """ Simple method to join all threads
            """

            main_thread = threading.currentThread()
            for thread_handle in threading.enumerate():
                if thread_handle is main_thread:
                    continue
                
                thread_handle.join()

        for index, job in enumerate(jobs):

            print "Starting: %s [%d of %d]" % (job.job_name, index + 1, len(jobs))
            thread_handle = threading.Thread(target=write_config_file, args=(output_dir, job))
            thread_handle.setDaemon(True)
            thread_handle.start()

            # Join every step
            if index % step == 0:
                join_threads()

        join_threads()

################################################################################
# __main__ (entry point)
################################################################################

if __name__ == "__main__":
   main(parser.parse_args(sys.argv[1:]))