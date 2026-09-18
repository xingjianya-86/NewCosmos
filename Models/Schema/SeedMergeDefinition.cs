namespace NewCosmos.Models.Schema;

public class SeedMergeDefinition
{
    public string TableName { get; init; } = string.Empty;
    public string Schema { get; init; } = "public";
    public string BusinessKeyColumn { get; init; } = string.Empty;
    public string IdentityColumn { get; init; } = "id";
    public List<string> Columns { get; init; } = new();
}

public class MergeResult
{
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string ErrorMessage { get; set; } = string.Empty;
}
