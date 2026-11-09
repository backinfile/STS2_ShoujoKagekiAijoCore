using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
namespace ShoujoKageki.Core.Runtime.Modules.Execution;

/// <summary>One native effect/destination adapter, preserving third-party dispatch hooks.</summary>
public sealed class CardPlayRoutingModule : ICoreModule, ICardPlayRouting, ICardEffectRouting
{
    private sealed record Route(CardEffectMiddleware Middleware, Func<CardModel, PileType?> Destination);
    private static CardPlayRoutingModule _instance = null!;
    private readonly List<Route> _routes = [];
    private static readonly ConcurrentDictionary<Type, MethodInfo> Effects = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> Destinations = new();
    public string Id => "card-play-routing";
    public int Order => -550;
    public void Initialize(CoreContext context)
    {
        _instance = this;
        context.Services.Add<ICardPlayRouting>(this);
        context.Services.Add<ICardEffectRouting>(this);
        context.ModelsReady += Install;
    }
    private bool _installed;
    private void Install()
    {
        if (_installed) return;
        // All mod initializers have completed, so optional dispatchers are known and patched first.
        var harmony = new Harmony(CoreApi.ModId + ".CardRouting");
        harmony.Patch(AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CardModel), nameof(CardModel.OnPlayWrapper))),
            transpiler: new HarmonyMethod(typeof(NativeBoundary), "Transpiler") { priority = int.MinValue });
        var hook = AccessTools.TypeByName("STS2RitsuLib.Cards.CardOnPlayHook");
        if (hook != null) harmony.Patch(AccessTools.AsyncMoveNext(AccessTools.Method(hook, "RunCardOnPlayHooks")),
            transpiler: new HarmonyMethod(typeof(RitsuBoundary), "Transpiler"));
        _installed = true;
    }
    public IDisposable Register(CardPlayMiddleware middleware, Func<CardModel, PileType?> destination)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        return Register((card, context, play, next) =>
            middleware(card, context, play, payload => next(context, payload)), destination);
    }
    public IDisposable Register(CardEffectMiddleware middleware, Func<CardModel, PileType?> destination)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        ArgumentNullException.ThrowIfNull(destination);
        var route = new Route(middleware, destination);
        _routes.Add(route);
        return new Registration(() => _routes.Remove(route));
    }
    private sealed class Registration(Action remove) : IDisposable
    {
        private Action? _remove = remove;
        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }
    public static Task Dispatch(CardModel card, PlayerChoiceContext context, CardPlay play)
        => Execute(card, context, play, (choice, payload) => (Task)Effects.GetOrAdd(card.GetType(), type =>
            AccessTools.Method(type, "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)])).Invoke(card, BindingFlags.DoNotWrapExceptions, null, [choice, payload], null)!);
    public static Task DispatchFromHook(Delegate original, CardModel card, PlayerChoiceContext context, CardPlay play)
        => Execute(card, context, play, (choice, payload) => InvokeHook(original, card, choice, payload));
    private static Task InvokeHook(Delegate original, CardModel card, PlayerChoiceContext context, CardPlay play)
    {
        try { return (Task)original.DynamicInvoke(card, context, play)!; }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
    private static Task Execute(CardModel card, PlayerChoiceContext context, CardPlay play, CardEffectContinuation original)
    {
        var routes = _instance._routes.ToArray();
        Task Next(int index, PlayerChoiceContext choice, CardPlay payload)
        {
            if (index == routes.Length) return original(choice, payload);
            return routes[index].Middleware(card, choice, payload,
                (nextChoice, nextPlay) => Next(index + 1, nextChoice, nextPlay));
        }
        return Next(0, context, play);
    }
    private static PileType? Destination(CardModel card)
    {
        foreach (var route in _instance._routes)
            if (route.Destination(card) is { } destination) return destination;
        return null;
    }
#if STS2_BETA
    public static CardLocation Result(CardModel card) => Destination(card) is { } destination
        ? new(card.Owner, destination, CardPilePosition.Bottom)
        : (CardLocation)Destinations.GetOrAdd(card.GetType(), type => AccessTools.Method(type, "GetResultLocationForCardPlay")).Invoke(card, BindingFlags.DoNotWrapExceptions, null, null, null)!;
#else
    public static PileType Result(CardModel card) => Destination(card)
        ?? (PileType)Destinations.GetOrAdd(card.GetType(), type => AccessTools.Method(type, "GetResultPileTypeForCardPlay")).Invoke(card, BindingFlags.DoNotWrapExceptions, null, null, null)!;
#endif
    internal static class NativeBoundary
    {
        private static MethodBase TargetMethod() => AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CardModel), nameof(CardModel.OnPlayWrapper)));
        [HarmonyPriority(int.MinValue)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int effects = 0, results = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo hook && hook.DeclaringType?.FullName == "STS2RitsuLib.Cards.CardOnPlayHook" && hook.Name == "RunCardOnPlayHooks") effects++;
                if (instruction.operand is MethodInfo { DeclaringType: var type, Name: var name } && type == typeof(CardModel))
                {
                    string? replacement = name switch
                    {
                        "OnPlay" => nameof(Dispatch),
#if STS2_BETA
                        "GetResultLocationForCardPlay" => nameof(Result),
#else
                        "GetResultPileTypeForCardPlay" => nameof(Result),
#endif
                        _ => null
                    };
                    if (replacement != null)
                    {
                        if (name == "OnPlay") effects++; else results++;
                        instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(CardPlayRoutingModule), replacement);
                    }
                }
                yield return instruction;
            }
            if (effects != 1 || results != 1) throw new InvalidOperationException($"Core card boundaries: effects={effects}, results={results}");
        }
    }
    internal static class RitsuBoundary
    {
        private static Type? HookType => AccessTools.TypeByName("STS2RitsuLib.Cards.CardOnPlayHook");
        private static bool Prepare() => HookType != null;
        private static MethodBase TargetMethod() => AccessTools.AsyncMoveNext(AccessTools.Method(HookType!, "RunCardOnPlayHooks"));
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int effects = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.Name == "Invoke" && method.DeclaringType?.FullName == "STS2RitsuLib.Cards.CardOnPlayHook+CardOnPlayDelegate")
                {
                    instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(CardPlayRoutingModule), nameof(DispatchFromHook)); effects++;
                }
                yield return instruction;
            }
            if (effects != 1) throw new InvalidOperationException($"Core RitsuLib card boundary: effects={effects}");
        }
    }
}
