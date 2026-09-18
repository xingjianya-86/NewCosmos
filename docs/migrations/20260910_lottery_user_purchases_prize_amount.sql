-- 彩票模块迁移：购彩记录增加中奖金额列
-- 背景：中奖结果页需要展示解析自开奖 prize_grades_json 的单注奖金
-- 执行：psql -h <host> -p 5432 -U new_cosmos -d new_cosmos -f 20260910_lottery_user_purchases_prize_amount.sql

ALTER TABLE nc_lottery_user_purchases
    ADD COLUMN IF NOT EXISTS prize_amount DECIMAL(12,2) DEFAULT 0;

COMMENT ON COLUMN nc_lottery_user_purchases.prize_amount IS '中奖金额（元，解析自开奖奖级明细）';
