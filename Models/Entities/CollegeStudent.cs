using CommunityToolkit.Mvvm.ComponentModel;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 大学生档案实体（家庭成员→大学生关联）
/// 对应表 nc_biz_college_students
/// </summary>
public partial class CollegeStudent : ObservableObject
{
    public long Id { get; set; }

    /// <summary>关联低收入申请ID</summary>
    public long ApplicationId { get; set; }

    /// <summary>关联家庭成员ID</summary>
    public long FamilyMemberId { get; set; }

    /// <summary>大学生姓名</summary>
    [ObservableProperty]
    private string _studentName = string.Empty;

    /// <summary>身份证号</summary>
    [ObservableProperty]
    private string? _idCard;

    /// <summary>学历层次（本科/专科）</summary>
    [ObservableProperty]
    private string? _educationLevel;

    /// <summary>学校名称</summary>
    [ObservableProperty]
    private string? _schoolName;

    /// <summary>学制：3=三年制/4=四年制/5=五年制</summary>
    [ObservableProperty]
    private int? _schoolDuration;

    /// <summary>入学年份</summary>
    [ObservableProperty]
    private int? _enrollmentYear;

    /// <summary>毕业年份（入学年份+学制）</summary>
    [ObservableProperty]
    private int? _graduationYear;

    /// <summary>状态（Studying/Graduated/NotEligible/NoHigherEducation）</summary>
    [ObservableProperty]
    private string _status = Constants.CollegeStudentConstants.StatusStudying;

    /// <summary>备注</summary>
    [ObservableProperty]
    private string? _remark;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>学制显示文本</summary>
    public string SchoolDurationDisplay => Constants.CollegeStudentConstants.GetSchoolDurationDisplay(SchoolDuration);

    /// <summary>是否今年毕业（在读或已毕业且毕业年份=今年）</summary>
    public bool IsGraduatingThisYear =>
        (Status == Constants.CollegeStudentConstants.StatusStudying
         || Status == Constants.CollegeStudentConstants.StatusGraduated)
        && GraduationYear == DateTime.Today.Year;

    /// <summary>是否已超择业期（毕业年份早于今年，或今年已过12月）</summary>
    public bool IsBeyondJobSeekingPeriod
    {
        get
        {
            if (Status == Constants.CollegeStudentConstants.StatusNotEligible)
                return true;
            if (GraduationYear == null)
                return false;
            // 毕业年份早于今年：已超出（6月毕业+半年择业期=当年12月）
            if (GraduationYear < DateTime.Today.Year)
                return true;
            // 今年毕业：当年12月前为择业期，12月后即超出
            return GraduationYear == DateTime.Today.Year && DateTime.Today.Month >= 12;
        }
    }

    /// <summary>状态显示文本</summary>
    public string StatusDisplay => Constants.CollegeStudentConstants.GetStatusDisplay(Status);
}
