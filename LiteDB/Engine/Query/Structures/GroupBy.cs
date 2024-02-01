#if !NO_ORDERBY_OR_GROUPBY_QUERY
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    /// <summary>
    /// Represent an GroupBy definition (is based on OrderByDefinition)
    /// </summary>
    internal class GroupBy
    {
        public BsonExpression Expression { get; }

        public BsonExpression Select { get; }

#if !NO_HAVING_QUERY
        public BsonExpression Having { get; }
#endif

#if NO_HAVING_QUERY
        public GroupBy(BsonExpression expression, BsonExpression select)
#else
        public GroupBy(BsonExpression expression, BsonExpression select, BsonExpression having)
#endif
        {
            this.Expression = expression;
            this.Select = select;
#if !NO_HAVING_QUERY
            this.Having = having;
#endif
        }
    }
}
#endif
