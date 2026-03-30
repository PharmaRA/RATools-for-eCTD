using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace RATools.Api.Contracts;

public sealed class UploadDocumentRequestBody
{
    [Required]
    public IFormFile? File { get; init; }
}
