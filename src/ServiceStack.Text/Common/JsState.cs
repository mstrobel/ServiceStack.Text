using System;

namespace StrobelStack.Text.Common
{
	internal static class JsState
	{
		//Exposing field for perf
		[ThreadStatic] internal static int WritingKeyCount = 0;

		[ThreadStatic] internal static bool IsWritingValue = false;

		[ThreadStatic] internal static bool IsWritingDynamic = false;

		[ThreadStatic] internal static bool IsJson = false;
	}
}