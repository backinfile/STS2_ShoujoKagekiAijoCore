using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using ShoujoKageki.Core.Runtime.Modules.Shine;
using System.Collections.Generic;
using System.Linq;

namespace ShoujoKageki.Core.Runtime.Modules.Shine.Saves;

internal sealed class PlayerShinePileExtraFieldsHandler
    : PlayerExtraFieldsHandlerBase<List<SerializableCard>>
{

    public override string FieldName => "core_player_disposed_pile";

    protected override List<SerializableCard> Collect(Player player) => ShineArchive.Snapshots(player).ToList();
    protected override void Restore(Player player, List<SerializableCard> data) => ShineArchive.Restore(player, data);

    protected override void WriteNetData(PacketWriter writer, List<SerializableCard> data)
    {
        writer.WriteList(data);
    }

    protected override List<SerializableCard> ReadNetData(PacketReader reader)
    {
        return reader.ReadList<SerializableCard>();
    }

    protected override List<SerializableCard> CreateEmptyData()
    {
        return [];
    }
}
