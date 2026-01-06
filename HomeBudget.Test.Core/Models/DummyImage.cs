using System;
using System.Threading;
using System.Threading.Tasks;

using DotNet.Testcontainers.Images;

internal sealed class DummyImage : IImage
{
    private const string RepositoryName = "existing";
    private const string ImageTag = "latest";

    public string Repository => RepositoryName;
    public string Name => RepositoryName;
    public string Tag => ImageTag;
    public string FullName => $"{RepositoryName}:{ImageTag}";
    public string Id => "dummy-image-id";
    public string Registry => "local";
    public string Digest => "sha256:dummy";
    public string Platform => "linux/amd64";

    public Task CreateAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public string GetHostname() => string.Empty;

    public bool MatchLatestOrNightly() =>
        Tag.Equals("latest", StringComparison.OrdinalIgnoreCase)
        || Tag.Contains("nightly", StringComparison.OrdinalIgnoreCase);

    public bool MatchVersion(Predicate<string> predicate) => predicate(Tag);

    public bool MatchVersion(Predicate<Version> predicate)
    {
        try
        {
            return predicate(new Version(Tag));
        }
        catch (Exception ex) when (
            ex is ArgumentException ||
            ex is FormatException ||
            ex is OverflowException)
        {
            return false;
        }
    }
}
