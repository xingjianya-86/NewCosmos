-- 迁移登记表初始化（幂等，可重复执行）
-- 配套 deploy\check_migrations.ps1（对账 docs/migrations/*.sql 与本表）。
-- 执行：psql -h <host> -U <user> -d <dbname> -f deploy\migrations_registry_init.sql

CREATE TABLE IF NOT EXISTS nc_sys_migrations (
    id serial PRIMARY KEY,
    file_name varchar(200) NOT NULL UNIQUE,
    applied_at timestamptz NOT NULL DEFAULT now(),
    applied_by varchar(100)
);

COMMENT ON TABLE nc_sys_migrations IS '数据库迁移执行登记（docs/migrations/*.sql 对账用；人工执行后经 check_migrations.ps1 -Mark 登记）';
