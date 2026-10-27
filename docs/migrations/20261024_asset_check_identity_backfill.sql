-- 资产核查“有报告未建档”状态回写修复（2026-10-24）
-- 背景：PdfVerificationService.HasLowIncomeIdentityAsync 原先只查 6 张导入台账，
--       不查当前库 nc_biz_applications。仅在当前库建档的人员（如仲立亮 app95，2026-10-08 已批农村低保）
--       被月报核查/PDF 上传判定为“无低收入身份”，状态被回写成 '1'（有报告未建档），
--       工作流 Tab1 一直显示“有报告未建档”。
-- 本脚本：把 head_id_card 已存在 Approved/Completed 在享档案、且分类属于受保护范围
--       （低保含单人/边缘/特困；不含刚性支出、其他困难）的核查行
--       从 '0'/'1' 升级为 '2'（已建档/已纳入），与 UpdateCheckStatusFromUploadAsync 的整户口径一致。
-- 幂等：只动 status IN ('0','1')；重复执行 0 行。

UPDATE nc_biz_asset_checks c
SET status = '2',
    updated_at = NOW()
WHERE c.status IN ('0', '1')
  AND c.deleted_at IS NULL
  AND EXISTS (
      SELECT 1
      FROM nc_biz_applications a
      WHERE a.applicant_id_card = c.head_id_card
        AND a.deleted_at IS NULL
        AND a.status IN ('Approved', 'Completed')
        AND COALESCE(a.classification_result, '') <> ''
        AND a.classification_result IN (
            'RuralSubsistence', 'UrbanSubsistence',
            'RuralLowIncomeSingle', 'UrbanLowIncomeSingle',
            'RuralLowIncome', 'UrbanLowIncome',
            'RuralDestituteScattered', 'UrbanDestituteScattered',
            'RuralDestituteCentralized', 'UrbanDestituteCentralized'
        )
  );
