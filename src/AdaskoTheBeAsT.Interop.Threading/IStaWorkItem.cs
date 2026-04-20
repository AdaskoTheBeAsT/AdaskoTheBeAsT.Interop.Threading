#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace AdaskoTheBeAsT.Interop.Threading;

#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
internal interface IStaWorkItem
{
    void Execute();

    void Cancel();
}
