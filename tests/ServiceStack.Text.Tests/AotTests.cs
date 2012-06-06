using NUnit.Framework;
using StrobelStack.Text.Tests.Support;

namespace StrobelStack.Text.Tests
{
	[TestFixture]
	public class AotTests
	{
#if SILVERLIGHT || MONOTOUCH
		[Test]
		public void Can_Register_AOT()
		{
			JsConfig.RegisterForAot();
		}
#endif
	}
}