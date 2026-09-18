using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 大学生档案服务接口（家庭成员→大学生关联）
/// </summary>
public interface ICollegeStudentService
{
    /// <summary>
    /// 获取某低收入申请下的大学生档案列表
    /// </summary>
    Task<Result<List<CollegeStudent>>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 获取全部大学生档案（支持姓名/身份证搜索）
    /// </summary>
    Task<Result<List<CollegeStudent>>> GetAllAsync(string? keyword = null, CancellationToken ct = default);

    /// <summary>
    /// 搜索可关联的18-23周岁家庭成员（未建立大学生档案的成员）
    /// </summary>
    Task<Result<List<FamilyMember>>> SearchEligibleMembersAsync(string? keyword = null, CancellationToken ct = default);

    /// <summary>
    /// 新增大学生档案（自动计算毕业年份）
    /// </summary>
    Task<Result<long>> SaveAsync(CollegeStudent student, CancellationToken ct = default);

    /// <summary>
    /// 更新大学生档案
    /// </summary>
    Task<Result<bool>> UpdateAsync(CollegeStudent student, CancellationToken ct = default);

    /// <summary>
    /// 删除大学生档案（软删除）
    /// </summary>
    Task<Result<bool>> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 获取某年份毕业的在读大学生（用于首页年度提醒）
    /// </summary>
    Task<Result<List<CollegeStudent>>> GetGraduatingStudentsAsync(int year, CancellationToken ct = default);
}
