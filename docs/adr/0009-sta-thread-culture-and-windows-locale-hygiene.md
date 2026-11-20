# ADR 0009: STA thread culture and Windows kernel locale (`SetThreadLocale`) hygiene

- Status: Accepted
- Date: 2026-11-17

## Context

Hosting legacy 32-bit and 64-bit COM and native calculation engines (Visual Basic 6 `MSVBVM60.DLL`, `OLEAUT32.DLL` `VarBstrFromR8`/`VarR8FromStr`, Borland Delphi `SysUtils.GetFormatSettings`, and Microsoft Jet/DAO) on shared STA worker threads exposed dual-layer locale sensitivity:

1. **Managed `CultureInfo` alone does not control Win32 / OLE Automation locale APIs**:
   - Managed `.NET` string parsing and formatting consult `CultureInfo.CurrentCulture` (and resource lookups consult `CultureInfo.CurrentUICulture`).
   - Native VB6 (`CStr`/`CDbl`/`Val`), `OLEAUT32` default `LOCALE_USER_DEFAULT` conversions, and Delphi RTL initialization consult the Win32 kernel thread locale (`kernel32!GetThreadLocale` / `kernel32!SetThreadLocale`, such as `0x0409` `en-US` or `0x0809` `en-GB`).
   - On hosts configured with comma-decimal locales (`pl-PL`, `de-DE`, `fr-FR`, `tr-TR`), initializing a COM server or marshaling `BSTR` numbers without pinning **both** `CultureInfo.CurrentCulture` and the Win32 thread LCID corrupts decimal separators or triggers `DISP_E_TYPEMISMATCH`.
2. **Thread-sticky persistence and cross-item poisoning on STA workers**:
   - `StaWorkItem<T>` executes delegates directly inside the STA worker thread's `ExecutionContext` without capturing caller `ExecutionContext`.
   - Consequently, culture and Win32 LCID applied on the STA worker persist across queued work items on both `.NET Framework` and modern `.NET`, **and** any work item or native DLL that mutates `CultureInfo.CurrentCulture` or calls `SetThreadLocale` mid-call poisons every subsequent work item unless explicitly healed.
3. **Startup ordering relative to `CreateThreadScope`**:
   - Many Delphi and VB6 in-process COM DLLs cache decimal/date separators from `GetThreadLocale()` during `DllMain` or class-factory activation inside `CreateThreadScope`. Applying locale settings *after* `CreateThreadScope` would be too late.

## Decision

1. **Add opt-in culture and Win32 LCID options to `SingleThreadedApartmentTaskSchedulerOptions` (and one-off `SingleThreadedApartmentTaskOptions`)**:
   - `ThreadCulture` (`CultureInfo?`, default `null`)
   - `ThreadUICulture` (`CultureInfo?`, default `null`)
   - `WindowsThreadLocaleId` (`int?`, default `null`; non-positive values `<= 0` throw `ArgumentOutOfRangeException` during validation)
   - `ResetThreadCultureAndLocaleAfterWorkItem` (`bool`, default `false` on `SingleThreadedApartmentTaskSchedulerOptions`)
2. **Add `kernel32!GetThreadLocale` and `kernel32!SetThreadLocale` through `NativeMethods` and `StaPlatform`**:
   - Declare `GetThreadLocale()` (`uint`) and `SetThreadLocale(uint locale)` (`SetLastError = true`) in `NativeMethods`.
   - Expose `virtual uint GetThreadLocale()` and `virtual void SetThreadLocale(uint localeId)` on `StaPlatform`, throwing `Win32Exception(Marshal.GetLastWin32Error())` when `SetThreadLocale` returns `false`.
3. **Apply culture and Win32 LCID before `CreateThreadScope`, and heal on `ExecuteWork.finally` when opted in**:
   - In `SingleThreadedApartmentTaskScheduler.ThreadEntry`, apply configured `ThreadCulture`, `ThreadUICulture`, and `WindowsThreadLocaleId` **after** OLE/STA verification and **before** `CreateThreadScope` executes. If `SetThreadLocale` fails (for example for an invalid or uninstalled LCID), `_threadReady` faults deterministically during scheduler construction.
   - When `ResetThreadCultureAndLocaleAfterWorkItem` is `true`, capture the post-configuration worker baseline and re-verify both immediately after `CreateThreadScope` and inside `ExecuteWork<T>.finally` using fast equality guards (`!ReferenceEquals(CultureInfo.CurrentCulture, baseline)` and `_platform.GetThreadLocale() != baselineLcid`) so unmutated calls incur no redundant locale-switch cost while mutated state is healed before the next work item.
4. **Provide standalone `ThreadCultureScope`**:
   - `ThreadCultureScope.Enter(CultureInfo? culture, CultureInfo? uiCulture = null)` (portable across all OSes and TFMs).
   - `ThreadCultureScope.Enter(CultureInfo? culture, CultureInfo? uiCulture, int? windowsThreadLocaleId)` (`[SupportedOSPlatform("windows")]` on modern `.NET`).
   - Captures `ThreadAffinity.Capture()`, previous `CultureInfo.CurrentCulture`, previous `CultureInfo.CurrentUICulture`, and (when `windowsThreadLocaleId` is provided) previous `GetThreadLocale()`, restoring all saved values idempotently on `Dispose()` on the owning thread and throwing `InvalidOperationException` on cross-thread disposal.

## Why

- Defaulting all four scheduler options to `null` / `false` guarantees zero behavioral or performance impact for callers that do not configure locale pinning.
- Setting `CultureInfo` and `SetThreadLocale` before `CreateThreadScope` ensures native CRT/Delphi/VB6 module initialization sees the expected decimal and date formatting rules from the very first activation call.
- Guarding post-work-item resets with `ReferenceEquals` and `GetThreadLocale() != baselineLcid` keeps per-call overhead negligible when work items behave cleanly while guaranteeing deterministic recovery when a call mutates thread locale state.
- `ThreadCultureScope` gives callers running on external worker pools or inside `SingleThreadedApartmentTask.Run` the same dual managed + Win32 locale protection with deterministic `using`-scope restoration.

## Consequences

Positive:

- Legacy VB6, OLE Automation, Delphi, and Jet/DAO STA components behave identically regardless of the host machine's regional settings (`pl-PL`, `fr-FR`, `de-DE`, `tr-TR`, `en-US`, `en-GB`).
- Mid-call managed or Win32 locale mutations cannot leak across work items when `ResetThreadCultureAndLocaleAfterWorkItem = true` or `ThreadCultureScope` is used.

Trade-offs:

- `kernel32!SetThreadLocale` affects Win32 APIs consulting the thread LCID (`GetThreadLocale`, `LOCALE_USER_DEFAULT` in `OLEAUT32`/VB6/Delphi); modern Win32 NLS name-based APIs using `GetThreadPreferredUILanguages` instead follow `CultureInfo.CurrentUICulture` or process UI language settings.
- Passing an LCID rejected by the OS causes `SetThreadLocale` to throw `Win32Exception` at scheduler startup or `ThreadCultureScope.Enter`.
