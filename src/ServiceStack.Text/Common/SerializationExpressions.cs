// SerializationExpressions.cs
// 
// Copyright (c) 2012 Mike Strobel
// 
// This source code is subject to the terms of the Microsoft Reciprocal License (Ms-RL).
// For details, see <http://www.opensource.org/licenses/ms-rl.html>.
// 
// All other rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Xml.Schema;

using StrobelStack.Text.Json;
using StrobelStack.Text.Jsv;
using StrobelStack.Text.Reflection;

// ReSharper disable PossiblyMistakenUseOfParamsMethod

namespace StrobelStack.Text.Common
{
    internal static class SerializationExpressions
    {
        internal static readonly Expression NullLiteral = Expression.Constant(JsonUtils.Null);
        internal static readonly Expression QuoteLiteral = Expression.Constant(JsWriter.QuoteChar);
        internal static readonly Expression ListStartLiteral = Expression.Constant(JsWriter.ListStartChar);
        internal static readonly Expression ListEndLiteral = Expression.Constant(JsWriter.ListEndChar);
        internal static readonly Expression MapStartLiteral = Expression.Constant(JsWriter.MapStartChar);
        internal static readonly Expression MapEndLiteral = Expression.Constant(JsWriter.MapEndChar);
        internal static readonly Expression MapKeySeparatorLiteral = Expression.Constant(JsWriter.MapKeySeperator);
        internal static readonly Expression ItemSeparatorLiteral = Expression.Constant(JsWriter.ItemSeperator);
        internal static readonly Expression TrueLiteral = Expression.Constant("True");
        internal static readonly Expression FalseLiteral = Expression.Constant("False");
        internal static readonly Expression TrueConstant = Expression.Constant(true);
        internal static readonly Expression FalseConstant = Expression.Constant(false);

        internal static readonly MethodInfo TextWriterWriteStringMethod = typeof(TextWriter).GetMethod("Write", new[] { typeof(string) });
        internal static readonly MethodInfo WriteStringMethod = typeof(JsonUtils).GetMethod("WriteString");
        internal static readonly MethodInfo GetTypeMethod = typeof(object).GetMethod("GetType");
        internal static readonly MethodInfo ToStringMethod = typeof(object).GetMethod("ToString");
        internal static readonly MethodInfo EqualsMethod = typeof(object).GetMethod("Equals", new[] { typeof(object) });

        private static readonly Expression NullDelegate = Expression.Empty();

        private static Dictionary<Type, Expression> ValueTypeSerializerCache = new Dictionary<Type, Expression>();
        private static Dictionary<Type, Expression> SpecialSerializerCache = new Dictionary<Type, Expression>();

        private static readonly MethodInfo LateBoundSerializeMethod = typeof(SerializationExpressions).GetMethod(
            "LateBoundSerialize",
            BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly MethodInfo GetCustomValueTypeToStringCallbackMethod = typeof(SerializationExpressions).GetMethod(
            "GetCustomValueTypeToStringCallback",
            BindingFlags.Static | BindingFlags.NonPublic);

        internal static Expression MakeTextWriterCall(Expression writer, Expression value, bool quoteStrings = true)
        {
            if (quoteStrings && value.Type == typeof(string))
            {
                if (JsState.IsJson)
                {
                    return Expression.Call(
                        null,
                        WriteStringMethod,
                        writer,
                        value,
                        Expression.Constant(JsState.IsJson));
                }

                value = Expression.Call(
                    typeof(TextExtensions),
                    "ToCsvField",
                    Type.EmptyTypes,
                    value);
            }

            return Expression.Call(writer, TypeWriterMethods.GetWriteMethod(value.Type), value);
        }

        public static Expression BuildSerializeExpression(Expression writer, Expression value, bool writeExplicitNull = false)
        {
            var type = value.Type;

            if (type.IsNullableType())
            {
                var temp = Expression.Variable(type, "temp");

                /*
                 * temp = $value
                 * if (temp.HasValue)
                 *     $serialize(temp)
                 * else
                 *     writer.Write("null")
                 */
                if (writeExplicitNull)
                {
                    return Expression.Block(
                        new[] { temp },
                        Expression.Assign(temp, value),
                        Expression.IfThenElse(
                            Expression.Property(temp, "HasValue"),
                            BuildSerializeNonNullableValueExpression(writer, Expression.Property(temp, "Value")),
                            BuildSerializeNullExpression(writer)));
                }
             
                return Expression.Block(
                    new[] { temp },
                    Expression.Assign(temp, value),
                    Expression.IfThen(
                        Expression.Property(temp, "HasValue"),
                        BuildSerializeNonNullableValueExpression(writer, Expression.Property(temp, "Value"))));
            }

            if (!type.IsValueType)
            {
                var temp = Expression.Variable(type, "temp");

                /*
                 * temp = $value
                 * if (temp == null)
                 *     writer.Write("null")
                 * else
                 *     $serialize(temp)
                 */

                Expression expression;

                if (writeExplicitNull)
                {
                    expression = Expression.Block(
                        new[] { temp },
                        Expression.Assign(temp, value),
                        Expression.IfThenElse(
                            Expression.ReferenceNotEqual(temp, Expression.Default(type)),
                            BuildSerializeNonNullableValueExpression(writer, temp),
                            BuildSerializeNullExpression(writer)));
                }
                else
                {
                    expression = Expression.Block(
                        new[] { temp },
                        Expression.Assign(temp, value),
                        Expression.IfThen(
                            Expression.ReferenceNotEqual(temp, Expression.Default(type)),
                            BuildSerializeNonNullableValueExpression(writer, temp)));
                }
                return expression;
            }
            else
            {
                var temp = Expression.Variable(type, "temp");

                return Expression.Block(
                    new[] { temp },
                    Expression.Assign(temp, value),
                    BuildSerializeNonNullableValueExpression(writer, temp));
            }
        }

        private static Expression BuildSerializeNonNullableValueExpression(Expression writer, Expression value)
        {
            var type = value.Type;
            if (type.IsArray)
            {
                if (type == typeof(byte[]))
                    return BuildSerializeBlobExpression(writer, value);

                return BuildSerializeArrayExpression(writer, value);
            }

            var constant = value as ConstantExpression;
            if (constant != null && constant.Value == null)
                return BuildSerializeNullExpression(writer);

            if (type.IsEnum)
                return BuildSerializeValueTypeExpression(writer, value);

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Object:
                    if (type == typeof(object))
                        return BuildSerializeLateBoundObjectExpression(writer, value);

                    if (type == typeof(DateTimeOffset))
                        return BuildSerializeDateTimeOffsetExpression(writer, value);

                    if (type == typeof(Guid))
                        return BuildSerializeGuidExpression(writer, value);

                    if (type == typeof(TimeSpan))
                        return BuildSerializeTimeSpanExpression(writer, value);

                    if (typeof(Exception).IsAssignableFrom(type))
                        return BuildSerializeExceptionExpression(writer, value);

                    if (typeof(Type).IsAssignableFrom(type))
                        return BuildSerializeTypeExpression(writer, value);

                    if (type == typeof(System.Data.Linq.Binary))
                        return BuildSerializeExpression(writer, Expression.Call(value, "ToArray", Type.EmptyTypes));

                    Expression specialExpression;

                    if (type.IsValueType && !JsConfig.TreatAsRefType(type))
                    {
                        if (TryBuildSpecialSerializeFunctionExpresion(writer, value, out specialExpression))
                            return specialExpression;

                        return BuildSerializeValueTypeExpression(writer, value);
                    }

                    Expression collectionExpression;

                    if (TryBuildSerializeCollectionExpression(writer, value, out collectionExpression))
                        return collectionExpression;

                    if (TryBuildSpecialSerializeFunctionExpresion(writer, value, out specialExpression))
                        return specialExpression;

                    return BuildSerializeComplexObjectExpression(writer, value);

                case TypeCode.DBNull:
                    return BuildSerializeNullExpression(writer);

                case TypeCode.Boolean:
                    return BuildSerializeBooleanExpression(writer, value);

                case TypeCode.Char:
                    return BuildSerializeCharacterExpression(writer, value);

                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                    value = Expression.Convert(value, typeof(int));
                    type = typeof(int);
                    goto case TypeCode.Int32;

                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return Expression.Call(writer, TypeWriterMethods.GetWriteMethod(type), value);

                case TypeCode.Single:
                case TypeCode.Double:
                    return BuildSerializeFloatingPointExpression(writer, value);

                case TypeCode.Decimal:
                    return Expression.Call(writer, TypeWriterMethods.GetWriteMethod(type), value);

                case TypeCode.DateTime:
                    return BuildSerializeDateTimeExpression(writer, value);

                case TypeCode.String:
                    return BuildSerializeStringExpression(writer, value);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static bool TryBuildSpecialSerializeFunctionExpresion(Expression writer, Expression value, out Expression specialExpression)
        {
            var type = value.Type;

            Expression specialFunction;

            var isJson = JsState.IsJson;

            if (!SpecialSerializerCache.TryGetValue(type, out specialFunction))
            {
                var fn = isJson ? JsonWriter.Instance.GetSpecialWriteFn(type) : JsvWriter.Instance.GetSpecialWriteFn(type);

                if (fn == null)
                    specialFunction = NullDelegate;
                else
                    specialFunction = Expression.Constant(fn);

                Dictionary<Type, Expression> oldCache;
                Dictionary<Type, Expression> newCache;

                do
                {
                    oldCache = SpecialSerializerCache;
                    newCache = new Dictionary<Type, Expression>(oldCache);
                    newCache[type] = specialFunction;
                }
                while (!ReferenceEquals(Interlocked.CompareExchange(ref SpecialSerializerCache, newCache, oldCache), oldCache));
            }

            if (specialFunction != NullDelegate)
            {
                specialExpression = Expression.Invoke(
                    specialFunction,
                    writer,
                    value.Type == typeof(object) ? value : Expression.Convert(value, typeof(object)));

                return true;
            }

            specialExpression = null;
            return false;
        }

        private static Func<T, string> GetCustomValueTypeToStringCallback<T>()
        {
            return JsConfig<T>.SerializeFn;
        }

        private static Expression BuildSerializeValueTypeExpression(Expression writer, Expression value)
        {
            Func<DateTime, string> s = time => time.ToString();

            var type = value.Type;

            if (!JsConfig.TreatAsRefType(type))
            {
                Expression makeTextWriterCall;
                if (TryBuildSerializeExpressionFromConfig(writer, value, type, out makeTextWriterCall))
                    return makeTextWriterCall;
            }

            if (type.IsEnum)
                return BuildSerializeEnumExpression(writer, value);

            return BuildSerializeStringExpression(
                writer,
                Expression.Call(
                    value,
                    ToStringMethod));
/*
            Expression defaultExpression;

            if (type.IsEnum)
            {
                defaultExpression = BuildSerializeEnumExpression(writer, value);
            }
            else
            {
                defaultExpression = BuildSerializeStringExpression(
                    writer,
                    Expression.Call(
                        value,
                        ToStringMethod));
            }

            var customSerializer = Expression.Variable(typeof(Func<,>).MakeGenericType(type, typeof(string)));

            return Expression.Block(
                new[] { customSerializer },
                Expression.Assign(
                    customSerializer,
                    Expression.Property(
                        null,
                        typeof(JsConfig<>).MakeGenericType(type),
                        "SerializeFn")),
                Expression.IfThenElse(
                    Expression.ReferenceNotEqual(
                        customSerializer,
                        Expression.Default(customSerializer.Type)),
                    MakeTextWriterCall(
                        writer,
                        Expression.Invoke(customSerializer, value)),
                    defaultExpression));
*/
        }

        private static bool TryBuildSerializeExpressionFromConfig(Expression writer, Expression value, Type type, out Expression makeTextWriterCall)
        {
            Expression customCallback;

            if (!ValueTypeSerializerCache.TryGetValue(type, out customCallback))
            {
                var fnField = typeof(JsConfig<>).MakeGenericType(type).GetProperty(
                    "SerializeFn",
                    BindingFlags.Static | BindingFlags.Public);

                var fn = fnField.GetValue(null, null);
                if (fn == null)
                    customCallback = NullDelegate;
                else
                    customCallback = Expression.Constant(fn);

                Dictionary<Type, Expression> oldCache;
                Dictionary<Type, Expression> newCache;

                do
                {
                    oldCache = ValueTypeSerializerCache;
                    newCache = new Dictionary<Type, Expression>(oldCache);
                    newCache[type] = customCallback;
                }
                while (!ReferenceEquals(Interlocked.CompareExchange(ref ValueTypeSerializerCache, newCache, oldCache), oldCache));
            }

            if (customCallback != NullDelegate)
            {
                makeTextWriterCall = MakeTextWriterCall(
                    writer,
                    Expression.Invoke(customCallback, value));

                return true;
            }

            makeTextWriterCall = null;
            return false;
        }

        private static Expression BuildSerializeComplexObjectExpression(Expression writer, Expression value)
        {
            var type = value.Type;

            var writerType = JsState.IsJson
                     ? typeof(WriteType<,>).MakeGenericType(type, typeof(JsonTypeSerializer))
                     : typeof(WriteType<,>).MakeGenericType(type, typeof(JsvTypeSerializer));

            var wasJson = Expression.Variable(typeof(bool), "wasJson");

            var expression = Expression.Block(
                new[] { wasJson },
                // wasJson = JsState.IsJson
                Expression.Assign(
                    wasJson,
                    Expression.Field(null, typeof(JsState), "IsJson")),

                // JsState.IsJson = $isJson
                Expression.Assign(
                    Expression.Field(null, typeof(JsState), "IsJson"),
                    Expression.Constant(JsState.IsJson)),

                // try {
                Expression.TryFinally(
                    // $serializer.GetWriter()($writer, $value)
                    Expression.Call(
                        Expression.Field(
                            null,
                            writerType,
                            "CacheFn"),
                        "Invoke",
                        Type.EmptyTypes,
                        writer,
                        value),
                    // }
                    // finally {
                    //     JsState.IsJson = wasJson
                    // }
                    Expression.Assign(
                        Expression.Field(null, typeof(JsState), "IsJson"),
                        wasJson)));

            return expression;
        }

        private static Expression BuildSerializeBlobExpression(Expression writer, Expression value)
        {
            return MakeTextWriterCall(
                writer,
                Expression.Call(
                    typeof(Convert),
                    "ToBase64String",
                    Type.EmptyTypes,
                    value));
        }

        private static Expression BuildSerializeFloatingPointExpression(Expression writer, Expression value)
        {
            Expression minValue, maxValue;

            if (value.Type == typeof(float))
            {
                minValue = Expression.Constant(float.MinValue);
                maxValue = Expression.Constant(float.MaxValue);
            }
            else
            {
                minValue = Expression.Constant(double.MinValue);
                maxValue = Expression.Constant(double.MaxValue);
            }

            return Expression.IfThenElse(
                Expression.OrElse(
                    Expression.Equal(value, maxValue),
                    Expression.Equal(value, minValue)),
                MakeTextWriterCall(
                    writer,
                    Expression.Call(value, "ToString", Type.EmptyTypes, Expression.Constant("r"))),
                MakeTextWriterCall(
                    writer,
                    value));
        }

        private static Expression BuildSerializeTimeSpanExpression(Expression writer, Expression value)
        {
            return MakeTextWriterCall(
                writer,
                Expression.Call(
                    typeof(DateTimeSerializer),
                    "ToXsdTimeSpanString",
                    Type.EmptyTypes,
                    value));
        }

        private static Expression BuildSerializeGuidExpression(Expression writer, Expression value)
        {
            return MakeTextWriterCall(
                writer,
                Expression.Call(
                    value,
                    "ToString",
                    Type.EmptyTypes,
                    Expression.Constant("N")));
        }

        private static Expression BuildSerializeTypeExpression(Expression writer, Expression value)
        {
            var serializerType = JsState.IsJson ? typeof(JsonTypeSerializer) : typeof(JsvTypeSerializer);

            // $serializer.WriteRawString(writer, value.ToTypeString())
            return Expression.Call(
                Expression.Field(
                    null,
                    serializerType,
                    "Instance"),
                "WriteRawString",
                Type.EmptyTypes,
                writer,
                Expression.Call(
                    typeof(AssemblyUtils),
                    "ToTypeString",
                    Type.EmptyTypes,
                    value));
        }

        private static Expression BuildSerializeEnumExpression(Expression writer, Expression value)
        {
            var enumType = value.Type;

            if (Attribute.IsDefined(enumType, typeof(FlagsAttribute)))
            {
                return BuildSerializeExpression(
                    writer,
                    Expression.Convert(
                        value,
                        enumType.GetEnumUnderlyingType()));
            }

            return MakeTextWriterCall(
                writer,
                Expression.Call(
                    Expression.Convert(value, typeof(object)),
                    ToStringMethod));
        }

        private static Expression BuildSerializeExceptionExpression(Expression writer, Expression value)
        {
            var serializerType = JsState.IsJson ? typeof(JsonTypeSerializer) : typeof(JsvTypeSerializer);
            return Expression.Call(
                Expression.Field(null, serializerType, "Instance"),
                "WriteException",
                Type.EmptyTypes,
                writer,
                Expression.Convert(value, typeof(object)));
        }

        private static bool TryBuildSerializeCollectionExpression(Expression writer, Expression value, out Expression result)
        {
            var type = value.Type;

            Type dictionary = null;
            Type genericDictionary = null;
            Type genericEnumerable = null;
            Type enumerable = null;
            Type genericList = null;
            Type list = null;

            var interfaceTypes = type.GetInterfaces();

            if (type.IsInterface)
            {
                var newInterfaceTypes = new Type[interfaceTypes.Length + 1];
                newInterfaceTypes[0] = type;
                Array.Copy(interfaceTypes, 0, newInterfaceTypes, 1, interfaceTypes.Length);
                interfaceTypes = newInterfaceTypes;
            }

            foreach (var interfaceType in interfaceTypes)
            {
                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                {
                    genericDictionary = interfaceType;
                    break;
                }

                if (interfaceType == typeof(IDictionary))
                {
                    dictionary = interfaceType;
                    break;
                }

                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IList<>))
                {
                    genericList = interfaceType;
                    break;
                }

                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    genericEnumerable = interfaceType;
                else if (interfaceType == typeof(IList))
                    list = interfaceType;
                else if (interfaceType == typeof(IEnumerable))
                    enumerable = interfaceType;
            }

            if (genericDictionary != null)
            {
                result = BuildSerializeGenericDictionaryExpression(writer, value, genericDictionary);
                return true;
            }

            if (dictionary != null)
            {
                result = BuildSerializeDictionaryExpression(writer, value);
                return true;
            }

            if (genericList != null)
            {
                result = BuildSerializeListExpression(writer, value, genericList);
                return true;
            }

            if (list != null)
            {
                result = BuildSerializeListExpression(writer, value, list);
                return true;
            }
            
            if (genericEnumerable != null)
            {
                result = BuildSerializeEnumerableExpression(writer, value, genericEnumerable);
                return true;
            }

            if (enumerable != null)
            {
                result = BuildSerializeEnumerableExpression(writer, value, enumerable);
                return true;
            }

            result = null;
            return false;
        }

        private static Expression BuildSerializeGenericDictionaryExpression(Expression writer, Expression value, Type dictionaryType)
        {
            var genericArguments = dictionaryType.GetGenericArguments();
            var keyType = genericArguments[0];
            var valueType = genericArguments[1];

            var dictionary = Expression.Variable(dictionaryType, "dictionary");
            var anyWritten = Expression.Variable(typeof(bool), "anyWritten");
            var keyEnumerator = Expression.Variable(typeof(IEnumerator<>).MakeGenericType(keyType), "keyEnumerator");
            var baseEnumerator = Expression.Variable(typeof(IEnumerator), "baseEnumerator");
            var keyVariable = Expression.Variable(keyType, "key");
            var valueVariable = Expression.Variable(valueType, "value");

            var serializer = JsState.IsJson ? JsonTypeSerializer.Instance : JsvTypeSerializer.Instance;
            var encodeMapKey = serializer.GetTypeInfo(keyType).EncodeMapKey;

            var blockExpressions = new List<Expression>
                                   {
                                       // $writer.Write('{')
                                       MakeTextWriterCall(writer, MapStartLiteral, false),

                                       // dictionary = (IDictionary<TKey, TValue>)$value
                                       Expression.Assign(
                                           dictionary,
                                           value.Type == dictionaryType ? value : Expression.Convert(value, dictionaryType)),

                                       // enumerator = dictionary.Keys.GetEnumerator()
                                       Expression.Assign(
                                           keyEnumerator,
                                           Expression.Call(
                                               Expression.Convert(
                                                   Expression.Property(dictionary, "Keys"),
                                                   typeof(IEnumerable<>).MakeGenericType(keyType)),
                                               "GetEnumerator",
                                               Type.EmptyTypes)),

                                       Expression.Assign(
                                           baseEnumerator,
                                           Expression.Convert(keyEnumerator, typeof(IEnumerator))),

                                       Expression.Assign(anyWritten, Expression.Constant(false))
                                   };

            var breakLabel = Expression.Label("breakLoop");
            var continueLabel = Expression.Label("continueLoop");

            var loopExpressions = new List<Expression>()
                                  {
                                      // if (!keyEnumerator.MoveNext()) break
                                      Expression.IfThen(
                                          Expression.IsFalse(
                                              Expression.Call(
                                                  baseEnumerator,
                                                  "MoveNext",
                                                  Type.EmptyTypes)),
                                          Expression.Goto(breakLabel)),

                                      // key = keyEnumerator.Current
                                      Expression.Assign(
                                          keyVariable,
                                          Expression.Property(keyEnumerator, "Current")),

                                      // value = dictionary[key]
                                      Expression.Assign(
                                          valueVariable,
                                          Expression.Property(
                                              dictionary,
                                              "Item",
                                              keyVariable))
                                  };

            Expression nullCheck = null;

            if (!valueType.IsValueType)
                nullCheck = Expression.ReferenceEqual(valueVariable, Expression.Default(valueType));
            else if (valueType.IsNullableType())
                nullCheck = Expression.IsFalse(Expression.Property(valueVariable, "HasValue"));

            if (nullCheck != null)
            {
                // if (value == null && !JsConfig.IncludeNullValues) continue
                loopExpressions.Add(
                    Expression.IfThen(
                        Expression.AndAlso(
                            nullCheck,
                            Expression.IsFalse(
                                Expression.Property(
                                    null,
                                    typeof(JsConfig),
                                    "IncludeNullValues"))),
                        Expression.Goto(continueLabel)));
            }

            // if (anyWritten) $writer.Write('{')
            loopExpressions.Add(
                Expression.IfThen(
                    anyWritten,
                    MakeTextWriterCall(writer, ItemSeparatorLiteral, false)));

            // ++JsState.WritingKeyCount
            loopExpressions.Add(
                Expression.PreIncrementAssign(
                    Expression.Field(
                        null,
                        typeof(JsState),
                        "WritingKeyCount")));

            // JsState.IsWritingValue = false
            loopExpressions.Add(
                Expression.Assign(
                    Expression.Field(
                        null,
                        typeof(JsState),
                        "IsWritingValue"),
                    Expression.Constant(false)));

/*
            loopExpressions.Add(
                Expression.IfThenElse(
                    encodeMapKey,
                    // if (encodeMapKey)
                    Expression.Block(
                        // JsState.IsWritingValue = true
                        Expression.Assign(
                            Expression.Field(
                                null,
                                typeof(JsState),
                                "IsWritingValue"),
                            Expression.Constant(true)),
                        // $writer.Write('"')
                        MakeTextWriterCall(writer, QuoteLiteral, false),
                        // $serialize(key)
                        BuildSerializeExpression(writer, keyVariable),
                        // $writer.Write('"')
                        MakeTextWriterCall(writer, QuoteLiteral, false)),
                    // else $serialize(key)
                    BuildSerializeExpression(writer, keyVariable)));
*/

            if (encodeMapKey)
            {
                loopExpressions.Add(
                    Expression.Block(
                        // JsState.IsWritingValue = true
                        Expression.Assign(
                            Expression.Field(
                                null,
                                typeof(JsState),
                                "IsWritingValue"),
                            Expression.Constant(true)),
                        // $writer.Write('"')
                        MakeTextWriterCall(writer, QuoteLiteral, false),
                        // $serialize(key)
                        BuildSerializeExpression(writer, keyVariable),
                        // $writer.Write('"')
                        MakeTextWriterCall(writer, QuoteLiteral, false)));
            }
            else
            {
                // $serialize(key)
                loopExpressions.Add(BuildSerializeExpression(writer, keyVariable));
            }

            // --JsState.WritingKeyCount
            loopExpressions.Add(
                Expression.PreDecrementAssign(
                    Expression.Field(
                        null,
                        typeof(JsState),
                        "WritingKeyCount")));

            // $writer.Write(':')
            loopExpressions.Add(MakeTextWriterCall(writer, MapKeySeparatorLiteral, false));

            if (nullCheck != null)
            {
                loopExpressions.Add(
                    Expression.IfThenElse(
                        nullCheck,
                        // if (value == null) $writer.Write("null")
                        MakeTextWriterCall(writer, NullLiteral, false),
                        // else
                        Expression.Block(
                        // JsState.IsWritingValue = true
                            Expression.Assign(
                                Expression.Field(
                                    null,
                                    typeof(JsState),
                                    "IsWritingValue"),
                                Expression.Constant(true)),
                            // $serialize(value)
                            BuildSerializeExpression(writer, valueVariable),
                            // JsState.IsWritingValue = false
                            Expression.Assign(
                                Expression.Field(
                                    null,
                                    typeof(JsState),
                                    "IsWritingValue"),
                                Expression.Constant(false))
                            )));
            }
            else
            {
                // JsState.IsWritingValue = true
                loopExpressions.Add(
                    Expression.Assign(
                        Expression.Field(
                            null,
                            typeof(JsState),
                            "IsWritingValue"),
                        Expression.Constant(true)));

                // $serialize(value)
                loopExpressions.Add(BuildSerializeExpression(writer, valueVariable));

                // JsState.IsWritingValue = false
                loopExpressions.Add(
                    Expression.Assign(
                        Expression.Field(
                            null,
                            typeof(JsState),
                            "IsWritingValue"),
                        Expression.Constant(false)));
            }

            // anyWritten = true
            loopExpressions.Add(Expression.Assign(anyWritten, Expression.Constant(true)));

            var loop = Expression.Loop(
                Expression.Block(
                    new[] { keyVariable, valueVariable },
                    loopExpressions),
                breakLabel,
                continueLabel);

            blockExpressions.Add(loop);

            // $writer.Write('}')
            blockExpressions.Add(MakeTextWriterCall(writer, MapEndLiteral, false));

            var block = Expression.Block(
                new[] { dictionary, keyEnumerator, baseEnumerator, anyWritten },
                blockExpressions);

            return block;
        }

        private static Expression BuildSerializeDictionaryExpression(Expression writer, Expression value)
        {
            var dictionary = Expression.Variable(typeof(IDictionary), "dictionary");
            var anyWritten = Expression.Variable(typeof(bool), "anyWritten");
            var enumerator = Expression.Variable(typeof(IDictionaryEnumerator), "enumerator");
            var keyVariable = Expression.Variable(typeof(object), "key");
            var valueVariable = Expression.Variable(typeof(object), "value");

            var encodeMapKey = Expression.Variable(typeof(bool), "encodeMapKey");

            var blockExpressions = new List<Expression>
                                   {
                                       // $writer.Write('{')
                                       MakeTextWriterCall(writer, MapStartLiteral, false),

                                       // dictionary = (IDictionary<TKey, TValue>)$value
                                       Expression.Assign(
                                           dictionary,
                                           value.Type == typeof(IDictionary) ? value : Expression.Convert(value, typeof(IDictionary))),

                                       // enumerator = dictionary.Keys.GetEnumerator()
                                       Expression.Assign(
                                           enumerator,
                                           Expression.Call(
                                               Expression.Property(value, "Keys"),
                                               "GetEnumerator",
                                               Type.EmptyTypes)),

                                       Expression.Assign(anyWritten, Expression.Constant(false))
                                   };

            var breakLabel = Expression.Label("breakLoop");
            var continueLabel = Expression.Label("continueLoop");
            
            var loop = Expression.Loop(
                Expression.Block(
                    new[] { keyVariable, valueVariable },
                    // if (!keyEnumerator.MoveNext()) break
                    Expression.IfThen(
                        Expression.IsFalse(
                            Expression.Call(
                                enumerator,
                                "MoveNext",
                                Type.EmptyTypes)),
                        Expression.Goto(breakLabel)),

                    // key = enumerator.Key
                    Expression.Assign(
                        keyVariable,
                        Expression.Property(enumerator, "Key")),

                    // value = enumerator.Value
                    Expression.Assign(
                        valueVariable,
                        Expression.Property(enumerator, "Value")),

                    // if (value == null && !JsConfig.IncludeNullValues) continue
                    Expression.IfThen(
                        Expression.AndAlso(
                            Expression.ReferenceEqual(
                                valueVariable,
                                Expression.Default(typeof(object))),
                            Expression.IsFalse(
                                Expression.Property(
                                    null,
                                    typeof(JsConfig),
                                    "IncludeNullValues"))),
                        Expression.Goto(continueLabel)),

                    Expression.IfThenElse(
                        Expression.Field(null, typeof(JsState), "IsJson"),
                        // if (JsState.IsJson) { encodeAny = JsonTypeSerializer.Instance.GetTypeInfo(key.GetType()).EncodeMapKey }
                        Expression.Assign(
                            encodeMapKey,
                            Expression.PropertyOrField(
                                Expression.Call(
                                    Expression.Field(null, typeof(JsonTypeSerializer), "Instance"),
                                    "GetTypeInfo",
                                    Type.EmptyTypes,
                                    Expression.Call(keyVariable, GetTypeMethod)),
                                "EncodeMapKey")),
                        // else { encodeAny = JsonTypeSerializer.Instance.GetTypeInfo(key.GetType()).EncodeMapKey }
                        Expression.Assign(
                            encodeMapKey,
                            Expression.PropertyOrField(
                                Expression.Call(
                                    Expression.Field(null, typeof(JsvTypeSerializer), "Instance"),
                                    "GetTypeInfo",
                                    Type.EmptyTypes,
                                    Expression.Call(keyVariable, GetTypeMethod)),
                                "EncodeMapKey"))
                        ),

                    // if (anyWritten) $writer.Write('{')
                    Expression.IfThen(
                        anyWritten,
                        MakeTextWriterCall(writer, ItemSeparatorLiteral, false)),

                    // ++JsState.WritingKeyCount
                    Expression.PreIncrementAssign(
                        Expression.Field(
                            null,
                            typeof(JsState),
                            "WritingKeyCount")),

                    // JsState.IsWritingValue = false
                    Expression.Assign(
                        Expression.Field(
                            null,
                            typeof(JsState),
                            "IsWritingValue"),
                        Expression.Constant(false)),

                    Expression.IfThenElse(
                        encodeMapKey,
                        // if (encodeMapKey)
                        Expression.Block(
                            // JsState.IsWritingValue = true
                            Expression.Assign(
                                Expression.Field(
                                    null,
                                    typeof(JsState),
                                    "IsWritingValue"),
                                Expression.Constant(true)),
                            // $writer.Write('"')
                            MakeTextWriterCall(writer, QuoteLiteral, false),
                            // $serialize(key)
                            BuildSerializeExpression(writer, keyVariable),
                            // $writer.Write('"')
                            MakeTextWriterCall(writer, QuoteLiteral, false)),
                        // else $serialize(key)
                        BuildSerializeExpression(writer, keyVariable)),

                    // --JsState.WritingKeyCount
                    Expression.PreDecrementAssign(
                        Expression.Field(
                            null,
                            typeof(JsState),
                            "WritingKeyCount")),

                    // $writer.Write(':')
                    MakeTextWriterCall(writer, MapKeySeparatorLiteral, false),

                    Expression.IfThenElse(
                        Expression.ReferenceEqual(
                            valueVariable,
                            Expression.Default(typeof(object))),
                        // if (value == null) $writer.Write("null")
                        MakeTextWriterCall(writer, NullLiteral, false),
                        // else
                        Expression.Block(
                            // JsState.IsWritingValue = true
                            Expression.Assign(
                                Expression.Field(
                                    null,
                                    typeof(JsState),
                                    "IsWritingValue"),
                                Expression.Constant(true)),
                            // $serialize(value)
                            BuildSerializeExpression(writer, valueVariable),
                            // JsState.IsWritingValue = false
                            Expression.Assign(
                                Expression.Field(
                                    null,
                                    typeof(JsState),
                                    "IsWritingValue"),
                                Expression.Constant(false))
                            )),

                            // anyWritten = true
                            Expression.Assign(anyWritten, Expression.Constant(true))),
                breakLabel,
                continueLabel);

            blockExpressions.Add(loop);

            // $writer.Write('}')
            blockExpressions.Add(MakeTextWriterCall(writer, MapEndLiteral, false));

            var block = Expression.Block(
                new[] { dictionary, enumerator, anyWritten, encodeMapKey },
                blockExpressions);

            return block;
        }

        private static Expression BuildSerializeBooleanExpression(Expression writer, Expression value)
        {
            if (JsState.IsJson)
            {
                return Expression.Call(
                    writer,
                    TypeWriterMethods.GetWriteMethod(typeof(string)),
                    Expression.Condition(
                        value,
                        Expression.Constant(JsonUtils.True),
                        Expression.Constant(JsonUtils.False)));
            }

            return Expression.Call(
                writer,
                TypeWriterMethods.GetWriteMethod(typeof(string)),
                Expression.Condition(
                    value,
                    TrueLiteral,
                    FalseLiteral));
        }

        private static Expression BuildSerializeCharacterExpression(Expression writer, Expression value)
        {
            return Expression.Block(
                Expression.Call(writer, TypeWriterMethods.GetWriteMethod(typeof(char)), QuoteLiteral),
                Expression.Call(writer, TypeWriterMethods.GetWriteMethod(typeof(char)), value),
                Expression.Call(writer, TypeWriterMethods.GetWriteMethod(typeof(char)), QuoteLiteral));
        }

        private static Expression BuildSerializeDateTimeExpression(Expression writer, Expression value)
        {
            if (JsState.IsJson)
            {
                return Expression.Block(
                    Expression.Call(writer, TypeWriterMethods.GetWriteMethod(typeof(char)), QuoteLiteral),
                    MakeTextWriterCall(
                        writer,
                        Expression.Call(typeof(DateTimeSerializer), "ToWcfJsonDate", Type.EmptyTypes, value),
                        false),
                    Expression.Call(writer, TypeWriterMethods.GetWriteMethod(typeof(char)), QuoteLiteral));
            }

            return MakeTextWriterCall(
                writer,
                Expression.Call(typeof(DateTimeSerializer), "ToShortestXsdDateTimeString", Type.EmptyTypes, value),
                false);
        }

        private static Expression BuildSerializeDateTimeOffsetExpression(Expression writer, Expression value)
        {
            if (JsState.IsJson)
            {
                return Expression.Block(
                    Expression.Call(writer, TypeWriterMethods.GetWriteMethod(typeof(char)), QuoteLiteral),
                    MakeTextWriterCall(
                        writer,
                        Expression.Call(typeof(DateTimeSerializer), "ToWcfJsonDateTimeOffset", Type.EmptyTypes, value),
                        false),
                    Expression.Call(writer, TypeWriterMethods.GetWriteMethod(typeof(char)), QuoteLiteral));
            }

            return MakeTextWriterCall(
                writer,
                Expression.Call(typeof(DateTimeSerializer), "ToShortestXsdDateTimeString", Type.EmptyTypes, value));
        }

        internal static Expression BuildSerializeNullExpression(Expression writer)
        {
            if (JsState.IsJson)
                return Expression.Call(writer, TextWriterWriteStringMethod, NullLiteral);
            return Expression.Empty();
        }

        private static Expression BuildSerializeLateBoundObjectExpression(Expression writer, Expression value)
        {
            var wasJson = Expression.Variable(typeof(bool), "wasJson");

            var isJson = JsState.IsJson;

            return Expression.Block(
                new[] { wasJson },
                // wasJson = JsState.IsJson
                Expression.Assign(
                    wasJson,
                    Expression.Field(null, typeof(JsState), "IsJson")),
                // JsState.IsJson = $isJson
                Expression.Assign(
                    Expression.Field(null, typeof(JsState), "IsJson"),
                    Expression.Constant(isJson)),
                // try {
                Expression.TryFinally(
                    // LateBoundSerialize($writer, $value)
                    Expression.Call(null, LateBoundSerializeMethod, writer, value),
                    // }
                    // finally {
                    //     JsState.IsJson = wasJson
                    // }
                    Expression.Assign(
                        Expression.Field(null, typeof(JsState), "IsJson"),
                        wasJson)));
        }

        private static Expression BuildSerializeStringExpression(Expression writer, Expression value)
        {
            return MakeTextWriterCall(writer, value);
        }

        private static Expression BuildSerializeArrayExpression(Expression writer, Expression value)
        {
            var index = Expression.Variable(typeof(int), "index");
            var length = Expression.Variable(typeof(int), "length");
            var breakLabel = Expression.Label("break");
            var continueLabel = Expression.Label("continue");

            var serializeExpression = Expression.Block(
                new[] { index, length },
                Expression.Assign(index, Expression.Constant(0)),
                Expression.Assign(length, Expression.ArrayLength(value)),
                MakeTextWriterCall(writer, ListStartLiteral),
                Expression.Loop(
                    Expression.Block(
                        Expression.IfThen(
                            Expression.GreaterThanOrEqual(index, length),
                            Expression.Goto(breakLabel)),
                        Expression.IfThen(
                            Expression.NotEqual(index, Expression.Constant(0)),
                            MakeTextWriterCall(writer, ItemSeparatorLiteral)),
                        BuildSerializeExpression(
                            writer,
                            Expression.ArrayIndex(value, index),
                            true),
                        Expression.PreIncrementAssign(index),
                        Expression.Goto(continueLabel)),
                    breakLabel,
                    continueLabel),
                MakeTextWriterCall(writer, ListEndLiteral));

            return serializeExpression;
        }

        private static Expression BuildSerializeListExpression(Expression writer, Expression value, Type listType)
        {
            var list = Expression.Variable(listType, "list");
            var index = Expression.Variable(typeof(int), "index");
            var length = Expression.Variable(typeof(int), "length");
            var breakLabel = Expression.Label("break");
            var continueLabel = Expression.Label("continue");

            var elementType = listType.IsGenericType ? listType.GetGenericArguments()[0] : typeof(object);

            var countProperty = listType.IsGenericType
                                    ? typeof(ICollection<>).MakeGenericType(listType.GetGenericArguments()[0]).GetProperty("Count")
                                    : typeof(ICollection).GetProperty("Count");

            var indexer = listType.IsGenericType
                                    ? typeof(IList<>).MakeGenericType(listType.GetGenericArguments()[0]).GetProperty("Item", new[] { typeof(int) })
                                    : typeof(IList).GetProperty("Item", new[] { typeof(int) });

            Expression elementExpression;

            if (elementType.IsValueType || elementType.IsSealed)
                elementExpression = Expression.MakeIndex(value, indexer, new[] { index });
            else
                elementExpression = Expression.Convert(Expression.MakeIndex(value, indexer, new[] { index }), typeof(object));

            var serializeExpression = Expression.Block(
                new[] { list, index, length },
                Expression.Assign(list, value.Type == listType ? value : Expression.Convert(value, listType)),
                Expression.Assign(index, Expression.Constant(0)),
                Expression.Assign(length, Expression.Property(list, countProperty)),
                MakeTextWriterCall(writer, ListStartLiteral),
                Expression.Loop(
                    Expression.Block(
                        Expression.IfThen(
                            Expression.GreaterThanOrEqual(index, length),
                            Expression.Goto(breakLabel)),
                        Expression.IfThen(
                            Expression.NotEqual(index, Expression.Constant(0)),
                            MakeTextWriterCall(writer, ItemSeparatorLiteral)),
                        BuildSerializeExpression(
                            writer,
                            elementExpression),
                        Expression.PreIncrementAssign(index),
                        Expression.Goto(continueLabel)),
                    breakLabel,
                    continueLabel),
                MakeTextWriterCall(writer, ListEndLiteral));

            return serializeExpression;
        }

        private static Expression BuildSerializeEnumerableExpression(Expression writer, Expression value, Type enumerableType)
        {
            var getEnumeratorMethod = enumerableType.IsGenericType
                ? typeof(IEnumerable<>).MakeGenericType(enumerableType.GetGenericArguments()[0]).GetMethod("GetEnumerator")
                : typeof(IEnumerable).GetMethod("GetEnumerator");
            
            var enumeratorType = getEnumeratorMethod.ReturnType;

            var moveNextMethod = typeof(IEnumerator).GetMethod("MoveNext");

            var currentProperty = enumeratorType.IsGenericType
                                     ? typeof(IEnumerator<>).MakeGenericType(enumerableType.GetGenericArguments()[0]).GetProperty("Current")
                                     : typeof(IEnumerator).GetProperty("Current");

            var list = Expression.Variable(enumerableType, "list");
            var enumerator = Expression.Variable(enumeratorType, "enumerator");
            var breakLabel = Expression.Label("break");
            var continueLabel = Expression.Label("continue");
            var anyWritten = Expression.Variable(typeof(bool), "anyWritten");

            var loopExpressions = new List<Expression>
                                  {
                                      Expression.IfThen(
                                          Expression.IsFalse(Expression.Call(enumerator, moveNextMethod)),
                                          Expression.Goto(breakLabel)),

                                      Expression.IfThen(
                                          anyWritten,
                                          MakeTextWriterCall(writer, ItemSeparatorLiteral)),

                                      BuildSerializeExpression(
                                          writer,
                                          Expression.Property(
                                              enumerator,
                                              currentProperty)),

                                      Expression.Assign(anyWritten, Expression.Constant(true)),

                                      Expression.Goto(continueLabel)
                                  };

            var serializeExpression = Expression.Block(
                new[] { list, enumerator, anyWritten },
                Expression.Assign(anyWritten, Expression.Constant(false)),
                Expression.Assign(list, value.Type == enumerableType ? value : Expression.Convert(value, enumerableType)),
                Expression.Assign(enumerator, Expression.Call(list, getEnumeratorMethod)),
                MakeTextWriterCall(writer, ListStartLiteral),
                Expression.Loop(
                    Expression.Block(loopExpressions),
                    breakLabel,
                    continueLabel),
                MakeTextWriterCall(writer, ListEndLiteral));

            return serializeExpression;
        }

        private static void LateBoundSerialize(TextWriter writer, object value)
        {
            if (value == null)
            {
                writer.Write(JsonUtils.Null);
                return;
            }

            var isJson = JsState.IsJson;

            var stringValue = value as string;
            if (stringValue != null)
            {
                if (isJson)
                    JsonUtils.WriteString(writer, stringValue);
                else
                    writer.Write(stringValue.ToCsvField());
                return;
            }

            var wasWritingDynamic = JsState.IsWritingDynamic;

            JsState.IsWritingDynamic = true;

            try
            {
                if (isJson)
                    JsonWriter.GetWriteFn(value.GetType())(writer, value);
                else
                    JsvWriter.GetWriteFn(value.GetType())(writer, value);
            }
            finally
            {
                JsState.IsWritingDynamic = wasWritingDynamic;
            }
        }
    }
}

// ReSharper restore PossiblyMistakenUseOfParamsMethod
