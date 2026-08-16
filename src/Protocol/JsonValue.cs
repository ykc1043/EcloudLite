using System;
using System.Collections;
using System.Collections.Generic;

namespace EcloudLite.Protocol
{
    internal static class JsonValue
    {
        public static Dictionary<string, object> AsDictionary(object value)
        {
            Dictionary<string, object> dictionary = value as Dictionary<string, object>;
            return dictionary ?? new Dictionary<string, object>();
        }

        public static string String(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary != null && dictionary.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value);
            return string.Empty;
        }

        public static bool Bool(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary == null || !dictionary.TryGetValue(key, out value) || value == null) return false;
            bool parsed;
            if (bool.TryParse(Convert.ToString(value), out parsed)) return parsed;
            int number;
            return int.TryParse(Convert.ToString(value), out number) && number != 0;
        }

        public static Dictionary<string, object> Dictionary(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary != null && dictionary.TryGetValue(key, out value))
                return AsDictionary(value);
            return new Dictionary<string, object>();
        }

        public static IEnumerable<object> Array(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary != null && dictionary.TryGetValue(key, out value) && value is IEnumerable && !(value is string))
            {
                foreach (object item in (IEnumerable)value) yield return item;
            }
        }

        public static string KeyList(Dictionary<string, object> dictionary)
        {
            if (dictionary == null) return "<null>";
            List<string> keys = new List<string>(dictionary.Keys);
            keys.Sort(StringComparer.Ordinal);
            return string.Join(",", keys.ToArray());
        }

        public static string Shape(object value)
        {
            return Shape(value, 0);
        }

        private static string Shape(object value, int depth)
        {
            if (value == null) return "null";
            if (depth > 3) return value.GetType().Name;

            Dictionary<string, object> dictionary = value as Dictionary<string, object>;
            if (dictionary != null)
            {
                List<string> keys = new List<string>(dictionary.Keys);
                keys.Sort(StringComparer.Ordinal);
                List<string> fields = new List<string>();
                for (int i = 0; i < keys.Count && i < 40; i++)
                {
                    string key = keys[i];
                    fields.Add(key + ":" + Shape(dictionary[key], depth + 1));
                }
                if (keys.Count > 40) fields.Add("...+" + (keys.Count - 40));
                return "{" + string.Join(",", fields.ToArray()) + "}";
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string))
            {
                List<object> items = new List<object>();
                foreach (object item in enumerable) items.Add(item);
                return "array[" + items.Count + "]" + (items.Count == 0 ? string.Empty : "<" + Shape(items[0], depth + 1) + ">");
            }

            string text = value as string;
            if (text != null) return "string(len=" + text.Length + ")";
            if (value is bool) return "bool";
            if (value is int || value is long || value is decimal || value is double || value is float) return "number";
            return value.GetType().Name;
        }
    }
}
