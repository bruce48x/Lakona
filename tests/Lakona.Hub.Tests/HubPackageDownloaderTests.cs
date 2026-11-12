using System.Net;
using System.Net.Http.Headers;
using Lakona.Hub.Updates;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubPackageDownloaderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resume_requires_a_matching_validator_and_accepts_full_restart(bool supportsResume)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "hub.zip");
        try
        {
            await File.WriteAllBytesAsync(destination + ".part", [1, 2], TestContext.Current.CancellationToken);
            await File.WriteAllLinesAsync(destination + ".resume", [new string('a', 64), "\"original\""], TestContext.Current.CancellationToken);
            using var client = new HttpClient(new Handler(request =>
            {
                Assert.Equal(2, request.Headers.Range!.Ranges.Single().From);
                Assert.Equal("\"original\"", request.Headers.IfRange!.EntityTag!.Tag);
                var response = new HttpResponseMessage(supportsResume ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                { Content = new ByteArrayContent(supportsResume ? [3, 4] : [1, 2, 3, 4]) };
                response.Headers.ETag = new EntityTagHeaderValue("\"original\"");
                if (supportsResume) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(2, 3, 4);
                return response;
            }));
            await HubPackageDownloader.DownloadAsync(client, "https://example.test/update", destination, new HubReleaseAsset("hub.zip", new string('a', 64), 4), null, TestContext.Current.CancellationToken);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(destination + ".part"));
            Assert.False(File.Exists(destination + ".resume"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Oversized_payload_is_rejected_before_publishing_a_download()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "hub.zip");
        try
        {
            using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }));
            await Assert.ThrowsAsync<InvalidDataException>(() => HubPackageDownloader.DownloadAsync(client, "https://example.test/update", destination,
                new HubReleaseAsset("hub.zip", new string('a', 64), 2), null, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(destination));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Retry_is_bounded_and_a_mismatching_range_restarts_from_zero()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "hub.zip");
        var requests = 0;
        try
        {
            await File.WriteAllBytesAsync(destination + ".part", [1], TestContext.Current.CancellationToken);
            await File.WriteAllLinesAsync(destination + ".resume", [new string('a', 64), "\"original\""], TestContext.Current.CancellationToken);
            using var client = new HttpClient(new Handler(request =>
            {
                requests++;
                if (requests == 1) return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([2]) };
                Assert.Null(request.Headers.Range);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }));
            await Assert.ThrowsAsync<HttpRequestException>(() => HubPackageDownloader.DownloadAsync(client, "https://example.test/update", destination,
                new HubReleaseAsset("hub.zip", new string('a', 64), 2), null, TestContext.Current.CancellationToken));
            Assert.Equal(3, requests);
            Assert.False(File.Exists(destination));
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
