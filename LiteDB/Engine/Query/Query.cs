using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static LiteDB.Constants;

namespace LiteDB
{
    /// <summary>
    /// Represent full query options
    /// </summary>
    public partial class Query
    {
        public BsonExpression Select { get; set; } = BsonExpression.Root;

#if !NO_INCLUDE_QUERY
        public List<BsonExpression> Includes { get; } = new List<BsonExpression>();
#endif
#if !NO_WHERE_QUERY
        public List<BsonExpression> Where { get; } = new List<BsonExpression>();
#endif

#if !NO_ORDERBY_QUERY
        public BsonExpression OrderBy { get; set; } = null;
        public int Order { get; set; } = Query.Ascending;
#endif

#if !NO_GROUPBY_QUERY
        public BsonExpression GroupBy { get; set; } = null;
#endif
        public BsonExpression Having { get; set; } = null;

        public int Offset { get; set; } = 0;
        public int Limit { get; set; } = int.MaxValue;
        public bool ForUpdate { get; set; } = false;

        public string Into { get; set; }
        public BsonAutoId IntoAutoId { get; set; } = BsonAutoId.ObjectId;

        public bool ExplainPlan { get; set; }

        /// <summary>
        /// [ EXPLAIN ]
        ///    SELECT {selectExpr}
        ///    [ INTO {newcollection|$function} [ : {autoId} ] ]
        ///    [ FROM {collection|$function} ]
        /// [ INCLUDE {pathExpr0} [, {pathExprN} ]
        ///   [ WHERE {filterExpr} ]
        ///   [ GROUP BY {groupByExpr} ]
        ///  [ HAVING {filterExpr} ]
        ///   [ ORDER BY {orderByExpr} [ ASC | DESC ] ]
        ///   [ LIMIT {number} ]
        ///  [ OFFSET {number} ]
        ///     [ FOR UPDATE ]
        /// </summary>
        public string ToSQL(string collection)
        {
            var sb = new StringBuilder();

            if (this.ExplainPlan)
            {
                sb.AppendLine("EXPLAIN");
            }

            sb.AppendLine($"SELECT {this.Select.Source}");

            if (this.Into != null)
            {
                sb.AppendLine($"INTO {this.Into}:{IntoAutoId.ToString().ToLower()}");
            }

            sb.AppendLine($"FROM {collection}");

#if !NO_INCLUDE_QUERY
            if (this.Includes.Count > 0)
            {
                sb.AppendLine($"INCLUDE {string.Join(", ", this.Includes.Select(x => x.Source))}");
            }
#endif

#if !NO_WHERE_QUERY
            if (this.Where.Count > 0)
            {
                sb.AppendLine($"WHERE {string.Join(" AND ", this.Where.Select(x => x.Source))}");
            }
#endif

#if !NO_GROUPBY_QUERY
            if (this.GroupBy != null)
            {
                sb.AppendLine($"GROUP BY {this.GroupBy.Source}");
            }
#endif

            if (this.Having != null)
            {
                sb.AppendLine($"HAVING {this.Having.Source}");
            }

#if !NO_ORDERBY_QUERY
            if (this.OrderBy != null)
            {
                sb.AppendLine($"ORDER BY {this.OrderBy.Source} {(this.Order == Query.Ascending ? "ASC" : "DESC")}");
            }
#endif

            if (this.Limit != int.MaxValue)
            {
                sb.AppendLine($"LIMIT {this.Limit}");
            }

            if (this.Offset != 0)
            {
                sb.AppendLine($"OFFSET {this.Offset}");
            }

            if (this.ForUpdate)
            {
                sb.AppendLine($"FOR UPDATE");
            }

            return sb.ToString().Trim();
        }
    }
}
