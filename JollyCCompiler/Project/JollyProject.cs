using System.IO;
using System.Text.Json.Serialization;

namespace JollyCCompiler.Project
{
    public sealed class JollyProject
    {
        public string Name { get; set; } = "JollyCProgram";
        public ProjectTarget Target { get; set; } = ProjectTarget.X64;
        public string OutputDirectory { get; set; } = "output";
        public string? VicePath { get; set; }
        public List<string> SourceFiles { get; set; } = new();

        [JsonIgnore]
        public string? ProjectFilePath { get; set; }

        [JsonIgnore]
        public string? ProjectDirectory => string.IsNullOrWhiteSpace(ProjectFilePath) ? null : Path.GetDirectoryName(ProjectFilePath);
    }
}