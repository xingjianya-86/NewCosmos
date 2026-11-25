-- 两项政策数据契约对齐 + 临时救助「两大类 × 三小类」改造（C# NewCosmos 权威版）
-- 依据：
--   * 《黑龙江省临时救助工作实施办法》
--   * 黑民规〔2026〕10 号「一事一议」
-- 说明：
--   1. 仅新增可空列 / 带默认值列，向后兼容；旧版 C# 客户端不读新列也不会崩。
--   2. 全部 IF NOT EXISTS，可重复执行。
--   3. Rust 项目已废弃；时间列统一用 C# 惯例 timestamp without time zone。
--      若测试库此前按 Rust 建成了 timestamptz，本脚本会就地改成 timestamp。
--   4. 县级不使用本系统：county_confirmed_* / filing_status 由乡镇登记县确认件/备案表。

-- ── 临时救助：政策字段 + 分类维度 ──
ALTER TABLE nc_biz_temp_relief_applications
    ADD COLUMN IF NOT EXISTS relief_person_count  integer NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS standard_tier        varchar(20),
    ADD COLUMN IF NOT EXISTS standard_multiplier  numeric(6,2),
    ADD COLUMN IF NOT EXISTS accepted_at          timestamp,
    ADD COLUMN IF NOT EXISTS investigation_done_at timestamp,
    ADD COLUMN IF NOT EXISTS county_confirmed_at  timestamp,
    ADD COLUMN IF NOT EXISTS county_confirmed_by  varchar(100),
    ADD COLUMN IF NOT EXISTS filing_status        varchar(20),
    ADD COLUMN IF NOT EXISTS reject_reason        text,
    ADD COLUMN IF NOT EXISTS reject_notified_at   timestamp,
    ADD COLUMN IF NOT EXISTS reject_notice_method varchar(30),
    ADD COLUMN IF NOT EXISTS population_status    varchar(20) NOT NULL DEFAULT 'Insured',
    ADD COLUMN IF NOT EXISTS payment_channel      varchar(20);

-- 测试库若曾按 Rust 建为 timestamptz，就地归一为 timestamp（C# Npgsql 口径）
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
         WHERE table_name = 'nc_biz_temp_relief_applications'
           AND column_name = 'accepted_at'
           AND data_type = 'timestamp with time zone'
    ) THEN
        ALTER TABLE nc_biz_temp_relief_applications
            ALTER COLUMN accepted_at TYPE timestamp,
            ALTER COLUMN investigation_done_at TYPE timestamp,
            ALTER COLUMN county_confirmed_at TYPE timestamp,
            ALTER COLUMN reject_notified_at TYPE timestamp;
    END IF;
END $$;

COMMENT ON COLUMN nc_biz_temp_relief_applications.relief_person_count IS '家庭遇困人数（≠ family_size 家庭人口；政策公式乘数，含户主）';
COMMENT ON COLUMN nc_biz_temp_relief_applications.standard_tier       IS '救助档位码：小额 S50/S75/S100；常规救助不落档';
COMMENT ON COLUMN nc_biz_temp_relief_applications.standard_multiplier IS '档位系数：0.5/0.75/1（小额）；常规为空';
COMMENT ON COLUMN nc_biz_temp_relief_applications.accepted_at         IS '乡镇受理时间（时限起算）';
COMMENT ON COLUMN nc_biz_temp_relief_applications.county_confirmed_at IS '县确认日期（乡镇登记县确认件）';
COMMENT ON COLUMN nc_biz_temp_relief_applications.filing_status       IS '备案状态：Pending/Filed';
COMMENT ON COLUMN nc_biz_temp_relief_applications.reject_reason       IS '不予救助/退回补正原因';
COMMENT ON COLUMN nc_biz_temp_relief_applications.population_status   IS '人群状态：Insured 在保障人群内（四类在册）/ Uninsured 不在保障人群内';
COMMENT ON COLUMN nc_biz_temp_relief_applications.payment_channel     IS '发放渠道：County 上级部门发放 / Reserve 备用金发放（小额固定 Reserve）';

COMMENT ON COLUMN nc_biz_temp_relief_applications.status IS '状态：Draft草稿/Submitted已提交/Confirmed已确认/Stopped已终止/Rejected不予救助/Returned退回补正';

-- 存量回填（幂等）：
--   ① 遇困人数默认按家庭人口；
--   ② 发放渠道：小额→备用金，常规→上级部门（历史“大额报县局审批”口径）；
--   ③ 人群状态：ADD COLUMN 的 DEFAULT 已回填 Insured，无需逐行 UPDATE。
UPDATE nc_biz_temp_relief_applications
   SET relief_person_count = GREATEST(COALESCE(family_size, 1), 1)
 WHERE relief_person_count = 1
   AND COALESCE(family_size, 1) > 1;

UPDATE nc_biz_temp_relief_applications
   SET payment_channel = CASE WHEN relief_type = 'Small' THEN 'Reserve' ELSE 'County' END
 WHERE payment_channel IS NULL;

-- 分类组合查询索引（列表/月报/统计按大类、类型、渠道、状态切片）
CREATE INDEX IF NOT EXISTS idx_temp_relief_category
    ON nc_biz_temp_relief_applications (population_status, relief_type, payment_channel, status);

-- ── 一事一议 ──
ALTER TABLE nc_biz_special_approval_forms
    ADD COLUMN IF NOT EXISTS case_type    varchar(40),
    ADD COLUMN IF NOT EXISTS submitted_at date,
    ADD COLUMN IF NOT EXISTS filed_at     date;

COMMENT ON COLUMN nc_biz_special_approval_forms.case_type    IS '适用情形代码（新规 8 类，字典 SpecialApprovalCaseTypes）';
COMMENT ON COLUMN nc_biz_special_approval_forms.submitted_at IS '报县日期（10 个工作日评议计时起点）';
COMMENT ON COLUMN nc_biz_special_approval_forms.filed_at     IS '备案日期（5 个工作日备案终点）';

COMMENT ON COLUMN nc_biz_special_approval_forms.status IS '状态：Draft草稿/Submitted已上报/Returned已返回结论（待备案）/NotPassed不通过/Filed已备案/ReturnedForCorrection退回补正（兼容历史 Approved/Rejected/NotApplicable）';

-- 同一申请只允许一份有效申报表。
-- 先检查重复；存在一申请多表时跳过建索引并提示人工合并，避免迁移中断。
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
          FROM nc_biz_special_approval_forms
         WHERE deleted_at IS NULL
         GROUP BY application_id
        HAVING COUNT(*) > 1
    ) THEN
        RAISE NOTICE '存在一申请多份申报表，跳过唯一索引 ux_special_approval_forms_application，请先人工合并';
    ELSE
        CREATE UNIQUE INDEX IF NOT EXISTS ux_special_approval_forms_application
            ON nc_biz_special_approval_forms (application_id)
            WHERE deleted_at IS NULL;
    END IF;
END $$;
