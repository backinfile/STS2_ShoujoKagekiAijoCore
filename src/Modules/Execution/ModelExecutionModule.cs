using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

/// <summary>Owns the native model-method hooks shared by all rule consumers.</summary>
public sealed class ModelExecutionModule : ICoreModule, IModelExecutionScopes, IModelMethodScopes
{
    private static ModelExecutionModule _instance = null!;
    private readonly Harmony _patcher = new(CoreApi.ModId + ".ModelExecution");
    private readonly HashSet<MethodInfo> _patched = [];
    private readonly List<Func<AbstractModel, CardPlay?, IDisposable?>> _observers = [];
    private readonly List<Func<CardModel, PlayerChoiceContext, CardPlay, Func<Task, Task>?>> _native = [];
    private readonly Dictionary<MethodBase, List<Func<object?, IDisposable?>>> _methods = [];
    public string Id => "model-execution";
    public int Order => -600;
    public void Initialize(CoreContext context)
    {
        _instance = this;
        context.Services.Add<IModelExecutionScopes>(this);
        context.Services.Add<IModelMethodScopes>(this);
        context.ModelsReady += Install;
        context.Discovery.ModelsChanged += Install;
    }
    public IDisposable Register(Func<AbstractModel, CardPlay?, IDisposable?> enter)
    {
        ArgumentNullException.ThrowIfNull(enter);
        _observers.Add(enter);
        return new Subscription(() => _observers.Remove(enter));
    }
    public IDisposable Register(MethodInfo method, Func<object?, IDisposable?> enter)
    {
        if (!_methods.TryGetValue(method, out var handlers))
        {
            _methods.Add(method, handlers = []);
            Patch(method, typeof(CardModel).IsAssignableFrom(method.DeclaringType!) && method.Name == "OnPlay");
        }
        handlers.Add(enter);
        return new Subscription(() => handlers.Remove(enter));
    }
    private void Patch(MethodInfo method, bool card)
    {
        if (!_patched.Add(method)) return;
        _patcher.Patch(method,
            prefix: new(typeof(ModelExecutionModule), card ? nameof(CardPrefix) : nameof(ModelPrefix)),
            postfix: typeof(Task).IsAssignableFrom(method.ReturnType)
                ? new(typeof(ModelExecutionModule), card ? nameof(CardPostfix) : nameof(ModelPostfix)) : null,
            finalizer: new(typeof(ModelExecutionModule), nameof(Finalizer)));
    }

    internal static void RegisterNative(Func<CardModel, PlayerChoiceContext, CardPlay, Func<Task, Task>?> enter)
        => _instance._native.Add(enter);
    private sealed class Subscription(Action remove) : IDisposable
    {
        private Action? _remove = remove;
        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }
    private void Install()
    {
        foreach (var type in RuntimeEntry.Context.Discovery.Models.Select(model => model.ModelType).Distinct())
        {
            bool card = typeof(CardModel).IsAssignableFrom(type);
            if (!typeof(AbstractModel).IsAssignableFrom(type)) continue;
            // Include inherited card effects once; model methods on other types are declared hooks.
            var methods = card
                ? new[] { AccessTools.Method(type, "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]) }
                : ModelMethods(type);
            foreach (var candidate in methods.OfType<MethodInfo>())
            {
                var method = AccessTools.DeclaredMethod(candidate.DeclaringType, candidate.Name,
                    candidate.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
                if (method.IsAbstract || method.ContainsGenericParameters || !typeof(Task).IsAssignableFrom(method.ReturnType)
                    || method.GetMethodBody() == null) continue;
                Patch(method, card);
            }
        }
    }
    private static IEnumerable<MethodInfo> ModelMethods(Type type)
    {
        for (var current = type; current != null && current != typeof(AbstractModel); current = current.BaseType)
            foreach (var method in current.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return method;
    }
    private sealed class Invocation : IDisposable
    {
        internal readonly CommandEffectScope Effects = new();
        internal readonly List<IDisposable> Scopes = [];
        internal readonly List<Func<Task, Task>> Completions = [];
        public void Dispose()
        {
            var scopes = Scopes.ToArray();
            Scopes.Clear();
            Cleanup.Run(scopes.Reverse().Select<IDisposable, Action>(scope => scope.Dispose).Append(Effects.Dispose));
        }
    }
    private static Invocation Enter(object? model, CardPlay? play, MethodBase method)
    {
        var invocation = new Invocation();
        try
        {
            if (model is AbstractModel abstractModel)
                foreach (var observer in _instance._observers.ToArray())
                    if (observer(abstractModel, play) is { } scope) invocation.Scopes.Add(scope);
            if (_instance._methods.TryGetValue(method, out var handlers))
                foreach (var enter in handlers.ToArray())
                    if (enter(model) is { } scope) invocation.Scopes.Add(scope);
            return invocation;
        }
        catch (Exception error) { Cleanup.Run([invocation.Dispose], error); throw; }
    }
    private static void ModelPrefix(object? __instance, MethodBase __originalMethod, out Invocation __state)
        => __state = Enter(__instance, null, __originalMethod);
    private static void CardPrefix(CardModel __instance, PlayerChoiceContext __0, CardPlay __1, MethodBase __originalMethod, out Invocation __state)
    {
        __state = Enter(__instance, __1, __originalMethod);
        try
        {
            foreach (var begin in _instance._native)
                if (begin(__instance, __0, __1) is { } complete) __state.Completions.Add(complete);
        }
        catch (Exception error) { Cleanup.Run([__state.Dispose], error); throw; }
    }
    private static void ModelPostfix(ref Task __result, Invocation __state)
    {
        __result = __state.Effects.Complete(__result);
        Release(ref __result, __state);
    }
    private static void CardPostfix(ref Task __result, Invocation __state)
    {
        __result = __state.Effects.Complete(__result);
        foreach (var complete in __state.Completions.AsEnumerable().Reverse()) __result = complete(__result);
        Release(ref __result, __state);
    }
    private static void Release(ref Task result, Invocation state)
    {
        if (Cleanup.Capture([state.Dispose]) is { } error)
            result = Cleanup.Complete(result, () => ValueTask.FromException(error));
    }
    private static Exception? Finalizer(Exception? __exception, Invocation? __state)
        => __state == null ? __exception : Cleanup.Capture([__state.Dispose], __exception);
}
