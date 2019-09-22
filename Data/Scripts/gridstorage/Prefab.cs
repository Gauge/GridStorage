using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;

namespace GridStorage
{
	[ProtoContract]
	public class Prefab
	{
		[ProtoMember(1)]
		public List<string> Grids = new List<string>();
	}
}
