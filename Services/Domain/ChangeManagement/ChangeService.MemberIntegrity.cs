using NewCosmos.Constants;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ChangeManagement;

/// <summary>
/// 停旧建新 / 换户主流程的成员与户主人身数据一致性收口（2026-11-19 张桂秋 app115 实测错例：
/// 户主死亡 56→115 后，新档主表带死者病情、张桂秋"免于劳动力判定"因照顾目标（重病户主）被剔除而悬空，
/// 表单保存被联动校验拦截，业务卡死）。
/// 两处收口均幂等，调用失败由业务流程回滚（§7 显式失败）。
/// </summary>
public partial class ChangeService
{
    /// <summary>
    /// 新户主人身字段同步：主表"户主本人"字段从**新户主的成员行**镜像（健康/疾病/残疾/就业/收入来源等），
    /// 并清空无成员来源的 physical_condition / disability_card_no（旧户主的身体状况与残疾证号不得沿用）。
    /// 背景：停旧建新主表 INSERT 从旧档案整行复制，只替换姓名/证件/性别等少数列 → 换户主后死者病情、
    /// 残疾、就业信息整体串到新户主名下（`is_severe_disease` 又不在复制列清单落默认 false，形成
    /// "健康=重病、重病勾选=否"的自相矛盾）。须在成员复制完成、新户主成员行可定位后调用（事务内）。
    /// </summary>
    private async Task<Result> SyncHeadPersonFieldsAsync(long applicationId, long headMemberId, CancellationToken ct)
    {
        const string sql = @"UPDATE nc_biz_applications a
            SET applicant_phone = COALESCE(m.phone, ''),
                gender = COALESCE(m.gender, ''),
                ethnicity = COALESCE(m.ethnicity, ''),
                marital_status = COALESCE(m.marital_status, ''),
                hukou_type = COALESCE(m.hukou_type, ''),
                education_level = COALESCE(m.education_level, ''),
                political_status = COALESCE(m.political_status, ''),
                physical_condition = '',
                health_status = COALESCE(m.health_status, ''),
                disease_code = COALESCE(m.disease_code, ''),
                disease_name = COALESCE(m.disease_name, ''),
                secondary_disease_name = COALESCE(m.secondary_disease, ''),
                disability_type = COALESCE(m.disability_type, ''),
                disability_level = COALESCE(m.disability_level, ''),
                disability_card_no = '',
                is_severe_disease = COALESCE(m.is_severe_disease, FALSE),
                employment_status = COALESCE(m.employment_status, ''),
                work_unit = COALESCE(m.work_unit, ''),
                income_source = COALESCE(m.main_income_source, ''),
                updated_at = NOW()
            FROM nc_biz_family_members m
            WHERE a.id = $1 AND m.id = $2 AND m.application_id = a.id AND m.deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, applicationId, headMemberId);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "新户主人身字段同步失败");
        return Result.Success();
    }

    /// <summary>
    /// 清理"无照顾目标"的免于劳动力判定残留（与表单保存联动校验同口径）：
    /// 照顾目标 = 本户**非豁免**成员重病/重残，或户主本人主表重病/一、二级残疾；
    /// 目标不存在时清除 `is_labor_exempt`（否则表单保存恒被拦，业务卡死）。
    /// 幂等：无残留时 0 行更新；清理条数记 Info 便于追溯。
    /// </summary>
    private async Task<Result> ClearInvalidLaborExemptAsync(long applicationId, CancellationToken ct)
    {
        const string sql = @"UPDATE nc_biz_family_members m
            SET is_labor_exempt = FALSE, updated_at = NOW()
            WHERE m.application_id = $1
              AND m.deleted_at IS NULL
              AND m.is_labor_exempt = TRUE
              AND NOT EXISTS (
                  SELECT 1 FROM nc_biz_family_members t
                  WHERE t.application_id = $1 AND t.deleted_at IS NULL
                    AND t.is_labor_exempt = FALSE
                    AND (t.is_severe_disease = TRUE OR t.is_severe_disability = TRUE))
              AND NOT EXISTS (
                  SELECT 1 FROM nc_biz_applications a
                  WHERE a.id = $1
                    AND (a.is_severe_disease = TRUE OR a.disability_level = ANY($2::text[])))";
        var severeLevels = new[] { DisabilityConstants.LEVEL1, DisabilityConstants.LEVEL2 };
        var result = await _db.ExecuteNonQueryAsync(sql, ct, applicationId, severeLevels);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "豁免残留清理失败");
        if (result.Value > 0)
            LogInfo($"清理无照顾目标的免于劳动力判定残留: ApplicationId={applicationId}, 清除{result.Value}条");
        return Result.Success();
    }
}
