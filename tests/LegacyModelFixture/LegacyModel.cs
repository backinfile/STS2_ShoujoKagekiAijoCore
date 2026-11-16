using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
public abstract class LegacyModel : AbstractModel
{
    public Task BrokenHook(PlayerChoiceContext context, Creature creature) => base.AfterDeath(context, creature, false, 0f);
}
