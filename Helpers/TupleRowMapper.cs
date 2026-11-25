using System.Data;
using System.Globalization;
using System.Reflection;

namespace NewCosmos.Helpers;

/// <summary>
/// ValueTuple 行映射（2026-11-15 修复既有 Bug）：DB 行 → 元组按 **SELECT 列顺序位置映射**
/// 到 Item1..ItemN。
/// <para>根因：ValueTuple 只有公共字段（Item1..ItemN + Rest）、无可写公共属性，而行映射走
/// <c>GetProperties()</c> 恒取空数组 → 元组查询永远返回默认值（运行实证 props=0 fields=Item1,Item2）。
/// 位序前提：调用点 SELECT 列序必须与元组元素序一致——现有 3 处调用点已逐一核对：
/// ApplicationService.GetClassificationsByIdCardsAsync（applicant_id_card, classification_result）、
/// ApplicationService.CompleteArchiveAsync（id, classification_result, applicant_id_card）、
/// FamilyMemberService 批量添加（RETURNING id, id_card）。</para>
/// <para>规则：DBNull / 缺列 → 元素保持 default；PostgreSQL date（Npgsql 10 → DateOnly）→ DateTime；
/// 单字段转换失败跳过该字段（与实体映射「跳过转换失败字段」同口径，此处为纯函数无日志）。
/// 禁止新增元组调用点而不核对列序。</para>
/// </summary>
public static class TupleRowMapper
{
    /// <summary>是否 ValueTuple 类型（含 8 元组的 TRest 形态）。</summary>
    public static bool IsValueTuple(Type? type) =>
        type is { IsValueType: true, IsGenericType: true, Namespace: "System" } &&
        type.Name.StartsWith("ValueTuple", StringComparison.Ordinal);

    /// <summary>按 Item 序展开字段（8 元组的 Rest 递归展开为第 8 个及以后元素）。</summary>
    public static FieldInfo[] GetFlattenedFields(Type tupleType)
    {
        var fields = new List<FieldInfo>();
        Collect(tupleType, fields);
        return fields.ToArray();

        static void Collect(Type type, List<FieldInfo> output)
        {
            output.AddRange(GetItemFields(type));
            var rest = type.GetField("Rest", BindingFlags.Public | BindingFlags.Instance);
            if (rest != null && IsValueTuple(rest.FieldType))
                Collect(rest.FieldType, output);
        }
    }

    /// <summary>本层 Item1..ItemN 字段（按序；Rest 不在本层）。</summary>
    private static FieldInfo[] GetItemFields(Type tupleType) =>
        tupleType.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.Name.StartsWith("Item", StringComparison.Ordinal))
            .OrderBy(f => int.Parse(f.Name.AsSpan(4), CultureInfo.InvariantCulture))
            .ToArray();

    /// <summary>单值转换（日期/可空/数值的行级适配，口径同实体映射）。</summary>
    public static object ConvertValue(object raw, Type targetType)
    {
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (raw is DateOnly dateOnly)
        {
            var dt = dateOnly.ToDateTime(TimeOnly.MinValue);
            return target == typeof(DateTime) ? dt : Convert.ChangeType(dt, target, CultureInfo.InvariantCulture);
        }
        if (target == typeof(DateTime) && raw is DateTime dateTime) return dateTime;
        if (target == typeof(string)) return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
        if (target.IsInstanceOfType(raw)) return raw;
        return Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 把读取器当前行按列序映射进元组（超出元组长度的列忽略）。
    /// 8 元组经 Rest 逐层映射——Item/Rest 均为**嵌套只读字段**，须先改 Rest 的装箱实例再写回外层
    /// （对最外层对象直接 SetValue 内层字段会抛 ArgumentException，位置因此落空）。
    /// </summary>
    public static T MapRow<T>(IDataRecord reader)
    {
        // 必须显式声明为 object：var 会推断成 T（值类型），SetValue 改的是装箱副本、映射落空
        object boxed = Activator.CreateInstance<T>()!;   // T=元组值类型，恒可创建
        var column = 0;
        MapLevel(boxed, typeof(T), reader, ref column);
        return (T)boxed;
    }

    private static void MapLevel(object target, Type tupleType, IDataRecord reader, ref int column)
    {
        foreach (var item in GetItemFields(tupleType))
        {
            if (column >= reader.FieldCount) return;

            var raw = reader.IsDBNull(column) ? null : reader.GetValue(column);
            column++;
            if (raw is null || raw == DBNull.Value) continue;   // 可空元素保持 default(null)

            try
            {
                item.SetValue(target, ConvertValue(raw, item.FieldType));
            }
            catch (Exception ex)
            {
                // 与实体映射同口径：单字段转换失败跳过（保持 default），不让整行失败；
                // 但必须留痕，否则关键列（身份证/分类）静默变 null 无从定位。
                Serilog.Log.Debug(ex, "[TupleRowMapper] 字段转换失败，保持 default: Field={Field}, SourceType={SourceType}",
                    item.Name, raw?.GetType().Name);
            }
        }

        var rest = tupleType.GetField("Rest", BindingFlags.Public | BindingFlags.Instance);
        if (rest == null || !IsValueTuple(rest.FieldType) || column >= reader.FieldCount) return;

        // 元组 Rest 为值类型字段：GetValue 装箱实例或新建默认实例，恒非 null
        object restBoxed = rest.GetValue(target) ?? Activator.CreateInstance(rest.FieldType)!;
        MapLevel(restBoxed, rest.FieldType, reader, ref column);
        try
        {
            rest.SetValue(target, restBoxed);
        }
        catch (Exception)
        {
            // 写回失败保持默认（正常路径不会发生，兜底不让整行失败）
        }
    }
}
