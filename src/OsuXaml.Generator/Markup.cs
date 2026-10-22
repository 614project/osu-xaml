using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OsuXaml.Generator
{
    /// <summary>
    /// A parsed markup extension: <c>{Bind Path, Key=Value}</c> (or WPF's <c>{Binding}</c>), <c>{StaticResource Key}</c>,
    /// <c>{ThemeResource Key}</c>, <c>{x:Static Type.Member}</c>, <c>{TemplateBinding Property}</c>, <c>{x:Null}</c> or <c>{x:Type Type}</c>.
    /// Values may be single-quoted to contain commas, e.g. <c>StringFormat='{0} left, {1}'</c>.
    /// </summary>
    internal sealed class Markup
    {
        public const string Bind = "Bind";
        public const string StaticResource = "StaticResource";
        public const string ThemeResource = "ThemeResource";
        public const string Static = "Static";
        public const string TemplateBinding = "TemplateBinding";
        public const string Null = "Null";
        public const string Type = "Type";

        private static readonly string[] bind_options = { "Mode", "Converter", "StringFormat", "Animate", "Easing", "Source", "ElementName", "UpdateSourceTrigger" };

        public string Extension = Bind;
        public string Path = string.Empty;
        public readonly Dictionary<string, string> Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Get(string key) => Options.TryGetValue(key, out string v) ? v : null;

        public bool IsBinding => Extension == Bind || Extension == TemplateBinding;

        public static bool IsMarkup(string value) => value.StartsWith("{", StringComparison.Ordinal) && !value.StartsWith("{}", StringComparison.Ordinal);

        /// <summary>Strips the <c>{}</c> escape that lets a literal start with a brace.</summary>
        public static string Unescape(string value) => value.StartsWith("{}", StringComparison.Ordinal) ? value.Substring(2) : value;

        /// <summary>A plain binding to <paramref name="path"/>, as <c>DisplayMemberPath="Name"</c> means.</summary>
        public static Markup ForPath(string path) => new Markup { Path = path ?? string.Empty };

        public static Markup Parse(string value, out string error)
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
            int colon = name.IndexOf(':');
            string local = colon < 0 ? name : name.Substring(colon + 1);

            var result = new Markup();

            switch (local)
            {
                case "Bind":
                case "Binding":
                    result.Extension = Bind;
                    break;

                case StaticResource:
                case "DynamicResource":
                    result.Extension = StaticResource;
                    break;

                case ThemeResource:
                    result.Extension = ThemeResource;
                    break;

                case Static:
                case Null:
                case Type:
                    result.Extension = local;
                    break;

                case TemplateBinding:
                    result.Extension = TemplateBinding;
                    break;

                default:
                    error = $"Unknown markup extension '{name}'. Supported: {{Bind}}, {{Binding}}, {{StaticResource}}, {{ThemeResource}}, {{x:Static}}, {{TemplateBinding}}, {{x:Null}}";
                    return null;
            }

            string rest = space < 0 ? string.Empty : body.Substring(space).Trim();
            bool hasPath = false;

            foreach (string part in splitTopLevel(rest))
            {
                string p = part.Trim();
                if (p.Length == 0)
                    continue;

                int eq = p.IndexOf('=');

                if (eq < 0)
                {
                    if (hasPath)
                    {
                        error = $"Markup extension has more than one value ('{result.Path}' and '{p}')";
                        return null;
                    }

                    result.Path = p;
                    hasPath = true;
                    continue;
                }

                string key = p.Substring(0, eq).Trim();
                string val = p.Substring(eq + 1).Trim();

                if (val.Length >= 2 && val[0] == '\'' && val[val.Length - 1] == '\'')
                    val = val.Substring(1, val.Length - 2);

                if (key == "Path" || key == "ResourceKey" || key == "Member" || key == "Property" || key == "TypeName")
                {
                    result.Path = val;
                    hasPath = true;
                }
                else if (result.Extension == Bind && !bind_options.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    error = $"Unknown binding option '{key}'. Supported: Path, {string.Join(", ", bind_options.Take(bind_options.Length - 1))}";
                    return null;
                }
                else
                    result.Options[key] = val;
            }

            if ((result.Extension == StaticResource || result.Extension == ThemeResource || result.Extension == Static || result.Extension == Type
                 || result.Extension == TemplateBinding) && result.Path.Length == 0)
            {
                error = $"{{{local}}} needs a value";
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
