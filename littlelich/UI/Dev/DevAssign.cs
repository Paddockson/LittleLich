using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>
/// Routes dev assignment commands to the owner's class so its rules apply: Workshop buildings (slot limits),
/// then skills (level locks, via LichManager.AssignVia), then the raw pool for anything else.
/// </summary>
public static class DevAssign
{
    public static long Add(string ownerId, string activityId, long count) =>
        CryptManager.Instance.Building(ownerId) is { } b && activityId == Crypt.Buildings.BuildingBase.StaffActivity
            ? b.AddNecromancer(count)
            : LichManager.Instance.AssignVia(ownerId, activityId, count);

    public static long Remove(string ownerId, string activityId, long count) =>
        CryptManager.Instance.Building(ownerId) is { } b && activityId == Crypt.Buildings.BuildingBase.StaffActivity
            ? b.RemoveNecromancer(count)
            : LichManager.Instance.UnassignVia(ownerId, activityId, count);
}
