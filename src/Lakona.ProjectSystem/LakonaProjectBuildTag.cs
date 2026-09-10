using System.Text;
using System.Xml;
using System.Xml.Linq;
using Lakona.ProjectSystem.Packaging;

namespace Lakona.ProjectSystem;

public static class LakonaProjectBuildTag
{
    public static bool IsValid(string? value) => BuildTagReader.IsValid(value);

    public static void Save(string projectRoot, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        if (!IsValid(value))
            throw new ArgumentException("BuildTag must contain 1 to 64 ASCII letters and digits.", nameof(value));

        var path = Path.Combine(Path.GetFullPath(projectRoot), "Server", "BuildTag.props");
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var property = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "LakonaBuildTag")
            ?? throw new InvalidOperationException("BuildTag.props does not define LakonaBuildTag.");
        property.Value = value;
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = XmlWriter.Create(temporaryPath, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = document.Declaration is null,
                NewLineHandling = NewLineHandling.None
            }))
            {
                document.Save(writer);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
