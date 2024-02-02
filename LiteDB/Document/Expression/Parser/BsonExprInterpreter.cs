#if EXPRESSION_PARSER_ONLY_FOR_INDEX
using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB
{
    internal class BsonExprInterpreter
    {
        public static BsonValue MEMBER_PATH(BsonValue value, string name)
        {
            // if value is null is because there is no "current document", only "all documents"
            // SELECT COUNT(*), $.pageID FROM $page_list IS invalid!
            if (value == null)
            {
                throw new LiteException(0,
                    $"Field '{name}' is invalid in the select list because it is not contained in either an aggregate function or the GROUP BY clause.");
            }

            if (string.IsNullOrEmpty(name))
            {
                return value;
            }
            else if (value.IsDocument)
            {
                var doc = value.AsDocument;

                if (doc.TryGetValue(name, out BsonValue item))
                {
                    return item;
                }
            }

            return BsonValue.Null;
        }

        public static BsonValue DOCUMENT_INIT(string[] keys, BsonValue[] values)
        {
            // test for special JsonEx data types (date, numberLong, ...)
            if (keys.Length == 1 && keys[0][0] == '$' && values[0].IsString)
            {
                var value = values[0];
                switch (keys[0])
                {
                    case "$binary":
                    {
                        try
                        {
                            return Convert.FromBase64String(value.AsString);
                        }
                        catch (FormatException)
                        {
                            return BsonValue.Null;
                        }
                    }
                    case "$oid":
                    {
                        try
                        {
                            return new ObjectId(value.AsString);
                        }
                        catch
                        {
                            return BsonValue.Null;
                        }
                    }
                    case "$guid":
                    {
                        try
                        {
                            return new Guid(value.AsString);
                        }
                        catch
                        {
                            return BsonValue.Null;
                        }

                    }
                    case "$date":
                    {
                        if (DateTime.TryParse(value.AsString, CultureInfo.InvariantCulture.DateTimeFormat,
                                DateTimeStyles.None, out var val))
                            return val;
                        return BsonValue.Null;
                    }
                    case "$numberLong":
                    {
                        if (Int64.TryParse(value.AsString, out var val))
                            return val;
                        return BsonValue.Null;
                    }
                    case "$numberDecimal":
                    {
                        if (Decimal.TryParse(value.AsString, NumberStyles.Any,
                                CultureInfo.InvariantCulture.NumberFormat, out var val))
                            return val;

                        return BsonValue.Null;
                    }
                    case "$minValue": return BsonValue.MinValue;
                    case "$maxValue": return BsonValue.MaxValue;
                }
            }

            var doc = new BsonDocument();

            for (var i = 0; i < keys.Length; i++)
            {
                doc[keys[i]] = values[i];
            }

            return doc;
        }

        /// <summary>
        /// Returns a single value from array according index or expression parameter
        /// </summary>
        public static BsonValue ARRAY_CONST_INDEX(BsonValue value, int index)
        {
            if (!value.IsArray) return BsonValue.Null;
            var arr = value.AsArray;
            var idx = index < 0 ? arr.Count + index : index;
            return arr.Count > idx ? arr[idx] : BsonValue.Null;
        }

        /// <summary>
        /// Returns all values from array according filter expression or all values (index = MaxValue)
        /// </summary>
        public static IEnumerable<BsonValue> ARRAY_ALL(BsonValue value)
        {
            if (!value.IsArray) yield break;

            var arr = value.AsArray;

            // [*] - index are all values
            foreach (var item in arr)
            {
                yield return item;
            }
        }

        /// <summary>
        /// Returns all values from array according filter expression or all values (index = MaxValue)
        /// </summary>
        public static IEnumerable<BsonValue> ARRAY_FILTER(BsonValue value, BsonExpression filterExpr, BsonDocument root)
        {
            if (!value.IsArray) yield break;

            var arr = value.AsArray;

            // [*] - index are all values
            foreach (var item in arr)
            {
                // execute for each child value and except a first bool value (returns if true)
#if INVARIANT_CULTURE
                var c = filterExpr.ExecuteScalar(new [] { root }, root, item);
#else
                var c = filterExpr.ExecuteScalar(new BsonDocument[] { root }, root, item, null);
#endif

                if (c.IsBoolean && c.AsBoolean == true)
                {
                    yield return item;
                }
            }
        }

        public static BsonValue EXTEND(BsonDocument source, BsonDocument extend)
        {
            // make a copy of source document
            var newDoc = new BsonDocument();

            source.AsDocument.CopyTo(newDoc);
            extend.AsDocument.CopyTo(newDoc);

            // copy rawId from source
            newDoc.RawId = source.AsDocument.RawId;

            return newDoc;
        }

        public static IEnumerable<BsonValue> MAP(BsonDocument root, IEnumerable<BsonValue> input, BsonExpression mapExpr)
        {
            foreach (var item in input)
            {
                // execute for each child value and except a first bool value (returns if true)
#if INVARIANT_CULTURE
                var values = mapExpr.Execute(new BsonDocument[] { root }, root, item);
#else
                var values = mapExpr.Execute(new BsonDocument[] { root }, root, item, null);
#endif

                foreach (var value in values)
                {
                    yield return value;
                }
            }
        }
    }
}
#endif
