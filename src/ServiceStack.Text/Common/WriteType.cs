//
// http://code.google.com/p/servicestack/wiki/TypeSerializer
// StrobelStack.Text: .NET C# POCO Type Text Serializer.
//
// Authors:
//   Demis Bellot (demis.bellot@gmail.com)
//
// Copyright 2011 Liquidbit Ltd.
//
// Licensed under the same terms of ServiceStack: new BSD license.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using StrobelStack.Text.Json;
using StrobelStack.Text.Jsv;
using StrobelStack.Text.Reflection;
using System.Linq;
using System.Runtime.Serialization;

namespace StrobelStack.Text.Common
{
	internal static class WriteType<T, TSerializer>
		where TSerializer : ITypeSerializer
	{
		private static readonly ITypeSerializer Serializer = JsWriter.GetTypeSerializer<TSerializer>();

		private static readonly WriteValueDelegate<T> CacheFn;
		internal static TypePropertyWriter[] PropertyWriters;
		private static WriteObjectDelegate WriteTypeInfo;
	    private static readonly ParameterExpression WriterParameter;
	    private static readonly ParameterExpression ValueParameter;

		static WriteType()
		{
            WriterParameter = Expression.Parameter(typeof(TextWriter));
            ValueParameter = Expression.Parameter(typeof(T));

            CacheFn = Init() ? GetWriter() : ((writer, obj) => WriteEmptyType(writer, obj));

			if (typeof(T).IsAbstract)
			{
				WriteTypeInfo = TypeInfoWriter;
				if (!typeof(T).IsInterface)
				{
                    CacheFn = (writer, obj) => WriteAbstractProperties(writer, obj);
				}
			}
		}

		public static void TypeInfoWriter(TextWriter writer, object obj)
		{
			DidWriteTypeInfo(writer, obj);
		}

        private static bool DidWriteTypeInfo(TextWriter writer, object obj)
        {
            if (obj == null
                || JsConfig.ExcludeTypeInfo
                || JsConfig<T>.ExcludeTypeInfo) return false;

            Serializer.WriteTypeInfo(writer, obj);
            return true;
        }

		public static WriteValueDelegate<T> Write
		{
			get { return CacheFn; }
		}

		private static WriteObjectDelegate GetWriteFn()
		{
			return WriteProperties;
		}

		private static bool Init()
		{
			if (!typeof(T).IsClass && !typeof(T).IsInterface && !JsConfig.TreatAsRefType(typeof(T))) return false;

			var propertyInfos = TypeConfig<T>.Properties;
			if (propertyInfos.Length == 0 && !JsState.IsWritingDynamic)
			{
				return typeof(T).IsDto();
			}

			var propertyNamesLength = propertyInfos.Length;

			PropertyWriters = new TypePropertyWriter[propertyNamesLength];

			// NOTE: very limited support for DataContractSerialization (DCS)
			//	NOT supporting Serializable
			//	support for DCS is intended for (re)Name of properties and Ignore by NOT having a DataMember present
			var isDataContract = typeof(T).GetCustomAttributes(typeof(DataContractAttribute), false).Any();
			for (var i = 0; i < propertyNamesLength; i++)
			{
				var propertyInfo = propertyInfos[i];

				string propertyName, propertyNameCLSFriendly;

				if (isDataContract)
				{
					var dcsDataMember = propertyInfo.GetCustomAttributes(typeof(DataMemberAttribute), false).FirstOrDefault() as DataMemberAttribute;
					if (dcsDataMember == null) continue;

					propertyName = dcsDataMember.Name ?? propertyInfo.Name;
					propertyNameCLSFriendly = dcsDataMember.Name ?? propertyName.ToCamelCase();
				}
				else
				{
					propertyName = propertyInfo.Name;
					propertyNameCLSFriendly = propertyName.ToCamelCase();
				}

			    var propertyType = propertyInfo.PropertyType;
			    var suppressDefaultValue = propertyType.IsValueType && JsConfig.HasSerializeFn.Contains(propertyType)
			        ? ReflectionExtensions.GetDefaultValue(propertyType)
			        : null;

			    PropertyWriters[i] = new TypePropertyWriter
			        (
			        propertyName,
			        propertyNameCLSFriendly,
			        propertyInfo.GetValueGetter<T>(),
                    (writer, o) => Serializer.GetWriteFn(propertyType)(writer, o),
			        propertyInfo,
			        suppressDefaultValue
			        );
			}

			return true;
		}

		internal struct TypePropertyWriter
		{
			internal string PropertyName
			{
				get
				{
					return (JsConfig.EmitCamelCaseNames)
						? propertyNameCLSFriendly
						: propertyName;
				}
			}
			internal readonly string propertyName;
			internal readonly string propertyNameCLSFriendly;
			internal readonly Func<T, object> GetterFn;
            internal readonly WriteObjectDelegate WriteFn;
            internal readonly PropertyInfo PropertyInfo;
            internal readonly object DefaultValue;

			public TypePropertyWriter(string propertyName, string propertyNameCLSFriendly,
				Func<T, object> getterFn, WriteObjectDelegate writeFn, object defaultValue)
			{
				this.propertyName = propertyName;
				this.propertyNameCLSFriendly = propertyNameCLSFriendly;
				this.GetterFn = getterFn;
				this.WriteFn = writeFn;
			    this.PropertyInfo = null;
			    this.DefaultValue = defaultValue;
			}
			public TypePropertyWriter(string propertyName, string propertyNameCLSFriendly,
				Func<T, object> getterFn, WriteObjectDelegate writeFn, PropertyInfo propertyInfo, object defaultValue)
			{
				this.propertyName = propertyName;
				this.propertyNameCLSFriendly = propertyNameCLSFriendly;
				this.GetterFn = getterFn;
			    this.WriteFn = writeFn;
			    this.PropertyInfo = propertyInfo;
			    this.DefaultValue = defaultValue;
			}
		}

		public static void WriteEmptyType(TextWriter writer, object value)
		{
			writer.Write(JsWriter.EmptyMap);
		}

		public static void WriteAbstractProperties(TextWriter writer, object value)
		{
			if (value == null)
			{
				writer.Write(JsWriter.EmptyMap);
				return;
			}
			var valueType = value.GetType();
			if (valueType.IsAbstract)
			{
				WriteProperties(writer, value);
				return;
			}

			var writeFn = Serializer.GetWriteFn(valueType);			
			if (!JsConfig<T>.ExcludeTypeInfo) JsState.IsWritingDynamic = true;
			writeFn(writer, value);
			if (!JsConfig<T>.ExcludeTypeInfo) JsState.IsWritingDynamic = false;
		}
		 
		public static void WriteProperties(TextWriter writer, object value)
		{
			if (typeof(TSerializer) == typeof(JsonTypeSerializer) && JsState.WritingKeyCount > 0)
				writer.Write(JsWriter.QuoteChar);

			writer.Write(JsWriter.MapStartChar);

			var i = 0;
			if (WriteTypeInfo != null || JsState.IsWritingDynamic)
			{
				if (DidWriteTypeInfo(writer, value)) i++;
			}

			if (PropertyWriters != null)
			{
				var len = PropertyWriters.Length;
				for (int index = 0; index < len; index++)
				{
					var propertyWriter = PropertyWriters[index];
					var propertyValue = value != null 
						? propertyWriter.GetterFn((T)value)
						: null;

					if ((propertyValue == null
					     || (propertyWriter.DefaultValue != null && propertyWriter.DefaultValue.Equals(propertyValue)))
					    && !JsConfig.IncludeNullValues) continue;

					if (i++ > 0)
						writer.Write(JsWriter.ItemSeperator);

					Serializer.WritePropertyName(writer, propertyWriter.PropertyName);
					writer.Write(JsWriter.MapKeySeperator);

					if (typeof (TSerializer) == typeof (JsonTypeSerializer)) JsState.IsWritingValue = true;
					if (propertyValue == null)
					{
						writer.Write(JsonUtils.Null);
					}
					else
					{
						propertyWriter.WriteFn(writer, propertyValue);
					}
					if (typeof(TSerializer) == typeof(JsonTypeSerializer)) JsState.IsWritingValue = false;
				}
			}

			writer.Write(JsWriter.MapEndChar);

			if (typeof(TSerializer) == typeof(JsonTypeSerializer) && JsState.WritingKeyCount > 0)
				writer.Write(JsWriter.QuoteChar);
		}

	    public static WriteValueDelegate<T> GetWriter()
	    {
	        var anyWritten = Expression.Variable(typeof(bool));
	        var bodyExpressions = new List<Expression>();

	        var isJson = typeof(TSerializer) == typeof(JsonTypeSerializer);
	        if (isJson)
	        {
	            bodyExpressions.Add(
	                Expression.IfThen(
	                    Expression.GreaterThan(
	                        Expression.Field(
	                            null,
	                            typeof(JsState).GetField(
	                                "WritingKeyCount",
	                                BindingFlags.Static | BindingFlags.NonPublic)),
	                        Expression.Constant(0)),
	                    Expression.Call(
	                        WriterParameter,
	                        TypeWriterMethods.GetWriteMethod(typeof(char)),
	                        SerializationExpressions.QuoteLiteral)));
	        }

	        bodyExpressions.Add(
	            SerializationExpressions.MakeTextWriterCall(
	                WriterParameter,
	                Expression.Constant(JsWriter.MapStartChar)));

	        Expression writeTypeInfo = Expression.Condition(
	            Expression.AndAlso(
	                Expression.IsFalse(
	                    Expression.Property(
	                        null,
	                        typeof(JsConfig),
	                        "ExcludeTypeInfo")),
	                Expression.IsFalse(
	                    Expression.Field(
	                        null,
	                        typeof(JsConfig<>).MakeGenericType(typeof(T)),
	                        "ExcludeTypeInfo"))),
	            Expression.Block(
	                Expression.Call(
	                    Expression.Constant(Serializer),
	                    "WriteTypeInfo",
	                    Type.EmptyTypes,
	                    WriterParameter,
	                    ValueParameter.Type == typeof(object)
	                        ? (Expression)ValueParameter
	                        : Expression.Convert(ValueParameter, typeof(object))),
	                SerializationExpressions.TrueConstant),
	            SerializationExpressions.FalseConstant);

            if (WriteTypeInfo == null)
            {
                writeTypeInfo = Expression.AndAlso(
                    Expression.Field(null, typeof(JsState), "IsWritingDynamic"),
                    writeTypeInfo);
            }

	        bodyExpressions.Add(
	            Expression.IfThen(
	                writeTypeInfo,
	                Expression.Assign(anyWritten, Expression.Constant(true))));

	        if (PropertyWriters != null)
	        {
	            for (var i = 0; i < PropertyWriters.Length; i++)
	                bodyExpressions.Add(GetWritePropertyExpression(PropertyWriters[i], isJson, anyWritten));
	        }

	        bodyExpressions.Add(
	            SerializationExpressions.MakeTextWriterCall(
	                WriterParameter,
	                Expression.Constant(JsWriter.MapEndChar)));

	        if (isJson)
	        {
	            bodyExpressions.Add(
	                Expression.IfThen(
	                    Expression.GreaterThan(
	                        Expression.Field(
	                            null,
	                            typeof(JsState).GetField(
	                                "WritingKeyCount",
	                                BindingFlags.Static | BindingFlags.NonPublic)),
	                        Expression.Constant(0)),
	                    Expression.Call(
	                        WriterParameter,
	                        TypeWriterMethods.GetWriteMethod(typeof(char)),
	                        SerializationExpressions.QuoteLiteral)));
	        }

	        var body = Expression.Block(
                typeof(void),
	            new[] { anyWritten },
	            bodyExpressions);

            if (typeof(T).IsAbstract)
            {
                var wasWritingDynamic = Expression.Variable(typeof(bool), "wasWritingAbstract");

                var isWritingDynamicField = Expression.Field(
                    null,
                    typeof(JsState).GetField(
                        "IsWritingDynamic",
                        BindingFlags.Static | BindingFlags.NonPublic));

                body = Expression.Block(
                    new[] { wasWritingDynamic },
                    Expression.Assign(
                        wasWritingDynamic,
                        isWritingDynamicField),
                    Expression.Assign(
                        isWritingDynamicField,
                        SerializationExpressions.TrueConstant),
                    Expression.TryFinally(
                        body,
                        Expression.Assign(
                            isWritingDynamicField,
                            wasWritingDynamic)));
            }

	        var lambda = Expression.Lambda<WriteValueDelegate<T>>(
	            body,
	            WriterParameter,
	            ValueParameter);

	        return lambda.Compile();
	    }

        // ReSharper disable PossiblyMistakenUseOfParamsMethod

	    private static Expression GetWritePropertyExpression(TypePropertyWriter propertyWriter, bool isJson, Expression anyWritten)
	    {
	        var propertyInfo = propertyWriter.PropertyInfo;
	        var propertyType = propertyInfo.PropertyType;
	        var tempVariable = Expression.Variable(propertyInfo.PropertyType);

	        var bodyExpressions = new List<Expression>
	                              {
	                                  Expression.Assign(
	                                      tempVariable,
	                                      Expression.Property(ValueParameter, propertyWriter.PropertyInfo))
	                              };

	        Expression writeTest = null;

	        if (!propertyType.IsValueType)
	            writeTest = Expression.ReferenceNotEqual(tempVariable, Expression.Default(propertyType));
	        else if (propertyType.IsNullableType())
	            writeTest = Expression.Property(tempVariable, "HasValue");

	        if (propertyWriter.DefaultValue != null)
	        {
	            var defaultValueCheck = Expression.IsFalse(
	                Expression.Call(
	                    tempVariable,
	                    SerializationExpressions.EqualsMethod,
	                    Expression.Constant(
	                        propertyWriter.DefaultValue,
	                        typeof(object))));

	            if (writeTest == null)
	                writeTest = defaultValueCheck;
	            else
	                writeTest = Expression.AndAlso(writeTest, defaultValueCheck);
	        }

            if (!propertyType.IsValueType || propertyType.IsNullableType())
            {
                var includeNullValuesCheck = Expression.Property(
                    null,
                    typeof(JsConfig),
                    "IncludeNullValues");

                if (writeTest == null)
                    writeTest = includeNullValuesCheck;
                else
                    writeTest = Expression.OrElse(writeTest, includeNullValuesCheck);
            }

            var writeExpressions = writeTest == null ? bodyExpressions : new List<Expression>();

	        if (isJson)
	        {
	            writeExpressions.Add(
	                Expression.IfThenElse(
	                    Expression.GreaterThan(
	                        Expression.Field(
	                            null,
	                            typeof(JsState).GetField(
	                                "WritingKeyCount",
	                                BindingFlags.Static | BindingFlags.NonPublic)),
	                        Expression.Constant(0)),
	                    Expression.Block(
	                        Expression.Call(
	                            WriterParameter,
	                            TypeWriterMethods.GetWriteMethod(typeof(string)),
	                            Expression.Condition(
	                                anyWritten,
	                                Expression.Constant(JsWriter.ItemSeperatorString + JsWriter.QuoteString),
	                                Expression.Constant(JsWriter.QuoteString))),
	                        Expression.Call(
	                            WriterParameter,
	                            typeof(TextWriter).GetMethod("Write", new[] { typeof(string) }),
	                            Expression.Constant(propertyWriter.PropertyName)),
	                        Expression.Call(
	                            WriterParameter,
	                            TypeWriterMethods.GetWriteMethod(typeof(string)),
	                            Expression.Constant(JsWriter.QuoteString + JsWriter.MapKeySeperator))),
	                    Expression.Call(
	                        WriterParameter,
	                        typeof(TextWriter).GetMethod("Write", new[] { typeof(string) }),
	                        Expression.Condition(
	                            anyWritten,
	                            Expression.Constant(
	                                JsWriter.ItemSeperatorString +
	                                JsWriter.QuoteString +
	                                propertyWriter.PropertyName +
	                                JsWriter.QuoteString +
	                                JsWriter.MapKeySeperatorString),
	                            Expression.Constant(
	                                JsWriter.QuoteString +
	                                propertyWriter.PropertyName +
	                                JsWriter.QuoteString +
	                                JsWriter.MapKeySeperatorString)))));
	        }
	        else
	        {
	            writeExpressions.Add(
	                Expression.Call(
	                    WriterParameter,
	                    typeof(TextWriter).GetMethod("Write", new[] { typeof(string) }),
	                    Expression.Condition(
	                        anyWritten,
	                        Expression.Constant(
	                            JsWriter.ItemSeperatorString +
	                            propertyWriter.PropertyName +
	                            JsWriter.MapKeySeperatorString),
	                        Expression.Constant(
	                            propertyWriter.PropertyName +
	                            JsWriter.MapKeySeperatorString))));
	        }

	        writeExpressions.Add(
	            SerializationExpressions.BuildSerializeExpression(
	                WriterParameter,
	                tempVariable,
	                true));

	        writeExpressions.Add(
	            Expression.Assign(
	                anyWritten,
	                Expression.Constant(true)));

            if (writeTest != null)
            {
                bodyExpressions.Add(
                    Expression.IfThen(
                        writeTest,
                        Expression.Block(writeExpressions)));
            }

	        var body = Expression.Block(
	            new[] { tempVariable },
	            bodyExpressions);

	        return body;
	    }

	    // ReSharper restore PossiblyMistakenUseOfParamsMethod

	    public static void WriteQueryString(TextWriter writer, object value)
		{
			var i = 0;
			foreach (var propertyWriter in PropertyWriters)
			{
				var propertyValue = propertyWriter.GetterFn((T)value);
				if (propertyValue == null) continue;
				var propertyValueString = propertyValue as string;
				if (propertyValueString != null)
				{
					propertyValue = propertyValueString.UrlEncode();
				}

				if (i++ > 0)
					writer.Write('&');

				Serializer.WritePropertyName(writer, propertyWriter.PropertyName);
				writer.Write('=');
				propertyWriter.WriteFn(writer, propertyValue);
			}
		}
	}
}
