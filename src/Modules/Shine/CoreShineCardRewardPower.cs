using BaseLib.Abstracts;
using BaseLib.Utils.Attributes;
using MegaCrit.Sts2.Core.Entities.Powers;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

[CustomID("KAREN_SHINE_CARD_REWARD_POWER")]
public sealed class CoreShineCardRewardPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override string? CustomPackedIconPath => V1.CoreApi.ResourceRoot + "images/powers/shine_reward_power.png";
    public override string? CustomBigIconPath => CustomPackedIconPath;
}
