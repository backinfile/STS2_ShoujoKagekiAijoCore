using MegaCrit.Sts2.Core.Nodes.CommonUi;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

public sealed class CardUpgradeModule : ICoreModule, ICardUpgrades
{
    private sealed record Observer(Func<CardModel, bool> Matches, Func<CardModel, int, int, Task> Upgraded);
    private static CardUpgradeModule _instance = null!;
    private readonly List<Observer> _observers = [];
    public string Id => "card-upgrades";
    public int Order => -650;
    public void Initialize(CoreContext context)
    {
        _instance = this;
        context.Services.Add<ICardUpgrades>(this);
    }
    public IDisposable Register(Func<CardModel, bool> matches, Func<CardModel, int, int, Task> upgraded)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(upgraded);
        var observer = new Observer(matches, upgraded);
        _observers.Add(observer);
        return new Registration(() => _observers.Remove(observer));
    }
    private sealed class Registration(Action remove) : IDisposable
    {
        private Action? _remove = remove;
        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }
    public async Task Upgrade(IEnumerable<CardModel> cards, CardPreviewStyle style)
    {
        using var scope = new CommandEffectScope();
        CardCmd.Upgrade(cards, style);
        await scope.Complete(Task.CompletedTask);
    }
    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Upgrade), [typeof(IEnumerable<CardModel>), typeof(CardPreviewStyle)])]
    private static class NativeUpgrade
    {
        private static void Prefix(ref IEnumerable<CardModel> cards, out Dictionary<CardModel, (int Level, Observer[] Observers)> __state)
        {
            cards = cards.ToArray();
            __state = cards.Distinct().Where(card => card.Pile != null && card.IsUpgradable)
                .Select(card => (Card: card, Observers: _instance._observers.Where(observer => observer.Matches(card)).ToArray()))
                .Where(entry => entry.Observers.Length > 0)
                .ToDictionary(entry => entry.Card, entry => (entry.Card.CurrentUpgradeLevel, entry.Observers));
            if (__state.Count > 0 && !CommandEffectScope.IsActive)
                throw new InvalidOperationException("Use CardUpgrades.Upgrade for upgrades outside an asynchronous model or game action.");
        }
        private static void Postfix(Dictionary<CardModel, (int Level, Observer[] Observers)> __state)
        {
            foreach (var (card, before) in __state)
            {
                int current = card.CurrentUpgradeLevel;
                if (current <= before.Level) continue;
                foreach (var observer in before.Observers)
                    CommandEffectScope.Enqueue(() => observer.Upgraded(card, before.Level, current));
            }
        }
    }
    // A native action keeps its existing state machine, replay recording and completion/checksum boundary.
    [HarmonyPatch]
    private static class ActionCompletion
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(GameAction).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(GameAction).IsAssignableFrom(type))
            .Select(type => AccessTools.DeclaredMethod(type, "ExecuteAction"))
            .Where(method => method != null && method.ReturnType == typeof(Task)).Distinct()!;
        private static void Prefix(out CommandEffectScope __state) => __state = new();
        private static void Postfix(ref Task __result, CommandEffectScope __state)
        {
            __result = __state.Complete(__result);
            __state.Dispose();
        }
        private static Exception? Finalizer(Exception? __exception, CommandEffectScope? __state)
        {
            __state?.Dispose();
            return __exception;
        }
    }
}
