using System;

namespace StrobelStack.Text.Tests.DynamicModels.DataModel
{
	public class StrictType
	{
		public string Name { get; set; }
		public Type Type { get; set; }
		public CustomCollectionDto Value { get; set; }
	}
}