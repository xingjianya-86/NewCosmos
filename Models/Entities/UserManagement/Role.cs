namespace NewCosmos.Models.Entities.UserManagement;

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Level { get; set; }

    /// <summary>数据范围（SELF/ORG/ORG_AND_CHILDREN/ALL），决定角色可访问的业务数据边界</summary>
    public string DataScope { get; set; } = Constants.DataScopeConstants.SELF;
}
