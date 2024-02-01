using System;

namespace LiteDB
{
    internal static class Unsupported
    {
        public static Exception AesRemoved => new Exception("AES is removed");
        public static Exception Query => new Exception("SQL support is removed");
        public static Exception Shared => new Exception("Shared Connection is removed");
        public static Exception WhereQuery => new Exception("Where querty support is removed");
        public static Exception OffsetQuery => new Exception("Offset query support is removed");
        public static Exception LimitQuery => new Exception("Limit query support is removed");
    }
}
