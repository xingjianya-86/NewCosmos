using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

public class PrintRecord
{
    public long Id { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public long? BusinessId { get; set; }
    /// <summary>
    /// 模板ID。注意：数据库列 template_id 为 character varying(100)（历史遗留），
    /// 写入时须以 ToString() 传参；读取时由反射映射器 Convert.ChangeType 将数字字符串转回 long。
    /// </summary>
    public long TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;

    public byte[] PdfData { get; set; } = [];
    public long PdfSize { get; set; }
    public byte[] SourceData { get; set; } = [];
    public string SourceType { get; set; } = string.Empty;
    public long SourceSize { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string PdfPath { get; set; } = string.Empty;

    public string PrinterName { get; set; } = string.Empty;
    public int Copies { get; set; } = 1;
    public int? OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    /// <summary>
    /// 申请人身份证号（留痕归属标记）：证明类打印 business_id 跨多套 id 空间
    /// （当前库/5 张导入台账），按此列区分归属，防 id 碰撞串台。NULL=无归属。
    /// </summary>
    public string ApplicantIdCard { get; set; } = string.Empty;
    public string Status { get; set; } = ApplicationStatusCodes.COMPLETED;
    public string Remark { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }
}
