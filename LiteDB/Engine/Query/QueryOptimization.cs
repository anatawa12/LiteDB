﻿using System;
using System.Collections.Generic;
using System.Linq;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    /// <summary>
    /// Class that optimize query transforming user "Query" into "QueryPlan"
    /// </summary>
    internal class QueryOptimization
    {
        private readonly Snapshot _snapshot;
        private readonly Query _query;
#if !INVARIANT_CULTURE
        private readonly Collation _collation;
#endif
        private readonly QueryPlan _queryPlan;
#if !NO_WHERE_QUERY
        private readonly List<BsonExpression> _terms = new List<BsonExpression>();
#endif

#if INVARIANT_CULTURE
        public QueryOptimization(Snapshot snapshot, Query query, IEnumerable<BsonDocument> source)
#else
        public QueryOptimization(Snapshot snapshot, Query query, IEnumerable<BsonDocument> source, Collation collation)
#endif
        {
            if (query.Select == null) throw new ArgumentNullException(nameof(query.Select));

            _snapshot = snapshot;
            _query = query;
#if !INVARIANT_CULTURE
            _collation = collation;
#endif

            _queryPlan = new QueryPlan(snapshot.CollectionName)
            {
                // define index only if source are external collection
                Index = source != null ? new IndexVirtual(source) : null,
                Select = new Select(_query.Select, _query.Select.UseSource),
                ForUpdate = query.ForUpdate,
                Limit = query.Limit,
                Offset = query.Offset
            };
        }

        /// <summary>
        /// Build QueryPlan instance based on QueryBuilder fields
        /// - Load used fields in all expressions
        /// - Select best index option
        /// - Fill includes 
        /// - Define orderBy
        /// - Define groupBy
        /// </summary>
        public QueryPlan ProcessQuery()
        {
#if !NO_WHERE_QUERY
            // split where expressions into TERMs (splited by AND operator)
            this.SplitWherePredicateInTerms();

            // do terms optimizations
            this.OptimizeTerms();
#endif

            // define Fields
            this.DefineQueryFields();

            // define Index, IndexCost, IndexExpression, IsIndexKeyOnly + Where (filters - index)
            this.DefineIndex();

#if !NO_ORDERBY_OR_GROUPBY_QUERY
            // define OrderBy
            this.DefineOrderBy();

            // define GroupBy
            this.DefineGroupBy();
#endif

#if !NO_INCLUDE_QUERY && !NO_WHERE_QUERY
            // define IncludeBefore + IncludeAfter
            this.DefineIncludes();
#endif

            return _queryPlan;
        }

        #region Split Where

#if !NO_WHERE_QUERY
        /// <summary>
        /// Fill terms from where predicate list
        /// </summary>
        private void SplitWherePredicateInTerms()
        {
            void add(BsonExpression predicate)
            {
                // do not accept source * in WHERE
                if (predicate.UseSource)
                {
                    throw new LiteException(0, $"WHERE filter can not use `*` expression in `{predicate.Source}");
                }

                // add expression in where list breaking AND statments
                if (predicate.IsPredicate || predicate.Type == BsonExpressionType.Or)
                {
                    _terms.Add(predicate);
                }
                else if (predicate.Type == BsonExpressionType.And)
                {
                    var left = predicate.Left;
                    var right = predicate.Right;

                    add(left);
                    add(right);
                }
                else
                {
                    throw LiteException.InvalidExpressionTypePredicate(predicate);
                }
            }

            // check all where predicate for AND operators
            foreach(var predicate in _query.Where)
            {
                add(predicate);
            }
        }

        /// <summary>
        /// Do some pre-defined optimization on terms to convert expensive filter in indexable filter
        /// </summary>
        private void OptimizeTerms()
        {
            // simple optimization
            for (var i = 0; i < _terms.Count; i++)
            {
                var term = _terms[i];

                // convert: { [Enum] ANY = [Path] } to { [Path] IN ARRAY([Enum]) }
                // very used in LINQ expressions: `query.Where(x => ids.Contains(x.Id))`
                if (term.Left?.IsScalar == false &&
                    term.IsANY &&
                    term.Type == BsonExpressionType.Equal &&
                    term.Right?.Type == BsonExpressionType.Path)
                {
                    _terms[i] = BsonExpression.Create(term.Right.Source + " IN ARRAY(" + term.Left.Source + ")", term.Parameters);
                }
            }
        }
#endif

        #endregion

        #region Document Fields

        /// <summary>
        /// Load all fields that must be deserialize from document.
        /// </summary>
        private void DefineQueryFields()
        {
            // load only query fields (null return all document)
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // include all fields detected in all used expressions
            fields.AddRange(_query.Select.Fields);
#if !NO_WHERE_QUERY
            fields.AddRange(_terms.SelectMany(x => x.Fields));
#endif
#if !NO_INCLUDE_QUERY
            fields.AddRange(_query.Includes.SelectMany(x => x.Fields));
#endif
#if !NO_ORDERBY_OR_GROUPBY_QUERY
            fields.AddRange(_query.GroupBy?.Fields);
#endif
#if !NO_HAVING_QUERY
            fields.AddRange(_query.Having?.Fields);
#endif
#if !NO_ORDERBY_OR_GROUPBY_QUERY
            fields.AddRange(_query.OrderBy?.Fields);
#endif

            // if contains $, all fields must be deserialized
            if (fields.Contains("$"))
            {
                fields.Clear();
            }

            _queryPlan.Fields = fields;
        }

        #endregion

        #region Index Definition

        private void DefineIndex()
        {
            // selected expression to be used as index (from _terms)
            BsonExpression selected = null;

            // if index are not defined yet, get index
            if (_queryPlan.Index == null)
            {
                // try select best index (if return null, there is no good choice)
                var indexCost = this.ChooseIndex(_queryPlan.Fields);

                // if found an index, use-it
                if (indexCost != null)
                {
                    _queryPlan.Index = indexCost.Index;
                    _queryPlan.IndexCost = indexCost.Cost;
                    _queryPlan.IndexExpression = indexCost.IndexExpression;
                }
                else
                {
                    // if has no index to use, use full scan over _id
                    var pk = _snapshot.CollectionPage.PK;

                    _queryPlan.Index = new IndexAll("_id", Query.Ascending);
                    _queryPlan.IndexCost = _queryPlan.Index.GetCost(pk);
                    _queryPlan.IndexExpression = "$._id";
                }

                // get selected expression used as index
                selected = indexCost?.Expression;
            }
            else
            {
                ENSURE(_queryPlan.Index is IndexVirtual, "pre-defined index must be only for virtual collections");

                _queryPlan.IndexCost = 0;
            }

            // if is only 1 field to deserialize and this field are same as index, use IndexKeyOnly = rue
            if (_queryPlan.Fields.Count == 1 && _queryPlan.IndexExpression == "$." + _queryPlan.Fields.First())
            {
                // best choice - no need lookup for document (use only index)
                _queryPlan.IsIndexKeyOnly = true;
            }

            // fill filter using all expressions (remove selected term used in Index)
#if !NO_WHERE_QUERY
            _queryPlan.Filters.AddRange(_terms.Where(x => x != selected));
#endif
        }

        /// <summary>
        /// Try select index based on lowest cost or GroupBy/OrderBy reuse - use this priority order:
        /// - Get lowest index cost used in WHERE expressions (will filter data)
        /// - If there is no candidate, try get:
        ///     - Same of GroupBy
        ///     - Same of OrderBy
        ///     - Prefered single-field (when no lookup neeed)
        /// </summary>
        private IndexCost ChooseIndex(HashSet<string> fields)
        {
            var indexes = _snapshot.CollectionPage.GetCollectionIndexes().ToArray();

            // if query contains a single field used, give preferred if this index exists
            var preferred = fields.Count == 1 ? "$." + fields.First() : null;

            // otherwise, check for lowest index cost
            IndexCost lowest = null;

            // test all possible predicates in terms
#if !NO_WHERE_QUERY
            foreach (var expr in _terms.Where(x => x.IsPredicate))
            {
                ENSURE(expr.Left != null && expr.Right != null, "predicate expression must has left/right expressions");

                Tuple<CollectionIndex, BsonExpression> index = null;

                // check if expression is ANY
                if (expr.Left.IsScalar == false && expr.Right.IsScalar == true)
                {
                    // ANY expression support only LEFT (Enum) -> RIGHT (Scalar)
                    if (expr.IsANY)
                    {
                        index = indexes
                            .Where(x => x.Expression == expr.Left.Source && expr.Right.IsValue)
                            .Select(x => Tuple.Create(x, expr.Right))
                            .FirstOrDefault();
                    }
                    // ALL are not supported in index
                }
                else
                {
                    index = indexes
                        .Where(x => x.Expression == expr.Left.Source && expr.Right.IsValue)
                        .Select(x => Tuple.Create(x, expr.Right))
                        .Union(indexes
                            .Where(x => x.Expression == expr.Right.Source && expr.Left.IsValue)
                            .Select(x => Tuple.Create(x, expr.Left))
                        ).FirstOrDefault();
                }

                // get index that match with expression left/right side 

                if (index == null) continue;

                // calculate index score and store highest score
                var current = new IndexCost(index.Item1, expr, index.Item2, _collation);

                if (lowest == null || current.Cost < lowest.Cost)
                {
                    lowest = current;
                }
            }
#endif

            // if no index found, try use same index in orderby/groupby/preferred
            if (lowest == null && (
#if !NO_ORDERBY_OR_GROUPBY_QUERY
                    _query.OrderBy != null || 
                    _query.GroupBy != null || 
#endif
                    preferred != null))
            {
                var index =
#if !NO_ORDERBY_OR_GROUPBY_QUERY
                    indexes.FirstOrDefault(x => x.Expression == _query.GroupBy?.Source) ??
                    indexes.FirstOrDefault(x => x.Expression == _query.OrderBy?.Source) ??
#endif
                    indexes.FirstOrDefault(x => x.Expression == preferred);

                if (index != null)
                {
                    lowest = new IndexCost(index);
                }
            }

            return lowest;
        }

        #endregion

        #region OrderBy / GroupBy Definition

#if !NO_ORDERBY_OR_GROUPBY_QUERY
        /// <summary>
        /// Define OrderBy optimization (try re-use index)
        /// </summary>
        private void DefineOrderBy()
        {
            // if has no order by, returns null
            if (_query.OrderBy == null) return;

            var orderBy = new OrderBy(_query.OrderBy, _query.Order);

            // if index expression are same as orderBy, use index to sort - just update index order
            if (orderBy.Expression.Source == _queryPlan.IndexExpression)
            {
                // re-use index order and no not run OrderBy
                // update index order to be same as required in OrderBy
                _queryPlan.Index.Order = orderBy.Order;

                // in this case "query.OrderBy" will be null
                orderBy = null;
            }

            // otherwise, query.OrderBy will be setted according user defined
            _queryPlan.OrderBy = orderBy;
        }

        /// <summary>
        /// Define GroupBy optimization (try re-use index)
        /// </summary>
        private void DefineGroupBy()
        {
            if (_query.GroupBy == null) return;

            if (_query.OrderBy != null) throw new NotSupportedException("GROUP BY expression do not support ORDER BY");
#if !NO_INCLUDE_QUERY
            if (_query.Includes.Count > 0) throw new NotSupportedException("GROUP BY expression do not support INCLUDE");
#endif

#if NO_HAVING_QUERY
            var groupBy = new GroupBy(_query.GroupBy, _queryPlan.Select.Expression);
#else
            var groupBy = new GroupBy(_query.GroupBy, _queryPlan.Select.Expression, _query.Having);
#endif
            var orderBy = (OrderBy)null;

            // if groupBy use same expression in index, set group by order to MaxValue to not run
            if (groupBy.Expression.Source == _queryPlan.IndexExpression)
            {
                // great - group by expression are same used in index - no changes here
            }
            else
            {
                // create orderBy expression
                orderBy = new OrderBy(groupBy.Expression, Query.Ascending);
            }

            _queryPlan.GroupBy = groupBy;
            _queryPlan.OrderBy = orderBy;
        }
#endif

        #endregion

#if !NO_INCLUDE_QUERY && !NO_WHERE_QUERY
        /// <summary>
        /// Will define each include to be run BEFORE where (worst) OR AFTER where (best)
        /// </summary>
        private void DefineIncludes()
        {
            foreach(var include in _query.Includes)
            {
                // includes always has one single field
                var field = include.Fields.Single();

                // test if field are using in any filter or orderBy
                var used = _queryPlan.Filters.Any(x => x.Fields.Contains(field)) ||
                    (_queryPlan.OrderBy?.Expression.Fields.Contains(field) ?? false);

                if (used)
                {
                    _queryPlan.IncludeBefore.Add(include);
                }

                // in case of using OrderBy this can eliminate IncludeBefre - this need be added in After
                if (!used || _queryPlan.OrderBy != null)
                {
                    _queryPlan.IncludeAfter.Add(include);
                }
            }
        }
#endif
    }
}