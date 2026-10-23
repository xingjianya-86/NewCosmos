-- 20261023 登录会话表（多端重复登录治理）
-- 幂等迁移：已部署库可重复执行；SchemaService 也会依据
-- Resources\Schema/system_sessions/database.yaml 幂等建表/补索引。
--
-- 会话策略配置（可选，写 nc_config_system_standards，category='session'）：
--   session.policy               1=按端配额（默认） 2=严格单会话 3=多端并存
--   session.max_desktop          桌面端配额，默认 1
--   session.max_mobile           移动端配额，默认 1
--   session.heartbeat_desktop    桌面心跳秒数，默认 45
--   session.heartbeat_mobile     移动心跳秒数，默认 90
--   session.online_window        在线判定窗口秒数，默认 180
--   session.absolute_days        会话绝对有效期（天），默认 7
--   session.grace_seconds        被顶号后的宽限退出秒数，默认 30
-- 未配置时客户端使用 Constants/SessionConstants.cs 的默认值，无需先插数据。
-- 注意 nc_config_system_standards.config_value 为 NUMERIC(18,2)，故策略用数值编码。

BEGIN;

CREATE TABLE IF NOT EXISTS nc_sys_user_sessions (
    id              BIGSERIAL PRIMARY KEY,
    session_id      uuid            NOT NULL,
    user_id         integer         NOT NULL,
    device_id       character varying(64)  NOT NULL DEFAULT '',
    device_kind     character varying(16)  NOT NULL DEFAULT 'desktop',
    device_name     character varying(100) NOT NULL DEFAULT '',
    app_version     character varying(32)  NOT NULL DEFAULT '',
    login_at        timestamp with time zone NOT NULL DEFAULT NOW(),
    last_seen_at    timestamp with time zone NOT NULL DEFAULT NOW(),
    expires_at      timestamp with time zone NOT NULL,
    status          character varying(16)  NOT NULL DEFAULT 'active',
    end_reason      character varying(64),
    ended_by        integer,
    ended_at        timestamp with time zone,
    client_ip       character varying(64),
    created_at      timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_sys_user_sessions_session_id
    ON nc_sys_user_sessions (session_id);

CREATE INDEX IF NOT EXISTS idx_sys_user_sessions_user_status
    ON nc_sys_user_sessions (user_id, status);

CREATE INDEX IF NOT EXISTS idx_sys_user_sessions_active
    ON nc_sys_user_sessions (status, last_seen_at);

CREATE INDEX IF NOT EXISTS idx_sys_user_sessions_ended_at
    ON nc_sys_user_sessions (ended_at);

COMMIT;
