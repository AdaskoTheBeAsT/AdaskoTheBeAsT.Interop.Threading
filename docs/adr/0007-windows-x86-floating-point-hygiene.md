# ADR 0007: Opt-in Windows x86 floating-point exception-mask hygiene

- Status: Accepted
- Date: 2026-11-16

## Context

In 32-bit Windows (`win-x86`) processes, legacy native and COM libraries (notably Borland Delphi runtimes setting `Default8087CW = 0x1332`, Visual Basic 6 add-ins, and C++/Fortran numerical engines) modify the thread's x87 floating-point control word (`FCW`) and/or SSE2 `MXCSR` to unmask hardware floating-point traps (`#IE`, `#DE`, `#ZE`, `#OE`, `#UE`, `#PE`), and can return to managed code with sticky exception status flags already set (`FSW` / `MXCSR` armed pending traps).

Validating this failure mode on real 32-bit x86 hardware across `.NET Framework 4.8.1` (`net472`), `.NET 10` (`net10.0`), and `.NET 11` (`net11.0`) established four concrete technical requirements:

1. **Severe failure impact on both LegacyCLR and CoreCLR `win-x86`**:
   - On `.NET Framework 4.x` (`win-x86`), an unmasked trap left behind by Work Item 1 causes normal IEEE-754 operations in Work Item 2 (such as `0.0 / 0.0` producing `double.NaN`, `1.0 / 0.0` producing `+Infinity`, subnormals, or `1.0 / 3.0` inexact rounding) to throw `ArithmeticException`.
   - On modern CoreCLR (`.NET 10` and `.NET 11` `win-x86`), `System.Runtime.EH.RhThrowHwEx` treats unmasked hardware floating-point exceptions in managed code as unrecoverable `Environment.FailFast` (`0x80131623`), terminating the entire host process immediately without allowing `try`/`catch` recovery.
2. **`_controlfp_s` silently drops `_EM_DENORMAL` on x86**:
   - Both `msvcrt!_controlfp_s` and `ucrtbase!_controlfp_s` strip `_EM_DENORMAL` (`0x00080000`) from their mask argument on x86, leaving denormalized-operand traps unmasked if a native component unmasked bit 1 (`DM`) in `FCW` or bit 8 (`DM`) in `MXCSR`.
   - `ucrtbase!__control87_2(McwEm, McwEm, out x86Cw, out sse2Cw)` (`_MCW_EM = 0x0008001F`) is the x86 UCRT entry point that masks all six floating-point exceptions (including `_EM_DENORMAL`) in **both** the x87 control word and SSE2 `MXCSR` while leaving rounding (`_MCW_RC = 0x00000300`) and x87 precision (`_MCW_PC = 0x00030000`) untouched.
3. **First-call JIT compilation and lazy P/Invoke binding hazard under armed traps**:
   - In a fresh 32-bit process, if `CreateThreadScope` (or Work Item 1) arms a pending x87/SSE trap before `StaPlatform.ResetFloatingPointState()` and `NativeMethods.ResetFloatingPointExceptionMask()` have ever executed, the first call to `ResetFloatingPointState()` invokes the CLR JIT compiler (`clrjit.dll`) and lazy `[DllImport]` stub resolution *while the hardware trap is armed*, crashing inside the runtime (`StackOverflowException` / `0xC00000FD`) before `ucrtbase!_clearfp()` is reached.
4. **Scheduler `ExecuteWork` `finally` ordering**:
   - `SingleThreadedApartmentTaskScheduler.ExecuteWork<T>` previously evaluated `execution.Elapsed.TotalMilliseconds` (`double` division) unconditionally in its `finally` block before returning to `MessageLoopThread`. If a work item left `#PE` (inexact) unmasked or armed a pending x87 trap, `TotalMilliseconds` faulted inside `ExecuteWork`'s `finally` block before a loop-level reset could run.

## Decision

1. **Add opt-in `ResetFloatingPointMaskAfterWorkItem` (`bool`, default `false`), scoped to Windows `Architecture.X86`**:
   - Keep default behavior unchanged (`false`).
   - Guard `NativeMethods.ResetFloatingPointExceptionMask()` with `RuntimeInformation.ProcessArchitecture != Architecture.X86` (and `Environment.OSVersion.Platform == PlatformID.Win32NT` in `StaPlatform`), making the option a safe zero-P/Invoke no-op on `x64` and `Arm64`.
2. **Clear sticky status flags via `ucrtbase!_clearfp` before masking `_MCW_EM` via `ucrtbase!__control87_2`**:
   - Call `ucrtbase!_clearfp()` first (which executes non-waiting `fnclex` and clears `MXCSR` sticky exception flags without triggering armed traps).
   - Immediately call `ucrtbase!__control87_2(McwEm, McwEm, out _, out _)` with `McwEm = 0x0008001F` as both `newValue` and `mask` so all six exception masks are restored in x87 `FCW` and SSE2 `MXCSR` while preserving callers' rounding (`_MCW_RC`) and precision (`_MCW_PC`) modes.
3. **Pre-JIT and pre-bind the exact reset path during worker startup, and reset first in `ExecuteWork.finally`**:
   - When `ResetFloatingPointMaskAfterWorkItem` is `true`, invoke `_platform.ResetFloatingPointState()` once in `ThreadEntry` **before** `CreateThreadScope` runs so virtual dispatch, managed wrapper, and `ucrtbase` P/Invoke stubs are all JIT-compiled and bound while FPU traps are masked, and invoke `_platform.ResetFloatingPointState()` again immediately after `CreateThreadScope`.
   - Inside `ExecuteWork<T>`, allocate the per-item `Stopwatch` only when `_diagnostics` is enabled, and invoke `_platform.ResetFloatingPointState()` **first** inside `ExecuteWork<T>`'s `finally` block before `execution.Elapsed.TotalMilliseconds` (`TraceWork`) and before `StaWorkItem<T>` completes its `TaskCompletionSource`.
4. **Enforce real x86 hardware validation in both unit tests and a fresh child process**:
   - Test simulated `StaPlatform` ordering on all architectures, W^X native x86 machine-code stubs in `StaFloatingPointHygieneContractTest` on `net472|x86`, `net10.0|x86`, and `net11.0|x86`, and a compiled 32-bit MSVC C DLL (`test/native/fpu_poison.c` via `scripts/Test-FpuProcess.ps1`) in fresh 32-bit `SysWOW64` processes.

## Why

- Clearing sticky flags (`_clearfp`) before modifying control words prevents waiting x87 instructions from firing armed traps during the reset sequence itself.
- Using `ucrtbase!__control87_2` with `mask = _MCW_EM` is the only UCRT primitive on Windows x86 that masks all six hardware traps (including `_EM_DENORMAL`) across both x87 and SSE2 without clobbering rounding or precision modes configured by native numerical libraries.
- Warming the exact `ResetFloatingPointState()` call chain prior to `CreateThreadScope` and running resets at the top of `ExecuteWork.finally` eliminates both first-call JIT crashes in fresh processes and post-delegate `Stopwatch` floating-point faults.

## Consequences

Positive:

- 32-bit Delphi, VB6, C++, and Fortran STA COM servers that unmask FPU traps or leave sticky status flags set can no longer crash subsequent managed work items with `ArithmeticException` (`.NET Framework`) or `Environment.FailFast` (`.NET 10` / `.NET 11` CoreCLR `win-x86`).
- Callers' x87/SSE rounding modes (such as `RC_UP`) and x87 precision modes (such as `PC_64`) remain intact across work items.
- Zero overhead on `x64`/`Arm64` and zero overhead when `ResetFloatingPointMaskAfterWorkItem` is left at its `false` default, plus one fewer `Stopwatch` allocation per work item when diagnostics are disabled.

Trade-offs:

- When `ResetFloatingPointMaskAfterWorkItem = true` on Windows `x86`, each executed work item incurs two cheap `Cdecl` calls into `ucrtbase.dll` (`_clearfp` and `__control87_2`).
- Floating-point hygiene applies at scope/work-item boundaries; if a single delegate calls a trap-unmasking native function and then executes managed floating-point arithmetic *inside that same delegate* before returning, the delegate itself must still guard or reset FPU state around the native call.
