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
using StrobelStack.Text.Common;

using System.Linq;

namespace StrobelStack.Text.Json
{
	internal static class JsonWriter
	{
		public static readonly JsWriter<JsonTypeSerializer> Instance = new JsWriter<JsonTypeSerializer>();

        private static Dictionary<Type, WriteObjectDelegate> OldWriteFnCache = new Dictionary<Type, WriteObjectDelegate>();
        private static Dictionary<Type, Delegate> WriteFnCache = new Dictionary<Type, Delegate>();

		public static WriteObjectDelegate GetWriteFn(Type type)
		{
            try
            {
                WriteObjectDelegate writeFn;
                if (OldWriteFnCache.TryGetValue(type, out writeFn)) return writeFn;

                var writerParameter = Expression.Parameter(typeof(TextWriter), "writer");
                var valueParameter = Expression.Parameter(typeof(object), "value");

                var wrapper = Expression.Lambda<WriteObjectDelegate>(
                    Expression.Invoke(
                        Expression.Call(typeof(JsonWriter), "GetWriteFn", new[] { type }),
                        writerParameter,
                        type == typeof(object) ? (Expression)valueParameter : Expression.Convert(valueParameter, type)),
                    writerParameter,
                    valueParameter);

                var compiledWrapper = wrapper.Compile();

                Dictionary<Type, WriteObjectDelegate> snapshot, newCache;

                do
                {
                    snapshot = OldWriteFnCache;
                    newCache = new Dictionary<Type, WriteObjectDelegate>(snapshot);
                    newCache[type] = compiledWrapper;
                }
                while (!ReferenceEquals(Interlocked.CompareExchange(ref OldWriteFnCache, newCache, snapshot), snapshot) &&
                       !OldWriteFnCache.TryGetValue(type, out writeFn));

                return writeFn ?? compiledWrapper;
            }
            catch (Exception ex)
            {
                Tracer.Instance.WriteError(ex);
                throw;
            }

		}

		public static WriteValueDelegate<T> GetWriteFn<T>()
		{
            try
            {
                var type = typeof(T);

                Delegate writeFn;
                if (WriteFnCache.TryGetValue(type, out writeFn)) return (WriteValueDelegate<T>)writeFn;

                var genericType = typeof(JsonWriter<>).MakeGenericType(type);
                var mi = genericType.GetMethod("WriteFn", BindingFlags.Public | BindingFlags.Static);
                var writeFactoryFn = (Func<WriteValueDelegate<T>>)Delegate.CreateDelegate(typeof(Func<WriteValueDelegate<T>>), mi);
                writeFn = writeFactoryFn();

                Dictionary<Type, Delegate> snapshot, newCache;
                do
                {
                    snapshot = WriteFnCache;
                    newCache = new Dictionary<Type, Delegate>(WriteFnCache);
                    newCache[type] = writeFn;

                } while (!ReferenceEquals(
                    Interlocked.CompareExchange(ref WriteFnCache, newCache, snapshot), snapshot));

                return (WriteValueDelegate<T>)writeFn;
            }
            catch (Exception ex)
            {
                Tracer.Instance.WriteError(ex);
                throw;
            }
        }

		private static Dictionary<Type, TypeInfo> JsonTypeInfoCache = new Dictionary<Type, TypeInfo>();

		public static TypeInfo GetTypeInfo(Type type)
		{
			try
			{
				TypeInfo writeFn;
				if (JsonTypeInfoCache.TryGetValue(type, out writeFn)) return writeFn;

				var genericType = typeof(JsonWriter<>).MakeGenericType(type);
				var mi = genericType.GetMethod("GetTypeInfo", BindingFlags.Public | BindingFlags.Static);
				var writeFactoryFn = (Func<TypeInfo>)Delegate.CreateDelegate(typeof(Func<TypeInfo>), mi);
				writeFn = writeFactoryFn();

				Dictionary<Type, TypeInfo> snapshot, newCache;
				do
				{
					snapshot = JsonTypeInfoCache;
					newCache = new Dictionary<Type, TypeInfo>(JsonTypeInfoCache);
					newCache[type] = writeFn;

				} while (!ReferenceEquals(
					Interlocked.CompareExchange(ref JsonTypeInfoCache, newCache, snapshot), snapshot));

				return writeFn;
			}
			catch (Exception ex)
			{
				Tracer.Instance.WriteError(ex);
				throw;
			}
		}

		public static void WriteLateBoundObject(TextWriter writer, object value)
		{
			if (value == null)
			{
				if (JsConfig.IncludeNullValues)
				{
					writer.Write(JsonUtils.Null);
				}
				return;
			}
			var writeFn = GetWriteFn(value.GetType());

			var prevState = JsState.IsWritingDynamic;
			JsState.IsWritingDynamic = true;
			writeFn(writer, value);
			JsState.IsWritingDynamic = prevState;
		}

		public static WriteObjectDelegate GetValueTypeToStringMethod(Type type)
		{
			return Instance.GetValueTypeToStringMethod(type);
		}
	}

	internal class TypeInfo
	{
		internal bool EncodeMapKey;
	}

	/// <summary>
	/// Implement the serializer using a more static approach
	/// </summary>
	/// <typeparam name="T"></typeparam>
	internal static class JsonWriter<T>
	{
		internal static TypeInfo TypeInfo;
        private static readonly WriteValueDelegate<T> CacheFn;

        public static WriteValueDelegate<T> WriteFn()
		{
			return CacheFn ?? ((writer, obj) => WriteObject(writer, obj));
		}

		public static TypeInfo GetTypeInfo()
		{
			return TypeInfo;
		}

        static JsonWriter()
        {
            TypeInfo = new TypeInfo
                       {
                           EncodeMapKey = typeof(T) == typeof(bool) || typeof(T).IsNumericType()
                       };

            var wasJson = JsState.IsJson;

            JsState.IsJson = true;

            try
            {
                CacheFn = typeof(T) == typeof(object)
                              ? ((writer, obj) => JsonWriter.WriteLateBoundObject(writer, obj))
                              : JsonWriter.Instance.GetWriteFn<T>();
            }
            finally
            {
                JsState.IsJson = wasJson;
            }
        }

	    public static void WriteObject(TextWriter writer, object value)
	    {
	        WriteValueDelegate<T> writeValueDelegate = CacheFn;
	        writeValueDelegate(writer, (T)value);
	    }
	}

}