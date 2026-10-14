// ============================================================
// Tests — Docker image must ship the ONNX embedding model
//
// WHY THIS EXISTS
// appsettings.json ships Embedding:Provider=Onnx, and Program.cs eagerly
// resolves IEmbeddingService at startup so a misconfigured provider fails
// fast. OnnxEmbeddingService's constructor throws FileNotFoundException when
// the model or vocab is absent. models/ is gitignored (~127 MB), so the ONLY
// thing that puts the model into the runtime image is the Dockerfile's
// model-fetch stage.
//
// That stage was silently deleted once already when the Dockerfile was
// rewritten for an unrelated feature. Nothing caught it: the solution still
// built, every unit test still passed, and the break only surfaced as a
// crash-looping container. These are static-text assertions precisely
// because the failure lives in a file no test would otherwise read.
// ============================================================

using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace OmniSift.UnitTests.Deployment;

public sealed class DockerModelPackagingTests
{
    private const string SolutionFile = "Lindrake.sln";
    private const string DockerfileRelativePath = "src/Lindrake.Api/Dockerfile";
    private const string AppSettingsRelativePath = "src/Lindrake.Api/appsettings.json";

    /// <summary>
    /// ASP.NET Core sets ContentRootPath to the app's working directory, which is
    /// WORKDIR in the runtime stage. Relative Embedding paths resolve against it.
    /// </summary>
    private const string ContentRoot = "/app";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFile)))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate {SolutionFile} walking up from {AppContext.BaseDirectory}.");
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private sealed record EmbeddingConfig(string Provider, string ModelPath, string TokenizerPath);

    private static EmbeddingConfig ReadEmbeddingConfig()
    {
        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        using var doc = JsonDocument.Parse(ReadRepoFile(AppSettingsRelativePath), options);
        var section = doc.RootElement.GetProperty("Embedding");

        return new EmbeddingConfig(
            section.GetProperty("Provider").GetString()!,
            section.GetProperty("ModelPath").GetString()!,
            section.GetProperty("TokenizerPath").GetString()!);
    }

    /// <summary>
    /// Resolves the absolute in-image path of a ContentRoot-relative Embedding
    /// path by replaying the Dockerfile's model-fetch WORKDIR and the
    /// <c>COPY --from=model-fetch &lt;src&gt; &lt;dest&gt;</c> that lifts it into
    /// the runtime stage. Returns null when either directive is missing.
    /// </summary>
    private static string? ResolveInImagePath(string dockerfile, string contentRootRelativePath)
    {
        // WORKDIR of the model-fetch stage — the directory the files are downloaded into.
        var stageStart = dockerfile.IndexOf("AS model-fetch", StringComparison.OrdinalIgnoreCase);
        if (stageStart < 0)
            return null;

        var workdirMatch = Regex.Match(
            dockerfile[stageStart..],
            @"^\s*WORKDIR\s+(?<dir>\S+)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!workdirMatch.Success)
            return null;

        var copyMatch = Regex.Match(
            dockerfile,
            @"^\s*COPY\s+--from=model-fetch\s+(?<src>\S+)\s+(?<dest>\S+)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!copyMatch.Success)
            return null;

        var workdir = workdirMatch.Groups["dir"].Value.TrimEnd('/');
        var copySrc = copyMatch.Groups["src"].Value.TrimEnd('/');
        var copyDest = copyMatch.Groups["dest"].Value.TrimEnd('/');

        // The download directory must sit under what the COPY actually lifts,
        // otherwise the files never reach the runtime stage at all.
        if (!workdir.Equals(copySrc, StringComparison.Ordinal) &&
            !workdir.StartsWith(copySrc + "/", StringComparison.Ordinal))
            return null;

        // /models/bge-small-en-v1.5 relative to /models => bge-small-en-v1.5
        var withinCopy = workdir[copySrc.Length..].TrimStart('/');

        // The Embedding path is <dir>/<file>; the download WORKDIR already supplies
        // <dir>, so only the file name is appended.
        var fileName = contentRootRelativePath.Replace('\\', '/').Split('/')[^1];

        var segments = new[] { copyDest, withinCopy, fileName }
            .Where(s => !string.IsNullOrEmpty(s));

        return string.Join('/', segments);
    }

    private static string ExpectedAbsolutePath(string contentRootRelativePath) =>
        $"{ContentRoot}/{contentRootRelativePath.Replace('\\', '/').TrimStart('/')}";

    [Fact]
    public void ApiDockerfile_ShipsTheOnnxModel_WhenAppSettingsSelectsTheOnnxProvider()
    {
        var config = ReadEmbeddingConfig();

        if (!config.Provider.Equals("Onnx", StringComparison.OrdinalIgnoreCase))
            return; // OpenAI provider selected — no model needs to be baked into the image.

        var dockerfile = ReadRepoFile(DockerfileRelativePath);

        dockerfile.Should().Contain(
            "AS model-fetch",
            because: "appsettings.json selects the ONNX provider, so the image must fetch the model; " +
                     "without this stage the API throws FileNotFoundException at startup and crash-loops");

        foreach (var embeddingPath in new[] { config.ModelPath, config.TokenizerPath })
        {
            var resolved = ResolveInImagePath(dockerfile, embeddingPath);

            resolved.Should().NotBeNull(
                because: $"the Dockerfile must copy the model-fetch output into the runtime stage so " +
                         $"'{embeddingPath}' exists in the image");

            resolved.Should().Be(
                ExpectedAbsolutePath(embeddingPath),
                because: $"OnnxEmbeddingService resolves '{embeddingPath}' against ContentRootPath " +
                         $"({ContentRoot}); the Dockerfile's WORKDIR + COPY must land the file exactly there");
        }
    }

    [Fact]
    public void ApiDockerfile_PinsModelDownloadsToImmutableRevisionsWithRealChecksums()
    {
        var dockerfile = ReadRepoFile(DockerfileRelativePath);

        if (!dockerfile.Contains("AS model-fetch", StringComparison.OrdinalIgnoreCase))
            return; // Covered by the packaging test above.

        // A HuggingFace `resolve/main/...` URL is a moving target: an upstream retag
        // would change tokenization or output width with no signal.
        var unpinnedUrls = Regex.Matches(dockerfile, @"huggingface\.co/\S*?/resolve/(?<rev>[^/\s""]+)/")
            .Select(m => m.Groups["rev"].Value)
            .Where(rev => !Regex.IsMatch(rev, @"^(\$\{?[A-Z_]+\}?|[0-9a-f]{40})$"))
            .ToList();

        unpinnedUrls.Should().BeEmpty(
            because: "HuggingFace download URLs must pin an immutable 40-hex commit revision, not a branch name");

        // Every revision ARG must carry a real 40-hex commit SHA.
        var revisionArgs = Regex.Matches(dockerfile, @"^\s*ARG\s+(?<name>\w*REVISION)\s*=\s*""?(?<value>[^""\s]*)""?",
            RegexOptions.Multiline);

        revisionArgs.Should().NotBeEmpty(because: "the pinned revisions should be declared as build args");

        foreach (Match arg in revisionArgs)
        {
            arg.Groups["value"].Value.Should().MatchRegex(
                "^[0-9a-f]{40}$",
                because: $"{arg.Groups["name"].Value} must default to a real HuggingFace commit SHA");
        }

        // Every checksum ARG must carry a real 64-hex sha256 — a placeholder here
        // makes `sha256sum -c` fail and takes the whole image build down.
        var checksumArgs = Regex.Matches(dockerfile, @"^\s*ARG\s+(?<name>\w*SHA256)\s*=\s*""?(?<value>[^""\s]*)""?",
            RegexOptions.Multiline);

        checksumArgs.Should().NotBeEmpty(because: "downloads must be checksum-verified inside the image build");

        foreach (Match arg in checksumArgs)
        {
            arg.Groups["value"].Value.Should().MatchRegex(
                "^[0-9a-f]{64}$",
                because: $"{arg.Groups["name"].Value} must default to a real sha256, not a placeholder");
        }

        dockerfile.Should().Contain(
            "sha256sum -c",
            because: "the fetched model and vocab must be checksum-verified during the image build");
    }
}
