# clrjit pintool

This directory contains the source code of a pintool that can be used with
[PIN](https://www.intel.com/content/www/us/en/developer/articles/tool/pin-a-dynamic-binary-instrumentation-tool.html)
to measure throughput of the JIT. The pintool counts the number of instructions
executed inside the JIT only. Furthermore it has some special support to
integrate with SuperPMI's metric collection to allow support for diffing
throughput.

## Building
The easiest way to build it is to follow PIN's manual and adding the pintool
here as another example. See the "Building the Example Tools" and "Building Your
Own Tool" sections in the manual.
Note that this requires cygwin on Windows.
