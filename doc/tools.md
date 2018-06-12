# Other Tools

## pmi

`pmi` is a low-level tool for running the jit across the methods in an assembly.
It can be used as a component to create diffs or to simply test whether the jit
encounters any internal issues when jitting methods.
```
$pmi --help

Usage:

  pmi Count PATH_TO_ASSEMBLY
      Count the number of types and methods in an assembly.

  pmi PrepOne PATH_TO_ASSEMBLY INDEX_OF_TARGET_METHOD
      JIT a single method, specified by a method number.

  pmi PrepAll PATH_TO_ASSEMBLY [INDEX_OF_FIRST_METHOD_TO_PROCESS]
      JIT all the methods in an assembly. If INDEX_OF_FIRST_METHOD_TO_PROCESS
      is specified, it is the first method compiled, followed by all subsequent
      methods.

  pmi DriveAll PATH_TO_ASSEMBLY
      The same as PrepAll, but is more robust. While PrepAll will stop at the
      first JIT assert, DriveAll will continue by skipping that method.

Environment variable PMIPATH is a semicolon-separated list of paths used to find
dependent assemblies.

Use PrepAll-Quiet and PrepOne-Quiet if less verbose output is desired.
```
