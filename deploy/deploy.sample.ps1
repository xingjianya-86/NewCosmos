# ============================================================================
# deploy.local.ps1 样例（可入库）
# 复制本文件为 deploy\deploy.local.ps1 并填入真实值；deploy.local.ps1 已被 .gitignore 忽略。
# 发布/导入脚本会按需 dot-source 该文件；也可改用命令行参数覆盖。
# ============================================================================

# 更新服务器（SSH 部署）
$LocalServerHost = 'your-update-server'                 # 例：内网 IP 或域名
$LocalServerUser = 'root'
$LocalServerRoot = '/path/to/update/site/newcosmos'      # 服务器上更新站点根目录

# 发布历史入库所用数据库
$LocalDbHost     = 'your-db-host'
$LocalDbName     = 'new_cosmos'
$LocalDbUser     = 'new_cosmos'

# 线上清单校验地址
$LocalVerifyUrl  = 'https://your-update-domain/stable/update.json'

# 本机 psql 路径
$LocalPsqlPath   = 'C:\pgsql\bin\psql.exe'
