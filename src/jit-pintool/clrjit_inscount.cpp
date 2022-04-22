// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#include <iostream>
#include <fstream>
#include <utility>
#include "pin.H"

using std::cerr;
using std::endl;
using std::string;

PIN_LOCK Lock;
UINT64 TotalCount = 0;

#ifdef TARGET_IA32
REG InsCountLowReg;
REG InsCountHighReg;
#else
REG InsCountReg;
#endif

std::vector<std::pair<ADDRINT, ADDRINT>> InstrumentRanges;

KNOB<bool> QuietKnob(KNOB_MODE_WRITEONCE, "pintool", "quiet", "", "if specified the instruction count is not written to stderr on exit");

#ifdef TARGET_IA32
ADDRINT PIN_FAST_ANALYSIS_CALL DoCountLow(ADDRINT countLow, ADDRINT numInsts)
{
    return countLow + numInsts;
}

ADDRINT PIN_FAST_ANALYSIS_CALL OverflowedLow(ADDRINT countLow, ADDRINT numInsts)
{
    return countLow < numInsts;
}

ADDRINT PIN_FAST_ANALYSIS_CALL DoCountHigh(ADDRINT countHigh)
{
    return countHigh + 1;
}

VOID ReadInsCount(UINT64* result, ADDRINT countLow, ADDRINT countHigh)
{
    *result = ((UINT64)countHigh << 32) | countLow;
}
#else
ADDRINT DoCount(ADDRINT count, ADDRINT numInsts) { return count + numInsts; }

VOID ReadInsCount(UINT64* result, ADDRINT count)
{
    *result = count;
}
#endif


VOID ThreadStart(THREADID tid, CONTEXT* ctxt, INT32 flags, VOID* v)
{
    // When the thread starts, zero the storage that holds the
    // dynamic instruction count.
    //
#ifdef TARGET_IA32
    PIN_SetContextReg(ctxt, InsCountLowReg, 0);
    PIN_SetContextReg(ctxt, InsCountHighReg, 0);
#else
    PIN_SetContextReg(ctxt, InsCountReg, 0);
#endif
}

VOID ThreadFini(THREADID tid, const CONTEXT* ctxt, INT32 code, VOID* v)
{
    // When the thread exits, accumulate the thread's dynamic instruction
    // count into the total.
    PIN_GetLock(&Lock, tid + 1);
#ifdef TARGET_IA32
    ADDRINT low = PIN_GetContextReg(ctxt, InsCountLowReg);
    ADDRINT high = PIN_GetContextReg(ctxt, InsCountHighReg);
    TotalCount += ((UINT64)high << 32) | low;
#else
    TotalCount += PIN_GetContextReg(ctxt, InsCountReg);
#endif
    PIN_ReleaseLock(&Lock);
}

static bool IsJitImage(const string& name)
{
#if defined(TARGET_WINDOWS)
    const char* pathSep = "\\";
#elif defined(TARGET_LINUX) || defined(TARGET_MAC)
    const char* pathSep = "/";
#else
#error Invalid platform
#endif

    std::size_t fileNameStart = name.rfind(pathSep);
    if (fileNameStart == std::string::npos)
        fileNameStart = 0;
    else
        fileNameStart++;

    return name.find("clrjit", fileNameStart) != std::string::npos;
}

VOID ImageLoad(IMG img, VOID* v)
{
    if (IsJitImage(IMG_Name(img)))
    {
        InstrumentRanges.push_back(std::make_pair(IMG_LowAddress(img), IMG_HighAddress(img)));
    }
                                           
    RTN getInsCount = RTN_FindByName(img, "Instrumentor_GetInsCount");
    if (RTN_Valid(getInsCount))
    {
        RTN_Open(getInsCount);

        RTN_InsertCall(
            getInsCount, IPOINT_BEFORE, AFUNPTR(ReadInsCount),
            // Pointer
            IARG_FUNCARG_ENTRYPOINT_VALUE, 0,
#ifdef TARGET_IA32
            IARG_REG_VALUE, InsCountLowReg,
            IARG_REG_VALUE, InsCountHighReg,
#else
            IARG_REG_VALUE, InsCountReg,
#endif
            IARG_END);

        RTN_Close(getInsCount);
    }
}

VOID ImageUnload(IMG img, VOID* v)
{
    if (IsJitImage(IMG_Name(img)))
    {
        auto it = std::find(InstrumentRanges.begin(), InstrumentRanges.end(), std::make_pair(IMG_LowAddress(img), IMG_HighAddress(img)));
        if (it != InstrumentRanges.end())
            InstrumentRanges.erase(it);
    }
}

VOID Trace(TRACE trace, VOID* v)
{
    ADDRINT addr = TRACE_Address(trace);
    for (const std::pair<ADDRINT, ADDRINT>& p : InstrumentRanges)
    {
        if (addr >= p.first && addr < p.second)
        {
            goto Found;
        }
    }

    return;

Found:
    for (BBL bbl = TRACE_BblHead(trace); BBL_Valid(bbl); bbl = BBL_Next(bbl))
    {
#ifdef TARGET_IA32
        // We insert the equivalent of:
        // countLow += insCount
        // if (countLow < insCount)
        //   countHigh++;
        // Given that 'insCount' is typically small this is much faster than
        // passing both halves and doing a 64-bit addition.
        // Additionally, it is faster to do the first two lines as two separate
        // inserted calls. Maybe because otherwise we need a reference to the
        // low register which PIN might handle less efficiently.

        BBL_InsertCall(bbl, IPOINT_BEFORE, AFUNPTR(DoCountLow),
                       IARG_FAST_ANALYSIS_CALL,
                       IARG_REG_VALUE, InsCountLowReg,
                       IARG_ADDRINT, BBL_NumIns(bbl),
                       IARG_RETURN_REGS, InsCountLowReg,
                       IARG_END);

        BBL_InsertIfCall(bbl, IPOINT_BEFORE, AFUNPTR(OverflowedLow),
                         IARG_FAST_ANALYSIS_CALL,
                         IARG_REG_VALUE, InsCountLowReg,
                         IARG_ADDRINT, BBL_NumIns(bbl),
                         IARG_END);

        BBL_InsertThenCall(bbl, IPOINT_BEFORE, AFUNPTR(DoCountHigh),
                           IARG_FAST_ANALYSIS_CALL,
                           IARG_REG_VALUE, InsCountHighReg,
                           IARG_RETURN_REGS, InsCountHighReg,
                           IARG_END);
#else
        BBL_InsertCall(bbl, IPOINT_ANYWHERE, AFUNPTR(DoCount),
                       // Things are simpler on x64.
                       IARG_REG_VALUE, InsCountReg,
                       IARG_RETURN_REGS, InsCountReg,
                       IARG_ADDRINT, BBL_NumIns(bbl),
                       IARG_END);
#endif
    }
}

VOID Fini(INT32 code, VOID* v)
{
    if (!QuietKnob)
    {
        cerr << "Instructions executed: " << TotalCount << endl;
    }
}

INT32 Usage()
{
    cerr << "CoreCLR pintool for investigating JIT throughput" << endl;
    cerr << KNOB_BASE::StringKnobSummary() << endl;
    return -1;
}

/* ===================================================================== */
/* Main                                                                  */
/* ===================================================================== */

int main(int argc, char* argv[])
{
    if (PIN_Init(argc, argv)) return Usage();

    PIN_InitLock(&Lock);

#ifdef TARGET_IA32
    InsCountLowReg = PIN_ClaimToolRegister();
    InsCountHighReg = PIN_ClaimToolRegister();
    bool claimed = REG_valid(InsCountLowReg) && REG_valid(InsCountHighReg);
#else
    InsCountReg = PIN_ClaimToolRegister();
    bool claimed = REG_valid(InsCountReg);
#endif
    if (!claimed)
    {
        cerr << "Could not allocate storage for instruction count." << endl;
        return 1;
    }

    PIN_AddThreadStartFunction(ThreadStart, 0);
    PIN_AddThreadFiniFunction(ThreadFini, 0);
    PIN_AddFiniFunction(Fini, 0);

    IMG_AddInstrumentFunction(ImageLoad, 0);
    IMG_AddUnloadFunction(ImageUnload, 0);
    TRACE_AddInstrumentFunction(Trace, 0);

    PIN_InitSymbolsAlt(EXPORT_SYMBOLS);
    PIN_StartProgram();
    return 0;
}
