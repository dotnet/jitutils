# jit-analyze - Managed CodeGen difference analysis tool

jit-analyze is a utility to provide feedback on generated disassembly.
The tool will produce the total bytes of difference and list of files 
and methods sorted by contribution (size in bytes of regression/improvement)

To build/setup:

* Download dotnet cli.  Follow install instructions and get dotnet on your
  your path.
* Follow publish directions for the mcgutils repo in the root.  This will 
  put the tools on your path.
* Generate corediff disasm run.  See [Getting Started](../../doc/getstarted.md) 
  for directions how.
* Run analyze --base `<base path>` --diff `<diff path>` to produce a summary of the 
  differences.
  
The output of analyze looks like the following:
```
$ jit-analyze --base ~/Work/output/base --diff ~/Work/output/diff

(Note: Lower is better)

Total bytes of diff: -4124
    diff is an improvement.

Top file regressions by size (bytes):
    193 : Microsoft.CodeAnalysis.dasm
    154 : System.Dynamic.Runtime.dasm
    60 : System.IO.Compression.dasm
    43 : System.Net.Security.dasm
    43 : System.Xml.ReaderWriter.dasm

Top file improvements by size (bytes):
    -1804 : mscorlib.dasm
    -1532 : Microsoft.CodeAnalysis.CSharp.dasm
    -726 : System.Xml.XmlDocument.dasm
    -284 : System.Linq.Expressions.dasm
    -239 : System.Net.Http.dasm

21 total files with diffs.

Top method regessions by size (bytes):
    328 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax.DocumentationCommentXmlTokens:.cctor()
    266 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.MethodTypeInferrer:Fix(int,byref):bool:this
    194 : mscorlib.dasm - System.DefaultBinder:BindToMethod(int,ref,byref,ref,ref,ref,byref):ref:this
    187 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax.LanguageParser:ParseModifiers(ref):this
    163 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Symbols.SourceAssemblySymbol:DecodeWellKnownAttribute(byref,int,bool):this

Top method improvements by size (bytes):
    -160 : System.Xml.XmlDocument.dasm - System.Xml.XmlTextWriter:AutoComplete(int):this
    -124 : System.Xml.XmlDocument.dasm - System.Xml.XmlTextWriter:WriteEndStartTag(bool):this
    -110 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.MemberSemanticModel:GetEnclosingBinder(ref,int):ref:this
    -95 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.CSharpDataFlowAnalysis:AnalyzeReadWrite():this
    -85 : Microsoft.CodeAnalysis.CSharp.dasm - Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax.LanguageParser:ParseForStatement():ref:this

3762 total methods with diffs
```