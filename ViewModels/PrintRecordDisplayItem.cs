namespace NewCosmos.ViewModels;

/// <summary>历史打印记录展示项（社会救助/临时救助/普惠高龄补打页面共用）</summary>
public sealed record PrintRecordDisplayItem(
    long Id, string TemplateName, string DisplayTime,
    string OperatorName, string PrinterName, string Status);
