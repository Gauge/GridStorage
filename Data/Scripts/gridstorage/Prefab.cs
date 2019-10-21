using ProtoBuf;
using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage;
using VRage.Game;

namespace GridStorage
{
	[ProtoContract]
	public class Prefab
	{
		// Need to save as XML cause keen serializing ObjectBuilder_CubeGrid doesn't work
		[ProtoMember(3)]
		public List<string> Grids = new List<string>();

		public List<MyObjectBuilder_CubeGrid> UnpackGrids()
		{
			List<MyObjectBuilder_CubeGrid> list = new List<MyObjectBuilder_CubeGrid>();

			foreach (string gridXML in Grids)
			{
				list.Add(MyAPIGateway.Utilities.SerializeFromXML<MyObjectBuilder_CubeGrid>(gridXML));
			}

			return list;
		}
	}

	[ProtoContract]
	public class StoreGridData
	{
		[ProtoMember(1)]
		public long BlockId;

		[ProtoMember(2)]
		public long Target;
	}

	[ProtoContract]
	public class PreviewGridData
	{
		[ProtoMember(1)]
		public long BlockId;

		[ProtoMember(2)]
		public string GridName;

		[ProtoMember(3)]
		public Prefab Prefab;
	}

	[ProtoContract]
	public class PlaceGridData
	{
		[ProtoMember(1)]
		public long BlockId;

		[ProtoMember(2)]
		public string GridName;

		[ProtoMember(3)]
		public SerializableVector3D Position;
	}
}
