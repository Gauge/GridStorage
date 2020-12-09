using ProtoBuf;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage;
using VRage.Game;

namespace GridStorage
{
	[ProtoContract]
	public class Prefab
	{
		[ProtoMember(2)]
		public string Name;
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

			for (int i = 0; i < list.Count; i++)
			{
				MyObjectBuilder_CubeGrid grid = list[i];
				grid.Name = i.ToString();

				foreach (MyObjectBuilder_CubeBlock cubeBlock in grid.CubeBlocks)
				{
					if (cubeBlock is MyObjectBuilder_Cockpit)
					{
						(cubeBlock as MyObjectBuilder_Cockpit).ClearPilotAndAutopilot();

						MyObjectBuilder_CryoChamber myObjectBuilder_CryoChamber = cubeBlock as MyObjectBuilder_CryoChamber;
						myObjectBuilder_CryoChamber?.Clear();
					}
				}
			}

			return list;
		}
	}

	[ProtoContract]
	public class StoreGridData
	{
		[ProtoMember(1)]
		public long GarageId;

		[ProtoMember(2)]
		public long TargetId;
	}

	[ProtoContract]
	public class PreviewGridData
	{
		[ProtoMember(1)]
		public long GarageId;

		[ProtoMember(2)]
		public int Index;

		[ProtoMember(3)]
		public Prefab Prefab;
	}


	[ProtoContract]
	public class PlaceGridData
	{
		[ProtoMember(1)]
		public long GarageId;

		[ProtoMember(2)]
		public int GridIndex;

		[ProtoMember(3)]
		public string GridName;

		[ProtoMember(4)]
		public long NewOwner;

		[ProtoMember(5)]
		public List<MyPositionAndOrientation> MatrixData = new List<MyPositionAndOrientation>();
	}

	[ProtoContract]
	public class StorageData
	{
		[ProtoMember(1)]
		public List<Prefab> StoredGrids = new List<Prefab>();
	}
}
