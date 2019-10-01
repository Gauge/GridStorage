using ProtoBuf;
using System.Collections.Generic;

namespace GridStorage
{
	[ProtoContract]
	public class Prefab
	{
		[ProtoMember(1)]
		public List<string> Grids = new List<string>();
	}
}
