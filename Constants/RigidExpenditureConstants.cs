namespace NewCosmos.Constants;

public static class RigidExpenditureConstants
{
    public static string GetDescription(string code) =>
        Helpers.DictDisplayHelper.GetRigidExpenditureDisplay(code);
}
