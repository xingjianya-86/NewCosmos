using NewCosmos.Models.Session;

namespace NewCosmos.Services.Core;

/// <summary>登录会话持久化（SecureStorage，不含密码；默认有效期 7 天）。</summary>
public interface ISessionStore
{
    Task SaveAsync(UserSession session);
    Task<UserSession?> LoadAsync();
    Task ClearAsync();
}
