using System.Xml.Linq;

internal static class ProjectXmlMutator
{
    public static void SetProperty(XElement project, string name, string value)
    {
        var property = project.Elements("PropertyGroup").SelectMany(group => group.Elements(name)).FirstOrDefault();
        if (property is null)
        {
            var propertyGroup = project.Elements("PropertyGroup").FirstOrDefault() ?? AddElement(project, "PropertyGroup");
            propertyGroup.Add(new XElement(name, value));
            return;
        }

        property.Value = value;
    }

    public static void RemoveProperty(XElement project, string name)
    {
        foreach (var property in project.Elements("PropertyGroup").SelectMany(group => group.Elements(name)).ToArray())
        {
            property.Remove();
        }
    }

    public static void EnsureProjectReference(XElement project, string include, string targetFramework)
    {
        var reference = project
            .Descendants("ProjectReference")
            .FirstOrDefault(element => string.Equals(element.Attribute("Include")?.Value, include, StringComparison.OrdinalIgnoreCase));

        if (reference is null)
        {
            var itemGroup = FindOrAddItemGroup(project);
            reference = new XElement("ProjectReference", new XAttribute("Include", include));
            itemGroup.Add(reference);
        }

        reference.SetAttributeValue("TargetFramework", targetFramework);
        var setTargetFramework = reference.Elements("SetTargetFramework").FirstOrDefault();
        if (setTargetFramework is null)
        {
            reference.Add(new XElement("SetTargetFramework", $"TargetFramework={targetFramework}"));
        }
        else
        {
            setTargetFramework.Value = $"TargetFramework={targetFramework}";
        }
    }

    public static void EnsureProjectReferenceWithoutOutput(XElement project, string include)
    {
        var reference = project
            .Descendants("ProjectReference")
            .FirstOrDefault(element => string.Equals(element.Attribute("Include")?.Value, include, StringComparison.OrdinalIgnoreCase));

        if (reference is null)
        {
            reference = new XElement("ProjectReference", new XAttribute("Include", include));
            FindOrAddItemGroup(project).Add(reference);
        }

        reference.SetAttributeValue("ReferenceOutputAssembly", "false");
    }

    public static void EnsurePackageReference(
        XElement project,
        string include,
        string version,
        params (string Name, string Value)[] attributes)
    {
        var reference = project
            .Descendants("PackageReference")
            .FirstOrDefault(element => string.Equals(element.Attribute("Include")?.Value, include, StringComparison.OrdinalIgnoreCase));

        if (reference is null)
        {
            reference = new XElement(
                "PackageReference",
                new XAttribute("Include", include),
                new XAttribute("Version", version));
            FindOrAddItemGroup(project).Add(reference);
        }
        else
        {
            reference.SetAttributeValue("Version", version);
        }

        foreach (var attribute in attributes)
        {
            reference.SetAttributeValue(attribute.Name, attribute.Value);
        }
    }

    public static void EnsureConditionalPackageReference(
        XElement project,
        string condition,
        string include,
        string version,
        params (string Name, string Value)[] attributes)
    {
        var reference = project
            .Descendants("PackageReference")
            .FirstOrDefault(element => string.Equals(element.Attribute("Include")?.Value, include, StringComparison.OrdinalIgnoreCase));

        if (reference is null)
        {
            var itemGroup = project
                .Elements("ItemGroup")
                .FirstOrDefault(group => string.Equals(group.Attribute("Condition")?.Value, condition, StringComparison.Ordinal));
            if (itemGroup is null)
            {
                itemGroup = new XElement("ItemGroup", new XAttribute("Condition", condition));
                project.Add(itemGroup);
            }

            reference = new XElement("PackageReference", new XAttribute("Include", include));
            itemGroup.Add(reference);
        }

        reference.SetAttributeValue("Version", version);
        foreach (var attribute in attributes)
        {
            reference.SetAttributeValue(attribute.Name, attribute.Value);
        }
    }

    public static void EnsureNoneUpdate(XElement project, string update, string copyToOutputDirectory)
    {
        var none = project
            .Descendants("None")
            .FirstOrDefault(element => string.Equals(element.Attribute("Update")?.Value, update, StringComparison.OrdinalIgnoreCase));

        if (none is null)
        {
            none = new XElement("None", new XAttribute("Update", update));
            FindOrAddItemGroup(project).Add(none);
        }

        var copy = none.Elements("CopyToOutputDirectory").FirstOrDefault();
        if (copy is null)
        {
            none.Add(new XElement("CopyToOutputDirectory", copyToOutputDirectory));
        }
        else
        {
            copy.Value = copyToOutputDirectory;
        }
    }

    public static void EnsureNuGetForUnityPackage(XElement packages, string id, string version)
    {
        if (!string.Equals(packages.Name.LocalName, "packages", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid packages.config root element.");
        }

        var package = packages
            .Elements("package")
            .FirstOrDefault(element => string.Equals(element.Attribute("id")?.Value, id, StringComparison.OrdinalIgnoreCase));

        if (package is null)
        {
            packages.Add(new XElement(
                "package",
                new XAttribute("id", id),
                new XAttribute("version", version),
                new XAttribute("manuallyInstalled", "true")));
            return;
        }

        package.SetAttributeValue("version", version);
        package.SetAttributeValue("manuallyInstalled", "true");
    }

    public static XElement FindOrAddItemGroup(XElement project)
    {
        return project.Elements("ItemGroup").FirstOrDefault() ?? AddElement(project, "ItemGroup");
    }

    private static XElement AddElement(XElement parent, string name)
    {
        var element = new XElement(name);
        parent.Add(element);
        return element;
    }
}
