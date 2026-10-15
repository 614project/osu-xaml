using System;
using System.Collections.Generic;
using System.Text;

namespace OsuXaml.Generator
{
    /// <summary>
    /// A parsed <c>{Bind Path, Key=Value, ...}</c> markup extension.
    /// Values may be single-quoted to contain commas, e.g. <c>StringFormat='{0} left, {1}'</c>.
    /// </summary>
    internal sealed class BindMarkup
    {
        public string Path;
        public readonly Dictionary<string, string> Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Get(string key) => Options.TryGetValue(key, out string v) ? v : null;

        public static bool IsMarkup(string value) => value.StartsWith("{", StringComparison.Ordinal) && !value.StartsWith("{}", StringComparison.Ordinal);

        /// <summary>Strips the <c>{}</c> escape that lets a literal start with a brace.</summary>
        public static string Unescape(string value) => value.StartsWith("{}", StringComparison.Ordinal) ? value.Substring(2) : value;

        public static BindMarkup Parse(string value, out string error)
        {
            error = null;
            string body = value.Trim();

            if (!body.EndsWith("}", StringComparison.Ordinal))
            {
                error = $"Markup extension '{value}' is missing a closing brace";
                return null;
            }

            body = body.Substring(1, body.Length - 2).Trim();

            int space = indexOfWhitespace(body);
            string name = space < 0 ? body : body.Substring(0, space);

            if (name != "Bind")
            {
                error = $"Unknown markup extension '{name}'. Only {{Bind ...}} is supported";
                return null;
            }

            var result = new BindMarkup();
            string rest = space < 0 ? string.Empty : body.Substring(space).Trim();

            foreach (string part in splitTopLevel(rest))
            {
                string p = part.Trim();
                if (p.Length == 0)
                    continue;

                int eq = p.IndexOf('=');

                if (eq < 0)
                {
                    if (result.Path != null)
                    {
                        error = $"Binding has more than one path ('{result.Path}' and '{p}')";
                        return null;
                    }

                    result.Path = p;
                    continue;
                }

                string key = p.Substring(0, eq).Trim();
                string val = p.Substring(eq + 1).Trim();

                if (val.Length >= 2 && val[0] == '\'' && val[val.Length - 1] == '\'')
                    val = val.Substring(1, val.Length - 2);

                if (key == "Path")
                    result.Path = val;
                else
                    result.Options[key] = val;
            }

            if (string.IsNullOrEmpty(result.Path))
            {
                error = "Binding needs a path, e.g. {Bind Title}";
                return null;
            }

            return result;
        }

        private static int indexOfWhitespace(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsWhiteSpace(s[i]))
                    return i;
            }

            return -1;
        }

        private static IEnumerable<string> splitTopLevel(string s)
        {
            var current = new StringBuilder();
            bool quoted = false;

            foreach (char c in s)
            {
                if (c == '\'')
                    quoted = !quoted;

                if (c == ',' && !quoted)
                {
                    yield return current.ToString();

                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            yield return current.ToString();
        }
    }
}
