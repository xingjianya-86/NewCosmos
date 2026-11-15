using NewCosmos.Helpers;
using System.Data;
using System.Globalization;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// ValueTuple 行映射（2026-11-15 修复：元组查询此前恒返回默认值）。
/// 覆盖：类型判定、8 元组 Rest 展开、列序位置映射、DBNull 保持 default、DateOnly→DateTime、数值转换。
/// </summary>
public class TupleRowMapperTests
{
    [Fact]
    public void IsValueTuple_识别各元组与非元组()
    {
        Assert.True(TupleRowMapper.IsValueTuple(typeof((long Id, string? IdCard))));
        Assert.True(TupleRowMapper.IsValueTuple(typeof((int A, string B, long C, double D, bool E, DateTime F, decimal G, string H))));
        Assert.False(TupleRowMapper.IsValueTuple(typeof(string)));
        Assert.False(TupleRowMapper.IsValueTuple(typeof(int)));
        Assert.False(TupleRowMapper.IsValueTuple(typeof(List<int>)));
        Assert.False(TupleRowMapper.IsValueTuple(typeof(int?)));
        Assert.False(TupleRowMapper.IsValueTuple(null));
    }

    [Fact]
    public void GetFlattenedFields_按Item序_八元组Rest展开为第8元素()
    {
        var fields = TupleRowMapper.GetFlattenedFields(
            typeof((int A, string B, long C, double D, bool E, DateTime F, decimal G, string H)));

        // 前 7 个是本体 Item1..Item7；第 8 个来自 Rest（其字段名即 Item1），位置序才是映射依据
        Assert.Equal(8, fields.Length);
        Assert.Equal(
            new[] { "Item1", "Item2", "Item3", "Item4", "Item5", "Item6", "Item7" },
            fields.Take(7).Select(f => f.Name).ToArray());
        Assert.Equal(typeof(string), fields[7].FieldType);
    }

    [Fact]
    public void MapRow_八元组_Rest位置正确落值()
    {
        var record = new FakeRecord(
            new[] { "c1", "c2", "c3", "c4", "c5", "c6", "c7", "c8" },
            new object?[] { 1, "a", 2L, 3.5d, true, new DateOnly(2026, 1, 2), 4.5m, "尾" });

        var row = TupleRowMapper.MapRow<(int A, string B, long C, double D, bool E, DateTime F, decimal G, string H)>(
            record);

        Assert.Equal(1, row.A);
        Assert.Equal("a", row.B);
        Assert.Equal(2L, row.C);
        Assert.Equal(3.5d, row.D);
        Assert.True(row.E);
        Assert.Equal(new DateTime(2026, 1, 2), row.F);
        Assert.Equal(4.5m, row.G);
        Assert.Equal("尾", row.H);   // 第 8 元素 = Rest.Item1 位置映射
    }

    [Fact]
    public void ConvertValue_日期数值字符串()
    {
        Assert.Equal(new DateTime(2026, 11, 15),
            TupleRowMapper.ConvertValue(new DateOnly(2026, 11, 15), typeof(DateTime)));
        Assert.Equal(new DateTime(2026, 11, 15, 9, 42, 0),
            TupleRowMapper.ConvertValue(new DateTime(2026, 11, 15, 9, 42, 0), typeof(DateTime?)));
        Assert.Equal(42L, TupleRowMapper.ConvertValue(42, typeof(long)));
        Assert.Equal("张三", TupleRowMapper.ConvertValue("张三", typeof(string)));
        Assert.Equal(126.5d, TupleRowMapper.ConvertValue(126.5m, typeof(double)));
    }

    [Fact]
    public void MapRow_列序位置映射_DBNull保持默认()
    {
        var record = new FakeRecord(
            new[] { "id", "applicant_id_card", "survey_date", "optional_missing" },
            new object?[] { 42, "张三", new DateOnly(2026, 11, 15), DBNull.Value });

        var row = TupleRowMapper.MapRow<(long Id, string? Name, DateTime? Date, string? Missing)>(record);

        Assert.Equal(42L, row.Id);
        Assert.Equal("张三", row.Name);
        Assert.Equal(new DateTime(2026, 11, 15), row.Date);
        Assert.Null(row.Missing);   // DBNull → 元素保持 default
    }

    [Fact]
    public void MapRow_缺列保持默认_多列忽略()
    {
        // 3 列读取器映射 4 元组 → 第 4 元素保持默认；第 5 元素超出读取器列数不读
        var record = new FakeRecord(new[] { "a", "b", "c" }, new object?[] { 1, "x", 2.5d });
        var row = TupleRowMapper.MapRow<(int A, string? B, double C, long D)>(record);

        Assert.Equal(1, row.A);
        Assert.Equal("x", row.B);
        Assert.Equal(2.5d, row.C);
        Assert.Equal(0L, row.D);
    }

    /// <summary>最小 IDataRecord 假件：仅实现映射用到的成员，其余按接口要求存在。</summary>
    private sealed class FakeRecord : IDataRecord
    {
        private readonly string[] _names;
        private readonly object?[] _values;

        public FakeRecord(string[] names, object?[] values)
        {
            _names = names;
            _values = values;
        }

        public int FieldCount => _values.Length;
        public object this[int i] => GetValue(i);
        public object this[string name] => GetValue(GetOrdinal(name));
        public string GetName(int i) => _names[i];
        public int GetOrdinal(string name) => Array.IndexOf(_names, name);
        public Type GetFieldType(int i) => _values[i]?.GetType() ?? typeof(object);
        public string GetDataTypeName(int i) => GetFieldType(i).Name;
        public object GetValue(int i) => (i >= _values.Length ? null : _values[i]) ?? DBNull.Value;
        public int GetValues(object[] values)
        {
            var n = Math.Min(values.Length, _values.Length);
            Array.Copy(_values, values, n);
            return n;
        }
        public IDataReader GetData(int i) => throw new NotSupportedException();
        public bool IsDBNull(int i) => i >= _values.Length || _values[i] is null or DBNull;

        public bool GetBoolean(int i) => Convert.ToBoolean(GetValue(i), CultureInfo.InvariantCulture);
        public byte GetByte(int i) => Convert.ToByte(GetValue(i), CultureInfo.InvariantCulture);
        public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => 0;
        public char GetChar(int i) => Convert.ToChar(GetValue(i), CultureInfo.InvariantCulture);
        public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => 0;
        public DateTime GetDateTime(int i) => Convert.ToDateTime(GetValue(i), CultureInfo.InvariantCulture);
        public decimal GetDecimal(int i) => Convert.ToDecimal(GetValue(i), CultureInfo.InvariantCulture);
        public double GetDouble(int i) => Convert.ToDouble(GetValue(i), CultureInfo.InvariantCulture);
        public float GetFloat(int i) => Convert.ToSingle(GetValue(i), CultureInfo.InvariantCulture);
        public Guid GetGuid(int i) => (Guid)GetValue(i)!;
        public short GetInt16(int i) => Convert.ToInt16(GetValue(i), CultureInfo.InvariantCulture);
        public int GetInt32(int i) => Convert.ToInt32(GetValue(i), CultureInfo.InvariantCulture);
        public long GetInt64(int i) => Convert.ToInt64(GetValue(i), CultureInfo.InvariantCulture);
        public string GetString(int i) => Convert.ToString(GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
