using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ShoujoKageki.Core.Runtime.Modules.Shine.Saves;

[HarmonyPatch]
internal static class SerializablePlayerExtraFields_Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.ToSerializable))]
    private static class Player_ToSerializable_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, ref SerializablePlayer __result)
        {
            __result.ExtraFields ??= new SerializableExtraPlayerFields();

            foreach (var handler in PlayerExtraFieldsHandlers.All)
                handler.WriteSerializableField(__instance, __result.ExtraFields);
        }
    }

    [HarmonyPatch(typeof(SerializableExtraPlayerFields), nameof(SerializableExtraPlayerFields.Serialize))]
    private static class SerializableExtraPlayerFields_Serialize_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(SerializableExtraPlayerFields __instance, PacketWriter writer)
        {
            foreach (var handler in PlayerExtraFieldsHandlers.All)
                handler.SerializeExtraFields(__instance, writer);
        }
    }

    [HarmonyPatch(typeof(SerializableExtraPlayerFields), nameof(SerializableExtraPlayerFields.Deserialize))]
    private static class SerializableExtraPlayerFields_Deserialize_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(SerializableExtraPlayerFields __instance, PacketReader reader)
        {
            foreach (var handler in PlayerExtraFieldsHandlers.All)
                handler.DeserializeExtraFields(__instance, reader);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.FromSerializable))]
    private static class Player_FromSerializable_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(SerializablePlayer save, Player __result)
        {
            if (save.ExtraFields == null) return;

            foreach (var handler in PlayerExtraFieldsHandlers.All)
                handler.RestoreFromSerializableField(__result, save.ExtraFields);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SyncWithSerializedPlayer))]
    private static class Player_SyncWithSerializedPlayer_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, SerializablePlayer player)
        {
            if (player.ExtraFields == null) return;

            foreach (var handler in PlayerExtraFieldsHandlers.All)
                handler.RestoreFromSerializableField(__instance, player.ExtraFields);
        }
    }

}
