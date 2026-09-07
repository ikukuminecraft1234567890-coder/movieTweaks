using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MovieTweaks.Models;

namespace MovieTweaks.Services
{
    public static class ProjectSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static async Task SaveAsync(Project project, string filePath)
        {
            var json = JsonSerializer.Serialize(project, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        public static async Task<Project?> LoadAsync(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<Project>(json, JsonOptions);
        }
    }
}
