using System.Net;
using System.Net.Http.Headers;

namespace Lakona.Hub.Updates;

internal static class HubPackageDownloader
{
    public static async Task DownloadAsync(HttpClient client, string url, string destination, HubReleaseAsset asset,
        IProgress<HubUpdateProgress>? progress, CancellationToken cancellationToken)
    {
        if (asset.Size is < 1 or > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("The update package size is invalid.");
        var partial = destination + ".part";
        var validator = destination + ".resume";
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long offset = 0;
            string? etag = null;
            if (File.Exists(partial) && File.Exists(validator) && new FileInfo(validator).Length < 1024)
            {
                var lines = await File.ReadAllLinesAsync(validator, cancellationToken);
                if (lines.Length == 2 && lines[0] == asset.Sha256 && EntityTagHeaderValue.TryParse(lines[1], out var tag) && !tag.IsWeak)
                {
                    offset = new FileInfo(partial).Length;
                    etag = tag.ToString();
                }
            }
            if (offset >= asset.Size) offset = 0;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (offset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
                request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(etag!));
            }
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    if (offset == 0 || response.Content.Headers.ContentRange?.From != offset ||
                        response.Content.Headers.ContentRange?.Length != asset.Size || response.Headers.ETag?.ToString() != etag)
                    {
                        File.Delete(validator);
                        throw new IOException("The server did not confirm the partial update package.");
                    }
                }
                else offset = 0;
                if (response.Headers.ETag is { IsWeak: false } receivedTag)
                    await File.WriteAllLinesAsync(validator, [asset.Sha256, receivedTag.ToString()], cancellationToken);
                else File.Delete(validator);
                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var file = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    progress?.Report(new HubUpdateProgress(HubUpdateStage.Downloading, offset, asset.Size));
                    int count;
                    while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
                    {
                        offset = checked(offset + count);
                        if (offset > asset.Size) throw new InvalidDataException("The update exceeds its declared size.");
                        await file.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        progress?.Report(new HubUpdateProgress(HubUpdateStage.Downloading, offset, asset.Size));
                    }
                    if (offset != asset.Size) throw new IOException("The update download is incomplete.");
                }
                File.Move(partial, destination, overwrite: true);
                File.Delete(validator);
                return;
            }
            catch (Exception ex) when (attempt < 2 && (ex is HttpRequestException or IOException) && ex is not InvalidDataException && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), cancellationToken);
            }
        }
    }
}
