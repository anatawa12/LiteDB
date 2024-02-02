using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
#if !NO_REGEX
using System.Text.RegularExpressions;
#endif
using static LiteDB.Constants;

namespace LiteDB
{
    internal static class DictionaryExtensions
    {
        public static T GetOrDefault<K, T>(this IDictionary<K, T> dict, K key, T defaultValue = default(T))
        {
            if (dict.TryGetValue(key, out T result))
            {
                return result;
            }

            return defaultValue;
        }

        public static T GetOrAdd<K, T>(this IDictionary<K, T> dict, K key, Func<K, T> valueFactoy)
        {
            if (dict.TryGetValue(key, out var value) == false)
            {
                value = valueFactoy(key);

                dict.Add(key, value);
            }

            return value;
        }

        public static void ParseKeyValue(this IDictionary<string, string> dict, string connectionString)
        {
            var position = 0;

            while(position < connectionString.Length)
            {
                EatWhitespace();
                var key = ReadKey();

                EatWhitespace();
                var value = ReadValue();

                dict[key] = value;
            }

            string ReadKey()
            {
                var sb = new StringBuilder();

                while (position < connectionString.Length)
                {
                    var current = connectionString[position];

                    if (current == '=')
                    {
                        position++;
                        return sb.ToString().Trim();
                    }

                    sb.Append(current);
                    position++;
                }

                return sb.ToString().Trim();
            }

            string ReadValue()
            {
                var sb = new StringBuilder();
                var quote =
                    connectionString[position] == '"' ? '"' :
                    connectionString[position] == '\'' ? '\'' : ' ';

                if (quote != ' ') position++;

                while (position < connectionString.Length)
                {
                    var current = connectionString[position];

                    if (quote == ' ')
                    {
                        if (current == ';')
                        {
                            position++;
                            return sb.ToString().Trim();
                        }
                    }
                    else if (quote != ' ' && current == quote)
                    {
                        if (connectionString[position - 1] == '\\')
                        {
                            sb.Length--;
                        }
                        else
                        {
                            position++;

                            EatWhitespace();

                            if (position < connectionString.Length && connectionString[position] == ';') position++;

                            return sb.ToString();
                        }
                    }

                    sb.Append(current);
                    position++;
                }

                return sb.ToString().Trim();
            }

            void EatWhitespace()
            {
                while (position < connectionString.Length)
                {
                    if(connectionString[position] == ' ' ||
                        connectionString[position] == '\t' ||
                        connectionString[position] == '\f')
                    {
                        position++;
                        continue;
                    }
                    break;
                }
            }
        }

#if NO_REFLECTION_MORE // try to minimize reflection
        public static string 
            GetValue(this Dictionary<string, string> dict, string key, string defaultValue = default) =>
            dict.GetValueOrDefault(key, defaultValue);

        public static TimeSpan GetValue(this Dictionary<string, string> dict, string key, TimeSpan defaultValue = default)
        {
            try
            {
                if (dict.TryGetValue(key, out var value) == false) return defaultValue;

                {
                    // if timespan are numbers only, convert as seconds
#if NO_REGEX
                    var isAsciiDigit = value.Length > 0;
                    if (isAsciiDigit)
                        foreach (var c in value)
                            if (c < '0' || c > '9')
                                isAsciiDigit = false;
                    if (isAsciiDigit)
#else
                    if (Regex.IsMatch(value, @"^\d+$", RegexOptions.Compiled))
#endif
                    {
                        return TimeSpan.FromSeconds(Convert.ToInt32(value));
                    }
                    else
                    {
                        return TimeSpan.Parse(value);
                    }
                }
            }
            catch (Exception)
            {
                //TODO: fix string connection parser
                throw new LiteException(0, $"Invalid connection string value type for `{key}`");
            }
        }
        
        public static bool GetValue(this Dictionary<string, string> dict, string key, bool defaultValue = default)
        {
            try
            {
                return dict.TryGetValue(key, out var value) == false ? defaultValue : Convert.ToBoolean(value);
            }
            catch (Exception)
            {
                //TODO: fix string connection parser
                throw new LiteException(0, $"Invalid connection string value type for `{key}`");
            }
        }

        public static ConnectionType GetValue(this Dictionary<string, string> dict, string key, ConnectionType defaultValue = default)
        {
            try
            {
                return dict.TryGetValue(key, out var value) == false
                    ? defaultValue
                    : Enum.Parse<ConnectionType>(value, true);
            }
            catch (Exception)
            {
                //TODO: fix string connection parser
                throw new LiteException(0, $"Invalid connection string value type for `{key}`");
            }
        }
#else
        /// <summary>
        /// Get value from dictionary converting datatype T
        /// </summary>
        public static T GetValue<T>(this Dictionary<string, string> dict, string key, T defaultValue = default(T))
        {
            try
            {
                if (dict.TryGetValue(key, out var value) == false) return defaultValue;

                if (typeof(T) == typeof(TimeSpan))
                {
                    // if timespan are numbers only, convert as seconds
#if NO_REGEX
                    var isAsciiDigit = value.Length > 0;
                    if (isAsciiDigit)
                        foreach (var c in value)
                            if (c < '0' || c > '9')
                                isAsciiDigit = false;
                    if (isAsciiDigit)
#else
                    if (Regex.IsMatch(value, @"^\d+$", RegexOptions.Compiled))
#endif
                    {
                        return (T)(object)TimeSpan.FromSeconds(Convert.ToInt32(value));
                    }
                    else
                    {
                        return (T)(object)TimeSpan.Parse(value);
                    }
                }
                else if (typeof(T).GetTypeInfo().IsEnum)
                {
                    return (T)Enum.Parse(typeof(T), value, true);
                }
                else
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
            }
            catch (Exception)
            {
                //TODO: fix string connection parser
                throw new LiteException(0, $"Invalid connection string value type for `{key}`");
            }
        }
#endif

        /// <summary>
        /// Get a value from a key converted in file size format: "1gb", "10 mb", "80000"
        /// </summary>
        public static long GetFileSize(this Dictionary<string, string> dict, string key, long defaultValue)
        {
#if NO_REFLECTION_MORE
            var size = dict.GetValue(key, null);
#else
            var size = dict.GetValue<string>(key, null);
#endif

            if (size == null) return defaultValue;

#if NO_REGEX
            var index = 0;
            while (index < size.Length && '0' <= size[index] && size[index] <= '9') index++;
            var num = Convert.ToInt64(size.Substring(0, index));
            var unit = size.Substring(index).TrimStart().ToLower();
            if (unit.EndsWith("b")) unit = unit.Substring(0, unit.Length - 1);
            else if (unit.EndsWith("byte")) unit = unit.Substring(0, unit.Length - 4);
            else if (unit.EndsWith("bytes")) unit = unit.Substring(0, unit.Length - 5);

            switch (unit)
            {
                case "t": return num * 1024L * 1024L * 1024L * 1024L;
                case "g": return num * 1024L * 1024L * 1024L;
                case "m": return num * 1024L * 1024L;
                case "k": return num * 1024L;
                case "": return num;
                default: return 0;
            }
#else
            var match = Regex.Match(size, @"^(\d+)\s*([tgmk])?(b|byte|bytes)?$", RegexOptions.IgnoreCase);

            if (!match.Success) return 0;

            var num = Convert.ToInt64(match.Groups[1].Value);

            switch (match.Groups[2].Value.ToLower())
            {
                case "t": return num * 1024L * 1024L * 1024L * 1024L;
                case "g": return num * 1024L * 1024L * 1024L;
                case "m": return num * 1024L * 1024L;
                case "k": return num * 1024L;
                case "": return num;
            }

            return 0;
#endif
        }
    }
}