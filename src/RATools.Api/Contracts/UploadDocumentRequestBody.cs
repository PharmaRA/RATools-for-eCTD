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
    [Required]
    public IFormFile? File { get; init; }

    [Required]
    [StringLength(128, MinimumLength = 2)]
    public string CtdSection { get; init; } = string.Empty;
}
