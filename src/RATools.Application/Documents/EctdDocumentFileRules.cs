namespace RATools.Application.Documents;

internal static class EctdDocumentFileRules
{
    private static readonly HashSet<string> AllowedExtensions =
    [
        ".pdf",
        ".xml",
        ".xpt",
        ".sas7bdat",
        ".txt",
        ".rtf",
        ".doc",
        ".docx",
        ".xls",
        ".xlsx",
        ".csv",
        ".jpg",
        ".jpeg",
        ".png",
        ".tif",
        ".tiff"
    ];

    public static bool IsAllowedFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            return false;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(extension) && AllowedExtensions.Contains(extension);
    }

    public static string GetMediaType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".xml" => "application/xml",
            ".xpt" => "application/octet-stream",
            ".sas7bdat" => "application/octet-stream",
            ".txt" => "text/plain",
            ".rtf" => "application/rtf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".csv" => "text/csv",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".tif" => "image/tiff",
            ".tiff" => "image/tiff",
            _ => "application/octet-stream"
        };
    }

    public static string? TryGetMediaType(string fileName)
    {
        if (!IsAllowedFileName(fileName))
        {
            return null;
        }

        return GetMediaType(fileName);
    }

    public static string BuildAllowedExtensionsMessage()
    {
        return "Allowed extensions: .pdf, .xml, .xpt, .sas7bdat, .txt, .rtf, .doc, .docx, .xls, .xlsx, .csv, .jpg, .jpeg, .png, .tif, .tiff.";
    }
}
