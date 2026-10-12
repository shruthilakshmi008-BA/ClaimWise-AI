using System.Security.Cryptography;
using ClaimWise.Api.Data;
using ClaimWise.Api.Domain;
using ClaimWise.Api.Dtos;
using ClaimWise.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClaimWise.Api.Services;

public interface IClaimDocumentService
{
    Task<DocumentDto> UploadAsync(int claimId, UploadClaimDocumentRequest request, CancellationToken ct);
    Task<(Stream Content, string FileName, string ContentType)> OpenAsync(int claimId, int documentId, CancellationToken ct);
}

/// <summary>Accepts the actual supporting file for a required document, with the same rules as the metadata endpoint.</summary>
public sealed class ClaimDocumentService(
    ClaimWiseDbContext db, IClaimFileStore store, ClaimFileOptions options, TimeProvider clock, ILogger<ClaimDocumentService> logger)
    : IClaimDocumentService
{
    private static readonly Dictionary<string, (string ContentType, byte[][] Signatures)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("application/pdf", [[0x25, 0x50, 0x44, 0x46, 0x2D]]),
        [".png"] = ("image/png", [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]]),
        [".jpg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
        [".jpeg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
        [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [[0x50, 0x4B, 0x03, 0x04]]),
        [".txt"] = ("text/plain", []),
    };

    public async Task<DocumentDto> UploadAsync(int claimId, UploadClaimDocumentRequest request, CancellationToken ct)
    {
        var claim = await db.Claims.Include(c => c.Documents).FirstOrDefaultAsync(c => c.Id == claimId, ct)
                    ?? throw new NotFoundException($"Claim {claimId} was not found.");
        if (ClaimWorkflow.IsFinal(claim.Status))
            throw new BusinessRuleException($"Claim {claim.ClaimNumber} is {claim.Status}, so documents can no longer be added.");

        var type = request.DocumentType!.Value;
        if (claim.Documents.Any(d => d.DocumentType == type))
            throw new BusinessRuleException($"Claim {claim.ClaimNumber} already has a {type} document.");

        var file = request.File!;
        var displayName = DisplayName(file.FileName);
        var ext = Path.GetExtension(displayName).ToLowerInvariant();
        if (!Allowed.TryGetValue(ext, out var kind))
            throw new UnsupportedFileException($"Unsupported file type. Allowed: {string.Join(", ", Allowed.Keys.Order())}.");
        if (file.Length == 0) throw new UnsupportedFileException("The file is empty.");
        if (file.Length > options.MaxBytes) throw new PayloadTooLargeException($"The file is larger than the {options.MaxBytes / (1024 * 1024)} MB limit.");

        // Buffer with a hard cap, then verify the content really is what the extension claims.
        await using var buffer = new MemoryStream();
        await using (var input = file.OpenReadStream())
        {
            var block = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(block, ct)) > 0)
            {
                if (buffer.Length + read > options.MaxBytes)
                    throw new PayloadTooLargeException($"The file is larger than the {options.MaxBytes / (1024 * 1024)} MB limit.");
                buffer.Write(block, 0, read);
            }
        }

        var bytes = buffer.ToArray();
        if (!LooksLike(bytes, ext, kind.Signatures))
            throw new UnsupportedFileException($"The file content does not match a {ext} file.");

        var storedName = $"{Guid.NewGuid():N}{(ext == ".jpeg" ? ".jpg" : ext)}";
        var now = clock.GetUtcNow().UtcDateTime;
        buffer.Position = 0;
        await store.SaveAsync(claimId, storedName, buffer, ct);

        var doc = new ClaimDocument
        {
            DocumentType = type, FileName = displayName, ReceivedAt = now, SizeBytes = buffer.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), ContentType = kind.ContentType, StoredFileName = storedName
        };
        claim.Documents.Add(doc);
        claim.UpdatedAt = now;
        claim.Version++;
        db.AuditEntries.Add(new AuditEntry
        {
            ClaimId = claim.Id, Action = "DocumentAdded", Actor = "intake", Timestamp = now,
            Details = $"{type}: {displayName} ({buffer.Length:N0} bytes, sha256 {doc.Sha256![..12]}…)"
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            store.Delete(claimId, storedName); // do not leave an orphaned file behind
            throw;
        }

        logger.LogInformation("Claim {ClaimNumber}: uploaded {Type} document ({Bytes} bytes)", claim.ClaimNumber, type, buffer.Length);
        return ClaimService.ToDocumentDto(doc);
    }

    public async Task<(Stream Content, string FileName, string ContentType)> OpenAsync(int claimId, int documentId, CancellationToken ct)
    {
        var doc = await db.ClaimDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.ClaimId == claimId && d.Id == documentId, ct)
                  ?? throw new NotFoundException($"Document {documentId} was not found for claim {claimId}.");
        if (doc.StoredFileName is null)
            throw new NotFoundException("Only the receipt of this document was recorded; no file was uploaded.");

        try { return (store.OpenRead(claimId, doc.StoredFileName), doc.FileName, doc.ContentType ?? "application/octet-stream"); }
        catch (FileNotFoundException) { throw new NotFoundException("The stored file is no longer available."); }
        catch (DirectoryNotFoundException) { throw new NotFoundException("The stored file is no longer available."); }
    }

    private static string DisplayName(string? name)
    {
        var leaf = Path.GetFileName((name ?? "").Replace('\\', '/'));
        leaf = new string(leaf.Where(ch => !char.IsControl(ch) && "\\/:*?\"<>|".IndexOf(ch) < 0).ToArray()).Trim();
        if (leaf.Length == 0) throw new UnsupportedFileException("The file needs a name with an extension.");
        return leaf.Length > 200 ? leaf[^200..] : leaf;
    }

    private static bool LooksLike(ReadOnlySpan<byte> bytes, string ext, byte[][] signatures)
    {
        if (ext == ".txt") return !bytes.Contains((byte)0);
        foreach (var sig in signatures)
            if (bytes.Length >= sig.Length && bytes[..sig.Length].SequenceEqual(sig)) return true;
        return false;
    }
}
