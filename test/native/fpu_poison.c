#include <float.h>
#include <xmmintrin.h>

#ifndef _M_IX86
#error fpu_poison.c must be compiled for 32-bit x86 (_M_IX86).
#endif

__declspec(dllexport) void __cdecl fpu_poison_unmask_delphi(void)
{
    unsigned short cw = 0x1332; /* Borland Delphi Default8087CW (#IE, #ZE, #OE unmasked) */
    _clearfp();
    __asm {
        fldcw word ptr [cw]
    }
    _mm_setcsr(0x00001900u); /* SSE #IE, #ZE, #OE unmasked */
}

__declspec(dllexport) void __cdecl fpu_poison_arm_pending_and_unmask_all_upward64(void)
{
    /* Set sticky x87 ZE=1 while masked, then load x87 CW 0x0B00 (all 6 x87 traps unmasked,
       PC=64-bit, RC=UP) via non-waiting fldcw + SSE MXCSR 0x00004004 (sticky ZE=1, all 6
       SSE masks unmasked, RC=UP), returning with armed pending traps. */
    unsigned short cw = 0x0B00;
    unsigned int mxcsr = 0x00004004u;
    _clearfp();
    __asm {
        fld1
        fldz
        fdivp st(1), st(0)
        fstp st(0)
        fldcw word ptr [cw]
        ldmxcsr dword ptr [mxcsr]
    }
}
