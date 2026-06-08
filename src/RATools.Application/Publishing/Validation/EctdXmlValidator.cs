using System.Net;
using System.Xml;
using RATools.Application.Abstractions.Publishing;

namespace RATools.Application.Publishing.Validation;

public sealed class EctdXmlValidator : IEctdXmlValidator
{
    private static readonly string[] AllowedDtdFileNames =
    [
        "ich-ectd-3-2.dtd",
        "us-regional-v3-3.dtd"
    ];

    public void Validate(BackboneGeneratedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            ValidationType = ValidationType.DTD,
            XmlResolver = new BundledDtdResolver(),
            IgnoreWhitespace = false
        };
        settings.ValidationEventHandler += (_, args) => errors.Add(args.Message);

        try
        {
            using var reader = XmlReader.Create(
                new StringReader(file.Content),
                settings,
                BuildPackageBaseUri(file.RelativePath));

            while (reader.Read())
            {
            }
        }
        catch (EctdXmlValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw new EctdXmlValidationException(file.RelativePath, exception.Message, exception);
        }

        if (errors.Count > 0)
        {
            throw new EctdXmlValidationException(file.RelativePath, string.Join(" | ", errors));
        }
    }

    private static string BuildPackageBaseUri(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath) || relativePath.Split(['/', '\\']).Any(x => x == ".."))
        {
            throw new EctdXmlValidationException(relativePath, "Package XML relative path is invalid.");
        }

        var normalizedRelativePath = relativePath.Replace('\\', '/');
        return $"file:///ectd-package/{normalizedRelativePath}";
    }

    private sealed class BundledDtdResolver : XmlResolver
    {
        public override ICredentials? Credentials
        {
            set { }
        }

        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var fileName = Path.GetFileName(absoluteUri.LocalPath);
            if (!AllowedDtdFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"DTD system id '{absoluteUri}' is not an allowed bundled eCTD DTD.");
            }

            var dtdPath = Path.Combine(AppContext.BaseDirectory, "reference", "dtd", fileName);
            if (!File.Exists(dtdPath))
            {
                throw new FileNotFoundException($"Bundled eCTD DTD '{fileName}' was not found.", dtdPath);
            }

            return File.OpenRead(dtdPath);
        }

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
        {
            if (string.IsNullOrWhiteSpace(relativeUri))
            {
                throw new InvalidOperationException("DTD system id is empty.");
            }

            return baseUri is null
                ? new Uri(relativeUri, UriKind.RelativeOrAbsolute)
                : new Uri(baseUri, relativeUri);
        }
    }
}
