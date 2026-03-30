using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed record CreateApplicationRequestBody(
    [property: Required]
    [property: StringLength(32, MinimumLength = 3)]
    string ApplicationNumber,

    [property: Required]
    [property: StringLength(32, MinimumLength = 2)]
    string Region,

    [property: Required]
    [property: StringLength(128, MinimumLength = 2)]
    string SponsorName);
