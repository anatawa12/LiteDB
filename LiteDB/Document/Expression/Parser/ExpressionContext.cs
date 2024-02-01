using LiteDB.Engine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using static LiteDB.Constants;

namespace LiteDB
{
    internal class ExpressionContext
    {
        public ExpressionContext()
        {
            this.Source = Expression.Parameter(typeof(IEnumerable<BsonDocument>), "source");
            this.Root = Expression.Parameter(typeof(BsonDocument), "root");
            this.Current = Expression.Parameter(typeof(BsonValue), "current");
            this.Collation = Expression.Parameter(typeof(Collation), "collation");
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
            this.ParametersUnused = Expression.Parameter(typeof(BsonDocument), "parameters");
#else
            this.Parameters = Expression.Parameter(typeof(BsonDocument), "parameters");
#endif
        }

        public ParameterExpression Source { get; }
        public ParameterExpression Root { get; }
        public ParameterExpression Current { get; }
        public ParameterExpression Collation { get; }
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
        public ParameterExpression ParametersUnused { get; }
#else
        public ParameterExpression Parameters { get; }
#endif
    }
}
