using System;
using System.Linq;

namespace Pulse;

/// <summary>A channel that shared Pulse, and the post where they did.</summary>
public sealed record Supporter(string Name, string PostUrl)
{
    /// <summary>"https://t.me/pcgaminghub/28369" → "pcgaminghub".</summary>
    public string Handle => Uri.TryCreate(PostUrl, UriKind.Absolute, out var uri)
        ? uri.AbsolutePath.Trim('/').Split('/')[0]
        : "";

    /// <summary>"PC Gaming Hub" → "PG".</summary>
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2).Select(w => char.ToUpperInvariant(w[0])));
}

/// <summary>Thank-you list shown in the customize window, oldest first. Add new ones at the end.</summary>
public static class Supporters
{
    public static readonly Supporter[] All =
    {
        new("PC Gaming Hub", "https://t.me/pcgaminghub/28369"),
    };
}
