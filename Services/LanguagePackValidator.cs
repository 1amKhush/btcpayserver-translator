using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayTranslator.Services;

public sealed record ValidationIssue(string FileName, string Key, string Reason);

public sealed record ValidationResult(
    int FilesScanned,
    int EntriesScanned,
    List<ValidationIssue> Issues);

public class LanguagePackValidator
{
    private static readonly Regex PlaceholderRegex =
        new(@"\{[A-Za-z0-9_]+\}", RegexOptions.Compiled);

    private static readonly Regex[] SuspiciousMetaPatterns =
    {
        new(@"\bplease provide (the )?english text\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bi\s*(?:am|'m) ready to translate\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\btranslate english text to\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bi understand(?:\s+the\s+instructions)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bi don't see any text\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\byou haven't provided any text\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bprofessional translator for btcpay server\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bas an ai\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<LanguagePackValidator> _logger;

    public LanguagePackValidator(IConfiguration configuration, ILogger<LanguagePackValidator> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateAsync(bool fix)
    {
        var outputDirectory = _configuration["Translation:OutputDirectory"] ?? "translations";

        if (!Directory.Exists(outputDirectory))
        {
            return new ValidationResult(0, 0, new List<ValidationIssue>
            {
                new("<none>", "<none>", $"Translation directory '{outputDirectory}' does not exist")
            });
        }

        var files = Directory.GetFiles(outputDirectory, "*.json").OrderBy(path => path).ToList();
        var issues = new List<ValidationIssue>();
        var totalEntries = 0;

        foreach (var filePath in files)
        {
            var content = await File.ReadAllTextAsync(filePath);
            var json = JObject.Parse(content);
            var fileChanged = false;

            foreach (var property in json.Properties().ToList())
            {
                var key = property.Name;
                var value = property.Value?.ToString() ?? string.Empty;
                totalEntries++;

                if (IsSuspiciousMetaResponse(value))
                {
                    issues.Add(new ValidationIssue(Path.GetFileName(filePath), key, "Suspicious LLM/meta-response content"));
                    if (fix)
                    {
                        property.Value = key;
                        fileChanged = true;
                    }
                    continue;
                }

                if (!HasMatchingPlaceholders(key, value))
                {
                    issues.Add(new ValidationIssue(Path.GetFileName(filePath), key, "Placeholder/token mismatch between source key and translation"));
                    if (fix)
                    {
                        property.Value = key;
                        fileChanged = true;
                    }
                }
            }

            if (fix && fileChanged)
            {
                await File.WriteAllTextAsync(filePath, json.ToString(Formatting.Indented));
                _logger.LogInformation("Fixed suspicious/mismatched entries in {FileName}", Path.GetFileName(filePath));
            }
        }

        return new ValidationResult(files.Count, totalEntries, issues);
    }

    private static bool IsSuspiciousMetaResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return SuspiciousMetaPatterns.Any(pattern => pattern.IsMatch(text));
    }

    private static bool HasMatchingPlaceholders(string source, string translation)
    {
        var sourceTokens = ExtractTokenCounts(source);
        var translationTokens = ExtractTokenCounts(translation);

        if (sourceTokens.Count != translationTokens.Count)
            return false;

        foreach (var token in sourceTokens)
        {
            if (!translationTokens.TryGetValue(token.Key, out var count) || count != token.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, int> ExtractTokenCounts(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match match in PlaceholderRegex.Matches(text))
        {
            if (!counts.TryAdd(match.Value, 1))
            {
                counts[match.Value]++;
            }
        }

        return counts;
    }
}
