using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRage.Game;

namespace GridStorage
{
	//[ProtoContract]
	//public class GridGroup
	//{
	//	[ProtoMember(1)]
	//	public SerializableVector3I parent;

	//	[ProtoMember(2)]
	//	public SerializableVector3I child;
	//}

	[ProtoContract]
	public class Prefab
	{
		[ProtoMember(1)]
		public List<string> Grids = new List<string>();

		//[ProtoMember(2)]
		//public List<GridGroup> Group = new List<GridGroup>();
	}
}
