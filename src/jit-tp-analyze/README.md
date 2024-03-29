# jit-tp-analyze - throughput difference analysis tool

jit-tp-analyze is a utility to parse traces generated by PIN-based
instrumentation over runs of the JIT. The tool reads all lines in
the following format from the two input files:
```
<Exclusive instruction count> : <Method name>
```
The tool ignores all lines that do not match this pattern and so can be
run directly against superpmi.exe's usual output.

The tool produces the following summary:
```
Base: 1039322782, Diff: 1040078986, +0.0728%

`Compiler::optCopyPropPushDef'::`2'::<lambda_1>::operator()      : 1073512 : NA       : 18.17% : +0.1033%
SsaBuilder::RenamePushDef                                        : 911022  : NA       : 15.42% : +0.0877%
`Compiler::fgValueNumberLocalStore'::`2'::<lambda_1>::operator() : 584435  : NA       : 9.89%  : +0.0562%
Compiler::lvaLclExactSize                                        : 244692  : +60.09%  : 4.14%  : +0.0235%
ValueNumStore::VNForMapSelectWork                                : 87006   : +2.78%   : 1.47%  : +0.0084%
GenTree::DefinesLocal                                            : 82633   : +1.63%   : 1.40%  : +0.0080%
Rationalizer::DoPhase                                            : -91104  : -6.36%   : 1.54%  : -0.0088%
Compiler::gtCallGetDefinedRetBufLclAddr                          : -115926 : -98.78%  : 1.96%  : -0.0112%
Compiler::optBlockCopyProp                                       : -272450 : -5.75%   : 4.61%  : -0.0262%
Compiler::fgValueNumberLocalStore                                : -313540 : -50.82%  : 5.31%  : -0.0302%
Compiler::GetSsaNumForLocalVarDef                                : -322826 : -100.00% : 5.46%  : -0.0311%
SsaBuilder::RenameDef                                            : -478441 : -28.33%  : 8.10%  : -0.0460%
Compiler::optCopyPropPushDef                                     : -711380 : -55.34%  : 12.04% : -0.0684%
```
The columns, in order:
1. Method name.
2. The instruction count difference for the given function.
3. Same as `1`, but relative. May be `NA`, indicating the base didn't contain the given function, or `-100%` indicating the diff didn't.
4. Relative contribution to the diff. Calculated as `abs(instruction diff count) / sum-over-all-functions(abs(instruction diff count))`.
5. Relative difference, calculated as `instruction diff count / total base instruction count`.

To use:
1. Obtain the base and diff traces, by compiling and running a PIN tool that counts instructions retired for each function.
2. Invoke `./jit-tp-analyze --base base-trace.txt --diff diff-trace.txt`.

For convenience, both arguments have default values: `basetp.txt` for `--base`, `difftp.txt` for `--diff`, and so can be omitted.

By default, the tool will hide functions that contributed less than `0.1%` to the difference. You can change this value with the `--noise` argument.
