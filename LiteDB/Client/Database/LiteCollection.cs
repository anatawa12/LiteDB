using LiteDB.Engine;
using System;
using System.Collections.Generic;
using static LiteDB.Constants;

namespace LiteDB
{
    public sealed partial class LiteCollection<T> : ILiteCollection<T>
    {
        private readonly string _collection;
        private readonly ILiteEngine _engine;
#if !NO_INCLUDE_QUERY
        private readonly List<BsonExpression> _includes;
#endif
        private readonly BsonMapper _mapper;
#if !NO_ENTITY_MAPPER
        private readonly EntityMapper _entity;
        private readonly MemberMapper _id;
#endif
        private readonly BsonAutoId _autoId;

        /// <summary>
        /// Get collection name
        /// </summary>
        public string Name => _collection;

        /// <summary>
        /// Get collection auto id type
        /// </summary>
        public BsonAutoId AutoId => _autoId;

#if !NO_ENTITY_MAPPER
        /// <summary>
        /// Getting entity mapper from current collection. Returns null if collection are BsonDocument type
        /// </summary>
        public EntityMapper EntityMapper => _entity;
#endif

        internal LiteCollection(string name, BsonAutoId autoId, ILiteEngine engine, BsonMapper mapper)
        {
#if NO_ENTITY_MAPPER
            _collection = name ?? throw new ArgumentNullException(nameof(name));
#else
            _collection = name ?? mapper.ResolveCollectionName(typeof(T));
#endif
            _engine = engine;
            _mapper = mapper;
#if !NO_INCLUDE_QUERY
            _includes = new List<BsonExpression>();
#endif

            // if strong typed collection, get _id member mapped (if exists)
            if (typeof(T) == typeof(BsonDocument))
            {
#if !NO_ENTITY_MAPPER
                _entity = null;
                _id = null;
#endif
                _autoId = autoId;
            }
            else
            {
#if NO_ENTITY_MAPPER
                throw Unsupported.EntityMapper;
#else
                _entity = mapper.GetEntityMapper(typeof(T));
                _id = _entity.Id;

                if (_id != null && _id.AutoId)
                {
                    _autoId =
                        _id.DataType == typeof(Int32) || _id.DataType == typeof(Int32?) ? BsonAutoId.Int32 :
                        _id.DataType == typeof(Int64) || _id.DataType == typeof(Int64?) ? BsonAutoId.Int64 :
                        _id.DataType == typeof(Guid) || _id.DataType == typeof(Guid?) ? BsonAutoId.Guid :
                        BsonAutoId.ObjectId;
                }
                else
                {
                    _autoId = autoId;
                }
#endif
            }
        }
    }
}