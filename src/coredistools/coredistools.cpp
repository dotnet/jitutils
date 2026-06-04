//===-------- coredistools.cpp - Disassembly tools for CoreClr ------------===//
//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// See LICENSE file in the project root for full license information.
//
//===----------------------------------------------------------------------===//
///
/// \file
/// \brief Implementation of Disassembly Tools API for AOT/JIT
///
//===----------------------------------------------------------------------===//

#include "llvm/MC/MCAsmInfo.h"
#include "llvm/MC/MCContext.h"
#include "llvm/MC/MCDisassembler/MCDisassembler.h"
#include "llvm/MC/MCInst.h"
#include "llvm/MC/MCInstPrinter.h"
#include "llvm/MC/MCInstrInfo.h"
#include "llvm/MC/MCObjectFileInfo.h"
#include "llvm/MC/MCRegisterInfo.h"
#include "llvm/MC/MCSubtargetInfo.h"
#include "llvm/MC/TargetRegistry.h"
#include "llvm/TargetParser/Host.h"
#include "llvm/Support/Format.h"
#include "llvm/Support/ManagedStatic.h"
#include "llvm/Support/PrettyStackTrace.h"
#include "llvm/Support/SourceMgr.h"
#include "llvm/Support/TargetSelect.h"
#include "llvm/Support/raw_ostream.h"
#include "llvm/Support/DataTypes.h"
#include <inttypes.h>
#include <stdarg.h>

#define DllInterfaceExporter
#include "coredistools.h"

using namespace llvm;
using namespace std;

class BlockIterator;

// Represents a Code block
class BlockInfo {
public:
  BlockInfo(const uint8_t *Pointer, uint64_t Size, uintptr_t Address,
            const char *BlockName = "")
      : Ptr(Pointer), BlockSize(Size), Addr(Address), Name(BlockName) {}

  bool isEmpty() const { return BlockSize == 0; }

  // A pointer to the code block to disassemble.
  const uint8_t *Ptr;

  // The size of the code block to compare.
  uint64_t BlockSize;

  // The original base address of the code block.
  uintptr_t Addr;

  // An identifying string, debug output only
  const char *Name;
};

// A block iterator represents a code-point within a code block.
// It represents an instruction within the block, and can move
// forward to subsequent instructions via the advance() method.
// The iterator can be in two modes:
//  Not-Decoded: Before DecodeInstruction() is called at this
//               code point
//  Decoded: After the current instruction is Decoded. In this state,
//           Inst is a valid MCInst and InstrSize is an actual non-zero
//           length of the current instruction.
class BlockIterator : public BlockInfo {
public:
  BlockIterator(const BlockInfo &Block)
      : BlockInfo(Block), Inst(), InstrSize(0), BlockStartAddr(Block.Addr) {}
  BlockIterator(const uint8_t *Pointer, uint64_t Size, uintptr_t Address = 0,
                const char *BlockName = "")
      : BlockInfo(Pointer, Size, Address, BlockName), Inst(), InstrSize(0),
        BlockStartAddr(Address) {}

  void advance() {
    assert(isDecoded() && "Cannot advance before Decode");
    assert(InstrSize <= BlockSize && "Overflow");
    Ptr += InstrSize;
    Addr += InstrSize;
    BlockSize -= InstrSize;

    // Next instruction is not yet decoded
    InstrSize = 0;
  }

  bool isDecoded() const { return (InstrSize != 0); }
  bool isBitwiseEqual(const BlockIterator &BIter) const;

  // Offset of this iterator (Instruction) wrt the beginning
  // of the Block (given at construction time)
  size_t BlockOffset() const { return Addr - BlockStartAddr; }

  // The machine instruction at this code point, after decode.
  MCInst Inst;

  // Why store the InstrSize separately, rather than obtaining
  // it from Inst.Size()? This is because of a limitation in
  // MCInst representation on certain architectures.
  // Prefix instructions on X64 such as Lock prefix are
  // encoded as an MCInst of zero size! When such a prefix is
  // decoded, the actual decode InstrSize=1, but Inst.Size()=0
  uint64_t InstrSize;

  // The original base address of the beginning of the code block
  // into which this Iterator has indexed.
  const uintptr_t BlockStartAddr;
};

// Default Print Controls
//
// The default controls simply print to stdout and stderr.
// Unfortunately, some of the CoreDisTools' clients expect the
// print messages without a trailing newline -- because the
// printing functions append the newline themselves.
// Therefore all messages are generated without the trailing
// newline. Consequently, the default printers add a newline
// at the end of the message.

void StdOut(const char *msg, ...) {
  va_list argList;
  va_start(argList, msg);
  string message = msg;
  message += "\n";
  vprintf(message.c_str(), argList);
  va_end(argList);
}
void StdErr(const char *msg, ...) {
  va_list argList;
  va_start(argList, msg);
  string message = msg;
  message += "\n";
  vfprintf(stderr, message.c_str(), argList);
  va_end(argList);
}
const PrintControl DefaultPrintControl = {StdErr, StdErr, StdOut, StdOut};

string outputBuffer;
raw_string_ostream outputStream (outputBuffer);
void BufferedOut(const char *msg, ...) {
  va_list argList;
  va_start(argList, msg);
  string message = msg;
  message += "\n";
  size_t size = vsnprintf( nullptr, 0, message.c_str(), argList ) + 1; // Extra space for '\0'
  unique_ptr<char[]> buf( new char[ size ] );
  vsnprintf( buf.get(), size, message.c_str(), argList );
  outputStream << buf.get();
  outputStream.flush();
  va_end(argList);
}
const PrintControl BufferedPrintControl = {StdErr, StdErr, StdOut, BufferedOut};

// Default Compare Controls

bool DefaultEqualityComparator(const void *UserData, size_t BlockOffset,
                               size_t InstructionLength, uint64_t Offset1,
                               uint64_t Offset2) {
  return Offset1 == Offset2;
}

// ULEB128 helpers used to walk Wasm framed code buffers.
//
// readULEB128:
//   On success, stores the decoded value in *Result, returns the number of
//   consumed bytes, and advances *Cursor past the consumed bytes.
//   On failure (buffer exhausted, or value would exceed 10 bytes), returns
//   0 and leaves *Cursor untouched.
static unsigned readULEB128(const uint8_t **Cursor, const uint8_t *End,
                            uint64_t *Result) {
  const uint8_t *P = *Cursor;
  uint64_t Value = 0;
  unsigned Shift = 0;
  unsigned BytesConsumed = 0;

  // ULEB128 is at most 10 bytes for a 64-bit value.
  while (P < End && BytesConsumed < 10) {
    uint8_t B = *P++;
    Value |= (uint64_t)(B & 0x7F) << Shift;
    BytesConsumed++;
    if ((B & 0x80) == 0) {
      *Result = Value;
      *Cursor = P;
      return BytesConsumed;
    }
    Shift += 7;
  }

  return 0;
}

// Instruction-wise disassembler helper.
// This utility is used to implement GcStress in CoreCLr
// Adapted from LLVM-objdump

struct CorDisasm {
public:
  CorDisasm(enum TargetArch Target,
            const PrintControl *PControl = &DefaultPrintControl)
      : TheTargetArch(Target), Print(PControl) {}

  bool init();
  bool decodeInstruction(BlockIterator &BIter, bool MayFail = false) const;
  uint64_t disasmInstruction(BlockIterator &BIter, bool DumpAsm = false) const;
  void dumpInstruction(const BlockIterator &BIter) const;
  void dumpBlock(const BlockInfo &Block) const;

  // Wasm framed-buffer helpers (see coredistools.h for the framing layout).
  // dumpWasmFramedBlock walks the framed buffer and prints each body as
  // `.wat`-style assembly. Returns false on a malformed buffer (bad ULEB128,
  // truncated body, or a body that fails to fully disassemble).
  bool dumpWasmFramedBlock(const BlockInfo &Block) const;

  // Parses one body's locals declaration at *Cursor (which must point at the
  // first byte of the body, i.e. immediately after the body-length prefix),
  // advancing *Cursor past the locals header. On success returns true and
  // writes the parsed group count to *NumLocalGroups (which must not be
  // null). Returns false on malformed input (bad ULEB128 or truncated body);
  // *Cursor is left in an unspecified state in that case. Emits a Log
  // warning via Print if an unrecognized valtype byte is encountered, but
  // still returns true (an unknown valtype consumes one byte just like a
  // known one, so the stream remains parseable). Does not render the
  // locals -- the dump path uses formatWasmLocals for that.
  bool parseWasmLocals(const uint8_t **Cursor, const uint8_t *BodyEnd,
                       uint64_t *NumLocalGroups) const;

  enum TargetArch getTargetArch() const { return TheTargetArch; }

protected:
  enum TargetArch TheTargetArch;
  const PrintControl *Print;

private:
  bool setTarget();

  string TargetTriple;
  unique_ptr<Triple> TheTriple;
  const Target *TheTarget;

  unique_ptr<MCRegisterInfo> MRI;
  unique_ptr<const MCAsmInfo> AsmInfo;
  unique_ptr<const MCSubtargetInfo> STI;
  unique_ptr<const MCInstrInfo> MII;
  unique_ptr<MCContext> Ctx;
  unique_ptr<MCDisassembler> Disassembler;
  unique_ptr<MCInstPrinter> IP;

  // LLVM's MCInst does not expose Opcode enumerations by design.
  // The following enumeration is a hack to use X86 opcode numbers,
  // until bug 7709 is fixed.
  struct OpcodeMap {
    const char *Name;
    uint8_t MachineOpcode;
  };

  static const int X86NumPrefixes = 19;
  static const OpcodeMap X86Prefix[X86NumPrefixes];
};

struct CorAsmDiff : public CorDisasm {
public:
  CorAsmDiff(enum TargetArch Target,
             const PrintControl *PControl = &DefaultPrintControl,
             const OffsetComparator Comp = DefaultEqualityComparator,
             const OffsetMunger Munge = nullptr)
      : CorDisasm(Target, PControl), Comparator(Comp), Munger(Munge) {}

  bool nearDiff(const BlockInfo &LeftBlock, const BlockInfo &RightBlock,
                const void *UserData) const;

  // Wasm framed-buffer differ. Walks both buffers as length-prefixed bodies
  // and compares opcodes / operands per body. The OffsetComparator is invoked
  // for differing integer immediates with BlockOffset set to the opcode byte
  // offset measured from the start of the framed buffer (i.e. the same key
  // the JIT-side recorded reloc tables use).
  bool nearDiffWasmFramed(const BlockInfo &LeftBlock,
                          const BlockInfo &RightBlock,
                          const void *UserData) const;

  // Pretty-print both framed buffers, one body at a time, prefixed with a
  // "(baseline)" / "(diff)" label per body so a human can spot which body
  // differs at a glance.
  void dumpDiffWasmFramed(const BlockInfo &LeftBlock,
                          const BlockInfo &RightBlock) const;

private:
  bool fail(const char *Mesg, const BlockIterator &Left,
            const BlockIterator &Right) const;

  // Operand-by-operand compare of two already-decoded MCInsts. Used by
  // nearDiffWasmFramed; the native nearDiff has an inlined copy of the same
  // logic that we deliberately do not share to keep the hot native path
  // untouched.
  bool compareWasmInstOperands(const MCInst &InstL, const MCInst &InstR,
                               size_t BlockOffset, size_t InstrLen,
                               const void *UserData) const;

  OffsetComparator Comparator;
  OffsetMunger Munger;
};

// clang-format off
CorDisasm::OpcodeMap const CorDisasm::X86Prefix[CorDisasm::X86NumPrefixes] = {
  { "LOCK",           0xF0 },
  { "REPNE/XACQUIRE", 0xF2 }, // Both the (TSX/normal) instrs 
  { "REP/XRELEASE",   0xF3 }, // have the same byte encoding 
  { "OP_OVR",         0x66 },
  { "CS_OVR",         0x2E },
  { "DS_OVR",         0x3E },
  { "ES_OVR",         0x26 },
  { "FS_OVR",         0x64 },
  { "GS_OVR",         0x65 },
  { "SS_OVR",         0x36 },
  { "ADDR_OVR",       0x67 },
  { "REX64W",         0x48 },
  { "REX64WB",        0x49 },
  { "REX64WX",        0x4A },
  { "REX64WXB",       0x4B },
  { "REX64WR",        0x4C },
  { "REX64WRB",       0x4D },
  { "REX64WRX",       0x4E },
  { "REX64WRXB",      0x4F }
};
// clang-format on

#if !defined(_MSC_VER)
// Disable "warning: default label in switch which covers all enumeration values [-Wcovered-switch-default]"
#pragma clang diagnostic ignored "-Wcovered-switch-default"
#endif

bool CorDisasm::setTarget() {
  // Figure out the target triple.

  TargetTriple = sys::getDefaultTargetTriple();
  TargetTriple = Triple::normalize(TargetTriple);
  TheTriple.reset(new Triple(TargetTriple));

  switch (TheTargetArch) {
  case Target_Host:
    switch (TheTriple->getArch()) {
    case Triple::x86:
      TheTargetArch = Target_X86;
      break;
    case Triple::x86_64:
      TheTargetArch = Target_X64;
      break;
    case Triple::thumb:
      TheTargetArch = Target_Thumb;
      break;
    case Triple::aarch64:
      TheTargetArch = Target_Arm64;
      break;
    case Triple::loongarch64:
      TheTargetArch = Target_LoongArch64;
      break;
    case Triple::riscv64:
      TheTargetArch = Target_RiscV64;
      break;
    case Triple::wasm32:
      TheTargetArch = Target_Wasm32;
      break;
    default:
      Print->Error("Unsupported Architecture: %s\n",
                   Triple::getArchTypeName(TheTriple->getArch()));
      return false;
    }
    break;

  case Target_Thumb:
    // TODO: Use TheTriple.setArch(Triple::thumb, Triple::ARMSubArch_v7) when the API becomes publicly available.
    TheTriple->setArchName("thumbv7");
    break;
  case Target_Arm64:
    TheTriple->setArch(Triple::aarch64);
    break;
  case Target_X86:
    TheTriple->setArch(Triple::x86);
    break;
  case Target_X64:
    TheTriple->setArch(Triple::x86_64);
    break;
  case Target_LoongArch64:
    TheTriple->setArch(Triple::loongarch64);
    break;
  case Target_RiscV64:
    TheTriple->setArch(Triple::riscv64);
    break;
  case Target_Wasm32:
    TheTriple->setArch(Triple::wasm32);
    break;
  default:
    Print->Error("Unsupported Architecture: %s\n",
                 Triple::getArchTypeName(TheTriple->getArch()));
    return false;
  }

  assert(TheTargetArch != Target_Host && "Target Expected to be specific");

  // Get the target specific parser.
  string Error;
  string ArchName; // Target architecture is picked up from TargetTriple.
  TheTarget = TargetRegistry::lookupTarget(ArchName, *TheTriple, Error);
  if (TheTarget == nullptr) {
    Print->Error(Error.c_str());
    return false;
  }

  // Update the triple name and return the found target.
  TargetTriple = TheTriple->getTriple();
  return true;
}

bool CorDisasm::init() {
  // Call llvm_shutdown() on exit.
  llvm_shutdown_obj Y;

  // Initialize targets and assembly printers/parsers.
  InitializeAllTargetInfos();
  InitializeAllTargetMCs();
  InitializeAllDisassemblers();

  if (!setTarget()) {
    // setTarget() prints error message if necessary
    return false;
  }

  MRI.reset(TheTarget->createMCRegInfo(TargetTriple));
  if (!MRI) {
    Print->Error("Error: no register info for target %s\n",
                 TargetTriple.c_str());
    return false;
  }

  // Set up disassembler.
  MCTargetOptions TargetOpts;
  AsmInfo.reset(TheTarget->createMCAsmInfo(*MRI, TargetTriple.c_str(), TargetOpts));
  if (!AsmInfo) {
    Print->Error("error: no assembly info for target %s\n");
    return false;
  }

  string Mcpu;        // Not specifying any particular CPU type.
  string FeaturesStr; // No additional target specific attributes.

  if (TheTargetArch == Target_Arm64) {
    // Enable all features for disassembly. Setting a specific advanced CPU enables all the architecture
    // features for that CPU (e.g., `Mcpu = "neoverse-n2"`), but we want to use the "meta" feature
    // string "+all" to just enable all features, even those not implemented in any current CPU.
    FeaturesStr = "+all";
  } else if (TheTargetArch == Target_RiscV64) {
    FeaturesStr = "+rva23u64,"
      "+zkn,+zks," // RVA22-only options, superseded with "+zvkng,+zvksg" vector equivalents in RVA23
      "+zvkng,+zvksg," // RVA23 localized options
      "+zabha,+zacas,+zvbc,+zama16b," // RVA23 development options
      "+zbc,+zfh,+zvfh,+zfbfmin,+zvfbfmin,+zvfbfwma"; // RVA23 expansion options
  } else if (TheTargetArch == Target_Wasm32) {
    // Enable the Wasm proposals the LLVM/Wasm RyuJIT backend may emit.
    // Keep this in sync with the JIT's emitted feature set on each LLVM bump.
    FeaturesStr = "+simd128,+relaxed-simd,+sign-ext,+nontrapping-fptoint,"
                  "+mutable-globals,+reference-types,+bulk-memory,+tail-call,"
                  "+exception-handling,+multivalue";
  }

  STI.reset(TheTarget->createMCSubtargetInfo(TargetTriple, Mcpu, FeaturesStr));
  if (!STI) {
    Print->Error("error: no subtarget info for target %s\n",
                 TargetTriple.c_str());
    return false;
  }

  MII.reset(TheTarget->createMCInstrInfo());
  if (!MII) {
    Print->Error("error: no instruction info for target %s\n",
                 TargetTriple.c_str());
    return false;
  }

  Ctx.reset(new MCContext(*TheTriple, AsmInfo.get(), MRI.get(), STI.get()));

  Disassembler.reset(TheTarget->createMCDisassembler(*STI, *Ctx));

  if (!Disassembler) {
    Print->Error("error: no disassembler for target %s\n",
                 TargetTriple.c_str());
    return false;
  }

  int AsmPrinterVariant;
  if ((TheTargetArch == Target_X86) || (TheTargetArch == Target_X64)) {
    // ASM printer variants:
    // 0 = ATT, 1 = Intel.
    // LLVM doesn't export this enumeration.
    AsmPrinterVariant = 1;
  } else {
    AsmPrinterVariant = AsmInfo->getAssemblerDialect();
  }

  IP.reset(TheTarget->createMCInstPrinter(
      *TheTriple, AsmPrinterVariant, *AsmInfo, *MII, *MRI));

  if (!IP) {
    Print->Error("error: No Instruction Printer for target %s\n",
                 TargetTriple.c_str());
    return false;
  }

  return true;
}

bool CorDisasm::decodeInstruction(BlockIterator &BIter, bool MayFail) const {
  raw_ostream &CommentStream = nulls();
  ArrayRef<uint8_t> ByteArray(BIter.Ptr, BIter.BlockSize);
  bool IsDecoded =
      Disassembler->getInstruction(BIter.Inst, BIter.InstrSize, ByteArray,
                                   BIter.Addr, CommentStream);

  if (!IsDecoded) {
    if (!MayFail) {
      string buffer;
      raw_string_ostream OS(buffer);

      OS << format("%" PRIxPTR ": ", BIter.Addr);
      dumpBytes(ArrayRef<uint8_t>(BIter.Ptr, BIter.InstrSize), OS);

      Print->Error("Decode Failure %s@ offset:instr %s", BIter.Name, OS.str().c_str());
    }

    BIter.InstrSize = 0;
  } else {
    assert((BIter.InstrSize <= BIter.BlockSize) && "Invalid Decode");
    assert(BIter.InstrSize > 0 && "Zero Length Decode");
  }

  return IsDecoded;
}

uint64_t CorDisasm::disasmInstruction(BlockIterator &BIter,
                                      bool DumpAsm) const {
  uint64_t TotalSize = 0;
  bool ContinueDisasm;

  // On X86, LLVM disassembler does not handle instruction prefixes
  // correctly -- please see LLVM bug 7709.
  // The disassembler reports instruction prefixes separate from the
  // actual instruction. In order to work-around this problem, we
  // continue decoding  past the prefix bytes.

  do {

    if (!decodeInstruction(BIter)) {
      return 0;
    }

    uint64_t Size = BIter.InstrSize;
    TotalSize += Size;

    if (DumpAsm) {
      dumpInstruction(BIter);
    }

    ContinueDisasm = false;
    if ((TheTargetArch == Target_X86) || (TheTargetArch == Target_X64)) {

      // Check if the decoded instruction is a prefix byte, and if so,
      // continue decoding.
      if (Size == 1) {
        for (uint8_t Pfx = 0; Pfx < X86NumPrefixes; Pfx++) {
          if (BIter.Ptr[0] == X86Prefix[Pfx].MachineOpcode) {
            ContinueDisasm = true;
            BIter.advance();
            break;
          }
        }
      }
    }
  } while (ContinueDisasm);

  return TotalSize;
}

void CorDisasm::dumpInstruction(const BlockIterator &BIter) const {
  assert(BIter.isDecoded() && "Cannot print before Decode");

  uint64_t InstSize = BIter.InstrSize;
  string buffer;
  raw_string_ostream OS(buffer);

  OS << format("%" PRIxPTR ": ", BIter.Addr);
  dumpBytes(ArrayRef<uint8_t>(BIter.Ptr, InstSize), OS);

  if ((TheTargetArch == Target_X86) || (TheTargetArch == Target_X64)) {
    // For architectures with a variable size instruction, we pad the
    // byte dump with space up to 7 bytes. Some instructions might be longer,
    // but ...

    const char *Padding[] = {"",
                             "   ",
                             "      ",
                             "         ",
                             "            ",
                             "               ",
                             "                  "};
    OS << (Padding[(InstSize < 7) ? (7 - InstSize) : 0]);
  }
  else if ((TheTargetArch == Target_Thumb) || (TheTargetArch == Target_RiscV64)) {
    // Thumb-2 encoding has 32-bit instructions and 16-bit instructions.
    // RISC-V RVC has 32-bit instructions and 16-bit instructions.
    if (InstSize == 2) {
      OS << "      ";
    }
  }

  IP->printInst(&BIter.Inst, BIter.Addr, "", *STI, OS);
  Print->Dump(OS.str().c_str());
}

void CorDisasm::dumpBlock(const BlockInfo &Block) const {
  BlockIterator BIter(Block);

  Print->Dump("-----------------------------------------------");
  Print->Dump("Block:   %s\nSize:    %" PRIu64 "\nAddress: %" PRIxPTR "\nCodePtr: %" PRIxPTR,
              BIter.Name, BIter.BlockSize, BIter.Addr, (uintptr_t)BIter.Ptr);
  Print->Dump("-----------------------------------------------");

  while (!BIter.isEmpty()) {
    disasmInstruction(BIter, true);
    if (!BIter.isDecoded()) {
      break;
    }
    BIter.advance();
  }
  Print->Dump("-----------------------------------------------");
}

// Compares two code sections for syntactic equality. This is the core of the
// asm diffing logic.
//
// This function mostly relies on McInst representation of an instruction to
// compare for equality. That is, it goes through the code stream and compares,
// instruction by instruction, op code and operand values for equality.
//
// Obviously, just blindly comparing operand values will raise a lot of false
// alarms (ex: literal pointer addresses changing in the code stream).
// Therefore, this utility provides a facility where the users can provide
// custom heuristics via the OffsetComparator API -- to determine equivalency
// of mismatching operand values in order to normalize the false alarms.
//
// Notes:
//    - The core syntactic comparison is platform agnostic; we compare op codes
//      and operand values in an architecture independent way.
//    - The heuristics provided by the customer are not guaranteed to be
//      platform agnostic.
//
// Arguments:
//    Left: The first code block information
//    Right: The second code block information
//
// Return Value:
//    True if the code sections are syntactically identical; false otherwise.
//
bool CorAsmDiff::nearDiff(const BlockInfo &LeftBlock,
                          const BlockInfo &RightBlock,
                          const void *UserData) const {
  BlockIterator Left(LeftBlock);
  BlockIterator Right(RightBlock);

  if (Left.BlockSize != Right.BlockSize) {
    Print->Log("Code Size mismatch: %s=%lu, %s=%lu\n", Left.Name,
               Left.BlockSize, Right.Name, Right.BlockSize);
    return false;
  }

  while (!Left.isEmpty() && !Right.isEmpty()) {

    decodeInstruction(Left);
    decodeInstruction(Right);

    if (!Left.isDecoded() || !Right.isDecoded()) {
      return false;
    }

    if (Left.InstrSize != Right.InstrSize) {
      return fail("Instruction Size Mismatch", Left, Right);
    }

    if (Munger != nullptr)
    {
      // Need to call the munger first

      uint64_t ImmL = 0;
      uint64_t ImmR = 0;
      uint32_t SkipL = 0;
      uint32_t SkipR = 0;

      if (Munger(UserData, Left.BlockOffset(), Left.InstrSize, &ImmL, &ImmR, &SkipL, &SkipR))
      {
        const bool constMatches = ((ImmL == ImmR) || Comparator(UserData, Left.BlockOffset(), Left.InstrSize, ImmL, ImmR));
        if (!constMatches)
        {
          return fail("Munged Immediate Operand Value Mismatch", Left, Right);
        }

        Left.advance();
        Right.advance();

        for (uint32_t i = 0; i < SkipL; i++) {
          decodeInstruction(Left);
          Left.advance();
        }

        for (uint32_t i = 0; i < SkipR; i++) {
          decodeInstruction(Right);
          Right.advance();
        }

        continue;
      }
    }

    // First, check to see if these instructions are actually identical.
    // This is done 1) to avoid the detailed comparison of the fields of InstL
    // and InstR if they are identical, and 2) because in the event that
    // there are bugs or limitations in the user-supplied heuristics,
    // we don't want to count two Instructions as diffs if they are bitwise
    // identical.
    if (Left.isBitwiseEqual(Right)) {
      // Bytes are identical
    } else {
      // Compare field-wise

      const MCInst &InstL = Left.Inst;
      const MCInst &InstR = Right.Inst;

      if (InstL.getOpcode() != InstL.getOpcode()) {
        return fail("OpCode Mismatch", Left, Right);
      }

      size_t numOperands = InstL.getNumOperands();

      if (numOperands != InstL.getNumOperands()) {
        return fail("Operand Count Mismatch", Left, Right);
      }

      for (size_t i = 0; i < numOperands; i++) {
        const MCOperand &OperandL = InstL.getOperand(i);
        const MCOperand &OperandR = InstR.getOperand(i);

        if (OperandL.isExpr() || OperandR.isExpr() || OperandL.isInst() ||
            OperandR.isInst()) {
          return fail("Unexpected Operand Kind", Left, Right);
        } else if (OperandL.isReg()) {
          if (!OperandR.isReg()) {
            return fail("Operand Kind Mismatch", Left, Right);
          }

          if (OperandL.getReg() != OperandR.getReg()) {
            return fail("Operand Register Mismatch", Left, Right);
          }
        } else if (OperandL.isSFPImm()) {
          if (!OperandR.isSFPImm()) {
            return fail("Operand Kind Mismatch", Left, Right);
          }

          if (OperandL.getSFPImm() != OperandR.getSFPImm()) {
            return fail("Operand Single-FP Immediate Mismatch", Left, Right);
          }
        } else if (OperandL.isDFPImm()) {
          if (!OperandR.isDFPImm()) {
            return fail("Operand Kind Mismatch", Left, Right);
          }

          if (OperandL.getDFPImm() != OperandR.getDFPImm()) {
            return fail("Operand Double-FP Immediate Mismatch", Left, Right);
          }
        } else if (OperandL.isImm()) {
          if (!OperandR.isImm()) {
            return fail("Operand Kind Mismatch", Left, Right);
          }

          int64_t ImmL = OperandL.getImm();
          int64_t ImmR = OperandR.getImm();

          if (ImmL == ImmR) {
            continue;
          }

          if (Comparator(UserData, Left.BlockOffset(), Left.InstrSize, ImmL, ImmR)) {
            // The client somehow thinks that these offsets are equivalent
            continue;
          }

          return fail("Immediate Operand Value Mismatch", Left, Right);
        }
      }
    }

    Left.advance();
    Right.advance();
  }

  return true;
}

bool BlockIterator::isBitwiseEqual(const BlockIterator &BIter) const {
  return memcmp(this->Ptr, BIter.Ptr, this->InstrSize) == 0;
}

bool CorAsmDiff::fail(const char *Mesg, const BlockIterator &Left,
                      const BlockIterator &Right) const {
  Print->Log("%s @[%" PRIxPTR " : %" PRIxPTR "]", Mesg, Left.Addr, Right.Addr);
  return false;
}

// --- Wasm framed-buffer support ------------------------------------------

// Wasm value types we recognize when printing locals headers. Values not in
// this table are still consumed by the walker -- they just print as "?<hex>".
static const char *wasmValTypeName(uint8_t Code) {
  switch (Code) {
    case 0x7F: return "i32";
    case 0x7E: return "i64";
    case 0x7D: return "f32";
    case 0x7C: return "f64";
    case 0x7B: return "v128";
    case 0x70: return "funcref";
    case 0x6F: return "externref";
    default:   return nullptr;
  }
}

bool CorDisasm::parseWasmLocals(const uint8_t **Cursor,
                                const uint8_t *BodyEnd,
                                uint64_t *NumLocalGroups) const {
  uint64_t Groups = 0;
  if (readULEB128(Cursor, BodyEnd, &Groups) == 0) {
    return false;
  }
  for (uint64_t I = 0; I < Groups; I++) {
    uint64_t Count = 0;
    if (readULEB128(Cursor, BodyEnd, &Count) == 0) {
      return false;
    }
    if (*Cursor >= BodyEnd) {
      return false;
    }
    uint8_t VT = *(*Cursor)++;
    if (wasmValTypeName(VT) == nullptr) {
      Print->Log("Wasm: unrecognized valtype byte 0x%02x in locals header "
                 "(group %" PRIu64 "); wasmValTypeName needs update",
                 VT, I);
    }
  }
  *NumLocalGroups = Groups;
  return true;
}

// Format a locals header like "(local i32 i32 i64)" given the per-group
// count + valtype pairs. Cursor must point at the start of the locals
// declaration; on success it is advanced to the start of the opcode stream
// and the rendered text is written to *Out (which must not be null).
// Returns false if the locals declaration is malformed. If Print is
// non-null, also emits a Log warning when an unrecognized valtype byte is
// encountered (in addition to rendering it as "?XX" inline).
static bool formatWasmLocals(const uint8_t **Cursor, const uint8_t *BodyEnd,
                             std::string *Out, const PrintControl *Print) {
  uint64_t Groups = 0;
  if (readULEB128(Cursor, BodyEnd, &Groups) == 0) return false;

  if (Groups == 0) {
    *Out = "(local)";
    return true;
  }

  raw_string_ostream OS(*Out);
  OS << "(local";
  for (uint64_t G = 0; G < Groups; G++) {
    uint64_t Count = 0;
    if (readULEB128(Cursor, BodyEnd, &Count) == 0) return false;
    if (*Cursor >= BodyEnd) return false;
    uint8_t VT = *(*Cursor)++;
    const char *Name = wasmValTypeName(VT);
    char Buf[16];
    if (Name == nullptr) {
      if (Print != nullptr) {
        Print->Log("Wasm: unrecognized valtype byte 0x%02x in locals header "
                   "(group %" PRIu64 "); wasmValTypeName needs update",
                   VT, G);
      }
      snprintf(Buf, sizeof(Buf), "?%02x", VT);
      Name = Buf;
    }
    for (uint64_t I = 0; I < Count; I++) {
      OS << ' ' << Name;
    }
  }
  OS << ')';
  OS.flush();
  return true;
}

bool CorDisasm::dumpWasmFramedBlock(const BlockInfo &Block) const {
  Print->Dump("-----------------------------------------------");
  Print->Dump("Block:   %s\nSize:    %" PRIu64 "\nAddress: %" PRIxPTR
              "\nCodePtr: %" PRIxPTR " (wasm32 framed)",
              Block.Name, Block.BlockSize, Block.Addr, (uintptr_t)Block.Ptr);
  Print->Dump("-----------------------------------------------");

  const uint8_t *Cursor = Block.Ptr;
  const uint8_t *End = Block.Ptr + Block.BlockSize;
  uint32_t BodyIndex = 0;

  while (Cursor < End) {
    const uint8_t *RecordStart = Cursor;
    uint64_t BodySize = 0;
    if (readULEB128(&Cursor, End, &BodySize) == 0) {
      Print->Error("Wasm framed dump: bad ULEB128 body length at offset %tu",
                   (ptrdiff_t)(RecordStart - Block.Ptr));
      return false;
    }
    if ((uint64_t)(End - Cursor) < BodySize) {
      Print->Error("Wasm framed dump: body %u truncated (need %" PRIu64
                   ", have %tu)",
                   BodyIndex, BodySize, (ptrdiff_t)(End - Cursor));
      return false;
    }

    const uint8_t *BodyStart = Cursor;
    const uint8_t *BodyEnd = Cursor + BodySize;

    Print->Dump("body %u (size=%" PRIu64 ", buf-off=%tu)",
                BodyIndex, BodySize, (ptrdiff_t)(BodyStart - Block.Ptr));

    std::string LocalsText;
    if (!formatWasmLocals(&Cursor, BodyEnd, &LocalsText, Print)) {
      Print->Error("Wasm framed dump: malformed locals header in body %u",
                   BodyIndex);
      return false;
    }
    Print->Dump("  %s", LocalsText.c_str());

    while (Cursor < BodyEnd) {
      size_t BufOff = (size_t)(Cursor - Block.Ptr);
      BlockIterator BIter(Cursor, (uint64_t)(BodyEnd - Cursor),
                          (uintptr_t)BufOff);
      if (!decodeInstruction(BIter)) {
        Print->Error("Wasm framed dump: decode failure in body %u at buf-off %zu",
                     BodyIndex, BufOff);
        return false;
      }

      std::string Line;
      raw_string_ostream OS(Line);
      OS << format("  %5zx: ", BufOff);
      dumpBytes(ArrayRef<uint8_t>(Cursor, BIter.InstrSize), OS);
      IP->printInst(&BIter.Inst, BIter.Addr, "", *STI, OS);
      OS.flush();
      Print->Dump("%s", Line.c_str());

      Cursor += BIter.InstrSize;
    }

    BodyIndex++;
  }

  Print->Dump("-----------------------------------------------");
  return true;
}

bool CorAsmDiff::compareWasmInstOperands(const MCInst &InstL,
                                         const MCInst &InstR,
                                         size_t BlockOffset, size_t InstrLen,
                                         const void *UserData) const {
  if (InstL.getOpcode() != InstR.getOpcode()) {
    Print->Log("Wasm OpCode Mismatch @buf-off %zu", BlockOffset);
    return false;
  }

  // Defensive belt: nearDiffWasmFramed pre-screens each instruction-pair
  // by comparing encoded InstrSize before calling here, so for equal
  // opcodes the operand counts should already agree. In particular, two
  // br_tables with different table lengths encode to different sizes and
  // are rejected before reaching this point. Keep the check anyway in
  // case future opcodes have variable operand counts at fixed widths.
  size_t numOperands = InstL.getNumOperands();
  if (numOperands != InstR.getNumOperands()) {
    Print->Log("Wasm Operand Count Mismatch @buf-off %zu", BlockOffset);
    return false;
  }

  for (size_t i = 0; i < numOperands; i++) {
    const MCOperand &OperandL = InstL.getOperand(i);
    const MCOperand &OperandR = InstR.getOperand(i);

    if (OperandL.isExpr() || OperandR.isExpr() || OperandL.isInst() ||
        OperandR.isInst()) {
      Print->Log("Wasm Unexpected Operand Kind @buf-off %zu", BlockOffset);
      return false;
    } else if (OperandL.isReg()) {
      if (!OperandR.isReg() || OperandL.getReg() != OperandR.getReg()) {
        Print->Log("Wasm Operand Reg Mismatch @buf-off %zu", BlockOffset);
        return false;
      }
    } else if (OperandL.isSFPImm()) {
      if (!OperandR.isSFPImm() ||
          OperandL.getSFPImm() != OperandR.getSFPImm()) {
        Print->Log("Wasm Operand SFP Mismatch @buf-off %zu", BlockOffset);
        return false;
      }
    } else if (OperandL.isDFPImm()) {
      if (!OperandR.isDFPImm() ||
          OperandL.getDFPImm() != OperandR.getDFPImm()) {
        Print->Log("Wasm Operand DFP Mismatch @buf-off %zu", BlockOffset);
        return false;
      }
    } else if (OperandL.isImm()) {
      if (!OperandR.isImm()) {
        Print->Log("Wasm Operand Kind Mismatch @buf-off %zu", BlockOffset);
        return false;
      }
      int64_t ImmL = OperandL.getImm();
      int64_t ImmR = OperandR.getImm();
      if (ImmL == ImmR) {
        continue;
      }
      if (Comparator(UserData, BlockOffset, InstrLen, ImmL, ImmR)) {
        continue;
      }
      Print->Log("Wasm Immediate Mismatch @buf-off %zu (%" PRId64
                 " vs %" PRId64 ")",
                 BlockOffset, ImmL, ImmR);
      return false;
    }
  }

  return true;
}

bool CorAsmDiff::nearDiffWasmFramed(const BlockInfo &LeftBlock,
                                    const BlockInfo &RightBlock,
                                    const void *UserData) const {
  const uint8_t *LCur = LeftBlock.Ptr;
  const uint8_t *LEnd = LeftBlock.Ptr + LeftBlock.BlockSize;
  const uint8_t *RCur = RightBlock.Ptr;
  const uint8_t *REnd = RightBlock.Ptr + RightBlock.BlockSize;
  uint32_t BodyIndex = 0;

  while (LCur < LEnd && RCur < REnd) {
    uint64_t LBodySize = 0, RBodySize = 0;
    if (readULEB128(&LCur, LEnd, &LBodySize) == 0) {
      Print->Log("Wasm framed diff: bad ULEB128 body length in baseline");
      return false;
    }
    if (readULEB128(&RCur, REnd, &RBodySize) == 0) {
      Print->Log("Wasm framed diff: bad ULEB128 body length in diff");
      return false;
    }
    if ((uint64_t)(LEnd - LCur) < LBodySize ||
        (uint64_t)(REnd - RCur) < RBodySize) {
      Print->Log("Wasm framed diff: body %u truncated", BodyIndex);
      return false;
    }

    const uint8_t *LBodyEnd = LCur + LBodySize;
    const uint8_t *RBodyEnd = RCur + RBodySize;

    // Locals headers must match byte-for-byte. The JIT picks the locals
    // declaration deterministically per body, so any difference is a real
    // codegen change worth flagging.
    uint64_t LGroups = 0, RGroups = 0;
    const uint8_t *LLocStart = LCur;
    const uint8_t *RLocStart = RCur;
    if (!parseWasmLocals(&LCur, LBodyEnd, &LGroups) ||
        !parseWasmLocals(&RCur, RBodyEnd, &RGroups)) {
      Print->Log("Wasm framed diff: malformed locals in body %u", BodyIndex);
      return false;
    }
    size_t LLocLen = (size_t)(LCur - LLocStart);
    size_t RLocLen = (size_t)(RCur - RLocStart);
    if (LLocLen != RLocLen || memcmp(LLocStart, RLocStart, LLocLen) != 0) {
      Print->Log("Wasm framed diff: locals header mismatch in body %u",
                 BodyIndex);
      return false;
    }

    // Mirror the per-body progress trace emitted by dumpWasmFramedBlock so
    // that callers tailing the dump stream can see how far the diff
    // iteration reached even when no mismatch was logged.
    Print->Dump("Wasm framed diff: comparing body %u "
                "(size=%" PRIu64 ", baseline-off=%tu, diff-off=%tu)",
                BodyIndex, LBodySize,
                (ptrdiff_t)(LLocStart - LeftBlock.Ptr),
                (ptrdiff_t)(RLocStart - RightBlock.Ptr));

    // Opcode-by-opcode comparison.
    while (LCur < LBodyEnd && RCur < RBodyEnd) {
      size_t LBufOff = (size_t)(LCur - LeftBlock.Ptr);
      size_t RBufOff = (size_t)(RCur - RightBlock.Ptr);

      BlockIterator LIter(LCur, (uint64_t)(LBodyEnd - LCur),
                          (uintptr_t)LBufOff);
      BlockIterator RIter(RCur, (uint64_t)(RBodyEnd - RCur),
                          (uintptr_t)RBufOff);

      if (!decodeInstruction(LIter) || !decodeInstruction(RIter)) {
        Print->Log("Wasm framed diff: decode failure in body %u", BodyIndex);
        return false;
      }
      if (LIter.InstrSize != RIter.InstrSize) {
        // For Wasm with fixed-width ULEB128 reloc slots, equal opcodes
        // produce equal instruction widths. A mismatch here is either a
        // codegen change (different opcode width family) or a JIT regression
        // around reloc-slot padding -- either way, flag as a real diff in v1.
        // Variable-length payloads (notably br_table's branch list) also
        // produce different InstrSizes when the table count differs, so
        // those mismatches are caught here before reaching the operand
        // comparator below.
        Print->Log("Wasm framed diff: instr-size mismatch in body %u "
                   "@buf-off %zu (%" PRIu64 " vs %" PRIu64 ")",
                   BodyIndex, LBufOff, LIter.InstrSize, RIter.InstrSize);
        return false;
      }

      if (memcmp(LCur, RCur, LIter.InstrSize) != 0) {
        if (!compareWasmInstOperands(LIter.Inst, RIter.Inst, LBufOff,
                                     LIter.InstrSize, UserData)) {
          return false;
        }
      }

      LCur += LIter.InstrSize;
      RCur += RIter.InstrSize;
    }

    if (LCur != LBodyEnd || RCur != RBodyEnd) {
      Print->Log("Wasm framed diff: body %u did not consume to end", BodyIndex);
      return false;
    }

    BodyIndex++;
  }

  if (LCur != LEnd || RCur != REnd) {
    Print->Log("Wasm framed diff: body count mismatch (baseline-rem=%tu, "
               "diff-rem=%tu)",
               (ptrdiff_t)(LEnd - LCur), (ptrdiff_t)(REnd - RCur));
    return false;
  }

  return true;
}

void CorAsmDiff::dumpDiffWasmFramed(const BlockInfo &LeftBlock,
                                    const BlockInfo &RightBlock) const {
  BlockInfo L = LeftBlock; L.Name = "baseline";
  BlockInfo R = RightBlock; R.Name = "diff";
  dumpWasmFramedBlock(L);
  dumpWasmFramedBlock(R);
}

// Implementation for CoreDisTools Interface

DllIface CorDisasm *InitDisasm(enum TargetArch Target) {
  return NewDisasm(Target, &DefaultPrintControl);
}

DllIface CorDisasm *InitBufferedDisasm(enum TargetArch Target) {
  return NewDisasm(Target, &BufferedPrintControl);
}

DllIface CorDisasm *NewDisasm(enum TargetArch Target,
                              const PrintControl *PControl) {
  CorDisasm *Disassembler = new CorDisasm(Target, PControl);
  if (Disassembler->init()) {
    return Disassembler;
  }

  delete Disassembler;
  return nullptr;
}

DllIface void FinishDisasm(const CorDisasm *Disasm) { delete Disasm; }

DllIface CorDisasm *InitBufferedDiffer(enum TargetArch Target,
                               const OffsetComparator Comparator) {
  return NewDiffer(Target, &BufferedPrintControl, Comparator);
}

DllIface CorAsmDiff *NewDiffer(enum TargetArch Target,
                               const PrintControl *PControl,
                               const OffsetComparator Comparator) {
  return NewDiffer2(Target, PControl, Comparator, nullptr);
}

DllIface CorAsmDiff *NewDiffer2(enum TargetArch Target,
                                const PrintControl *PControl,
                                const OffsetComparator Comparator,
                                const OffsetMunger Munger) {
  CorAsmDiff *AsmDiff = new CorAsmDiff(Target, PControl, Comparator, Munger);

  if (AsmDiff->init()) {
    return AsmDiff;
  }

  delete AsmDiff;
  return nullptr;
}

DllIface void FinishDiff(const CorAsmDiff *AsmDiff) { delete AsmDiff; }

DllIface size_t DisasmInstruction(const CorDisasm *Disasm,
                                  const uint8_t *Address, const uint8_t *Bytes,
                                  size_t Maxlength) {
  assert((Disasm != nullptr) && "Disassembler object Expected ");
  BlockIterator BIter(Bytes, Maxlength, (uintptr_t)Address);
  size_t DecodeLength = (size_t)Disasm->disasmInstruction(BIter);
  return DecodeLength;
}

DllIface size_t DumpInstruction(const CorDisasm *Disasm,
	const uint8_t *Address, const uint8_t *Bytes,
	size_t Maxlength) {
	assert((Disasm != nullptr) && "Disassembler object Expected ");
	BlockIterator BIter(Bytes, Maxlength, (uintptr_t)Address);
	size_t DecodeLength = (size_t)Disasm->disasmInstruction(BIter, true);
	return DecodeLength;
}

DllIface bool NearDiffCodeBlocks(const CorAsmDiff *AsmDiff,
                                 const void *UserData, const uint8_t *Address1,
                                 const uint8_t *Bytes1, size_t Size1,
                                 const uint8_t *Address2, const uint8_t *Bytes2,
                                 size_t Size2) {

  if (AsmDiff->getTargetArch() == Target_Wasm32) {
    BlockInfo Left(Bytes1, Size1, (uintptr_t)Address1, "Left");
    BlockInfo Right(Bytes2, Size2, (uintptr_t)Address2, "Right");
    return AsmDiff->nearDiffWasmFramed(Left, Right, UserData);
  }

  BlockIterator Left(Bytes1, Size1, (uintptr_t)Address1, "Left");
  BlockIterator Right(Bytes2, Size2, (uintptr_t)Address2, "Right");
  return AsmDiff->nearDiff(Left, Right, UserData);
}

DllIface bool NearDiffCodeBlocksFramed(const CorAsmDiff *AsmDiff,
                                       const void *UserData,
                                       const uint8_t *Address1,
                                       const uint8_t *Bytes1, size_t Size1,
                                       const uint8_t *Address2,
                                       const uint8_t *Bytes2, size_t Size2) {
  BlockInfo Left(Bytes1, Size1, (uintptr_t)Address1, "Left");
  BlockInfo Right(Bytes2, Size2, (uintptr_t)Address2, "Right");
  return AsmDiff->nearDiffWasmFramed(Left, Right, UserData);
}

DllIface void DumpCodeBlock(const CorDisasm *Disasm, const uint8_t *Address,
                            const uint8_t *Bytes, size_t Size) {
  BlockInfo Block(Bytes, Size, (uintptr_t)Address);
  if (Disasm->getTargetArch() == Target_Wasm32) {
    Disasm->dumpWasmFramedBlock(Block);
    return;
  }
  Disasm->dumpBlock(Block);
}

DllIface void DumpCodeBlockFramed(const CorDisasm *Disasm,
                                  const uint8_t *Address,
                                  const uint8_t *Bytes, size_t Size) {
  BlockInfo Block(Bytes, Size, (uintptr_t)Address);
  Disasm->dumpWasmFramedBlock(Block);
}

// This API is only necessary because we don't expose in the DLL interface
// that CorAsmDiff inherits from CorDisAsm.

DllIface void DumpDiffBlocks(const CorAsmDiff *AsmDiff, const uint8_t *Address1,
                             const uint8_t *Bytes1, size_t Size1,
                             const uint8_t *Address2, const uint8_t *Bytes2,
                             size_t Size2) {

  if (AsmDiff->getTargetArch() == Target_Wasm32) {
    BlockInfo Left(Bytes1, Size1, (uintptr_t)Address1, "baseline");
    BlockInfo Right(Bytes2, Size2, (uintptr_t)Address2, "diff");
    AsmDiff->dumpDiffWasmFramed(Left, Right);
    return;
  }

  BlockIterator Left(Bytes1, Size1, (uintptr_t)Address1, "Left");
  BlockIterator Right(Bytes2, Size2, (uintptr_t)Address2, "Right");

  AsmDiff->dumpBlock(Left);
  AsmDiff->dumpBlock(Right);
}

DllIface void DumpDiffBlocksFramed(const CorAsmDiff *AsmDiff,
                                   const uint8_t *Address1,
                                   const uint8_t *Bytes1, size_t Size1,
                                   const uint8_t *Address2,
                                   const uint8_t *Bytes2, size_t Size2) {
  BlockInfo Left(Bytes1, Size1, (uintptr_t)Address1, "baseline");
  BlockInfo Right(Bytes2, Size2, (uintptr_t)Address2, "diff");
  AsmDiff->dumpDiffWasmFramed(Left, Right);
}

DllIface const char* GetOutputBuffer() {
  return outputStream.str().c_str();
}

DllIface void ClearOutputBuffer() {
  outputBuffer.clear();
}
