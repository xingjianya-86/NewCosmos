-- 其他困难人员转出/停止表（2026-10-23）
-- 背景：其他困难人员台账只有导入快照，人员转出（如转低保）无痕迹；本表记录转出事实，
--       原 nc_biz_other_difficulty_families / persons 行一律保留，不删除。
-- 口径：stop_type=停止（整户转出）/ 减员（部分成员转出）；stop_date=转入档案首次审批日。
-- 幂等：可重复执行；唯一键 (person_id_card, stop_date)。

CREATE TABLE IF NOT EXISTS nc_biz_other_difficulty_stops (
    id                      BIGSERIAL PRIMARY KEY,
    family_id               BIGINT,
    person_id               BIGINT,
    person_name             VARCHAR(50)  NOT NULL,
    person_id_card          VARCHAR(18)  NOT NULL,
    head_name               VARCHAR(50),
    head_id_card            VARCHAR(18),
    family_size             INTEGER,
    hukou_type              VARCHAR(20),
    district                VARCHAR(50),
    street                  VARCHAR(50),
    community               VARCHAR(100),
    address                 VARCHAR(500),
    include_month           VARCHAR(10),
    old_category            VARCHAR(50)  DEFAULT '其他困难人员',
    stop_type               VARCHAR(10)  NOT NULL,
    stop_date               DATE         NOT NULL,
    stop_reason             VARCHAR(200),
    target_application_id   BIGINT,
    target_classification   VARCHAR(50),
    source                  VARCHAR(20)  NOT NULL DEFAULT 'approval',
    created_at              TIMESTAMP    DEFAULT CURRENT_TIMESTAMP
);

COMMENT ON TABLE nc_biz_other_difficulty_stops IS '其他困难人员转出/停止记录表（原台账行保留不删）';

-- 与 Schema YAML 的 UNIQUE 约束同名同列：SchemaService 启动时按 pg_constraint 检查，
-- 若这里只建唯一索引（同名）会被误判为"约束不存在"而 ADD CONSTRAINT 撞名，故直接建约束。
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uk_other_difficulty_stop_person_date') THEN
        ALTER TABLE nc_biz_other_difficulty_stops
            ADD CONSTRAINT uk_other_difficulty_stop_person_date UNIQUE (person_id_card, stop_date);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_other_difficulty_stops_person
    ON nc_biz_other_difficulty_stops (person_id_card);
CREATE INDEX IF NOT EXISTS idx_other_difficulty_stops_stop_date
    ON nc_biz_other_difficulty_stops (stop_date);
CREATE INDEX IF NOT EXISTS idx_other_difficulty_stops_target_app
    ON nc_biz_other_difficulty_stops (target_application_id);

-- 历史个案补录：仲立亮（档案95，2026-10-08 审批通过农村低保，由其他困难人员转出）。
-- 当前其他困难人员台账无此人，故按档案95 快照手工补一条（source='manual'）。
INSERT INTO nc_biz_other_difficulty_stops
    (person_name, person_id_card, head_name, head_id_card, family_size, hukou_type,
     district, street, community, address, include_month, old_category,
     stop_type, stop_date, stop_reason, target_application_id, target_classification,
     source, created_at)
SELECT a.applicant_name,
       a.applicant_id_card,
       a.applicant_name,
       a.applicant_id_card,
       a.family_size,
       a.hukou_type,
       a.district,
       a.town,
       a.community,
       a.address,
       TO_CHAR(COALESCE(a.first_approved_at, a.created_at), 'YYYY-MM'),
       '其他困难人员',
       '停止',
       COALESCE(a.first_approved_at, a.created_at)::date,
       '转出至' || COALESCE(
           (SELECT CASE a.classification_result
                       WHEN 'RuralSubsistence' THEN '最低生活保障对象'
                       WHEN 'UrbanSubsistence' THEN '最低生活保障对象'
                       WHEN 'RuralLowIncomeSingle' THEN '最低生活保障对象（单人）'
                       WHEN 'UrbanLowIncomeSingle' THEN '最低生活保障对象（单人）'
                       WHEN 'RuralLowIncome' THEN '最低生活保障边缘家庭'
                       WHEN 'UrbanLowIncome' THEN '最低生活保障边缘家庭'
                       WHEN 'RuralDestituteScattered' THEN '特困人员（分散供养）'
                       WHEN 'UrbanDestituteScattered' THEN '特困人员（分散供养）'
                       WHEN 'RuralDestituteCentralized' THEN '特困人员（集中供养）'
                       WHEN 'UrbanDestituteCentralized' THEN '特困人员（集中供养）'
                       WHEN 'RuralRigidExpenditure' THEN '刚性支出困难家庭'
                       WHEN 'UrbanRigidExpenditure' THEN '刚性支出困难家庭'
                       ELSE a.classification_result
                   END),
           '其他类别'),
       a.id,
       a.classification_result,
       'manual',
       CURRENT_TIMESTAMP
FROM nc_biz_applications a
WHERE a.id = 95
  AND a.applicant_id_card = '231025196809284916'
  AND a.deleted_at IS NULL
ON CONFLICT (person_id_card, stop_date) DO NOTHING;
