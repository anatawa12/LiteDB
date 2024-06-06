using LiteDB.Engine;
using System;
using System.Collections.Generic;
using System.Globalization;
using static LiteDB.Constants;

namespace LiteDB
{
    /// <summary>
    /// Manage ConnectionString to connect and create databases. Connection string are NameValue using Name1=Value1; Name2=Value2
    /// </summary>
    public class ConnectionString
    {
        private readonly Dictionary<string, string> _values;

        /// <summary>
        /// "connection": Return how engine will be open (default: Direct)
        /// </summary>
        public ConnectionType Connection { get; set; } = ConnectionType.Direct;

        /// <summary>
        /// "filename": Full path or relative path from DLL directory
        /// </summary>
        public string Filename { get; set; } = "";

        /// <summary>
        /// "password": Database password used to encrypt/decypted data pages
        /// </summary>
        public string Password { get; set; } = null;

        /// <summary>
        /// "initial size": If database is new, initialize with allocated space - support KB, MB, GB (default: 0)
        /// </summary>
        public long InitialSize { get; set; } = 0;

        /// <summary>
        /// "readonly": Open datafile in readonly mode (default: false)
        /// </summary>
        public bool ReadOnly { get; set; } = false;

#if !NO_V7_MIGRATION
        /// <summary>
        /// "upgrade": Check if data file is an old version and convert before open (default: false)
        /// </summary>
        public bool Upgrade { get; set; } = false;
#endif

        /// <summary>
        /// "auto-rebuild": If last close database exception result a invalid data state, rebuild datafile on next open (default: false)
        /// </summary>
        public bool AutoRebuild { get; set; } = false;

#if !INVARIANT_CULTURE
        /// <summary>
        /// "collation": Set default collaction when database creation (default: "[CurrentCulture]/IgnoreCase")
        /// </summary>
        public Collation Collation { get; set; }
#endif

        /// <summary>
        /// Initialize empty connection string
        /// </summary>
        public ConnectionString()
        {
            _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Initialize connection string parsing string in "key1=value1;key2=value2;...." format or only "filename" as default (when no ; char found)
        /// </summary>
        public ConnectionString(string connectionString)
            : this()
        {
            if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            // create a dictionary from string name=value collection
            if (connectionString.Contains("="))
            {
                _values.ParseKeyValue(connectionString);
            }
            else
            {
                _values["filename"] = connectionString;
            }

            // setting values to properties
            this.Connection = _values.GetValue("connection", this.Connection);
            this.Filename = _values.GetValue("filename", this.Filename).Trim();

            this.Password = _values.GetValue("password", this.Password);

            if(this.Password == string.Empty)
            {
                this.Password = null;
            }

            this.InitialSize = _values.GetFileSize(@"initial size", this.InitialSize);
            this.ReadOnly = _values.GetValue("readonly", this.ReadOnly);

#if INVARIANT_CULTURE
            if (_values.ContainsKey("collation")) throw Unsupported.Culture;
#else
            this.Collation = _values.ContainsKey("collation") ? new Collation(_values.GetValue<string>("collation")) : this.Collation;
#endif

#if NO_V7_MIGRATION
            if (_values.ContainsKey("upgrade")) throw Unsupported.V7Migration;
#else
            this.Upgrade = _values.GetValue("upgrade", this.Upgrade);
#endif
            this.AutoRebuild = _values.GetValue("auto-rebuild", this.AutoRebuild);
        }

        /// <summary>
        /// Get value from parsed connection string. Returns null if not found
        /// </summary>
        public string this[string key] => _values.GetOrDefault(key);

        /// <summary>
        /// Create ILiteEngine instance according string connection parameters. For now, only Local/Shared are supported
        /// </summary>
        internal ILiteEngine CreateEngine()
        {
            var settings = new EngineSettings
            {
                Filename = this.Filename,
                Password = this.Password,
                InitialSize = this.InitialSize,
                ReadOnly = this.ReadOnly,
#if !INVARIANT_CULTURE
                Collation = this.Collation
#endif
#if !NO_V7_MIGRATION
                Upgrade = this.Upgrade,
#endif
#if !NO_CREATE_INDEX
                AutoRebuild = this.AutoRebuild,
#endif
            };

            // create engine implementation as Connection Type
            if (this.Connection == ConnectionType.Direct)
            {
                return new LiteEngine(settings);
            }
            else if (this.Connection == ConnectionType.Shared)
            {
#if NO_SHARED
                throw Unsupported.Shared;
#else
                return new SharedEngine(settings);
#endif
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}