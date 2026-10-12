using System.Text.RegularExpressions;

namespace ClaimWise.Api.Services;

public sealed record ClaimFileOptions
{
    public const string SectionName = "ClaimFiles";

    /// <summary>Directory for uploaded claim documents. Relative paths resolve against the content root.</summary>
    public string Path { get; init; } = "claim-files";
    public long MaxBytes { get; init; } = 10 * 1024 * 1024;
}

public interface IClaimFileStore
{
    Task SaveAsync(int claimId, string storedName, Stream content, CancellationToken ct);
    Stream OpenRead(int claimId, string storedName);
    void Delete(int claimId, string storedName);
}

/// <summary>
/// Local-disk storage. File names on disk are server-generated (GUID + whitelisted extension), so an uploaded name can
/// never influence the path; reads re-validate the stored name and confirm the resolved path stays under the root.
/// </summary>
public sealed partial class LocalClaimFileStore(ClaimFileOptions options, IHostEnvironment environment) : IClaimFileStore
{
    [GeneratedRegex(@"^[0-9a-f]{32}\.(pdf|png|jpg|jpeg|txt|docx)$")]
    private static partial Regex StoredNamePattern();

    private string Root => System.IO.Path.GetFullPath(
        System.IO.Path.IsPathRooted(options.Path) ? options.Path : System.IO.Path.Combine(environment.ContentRootPath, options.Path));

    public async Task SaveAsync(int claimId, string storedName, Stream content, CancellationToken ct)
    {
        var path = Resolve(claimId, storedName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, ct);
    }

    public Stream OpenRead(int claimId, string storedName) =>
        new FileStream(Resolve(claimId, storedName), FileMode.Open, FileAccess.Read, FileShare.Read);

    public void Delete(int claimId, string storedName)
    {
        var path = Resolve(claimId, storedName);
        if (File.Exists(path)) File.Delete(path);
    }

    private string Resolve(int claimId, string storedName)
    {
        if (!StoredNamePattern().IsMatch(storedName)) throw new ArgumentException("Invalid stored file name.", nameof(storedName));
        var root = Root;
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, claimId.ToString(), storedName));
        if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Resolved path escapes the storage root.");
        return full;
    }
}
