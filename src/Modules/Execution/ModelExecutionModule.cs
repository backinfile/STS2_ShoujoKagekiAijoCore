using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

/// <summary>Owns the native model-method hooks shared by all rule consumers.</summary>
public sealed class ModelExecutionModule : ICoreModule, IModelExecutionScopes
{
    private static ModelExecutionModule _instance = null!;
    private readonly Harmony _patcher = new(CoreApi.ModId + ".ModelExecution");
    private readonly HashSet<MethodInfo> _patched = [];
    private readonly List<Func<AbstractModel, CardPlay?, IDisposable?>> _observers = [];
    private readonly List<Func<CardModel, PlayerChoiceContext, CardPlay, Func<Task, Task>?>> _native = [];
    public string Id => "model-execution";
    public int Order => -600;
    public void Initialize(CoreContext context)
    {
        _instance = this;
        context.Services.Add<IModelExecutionScopes>(this);
        context.ModelsReady += Install;
        context.Discovery.ModelsChanged += Install;
    }
    public IDisposable Register(Func<AbstractModel, CardPlay?, IDisposable?> enter)
    {
        ArgumentNullException.ThrowIfNull(enter);
        _observers.Add(enter);
        return new Subscription(() => _observers.Remove(enter));
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
            if (!card && !typeof(PowerModel).IsAssignableFrom(type) && !typeof(RelicModel).IsAssignableFrom(type)
                && !typeof(OrbModel).IsAssignableFrom(type)) continue;
            // Include inherited card effects once; model methods on other types are declared hooks.
            var methods = card
                ? new[] { AccessTools.Method(type, "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]) }
                : ModelMethods(type);
            foreach (var candidate in methods.OfType<MethodInfo>())
            {
                var method = AccessTools.DeclaredMethod(candidate.DeclaringType, candidate.Name,
                    candidate.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
                if (method.IsAbstract || method.ContainsGenericParameters || !typeof(Task).IsAssignableFrom(method.ReturnType)
                    || method.GetMethodBody() == null || !_patched.Add(method)) continue;
                _patcher.Patch(method,
                    prefix: new(typeof(ModelExecutionModule), card ? nameof(CardPrefix) : nameof(ModelPrefix)),
                    postfix: new(typeof(ModelExecutionModule), card ? nameof(CardPostfix) : nameof(ModelPostfix)),
                    finalizer: new(typeof(ModelExecutionModule), nameof(Finalizer)));
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
        internal readonly List<IDisposable> Scopes = [];
        internal readonly List<Func<Task, Task>> Completions = [];
        public void Dispose()
        {
            // Dispose is also called by Harmony's exception finalizer.
            for (int index = Scopes.Count - 1; index >= 0; index--) Scopes[index].Dispose();
            Scopes.Clear();
        }
    }
    private static Invocation Enter(AbstractModel model, CardPlay? play)
    {
        var invocation = new Invocation();
        try
        {
            foreach (var observer in _instance._observers.ToArray())
                if (observer(model, play) is { } scope) invocation.Scopes.Add(scope);
            return invocation;
        }
        catch { invocation.Dispose(); throw; }
    }
    private static void ModelPrefix(AbstractModel __instance, out Invocation __state) => __state = Enter(__instance, null);
    private static void CardPrefix(CardModel __instance, PlayerChoiceContext __0, CardPlay __1, out Invocation __state)
    {
        __state = Enter(__instance, __1);
        try
        {
            foreach (var begin in _instance._native)
                if (begin(__instance, __0, __1) is { } complete) __state.Completions.Add(complete);
        }
        catch { __state.Dispose(); throw; }
    }
    private static void ModelPostfix(Invocation __state) => __state.Dispose();
    private static void CardPostfix(ref Task __result, Invocation __state)
    {
        foreach (var complete in __state.Completions.AsEnumerable().Reverse()) __result = complete(__result);
        __state.Dispose();
    }
    private static Exception? Finalizer(Exception? __exception, Invocation? __state)
    {
        __state?.Dispose();
        return __exception;
    }
}
