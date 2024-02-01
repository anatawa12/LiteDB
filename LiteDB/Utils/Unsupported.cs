using System;

namespace LiteDB
{
    internal static class Unsupported
    {
        public static Exception AesRemoved => new Exception("AES is removed");
    }
}
