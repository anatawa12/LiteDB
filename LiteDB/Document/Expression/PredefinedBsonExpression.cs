using System.Collections.Generic;
using System.Linq;

namespace LiteDB
{
    // litedb for vrc-get uses nativeaot so I want to avoid using the expression tree to build the query.

    public partial class BsonExpression
    {
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
        private static BsonValue P(BsonValue value, string name) => BsonExprInterpreter.MEMBER_PATH(value, name);

        private static BsonValue D(string key, BsonValue value) =>
            new BsonDocument { [key] = value };

        // "{ i: _id }" is "{i:$._id}"
        internal static BsonExpression InitializeIAsId = new BsonExpression()
        {
            _funcScalar = (_1, root, _2) => D("i", P(root, "_id")),
            Source = "{i:$._id}",
            Fields = new HashSet<string> { "_id" },
            UseSource = false,
            Type = BsonExpressionType.Document,

            IsScalar = true,
            IsImmutable = true,
        };

        // "{ count: COUNT(*._id) }" is compiled to "{count:COUNT(MAP(*=>@._id))}"
        internal static BsonExpression Count = new BsonExpression()
        {
            _funcScalar = (source, _1, _2) => D("count", source.Count()),
            Source = "{count:COUNT(MAP(*=>@._id))}",
            Fields = new HashSet<string> { "_id" },
            UseSource = true,
            Type = BsonExpressionType.Document,

            IsScalar = true,
            IsImmutable = true,
        };

        // "{ exists: ANY(*._id) }" is compiled to "{exists:ANY(MAP(*=>@._id))}"
        internal static BsonExpression Exists = new BsonExpression()
        {
            _funcScalar = (source, _1, _2) => D("exists", source.Any()),
            Source = "{exists:ANY(MAP(*=>@._id))}",
            Fields = new HashSet<string> { "_id" },
            UseSource = true,
            Type = BsonExpressionType.Document,

            IsScalar = true,
            IsImmutable = true,
        };

        // DOCUMENT_INIT(new [] {"exists"}, new [] {ANY(MAP(root, collation, parameters, source, `@._id` [Path]))})
#else
        internal static BsonExpression InitializeIAsId = "{ i: _id }";
        internal static BsonExpression Count = "{ count: COUNT(*._id) }";
        internal static BsonExpression Any = "{ exists: ANY(*._id) }";
#endif
    }
}
