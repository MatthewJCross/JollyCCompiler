using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JollyCCompiler.Project
{
    public sealed class JollyProjectService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public JollyProject Create(string name, string projectFilePath)
        {
            var project = new JollyProject
            {
                Name = name,
                ProjectFilePath = projectFilePath
            };

            return project;
        }

        public void Save(JollyProject project, string projectFilePath)
        {
            project.ProjectFilePath = projectFilePath;

            var directory = Path.GetDirectoryName(projectFilePath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(project, JsonOptions);

            File.WriteAllText(projectFilePath, json);
        }

        public JollyProject Open(string projectFilePath)
        {
            var json = File.ReadAllText(projectFilePath);
            var project = JsonSerializer.Deserialize<JollyProject>(json, JsonOptions);

            if (project is null)
                throw new InvalidOperationException("The project file could not be loaded.");

            project.ProjectFilePath = projectFilePath;

            return project;
        }

        public string GetSourcePath(JollyProject project, string sourceFile)
        {
            if (Path.IsPathRooted(sourceFile))
                return sourceFile;

            if (string.IsNullOrWhiteSpace(project.ProjectDirectory))
                return Path.GetFullPath(sourceFile);

            return Path.GetFullPath(Path.Combine(project.ProjectDirectory, sourceFile));
        }

        public string GetOutputDirectory(JollyProject project)
        {
            if (string.IsNullOrWhiteSpace(project.ProjectDirectory))
                return Path.GetFullPath(project.OutputDirectory);

            if (Path.IsPathRooted(project.OutputDirectory))
                return project.OutputDirectory;

            return Path.GetFullPath(Path.Combine(project.ProjectDirectory, project.OutputDirectory));
        }

        public string GetRelativePath(JollyProject project, string filePath)
        {
            if (string.IsNullOrWhiteSpace(project.ProjectDirectory))
                return filePath;

            return Path.GetRelativePath(project.ProjectDirectory, filePath);
        }
    }
}
