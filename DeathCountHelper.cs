
namespace MentorRouletteTracker
{
    public static class DeathCountHelper
    {
        public static bool IsPvP(DutyRecord record)
        {
            return record.DutyType == "PvP";
        }

        public static int? GetDeaths(
            DutyRecord record,
            PartyMemberSnapshot player)
        {
            return IsPvP(record)
                ? player.PvPScoreboardDeaths
                : player.DeathCount;
        }
    }
}