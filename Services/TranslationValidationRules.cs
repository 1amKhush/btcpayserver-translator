using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BTCPayTranslator.Services;

internal static class TranslationValidationRules
{
    private static readonly Regex PlaceholderRegex =
        new(@"\{[A-Za-z0-9_]+\}", RegexOptions.Compiled);

    private static readonly Regex TokenRegex =
        new(@"[A-Za-z0-9+./_-]+", RegexOptions.Compiled);

    private static readonly Regex[] SuspiciousMetaPatterns =
    {
        new(@"\bplease provide (the )?english text\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bwaiting for the english text\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bi\s*(?:am|'m) ready to translate\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bready to translate english(?:\s+to\s+[a-z\s\-()]+)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\btranslate english text to\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bplease provide the text (?:you(?:'d)? like me to translate|you want me to translate|to translate)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bi understand(?:\s+the\s+instructions)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bi don't see any text\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\byou haven't provided any text\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bprofessional translator for btcpay server\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bas an ai\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    private static readonly HashSet<string> TechnicalAllowTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "api",
        "apis",
        "btc",
        "lnurl",
        "lnurlp",
        "auth",
        "node",
        "grpc",
        "ssl",
        "cipher",
        "suite",
        "suites",
        "bolt11",
        "bolt12",
        "bip21",
        "json",
        "csv",
        "http",
        "https",
        "url",
        "uri",
        "oauth",
        "webhook",
        "webhooks",
        "docker",
        "github",
        "btcpay",
        "bitcoin",
        "lightning",
        "nostr",
        "nfc",
        "tor",
        "psbt"
    };

    public static bool IsSuspiciousMetaResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return SuspiciousMetaPatterns.Any(pattern => pattern.IsMatch(text));
    }

    public static bool HasMatchingPlaceholders(string source, string translation)
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

    public static bool IsLikelySentenceFallback(string source, string translation)
    {
        if (!string.Equals(source, translation, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrWhiteSpace(source) || source.Length < 20)
            return false;

        if (PlaceholderRegex.IsMatch(source))
            return false;

        var words = source.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 4)
            return false;

        if (!source.Any(char.IsLower))
            return false;

        var tokens = TokenRegex.Matches(source).Select(match => match.Value).ToList();
        if (tokens.Count == 0)
            return false;

        foreach (var token in tokens)
        {
            if (TechnicalAllowTokens.Contains(token))
                continue;

            if (token.All(ch => char.IsUpper(ch) || char.IsDigit(ch) || ch == '_' || ch == '-'))
                continue;

            return true;
        }

        return false;
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