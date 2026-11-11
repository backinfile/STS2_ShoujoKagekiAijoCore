using System.Collections.Concurrent;
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using ShoujoKageki.Core.V1;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

public sealed class ShineService : IShineService, ICoreModule
{
    public static ShineService Instance { get; private set; } = null!;
    public string Id => "shine";
    public int Order => -500;
    private static readonly SavedSpireField<CardModel, int> Remaining = new(() => -1, "core_shine_current");
    private static readonly SavedSpireField<CardModel, int> Maximum = new(() => -1, "core_shine_max");
    private static readonly SavedSpireField<CardModel, bool> Temporary = new(() => false, "core_shine_temporary");
    private static readonly SavedSpireField<CardModel, bool> Depleted = new(() => false, "core_shine_depleted");
    private static readonly SavedSpireField<CardModel, bool> Pending = new(() => false, "core_shine_pending");
    private static readonly SavedSpireField<CardModel, int> LegacyRemaining = new(() => -1, "karen_shine_current");
    private static readonly SavedSpireField<CardModel, int> LegacyMaximum = new(() => -1, "karen_shine_max");
    private readonly List<Func<CardModel, bool>> _deferred = [];
    private static readonly AsyncLocal<Batch?> ActiveBatch = new();
    private static readonly AsyncLocal<int> SnapshotCopyDepth = new();
    [ThreadStatic] private static int _cardReadDepth;
    private static readonly ConditionalWeakTable<CardModel, object> PermanentOrigins = new();

    public void Initialize(CoreContext context)
    {
        Instance = this;
        context.Services.Add<IShineService>(this);
        ShoujoKageki.Core.Runtime.Modules.Execution.ModelExecutionModule.RegisterNative(BeginNativePlay);
    }
    private static void InitializeState(CardModel card)
    {
        if (Maximum.Get(card) < 0)
        {
            var legacyMax = LegacyMaximum.Get(card);
            var attribute = card.GetType().GetCustomAttribute<ShineCardAttribute>();
            if (legacyMax >= 0) { Maximum.Set(card, legacyMax); Remaining.Set(card, Math.Max(0, LegacyRemaining.Get(card))); }
            else if (attribute != null) { Maximum.Set(card, attribute.Uses); Remaining.Set(card, attribute.Uses); }
        }
    }
    internal readonly record struct VisualState(int Remaining, bool PendingDepletion);
    internal VisualState? GetVisualState(CardModel card)
    {
        InitializeState(card);
        return Maximum.Get(card) < 0 ? null : new VisualState(Remaining.Get(card), Pending.Get(card));
    }
    public ShineState? GetState(CardModel card)
    {
        InitializeState(card);
        if (Maximum.Get(card) < 0) return null;
        var temporary = Temporary.Get(card) || !Depleted.Get(card) && !PermanentOrigins.TryGetValue(card, out _)
            && card.IsMutable && card.Owner is { } owner
            && card.DeckVersion == null && !owner.Deck.Cards.Contains(card);
        return new ShineState(Remaining.Get(card), Maximum.Get(card), temporary, Pending.Get(card));
    }
    public void SetState(CardModel card, int remaining, int maximum, bool temporary = false)
    {
        if (maximum < 0 || remaining < 0) throw new ArgumentOutOfRangeException(nameof(remaining));
        Maximum.Set(card, maximum);
        Remaining.Set(card, remaining);
        Temporary.Set(card, temporary);
        if (temporary) PermanentOrigins.Remove(card);
        if (remaining > 0) { Pending.Set(card, false); Depleted.Set(card, false); }
    }
    public void ChangeMaximum(CardModel card, int amount)
    {
        var state = GetState(card);
        SetState(card, Math.Max(0, (state?.Remaining ?? 0) + amount), Math.Max(0, (state?.Maximum ?? 0) + amount), state?.Temporary ?? false);
    }
    public void RestoreToMaximum(CardModel card)
    {
        if (GetState(card) is { } state) SetState(card, state.Maximum, state.Maximum, state.Temporary);
    }
    public void GrantTemporary(CardModel card, int uses)
    {
        if (uses <= 0) throw new ArgumentOutOfRangeException(nameof(uses));
        SetState(card, uses, uses, true);
    }
    public void DepleteAfterCurrentResolution(CardModel card) => Pending.Set(card, true);
    public void RegisterDeferredResolutionPredicate(Func<CardModel, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (!_deferred.Contains(predicate)) _deferred.Add(predicate);
    }
    public bool IsDeferredResolution(CardModel card) => _deferred.Any(x => x(card));
    public IReadOnlyList<DepletedCard> GetDepletedCards(Player player) => ShineArchive.Entries(player);
    public IReadOnlyList<CardModel> GetDepletedModels(Player player) => ShineArchive.Models(player);
    public CardModel Reacquire(Player player, DepletedCard card) => ShineArchive.Reacquire(player, card);
    public IAsyncDisposable BeginResolutionBatch() => BeginBatch(null);

    internal sealed class Batch(Batch? parent, PlayerChoiceContext? choice, CardModel? nativeCard) : IAsyncDisposable
    {
        public readonly Batch? Parent = parent;
        public readonly CardModel? NativeCard = nativeCard;
        public readonly HashSet<CardModel> LegacyDebited = new(ReferenceEqualityComparer.Instance);
        public readonly HashSet<CardModel> NativePermits = new(ReferenceEqualityComparer.Instance);
        public PlayerChoiceContext? Choice = choice;
        public readonly HashSet<CardModel> Executed = new(ReferenceEqualityComparer.Instance);
        public readonly Dictionary<CardModel, int> Running = new(ReferenceEqualityComparer.Instance);
        private bool _disposed;
        internal void Detach() { if (ReferenceEquals(ActiveBatch.Value, this)) ActiveBatch.Value = Parent; }
        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            Detach();
            return FlushAsync();
        }
        private async ValueTask FlushAsync()
        {
            if (Parent != null)
            {
                Parent.Executed.UnionWith(Executed);
                Parent.Choice ??= Choice;
                return;
            }
            List<Exception>? errors = null;
            foreach (var card in Executed)
            {
                if (!Pending.Get(card) && Instance.GetState(card) is not { Remaining: <= 0 }) continue;
                try { await Instance.FinishDepletion(card, Choice); }
                catch (Exception error) { (errors ??= []).Add(error); }
            }
            if (errors != null) throw new AggregateException("Card depletion cleanup failed.", errors);
        }
    }
    internal static Batch BeginBatch(PlayerChoiceContext? context, CardModel? nativeCard = null)
    {
        var batch = new Batch(ActiveBatch.Value, context, nativeCard);
        ActiveBatch.Value = batch;
        return batch;
    }
    internal sealed record ResolutionLease(Batch Batch, CardModel Card, bool OwnBatch, bool Counted);
    private ResolutionLease Enter(CardModel card, CardResolutionKind kind, PlayerChoiceContext? choice)
    {
        var batch = ActiveBatch.Value;
        var own = batch == null;
        batch ??= BeginBatch(choice);
        batch.Choice ??= choice;
        var running = batch.Running.GetValueOrDefault(card);
        var ancestorRunning = false;
        for (var ancestor = batch.Parent; ancestor != null; ancestor = ancestor.Parent)
            ancestorRunning |= ancestor.Running.GetValueOrDefault(card) > 0;
        var independentNativePlay = kind == CardResolutionKind.NativePlay && ReferenceEquals(batch.NativeCard, card);
        var counted = running == 0 && (!ancestorRunning || independentNativePlay) && kind != CardResolutionKind.Effect
            && (kind != CardResolutionKind.NativePlay || !IsDeferredResolution(card));
        batch.Running[card] = running + 1;
        if (counted && kind == CardResolutionKind.NativePlay && card.Owner is { } legacyOwner
            && ShineArchive.GetRules(legacyOwner).RulesetId == ShineArchive.LegacyRulesetId)
            counted = batch.LegacyDebited.Add(card);
        if (counted)
        {
            batch.Executed.Add(card);
            if (GetState(card) is { } state && !card.Keywords.Contains(CardKeyword.Eternal))
            {
                var remaining = Math.Max(0, state.Remaining - 1);
                Remaining.Set(card, remaining);
                if (remaining == 0) Pending.Set(card, true);
                if (!state.Temporary && card.DeckVersion is { } permanent && !ReferenceEquals(permanent, card))
                {
                    _ = GetState(permanent);
                    Remaining.Set(permanent, Math.Min(Math.Max(0, Remaining.Get(permanent)), remaining));
                }
            }
        }
        return new ResolutionLease(batch, card, own, counted);
    }
    private static async Task Exit(Task task, ResolutionLease lease)
    {
        try { await task; }
        finally
        {
            var count = lease.Batch.Running[lease.Card] - 1;
            if (count == 0) lease.Batch.Running.Remove(lease.Card); else lease.Batch.Running[lease.Card] = count;
            if (lease.OwnBatch) await lease.Batch.DisposeAsync();
        }
    }
    public Task ResolveAsync(CardModel card, Func<Task> resolution, CardResolutionKind kind)
        => Resolve(card, null, null, resolution, kind);
    public Task ResolveAsync(CardModel card, PlayerChoiceContext context, CardPlay play, Func<Task> resolution, CardResolutionKind kind)
        => Resolve(card, context, play, resolution, kind);
    private Task Resolve(CardModel card, PlayerChoiceContext? context, CardPlay? play, Func<Task> resolution, CardResolutionKind kind)
    {
        var lease = Enter(card, kind, context);
        Task task;
        try { task = Execute(); }
        catch (Exception error) { task = Task.FromException(error); }
        if (lease.OwnBatch) lease.Batch.Detach();
        return Exit(task, lease);
        async Task Execute()
        {
            await resolution();
            if (lease.Counted && kind != CardResolutionKind.NativePlay && play != null && context != null && card is IShineResolutionHandler handler)
                await handler.AfterShineResolution(context, play);
        }
    }
    private static readonly ConditionalWeakTable<CardModel, object> ArchivedModels = new();
    internal bool IsArchivedModel(CardModel card) => ArchivedModels.TryGetValue(card, out _);
    internal void MarkArchived(CardModel card)
    {
        ArchivedModels.GetOrCreateValue(card);
        var state = GetState(card);
        Depleted.Set(card, true);
        Pending.Set(card, false);
        Temporary.Set(card, false);
        if (state != null) Remaining.Set(card, 0);
    }
    private static void ResetUninitializedState(CardModel card)
    {
        Remaining.Set(card, -1);
        Maximum.Set(card, -1);
        LegacyRemaining.Set(card, -1);
        LegacyMaximum.Set(card, -1);
        Pending.Set(card, false);
        Depleted.Set(card, false);
        Temporary.Set(card, false);
    }
    internal void ResetLifecycle(CardModel card)
    {
        ArchivedModels.Remove(card);
        PermanentOrigins.GetOrCreateValue(card);
        Depleted.Set(card, false);
        Pending.Set(card, false);
        Temporary.Set(card, false);
        if (GetState(card) is { } state) Remaining.Set(card, state.Maximum);
    }
    internal static IDisposable AllowSnapshotCopy()
    {
        SnapshotCopyDepth.Value++;
        return new SnapshotScope();
    }
    private sealed class SnapshotScope : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SnapshotCopyDepth.Value--;
        }
    }
    public CardModel Copy(CardModel card, CardCopyPurpose purpose) => CardCopyService.CopyDetached(card, purpose);
    internal void PreserveSnapshotOrigin(CardModel source, CardModel snapshot)
    {
        if (IsArchivedModel(source)) ArchivedModels.GetOrCreateValue(snapshot);
        if (GetState(source) is { Temporary: false }) PermanentOrigins.GetOrCreateValue(snapshot);
    }
    internal bool IsDepletedCopySource(CardModel card) => Depleted.Get(card) || GetState(card) is { Remaining: <= 0 };
    internal CardModel CreateEmptyShell(CardModel source) => EmptyShellFor(source);
    internal void InitializeCopy(CardModel source, CardModel copy, CardCopyPurpose purpose)
    {
        if (purpose == CardCopyPurpose.Reacquire) ResetLifecycle(copy);
        else if (GetState(source) is { } state)
            SetState(copy, state.Remaining, state.Maximum, purpose == CardCopyPurpose.TemporaryCopy || state.Temporary);
    }
    private static CardModel EmptyShellFor(CardModel source)
    {
        var shell = (CardModel)ModelDb.Card<CoreEmptyShell>().MutableClone();
        shell.Owner = source.Owner;
        return shell;
    }
    internal static bool ShouldDeferResultMove(CardModel card)
    {
        var batch = ActiveBatch.Value;
        return batch != null && batch.Executed.Contains(card) && batch.Running.GetValueOrDefault(card) == 0
            && !card.Keywords.Contains(CardKeyword.Eternal)
            && (Pending.Get(card) || Instance.GetState(card) is { Remaining: <= 0 });
    }
    private async Task FinishDepletion(CardModel card, PlayerChoiceContext? choice)
    {
        if (Depleted.Get(card) || card.Keywords.Contains(CardKeyword.Eternal)) return;
        var player = card.Owner;
        if (player == null) return;
        var permanent = card.DeckVersion ?? card;
        var isPermanent = !Temporary.Get(card) && !card.IsDupe && player.Deck.Cards.Contains(permanent);
        var combat = card.CombatState;
        Depleted.Set(card, true);
        Pending.Set(card, false);
        var lifecycle = "temporary";
        if (isPermanent)
        {
            Depleted.Set(permanent, true);
            lifecycle = ShineArchive.Add(player, permanent);
        }
        if (CoreApi.Get<IShinePresentation>() is ShoujoKageki.Core.Runtime.Modules.Presentation.ShinePresentation presentation) presentation.PlayDepletion(card);
        try
        {
            if (isPermanent)
            {
                var context = new ShineDepletionContext(card, permanent, player, lifecycle)
                    { ChoiceContext = choice ?? new BlockingPlayerChoiceContext(), CombatState = combat };
                var callbackChoice = context.ChoiceContext!;
                callbackChoice.PushModel(card);
                try
                {
                    if (card is IShineDepletionHandler handler) await handler.OnShineDepleted(context);
                    foreach (var listener in player.Relics.Where(x => !x.HasBeenRemovedFromState).OfType<IShineDepletionListener>().ToArray())
                        await listener.OnShineDepleted(context);
                }
                finally { callbackChoice.PopModel(card); }
            }
        }
        finally
        {
            if (card.Pile?.IsCombatPile == true) await CardPileCmd.RemoveFromCombat(card);
            card.RemoveFromState();
            if (isPermanent && !ReferenceEquals(card, permanent)) permanent.RemoveFromState();
        }
    }
    private static Func<Task, Task>? BeginNativePlay(CardModel card, PlayerChoiceContext choice, CardPlay play)
    {
        var batch = ActiveBatch.Value;
        if (batch == null || !batch.NativePermits.Remove(card)) return null;
        var lease = Instance.Enter(card, CardResolutionKind.NativePlay, choice);
        return task => Exit(task, lease);
    }
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
    private static class NativePermitPatch
    {
        private static void Postfix(CardPlay cardPlay, ref Task __result)
        {
            var batch = ActiveBatch.Value;
            if (batch?.NativeCard != cardPlay.Card || Instance.IsDeferredResolution(cardPlay.Card)) return;
            __result = Arm(__result, batch, cardPlay.Card);
        }
        private static async Task Arm(Task task, Batch batch, CardModel card)
        {
            await task;
            batch.NativePermits.Add(card);
        }
    }
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    private static class NativeAfterResolutionPatch
    {
        private static void Postfix(PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
        {
            if (Instance.IsDeferredResolution(cardPlay.Card) || ActiveBatch.Value?.Executed.Contains(cardPlay.Card) != true) return;
            __result = Notify(__result, choiceContext, cardPlay);
        }
        private static async Task Notify(Task task, PlayerChoiceContext context, CardPlay play)
        {
            await task;
            if (play.Card is IShineResolutionHandler handler) await handler.AfterShineResolution(context, play);
        }
    }

    [HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.MutableClone))]
    private static class ClonePatch
    {
        private static void Postfix(AbstractModel __instance, ref AbstractModel __result)
        {
            if (__instance is not CardModel source || __result is not CardModel copy || Instance.GetState(source) is not { } state) return;
            if (source.IsMutable && (Depleted.Get(source) || state.Remaining <= 0) && SnapshotCopyDepth.Value == 0) { __result = EmptyShellFor(source); return; }
            Instance.SetState(copy, state.Remaining, state.Maximum, state.Temporary);
            Depleted.Set(copy, Depleted.Get(source));
            Pending.Set(copy, Pending.Get(source));
        }
    }
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.FromSerializable))]
    private static class LegacyCardStatePatch
    {
        private static void Prefix(SerializableCard save, out bool __state)
        {
            _cardReadDepth++;
            __state = true;
            if (save.Id?.Entry == "KAREN_EMPTY_SHELL") save.Id = ModelDb.GetId<CoreEmptyShell>();
        }
        private static void Finalizer(bool __state)
        {
            if (__state) _cardReadDepth--;
        }
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(SerializableCard save, CardModel __result)
        {
            var properties = save.Props?.ints;
            var oldCurrent = properties?.Where(x => x.name == "CardModel_karen_shine_current").Select(x => (int?)x.value).FirstOrDefault();
            var oldMaximum = properties?.Where(x => x.name == "CardModel_karen_shine_max").Select(x => (int?)x.value).FirstOrDefault();
            if (properties?.Any(x => x.name is "CardModel_core_shine_current" or "CardModel_core_shine_max") == true)
            {
                if (!oldCurrent.HasValue) LegacyRemaining.Set(__result, -1);
                if (!oldMaximum.HasValue) LegacyMaximum.Set(__result, -1);
                return;
            }
            ResetUninitializedState(__result);
            if (oldCurrent.HasValue && oldMaximum is >= 0)
                Instance.SetState(__result, Math.Max(0, oldCurrent.Value), oldMaximum.Value);
        }
    }
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardChangedPiles))]
    private static class LegacyExhaustRestorePatch
    {
        private static void Prefix(CardModel card)
        {
            if (card.Owner is not { } owner || card.Pile?.Type != PileType.Exhaust || Pending.Get(card)) return;
            if (ShineArchive.GetRules(owner).RulesetId == ShineArchive.LegacyRulesetId) Instance.RestoreToMaximum(card);
        }
    }
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.UpgradeInternal))]
    private static class UpgradePatch
    {
        private static void Postfix(CardModel __instance)
        {
            if (_cardReadDepth == 0) Instance.RestoreToMaximum(__instance);
        }
    }
    [HarmonyPatch]
    private static class WrapperPatch
    {
        private sealed record StateFields(FieldInfo State, FieldInfo Card, FieldInfo Choice);
        private static readonly ConcurrentDictionary<Type, StateFields> Fields = new();
        private static MethodBase TargetMethod() => AccessTools.AsyncMoveNext(
            AccessTools.Method(typeof(CardModel), nameof(CardModel.OnPlayWrapper)));

        private static void Prefix(object __instance, MethodBase __originalMethod, out Batch? __state)
        {
            __state = ActiveBatch.Value;
            var fields = Fields.GetOrAdd(__originalMethod.DeclaringType!, type => new StateFields(
                AccessTools.Field(type, "<>1__state"), AccessTools.Field(type, "<>4__this"), AccessTools.Field(type, "choiceContext")));
            if ((int)fields.State.GetValue(__instance)! == -1)
                BeginBatch((PlayerChoiceContext?)fields.Choice.GetValue(__instance), (CardModel)fields.Card.GetValue(__instance)!);
        }
        private static void Postfix(Batch? __state) => ActiveBatch.Value = __state;
        private static Exception? Finalizer(Exception? __exception, Batch? __state)
        {
            ActiveBatch.Value = __state;
            return __exception;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int results = 0, exceptions = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(AsyncTaskMethodBuilder))
                {
                    if (method.Name == nameof(AsyncTaskMethodBuilder.SetResult))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = AccessTools.Method(typeof(WrapperPatch), nameof(Complete));
                        results++;
                    }
                    else if (method.Name == nameof(AsyncTaskMethodBuilder.SetException))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = AccessTools.Method(typeof(WrapperPatch), nameof(Fail));
                        exceptions++;
                    }
                }
                yield return instruction;
            }
            if (results != 1 || exceptions != 1)
                throw new InvalidOperationException($"Core native resolution completion boundaries changed ({results}, {exceptions}).");
        }
        private static void Complete(ref AsyncTaskMethodBuilder builder) => Finish(ref builder, null);
        private static void Fail(ref AsyncTaskMethodBuilder builder, Exception error) => Finish(ref builder, error);
        private static void Finish(ref AsyncTaskMethodBuilder builder, Exception? error)
        {
            var completion = ActiveBatch.Value?.DisposeAsync() ?? ValueTask.CompletedTask;
            if (completion.IsCompletedSuccessfully)
            {
                completion.GetAwaiter().GetResult();
                CompleteBuilder(ref builder, error);
                return;
            }
            // Materialize the original task before copying the builder for asynchronous cleanup.
            _ = builder.Task;
            _ = FinishAsync(builder, completion, error);
        }
        private static async Task FinishAsync(AsyncTaskMethodBuilder builder, ValueTask completion, Exception? error)
        {
            try { await completion; }
            catch (Exception cleanupError) { error = cleanupError; }
            CompleteBuilder(ref builder, error);
        }
        private static void CompleteBuilder(ref AsyncTaskMethodBuilder builder, Exception? error)
        {
            if (error == null) builder.SetResult();
            else builder.SetException(error);
        }
    }
}
