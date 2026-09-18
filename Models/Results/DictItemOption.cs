namespace NewCosmos.Models.Results;

/// <summary>
/// 字典选项（用于 Picker 绑定）
/// Key 存入数据库，Display 显示给用户
/// </summary>
public class DictItemOption
{
    public string Key { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;

    public override string ToString() => Display;

    public override bool Equals(object? obj)
    {
        if (obj is DictItemOption other)
            return Key == other.Key;
        return false;
    }

    public override int GetHashCode() => Key.GetHashCode();
}
