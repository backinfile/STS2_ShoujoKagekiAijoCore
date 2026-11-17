using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
public sealed class SignatureModel : AbstractModel
{
 public override bool ShouldReceiveCombatHooks => true;
 public Task BrokenSignature(CardLocation location) => Task.CompletedTask;
 public Task ValidHook() => Task.CompletedTask;
}
