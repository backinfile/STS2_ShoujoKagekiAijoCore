using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.V1;

/// <summary>Synchronous scopes captured by asynchronous native model methods.
/// Dispose restores the caller's context after invocation; the task retains its captured context.</summary>
public interface IModelExecutionScopes
{
    IDisposable Register(Func<AbstractModel, CardPlay?, IDisposable?> enter);
}
