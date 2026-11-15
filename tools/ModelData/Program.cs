// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.ModelData;

// dotnet run --project tools/ModelData -- --litellm <file> --commit <sha> [--date YYYY-MM-DD] [--out default/models.json] [--report report.md]
// Exit 0: written (or unchanged); 1: the checks failed and nothing was written; 2: wrong arguments.
var opt = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 0; i + 1 < args.Length; i += 2)
{
    opt[args[i]] = args[i + 1];
}

if (!opt.TryGetValue("--litellm", out var source) || !opt.TryGetValue("--commit", out var commit))
{
    Console.Error.WriteLine("Usage: --litellm <model_prices_and_context_window.json> --commit <sha> [--date YYYY-MM-DD] [--out default/models.json] [--report report.md]");
    return 2;
}

var output = opt.GetValueOrDefault("--out", Path.Combine("default", "models.json"));
var date = opt.GetValueOrDefault("--date", DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
var current = File.Exists(output) ? JsonNode.Parse(File.ReadAllText(output)) as JsonObject : null;
var litellm = JsonNode.Parse(File.ReadAllText(source)) as JsonObject ?? throw new InvalidDataException("LiteLLM's file is not a JSON object.");

var file = Generator.Build(litellm, commit, date, current);
var text = Writer.Write(file);
var problems = Checks.Problems(file, current, System.Text.Encoding.UTF8.GetByteCount(text));
if (problems.Count > 0)
{
    Console.Error.WriteLine("The new model data fails its checks; nothing was written:");
    problems.ForEach(p => Console.Error.WriteLine("- " + p));
    return 1;
}

File.WriteAllText(output, text);
var report = Checks.Report(file, current);
if (opt.TryGetValue("--report", out var reportFile))
{
    File.WriteAllText(reportFile, report);
}

Console.WriteLine(report);
return 0;
