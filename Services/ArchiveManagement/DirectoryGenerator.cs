using NewCosmos.Constants;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.ArchiveManagement;
using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.Services.ArchiveManagement;

/// <summary>
/// 目录生成服务
/// </summary>
public class DirectoryGenerator : BaseService, IDirectoryGenerator
{
    protected override string ServiceName => "DirectoryGenerator";

    public DirectoryGenerator(ILoggerService logger) : base(logger) { }

    /// <summary>
    /// 生成目录数据
    /// </summary>
    public DirectoryData GenerateDirectoryData(
        List<TemplateSelectItem> selectedTemplates,
        ApplicationEntity app,
        string classification)
    {
        LogInfo($"生成目录数据: 申请人={app.ApplicantName}, 模板数={selectedTemplates.Count}");

        var items = new List<DirectoryItem>();
        int currentPage = 1;

        // 按 SortOrder 排序模板
        var sortedTemplates = selectedTemplates
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ToList();

        foreach (var template in sortedTemplates)
        {
            var startPage = currentPage;
            var endPage = currentPage + template.BaseCopies - 1;

            items.Add(new DirectoryItem
            {
                Index = items.Count + 1,
                TemplateName = template.Name,
                Copies = template.BaseCopies,
                StartPage = startPage,
                EndPage = endPage
            });

            currentPage = endPage + 1;
        }

        var classificationDisplay = ClassificationConstants.ConvertFromCode(classification);

        var data = new DirectoryData
        {
            Title = "社会救助档案目录",
            Subtitle = $"{app.ApplicantName} - {classificationDisplay}",
            GenerateDate = DateTime.Now,
            TotalItems = items.Count,
            Items = items,
            TotalPages = currentPage - 1,
            VolumeNumber = GenerateVolumeNumber(app)
        };

        LogInfo($"目录数据生成完成: {data.TotalItems} 项, {data.TotalPages} 页");
        return data;
    }

    /// <summary>
    /// 生成档案卷号
    /// </summary>
    private string GenerateVolumeNumber(ApplicationEntity app)
    {
        var year = app.CreatedAt.Year;
        var appNo = app.ApplicationNo ?? "";
        return $"{year}-{appNo}";
    }
}

/// <summary>
/// 目录生成服务接口
/// </summary>
public interface IDirectoryGenerator
{
    /// <summary>
    /// 生成目录数据
    /// </summary>
    DirectoryData GenerateDirectoryData(
        List<TemplateSelectItem> selectedTemplates,
        NewCosmos.Models.Entities.Application app,
        string classification);
}

/// <summary>
/// 目录数据
/// </summary>
public class DirectoryData
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public DateTime GenerateDate { get; set; }
    public int TotalItems { get; set; }
    public List<DirectoryItem> Items { get; set; } = new();
    public int TotalPages { get; set; }
    public string VolumeNumber { get; set; } = string.Empty;
}

/// <summary>
/// 目录项
/// </summary>
public class DirectoryItem
{
    public int Index { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public int Copies { get; set; }
    public int StartPage { get; set; }
    public int EndPage { get; set; }
}

