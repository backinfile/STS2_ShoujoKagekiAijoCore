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
public sealed class CardPlayRoutingModule : ICoreModule, ICardPlayRouting, ICardEffectRouting, ICardPayloadRouting
{
    private sealed record Route(CardPayloadMiddleware Middleware, Func<CardModel, PileType?> Destination);
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
        context.Services.Add<ICardPayloadRouting>(this);
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
        return Register((CardModel card, PlayerChoiceContext context, CardPlay play, CardEffectContinuation next) =>
            middleware(card, context, play, payload => next(context, payload)), destination);
    }
    public IDisposable Register(CardEffectMiddleware middleware, Func<CardModel, PileType?> destination)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        return Register((CardModel card, PlayerChoiceContext context, CardPlay play, ICardEffectPayload next) =>
            middleware(card, context, play, next.Bind(card)), destination);
    }
    public ICardEffectPayload Native { get; } = new Payload([], 0, InvokeNative);
    public IDisposable Register(CardPayloadMiddleware middleware, Func<CardModel, PileType?> destination)
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
    private static Task InvokeNative(CardModel card, PlayerChoiceContext context, CardPlay play)
        => (Task)Effects.GetOrAdd(card.GetType(), type =>
            AccessTools.Method(type, "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]))
            .Invoke(card, BindingFlags.DoNotWrapExceptions, null, [context, play], null)!;
    public static Task Dispatch(CardModel card, PlayerChoiceContext context, CardPlay play)
        => Execute(card, context, play, InvokeNative);
    public static Task DispatchFromHook(Delegate original, CardModel card, PlayerChoiceContext context, CardPlay play)
        => Execute(card, context, play, (boundCard, choice, payload) => InvokeHook(original, boundCard, choice, payload));
    private static Task InvokeHook(Delegate original, CardModel card, PlayerChoiceContext context, CardPlay play)
    {
        try { return (Task)original.DynamicInvoke(card, context, play)!; }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
    private static bool IsRitsuDispatch(MethodInfo method)
    {
        if (method.DeclaringType?.FullName == "STS2RitsuLib.Cards.CardOnPlayHook" && method.Name == "RunCardOnPlayHooks") return true;
        // BWPxing 1.0.7 retains the dispatcher in a delegate, beyond a direct IL call.
        // Verify the captured target; RitsuBoundary still adapts its original effect.
        return method.DeclaringType?.FullName == "BWPxing.Compatibility.CardPlayContext"
            && method.Name == "PlayWithRitsuHooks" && method.IsStatic && method.ReturnType == typeof(Task)
            && method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(CardModel), typeof(PlayerChoiceContext), typeof(CardPlay) })
            && AccessTools.Field(method.DeclaringType, "_ritsuHooks")?.GetValue(null) is Delegate captured
            && captured.Method.DeclaringType?.FullName == "STS2RitsuLib.Cards.CardOnPlayHook"
            && captured.Method.Name == "RunCardOnPlayHooks";
    }
    private sealed class Payload(Route[] routes, int index,
        Func<CardModel, PlayerChoiceContext, CardPlay, Task> original) : ICardEffectPayload
    {
        public CardEffectContinuation Bind(CardModel card)
        {
            ArgumentNullException.ThrowIfNull(card);
            return (context, play) => index == routes.Length
                ? original(card, context, play)
                : routes[index].Middleware(card, context, play, new Payload(routes, index + 1, original));
        }
    }
    private static Task Execute(CardModel card, PlayerChoiceContext context, CardPlay play,
        Func<CardModel, PlayerChoiceContext, CardPlay, Task> original)
        => new Payload(_instance._routes.ToArray(), 0, original).Bind(card)(context, play);
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
            var code = instructions.ToArray();
            int effects = 0, results = 0;
            foreach (var instruction in code)
            {
                if (instruction.Calls(AccessTools.Method(typeof(CardPlayRoutingModule), nameof(Dispatch)))) effects++;
                if (instruction.Calls(AccessTools.Method(typeof(CardPlayRoutingModule), nameof(Result)))) results++;
                if (instruction.operand is MethodInfo hook && IsRitsuDispatch(hook)) effects++;
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
                else if (instruction.operand is MethodInfo forwarded && !IsRitsuDispatch(forwarded) &&
                    (ForwardedCardCall.Adapt(forwarded,
                        call => call.DeclaringType == typeof(CardModel) && call.Name == "OnPlay",
                        AccessTools.Method(typeof(CardPlayRoutingModule), nameof(Dispatch))) ||
                     ForwardedCardCall.Adapt(forwarded,
                        call => call.DeclaringType?.FullName == "STS2RitsuLib.Cards.CardOnPlayHook" && call.Name == "RunCardOnPlayHooks", null))) effects++;
                yield return instruction;
            }
            if (effects != 1 || results != 1) throw BoundaryDiagnostics.Mismatch($"Core card boundaries: effects={effects}, results={results}", TargetMethod(), code);
        }
    }
    internal static class RitsuBoundary
    {
        private static Type? HookType => AccessTools.TypeByName("STS2RitsuLib.Cards.CardOnPlayHook");
        private static bool Prepare() => HookType != null;
        private static MethodBase TargetMethod() => AccessTools.AsyncMoveNext(AccessTools.Method(HookType!, "RunCardOnPlayHooks"));
        private static bool IsEffectDelegate(MethodInfo method)
            => method.Name == "Invoke" && method.DeclaringType != null
                && typeof(Delegate).IsAssignableFrom(method.DeclaringType) && method.ReturnType == typeof(Task)
                && method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(new[] { typeof(CardModel), typeof(PlayerChoiceContext), typeof(CardPlay) });
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var replacement = AccessTools.Method(typeof(CardPlayRoutingModule), nameof(DispatchFromHook));
            var code = instructions.ToArray();
            int effects = 0;
            foreach (var instruction in code)
            {
                if (instruction.Calls(replacement)) effects++;
                else if (instruction.operand is MethodInfo method && IsEffectDelegate(method))
                {
                    instruction.opcode = OpCodes.Call; instruction.operand = replacement; effects++;
                }
                else if (instruction.operand is MethodInfo forwarded && ForwardedCardCall.Adapt(forwarded,
                    IsEffectDelegate, replacement)) effects++;
                yield return instruction;
            }
            if (effects != 1) throw BoundaryDiagnostics.Mismatch($"Core RitsuLib card boundary: effects={effects}", TargetMethod(), code);
        }
    }
}
