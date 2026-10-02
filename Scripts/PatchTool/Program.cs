using BsDiff;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

// 兼容 dotnet run（args[0]=DLL路径）和直接执行（args[0]=命令）
int cmdIndex = 0;
if (args.Length >= 2 && args[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    cmdIndex = 1;

var cmd = args.Length > cmdIndex ? args[cmdIndex].ToLowerInvariant() : "";
string? oldFile = null, newFile = null, patchFile = null, outputFile = null;

for (int i = cmdIndex + 1; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--old": case "--base": oldFile = args[++i]; break;
        case "--new": newFile = args[++i]; break;
        case "--patch": patchFile = args[++i]; break;
        case "--out": outputFile = args[++i]; break;
    }
}

if (cmd == "create")
{
    if (oldFile == null || newFile == null || outputFile == null)
    { Console.Error.WriteLine("Usage: PatchTool create --old <base> --new <target> --out <output>"); return 1; }
    if (!File.Exists(oldFile)) { Console.Error.WriteLine($"Base not found: {oldFile}"); return 1; }
    if (!File.Exists(newFile)) { Console.Error.WriteLine($"Target not found: {newFile}"); return 1; }

    Console.WriteLine($"Creating patch: {Path.GetFileName(oldFile)} -> {Path.GetFileName(newFile)}");
    var oldData = File.ReadAllBytes(oldFile);
    var newData = File.ReadAllBytes(newFile);

    // 写 patch（using 确保流释放后再计算 SHA）
    using (var outStream = File.Create(outputFile))
    {
        BinaryPatch.Create(oldData, newData, outStream);
    }

    var patchSize = new FileInfo(outputFile).Length;
    Console.WriteLine($"Patch created: {outputFile} ({patchSize:N0} bytes)");
    Console.WriteLine($"SHA-256: {ComputeSha256(outputFile)}");
    return 0;
}
else if (cmd == "apply")
{
    if (oldFile == null || patchFile == null || outputFile == null)
    { Console.Error.WriteLine("Usage: PatchTool apply --base <base> --patch <patch> --out <output>"); return 1; }
    if (!File.Exists(oldFile)) { Console.Error.WriteLine($"Base not found: {oldFile}"); return 1; }
    if (!File.Exists(patchFile)) { Console.Error.WriteLine($"Patch not found: {patchFile}"); return 1; }

    Console.WriteLine($"Applying patch: {Path.GetFileName(patchFile)} to {Path.GetFileName(oldFile)}");
    var patchData = File.ReadAllBytes(patchFile);

    // 应用 patch（using 确保流释放后再计算 SHA）
    using (var baseStream = File.OpenRead(oldFile))
    using (var outStream = File.Create(outputFile))
    {
        BinaryPatch.Apply(baseStream, () => new MemoryStream(patchData), outStream);
    }

    var newSize = new FileInfo(outputFile).Length;
    Console.WriteLine($"Result: {outputFile} ({newSize:N0} bytes)");
    Console.WriteLine($"SHA-256: {ComputeSha256(outputFile)}");
    return 0;
}
else
{
    Console.Error.WriteLine("Usage: PatchTool <create|apply> --old/--base <base> --new/--patch <file> --out <output>");
    return 1;
}

static string ComputeSha256(string path)
{
    using var sha = SHA256.Create();
    using var stream = File.OpenRead(path);
    var hash = sha.ComputeHash(stream);
    return Convert.ToHexString(hash).ToLowerInvariant();
}
