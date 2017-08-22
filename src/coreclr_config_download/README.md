**CoreClr Config Download**

Download all of the config.xml files for each coreclr job. This will will download into two different folders <output_location>/base and <output_location/diff>.

You can then diff the two folders with whatever your favorite diff tool is. An example on unix would be:

>$diff -rq <output_location>/base <output_location>/diff

**Requirements**

In order to grab the config.xml files you will need to authenticate with jenkins and Github. This requires an OAuth token.

Note that you are able to do this both from the command line (see [these](https://help.github.com/articles/creating-a-personal-access-token-for-the-command-line/) instructions) and using the web api [here](https://github.com/settings/tokens).

The minimum access you need is the user information.

**Usage**

Please use -h to see the full help. 

```
usage: coreclr_config_download.py [-h] [-api_token [API_TOKEN]]
                                  [-branch [BRANCH]]
                                  [-output_location [OUTPUT_LOCATION]]
                                  [-sim_connections [SIM_CONNECTIONS]]
                                  [-username [USERNAME]] [--baseline_only]
                                  [--diff_only]

Simple script to grab all of the config.xml files for jobs. Note that the
api_token can either be the token or the path to a file containing the token.
If you are having a problem with connecting try passing the, download basline
only then download the diff only after waiting a little. Github will
unforuntately rate limit OAuth connections.

optional arguments:
  -h, --help            show this help message and exit
  -api_token [API_TOKEN]
                        Github API Token. Can be the path to a file or string.
  -branch [BRANCH]
  -output_location [OUTPUT_LOCATION]
  -sim_connections [SIM_CONNECTIONS]
                        The amount of parrallel conntections to launch.
  -username [USERNAME]  Github username for the API Token.
  --baseline_only       Download the baseline config files only.
  --diff_only           Download the diff config files only.
  ```

**Example usage**

>$./coreclr_config_download.py -branch master -output_location ~/output -username jashook -api_token ~/jenkins_api_token

>$./coreclr_config_download.py -branch master -output_location ~/output -username jashook -api_token ~/jenkins_api_token --baseline_only

>$./coreclr_config_download.py -branch master -output_location ~/output -username jashook -api_token ~/jenkins_api_token --diff_only

**Caveats**

Each GET Request to download the config.xml must authenticate. Therefore, after around 2k GET Requests Github will start rate limiting the requests. From what it looks like it will require you to wait about an hour before being able to authenticate you from jenkins.

To work around this annoying problem the script has a --baseline_only and --diff_only option. Which will allow you to download the --baseline_only, wait then download --diff_only.