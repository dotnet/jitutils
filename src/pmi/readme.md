# PMI - use Prepare Method (Instantiation) to jit all methods in an assembly

PMI uses reflection to locate all the types in an assembly and all methods
each type. Then it calls `PrepareMethod` on each method in turn.

This gives us the ability to look at the code the jit will generate for a large
number of methods without needing to have test cases that call the methods. So
it is very useful for doing widespread jit-time testing and analysis of jit
codegen.

The methods jitted are not called, so PMI is not as useful for finding bugs in
the jit-generated code.

This initial commit is a preliminary port of the PMI tool we have developed for
.Net Framework testing. Over time we'll improve it and adapt it better for use
with .Net Core.

Improvements to come:
* proper subprocess launching for core
* integrated support for alt jits and/or alternate codegen modes
* jitting of methods in generic types
* jitting of generic methods
* support for corelib
* (possibly) the ability to fetch code via nuget
* integration into jit-diffs

