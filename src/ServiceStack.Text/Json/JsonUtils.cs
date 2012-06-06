using System;
using System.IO;

namespace StrobelStack.Text.Json
{
    public static class JsonUtils
    {
        public const char EscapeChar = '\\';
        public const char QuoteChar = '"';
        public const string Null = "null";
        public const string True = "true";
        public const string False = "false";

        static readonly char[] EscapeChars = new[]
		{
			QuoteChar, '\n', '\r', '\t', '"', '\\', '\f', '\b',
		};

        private const int LengthFromLargestChar = '\\' + 1;
        private static readonly bool[] EscapeCharFlags = new bool[LengthFromLargestChar];

        static JsonUtils()
        {
            foreach (var escapeChar in EscapeChars)
            {
                EscapeCharFlags[escapeChar] = true;
            }
        }

        public static void WriteString(TextWriter writer, string value, bool quoteUnescapedStrings = true)
        {
            if (value == null)
            {
                writer.Write(JsonUtils.Null);
                return;
            }
            if (!HasAnyEscapeChars(value))
            {
                if (quoteUnescapedStrings)
                    writer.Write(QuoteChar);
                writer.Write(value);
                if (quoteUnescapedStrings)
                    writer.Write(QuoteChar);
                return;
            }

            var hexSeqBuffer = new char[4];
            writer.Write(QuoteChar);

            var len = value.Length;
            for (var i = 0; i < len; i++)
            {
                switch (value[i])
                {
                    case '\n':
                        writer.Write("\\n");
                        continue;

                    case '\r':
                        writer.Write("\\r");
                        continue;

                    case '\t':
                        writer.Write("\\t");
                        continue;

                    case '"':
                    case '\\':
                        writer.Write('\\');
                        writer.Write(value[i]);
                        continue;

                    case '\f':
                        writer.Write("\\f");
                        continue;

                    case '\b':
                        writer.Write("\\b");
                        continue;
                }

                //Is printable char?
                if (value[i] >= 32 && value[i] <= 126)
                {
                    writer.Write(value[i]);
                    continue;
                }

                var isValidSequence = value[i] < 0xD800 || value[i] > 0xDFFF;
                if (isValidSequence)
                {
                    // Default, turn into a \uXXXX sequence
                    IntToHex(value[i], hexSeqBuffer);
                    writer.Write("\\u");
                    writer.Write(hexSeqBuffer);
                }
            }

            writer.Write(QuoteChar);
        }

        /// <summary>
        /// micro optimizations: using flags instead of value.IndexOfAny(EscapeChars)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool HasAnyEscapeChars(string value)
        {
            var len = value.Length;
            for (var i = 0; i < len; i++)
            {
                var c = value[i];
                if (c >= LengthFromLargestChar || !EscapeCharFlags[c]) continue;
                return true;
            }
            return false;
        }

        public static void IntToHex(int intValue, char[] hex)
        {
            for (var i = 0; i < 4; i++)
            {
                var num = intValue % 16;

                if (num < 10)
                    hex[3 - i] = (char)('0' + num);
                else
                    hex[3 - i] = (char)('A' + (num - 10));

                intValue >>= 4;
            }
        }

        public static bool IsJsObject(string value)
        {
            return !string.IsNullOrEmpty(value)
                && value[0] == '{'
                && value[value.Length - 1] == '}';
        }

    }

/*
	public static class JsonUtils
	{
		public const char EscapeChar = '\\';
		public const char QuoteChar = '"';
		public const string Null = "null";
		public const string True = "true";
		public const string False = "false";

		static readonly char[] EscapeChars = new[]
			{
				QuoteChar, '\n', '\r', '\t', '"', '\\', '\f', '\b',
			};

		private const int LengthFromLargestChar = '\\' + 1;
		private static readonly bool[] EscapeCharFlags = new bool[LengthFromLargestChar];
	    private static readonly char[] HexChars = new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

		static JsonUtils()
		{
			foreach (var escapeChar in EscapeChars)
			{
				EscapeCharFlags[escapeChar] = true;
			}
		}

        public static void WriteRawString(TextWriter writer, string value)
        {
            if (value == null)
            {
                writer.Write(Null);
                return;
            }

            writer.Write(QuoteChar);
            writer.Write(value);
            writer.Write(QuoteChar);
        }

	    public static void WriteString(TextWriter writer, string value)
	    {
	        if (value == null)
	        {
	            writer.Write(Null);
	            return;
	        }

	        writer.Write(QuoteChar);

	        var len = value.Length;
	        var start = 0;
	        var end = NextEscapeCharIndex(value, start);

	        var sb = ((StringWriter)writer).GetStringBuilder();

	        while (true)
	        {
	            sb.Append(value, start, end - start);
                
                if (end == len)
                    break;

	            WriteEscapedCharacter(writer, value[end]);

	            start = end + 1;

	            if (start >= len)
                    break;

	            end = NextEscapeCharIndex(value, start);
	        }

	        writer.Write(QuoteChar);
	    }

	    private static void WriteEscapedCharacter(TextWriter writer, char ch)
	    {
	        switch (ch)
	        {
	            case '\n':
	                writer.Write("\\n");
	                break;

	            case '\r':
	                writer.Write("\\r");
	                break;

	            case '\t':
	                writer.Write("\\t");
	                break;

	            case '"':
	            case '\\':
	                writer.Write('\\');
	                writer.Write(ch);
	                break;

	            case '\f':
	                writer.Write("\\f");
	                break;

	            case '\b':
	                writer.Write("\\b");
	                break;

	            default:
	                var isValidSequence = ch < 0xD800 || ch > 0xDFFF;
	                if (isValidSequence)
	                {
	                    // Default, turn into a \uXXXX sequence
	                    WriteHex(writer, ch);
	                }
	                break;
	        }
	    }

	    private static void WriteHex(TextWriter writer, char ch)
	    {
            writer.Write("\\u");
            writer.Write(HexChars[(ch >> 12) & 0xF]);
            writer.Write(HexChars[(ch >> 8) & 0xF]);
            writer.Write(HexChars[(ch >> 4) & 0xF]);
            writer.Write(HexChars[ch & 0xF]);
	    }

	    /// <summary>
		/// micro optimizations: using flags instead of value.IndexOfAny(EscapeChars)
		/// </summary>
		/// <param name="value"></param>
		/// <returns></returns>
		internal static bool HasAnyEscapeChars(string value)
		{
		    var len = value.Length;
		    for (var i = 0; i < len; i++)
		    {
		        var c = value[i];
		        if (c >= LengthFromLargestChar || !EscapeCharFlags[c])
		            continue;
		        return true;
		    }
		    return false;
		}

		private static int NextEscapeCharIndex(string value, int start)
		{
		    var len = value.Length;
            for (var i = start; i < len; i++)
		    {
		        var c = value[i];
		        if (c >= LengthFromLargestChar || !EscapeCharFlags[c])
		            continue;
		        return i;
		    }
		    return len;
		}

	    public static void IntToHex(int intValue, char[] hex)
		{
			for (var i = 0; i < 4; i++)
			{
				var num = intValue % 16;

				if (num < 10)
					hex[3 - i] = (char)('0' + num);
				else
					hex[3 - i] = (char)('A' + (num - 10));

				intValue >>= 4;
			}
		}

		public static bool IsJsObject(string value)
		{
			return !string.IsNullOrEmpty(value)
				&& value[0] == '{'
				&& value[value.Length - 1] == '}';
		}
	}
*/
}