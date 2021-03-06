using System;
using System.IO;
using StrobelStack.Text.Json;

namespace StrobelStack.Text.Common
{
	internal interface ITypeSerializer
	{
		string TypeAttrInObject { get; }

        WriteValueDelegate<T> GetWriteFn<T>();
		WriteObjectDelegate GetWriteFn(Type type);
		TypeInfo GetTypeInfo(Type type);

		void WriteTypeInfo(TextWriter writer, object value);

		void WriteRawString(TextWriter writer, string value);
		void WritePropertyName(TextWriter writer, string value);

		void WriteBuiltIn<T>(TextWriter writer, T value);
		void WriteObjectString(TextWriter writer, object value);
		void WriteException(TextWriter writer, object value);
		void WriteString(TextWriter writer, string value);
		void WriteDateTime(TextWriter writer, object oDateTime);
		void WriteNullableDateTime(TextWriter writer, object dateTime);
		void WriteDateTimeOffset(TextWriter writer, object oDateTimeOffset);
		void WriteNullableDateTimeOffset(TextWriter writer, object dateTimeOffset);
        void WriteTimeSpan(TextWriter writer, object dateTimeOffset);
        void WriteNullableTimeSpan(TextWriter writer, object dateTimeOffset);
		void WriteGuid(TextWriter writer, object oValue);
		void WriteNullableGuid(TextWriter writer, object oValue);
		void WriteBytes(TextWriter writer, object oByteValue);
		void WriteChar(TextWriter writer, object charValue);
		void WriteByte(TextWriter writer, object byteValue);
		void WriteInt16(TextWriter writer, object intValue);
		void WriteUInt16(TextWriter writer, object intValue);
		void WriteInt32(TextWriter writer, int? intValue);
		void WriteUInt32(TextWriter writer, object uintValue);
		void WriteInt64(TextWriter writer, object longValue);
		void WriteUInt64(TextWriter writer, object ulongValue);
		void WriteBool(TextWriter writer, object boolValue);
		void WriteFloat(TextWriter writer, object floatValue);
		void WriteDouble(TextWriter writer, object doubleValue);
        void WriteDecimal(TextWriter writer, object decimalValue);
        void WriteEnum(TextWriter writer, object enumValue);
        void WriteEnumFlags(TextWriter writer, object enumFlagValue);
		void WriteLinqBinary(TextWriter writer, object linqBinaryValue);

		//object EncodeMapKey(object value);

		ParseStringDelegate GetParseFn<T>();
		ParseStringDelegate GetParseFn(Type type);

		string ParseRawString(string value);
		string ParseString(string value);
		string EatTypeValue(string value, ref int i);
		bool EatMapStartChar(string value, ref int i);
		string EatMapKey(string value, ref int i);
		bool EatMapKeySeperator(string value, ref int i);
		string EatValue(string value, ref int i);
		bool EatItemSeperatorOrMapEndChar(string value, ref int i);
	}
}