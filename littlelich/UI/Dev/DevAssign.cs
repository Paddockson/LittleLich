using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>
/// Routes dev assignment commands to the owner's class so its rules apply: research benches ("research"/"bench_N",
/// bench slots), Workshop buildings (slot limits), then skills (level and research locks, via LichManager.AssignVia),
/// then the raw pool for anything else.
/// </summary>
public static class DevAssign
{
    public static long Add(string ownerId, string activityId, long count) =>
        Bench(ownerId, activityId) is int b ? CryptManager.Instance.Research!.AddNecromancer(b, count)
        : CryptManager.Instance.Building(ownerId) is { } bld && activityId == Crypt.Buildings.BuildingBase.StaffActivity
            ? bld.AddNecromancer(count)
            : LichManager.Instance.AssignVia(ownerId, activityId, count);

    public static long Remove(string ownerId, string activityId, long count) =>
        Bench(ownerId, activityId) is int b ? CryptManager.Instance.Research!.RemoveNecromancer(b, count)
        : CryptManager.Instance.Building(ownerId) is { } bld && activityId == Crypt.Buildings.BuildingBase.StaffActivity
            ? bld.RemoveNecromancer(count)
            : LichManager.Instance.UnassignVia(ownerId, activityId, count);

    private static int? Bench(string ownerId, string activityId)
    {
        if (ownerId != Crypt.Research.ResearchManager.Owner || CryptManager.Instance.Research is not { } r) return null;
        var i = 0;
        foreach (var b in r.Benches)
        {
            if (b.ActivityId == activityId) return i;
            i++;
        }
        return null;
    }
}
