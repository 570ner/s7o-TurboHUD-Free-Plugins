using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Turbo.Plugins.s7o
{
    public static class s7o_Localization
    {
        private const int MaxFileBytes = 524288;
        private const int MaxLines = 5000;
        private const int MaxKeyLength = 160;
        private const int MaxValueLength = 4096;
        private const int MaxDisplayCacheEntries = 2048;

        private static readonly object Sync = new object();
        private static readonly HashSet<string> SupportedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "deDE", "enUS", "esES", "esMX", "frFR", "itIT", "koKR",
            "plPL", "ptBR", "ptPT", "ruRU", "zhTW", "zhCN"
        };

        private static Dictionary<string, string> _resolved = EmptyDictionary();
        private static Dictionary<string, string> _selected = EmptyDictionary();
        private static Dictionary<string, string> _englishTextKeys = EmptyDictionary();
        private static Dictionary<string, string> _buttonTextKeys = EmptyDictionary();
        private static HashSet<string> _translatedTexts = new HashSet<string>(StringComparer.Ordinal);
        private static List<DisplayPattern> _displayPatterns = new List<DisplayPattern>();
        private static Dictionary<string, string> _displayCache = EmptyDictionary();
        private static bool _loaded;

        public static string LanguageCode { get; private set; } = "enUS";

        public static void Load()
        {
            if (_loaded)
                return;

            lock (Sync)
            {
                if (_loaded)
                    return;

                string root = AppDomain.CurrentDomain.BaseDirectory;
                string languageDirectory = Path.Combine(root, "plugins", "s7o", "settings", "languages");
                string languageCode = ReadLanguageCode(Path.Combine(root, "data", "selected_language.txt"));

                var english = new Dictionary<string, string>(StringComparer.Ordinal);
                MergeFile(english, Path.Combine(languageDirectory, "enUS.txt"));
                MergeFile(english, Path.Combine(languageDirectory, "enUS.override.txt"));

                var selected = new Dictionary<string, string>(StringComparer.Ordinal);
                if (string.Equals(languageCode, "enUS", StringComparison.OrdinalIgnoreCase))
                {
                    MergeDictionary(selected, english);
                }
                else
                {
                    MergeFile(selected, Path.Combine(languageDirectory, languageCode + ".txt"));
                    MergeFile(selected, Path.Combine(languageDirectory, languageCode + ".override.txt"));
                }

                var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
                MergeDictionary(resolved, english);
                MergeDictionary(resolved, selected);

                LanguageCode = languageCode;
                _resolved = resolved;
                _selected = selected;
                BuildDisplayIndexes(english, resolved);
                _loaded = true;
            }
        }

        public static string Get(string key, string englishFallback)
        {
            EnsureLoaded();

            string value;
            return !string.IsNullOrEmpty(key) && _resolved.TryGetValue(key, out value) && !string.IsNullOrEmpty(value)
                ? value
                : englishFallback;
        }

        public static string GetSelected(string key)
        {
            EnsureLoaded();

            string value;
            return !string.IsNullOrEmpty(key) && _selected.TryGetValue(key, out value) && !string.IsNullOrEmpty(value)
                ? value
                : null;
        }

        public static string Format(string key, string englishFallback, params object[] args)
        {
            string format = Get(key, englishFallback);
            if (format == null)
                return null;

            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args ?? new object[0]);
            }
            catch (FormatException)
            {
                try
                {
                    return englishFallback == null
                        ? null
                        : string.Format(CultureInfo.InvariantCulture, englishFallback, args ?? new object[0]);
                }
                catch
                {
                    return englishFallback;
                }
            }
        }

        public static string DisplayButton(string englishText)
        {
            if (string.IsNullOrEmpty(englishText))
                return englishText;

            EnsureLoaded();

            string key;
            string value;
            if (_buttonTextKeys.TryGetValue(englishText, out key) &&
                _resolved.TryGetValue(key, out value) &&
                !string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (_translatedTexts.Contains(englishText))
                return englishText;

            return Display(englishText);
        }

        public static string Display(string englishText)
        {
            if (string.IsNullOrEmpty(englishText))
                return englishText;

            EnsureLoaded();

            string key;
            string value;
            if (_englishTextKeys.TryGetValue(englishText, out key) &&
                _resolved.TryGetValue(key, out value) &&
                !string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (_translatedTexts.Contains(englishText))
                return englishText;

            lock (Sync)
            {
                if (_displayCache.TryGetValue(englishText, out value))
                    return value;
            }

            string translated = TranslateDisplayText(englishText);

            lock (Sync)
            {
                if (_displayCache.Count < MaxDisplayCacheEntries && !_displayCache.ContainsKey(englishText))
                    _displayCache[englishText] = translated;
            }

            return translated;
        }

        private static string TranslateDisplayText(string englishText)
        {
            string value;

            for (int i = 0; i < _displayPatterns.Count; i++)
            {
                string formatted;
                if (_displayPatterns[i].TryTranslate(englishText, out formatted))
                    return formatted;
            }

            return englishText;
        }

        private static void BuildDisplayIndexes(Dictionary<string, string> english, Dictionary<string, string> resolved)
        {
            var exact = new Dictionary<string, string>(StringComparer.Ordinal);
            var buttonExact = new Dictionary<string, string>(StringComparer.Ordinal);
            var translatedTexts = new HashSet<string>(StringComparer.Ordinal);
            var patterns = new List<DisplayPattern>();

            foreach (KeyValuePair<string, string> pair in english)
            {
                if (pair.Key.StartsWith("item.", StringComparison.Ordinal))
                    continue;

                string englishText = pair.Value;
                if (string.IsNullOrEmpty(englishText))
                    continue;

                string translated;
                if (!resolved.TryGetValue(pair.Key, out translated) || string.IsNullOrEmpty(translated))
                    translated = englishText;

                if (!string.Equals(englishText, translated, StringComparison.Ordinal))
                    translatedTexts.Add(translated);

                if (pair.Key.StartsWith("button.", StringComparison.Ordinal))
                {
                    if (!buttonExact.ContainsKey(englishText))
                        buttonExact.Add(englishText, pair.Key);
                    continue;
                }

                if (ContainsFormatPlaceholder(englishText))
                {
                    DisplayPattern pattern;
                    if (DisplayPattern.TryCreate(englishText, translated, out pattern))
                        patterns.Add(pattern);
                    continue;
                }

                string existingKey;
                if (!exact.TryGetValue(englishText, out existingKey) ||
                    DisplayKeyPriority(pair.Key) > DisplayKeyPriority(existingKey))
                {
                    exact[englishText] = pair.Key;
                }
            }

            patterns.Sort(delegate(DisplayPattern a, DisplayPattern b)
            {
                return b.EnglishLength.CompareTo(a.EnglishLength);
            });

            _englishTextKeys = exact;
            _buttonTextKeys = buttonExact;
            _translatedTexts = translatedTexts;
            _displayPatterns = patterns;
            _displayCache = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static int DisplayKeyPriority(string key)
        {
            if (key.StartsWith("overlay.", StringComparison.Ordinal))
                return 70;
            if (key.StartsWith("menu.", StringComparison.Ordinal))
                return 60;
            if (key.StartsWith("macro.", StringComparison.Ordinal))
                return 50;
            if (key.StartsWith("plugin.", StringComparison.Ordinal))
                return 40;
            if (key.StartsWith("common.", StringComparison.Ordinal))
                return 30;
            if (key.StartsWith("hud.ui.", StringComparison.Ordinal) ||
                key.StartsWith("hud.tooltip.", StringComparison.Ordinal))
                return 20;
            if (key.StartsWith("hud.", StringComparison.Ordinal))
                return 10;

            return 0;
        }

        private static bool ContainsFormatPlaceholder(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (int i = 0; i + 2 < value.Length; i++)
            {
                if (value[i] == '{' && char.IsDigit(value[i + 1]))
                    return true;
            }

            return false;
        }

        private static string ReadLanguageCode(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return "enUS";

                using (var reader = new StreamReader(path, new UTF8Encoding(false, true), true))
                {
                    int lineCount = 0;
                    string line;
                    while (lineCount++ < 64 && (line = reader.ReadLine()) != null)
                    {
                        line = line.Trim().Trim('\uFEFF');
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
                            continue;

                        return SupportedLanguages.Contains(line) ? CanonicalLanguageCode(line) : "enUS";
                    }
                }
            }
            catch
            {
                return "enUS";
            }

            return "enUS";
        }

        private static string CanonicalLanguageCode(string code)
        {
            foreach (string supported in SupportedLanguages)
                if (string.Equals(supported, code, StringComparison.OrdinalIgnoreCase))
                    return supported;

            return "enUS";
        }

        private static void MergeFile(Dictionary<string, string> target, string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaxFileBytes)
                    return;

                using (var reader = new StreamReader(path, new UTF8Encoding(false, true), true))
                {
                    int lineCount = 0;
                    string line;
                    while (lineCount++ < MaxLines && (line = reader.ReadLine()) != null)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
                            continue;

                        int equals = line.IndexOf('=');
                        if (equals <= 0)
                            continue;

                        string key = line.Substring(0, equals).Trim();
                        string value = line.Substring(equals + 1).Trim();
                        if (key.Length == 0 || key.Length > MaxKeyLength || value.Length == 0 || value.Length > MaxValueLength)
                            continue;

                        target[key] = value;
                    }
                }
            }
            catch
            {
            }
        }

        private static void MergeDictionary(Dictionary<string, string> target, Dictionary<string, string> source)
        {
            foreach (KeyValuePair<string, string> pair in source)
                target[pair.Key] = pair.Value;
        }

        private static Dictionary<string, string> EmptyDictionary()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static void EnsureLoaded()
        {
            if (!_loaded)
                Load();
        }

        private sealed class DisplayPattern
        {
            private readonly Regex _regex;
            private readonly string _translation;
            private readonly int[] _indexes;
            private readonly int _argumentCount;

            public int EnglishLength { get; private set; }

            private DisplayPattern(Regex regex, string translation, int[] indexes, int argumentCount, int englishLength)
            {
                _regex = regex;
                _translation = translation;
                _indexes = indexes;
                _argumentCount = argumentCount;
                EnglishLength = englishLength;
            }

            public static bool TryCreate(string english, string translation, out DisplayPattern pattern)
            {
                pattern = null;
                try
                {
                    var regex = new StringBuilder("^");
                    var indexes = new List<int>();
                    int maxIndex = -1;
                    int cursor = 0;
                    MatchCollection placeholders = Regex.Matches(english, "\\{(\\d+)(?:[^}]*)\\}");
                    for (int i = 0; i < placeholders.Count; i++)
                    {
                        Match placeholder = placeholders[i];
                        regex.Append(Regex.Escape(english.Substring(cursor, placeholder.Index - cursor)));
                        regex.Append("(.*?)");

                        int index;
                        if (!int.TryParse(placeholder.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out index))
                            return false;

                        indexes.Add(index);
                        if (index > maxIndex) maxIndex = index;
                        cursor = placeholder.Index + placeholder.Length;
                    }

                    regex.Append(Regex.Escape(english.Substring(cursor)));
                    regex.Append("$");
                    string safeTranslation = Regex.Replace(
                        translation ?? english,
                        "\\{(\\d+)(?:[^}]*)\\}",
                        "{$1}",
                        RegexOptions.CultureInvariant);
                    pattern = new DisplayPattern(new Regex(regex.ToString(), RegexOptions.CultureInvariant), safeTranslation, indexes.ToArray(), maxIndex + 1, english.Length);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            public bool TryTranslate(string text, out string translated)
            {
                translated = null;
                Match match = _regex.Match(text);
                if (!match.Success)
                    return false;

                var args = new object[_argumentCount];
                for (int i = 0; i < _indexes.Length; i++)
                    args[_indexes[i]] = Display(match.Groups[i + 1].Value);

                try
                {
                    translated = string.Format(CultureInfo.InvariantCulture, _translation, args);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

    }
}
