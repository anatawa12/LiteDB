using System;

namespace LiteDB
{
    internal static class Unsupported
    {
        public static Exception AesRemoved => new Exception("AES is removed");
        public static Exception Query => new Exception("SQL support is removed");
        public static Exception Shared => new Exception("Shared Connection is removed");
        public static Exception WhereQuery => new Exception("Where querty support is removed");

        // EXPRESSION_PARSER_ONLY_FOR_INDEX
        public static Exception ParametersInExpression => new Exception("Parameters in expression");
        public static Exception FunctionsInExpression => new Exception("Functions in expression");
        public static Exception SourceInExpression => new Exception("'*' in expression");
        public static Exception OperatorsInExpression => new Exception("Operators in expression");
    }
}
