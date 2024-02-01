using System;
using System.Collections.Generic;
using System.Linq;
#if !NO_LINQ_EXPRESSION
using System.Linq.Expressions;
#endif

namespace LiteDB
{
    public interface ILiteQueryable<T> : ILiteQueryableResult<T>
    {
        ILiteQueryable<T> Include(BsonExpression path);
        ILiteQueryable<T> Include(List<BsonExpression> paths);
#if !NO_LINQ_EXPRESSION
        ILiteQueryable<T> Include<K>(Expression<Func<T, K>> path);
#endif

        ILiteQueryable<T> Where(BsonExpression predicate);
        ILiteQueryable<T> Where(string predicate, BsonDocument parameters);
        ILiteQueryable<T> Where(string predicate, params BsonValue[] args);
#if !NO_LINQ_EXPRESSION
        ILiteQueryable<T> Where(Expression<Func<T, bool>> predicate);
#endif

        ILiteQueryable<T> OrderBy(BsonExpression keySelector, int order = 1);
#if !NO_LINQ_EXPRESSION
        ILiteQueryable<T> OrderBy<K>(Expression<Func<T, K>> keySelector, int order = 1);
#endif
        ILiteQueryable<T> OrderByDescending(BsonExpression keySelector);
#if !NO_LINQ_EXPRESSION
        ILiteQueryable<T> OrderByDescending<K>(Expression<Func<T, K>> keySelector);
#endif

        ILiteQueryable<T> GroupBy(BsonExpression keySelector);
        ILiteQueryable<T> Having(BsonExpression predicate);

        ILiteQueryableResult<BsonDocument> Select(BsonExpression selector);
#if !NO_LINQ_EXPRESSION
        ILiteQueryableResult<K> Select<K>(Expression<Func<T, K>> selector);
#endif
    }

    public interface ILiteQueryableResult<T>
    {
        ILiteQueryableResult<T> Limit(int limit);
        ILiteQueryableResult<T> Skip(int offset);
        ILiteQueryableResult<T> Offset(int offset);
        ILiteQueryableResult<T> ForUpdate();

        BsonDocument GetPlan();
        IBsonDataReader ExecuteReader();
        IEnumerable<BsonDocument> ToDocuments();
        IEnumerable<T> ToEnumerable();
        List<T> ToList();
        T[] ToArray();

        int Into(string newCollection, BsonAutoId autoId = BsonAutoId.ObjectId);

        T First();
        T FirstOrDefault();
        T Single();
        T SingleOrDefault();

        int Count();
        long LongCount();
        bool Exists();
    }
}