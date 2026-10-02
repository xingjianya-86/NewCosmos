using System.Security.Cryptography;
using System.Text;

// 在线更新清单签名工具（发布侧使用；不参与主程序编译）
// 命令：
//   keygen [--dir <目录>] [--force]           生成 RSA-2048 密钥对（私钥默认 %USERPROFILE%\.newcosmos\update_signing_key.pem），并输出公钥 PEM
//   sign   --key <私钥pem> --canonical <文件> [--out <输出文件>]   对 canonical 字符串做 RSA-SHA256(PKCS#1) 签名，输出 Base64
//   verify --pub <公钥pem> --canonical <文件> --sig <Base64签名文件> 验签（自检用）

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0)
    {
        PrintUsage();
        return 2;
    }

    try
    {
        return args[0].ToLowerInvariant() switch
        {
            "keygen" => KeyGen(args),
            "sign" => Sign(args),
            "verify" => Verify(args),
            _ => Fail($"未知命令: {args[0]}")
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: {ex.Message}");
        return 1;
    }
}

static int KeyGen(string[] args)
{
    var dir = GetArg(args, "--dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".newcosmos");
    var force = args.Any(a => string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase));
    var privPath = Path.Combine(dir, "update_signing_key.pem");

    if (File.Exists(privPath) && !force)
    {
        Console.Error.WriteLine($"私钥已存在: {privPath}（如需重新生成请加 --force；重新生成后旧版本客户端将无法验签）");
        return 3;
    }

    Directory.CreateDirectory(dir);
    using var rsa = RSA.Create(2048);
    var privatePem = rsa.ExportPkcs8PrivateKeyPem();
    var publicPem = rsa.ExportSubjectPublicKeyInfoPem();

    File.WriteAllText(privPath, privatePem, new UTF8Encoding(false));
    Console.WriteLine($"私钥已写入: {privPath}");
    Console.WriteLine("-----BEGIN PUBLIC KEY (粘贴到 Constants/UpdateSignatureConstants.cs 的 PublicKeyPem)-----");
    Console.WriteLine(publicPem.Trim());
    Console.WriteLine("-----END PUBLIC KEY-----");
    return 0;
}

static int Sign(string[] args)
{
    var keyPath = GetArg(args, "--key") ?? throw new ArgumentException("缺少 --key <私钥pem>");
    var canonicalPath = GetArg(args, "--canonical") ?? throw new ArgumentException("缺少 --canonical <文件>");
    var outPath = GetArg(args, "--out");

    var canonical = File.ReadAllText(canonicalPath, new UTF8Encoding(false));
    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(keyPath));
    var signature = Convert.ToBase64String(
        rsa.SignData(Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    if (!string.IsNullOrEmpty(outPath))
    {
        File.WriteAllText(outPath!, signature, new UTF8Encoding(false));
        Console.WriteLine($"签名已写入: {outPath}");
    }
    Console.WriteLine(signature);
    return 0;
}

static int Verify(string[] args)
{
    var pubPath = GetArg(args, "--pub") ?? throw new ArgumentException("缺少 --pub <公钥pem>");
    var canonicalPath = GetArg(args, "--canonical") ?? throw new ArgumentException("缺少 --canonical <文件>");
    var sigPath = GetArg(args, "--sig") ?? throw new ArgumentException("缺少 --sig <Base64签名文件>");

    var canonical = File.ReadAllText(canonicalPath, new UTF8Encoding(false));
    var signature = Convert.FromBase64String(File.ReadAllText(sigPath).Trim());

    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(pubPath));
    var ok = rsa.VerifyData(Encoding.UTF8.GetBytes(canonical), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    Console.WriteLine(ok ? "VERIFY_OK" : "VERIFY_FAILED");
    return ok ? 0 : 4;
}

static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }
    return null;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("""
        用法:
          keygen [--dir <目录>] [--force]
          sign   --key <私钥pem> --canonical <文件> [--out <输出文件>]
          verify --pub <公钥pem> --canonical <文件> --sig <Base64签名文件>
        """);
}
