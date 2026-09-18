namespace NewCosmos.Constants;

/// <summary>
/// 在线更新签名验签常量。
/// 公钥编译进 App；私钥仅发布机持有（%USERPROFILE%\.newcosmos\update_signing_key.pem，
/// 由 Scripts/UpdateSigningTool keygen 生成），绝不入仓库/部署机。
/// </summary>
public static class UpdateSignatureConstants
{
    /// <summary>更新渠道（清单 channel 与此不一致时拒绝）</summary>
    public const string Channel = "stable";

    /// <summary>清单签名公钥（RSA-2048 SPKI PEM）</summary>
    public const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA4BQdRgxMGjUrazgXWe9M
m7k5efi6My6euKU/mxTFMFPJoYg/o2/nwVEnyEC+ftcPywT7n+u1g3WWxcgO+92v
oXqF23tfyIiTmjiMgGHuf5meAZh8kYGB8TSruTSUj6Zw3qZqXt5MpIzem4WcAeRA
5H7aKSFzg8BOtoVZ1LutlG97+Wcr+V66898i8rZ6WKvFEHWEK9kPJxKdMxpcvO48
SZhdo7f/MKn1ngJ/jvX8Y7Evwod7NJaDhmgskHCndS+ZOpbszd3K2dFhszjh5cmm
nYnxuMzzYQx5YGSyuTfI/v/pjqkkSZw3i0A4UPPf52vj11D/chj4bHN+Utl6iD0y
eQIDAQAB
-----END PUBLIC KEY-----
""";
}
