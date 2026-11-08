using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ShoujoKageki.Core.Runtime;
using ShoujoKageki.Core.Runtime.Modules.Shine;
using ShoujoKageki.Core.V1;

if (args.Contains("--configuration-only"))
{
    var configuration = new ShoujoKageki.Core.Runtime.Modules.Configuration.CoreConfiguration();
    var checks = 0;
    void CheckSetting(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        checks++;
    }
    CheckSetting(configuration.Current.DerivedCardLabel == DerivedCardLabelMode.ShoujoOnly, "Derived labels default to Shoujo characters");
    CheckSetting(configuration.Current.ShinePileButton == ShinePileButtonMode.Auto, "Shine pile defaults to Shoujo characters");
    CheckSetting(configuration.Current.EnableShineEffects && configuration.Current.EnableInformationCollection, "New switches default on");
    foreach (var enabled in new[] { false, true })
    {
        configuration.Update(new CoreSettings { EnableShineEffects = enabled });
        CheckSetting(configuration.Current.EnableShineDepletionAnimation == enabled, "Shine effects controls depletion animation");
        configuration.Update(new CoreSettings { EnableShineDepletionAnimation = enabled });
        CheckSetting(configuration.Current.EnableShineEffects == enabled, "Legacy depletion switch aliases Shine effects");
    }
    configuration.Update(new CoreSettings { EnableShineDepletionAnimation = false, EnableShineEffects = true });
    CheckSetting(configuration.Current.EnableShineEffects && configuration.Current.EnableShineDepletionAnimation, "Explicit Shine effects overrides the legacy switch");
    foreach (var mode in Enum.GetValues<CombatCardGainRelicMode>())
    {
        configuration.Update(new CoreSettings { CombatCardGainRelicMode = mode });
        CheckSetting(configuration.Current.BookOfFiveRingsPatch == mode && configuration.Current.LuckyFyshPatch == mode, "Legacy relic setting applies to both relics");
    }
    configuration.Update(new CoreSettings { CombatCardGainRelicMode = CombatCardGainRelicMode.Enabled, BookOfFiveRingsPatch = CombatCardGainRelicMode.Disabled, LuckyFyshPatch = CombatCardGainRelicMode.ShoujoOnly });
    configuration.Update(configuration.Current with { EnableShineEffects = false });
    CheckSetting(configuration.Current.BookOfFiveRingsPatch == CombatCardGainRelicMode.Disabled && configuration.Current.LuckyFyshPatch == CombatCardGainRelicMode.ShoujoOnly, "Independent relic settings survive unrelated updates");
    ShoujoKagekiAijoCore.CoreModConfig.ShowDerivedCardLabel = false;
    CheckSetting(configuration.Current.DerivedCardLabel == DerivedCardLabelMode.Hidden, "Legacy hidden labels stay hidden");
    ShoujoKagekiAijoCore.CoreModConfig.ShowDerivedCardLabel = true;
    CheckSetting(configuration.Current.DerivedCardLabel == DerivedCardLabelMode.ShoujoOnly, "Legacy visible labels migrate to the default scope");
    foreach (var mode in Enum.GetValues<DerivedCardLabelMode>())
    {
        var settings = new CoreSettings { DerivedCardLabel = mode };
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<CoreSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        CheckSetting(roundtrip.DerivedCardLabel == mode, "Derived label scope survives JSON roundtrip");
    }
    var oldRules = System.Text.Json.JsonSerializer.Deserialize<CoreRunRules>("{\"RulesetId\":\"shine@1\",\"RelicMode\":0,\"NetworkFingerprint\":\"old\"}")!;
    CheckSetting((oldRules.BookOfFiveRingsPatch ?? oldRules.RelicMode) == CombatCardGainRelicMode.Enabled && (oldRules.LuckyFyshPatch ?? oldRules.RelicMode) == CombatCardGainRelicMode.Enabled, "Old rule snapshots retain the shared mode");
    var rules = oldRules with { BookOfFiveRingsPatch = CombatCardGainRelicMode.Disabled, LuckyFyshPatch = CombatCardGainRelicMode.ShoujoOnly };
    var restored = System.Text.Json.JsonSerializer.Deserialize<CoreRunRules>(System.Text.Json.JsonSerializer.Serialize(rules))!;
    CheckSetting(restored == rules, "Independent rule snapshots roundtrip");
    configuration.Update(new CoreSettings { DerivedCardLabel = (DerivedCardLabelMode)99, BookOfFiveRingsPatch = (CombatCardGainRelicMode)99, LuckyFyshPatch = (CombatCardGainRelicMode)99 });
    CheckSetting(configuration.Current.DerivedCardLabel == DerivedCardLabelMode.ShoujoOnly && configuration.Current.BookOfFiveRingsPatch == CombatCardGainRelicMode.ShoujoOnly && configuration.Current.LuckyFyshPatch == CombatCardGainRelicMode.ShoujoOnly, "Invalid settings recover defaults");
    var servicesForConfig = new CoreServices();
    servicesForConfig.Add<ICoreConfiguration>(configuration);
    var telemetry = typeof(ShoujoKageki.Core.Telemetry.TelemetryModule);
    telemetry.GetProperty("Context", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, new CoreContext(servicesForConfig, "test", "test", ""));
    bool DataEnabled() => (bool)telemetry.GetProperty("DataUploadEnabled", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    bool ErrorsEnabled() => (bool)telemetry.GetProperty("ErrorUploadEnabled", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    foreach (var master in new[] { false, true })
    foreach (var data in new[] { false, true })
    foreach (var errors in new[] { false, true })
    {
        configuration.Update(new CoreSettings { EnableInformationCollection = master, EnableDataUpload = data, EnableErrorUpload = errors });
        CheckSetting(DataEnabled() == (master && data) && ErrorsEnabled() == (master && errors), "Collection master gates both channels");
        CheckSetting(configuration.Current.EnableDataUpload == data && configuration.Current.EnableErrorUpload == errors, "Collection master preserves child switches");
    }
    Console.WriteLine($"Configuration checks passed: {checks}");
    return;
}

var services = new CoreServices();
services.Add<ICoreDiscovery>(new CoreDiscovery());
var context = new CoreContext(services, "1.1.0", "test", "");
new ShoujoKageki.Core.Runtime.Modules.Execution.ModelExecutionModule().Initialize(context);
var service = new ShineService();
service.Initialize(context);
CoreApi.Bind(services);
if (args.Contains("--routing-only"))
{
    new ShoujoKageki.Core.Runtime.Modules.Execution.CardPlayRoutingModule().Initialize(context);
    var routingCard = new SynchronousFailureCard();
    var expected = SynchronousFailureCard.Failure;
    void CheckFailure(Action invoke, string name)
    {
        try { invoke(); }
        catch (Exception actual) when (ReferenceEquals(actual, expected)) { Console.WriteLine(name + ": passed"); return; }
        throw new InvalidOperationException(name);
    }
    CheckFailure(() => ShoujoKageki.Core.Runtime.Modules.Execution.CardPlayRoutingModule.Dispatch(routingCard, null!, null!), "Native routing preserves synchronous exception identity");
    Func<CardModel, PlayerChoiceContext, CardPlay, Task> original = (_, _, _) => throw expected;
    CheckFailure(() => ShoujoKageki.Core.Runtime.Modules.Execution.CardPlayRoutingModule.DispatchFromHook(original, routingCard, null!, null!), "Hook routing preserves synchronous exception identity");
    return;
}
if (args.Contains("--copies-only"))
{
    var copies = new CardCopyService();
    copies.Initialize(context);
    var patcher = new Harmony("Core.CopyChecks");
    patcher.CreateClassProcessor(typeof(ShineService).GetNestedType("ClonePatch", BindingFlags.NonPublic)!).Patch();
    ModelDb.Inject(typeof(ResolutionCard));
    ModelDb.Inject(typeof(CoreEmptyShell));
    var source = (ResolutionCard)ModelDb.Card<ResolutionCard>().MutableClone();
    source.RuntimeValue = 17;
    source.EnergyCost.SetThisCombat(4);
    service.SetState(source, 2, 6);
    var snapshot = CardCopies.Snapshot(source);
    var temporary = (ResolutionCard)CardCopies.Preview(source, CardCopyPurpose.TemporaryCopy);
    if (snapshot.RuntimeValue != 17 || temporary.RuntimeValue != 17 || snapshot.EnergyCost.GetWithModifiers(CostModifiers.Local) != 4
        || temporary.EnergyCost.GetWithModifiers(CostModifiers.Local) != 4 || ReferenceEquals(source, snapshot))
        throw new InvalidOperationException("Runtime snapshots and combat previews must preserve local card data and cost.");
    if (service.GetState(temporary) is not { Remaining: 2, Maximum: 6, Temporary: true })
        throw new InvalidOperationException("Temporary copies need an independent Shine budget.");
    temporary.RuntimeValue = 29;
    if (source.RuntimeValue != 17) throw new InvalidOperationException("The copy must not mutate the source.");
    service.SetState(source, 0, 6);
    var depletedSnapshot = CardCopies.Snapshot(source);
    if (depletedSnapshot.RuntimeValue != 17 || service.GetState(depletedSnapshot)?.Remaining != 0)
        throw new InvalidOperationException("An internal snapshot must retain a depleted card's type and values.");
    if (source.MutableClone() is not CoreEmptyShell || copies.Preview(source, CardCopyPurpose.Transfer) is not CoreEmptyShell)
        throw new InvalidOperationException("An actual exhausted-card copy still creates an empty shell.");
    _ = CardCopies.Snapshot(source);
    if (source.MutableClone() is not CoreEmptyShell) throw new InvalidOperationException("Snapshot permissions must not escape their invocation.");
    Console.WriteLine("Copy lifecycle checks passed: runtime data, local cost, independence, Shine budgets, depleted snapshots and empty shells.");
    return;
}
var harmony = new Harmony("Core.ResolutionChecks");
var serviceType = typeof(ShineService);
Type Patch(string name) => serviceType.GetNestedType(name, BindingFlags.NonPublic)!;
HarmonyMethod Hook(string patch, string name) => new(AccessTools.Method(Patch(patch), name));
var wrapper = AccessTools.Method(typeof(ResolutionCard), nameof(ResolutionCard.NativeWrapper));
if (args.Contains("--presentation-only"))
{
    ModelDb.Inject(typeof(ResolutionCard));
    ModelDb.Inject(typeof(PlainCard));
    var canonical = ModelDb.Card<ResolutionCard>();
    var preview = (ResolutionCard)canonical.MutableClone();
    var ordinary = (PlainCard)ModelDb.Card<PlainCard>().MutableClone();
    void CheckWarning(bool expected, string name, CardModel model, bool alreadyDecreased = false)
    {
        if (ShoujoKageki.Core.Runtime.Modules.Presentation.ShineVfx.WillDeplete(model, alreadyDecreased) != expected)
            throw new InvalidOperationException(name);
    }
    CheckWarning(false, "Canonical previews have no warning", canonical);
    CheckWarning(false, "Ordinary cards have no warning", ordinary);
    service.SetState(preview, 2, 3);
    CheckWarning(false, "Two remaining uses have no warning", preview);
    service.SetState(preview, 1, 3);
    CheckWarning(true, "The final use warns before deduction", preview);
    CheckWarning(false, "A deducted use waits until zero", preview, true);
    service.SetState(preview, 0, 3);
    CheckWarning(true, "Zero remaining uses warn", preview, true);
    service.SetState(preview, 2, 3);
    service.DepleteAfterCurrentResolution(preview);
    CheckWarning(true, "Pending depletion warns with remaining uses", preview, true);
    preview.AddKeyword(CardKeyword.Eternal);
    CheckWarning(false, "Eternal cards suppress the warning", preview);
    Console.WriteLine("Shine warning checks passed: 8");
    return;
}
if (args.Contains("--save-only"))
{
    harmony.CreateClassProcessor(typeof(ShineService).Assembly.GetType("ShoujoKageki.Core.Runtime.Modules.Shine.ShineService+LegacyCardStatePatch")!).Patch();
    harmony.CreateClassProcessor(Patch("UpgradePatch")).Patch();
    harmony.CreateClassProcessor(Patch("ClonePatch")).Patch();
    harmony.CreateClassProcessor(typeof(BaseLib.Utils.SpireField<,>).Assembly.GetType("BaseLib.Patches.Utils.SavedSpireFieldPatch")!).Patch();
    ModelDb.Inject(typeof(ResolutionCard));
    ModelDb.Inject(typeof(PlainCard));
    foreach (int upgrades in new[] { 0, 1, 2 })
    {
        var save = new MegaCrit.Sts2.Core.Saves.Runs.SerializableCard
        {
            Id = ModelDb.GetId<ResolutionCard>(), CurrentUpgradeLevel = upgrades,
            Props = new() { ints = [new("CardModel_core_shine_current", 2), new("CardModel_core_shine_max", 6)] }
        };
        var restored = CardModel.FromSerializable(save);
        if (restored.CurrentUpgradeLevel != upgrades || service.GetState(restored) is not { Remaining: 2, Maximum: 6 })
            throw new InvalidOperationException("Restoring upgrades must preserve the saved Shine budget.");
        restored.UpgradeInternal();
        if (service.GetState(restored) is not { Remaining: 6 })
            throw new InvalidOperationException("A real upgrade must still refill Shine.");
    }
    var legacySave = new MegaCrit.Sts2.Core.Saves.Runs.SerializableCard
    {
        Id = ModelDb.GetId<ResolutionCard>(), CurrentUpgradeLevel = 1,
        Props = new() { ints = [new("CardModel_karen_shine_current", 2), new("CardModel_karen_shine_max", 6)] }
    };
    if (service.GetState(CardModel.FromSerializable(legacySave)) is not { Remaining: 2, Maximum: 6 })
        throw new InvalidOperationException("Legacy upgraded cards must preserve their saved budget.");
    var plainSave = new MegaCrit.Sts2.Core.Saves.Runs.SerializableCard { Id = ModelDb.GetId<PlainCard>(), CurrentUpgradeLevel = 1 };
    if (service.GetState(CardModel.FromSerializable(plainSave)) != null)
        throw new InvalidOperationException("An upgraded ordinary card must retain absent Shine state.");
    legacySave.Props!.ints!.Add(new("CardModel_core_shine_current", 1));
    legacySave.Props.ints.Add(new("CardModel_core_shine_max", 4));
    if (service.GetState(CardModel.FromSerializable(legacySave)) is not { Remaining: 1, Maximum: 4 })
        throw new InvalidOperationException("Core fields must take precedence when legacy fields coexist.");
    legacySave.CurrentUpgradeLevel = 4;
    try { CardModel.FromSerializable(legacySave); throw new Exception("Invalid upgrade count was accepted."); }
    catch (InvalidOperationException) { }
    var afterFailure = ModelDb.Card<ResolutionCard>().ToMutable();
    service.SetState(afterFailure, 1, 6);
    afterFailure.UpgradeInternal();
    if (service.GetState(afterFailure) is not { Remaining: 6 })
        throw new InvalidOperationException("A failed read must release the upgrade suppression scope.");
    Console.WriteLine("Save restoration and real upgrade checks passed.");
    return;
}
if (args.Contains("--legacy-only"))
{
    ModelDb.Inject(typeof(PlainCard));
    var plain = (PlainCard)ModelDb.Card<PlainCard>().MutableClone();
    var legacy = AccessTools.Method(Patch("LegacyCardStatePatch"), "Postfix");
    var save = new MegaCrit.Sts2.Core.Saves.Runs.SerializableCard
    {
        Id = plain.Id,
        Props = new() { ints = [new("CardModel_karen_shine_current", -1), new("CardModel_karen_shine_max", -1)] }
    };
    service.SetState(plain, 0, 0);
    legacy.Invoke(null, [save, plain]);
    Console.WriteLine($"Old ordinary card retains absent Shine state: {service.GetState(plain) == null}");
    if (service.GetState(plain) != null) throw new InvalidOperationException("Old -1 sentinels must restore an ordinary card without Shine.");
    service.SetState(plain, 0, 0);
    save.Props = new();
    legacy.Invoke(null, [save, plain]);
    if (service.GetState(plain) != null) throw new InvalidOperationException("An older ordinary card with no Shine fields must stay ordinary.");
    save.Props = new() { ints = [new("CardModel_karen_shine_current", 3), new("CardModel_karen_shine_max", 9)] };
    legacy.Invoke(null, [save, plain]);
    if (service.GetState(plain) is not { Remaining: 3, Maximum: 9 }) throw new InvalidOperationException("Old positive Shine values must be preserved.");
    Console.WriteLine("Legacy state checks passed.");
    return;
}
var machine = wrapper.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
harmony.Patch(AccessTools.Method(machine, "MoveNext"), Hook("WrapperPatch", "Prefix"), Hook("WrapperPatch", "Postfix"),
    Hook("WrapperPatch", "Transpiler"), Hook("WrapperPatch", "Finalizer"));
harmony.Patch(AccessTools.Method(typeof(ResolutionCard), nameof(ResolutionCard.Before)), postfix: Hook("NativePermitPatch", "Postfix"));
harmony.Patch(AccessTools.Method(typeof(ResolutionCard), "OnPlay"),
    new HarmonyMethod(typeof(ShoujoKageki.Core.Runtime.Modules.Execution.ModelExecutionModule), "CardPrefix"),
    new HarmonyMethod(typeof(ShoujoKageki.Core.Runtime.Modules.Execution.ModelExecutionModule), "CardPostfix"),
    finalizer: new HarmonyMethod(typeof(ShoujoKageki.Core.Runtime.Modules.Execution.ModelExecutionModule), "Finalizer"));

ModelDb.Inject(typeof(ResolutionCard)); var card = (ResolutionCard)ModelDb.Card<ResolutionCard>().MutableClone();
service.SetState(card, 3, 3);
await (Task)wrapper.Invoke(card, [null])!;
Console.WriteLine($"Non-inlined native play uses Shine 3 -> {service.GetState(card)?.Remaining}, effects {card.Effects}");
if (service.GetState(card)?.Remaining != 2) throw new InvalidOperationException("Non-inlined baseline must consume one use.");
service.SetState(card, 3, 3);
card.Effects = 0;
await StartInlined(wrapper, card);
var remaining = service.GetState(card)?.Remaining;
Console.WriteLine($"Inlined native play uses Shine 3 -> {remaining}, effects {card.Effects}");
if (remaining != 2 || card.Effects != 1) throw new InvalidOperationException("Inlined native play must consume exactly one use.");


void Check(string label, bool passed)
{
    Console.WriteLine($"{label}: {passed}");
    if (!passed) throw new InvalidOperationException(label);
}
service.SetState(card, 5, 5);
card.Effects = 0;
card.Plays = 3;
await StartInlined(wrapper, card);
Check("Every native replay consumes one use", service.GetState(card)?.Remaining == 2 && card.Effects == 3);
card.Plays = 1;
card.YieldBefore = false;
card.YieldEffect = false;
card.Effects = 0;
service.SetState(card, 3, 3);
await StartInlined(wrapper, card);
Check("Synchronous state-machine completion", service.GetState(card)?.Remaining == 2 && card.Effects == 1);
card.YieldBefore = true;
card.YieldEffect = true;
var nested = (ResolutionCard)ModelDb.Card<ResolutionCard>().MutableClone();
service.SetState(card, 5, 5);
service.SetState(nested, 3, 3);
card.Nested = nested;
await StartInlined(wrapper, card);
Check("Nested native plays isolate their budgets", service.GetState(card)?.Remaining == 4 && service.GetState(nested)?.Remaining == 2);
service.SetState(card, 5, 5);
card.Effects = 0;
card.Nested = card;
await StartInlined(wrapper, card);
Check("A nested native replay of the same card counts", service.GetState(card)?.Remaining == 3 && card.Effects == 2);
service.SetState(card, 3, 3);
await service.ResolveAsync(card, card.Effect, CardResolutionKind.Effect);
Check("An extra Effect preserves the budget", service.GetState(card)?.Remaining == 3);
service.RegisterDeferredResolutionPredicate(model => model is ResolutionCard { Deferred: true });
card.Deferred = true;
card.Effects = 0;
await StartInlined(wrapper, card);
Check("Arrow deployment preserves the budget", service.GetState(card)?.Remaining == 3 && card.Effects == 0);
await service.ResolveAsync(card, card.Effect, CardResolutionKind.Arrow);
Check("Actual arrow resolution consumes one use", service.GetState(card)?.Remaining == 2 && card.Effects == 1);
card.Deferred = false;
card.FailEffect = true;
service.SetState(card, 3, 3);
bool originalFailure = false;
try { await StartInlined(wrapper, card); }
catch (InvalidOperationException error) when (error.Message == "expected effect failure") { originalFailure = true; }
Check("Exception completion preserves the original failure", originalFailure && service.GetState(card)?.Remaining == 2);
card.FailEffect = false;
service.SetState(card, 3, 3);
await service.ResolveAsync(card, card.Effect, CardResolutionKind.Arrow);
Check("A failed native batch releases its context", service.GetState(card)?.Remaining == 2);
Console.WriteLine("Native resolution checks passed.");

static Task StartInlined(MethodInfo wrapper, ResolutionCard card)
{
    // Emit the compiler's async wrapper body without calling the patchable wrapper method.
    var stateMachine = wrapper.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
    var state = AccessTools.Field(stateMachine, "<>1__state");
    var builder = AccessTools.Field(stateMachine, "<>t__builder");
    var instance = AccessTools.Field(stateMachine, "<>4__this");
    var method = new DynamicMethod("InlinedNativeWrapper", typeof(Task), [typeof(ResolutionCard)], typeof(Program).Module, true);
    var il = method.GetILGenerator();
    il.DeclareLocal(stateMachine);
    il.Emit(OpCodes.Ldloca_S, (byte)0);
    il.Emit(OpCodes.Initobj, stateMachine);
    il.Emit(OpCodes.Ldloca_S, (byte)0);
    il.Emit(OpCodes.Ldc_I4_M1);
    il.Emit(OpCodes.Stfld, state);
    il.Emit(OpCodes.Ldloca_S, (byte)0);
    il.Emit(OpCodes.Call, AccessTools.Method(typeof(AsyncTaskMethodBuilder), nameof(AsyncTaskMethodBuilder.Create)));
    il.Emit(OpCodes.Stfld, builder);
    il.Emit(OpCodes.Ldloca_S, (byte)0);
    il.Emit(OpCodes.Ldarg_0);
    il.Emit(OpCodes.Stfld, instance);
    il.Emit(OpCodes.Ldloca_S, (byte)0);
    il.Emit(OpCodes.Ldflda, builder);
    il.Emit(OpCodes.Ldloca_S, (byte)0);
    il.Emit(OpCodes.Call, AccessTools.Method(typeof(AsyncTaskMethodBuilder), nameof(AsyncTaskMethodBuilder.Start)).MakeGenericMethod(stateMachine));
    il.Emit(OpCodes.Ldloca_S, (byte)0);
    il.Emit(OpCodes.Ldflda, builder);
    il.Emit(OpCodes.Call, AccessTools.PropertyGetter(typeof(AsyncTaskMethodBuilder), nameof(AsyncTaskMethodBuilder.Task)));
    il.Emit(OpCodes.Ret);
    return method.CreateDelegate<Func<ResolutionCard, Task>>()(card);
}

public abstract class ResolutionWrapperBase() : CardModel(0, CardType.Skill, CardRarity.Token, TargetType.Self)
{
    public int Effects;
    public int Plays = 1;
    public bool YieldBefore = true;
    public bool YieldEffect = true;
    public bool Deferred;
    public bool FailEffect;
    public ResolutionCard? Nested;
    public async Task NativeWrapper(PlayerChoiceContext? choiceContext = null)
    {
        if (YieldBefore) await Task.Yield();
        for (int index = 0; index < Plays; index++)
        {
            var play = new CardPlay
            {
                Card = this, Target = null, ResultPile = PileType.Exhaust,
                Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
                IsAutoPlay = true, PlayIndex = index, PlayCount = Plays,
#if STS2_BETA
                Player = null!,
#endif
            };
            await Before(play);
            await OnPlay(choiceContext!, play);
        }
    }
    public static async Task Before(CardPlay cardPlay)
    {
        if (((ResolutionWrapperBase)cardPlay.Card).YieldBefore) await Task.Yield();
    }
}

[ShineCard(3)]
public sealed class ResolutionCard : ResolutionWrapperBase
{
    public int RuntimeValue;
    public override int MaxUpgradeLevel => 3;
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Deferred ? Task.CompletedTask : Effect();
    public async Task Effect()
    {
        Effects++;
        if (YieldEffect) await Task.Yield();
        if (Nested is { } nested)
        {
            Nested = null;
            await nested.NativeWrapper();
        }
        if (FailEffect) throw new InvalidOperationException("expected effect failure");
    }
}

public sealed class PlainCard() : CardModel(0, CardType.Skill, CardRarity.Token, TargetType.Self) { }

public sealed class SynchronousFailureCard() : CardModel(0, CardType.Skill, CardRarity.Token, TargetType.Self)
{
    public static readonly Exception Failure = new IOException("expected synchronous failure");
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play) => throw Failure;
}
