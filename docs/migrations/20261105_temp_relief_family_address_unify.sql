-- 临时救助家庭住址统一（2026-11-05）
-- 背景：family_address 是月报/打印「家庭住址」列的直接数据源
--       （MonthlyReportService.GetTempReliefSummaryRowsAsync 取 t.family_address），
--       此前照抄来源台账原文，格式五花八门：省全量 / 只到村 / 「林口县柳树镇镇直」 / 只有门牌。
-- 口径：统一为「县+镇+村+门牌」，与 Helpers\AddressResolver.BuildAddressFromText 完全一致
--       （文本级地址拼装唯一入口；'-' 占位清空、村民委员会/村委会→XX村、XX镇本级→无村、已含级不重复）。
--       村取表单只读分列 village，详细地址取 family_detail。
-- 代码侧同步：TempReliefFormViewModel.BuildFamilyAddress 保存时按分列重拼，新记录不再产生不一致；
--       本迁移只修存量（执行时 29 行）。
-- 幂等：每条带 AND family_address IS DISTINCT FROM '<新值>'，重复执行为 no-op。
-- 执行：psql -w -v ON_ERROR_STOP=1 -f docs/migrations/20261105_temp_relief_family_address_unify.sql
-- 回滚原值：temp_relief_address_before.csv（C:\NewCosmosCode\backup\20261105_161100_临时救助家庭住址统一）
-- 注意：id 9/11/24/28 来源自相矛盾（结构化村列 与 来源地址原文所指的村 不一致），
--       本迁移以表单分列（结构化列）为准；如与实际居住地不符，请修正来源数据后重新保存该记录。

BEGIN;

UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柳毛村131号' WHERE id = 5 AND family_address IS DISTINCT FROM '林口县柳树镇柳毛村131号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇三道村丰谷屯84号' WHERE id = 6 AND family_address IS DISTINCT FROM '林口县柳树镇三道村丰谷屯84号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇土甸子村弄一63号' WHERE id = 7 AND family_address IS DISTINCT FROM '林口县柳树镇土甸子村弄一63号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇土甸子村168号' WHERE id = 8 AND family_address IS DISTINCT FROM '林口县柳树镇土甸子村168号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇榆树村晨光屯69号' WHERE id = 9 AND family_address IS DISTINCT FROM '林口县柳树镇榆树村晨光屯69号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇土甸子村石青屯' WHERE id = 10 AND family_address IS DISTINCT FROM '林口县柳树镇土甸子村石青屯';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇三道村' WHERE id = 11 AND family_address IS DISTINCT FROM '林口县柳树镇三道村';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柳毛村七星屯' WHERE id = 12 AND family_address IS DISTINCT FROM '林口县柳树镇柳毛村七星屯';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柞木村133号' WHERE id = 13 AND family_address IS DISTINCT FROM '林口县柳树镇柞木村133号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇双河村25号' WHERE id = 14 AND family_address IS DISTINCT FROM '林口县柳树镇双河村25号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇三道村' WHERE id = 15 AND family_address IS DISTINCT FROM '林口县柳树镇三道村';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柳树村' WHERE id = 16 AND family_address IS DISTINCT FROM '林口县柳树镇柳树村';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇土甸子村石青屯117号' WHERE id = 17 AND family_address IS DISTINCT FROM '林口县柳树镇土甸子村石青屯117号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柳宝村光明屯118号' WHERE id = 18 AND family_address IS DISTINCT FROM '林口县柳树镇柳宝村光明屯118号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柳树村' WHERE id = 19 AND family_address IS DISTINCT FROM '林口县柳树镇柳树村';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇复兴村51号' WHERE id = 20 AND family_address IS DISTINCT FROM '林口县柳树镇复兴村51号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇三道村丰谷屯61号' WHERE id = 21 AND family_address IS DISTINCT FROM '林口县柳树镇三道村丰谷屯61号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柳宝村22号' WHERE id = 22 AND family_address IS DISTINCT FROM '林口县柳树镇柳宝村22号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柳西村' WHERE id = 23 AND family_address IS DISTINCT FROM '林口县柳树镇柳西村';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇土甸子村257号' WHERE id = 24 AND family_address IS DISTINCT FROM '林口县柳树镇土甸子村257号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柞木村荣华屯81号' WHERE id = 25 AND family_address IS DISTINCT FROM '林口县柳树镇柞木村荣华屯81号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柞木村荣华屯81号' WHERE id = 26 AND family_address IS DISTINCT FROM '林口县柳树镇柞木村荣华屯81号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柳树村330号' WHERE id = 27 AND family_address IS DISTINCT FROM '林口县柳树镇柳树村330号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇宝山村70号' WHERE id = 28 AND family_address IS DISTINCT FROM '林口县柳树镇宝山村70号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇290号' WHERE id = 29 AND family_address IS DISTINCT FROM '林口县柳树镇290号';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柳西村' WHERE id = 30 AND family_address IS DISTINCT FROM '林口县柳树镇柳西村';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇' WHERE id = 31 AND family_address IS DISTINCT FROM '林口县柳树镇';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇双河村' WHERE id = 32 AND family_address IS DISTINCT FROM '林口县柳树镇双河村';
UPDATE nc_biz_temp_relief_applications SET family_address = '林口县柳树镇柳毛村121号' WHERE id = 33 AND family_address IS DISTINCT FROM '林口县柳树镇柳毛村121号';

COMMIT;
