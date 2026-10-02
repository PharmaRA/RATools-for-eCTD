using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace RATools.Api.Contracts;

public sealed class UploadDocumentRequestBody
{
    [Required]
    public IFormFile? File { get; init; }
}

public sealed class UploadSequenceDocumentRequestBody
{
    public Guid? NodeInstanceId { get; init; }
    public long? ExpectedRevision { get; init; }

    [Required]
    public IFormFile? File { get; init; }

    [Required]
    [StringLength(128, MinimumLength = 2)]
    public string CtdSection { get; init; } = string.Empty;
}
