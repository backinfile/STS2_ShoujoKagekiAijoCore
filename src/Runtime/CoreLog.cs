using MegaCrit.Sts2.Core.Logging;
using ShoujoKageki.Core.V1;
namespace ShoujoKageki.Core.Runtime;
internal static class CoreLog
{
    internal static readonly Logger Logger = new(CoreApi.ModId, LogType.Generic);
}
