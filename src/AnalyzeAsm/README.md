Notes:

* The code in `Program.cs` has **lot of redundant code**. Most of the methods are copied from previous methods with little tweak. 
* All the methods rely on `ngen_arm64.txt` / `ngen_amd64.txt` file that are produced by doing the following:
  * `set COMPlus_NGenDisasm=1`
  * Running `build-test.cmd crossgen > ngen_arm64.txt` 
* The path locations of these files are hardcoded too. 


`FindLdrGroups_1()` finds patterns:

```asm
ldr x1, [x0]
ldr x2, [x0, #8]
; becomes
ldp x1, x2 [x0]
```

`FindLdrGroups_2()` finds patterns:
```asm
ldr x1, [fp]
ldr x2, [fp, #8]
; becomes
ldp x1, x2 [fp]
```

`FindStrGroups_1()` finds patterns:
```asm
str x1, [x0]
str x2, [x0, #8]
; becomes
stp x1, x2, [x0]
```

`FindStrGroups_2()` finds patterns:
```asm
str x1, [fp]
str x2, [fp, #8]
; becomes
stp x1, x2, [fp]
```

`FindStrGroups_wzr()` finds patterns:
```asm
str wzr, [x1]
str wzr, [x1, #8]
; becomes
str xzr, [x1]
```

`FindLdrLdrToMovGroups()` finds patterns:
```asm
add x0, x0, x1
ldr x1, [x0]
; becomes
ldr x1, [x0, x1]
```

`FindPostIndexAddrMode1()` finds patterns:
```asm
ldr x0, [x2]
add x2, x2, #4
; becomes
ldr x0, [x2], #4
```

`FindPreIndexAddrMode1()` finds patterns:
```asm
ldr x0, [x2, #4]
add x2, x2, #4
;becomes
ldr x0, [x2, #4]!
```

`FindPreIndexAddrMode2()` finds patterns:
```asm
add x2, x2, #4
ldr x0, [x2, #4]
;becomes
ldr x0, [x2, #4]!
```

`RedundantMovs1()` finds patterns:
```asm
mov x0, x0
```

`RedundantMovs2()` finds patterns:
```asm
mov x0, x20
mov x20, x0
```

`RedundantMovs3()` finds patterns:
```asm
ldr w0, [x0] ; <-- this should zero extend the register so next mov is not needed.
mov w0, w0
```

`AdrpAddPairs()` finds patterns:
```asm
adrp    x11, [RELOC #0x1f40fb92b00]
add     x11, x11, #0
adrp    x0, [RELOC #0x1f40fb92b00]
add     x0, x0, #0
ldr     x0, [x0]
```

`ArrayAccess()` finds patterns:
```asm
sxtw    x4, x4
lsl     x4, x4, #3
add     x4, x4, #16
```

`PrologEpilogInx64()` finds methods that don't have prolog/epilog in x64

`BasePlusRegisterOffset()` finds patterns:
```asm
ldr     w11, [x11, x1, LSL #2]
```

`FindMovZMovKGroups()` finds groups of `movz/movk` instructions.

`OptimizeDmbs` finds patterns to remove unnecessary `dmb` instructions. Motivation from clang's [ARMOptimizeBarriersPass](https://github.com/llvm/llvm-project/blob/2946cd701067404b99c39fb29dc9c74bd7193eb3/llvm/lib/Target/ARM/ARMOptimizeBarriersPass.cpp).