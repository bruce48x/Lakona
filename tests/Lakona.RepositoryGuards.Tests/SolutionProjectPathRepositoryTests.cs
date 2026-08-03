using System.Xml.Linq;
using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class SolutionProjectPathRepositoryTests
{
    [Fact]
    public void Tracked_solution_project_paths_exist()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var solutionPaths = GitRunner.Run(repositoryRoot, "ls-files", "--", "*.slnx")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var missingProjects = new List<string>();

        Assert.NotEmpty(solutionPaths);

        foreach (var solutionPath in solutionPaths)
        {
            var absoluteSolutionPath = Path.Combine(repositoryRoot, solutionPath);
            var solutionDirectory = Path.GetDirectoryName(absoluteSolutionPath)
                ?? throw new InvalidOperationException($"Solution has no parent directory: {solutionPath}");
            var solution = XDocument.Load(absoluteSolutionPath);

            foreach (var project in solution.Descendants("Project"))
            {
                var projectPath = project.Attribute("Path")?.Value;
                if (string.IsNullOrWhiteSpace(projectPath))
                    continue;

                var absoluteProjectPath = Path.GetFullPath(Path.Combine(solutionDirectory, projectPath));
                if (!File.Exists(absoluteProjectPath))
                    missingProjects.Add($"{solutionPath}: {projectPath}");
            }
        }

        Assert.True(
            missingProjects.Count == 0,
            "Tracked solution project paths must resolve from the solution directory:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, missingProjects.Order(StringComparer.Ordinal)));
    }
}
