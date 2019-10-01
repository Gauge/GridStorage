using ProtoBuf;
using System.Collections.Generic;

namespace GridStorage
{
	[ProtoContract]
	public class BlockProperties
	{
		[ProtoMember(1)]
		public long BlockId;
		[ProtoMember(2)]
		public List<string> GridNames = new List<string>();
	}
}
