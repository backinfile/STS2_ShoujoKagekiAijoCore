namespace ShoujoKageki.Core.Runtime.Modules.Shine.Saves;

/// <summary>
/// 所有玩家扩展字段处理器的统一注册表。
///
/// 本地存档补丁和联机序列化补丁都只遍历这里，
/// 因此新增一个玩家扩展字段时，通常只需要：
/// 1. 新建一个 PlayerExtraFieldsHandlerBase&lt;TData&gt; 子类
/// 2. 在这里注册
/// </summary>
internal static class PlayerExtraFieldsHandlers
{
    public static readonly PlayerExtraFieldsHandlerBase[] All =
    [
        new PlayerShinePileExtraFieldsHandler(),
        new PlayerRuleFieldsHandler()
    ];
}
