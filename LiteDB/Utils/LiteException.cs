using LiteDB.Engine;
using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static LiteDB.Constants;

namespace LiteDB
{
    /// <summary>
    /// The main exception for LiteDB
    /// </summary>
    public class LiteException : Exception
    {
        #region Errors code

        public enum LiteErrorCode
        {
            UNKNOWN = 0,
#if !NO_FILE_STORAGE
            FILE_NOT_FOUND = 101,
#endif
#if !NO_UNUSED_ERROR_CODE
            DATABASE_SHUTDOWN = 102,
#endif
            INVALID_DATABASE = 103,
#if !NO_UNUSED_ERROR_CODE
            FILE_SIZE_EXCEEDED = 105,
            COLLECTION_LIMIT_EXCEEDED = 106,
#endif
#if !NO_CREATE_INDEX
            INDEX_DROP_ID = 108,
#endif
            INDEX_DUPLICATE_KEY = 110,
            INVALID_INDEX_KEY = 111,
            INDEX_NOT_FOUND = 112,
#if !NO_UNUSED_ERROR_CODE
            INVALID_DBREF = 113,
#endif
            LOCK_TIMEOUT = 120,
#if !NO_UNUSED_ERROR_CODE
            INVALID_COMMAND = 121,
#endif
#if !NO_RENAME_COLLECTION
            ALREADY_EXISTS_COLLECTION_NAME = 122,
#endif
#if !NO_UNUSED_ERROR_CODE
            ALREADY_OPEN_DATAFILE = 124,
#endif
            INVALID_TRANSACTION_STATE = 126,
#if !NO_UNUSED_ERROR_CODE
            INDEX_NAME_LIMIT_EXCEEDED = 128,
#endif
#if !NO_CREATE_INDEX
            INVALID_INDEX_NAME = 129,
#endif
            INVALID_COLLECTION_NAME = 130,
#if !NO_UNUSED_ERROR_CODE
            TEMP_ENGINE_ALREADY_DEFINED = 131,
#endif
#if !NO_WHERE_QUERY
            INVALID_EXPRESSION_TYPE = 132,
#endif
#if !NO_UNUSED_ERROR_CODE
            COLLECTION_NOT_FOUND = 133,
            COLLECTION_ALREADY_EXIST = 134,
#endif
#if !NO_CREATE_INDEX
            INDEX_ALREADY_EXIST = 135,
#endif
            INVALID_UPDATE_FIELD = 136,
            ENGINE_DISPOSED = 137,
#if !NO_V7_MIGRATION
            INVALID_FORMAT = 200,
#endif
#if !NO_ENTITY_MAPPER
            DOCUMENT_MAX_DEPTH = 201,
            INVALID_CTOR = 202,
#endif
            UNEXPECTED_TOKEN = 203,
            INVALID_DATA_TYPE = 204,
#if !NO_ENTITY_MAPPER
            PROPERTY_NOT_MAPPED = 206,
            INVALID_TYPED_NAME = 207,
            PROPERTY_READ_WRITE = 209,
#endif
#if !NO_AES
            INITIALSIZE_CRYPTO_NOT_SUPPORTED = 210,
#endif
            INVALID_INITIALSIZE = 211,
            INVALID_NULL_CHAR_STRING = 212,
            INVALID_FREE_SPACE_PAGE = 213,
#if !NO_ENTITY_MAPPER
            DATA_TYPE_NOT_ASSIGNABLE = 214,
            AVOID_USE_OF_PROCESS = 215,
#endif
#if !NO_AES
            NOT_ENCRYPTED = 216,
            INVALID_PASSWORD = 217,
#endif
            UNSUPPORTED = 300,

            INVALID_DATAFILE_STATE = 999,
        }

        #endregion

        #region Ctor

        public LiteErrorCode ErrorCode { get; private set; }
        public long Position { get; private set; }

        public LiteException(LiteErrorCode code, string message)
            : base(message)
        {
            this.ErrorCode = code;
        }

        internal LiteException(LiteErrorCode code, string message, params object[] args)
            : base(string.Format(message, args))
        {
            this.ErrorCode = code;
        }

        internal LiteException (LiteErrorCode code, Exception inner, string message, params object[] args)
        : base (string.Format (message, args), inner)
        {
            this.ErrorCode = code;
        }

        /// <summary>
        /// Critical error should be stop engine and release data files and all memory allocation
        /// </summary>
        public bool IsCritical => (int)this.ErrorCode >= 900;

        #endregion

        #region Method Errors

#if !NO_FILE_STORAGE
        internal static LiteException FileNotFound(object fileId)
        {
            return new LiteException(LiteErrorCode.FILE_NOT_FOUND, "File '{0}' not found.", fileId);
        }
#endif

#if !NO_UNUSED_ERROR_CODE
        internal static LiteException DatabaseShutdown()
        {
            return new LiteException(LiteErrorCode.DATABASE_SHUTDOWN, "Database is in shutdown process.");
        }
#endif

        internal static LiteException InvalidDatabase()
        {
            return new LiteException(LiteErrorCode.INVALID_DATABASE, "File is not a valid LiteDB database format or contains a invalid password.");
        }

#if !NO_UNUSED_ERROR_CODE
        internal static LiteException FileSizeExceeded(long limit)
        {
            return new LiteException(LiteErrorCode.FILE_SIZE_EXCEEDED, "Database size exceeds limit of {0}.", FileHelper.FormatFileSize(limit));
        }

        internal static LiteException CollectionLimitExceeded(int limit)
        {
            return new LiteException(LiteErrorCode.COLLECTION_LIMIT_EXCEEDED, "This database exceeded the maximum limit of collection names size: {0} bytes", limit);
        }

        internal static LiteException IndexNameLimitExceeded(int limit)
        {
            return new LiteException(LiteErrorCode.INDEX_NAME_LIMIT_EXCEEDED, "This collection exceeded the maximum limit of indexes names/expression size: {0} bytes", limit);
        }
#endif

#if !NO_CREATE_INDEX
        internal static LiteException InvalidIndexName(string name, string collection, string reason)
        {
            return new LiteException(LiteErrorCode.INVALID_INDEX_NAME, "Invalid index name '{0}' on collection '{1}': {2}", name, collection, reason);
        }
#endif

        internal static LiteException InvalidCollectionName(string name, string reason)
        {
            return new LiteException(LiteErrorCode.INVALID_COLLECTION_NAME, "Invalid collection name '{0}': {1}", name, reason);
        }

#if !NO_CREATE_INDEX
        internal static LiteException IndexDropId()
        {
            return new LiteException(LiteErrorCode.INDEX_DROP_ID, "Primary key index '_id' can't be dropped.");
        }
#endif

#if !NO_UNUSED_ERROR_CODE
        internal static LiteException TempEngineAlreadyDefined()
        {
            return new LiteException(LiteErrorCode.TEMP_ENGINE_ALREADY_DEFINED, "Temporary engine already defined or auto created.");
        }

        internal static LiteException CollectionNotFound(string key)
        {
            return new LiteException(LiteErrorCode.COLLECTION_NOT_FOUND, "Collection not found: '{0}'", key);
        }
#endif

#if !NO_WHERE_QUERY
        internal static LiteException InvalidExpressionType(BsonExpression expr, BsonExpressionType type)
        {
            return new LiteException(LiteErrorCode.INVALID_EXPRESSION_TYPE, "Expression '{0}' must be a {1} type.", expr.Source, type);
        }

        internal static LiteException InvalidExpressionTypePredicate(BsonExpression expr)
        {
            return new LiteException(LiteErrorCode.INVALID_EXPRESSION_TYPE, "Expression '{0}' are not supported as predicate expression.", expr.Source);
        }
#endif

#if !NO_UNUSED_ERROR_CODE
        internal static LiteException CollectionAlreadyExist(string key)
        {
            return new LiteException(LiteErrorCode.COLLECTION_ALREADY_EXIST, "Collection already exist: '{0}'", key);
        }
#endif

#if !NO_CREATE_INDEX
        internal static LiteException IndexAlreadyExist(string name)
        {
            return new LiteException(LiteErrorCode.INDEX_ALREADY_EXIST, "Index name '{0}' already exist with a differnt expression. Try drop index first.", name);
        }
#endif

        internal static LiteException InvalidUpdateField(string field)
        {
            return new LiteException(LiteErrorCode.INVALID_UPDATE_FIELD, "'{0}' can't be modified in UPDATE command.", field);
        }

        internal static LiteException IndexDuplicateKey(string field, BsonValue key)
        {
            return new LiteException(LiteErrorCode.INDEX_DUPLICATE_KEY, "Cannot insert duplicate key in unique index '{0}'. The duplicate value is '{1}'.", field, key);
        }

        internal static LiteException InvalidIndexKey(string text)
        {
            return new LiteException(LiteErrorCode.INVALID_INDEX_KEY, text);
        }

        internal static LiteException IndexNotFound(string name)
        {
            return new LiteException(LiteErrorCode.INDEX_NOT_FOUND, "Index not found '{0}'.", name);
        }

        internal static LiteException LockTimeout(string mode, TimeSpan ts)
        {
            return new LiteException(LiteErrorCode.LOCK_TIMEOUT, "Database lock timeout when entering in {0} mode after {1}", mode, ts.ToString());
        }

        internal static LiteException LockTimeout(string mode, string collection, TimeSpan ts)
        {
            return new LiteException(LiteErrorCode.LOCK_TIMEOUT, "Collection '{0}' lock timeout when entering in {1} mode after {2}", collection, mode, ts.ToString());
        }

#if !NO_UNUSED_ERROR_CODE
        internal static LiteException InvalidCommand(string command)
        {
            return new LiteException(LiteErrorCode.INVALID_COMMAND, "Command '{0}' is not a valid shell command.", command);
        }
#endif

#if !NO_RENAME_COLLECTION
        internal static LiteException AlreadyExistsCollectionName(string newName)
        {
            return new LiteException(LiteErrorCode.ALREADY_EXISTS_COLLECTION_NAME, "New collection name '{0}' already exists.", newName);
        }
#endif

#if !NO_UNUSED_ERROR_CODE
        internal static LiteException AlreadyOpenDatafile(string filename)
        {
            return new LiteException(LiteErrorCode.ALREADY_OPEN_DATAFILE, "Your datafile '{0}' is open in another process.", filename);
        }

        internal static LiteException InvalidDbRef(string path)
        {
            return new LiteException(LiteErrorCode.INVALID_DBREF, "Invalid value for DbRef in path '{0}'. Value must be document like {{ $ref: \"?\", $id: ? }}", path);
        }
#endif

        internal static LiteException AlreadyExistsTransaction()
        {
            return new LiteException(LiteErrorCode.INVALID_TRANSACTION_STATE, "The current thread already contains an open transaction. Use the Commit/Rollback method to release the previous transaction.");
        }

        internal static LiteException CollectionLockerNotFound(string collection)
        {
            return new LiteException(LiteErrorCode.INVALID_TRANSACTION_STATE, "Collection locker '{0}' was not found inside dictionary.", collection);
        }

#if !NO_V7_MIGRATION
        internal static LiteException InvalidFormat(string field)
        {
            return new LiteException(LiteErrorCode.INVALID_FORMAT, "Invalid format: {0}", field);
        }
#endif

#if !NO_ENTITY_MAPPER
        internal static LiteException DocumentMaxDepth(int depth, Type type)
        {
            return new LiteException(LiteErrorCode.DOCUMENT_MAX_DEPTH, "Document has more than {0} nested documents in '{1}'. Check for circular references (use DbRef).", depth, type == null ? "-" : type.Name);
        }

        internal static LiteException InvalidCtor(Type type, Exception inner)
        {
            return new LiteException(LiteErrorCode.INVALID_CTOR, inner, "Failed to create instance for type '{0}' from assembly '{1}'. Checks if the class has a public constructor with no parameters.", type.FullName, type.AssemblyQualifiedName);
        }
#endif

        internal static LiteException UnexpectedToken(Token token, string expected = null)
        {
            var position = (token?.Position - (token?.Value?.Length ?? 0)) ?? 0;
            var str = token?.Type == TokenType.EOF ? "[EOF]" : token?.Value ?? "";
            var exp = expected == null ? "" : $" Expected `{expected}`.";

            return new LiteException(LiteErrorCode.UNEXPECTED_TOKEN, $"Unexpected token `{str}` in position {position}.{exp}")
            {
                Position = position
            };
        }

        internal static LiteException UnexpectedToken(string message, Token token)
        {
            var position = (token?.Position - (token?.Value?.Length ?? 0)) ?? 0;

            return new LiteException(LiteErrorCode.UNEXPECTED_TOKEN, message)
            {
                Position = position
            };
        }

        internal static LiteException InvalidDataType(string field, BsonValue value)
        {
            return new LiteException(LiteErrorCode.INVALID_DATA_TYPE, "Invalid BSON data type '{0}' on field '{1}'.", value.Type, field);
        }

#if !NO_ENTITY_MAPPER
        internal static LiteException PropertyReadWrite(PropertyInfo prop)
        {
            return new LiteException(LiteErrorCode.PROPERTY_READ_WRITE, "'{0}' property must have public getter and setter.", prop.Name);
        }

        internal static LiteException PropertyNotMapped(string name)
        {
            return new LiteException(LiteErrorCode.PROPERTY_NOT_MAPPED, "Property '{0}' was not mapped into BsonDocument.", name);
        }

        internal static LiteException InvalidTypedName(string type)
        {
            return new LiteException(LiteErrorCode.INVALID_TYPED_NAME, "Type '{0}' not found in current domain (_type format is 'Type.FullName, AssemblyName').", type);
        }
#endif

#if !NO_AES
        internal static LiteException InitialSizeCryptoNotSupported()
        {
            return new LiteException(LiteErrorCode.INITIALSIZE_CRYPTO_NOT_SUPPORTED, "Initial Size option is not supported for encrypted datafiles.");
        }
#endif

        internal static LiteException InvalidInitialSize()
        {
            return new LiteException(LiteErrorCode.INVALID_INITIALSIZE, "Initial Size must be a multiple of page size ({0} bytes).", PAGE_SIZE);
        }

        internal static LiteException EngineDisposed()
        {
            return new LiteException(LiteErrorCode.ENGINE_DISPOSED, "This engine instance already disposed.");
        }

        internal static LiteException InvalidNullCharInString()
        {
            return new LiteException(LiteErrorCode.INVALID_NULL_CHAR_STRING, "Invalid null character (\\0) was found in the string");
        }

        internal static LiteException InvalidPageType(PageType pageType, BasePage page)
        {
            var sb = new StringBuilder($"Invalid {pageType} on {page.PageID}. ");

            sb.Append($"Full zero: {page.Buffer.All(0)}. ");
            sb.Append($"Page Type: {page.PageType}. ");
            sb.Append($"Prev/Next: {page.PrevPageID}/{page.NextPageID}. ");
            sb.Append($"UniqueID: {page.Buffer.UniqueID}. ");
            sb.Append($"ShareCounter: {page.Buffer.ShareCounter}. ");

            return new LiteException(0, sb.ToString());
        }

        internal static LiteException InvalidFreeSpacePage(uint pageID, int freeBytes, int length)
        {
            return new LiteException(LiteErrorCode.INVALID_FREE_SPACE_PAGE, $"An operation that would corrupt page {pageID} was prevented. The operation required {length} free bytes, but the page had only {freeBytes} available.");
        }

#if !NO_ENTITY_MAPPER
        internal static LiteException DataTypeNotAssignable(string type1, string type2)
        {
            return new LiteException(LiteErrorCode.DATA_TYPE_NOT_ASSIGNABLE, $"Data type {type1} is not assignable from data type {type2}");
        }
#endif
            
#if !NO_AES
        internal static LiteException FileNotEncrypted()
        {
            return new LiteException(LiteErrorCode.NOT_ENCRYPTED, "File is not encrypted.");
        }

        internal static LiteException InvalidPassword()
        {
            return new LiteException(LiteErrorCode.INVALID_PASSWORD, "Invalid password.");
        }
#endif

#if !NO_ENTITY_MAPPER
        internal static LiteException AvoidUseOfProcess()
        {
            return new LiteException(LiteErrorCode.AVOID_USE_OF_PROCESS, $"LiteDB do not accept System.Diagnostics.Process class in deserialize mapper");
        }
#endif

        internal static LiteException InvalidDatafileState(string message)
        {
            return new LiteException(LiteErrorCode.INVALID_DATAFILE_STATE, message);
        }

        #endregion
    }
}