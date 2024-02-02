using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
using System.Linq.Expressions;
#endif
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using static LiteDB.Constants;

namespace LiteDB
{
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
    internal enum BsonExpressionParserMode { Full, Single, SelectDocument, UpdateDocument }
#endif

    /// <summary>
    /// Compile and execute simple expressions using BsonDocuments. Used in indexes and updates operations. See https://github.com/mbdavid/LiteDB/wiki/Expressions
    /// </summary>
    internal class BsonExpressionParser
    {
        #region Operators quick access

#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
        private static MethodInfo M(string s) => typeof(BsonExpressionOperators).GetMethod(s);

        /// <summary>
        /// Operation definition by methods with defined expression type (operators are in precedence order)
        /// </summary>
        private static readonly Dictionary<string, Tuple<string, MethodInfo, BsonExpressionType>> _operators = new Dictionary<string, Tuple<string, MethodInfo, BsonExpressionType>>
        {
            // arithmetic
            ["%"] = Tuple.Create("%", M("MOD"), BsonExpressionType.Modulo),
            ["/"] = Tuple.Create("/", M("DIVIDE"), BsonExpressionType.Divide),
            ["*"] = Tuple.Create("*", M("MULTIPLY"), BsonExpressionType.Multiply),
            ["+"] = Tuple.Create("+", M("ADD"), BsonExpressionType.Add),
            ["-"] = Tuple.Create("-", M("MINUS"), BsonExpressionType.Subtract),

            // predicate
            ["LIKE"] = Tuple.Create(" LIKE ", M("LIKE"), BsonExpressionType.Like),
            ["BETWEEN"] = Tuple.Create(" BETWEEN ", M("BETWEEN"), BsonExpressionType.Between),
            ["IN"] = Tuple.Create(" IN ", M("IN"), BsonExpressionType.In),

            [">"] = Tuple.Create(">", M("GT"), BsonExpressionType.GreaterThan),
            [">="] = Tuple.Create(">=", M("GTE"), BsonExpressionType.GreaterThanOrEqual),
            ["<"] = Tuple.Create("<", M("LT"), BsonExpressionType.LessThan),
            ["<="] = Tuple.Create("<=", M("LTE"), BsonExpressionType.LessThanOrEqual),

            ["!="] = Tuple.Create("!=", M("NEQ"), BsonExpressionType.NotEqual),
            ["="] = Tuple.Create("=", M("EQ"), BsonExpressionType.Equal),

            ["ANY LIKE"] = Tuple.Create(" ANY LIKE ", M("LIKE_ANY"), BsonExpressionType.Like),
            ["ANY BETWEEN"] = Tuple.Create(" ANY BETWEEN ", M("BETWEEN_ANY"), BsonExpressionType.Between),
            ["ANY IN"] = Tuple.Create(" ANY IN ", M("IN_ANY"), BsonExpressionType.In),

            ["ANY >"] = Tuple.Create(" ANY>", M("GT_ANY"), BsonExpressionType.GreaterThan),
            ["ANY >="] = Tuple.Create(" ANY>=", M("GTE_ANY"), BsonExpressionType.GreaterThanOrEqual),
            ["ANY <"] = Tuple.Create(" ANY<", M("LT_ANY"), BsonExpressionType.LessThan),
            ["ANY <="] = Tuple.Create(" ANY<=", M("LTE_ANY"), BsonExpressionType.LessThanOrEqual),

            ["ANY !="] = Tuple.Create(" ANY!=", M("NEQ_ANY"), BsonExpressionType.NotEqual),
            ["ANY ="] = Tuple.Create(" ANY=", M("EQ_ANY"), BsonExpressionType.Equal),

            ["ALL LIKE"] = Tuple.Create(" ALL LIKE ", M("LIKE_ALL"), BsonExpressionType.Like),
            ["ALL BETWEEN"] = Tuple.Create(" ALL BETWEEN ", M("BETWEEN_ALL"), BsonExpressionType.Between),
            ["ALL IN"] = Tuple.Create(" ALL IN ", M("IN_ALL"), BsonExpressionType.In),

            ["ALL >"] = Tuple.Create(" ALL>", M("GT_ALL"), BsonExpressionType.GreaterThan),
            ["ALL >="] = Tuple.Create(" ALL>=", M("GTE_ALL"), BsonExpressionType.GreaterThanOrEqual),
            ["ALL <"] = Tuple.Create(" ALL<", M("LT_ALL"), BsonExpressionType.LessThan),
            ["ALL <="] = Tuple.Create(" ALL<=", M("LTE_ALL"), BsonExpressionType.LessThanOrEqual),

            ["ALL !="] = Tuple.Create(" ALL!=", M("NEQ_ALL"), BsonExpressionType.NotEqual),
            ["ALL ="] = Tuple.Create(" ALL=", M("EQ_ALL"), BsonExpressionType.Equal),

            // logic (will use Expression.AndAlso|OrElse)
            ["AND"] = Tuple.Create(" AND ", (MethodInfo)null, BsonExpressionType.And),
            ["OR"] = Tuple.Create(" OR ", (MethodInfo)null, BsonExpressionType.Or)
        };

        private static readonly MethodInfo _parameterPathMethod = M("PARAMETER_PATH");
        private static readonly MethodInfo _memberPathMethod = M("MEMBER_PATH");
        private static readonly MethodInfo _arrayIndexMethod = M("ARRAY_INDEX");
        private static readonly MethodInfo _arrayFilterMethod = M("ARRAY_FILTER");

        private static readonly MethodInfo _documentInitMethod = M("DOCUMENT_INIT");
        private static readonly MethodInfo _arrayInitMethod = M("ARRAY_INIT");

        private static readonly MethodInfo _itemsMethod = typeof(BsonExpressionMethods).GetMethod("ITEMS");
        private static readonly MethodInfo _arrayMethod = typeof(BsonExpressionMethods).GetMethod("ARRAY");
#endif

        #endregion

        /// <summary>
        /// Start parse string into linq expression. Read path, function or base type bson values (int, double, bool, string)
        /// </summary>
        public static BsonExpression ParseFullExpression(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters, DocumentScope scope)
        {
            var first = ParseSingleExpression(tokenizer, context, parameters, scope);
            var values = new List<BsonExpression> { first };
            var ops = new List<string>();

            // read all blocks and operation first
            while (!tokenizer.EOF)
            {
                // read operator between expressions
                var op = ReadOperant(tokenizer);

                if (op == null) break;

#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                throw Unsupported.OperatorsInExpression;
#else
                var expr = ParseSingleExpression(tokenizer, context, parameters, scope);

                // special BETWEEN "AND" read
                if (op.EndsWith("BETWEEN", StringComparison.OrdinalIgnoreCase))
                {
                    var and = tokenizer.ReadToken(true).Expect("AND");

                    var expr2 = ParseSingleExpression(tokenizer, context, parameters, scope);

                    // convert expr and expr2 into an array with 2 values
                    expr = NewArray(expr, expr2);
                }

                values.Add(expr);
                ops.Add(op.ToUpperInvariant());
#endif
            }

#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
            var order = 0;
#endif

            // now, process operator in correct order
            while (values.Count >= 2)
            {
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                throw Unsupported.OperatorsInExpression;
#else
                var op = _operators.ElementAt(order);
                var n = ops.IndexOf(op.Key);

                if (n == -1)
                {
                    order++;
                }
                else
                {
                    // get left/right values to execute operator
                    var left = values.ElementAt(n);
                    var right = values.ElementAt(n + 1);

                    var src = op.Value.Item1;
                    var method = op.Value.Item2;
                    var type = op.Value.Item3;

                    // test left/right scalar
                    var isLeftEnum = op.Key.StartsWith("ALL") || op.Key.StartsWith("ANY");

                    if (isLeftEnum && left.IsScalar) left = ConvertToEnumerable(left);
                    //if (isLeftEnum && left.IsScalar) throw new LiteException(0, $"Left expression `{left.Source}` must return multiples values");
                    if (!isLeftEnum && !left.IsScalar) throw new LiteException(0, $"Left expression `{left.Source}` returns more than one result. Try use ANY or ALL before operant.");
                    if (!isLeftEnum && !right.IsScalar) throw new LiteException(0, $"Left expression `{right.Source}` must return a single value");
                    if (right.IsScalar == false) throw new LiteException(0, $"Right expression `{right.Source}` must return a single value");

                    BsonExpression result;

                    // when operation is AND/OR, use AndAlso|OrElse
                    if (type == BsonExpressionType.And || type == BsonExpressionType.Or)
                    {
                        result = CreateLogicExpression(type, left, right);
                    }
                    else
                    {
                        // method call parameters
                        var args = new List<Expression>();

                        if (method?.GetParameters().FirstOrDefault()?.ParameterType == typeof(Collation))
                        {
                            args.Add(context.Collation);
                        }

                        args.Add(left.Expression);
                        args.Add(right.Expression);

                        // process result in a single value
                        result = new BsonExpression
                        {
                            Type = type,
                            Parameters = parameters,
                            IsImmutable = left.IsImmutable && right.IsImmutable,
                            UseSource = left.UseSource || right.UseSource,
                            IsScalar = true,
                            Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase).AddRange(left.Fields).AddRange(right.Fields),
                            Expression = Expression.Call(method, args.ToArray()),
                            Left = left,
                            Right = right,
                            Source = left.Source + src + right.Source
                        };
                    }

                    // remove left+right and insert result
                    values.Insert(n, result);
                    values.RemoveRange(n + 1, 2);

                    // remove operation
                    ops.RemoveAt(n);
                }
#endif
            }

            return values.Single();
        }

        /// <summary>
        /// Start parse string into linq expression. Read path, function or base type bson values (int, double, bool, string)
        /// </summary>
        public static BsonExpression ParseSingleExpression(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters, DocumentScope scope)
        {
            // read next token and test with all expression parts
            var token = tokenizer.ReadToken();

            return
                TryParseDouble(tokenizer, parameters) ??
                TryParseInt(tokenizer, parameters) ??
                TryParseBool(tokenizer, parameters) ??
                TryParseNull(tokenizer, parameters) ??
                TryParseString(tokenizer, parameters) ??
                TryParseSource(tokenizer, context, parameters, scope) ??
                TryParseDocument(tokenizer, context, parameters, scope) ??
                TryParseArray(tokenizer, context, parameters, scope) ??
                TryParseParameter(tokenizer, context, parameters, scope) ??
                TryParseInnerExpression(tokenizer, context, parameters, scope) ??
                TryParseFunction(tokenizer, context, parameters, scope) ??
                TryParseMethodCall(tokenizer, context, parameters, scope) ??
                TryParsePath(tokenizer, context, parameters, scope) ??
                throw LiteException.UnexpectedToken(token);
        }

#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
        /// <summary>
        /// Parse a document builder syntax used in SELECT statment: {expr0} [AS] [{alias}], {expr1} [AS] [{alias}], ...
        /// </summary>
        public static BsonExpression ParseSelectDocumentBuilder(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters)
        {
            // creating unique field names
            var fields = new List<KeyValuePair<string, BsonExpression>>();
            var names = new HashSet<string>();
            var counter = 1;

            // define when next token means finish reading document builder
            bool stop(Token t) => t.Is("FROM") || t.Is("INTO") || t.Type == TokenType.EOF || t.Type == TokenType.SemiColon;

            void Add(string alias, BsonExpression expr)
            {
                if (names.Contains(alias)) alias += counter++;

                names.Add(alias);

                if (!expr.IsScalar) expr = ConvertToArray(expr);

                fields.Add(new KeyValuePair<string, BsonExpression>(alias, expr));
            };

            while (true)
            {
                var expr = ParseFullExpression(tokenizer, context, parameters, DocumentScope.Root);

                var next = tokenizer.LookAhead();

                // finish reading
                if (stop(next))
                {
                    Add(expr.DefaultFieldName(), expr);

                    break;
                }
                // field with no alias
                if (next.Type == TokenType.Comma)
                {
                    tokenizer.ReadToken(); // consume ,

                    Add(expr.DefaultFieldName(), expr);
                }
                // using alias
                else
                {
                    if (next.Is("AS"))
                    {
                        tokenizer.ReadToken(); // consume "AS"
                    }

                    var alias = tokenizer.ReadToken().Expect(TokenType.Word);

                    Add(alias.Value, expr);

                    // go ahead to next token to see if last field
                    next = tokenizer.LookAhead();

                    if (stop(next))
                    {
                        break;
                    }

                    // consume ,
                    tokenizer.ReadToken().Expect(TokenType.Comma);
                }
            }

            var first = fields[0].Value;

            if (fields.Count == 1)
            {
                // if just $ return empty BsonExpression
                if (first.Type == BsonExpressionType.Path && first.Source == "$") return BsonExpression.Root;

                // if single field already a document
                if (fields.Count == 1 && first.Type == BsonExpressionType.Document) return first;

                // special case: EXTEND method also returns only a document
                if (fields.Count == 1 && first.Type == BsonExpressionType.Call && first.Source.StartsWith("EXTEND")) return first;
            }

            var arrKeys = Expression.NewArrayInit(typeof(string), fields.Select(x => Expression.Constant(x.Key)).ToArray());
            var arrValues = Expression.NewArrayInit(typeof(BsonValue), fields.Select(x => x.Value.Expression).ToArray());

            return new BsonExpression
            {
                Type = BsonExpressionType.Document,
                Parameters = parameters,
                IsImmutable = fields.All(x => x.Value.IsImmutable),
                UseSource = fields.Any(x => x.Value.UseSource),
                IsScalar = true,
                Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase).AddRange(fields.SelectMany(x => x.Value.Fields)),
                Expression = Expression.Call(_documentInitMethod, new Expression[] { arrKeys, arrValues }),
                Source = "{" + string.Join(",", fields.Select(x => x.Key + ":" + x.Value.Source)) + "}"
            };
        }

        /// <summary>
        /// Parse a document builder syntax used in UPDATE statment: 
        /// {key0} = {expr0}, .... will be converted into { key: [expr], ... }
        /// {key: value} ... return return a new document
        /// </summary>
        public static BsonExpression ParseUpdateDocumentBuilder(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters)
        {
            var next = tokenizer.LookAhead();

            // if starts with { just return a normal document expression
            if (next.Type == TokenType.OpenBrace)
            {
                tokenizer.ReadToken(); // consume {

                return TryParseDocument(tokenizer, context, parameters, DocumentScope.Root);
            }

            var keys = new List<Expression>();
            var values = new List<Expression>();
            var src = new StringBuilder();
            var isImmutable = true;
            var useSource = false;
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            src.Append("{");

            while (!tokenizer.CheckEOF())
            {
                var key = ReadKey(tokenizer, src);

                tokenizer.ReadToken().Expect(TokenType.Equals);

                src.Append(":");

                var value = ParseFullExpression(tokenizer, context, parameters, DocumentScope.Root);

                if (!value.IsScalar) value = ConvertToArray(value);

                // update isImmutable only when came false
                if (value.IsImmutable == false) isImmutable = false;
                if (value.UseSource) useSource = true;

                fields.AddRange(value.Fields);

                // add key and value to parameter list (as an expression)
                keys.Add(Expression.Constant(key));
                values.Add(value.Expression);

                src.Append(value.Source);

                // read ,
                if (tokenizer.LookAhead().Type == TokenType.Comma)
                {
                    src.Append(tokenizer.ReadToken().Value);
                    continue;
                }
                break;
            }

            src.Append("}");

            var arrKeys = Expression.NewArrayInit(typeof(string), keys.ToArray());
            var arrValues = Expression.NewArrayInit(typeof(BsonValue), values.ToArray());

            // create linq expression for "{ doc }"
            var docExpr = Expression.Call(_documentInitMethod, new Expression[] { arrKeys, arrValues });

            return new BsonExpression
            {
                Type = BsonExpressionType.Document,
                Parameters = parameters,
                IsImmutable = isImmutable,
                UseSource = useSource,
                IsScalar = true,
                Fields = fields,
                Expression = docExpr,
                Source = src.ToString()
            };
        }
#endif

        #region Constants

        /// <summary>
        /// Try parse double number - return null if not double token
        /// </summary>
        private static BsonExpression TryParseDouble(Tokenizer tokenizer, BsonDocument parameters)
        {
            string value = null;

            if (tokenizer.Current.Type == TokenType.Double)
            {
                value = tokenizer.Current.Value;
            }
            else if (tokenizer.Current.Type == TokenType.Minus)
            {
                var ahead = tokenizer.LookAhead(false);

                if (ahead.Type == TokenType.Double)
                {
                    value = "-" + tokenizer.ReadToken().Value;
                }
            }

            if (value != null)
            {
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture.NumberFormat);
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                var constant = Expression.Constant(new BsonValue(number));
#endif

                return new BsonExpression
                {
                    Type = BsonExpressionType.Double,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                    Parameters = parameters,
#endif
                    IsImmutable = true,
                    UseSource = false,
                    IsScalar = true,
                    Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    FuncScalar = Constant(new BsonValue(number)),
#else
                    Expression = constant,
#endif
                    Source = number.ToString("0.0########", CultureInfo.InvariantCulture.NumberFormat)
                };
            }

            return null;
        }

        /// <summary>
        /// Try parse int number - return null if not int token
        /// </summary>
        private static BsonExpression TryParseInt(Tokenizer tokenizer, BsonDocument parameters)
        {
            string value = null;

            if (tokenizer.Current.Type == TokenType.Int)
            {
                value = tokenizer.Current.Value;
            }
            else if (tokenizer.Current.Type == TokenType.Minus)
            {
                var ahead = tokenizer.LookAhead(false);

                if (ahead.Type == TokenType.Int)
                {
                    value = "-" + tokenizer.ReadToken().Value;
                }
            }

            if (value != null)
            {
                var isInt32 = Int32.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture.NumberFormat, out var i32);
                if (isInt32)
                {
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                    var constant32 = Expression.Constant(new BsonValue(i32));
#endif

                    return new BsonExpression
                    {
                        Type = BsonExpressionType.Int,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                        Parameters = parameters,
#endif
                        IsImmutable = true,
                        UseSource = false,
                        IsScalar = true,
                        Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                        FuncScalar = Constant(new BsonValue(i32)),
#else
                        Expression = constant32,
#endif
                        Source = i32.ToString(CultureInfo.InvariantCulture.NumberFormat)
                    };
                }

                var i64 = Int64.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture.NumberFormat);
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                var constant64 = Expression.Constant(new BsonValue(i64));
#endif

                return new BsonExpression
                {
                    Type = BsonExpressionType.Int,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                    Parameters = parameters,
#endif
                    IsImmutable = true,
                    UseSource = false,
                    IsScalar = true,
                    Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    FuncScalar = Constant(new BsonValue(i64)),
#else
                    Expression = constant64,
#endif
                    Source = i64.ToString(CultureInfo.InvariantCulture.NumberFormat)
                };
            }

            return null;
        }

        /// <summary>
        /// Try parse bool - return null if not bool token
        /// </summary>
        private static BsonExpression TryParseBool(Tokenizer tokenizer, BsonDocument parameters)
        {
            if (tokenizer.Current.Type == TokenType.Word && (tokenizer.Current.Is("true") || tokenizer.Current.Is("false")))
            {
                var boolean = Convert.ToBoolean(tokenizer.Current.Value);
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                var constant = Expression.Constant(new BsonValue(boolean));
#endif

                return new BsonExpression
                {
                    Type = BsonExpressionType.Boolean,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                    Parameters = parameters,
#endif
                    IsImmutable = true,
                    UseSource = false,
                    IsScalar = true,
                    Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    FuncScalar = Constant(new BsonValue(boolean)),
#else
                    Expression = constant,
#endif
                    Source = boolean.ToString().ToLower()
                };
            }

            return null;
        }

        /// <summary>
        /// Try parse null constant - return null if not null token
        /// </summary>
        private static BsonExpression TryParseNull(Tokenizer tokenizer, BsonDocument parameters)
        {
            if (tokenizer.Current.Type == TokenType.Word && tokenizer.Current.Is("null"))
            {
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                var constant = Expression.Constant(BsonValue.Null);
#endif

                return new BsonExpression
                {
                    Type = BsonExpressionType.Null,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                    Parameters = parameters,
#endif
                    IsImmutable = true,
                    UseSource = false,
                    IsScalar = true,
                    Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    FuncScalar = Constant(BsonValue.Null),
#else
                    Expression = constant,
#endif
                    Source = "null"
                };
            }

            return null;
        }

        /// <summary>
        /// Try parse string with both single/double quote - return null if not string
        /// </summary>
        private static BsonExpression TryParseString(Tokenizer tokenizer, BsonDocument parameters)
        {
            if (tokenizer.Current.Type == TokenType.String)
            {
                var bstr = new BsonValue(tokenizer.Current.Value);
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                var constant = Expression.Constant(bstr);
#endif

                return new BsonExpression
                {
                    Type = BsonExpressionType.String,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                    Parameters = parameters,
#endif
                    IsImmutable = true,
                    UseSource = false,
                    IsScalar = true,
                    Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    FuncScalar = Constant(bstr),
#else
                    Expression = constant,
#endif
                    Source = JsonSerializer.Serialize(bstr)
                };
            }

            return null;
        }

#if EXPRESSION_PARSER_ONLY_FOR_INDEX
        private static BsonExpressionScalarDelegate Constant(BsonValue value) => (_0, _1, _2) => value;
#endif

        #endregion

        /// <summary>
        /// Try parse json document - return null if not document token
        /// </summary>
        private static BsonExpression TryParseDocument(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters, DocumentScope scope)
        {
            if (tokenizer.Current.Type != TokenType.OpenBrace) return null;

            // read key value
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
            var keys = new List<Expression>();
            var values = new List<Expression>();
#else
            var keys = new List<string>();
            var values = new List<BsonExpressionScalarDelegate>();
#endif
            var src = new StringBuilder();
            var isImmutable = true;
            var useSource = false;
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            src.Append("{");

            // test for empty array
            if (tokenizer.LookAhead().Type == TokenType.CloseBrace)
            {
                src.Append(tokenizer.ReadToken().Value); // read }
            }
            else
            {
                while (!tokenizer.CheckEOF())
                {
                    // read simple or complex document key name
                    var innerSrc = new StringBuilder(); // use another builder to re-use in simplified notation
                    var key = ReadKey(tokenizer, innerSrc);

                    src.Append(innerSrc);

                    tokenizer.ReadToken(); // update s.Current 

                    src.Append(":");

                    BsonExpression value;

                    // test normal notation { a: 1 }
                    if (tokenizer.Current.Type == TokenType.Colon)
                    {
                        value = ParseFullExpression(tokenizer, context, parameters, scope);

                        // read next token here (, or }) because simplified version already did
                        tokenizer.ReadToken();
                    }
                    else
                    {
                        var fname = innerSrc.ToString();

                        // support for simplified notation { a, b, c } == { a: $.a, b: $.b, c: $.c }
                        value = new BsonExpression
                        {
                            Type = BsonExpressionType.Path,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                            Parameters = parameters,
#endif
                            IsImmutable = isImmutable,
                            UseSource = useSource,
                            IsScalar = true,
                            Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase).AddRange(new string[] { key }),
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                            FuncScalar = Simplified,
#else
                            Expression = Expression.Call(_memberPathMethod, context.Root, Expression.Constant(key)) as Expression,
#endif
                            Source = "$." + (fname.IsWord() ? fname : "[" + fname + "]")
                        };

#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                        BsonValue Simplified(IEnumerable<BsonDocument> _1, BsonDocument root, BsonValue _3) =>
                            BsonExprInterpreter.MEMBER_PATH(root, key);
#endif
                    }

                    // document value must be a scalar value
                    if (!value.IsScalar) value = ConvertToArray(value);

                    // update isImmutable only when came false
                    if (value.IsImmutable == false) isImmutable = false;
                    if (value.UseSource) useSource = true;

                    fields.AddRange(value.Fields);

                    // add key and value to parameter list (as an expression)
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    keys.Add(key);
                    values.Add(value.FuncScalar);
#else
                    keys.Add(Expression.Constant(key));
                    values.Add(value.Expression);
#endif

                    // include value source in current source
                    src.Append(value.Source);

                    // test next token for , (continue) or } (break)
                    tokenizer.Current.Expect(TokenType.Comma, TokenType.CloseBrace);

                    src.Append(tokenizer.Current.Value);

                    if (tokenizer.Current.Type == TokenType.Comma) continue;
                    break;
                }
            }

#if EXPRESSION_PARSER_ONLY_FOR_INDEX
            var arrKeys = keys.ToArray();
            var arrValues = values.ToArray();
#else
            var arrKeys = Expression.NewArrayInit(typeof(string), keys.ToArray());
            var arrValues = Expression.NewArrayInit(typeof(BsonValue), values.ToArray());
#endif

            return new BsonExpression
            {
                Type = BsonExpressionType.Document,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                Parameters = parameters,
#endif
                IsImmutable = isImmutable,
                UseSource = useSource,
                IsScalar = true,
                Fields = fields,
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                FuncScalar = FuncScalar,
#else
                Expression = Expression.Call(_documentInitMethod, new Expression[] { arrKeys, arrValues }),
#endif
                Source = src.ToString()
            };

            BsonValue FuncScalar(IEnumerable<BsonDocument> source, BsonDocument root, BsonValue current)
            {
                var actualValues = new BsonValue[arrValues.Length];
                for (var i = 0; i < arrValues.Length; i++)
                    actualValues[i] = arrValues[i](source, root, current);
                return BsonExprInterpreter.DOCUMENT_INIT(arrKeys, actualValues);
            }
        }

        /// <summary>
        /// Try parse source documents (when passed) * - return null if not source token
        /// </summary>
        private static BsonExpression TryParseSource(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters, DocumentScope scope)
        {
            if (tokenizer.Current.Type != TokenType.Asterisk) return null;

            var sourceExpr = new BsonExpression
            {
                Type = BsonExpressionType.Source,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                Parameters = parameters,
#endif
                IsImmutable = true,
                UseSource = true,
                IsScalar = false,
                Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "$" },
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                FuncEnumerable = (source, _1, _2) => source,
#else
                Expression = context.Source,
#endif
                Source = "*"
            };

            // checks if next token is "." to shortcut from "*.Name" as "MAP(*, @.Name)"
            if (tokenizer.LookAhead(false).Type == TokenType.Period)
            {
                tokenizer.ReadToken(); // consume .

#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                var pathExpr = BsonExpression.ParseAndCompileSingle(tokenizer, DocumentScope.Source);
#else
                var pathExpr = BsonExpression.ParseAndCompile(tokenizer, BsonExpressionParserMode.Single, parameters, DocumentScope.Source);
#endif

                if (pathExpr == null) throw LiteException.UnexpectedToken(tokenizer.Current);

                return new BsonExpression
                {
                    Type = BsonExpressionType.Map,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                    Parameters = parameters,
#endif
                    IsImmutable = pathExpr.IsImmutable,
                    UseSource = true,
                    IsScalar = false,
                    Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase).AddRange(pathExpr.Fields),
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    FuncEnumerable = (source, root, current) => 
                        BsonExprInterpreter.MAP(root, source, pathExpr),
#else
                    Expression = Expression.Call(BsonExpression.GetFunction("MAP"), context.Root, context.Collation, context.Parameters, sourceExpr.Expression, Expression.Constant(pathExpr)),
#endif
                    Source = "MAP(*=>" + pathExpr.Source + ")"
                };
            }
            else
            {
                return sourceExpr;
            }
        }

        /// <summary>
        /// Try parse array - return null if not array token
        /// </summary>
        private static BsonExpression TryParseArray(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters, DocumentScope scope)
        {
            if (tokenizer.Current.Type != TokenType.OpenBracket) return null;

#if EXPRESSION_PARSER_ONLY_FOR_INDEX
            var values = new List<BsonExpressionScalarDelegate>();
#else
            var values = new List<Expression>();
#endif
            var src = new StringBuilder();
            var isImmutable = true;
            var useSource = false;
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            src.Append("[");

            // test for empty array
            if (tokenizer.LookAhead().Type == TokenType.CloseBracket)
            {
                src.Append(tokenizer.ReadToken().Value); // read ]
            }
            else
            {
                while (!tokenizer.CheckEOF())
                {
                    // read value expression
                    var value = ParseFullExpression(tokenizer, context, parameters, scope);

                    // document value must be a scalar value
                    if (!value.IsScalar) value = ConvertToArray(value);

                    src.Append(value.Source);

                    // update isImmutable only when came false
                    if (value.IsImmutable == false) isImmutable = false;
                    if (value.UseSource) useSource = true;

                    fields.AddRange(value.Fields);

                    // include value source in current source
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    values.Add(value.FuncScalar);
#else
                    values.Add(value.Expression);
#endif

                    var next = tokenizer.ReadToken()
                        .Expect(TokenType.Comma, TokenType.CloseBracket);

                    src.Append(next.Value);

                    if (next.Type == TokenType.Comma) continue;
                    break;
                }
            }

#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
            var arrValues = Expression.NewArrayInit(typeof(BsonValue), values.ToArray());
#endif

            return new BsonExpression
            {
                Type = BsonExpressionType.Array,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                Parameters = parameters,
#endif
                IsImmutable = isImmutable,
                UseSource = useSource,
                IsScalar = true,
                Fields = fields,
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                FuncScalar = FuncScalar,
#else
                Expression = Expression.Call(_arrayInitMethod, arrValues),
#endif
                Source = src.ToString()
            };
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
            BsonValue FuncScalar(IEnumerable<BsonDocument> source, BsonDocument root, BsonValue current)
            {
                var array = new BsonValue[values.Count];
                for (var i = 0; i < values.Count; i++)
                    array[i] = values[i](source, root, current);
                return new BsonArray(array);
            }
#endif
        }

        /// <summary>
        /// Try parse parameter - return null if not parameter token
        /// </summary>
        private static BsonExpression TryParseParameter(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters, DocumentScope scope)
        {
            if (tokenizer.Current.Type != TokenType.At) return null;

            var ahead = tokenizer.LookAhead(false);

            if (ahead.Type == TokenType.Word || ahead.Type == TokenType.Int)
            {
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                throw Unsupported.ParametersInExpression;
#else
                var parameterName = tokenizer.ReadToken(false).Value;
                var name = Expression.Constant(parameterName);

                return new BsonExpression
                {
                    Type = BsonExpressionType.Parameter,
                    Parameters = parameters,
                    IsImmutable = false,
                    UseSource = false,
                    IsScalar = true,
                    Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    Expression = Expression.Call(_parameterPathMethod, context.Parameters, name),
                    Source = "@" + parameterName
                };
#endif
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Try parse inner expression - return null if not bracket token
        /// </summary>
        private static BsonExpression TryParseInnerExpression(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters, DocumentScope scope)
        {
            if (tokenizer.Current.Type != TokenType.OpenParenthesis) return null;

            // read a inner expression inside ( and )
            var inner = ParseFullExpression(tokenizer, context, parameters, scope);

            // read close )
            tokenizer.ReadToken().Expect(TokenType.CloseParenthesis);

            return new BsonExpression
            {
                Type = inner.Type,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                Parameters = inner.Parameters,
#endif
                IsImmutable = inner.IsImmutable,
                UseSource = inner.UseSource,
                IsScalar = inner.IsScalar,
                Fields = inner.Fields,
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                FuncScalar = inner.FuncScalar,
                FuncEnumerable = inner.FuncEnumerable,
#else
                Expression = inner.Expression,
#endif
                Left = inner.Left,
                Right = inner.Right,
                Source = "(" + inner.Source + ")"
            };
        }

        /// <summary>
        /// Try parse method call - return null if not method call
        /// </summary>
        private static BsonExpression TryParseMethodCall(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters, DocumentScope scope)
        {
            var token = tokenizer.Current;

            if (tokenizer.Current.Type != TokenType.Word) return null;
            if (tokenizer.LookAhead().Type != TokenType.OpenParenthesis) return null;
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
            throw Unsupported.FunctionsInExpression;
#else

            // read (
            tokenizer.ReadToken();

            // get static method from this class
            var pars = new List<BsonExpression>();
            var src = new StringBuilder();
            var isImmutable = true;
            var useSource = false;
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            src.Append(token.Value.ToUpperInvariant() + "(");

            // method call with no parameters
            if (tokenizer.LookAhead().Type == TokenType.CloseParenthesis)
            {
                src.Append(tokenizer.ReadToken().Value); // read )
            }
            else
            {
                while (!tokenizer.CheckEOF())
                {
                    var parameter = ParseFullExpression(tokenizer, context, parameters, scope);

                    // update isImmutable only when came false
                    if (parameter.IsImmutable == false) isImmutable = false;
                    if (parameter.UseSource) useSource = true;

                    // add fields from each parameters
                    fields.AddRange(parameter.Fields);

                    pars.Add(parameter);

                    // append source string
                    src.Append(parameter.Source);

                    // read , or )
                    var next = tokenizer.ReadToken()
                        .Expect(TokenType.Comma, TokenType.CloseParenthesis);

                    src.Append(next.Value);

                    if (next.Type == TokenType.Comma) continue;
                    break;
                }
            }

            var method = BsonExpression.GetMethod(token.Value, pars.Count);

            if (method == null) throw LiteException.UnexpectedToken($"Method '{token.Value.ToUpperInvariant()}' does not exist or contains invalid parameters", token);

            // test if method are decorated with "Variable" (immutable = false)
            if (method.GetCustomAttribute<VolatileAttribute>() != null)
            {
                isImmutable = false;
            }

            // method call arguments
            var args = new List<Expression>();

            if (method.GetParameters().FirstOrDefault()?.ParameterType == typeof(Collation))
            {
                args.Add(context.Collation);
            }

            // getting linq expression from BsonExpression for all parameters
            foreach (var item in method.GetParameters().Where(x => x.ParameterType != typeof(Collation)).Zip(pars, (parameter, expr) => new { parameter, expr }))
            {
                if (item.parameter.ParameterType.IsEnumerable() == false && item.expr.IsScalar == false)
                {
                    // convert enumerable expresion into scalar expression
                    args.Add(ConvertToArray(item.expr).Expression);
                }
                else if (item.parameter.ParameterType.IsEnumerable() && item.expr.IsScalar)
                {
                    // convert scalar expression into enumerable expression
                    args.Add(ConvertToEnumerable(item.expr).Expression);
                }
                else
                {
                    args.Add(item.expr.Expression);
                }
            }

            // special IIF case
            if (method.Name == "IIF" && pars.Count == 3) return CreateConditionalExpression(pars[0], pars[1], pars[2]);

            return new BsonExpression
            {
                Type = BsonExpressionType.Call,
                Parameters = parameters,
                IsImmutable = isImmutable,
                UseSource = useSource,
                IsScalar = method.ReturnType.IsEnumerable() == false,
                Fields = fields,
                Expression = Expression.Call(method, args.ToArray()),
                Source = src.ToString()
            };
#endif
        }

        /// <summary>
        /// Parse JSON-Path - return null if not method call
        /// </summary>
        private static BsonExpression TryParsePath(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters, DocumentScope scope)
        {
            // test $ or @ or WORD
            if (tokenizer.Current.Type != TokenType.At && tokenizer.Current.Type != TokenType.Dollar && tokenizer.Current.Type != TokenType.Word) return null;

            var defaultScope = (scope == DocumentScope.Root ? TokenType.Dollar : TokenType.At);

            if (tokenizer.Current.Type == TokenType.At || tokenizer.Current.Type == TokenType.Dollar)
            {
                defaultScope = tokenizer.Current.Type;

                var ahead = tokenizer.LookAhead(false);

                if (ahead.Type == TokenType.Period)
                {
                    tokenizer.ReadToken(); // read .
                    tokenizer.ReadToken(); // read word or [
                }
            }

            var src = new StringBuilder();
            var isImmutable = true;
            var useSource = false;
            var isScalar = true;
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            src.Append(defaultScope == TokenType.Dollar ? "$" : "@");

            // read field name (or "" if root)
            var field = ReadField(tokenizer, src);
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
            BsonExpressionScalarDelegate expr = (source, root, current) => 
                BsonExprInterpreter.MEMBER_PATH(defaultScope == TokenType.Dollar ? root : current, field);
            BsonExpressionEnumerableDelegate exprEnumerable = null;
#else
            var name = Expression.Constant(field);
            var expr = Expression.Call(_memberPathMethod, defaultScope == TokenType.Dollar ? context.Root : context.Current, name) as Expression;
#endif

            // add as field only if working with root document (or source root)
            if (defaultScope == TokenType.Dollar || scope == DocumentScope.Source)
            {
                fields.Add(field.Length == 0 ? "$" : field);
            }

            // parse the rest of path
            while (!tokenizer.EOF)
            {
                var result = ParsePath(tokenizer, expr, context, parameters, fields, ref isImmutable, ref useSource, ref isScalar, src);

                if (isScalar == false)
                {
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    exprEnumerable = (BsonExpressionEnumerableDelegate)result;
                    expr = null;
#else
                    expr = result;
#endif
                    break;
                }

                // filter method must exit
                if (result == null) break;

#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                expr = (BsonExpressionScalarDelegate)result;
#else
                expr = result;
#endif
            }

            var pathExpr = new BsonExpression
            {
                Type = BsonExpressionType.Path,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                Parameters = parameters,
#endif
                IsImmutable = isImmutable,
                UseSource = useSource,
                IsScalar = isScalar,
                Fields = fields,
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                FuncScalar = expr,
                FuncEnumerable = exprEnumerable,
#else
                Expression = expr,
#endif
                Source = src.ToString()
            };

            // if expr is enumerable and next token is . translate do MAP
            if (isScalar == false && tokenizer.LookAhead(false).Type == TokenType.Period)
            {
                tokenizer.ReadToken(); // consume .

#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                var mapExpr = BsonExpression.ParseAndCompileSingle(tokenizer, DocumentScope.Current);
#else
                var mapExpr = BsonExpression.ParseAndCompile(tokenizer, BsonExpressionParserMode.Single, parameters, DocumentScope.Current);
#endif

                if (mapExpr == null) throw LiteException.UnexpectedToken(tokenizer.Current);

                return new BsonExpression
                {
                    Type = BsonExpressionType.Map,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                    Parameters = parameters,
#endif
                    IsImmutable = pathExpr.IsImmutable && mapExpr.IsImmutable,
                    UseSource = pathExpr.UseSource || mapExpr.UseSource,
                    IsScalar = false,
                    Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase).AddRange(pathExpr.Fields).AddRange(mapExpr.Fields),
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    FuncEnumerable = (source, root, current) =>
                        BsonExprInterpreter.MAP(root, exprEnumerable(source, root, current), mapExpr),
#else
                    Expression = Expression.Call(BsonExpression.GetFunction("MAP"), context.Root, context.Collation, context.Parameters, pathExpr.Expression, Expression.Constant(mapExpr)),
#endif
                    Source = "MAP(" + pathExpr.Source + "=>" + mapExpr.Source + ")"
                };
            }
            else
            {
                return pathExpr;
            }
        }

        /// <summary>
        /// Implement a JSON-Path like navigation on BsonDocument. Support a simple range of paths
        /// </summary>
        private static
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
            Delegate
#else
            Expression
#endif
            ParsePath(Tokenizer tokenizer, 
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                BsonExpressionScalarDelegate expr,
#else
                Expression expr,
#endif
                ExpressionContext context, BsonDocument parameters, HashSet<string> fields, ref bool isImmutable, ref bool useSource, ref bool isScalar, StringBuilder src)
        {
            var ahead = tokenizer.LookAhead(false);

            if (ahead.Type == TokenType.Period)
            {
                tokenizer.ReadToken(); // read .
                tokenizer.ReadToken(false); //

                var field = ReadField(tokenizer, src);

#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                BsonExpressionScalarDelegate funcScalar = (source, root, current) =>
                    BsonExprInterpreter.MEMBER_PATH(expr(source, root, current), field);
                return funcScalar;
#else
                var name = Expression.Constant(field);

                return Expression.Call(_memberPathMethod, expr, name);
#endif
            }
            else if (ahead.Type == TokenType.OpenBracket) // array 
            {
                src.Append("[");

                tokenizer.ReadToken(); // read [

                ahead = tokenizer.LookAhead(); // look for "index" or "expression"

                var index = 0;
                var inner = new BsonExpression();
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                var method = _arrayIndexMethod;
#endif
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                Delegate result;
#endif

                if (ahead.Type == TokenType.Int)
                {
                    // fixed index
                    src.Append(tokenizer.ReadToken().Value);
                    index = Convert.ToInt32(tokenizer.Current.Value);
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    BsonExpressionScalarDelegate funcScalar = (source, root, current) =>
                        BsonExprInterpreter.ARRAY_CONST_INDEX(expr(source, root, current), index);
                    result = funcScalar;
#endif
                }
                else if (ahead.Type == TokenType.Minus)
                {
                    // fixed negative index
                    src.Append(tokenizer.ReadToken().Value + tokenizer.ReadToken().Expect(TokenType.Int).Value);
                    index = -Convert.ToInt32(tokenizer.Current.Value);
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    BsonExpressionScalarDelegate funcScalar = (source, root, current) =>
                        BsonExprInterpreter.ARRAY_CONST_INDEX(expr(source, root, current), index);
                    result = funcScalar;
#endif
                }
                else if (ahead.Type == TokenType.Asterisk)
                {
                    // all items * (index = MaxValue)
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                    method = _arrayFilterMethod;
#endif
                    isScalar = false;
                    index = int.MaxValue;

                    src.Append(tokenizer.ReadToken().Value);
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    BsonExpressionEnumerableDelegate funcEnumerable = (source, root, current) =>
                        BsonExprInterpreter.ARRAY_ALL(expr(source, root, current));
                    result = funcEnumerable;
#endif
                }
                else
                {
                    // inner expression
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    inner = BsonExpression.ParseAndCompileFull(tokenizer, DocumentScope.Current);
#else
                    inner = BsonExpression.ParseAndCompile(tokenizer, BsonExpressionParserMode.Full, parameters, DocumentScope.Current);
#endif

                    if (inner == null) throw LiteException.UnexpectedToken(tokenizer.Current);

                    // if array filter is not immutable, update ref (update only when false)
                    if (inner.IsImmutable == false) isImmutable = false;
                    if (inner.UseSource) useSource = true;

                    // if inner expression returns a single parameter, still Scalar
                    // otherwise it's an operand filter expression (enumerable)
                    if (inner.Type != BsonExpressionType.Parameter)
                    {
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                        method = _arrayFilterMethod;
#endif
                        isScalar = false;
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                        BsonExpressionEnumerableDelegate funcEnumerable = (source, root, current) =>
                            BsonExprInterpreter.ARRAY_FILTER(expr(source, root, current), inner, root);
                        result = funcEnumerable;
#endif
                    }
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                    else
                    {
                        BsonExpressionScalarDelegate funcEnumerable = (source, root, current) =>
                            BsonExprInterpreter.ARRAY_INDEX(expr(source, root, current), inner, root);
                        result = funcEnumerable;
                    }
#endif

                    // add inner fields (can contains root call)
                    fields.AddRange(inner.Fields);

                    src.Append(inner.Source);
                }

                // read ]
                tokenizer.ReadToken().Expect(TokenType.CloseBracket);

                src.Append("]");

#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                return result;
#else
                return Expression.Call(method, expr, Expression.Constant(index), Expression.Constant(inner), context.Root, context.Collation, context.Parameters);
#endif
            }

            return null;
        }

        /// <summary>
        /// Try parse FUNCTION methods: MAP, FILTER, SORT, ...
        /// </summary>
        private static BsonExpression TryParseFunction(Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters, DocumentScope scope)
        {
            if (tokenizer.Current.Type != TokenType.Word) return null;
            if (tokenizer.LookAhead().Type != TokenType.OpenParenthesis) return null;

            var token = tokenizer.Current.Value.ToUpperInvariant();

            switch (token)
            {
                case "MAP": return ParseFunction(token, BsonExpressionType.Map, tokenizer, context, parameters, scope);
                case "FILTER": return ParseFunction(token, BsonExpressionType.Filter, tokenizer, context, parameters, scope);
                case "SORT": return ParseFunction(token, BsonExpressionType.Sort, tokenizer, context, parameters, scope);
            }

            return null;
        }

        /// <summary>
        /// Parse expression functions, like MAP, FILTER or SORT.
        /// MAP(items[*] => @.Name)
        /// </summary>
        private static BsonExpression ParseFunction(string functionName, BsonExpressionType type, Tokenizer tokenizer, ExpressionContext context, BsonDocument parameters, DocumentScope scope)
        {
            // check if next token are ( otherwise returns null (is not a function)
            if (tokenizer.LookAhead().Type != TokenType.OpenParenthesis) return null;

#if EXPRESSION_PARSER_ONLY_FOR_INDEX
            throw Unsupported.FunctionsInExpression;
#else

            // read (
            tokenizer.ReadToken().Expect(TokenType.OpenParenthesis);

            var left = ParseSingleExpression(tokenizer, context, parameters, scope);

            // if left is a scalar expression, convert into enumerable expression (avoid to use [*] all the time)
            if (left.IsScalar)
            {
                left = ConvertToEnumerable(left);
            }

            var args = new List<Expression>();
            args.Add(context.Root);
            args.Add(context.Collation);
            args.Add(context.Parameters);

            var src = new StringBuilder(functionName + "(" + left.Source);
            var isImmutable = left.IsImmutable;
            var useSource = left.UseSource;
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            args.Add(left.Expression);
            fields.AddRange(left.Fields);

            // read =>
            if (tokenizer.LookAhead().Type == TokenType.Equals)
            {
                tokenizer.ReadToken().Expect(TokenType.Equals);
                tokenizer.ReadToken().Expect(TokenType.Greater);

                var right = BsonExpression.ParseAndCompile(tokenizer, BsonExpressionParserMode.Full, parameters,
                    left.Type == BsonExpressionType.Source ? DocumentScope.Source : DocumentScope.Current);

                src.Append("=>" + right.Source);
                args.Add(Expression.Constant(right));
                fields.AddRange(right.Fields);
            }

            if (tokenizer.LookAhead().Type != TokenType.CloseParenthesis)
            {
                tokenizer.ReadToken().Expect(TokenType.Comma);

                src.Append(",");

                // try more parameters ,
                while (!tokenizer.CheckEOF())
                {
                    var parameter = ParseFullExpression(tokenizer, context, parameters, scope);

                    // update isImmutable only when came false
                    if (parameter.IsImmutable == false) isImmutable = false;
                    if (parameter.UseSource) useSource = true;

                    args.Add(parameter.Expression);
                    src.Append(parameter.Source);
                    fields.AddRange(parameter.Fields);

                    if (tokenizer.LookAhead().Type == TokenType.Comma)
                    {
                        src.Append(tokenizer.ReadToken().Value);
                        continue;
                    }
                    break;
                }
            }

            // read )
            tokenizer.ReadToken().Expect(TokenType.CloseParenthesis);
            src.Append(")");

            var method = BsonExpression.GetFunction(functionName, args.Count - 5);

            return new BsonExpression
            {
                Type = type,
                Parameters = parameters,
                IsImmutable = isImmutable,
                UseSource = useSource,
                IsScalar = false,
                Fields = fields,
                Expression = Expression.Call(method, args.ToArray()),
                Source = src.ToString()
            };
#endif
        }

#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
        /// <summary>
        /// Create an array expression with 2 values (used only in BETWEEN statement)
        /// </summary>
        private static BsonExpression NewArray(BsonExpression item0, BsonExpression item1)
        {
            var values = new Expression[] { item0.Expression, item1.Expression };

            // both values must be scalar expressions
            if (item0.IsScalar == false) throw new LiteException(0, $"Expression `{item0.Source}` must be a scalar expression");
            if (item1.IsScalar == false) throw new LiteException(0, $"Expression `{item0.Source}` must be a scalar expression");

            var arrValues = Expression.NewArrayInit(typeof(BsonValue), values.ToArray());

            return new BsonExpression
            {
                Type = BsonExpressionType.Array,
                Parameters = item0.Parameters, // should be == item1.Parameters
                IsImmutable = item0.IsImmutable && item1.IsImmutable,
                UseSource = item0.UseSource || item1.UseSource,
                IsScalar = true,
                Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase).AddRange(item0.Fields).AddRange(item1.Fields),
                Expression = Expression.Call(_arrayInitMethod, new Expression[] { arrValues }),
                Source = item0.Source + " AND " + item1.Source
            };
        }
#endif

        /// <summary>
        /// Get field from simple \w regex or ['comp-lex'] - also, add into source. Can read empty field (root)
        /// </summary>
        private static string ReadField(Tokenizer tokenizer, StringBuilder source)
        {
            var field = "";

            // if field are complex
            if (tokenizer.Current.Type == TokenType.OpenBracket)
            {
                field = tokenizer.ReadToken().Expect(TokenType.String).Value;
                tokenizer.ReadToken().Expect(TokenType.CloseBracket);
            }
            else if (tokenizer.Current.Type == TokenType.Word)
            {
                field = tokenizer.Current.Value;
            }

            if (field.Length > 0)
            {
                source.Append(".");

                // add bracket in result only if is complex type
                if (field.IsWord())
                {
                    source.Append(field);
                }
                else
                {
                    source.Append("[");
                    JsonSerializer.Serialize(field, source);
                    source.Append("]");
                }
            }

            return field;
        }

        /// <summary>
        /// Read key in document definition with single word or "comp-lex"
        /// </summary>
        public static string ReadKey(Tokenizer tokenizer, StringBuilder source)
        {
            var token = tokenizer.ReadToken();
            var key = "";

            if (token.Type == TokenType.String)
            {
                key = token.Value;
            }
            else
            {
                key = token.Expect(TokenType.Word, TokenType.Int).Value;
            }

            if (key.IsWord())
            {
                source.Append(key);
            }
            else
            {
                JsonSerializer.Serialize(key, source);
            }

            return key;
        }

        /// <summary>
        /// Read next token as Operant with ANY|ALL keyword before - returns null if next token are not an operant
        /// </summary>
        private static string ReadOperant(Tokenizer tokenizer)
        {
            var token = tokenizer.LookAhead(true);

            if (token.IsOperand)
            {
                tokenizer.ReadToken(); // consume operant

                return token.Value;
            }

            if (token.Is("ALL") || token.Is("ANY"))
            {
                var key = token.Value.ToUpperInvariant();

                tokenizer.ReadToken(); // consume operant

                token = tokenizer.ReadToken();

                if (token.IsOperand == false) throw LiteException.UnexpectedToken("Expected valid operand", token);

                return key + " " + token.Value;
            }

            return null;
        }

#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
        /// <summary>
        /// Convert scalar expression into enumerable expression using ITEMS(...) method
        /// Append [*] to path or ITEMS(..) in all others
        /// </summary>
        private static BsonExpression ConvertToEnumerable(BsonExpression expr)
        {
            var src = expr.Type == BsonExpressionType.Path ?
                expr.Source + "[*]" :
                "ITEMS(" + expr.Source + ")";

            var exprType = expr.Type == BsonExpressionType.Path ?
                BsonExpressionType.Path :
                BsonExpressionType.Call;

            return new BsonExpression
            {
                Type = exprType,
                Parameters = expr.Parameters,
                IsImmutable = expr.IsImmutable,
                UseSource = expr.UseSource,
                IsScalar = false,
                Fields = expr.Fields,
                Expression = Expression.Call(_itemsMethod, expr.Expression),
                Source = src
            };
        }
#endif

        /// <summary>
        /// Convert enumerable expression into array using ARRAY(...) method
        /// </summary>
        private static BsonExpression ConvertToArray(BsonExpression expr)
        {
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
            DEBUG(!expr.IsScalar);
            var func = expr.FuncEnumerable;
#endif
            return new BsonExpression
            {
                Type = BsonExpressionType.Call,
#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
                Parameters = expr.Parameters,
#endif
                IsImmutable = expr.IsImmutable,
                UseSource = expr.UseSource,
                IsScalar = true,
                Fields = expr.Fields,
#if EXPRESSION_PARSER_ONLY_FOR_INDEX
                FuncScalar = (source, root, current) => 
                    new BsonArray(func(source, root, current)),
#else
                Expression = Expression.Call(_arrayMethod, expr.Expression),
#endif
                Source = "ARRAY(" + expr.Source + ")"
            };
        }

#if !EXPRESSION_PARSER_ONLY_FOR_INDEX
        /// <summary>
        /// Create new logic (AND/OR) expression based in 2 expressions
        /// </summary>
        internal static BsonExpression CreateLogicExpression(BsonExpressionType type, BsonExpression left, BsonExpression right)
        {
            // convert BsonValue into Boolean
            var boolLeft = Expression.Property(left.Expression, typeof(BsonValue), "AsBoolean");
            var boolRight = Expression.Property(right.Expression, typeof(BsonValue), "AsBoolean");

            var expr = type == BsonExpressionType.And ?
                Expression.AndAlso(boolLeft, boolRight) :
                Expression.OrElse(boolLeft, boolRight);

            // and convert back Boolean to BsonValue
            var ctor = typeof(BsonValue)
                .GetConstructors()
                .First(x => x.GetParameters().FirstOrDefault()?.ParameterType == typeof(bool));

            // create new binary expression based in 2 other expressions
            var result = new BsonExpression
            {
                Type = type,
                Parameters = left.Parameters, // should be == right.Parameters
                IsImmutable = left.IsImmutable && right.IsImmutable,
                UseSource = left.UseSource || right.UseSource,
                IsScalar = left.IsScalar && right.IsScalar,
                Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase).AddRange(left.Fields).AddRange(right.Fields),
                Expression = Expression.New(ctor, expr),
                Left = left,
                Right = right,
                Source = left.Source + " " + (type.ToString().ToUpperInvariant()) + " " + right.Source
            };

            return result;
        }

        /// <summary>
        /// Create new conditional (IIF) expression. Execute expression only if True or False value
        /// </summary>
        internal static BsonExpression CreateConditionalExpression(BsonExpression test, BsonExpression ifTrue, BsonExpression ifFalse)
        {
            // convert BsonValue into Boolean
            var boolTest = Expression.Property(test.Expression, typeof(BsonValue), "AsBoolean");

            var expr = Expression.Condition(boolTest, ifTrue.Expression, ifFalse.Expression);

            // create new binary expression based in 2 other expressions
            var result = new BsonExpression
            {
                Type = BsonExpressionType.Call, // there is not specific Conditional
                Parameters = test.Parameters, // should be == ifTrue|ifFalse parameters
                IsImmutable = test.IsImmutable && ifTrue.IsImmutable || ifFalse.IsImmutable,
                UseSource = test.UseSource || ifTrue.UseSource || ifFalse.UseSource,
                IsScalar = test.IsScalar && ifTrue.IsScalar && ifFalse.IsScalar,
                Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase).AddRange(test.Fields).AddRange(ifTrue.Fields).AddRange(ifFalse.Fields),
                Expression = expr,
                Source = "IIF(" + test.Source + "," + ifTrue.Source + "," + ifFalse.Source + ")"
            };

            return result;
        }
#endif
    }
}