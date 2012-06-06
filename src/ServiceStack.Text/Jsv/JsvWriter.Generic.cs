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

namespace StrobelStack.Text.Jsv
{
	internal static class JsvWriter
	{
		public static readonly JsWriter<JsvTypeSerializer> Instance = new JsWriter<JsvTypeSerializer>();

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
                        Expression.Call(typeof(JsvWriter), "GetWriteFn", new[] { type }),
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

                var genericType = typeof(JsvWriter<>).MakeGenericType(type);
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

		public static void WriteLateBoundObject(TextWriter writer, object value)
		{
			if (value == null) return;
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

	/// <summary>
	/// Implement the serializer using a more static approach
	/// </summary>
	/// <typeparam name="T"></typeparam>
	internal static class JsvWriter<T>
	{
        private static readonly WriteValueDelegate<T> CacheFn;

        public static WriteValueDelegate<T> WriteFn()
		{
            return (writer, obj) => CacheFn(writer, obj);
		}

		static JsvWriter()
		{
            CacheFn = typeof(T) == typeof(object)
                ? ((writer, obj) => JsvWriter.WriteLateBoundObject(writer, obj)) 
                : JsvWriter.Instance.GetWriteFn<T>();
        }

	    public static void WriteObject(TextWriter writer, object value)
		{
			CacheFn(writer, (T)value);
		}
	}
}