using Android.App;
using Android.Content;
using Android.Provider;
using NewCosmos.Services.Platform;

namespace NewCosmos.Services.Platform;

/// <summary>
/// Android 文件夹选择器 - 通过 SAF（ACTION_OPEN_DOCUMENT_TREE）选择目录，
/// 将目录内文件复制到应用缓存后返回真实文件系统路径，
/// 使共享业务逻辑（如月度审核批量上传 <c>Directory.GetFiles(path, "*.pdf")</c>）无需感知平台差异。
/// 结果经 <see cref="MainActivity.OnActivityResult"/> 回填到静态 TCS。
/// </summary>
public class AndroidFolderPickerService : IFolderPickerService
{
    private const int RequestCode = 0x4A01;

    private static readonly Lock _gate = new();
    private static TaskCompletionSource<Android.Net.Uri?>? _pending;
    private static string? _lastDirectory;

    public async Task<FolderPickResult> PickFolderAsync(string title = "选择保存位置", string? initialDirectory = null)
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null)
                return FolderPickResult.Failed("无法获取当前 Activity");

            TaskCompletionSource<Android.Net.Uri?> tcs;
            lock (_gate)
            {
                tcs = new TaskCompletionSource<Android.Net.Uri?>();
                _pending = tcs;
            }

            var intent = new Intent(Intent.ActionOpenDocumentTree);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission
                          | ActivityFlags.GrantPersistableUriPermission
                          | ActivityFlags.GrantPrefixUriPermission);
            activity.StartActivityForResult(intent, RequestCode);

            var uri = await tcs.Task;
            if (uri == null)
                return FolderPickResult.Canceled();

            var cacheDir = await CopyTreeToCacheAsync(activity, uri);
            if (string.IsNullOrEmpty(cacheDir))
                return FolderPickResult.Failed("所选文件夹中没有可读取的文件");

            _lastDirectory = cacheDir;
            return FolderPickResult.Picked(cacheDir);
        }
        catch (Exception ex)
        {
            return FolderPickResult.Failed(ex.Message);
        }
    }

    public string? GetLastDirectory() => _lastDirectory;

    /// <summary>由 <see cref="MainActivity.OnActivityResult"/> 调用，回填选择结果；返回 true 表示本次结果已被消费。</summary>
    public static bool HandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestCode)
            return false;

        TaskCompletionSource<Android.Net.Uri?>? tcs;
        lock (_gate)
        {
            tcs = _pending;
            _pending = null;
        }

        if (tcs == null)
            return false;

        tcs.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
        return true;
    }

    /// <summary>枚举所选目录（非递归）下的文件并复制到应用缓存目录，返回缓存目录绝对路径。</summary>
    private static async Task<string?> CopyTreeToCacheAsync(Context context, Android.Net.Uri treeUri)
    {
        var resolver = context.ContentResolver;
        if (resolver == null)
            return null;

        var treeDocId = DocumentsContract.GetTreeDocumentId(treeUri);
        if (string.IsNullOrEmpty(treeDocId))
            return null;

        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, treeDocId);
        if (childrenUri == null)
            return null;

        var outputDir = Path.Combine(
            Microsoft.Maui.Storage.FileSystem.CacheDirectory,
            "picked_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);

        var projection = new[]
        {
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnMimeType,
        };

        int copied = 0;
        using var cursor = resolver.Query(childrenUri, projection, null, null, null);
        if (cursor != null && cursor.MoveToFirst())
        {
            int idIdx = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDocumentId);
            int nameIdx = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDisplayName);
            int mimeIdx = cursor.GetColumnIndex(DocumentsContract.Document.ColumnMimeType);

            do
            {
                if (idIdx < 0) continue;

                var docId = cursor.GetString(idIdx);
                if (string.IsNullOrEmpty(docId)) continue;

                var mime = mimeIdx >= 0 ? cursor.GetString(mimeIdx) : null;
                if (mime == DocumentsContract.Document.MimeTypeDir) continue;

                var name = nameIdx >= 0 ? cursor.GetString(nameIdx) : null;
                if (string.IsNullOrEmpty(name)) name = $"file_{copied}";

                var fileUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, docId);
                if (fileUri == null) continue;

                using var input = resolver.OpenInputStream(fileUri);
                if (input == null) continue;

                using var output = File.Create(Path.Combine(outputDir, name));
                await input.CopyToAsync(output);
                copied++;
            }
            while (cursor.MoveToNext());
        }

        if (copied == 0)
        {
            TryDeleteDirectory(outputDir);
            return null;
        }

        return outputDir;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // 清理失败不影响主流程
        }
    }
}
