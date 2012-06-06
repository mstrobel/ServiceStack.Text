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
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using StrobelStack.Text.Common;
using StrobelStack.Text.Jsv;

namespace StrobelStack.Text
{
	public static class QueryStringSerializer
	{
		internal static readonly JsWriter<JsvTypeSerializer> Instance = new JsWriter<JsvTypeSerializer>();

		private static Dictionary<Type, WriteObjectDelegate> WriteFnCache = new Dictionary<Type, WriteObjectDelegate>();

		internal static WriteObjectDelegate GetWriteFn(Type type)
		{
			try
			{
				WriteObjectDelegate writeFn;
                if (WriteFnCache.TryGetValue(type, out writeFn)) return writeFn;

                var genericType = typeof(QueryStringWriter<>).MakeGenericType(type);
                var mi = genericType.GetMethod("WriteFn", BindingFlags.NonPublic | BindingFlags.Static);
                var writeFactoryFn = (Func<WriteObjectDelegate>)Delegate.CreateDelegate(
                    typeof(Func<WriteObjectDelegate>), mi);
                writeFn = writeFactoryFn();

                Dictionary<Type, WriteObjectDelegate> snapshot, newCache;
                do
                {
                    snapshot = WriteFnCache;
                    newCache = new Dictionary<Type, WriteObjectDelegate>(WriteFnCache);
                    newCache[type] = writeFn;

                } while (!ReferenceEquals(
                    Interlocked.CompareExchange(ref WriteFnCache, newCache, snapshot), snapshot));
                
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
			if (value == null) return;
			var writeFn = GetWriteFn(value.GetType());
			writeFn(writer, value);
		}

		internal static WriteObjectDelegate GetValueTypeToStringMethod(Type type)
		{
			return Instance.GetValueTypeToStringMethod(type);
		}

		public static string SerializeToString<T>(T value)
		{
			var sb = new StringBuilder();
			using (var writer = new StringWriter(sb, CultureInfo.InvariantCulture))
			{
				GetWriteFn(value.GetType())(writer, value);
			}
			return sb.ToString();
		}
	}

	/// <summary>
	/// Implement the serializer using a more static approach
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public static class QueryStringWriter<T>
	{
		private static readonly WriteObjectDelegate CacheFn;

		internal static WriteObjectDelegate WriteFn()
		{
			return CacheFn;
		}

		static QueryStringWriter()
		{
			if (typeof(T) == typeof(object))
			{
				CacheFn = QueryStringSerializer.WriteLateBoundObject;
			}
			else
			{
				if (typeof(T).IsClass || typeof(T).IsInterface)
				{
					var canWriteType = WriteType<T, JsvTypeSerializer>.Write;
					if (canWriteType != null)
					{
						CacheFn = WriteType<T, JsvTypeSerializer>.WriteQueryString;
						return;
					}
				}

                CacheFn = (writer, o) => QueryStringSerializer.Instance.GetWriteFn<T>()(writer, (T)o);
			}
		}

		public static void WriteObject(TextWriter writer, object value)
		{
			if (writer == null) return;
			CacheFn(writer, value);
		}
	}
	
}