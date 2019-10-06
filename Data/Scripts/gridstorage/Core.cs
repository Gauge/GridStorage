using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using System.IO;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace GridStorage
{
	public enum NetworkCommands { BlockPropertiesUpdate };

	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public class Core : MySessionComponentBase
	{
		public static NetworkAPI Network => NetworkAPI.Instance;

		private const ushort ModId = 65489;
		private const string ModName = "Storage Block";

		public const string Command_Store = "store";
		public const string Command_Preview = "preview";
		public const string Command_Place = "place";

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			if (!NetworkAPI.IsInitialized)
			{
				NetworkAPI.Init(ModId, ModName);
			}

			if (MyAPIGateway.Multiplayer.IsServer)
			{
				Network.RegisterNetworkCommand(Command_Store, Store_Server);
				Network.RegisterNetworkCommand(Command_Preview, Preview_Server);
				Network.RegisterNetworkCommand(Command_Place, Place_Server);
			}
			else
			{
				Network.RegisterNetworkCommand(Command_Store, Store_Client);
				Network.RegisterNetworkCommand(Command_Preview, Preview_Client);
				Network.RegisterNetworkCommand(Command_Place, Place_Client);
			}
		}

		private void Store_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			StoreGridData storeData = MyAPIGateway.Utilities.SerializeFromBinary<StoreGridData>(data);

			Prefab fab = SavePrefab(storeData.BlockId, storeData.Target);

			if (fab != null)
			{
				Network.SendCommand(Command_Store, data: MyAPIGateway.Utilities.SerializeToBinary(fab), steamId: steamId);
			}
		}

		private void Store_Client(ulong steamId, string command, byte[] data, DateTime timestamp)
		{

			Prefab fab = MyAPIGateway.Utilities.SerializeFromBinary<Prefab>(data);

			

		}

		private void Preview_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{

		}

		private void Preview_Client(ulong steamId, string command, byte[] data, DateTime timestamp)
		{

		}

		private void Place_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{

		}

		private void Place_Client(ulong steamId, string command, byte[] data, DateTime timestamp)
		{

		}

		protected override void UnloadData()
		{
			NetworkAPI.Dispose();
		}

		private Prefab SavePrefab(long gridId, long storageBlockId)
		{
			try
			{
				IMyCubeGrid selectedGrid = (IMyCubeGrid)MyAPIGateway.Entities.GetEntityById(gridId);

				// create prefab from parent and subgrids
				Prefab prefab = new Prefab();
				// forced into doing strings cause the dumb serializer is erroring out otherwise.
				prefab.Grids.Add(MyAPIGateway.Utilities.SerializeToXML((MyObjectBuilder_CubeGrid)selectedGrid.GetObjectBuilder()));

				List<IMyCubeGrid> grids = MyAPIGateway.GridGroups.GetGroup(selectedGrid, GridLinkTypeEnum.Mechanical);
				grids.Remove(selectedGrid);

				for (int i = 0; i < grids.Count; i++)
				{
					prefab.Grids.Add(MyAPIGateway.Utilities.SerializeToXML((MyObjectBuilder_CubeGrid)grids[i].GetObjectBuilder()));
				}

				// update terminal display

				string baseName = selectedGrid.DisplayName;
				int index = 0;
				string prefabName;
				Type t = typeof(Prefab);

				do
				{
					index++;
					prefabName = $"{baseName}{((index > 1) ? $"_{index}" : "")}";
				}
				while (MyAPIGateway.Utilities.FileExistsInWorldStorage(prefabName, t));

				MyLog.Default.Info($"[Grid Storage] Attempting to save \"{prefabName}\"");

				// write prefab to file

				string data = MyAPIGateway.Utilities.SerializeToXML(prefab);

				TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage($"{storageBlockId}_{prefabName}", typeof(Prefab));
				writer.Write(data);
				writer.Close();

				return prefab;

				//MyLog.Default.Info($"[Grid Storage] Removing grids");

				//// remove existing grid from world
				//MyAPIGateway.Entities.MarkForClose(selectedGrid);

				//foreach (IMyCubeGrid grid in grids)
				//{
				//	MyAPIGateway.Entities.MarkForClose(grid);
				//}

			}
			catch (Exception e)
			{
				MyLog.Default.Error(e.ToString());
			}

			return null;
		}

		private Prefab LoadPrefab(long entityId, string gridName)
		{
			try
			{
				string filename = $"{entityId}_{gridName}";

				if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(Prefab)))
				{
					return null;
				}

				TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(Prefab));
				string text = reader.ReadToEnd();
				reader.Close();

				Prefab prefab = MyAPIGateway.Utilities.SerializeFromXML<Prefab>(text);

				return prefab;

			}
			catch (Exception e)
			{
				MyLog.Default.Error(e.ToString());
				return null;
			}
		}
	}
}
