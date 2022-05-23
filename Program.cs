using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using OpenApiBrowser.Model;

namespace ApiToDart
{
    internal class Program
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static async Task ConvertSchemaAsync(DartConverter converter, DataSchema schema, string schemaName)
        {
            var settings = Utils.GetJobSettings(converter.Job, schemaName);

            if (settings.Ignore)
            {
                Console.WriteLine("Skipped " + schemaName);
                return;
            }
            else
            {
                Console.WriteLine("Converting " + schemaName + "...");
            }

            string fileName = schemaName.ApplyNaming(DartConverter.DartNamingConvention.FileName) + ".dart";
            string filePath = Path.Combine(settings.OutputDirectory, fileName);
            string source = converter.ToDartFile(schema, schemaName);

            await File.WriteAllTextAsync(filePath, source);
        }

        private static async Task Main(string[] args)
        {
            var jobPaths = Directory.GetFiles("jobs", "*.json");
            var tasks = jobPaths.Select((path) => ParseAndRunJobAsync(path));

            await Task.WhenAll(tasks);
        }

        private static async Task ParseAndRunJobAsync(string path)
        {
            await using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var job = await JsonSerializer.DeserializeAsync<Job>(fileStream, _jsonOptions);
                await RunJobAsync(job);
            }
        }

        private static async Task RunJobAsync(Job job)
        {
            var json = await Utils.FetchJson(job);
            var specification = JsonSerializer.Deserialize<Specification>(json, _jsonOptions);
            var converter = new DartConverter(job, specification);

            var outputDirectoryPath = job.Default.OutputDirectory;
            if (!Directory.Exists(outputDirectoryPath))
            {
                Directory.CreateDirectory(outputDirectoryPath);
            }

            var schemas = converter.Schemas.Where(kv => kv.Schema.Type == "object");

            foreach (var (key, schema) in schemas)
            {
                if (schema.Properties == null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Schema {key} had no data");
                    Console.ResetColor();
                    continue;
                }

                await ConvertSchemaAsync(converter, schema, schema.Title ?? key);
            }
        }
    }
}