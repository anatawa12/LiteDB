using LiteDB.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
#if !NO_LINQ_EXPRESSION
using System.Linq.Expressions;
#endif
using static LiteDB.Constants;

namespace LiteDB
{
    public partial class LiteCollection<T>
    {
        /// <summary>
        /// Return a new LiteQueryable to build more complex queries
        /// </summary>
        public ILiteQueryable<T> Query()
        {
#if NO_INCLUDE_QUERY
            return new LiteQueryable<T>(_engine, _mapper, _collection, new Query());
#else
            return new LiteQueryable<T>(_engine, _mapper, _collection, new Query()).Include(_includes);
#endif
        }

        #region Find

        /// <summary>
        /// Find documents inside a collection using predicate expression.
        /// </summary>
        public IEnumerable<T> Find(BsonExpression predicate, int skip = 0, int limit = int.MaxValue)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return this.Query()
#if !NO_INCLUDE_QUERY
                .Include(_includes)
#endif
                .Where(predicate)
                .Skip(skip)
                .Limit(limit)
                .ToEnumerable();
        }

        /// <summary>
        /// Find documents inside a collection using query definition.
        /// </summary>
        public IEnumerable<T> Find(Query query, int skip = 0, int limit = int.MaxValue)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            if (skip != 0) query.Offset = skip;
            if (limit != int.MaxValue) query.Limit = limit;

            return new LiteQueryable<T>(_engine, _mapper, _collection, query)
                .ToEnumerable();
        }

#if !NO_LINQ_EXPRESSION
        /// <summary>
        /// Find documents inside a collection using predicate expression.
        /// </summary>
        public IEnumerable<T> Find(Expression<Func<T, bool>> predicate, int skip = 0, int limit = int.MaxValue) => this.Find(_mapper.GetExpression(predicate), skip, limit);
#endif

        #endregion

        #region FindById + One + All

        /// <summary>
        /// Find a document using Document Id. Returns null if not found.
        /// </summary>
        public T FindById(BsonValue id)
        {
            if (id == null || id.IsNull) throw new ArgumentNullException(nameof(id));

            return this.Find(BsonExpression.Create("_id = @0", id)).FirstOrDefault();
        }

        /// <summary>
        /// Find the first document using predicate expression. Returns null if not found
        /// </summary>
        public T FindOne(BsonExpression predicate) => this.Find(predicate).FirstOrDefault();

        /// <summary>
        /// Find the first document using predicate expression. Returns null if not found
        /// </summary>
        public T FindOne(string predicate, BsonDocument parameters) => this.FindOne(BsonExpression.Create(predicate, parameters));

        /// <summary>
        /// Find the first document using predicate expression. Returns null if not found
        /// </summary>
        public T FindOne(BsonExpression predicate, params BsonValue[] args) => this.FindOne(BsonExpression.Create(predicate, args));

#if !NO_LINQ_EXPRESSION
        /// <summary>
        /// Find the first document using predicate expression. Returns null if not found
        /// </summary>
        public T FindOne(Expression<Func<T, bool>> predicate) => this.FindOne(_mapper.GetExpression(predicate));
#endif

        /// <summary>
        /// Find the first document using defined query structure. Returns null if not found
        /// </summary>
        public T FindOne(Query query) => this.Find(query).FirstOrDefault();

        /// <summary>
        /// Returns all documents inside collection order by _id index.
        /// </summary>
#if NO_INCLUDE_QUERY
        public IEnumerable<T> FindAll() => this.Query().ToEnumerable();
#else
        public IEnumerable<T> FindAll() => this.Query().Include(_includes).ToEnumerable();
#endif

        #endregion
    }
}