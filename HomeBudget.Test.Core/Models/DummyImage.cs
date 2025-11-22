using System;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Images;

namespace HomeBudget.Test.Core.Models
{
    internal sealed class DummyImage : IImage
    {
        private const string RepositoryName = "existing";
        private const string ImageTag = "latest";

        public string Repository => RepositoryName;
        public string Name => RepositoryName;
        public string Tag => ImageTag;
        public string FullName => $"{RepositoryName}:{ImageTag}";
        public string Id => string.Empty;
        public string Registry => string.Empty;
        public string Digest => string.Empty;

        public Task CreateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public string GetHostname() => string.Empty;

        public bool MatchLatestOrNightly() => true;

        public bool MatchVersion(Predicate<string> predicate) => predicate(ImageTag);

        public bool MatchVersion(Predicate<Version> predicate)
        {
            try
            {
                var version = new Version(ImageTag);
                return predicate(version);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }
}