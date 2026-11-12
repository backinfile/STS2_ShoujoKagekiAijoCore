using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.V1;

/// <summary>Notifications after native state and Core compatibility handling have completed.</summary>
public interface IPlayerStateEvents
{
    event Action<Player>? Restored;
}
public interface ICardCloneEvents
{
    event Action<CardModel, CardModel>? Cloned;
}
/// <summary>Invocation scopes are restored synchronously; async continuations retain their captured state.</summary>
public interface IModelMethodScopes
{
    IDisposable Register(MethodInfo method, Func<object?, IDisposable?> enter);
}
