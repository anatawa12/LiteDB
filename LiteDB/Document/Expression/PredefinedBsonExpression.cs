#if false
namespace LiteDB
{
    // litedb for vrc-get uses nativeaot so I want to avoid using the expression tree to build the query.
    using static BsonExpressionOperators;
    using static BsonExpressionFunctions;
    using static BsonExpressionMethods;

    public partial class BsonExpression
    {
#if NO_EXPRESSION_PARSER
        // "_id" is "$._id"
        internal static BsonExpression IdExpression = ScalarImmutable("$._id",
            (_1, root, _2, _3, _4) =>
                MEMBER_PATH(root, "_id"));

        // "{ i: _id }" is "{i:$._id}"
        internal static BsonExpression InitializeIAsId = ScalarImmutable("{i:$._id}",
            (_1, root, _2, _3, _4) =>
                DOCUMENT_INIT(new[]{"i"}, new[] { MEMBER_PATH(root, "_id")}));

        // "@._id"
        internal static BsonExpression AtId = ScalarImmutable("@._id",
            (_1, _2, current, _3, _4) =>
                DOCUMENT_INIT(new[]{"i"}, new[] { MEMBER_PATH(current, "_id")}));

        // "{ count: COUNT(*._id) }" is compiled to "{count:COUNT(MAP(*=>@._id))}"
        internal static BsonExpression Count = ScalarImmutable("{count:COUNT(MAP(*=>@._id))}",
            (source, root, _2, collation, parameters) =>
                DOCUMENT_INIT(new [] {"count"}, new [] {COUNT(MAP(root, collation, parameters, source, AtId))}));

        // "{ exists: ANY(*._id) }" is compiled to "{exists:ANY(MAP(*=>@._id))}"
        internal static BsonExpression Any = ScalarImmutable("{exists:ANY(MAP(*=>@._id))}",
            (source, root, _2, collation, parameters) =>
                DOCUMENT_INIT(new [] {"exists"}, new [] {ANY(MAP(root, collation, parameters, source, AtId))}));

        // DOCUMENT_INIT(new [] {"exists"}, new [] {ANY(MAP(root, collation, parameters, source, `@._id` [Path]))})
#else
        internal static BsonExpression IdExpression = "_id";
        internal static BsonExpression InitializeIAsId = "{ i: _id }";
        internal static BsonExpression Count = "{ count: COUNT(*._id) }";
        internal static BsonExpression Any = "{ exists: ANY(*._id) }";
#endif
    }
}
#endif
