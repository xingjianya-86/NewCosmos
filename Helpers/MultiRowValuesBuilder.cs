using System.Text;

namespace NewCosmos.Helpers;

/// <summary>
/// 多行 VALUES 位置参数骨架生成器：产出 <c>($1..$p),($(p+1)..$np)</c> 形式的占位骨架与扁平参数数组。
/// 骨架只含 $n 占位符、不含任何值插值（数据库访问规范：值一律走位置参数，批量写用多行 VALUES）。
/// </summary>
public static class MultiRowValuesBuilder
{
    /// <summary>
    /// 构建多行 VALUES 子句与扁平参数数组。
    /// </summary>
    /// <param name="rowCount">行数（必须 &gt; 0）</param>
    /// <param name="paramsPerRow">每行占用的参数个数</param>
    /// <param name="rowArgs">按行号（0 起）取该行参数序列；每行长度必须等于 <paramref name="paramsPerRow"/></param>
    /// <param name="rowTemplate">
    /// 可选自定义行模板，入参为该行首个参数序号（1 起），返回形如 <c>($1,$2,$4,$4)</c> 的行占位；
    /// null 时按顺序编号。用于单列复用占位（如 member_id=rotation_member_id）或行内字面量（NOW()）。
    /// </param>
    public static (string ValuesClause, object?[] Args) Build(
        int rowCount,
        int paramsPerRow,
        Func<int, IEnumerable<object?>> rowArgs,
        Func<int, string>? rowTemplate = null)
    {
        if (rowCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        if (paramsPerRow <= 0)
            throw new ArgumentOutOfRangeException(nameof(paramsPerRow));

        var sb = new StringBuilder(rowCount * (paramsPerRow * 4 + 4));
        var args = new List<object?>(rowCount * paramsPerRow);
        var startIndex = 1;

        for (var r = 0; r < rowCount; r++)
        {
            var row = rowArgs(r).ToArray();
            if (row.Length != paramsPerRow)
                throw new InvalidOperationException(
                    $"第 {r} 行参数个数 {row.Length} 与 paramsPerRow={paramsPerRow} 不符");

            if (r > 0)
                sb.Append(',');

            if (rowTemplate != null)
            {
                sb.Append(rowTemplate(startIndex));
            }
            else
            {
                sb.Append('(');
                for (var i = 0; i < paramsPerRow; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append('$').Append(startIndex + i);
                }
                sb.Append(')');
            }

            args.AddRange(row);
            startIndex += paramsPerRow;
        }

        return (sb.ToString(), args.ToArray());
    }
}
