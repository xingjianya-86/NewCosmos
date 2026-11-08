-- 家庭经济状况核对家庭住址统一（2026-11-05）
-- 背景：nc_biz_asset_checks.family_address 是月报/档案/模板/补打等 5 个消费方
--       （MonthlyReportViewModel、ArchiveProductionViewModel、TemplateFieldBuilder、
--         AssetVerificationReprintProvider 等，统一走 AddressResolver.BuildAddress(community, family_address)）
--       的地址数据源；写入口 QuickAssetVerificationViewModel 只存「详细地址」框原文：
--       档案预填的全地址与裸门牌（161号 / 七星屯66号）混存；
--       community 还因「查询历史」预填回环大面积错挂柳树村（174/292 行同码，140 行与文本村名矛盾）。
-- 口径：统一为「县+镇+村+门牌」，与 Helpers\AddressResolver.BuildAddressFromText 完全一致
--       （文本级地址拼装唯一入口；已含级不重复、村民委员会/村委会→XX村、占位横杠清空）。
-- 规则（2026-11-05 用户确认「文本/编码分层」+「错字全部规正为区划标准名」）：
--   ① 文本含村名的行以**文本**为准：
--      错字/别名规正区划标准名（嘎库村→戛库村、土甸村→土甸子村、双宝村→宝山村），
--      补「林口县」前缀，回写 community = 文本所指村编码（打断查询历史的错误继承回环）；
--   ② 裸门牌/屯的行以 **community 编码**为准（新流程用户手选，编码多样可信）：
--      按区划补「林口县+镇+村(code)+门牌」；
--   ③ 镇直 6 行：community 231025105208（柳树村，错）→ 231025105002（柳树镇镇直第一居民委员会），
--      文本规正为「林口县柳树镇镇直第一社区<门牌>」；
--   ④ 不详 5 行、外省 1 行（山东省青岛市…）、community 为空的行不动。
-- 代码侧同步（新记录不再产生不一致）：
--   - QuickAssetVerificationViewModel.FamilyAddress / BuildSubmitRequest 改走
--     AddressResolver.BuildAddressFromText（县+镇+村+门牌，已含级去重幂等）；
--   - AddressResolver.BuildAddressFromText 增补「村级名自带镇前缀」剥离
--     （区划「柳树镇镇直第一居民委员会」不剥会每次重存再插一个「柳树镇」）。
-- 幂等：每条带守卫，重复执行全部 UPDATE 0（首执行后已复测通过）。
-- 执行：psql -w -v ON_ERROR_STOP=1 -f docs/migrations/20261105_asset_verification_family_address_unify.sql
-- 回滚原值：sql\asset_checks_address_before.csv
--       （C:\NewCosmosCode\backup\20261105_165603_家庭经济状况核对地址统一，298 行 id+community+family_address）
-- 已知异形（均已在本迁移内规正）：
--   - id 248「荣华村57号」：「荣华村」为「荣华屯」误写（2026-11-05 用户确认实为柞木村荣华屯），
--     规正为「林口县柳树镇柞木村荣华屯57号」，community 231025105201（柞木村）本已正确；
--   - id 82「柳树村双河村北海屯」：首词为镇名误写，规正为「林口县柳树镇双河村北海屯」；
--   - id 92/163「柳树镇柳树镇」（旧拼接 bug 历史产物）：文本无村，按 community（柳树村）取村。

BEGIN;

-- ①a 错字/别名规正为区划标准名（nc_regions_villages：戛库村委会 / 土甸子村委会 / 宝山村下辖双宝屯）
UPDATE nc_biz_asset_checks
SET family_address = replace(replace(replace(family_address, '嘎库村', '戛库村'), '土甸村', '土甸子村'), '双宝村', '宝山村'),
    updated_at = NOW()
WHERE deleted_at IS NULL
  AND (family_address LIKE '%嘎库村%' OR family_address LIKE '%土甸村%' OR family_address LIKE '%双宝村%');

-- ①b 镇名误写（id 82）：「柳树村双河村北海屯」→「柳树镇双河村北海屯」
UPDATE nc_biz_asset_checks
SET family_address = replace(family_address, '柳树村双河村', '柳树镇双河村'),
    updated_at = NOW()
WHERE deleted_at IS NULL
  AND family_address LIKE '柳树村双河村%';

-- ①c 尾部占位横杠（实测 id 190「…戛库村-」）
UPDATE nc_biz_asset_checks
SET family_address = regexp_replace(family_address, '-$', ''),
    updated_at = NOW()
WHERE deleted_at IS NULL
  AND family_address ~ '-$'
  AND length(family_address) > 1;

-- ①d 双重镇前缀（id 92/163「柳树镇柳树镇」，旧「镇+村+detail」拼接 bug 历史产物）：
--    文本无村 → 按 community（均为柳树村 231025105208）取村，与写入口重拼结果一致
UPDATE nc_biz_asset_checks
SET family_address = '林口县柳树镇柳树村',
    updated_at = NOW()
WHERE deleted_at IS NULL
  AND family_address = '柳树镇柳树镇'
  AND community = '231025105208';

-- ② 镇直行文本规正：「柳树镇镇直[社区]<门牌>」→「林口县柳树镇镇直第一社区<门牌>」
--    （村级区划名 柳树镇镇直第一居民委员会 经 NormalizeVillageForAddress = 镇直第一社区，
--      文本带上该名，写入口重拼时村级名可命中、不重复插入）
UPDATE nc_biz_asset_checks
SET family_address = '林口县柳树镇镇直第一社区' || regexp_replace(family_address, '^柳树镇镇直(社区)?', ''),
    updated_at = NOW()
WHERE deleted_at IS NULL
  AND family_address LIKE '柳树镇镇直%'
  AND family_address NOT LIKE '林口县%';

-- ③ 文本所指村 → 回写 community（以文本为准）。
--    HAVING count(*) = 1：文本只命中一个区划名才回写；命中 0（裸门牌/不详/外省/荣华村57号）或
--    多个（防误配，①b 已先行消掉 id 82 的双命中）均跳过。min(code) 在唯一命中下即该编码。
UPDATE nc_biz_asset_checks a
SET community = s.code,
    updated_at = NOW()
FROM (
    SELECT a2.id,
           min(m.code)::text AS code,
           count(*)          AS hits
    FROM nc_biz_asset_checks a2
    CROSS JOIN (VALUES
        ('柞木村',       '231025105201'),
        ('双河村',       '231025105202'),
        ('戛库村',       '231025105203'),
        ('柳西村',       '231025105204'),
        ('柳新村',       '231025105205'),
        ('万寿村',       '231025105206'),
        ('复兴村',       '231025105207'),
        ('柳树村',       '231025105208'),
        ('榆树村',       '231025105209'),
        ('土甸子村',     '231025105210'),
        ('柳毛村',       '231025105211'),
        ('柳宝村',       '231025105212'),
        ('宝山村',       '231025105213'),
        ('三道村',       '231025105214'),
        ('镇直第一社区', '231025105002')
    ) AS m(name, code)
    WHERE a2.deleted_at IS NULL
      AND a2.family_address LIKE '%' || m.name || '%'
    GROUP BY a2.id
    HAVING count(*) = 1
) s
WHERE a.id = s.id
  AND a.community IS DISTINCT FROM s.code;

-- ④ 裸门牌/屯（无镇无村字样）：以 community 编码为准，按区划补「林口县+镇+村+门牌」。
--    仅编码可解析且非占位/外省的行参与；村名后缀按地址口径规整（委员会→社区、村委会→村）。
UPDATE nc_biz_asset_checks a
SET family_address = '林口县' || t.town_name ||
        CASE
            WHEN v.village_name LIKE '%居民委员会' THEN replace(v.village_name, '居民委员会', '社区')
            WHEN v.village_name LIKE '%村民委员会' THEN replace(v.village_name, '村民委员会', '村')
            WHEN v.village_name LIKE '%村委会'     THEN replace(v.village_name, '村委会', '村')
            ELSE v.village_name
        END ||
        a.family_address,
    updated_at = NOW()
FROM nc_regions_villages v
JOIN nc_regions_towns t ON t.id = v.town_id
WHERE a.deleted_at IS NULL
  AND v.village_code = a.community
  AND a.family_address NOT LIKE '%镇%'
  AND a.family_address NOT LIKE '%村%'
  AND a.family_address NOT LIKE '%街道%'
  AND a.family_address NOT LIKE '林口县%'
  AND a.family_address NOT LIKE '山东省%'
  AND a.family_address IS NOT NULL
  AND a.family_address NOT IN ('不详', '-', '—', '');

-- ⑤a 已以镇开头的文本只补「林口县」（数据实测全部属林口县柳树镇，区划回查已核）
UPDATE nc_biz_asset_checks
SET family_address = '林口县' || family_address,
    updated_at = NOW()
WHERE deleted_at IS NULL
  AND family_address LIKE '柳树镇%'
  AND family_address NOT LIKE '林口县%';

-- ⑤b 含级字样但不以县/镇开头（id 240「柳树村25号」、id 248「荣华村57号」）：
--    镇名按 community 经区划回查取得（两行均为柳树镇）
UPDATE nc_biz_asset_checks a
SET family_address = '林口县' || t.town_name || a.family_address,
    updated_at = NOW()
FROM nc_regions_villages v
JOIN nc_regions_towns t ON t.id = v.town_id
WHERE a.deleted_at IS NULL
  AND v.village_code = a.community
  AND a.family_address NOT LIKE '林口县%'
  AND a.family_address NOT LIKE '柳树镇%'
  AND (a.family_address LIKE '%镇%' OR a.family_address LIKE '%村%' OR a.family_address LIKE '%街道%')
  AND a.family_address NOT LIKE '山东省%';

-- ⑥ 「荣华村」→「荣华屯」（id 248；荣华屯属柞木村，非区划村名，2026-11-05 用户确认实为柞木村荣华屯）：
--    置于 ⑤b 之后（依赖其补出的「林口县柳树镇」前缀），按写入口 BuildAddressFromText 口径落值
UPDATE nc_biz_asset_checks
SET family_address = '林口县柳树镇柞木村荣华屯57号',
    updated_at = NOW()
WHERE deleted_at IS NULL
  AND family_address = '林口县柳树镇荣华村57号'
  AND community = '231025105201';

COMMIT;
