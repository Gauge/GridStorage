using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using System.IO;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

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

		private static DateTime storeTime = DateTime.MinValue;
		private static DateTime placementTime = DateTime.MinValue;

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			NetworkAPI.LogNetworkTraffic = true;

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
				Network.RegisterNetworkCommand(Command_Preview, Preview_Client);
			}
		}

		private void Store_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			if (TimeSpan.FromTicks(timestamp.Ticks - storeTime.Ticks).Milliseconds < 100)
			{
				MyLog.Default.Warning($"[Grid Storage] User {steamId} attempted to store a grid too soon");
				return;
			}
			else
			{
				MyLog.Default.Info($"[Grid Storage] User {steamId} attempting to store grid.");
				storeTime = timestamp;
			}

			StoreGridData storeData = MyAPIGateway.Utilities.SerializeFromBinary<StoreGridData>(data);
			string prefabName = SavePrefab(storeData.Target, storeData.BlockId);

			MyLog.Default.Info($"[Grid Storage] Storing: <{storeData.BlockId}> - {prefabName}");

			IMyEntity ent = MyAPIGateway.Entities.GetEntityById(storeData.BlockId);
			GridStorageBlock block = ent.GameLogic as GridStorageBlock;
			block.GridList.Value.Add(prefabName);
			block.GridList.Push();
		}

		private void Preview_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			PreviewGridData preview = MyAPIGateway.Utilities.SerializeFromBinary<PreviewGridData>(data);

			Prefab fab = LoadPrefab(preview.BlockId, preview.GridName);
			preview.Prefab = fab;

			if (fab == null)
			{
				MyLog.Default.Warning($"[Grid Storage] Failed to find grid \"{preview.GridName}\"");

				IMyEntity ent = MyAPIGateway.Entities.GetEntityById(preview.BlockId);
				GridStorageBlock block = ent.GameLogic as GridStorageBlock;
				block.ValidateGridList();
				return;
			}

			Network.SendCommand(Command_Preview, null, MyAPIGateway.Utilities.SerializeToBinary(preview), steamId: steamId);

		}

		private void Preview_Client(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			PreviewGridData preview = MyAPIGateway.Utilities.SerializeFromBinary<PreviewGridData>(data);

			IMyEntity ent = MyAPIGateway.Entities.GetEntityById(preview.BlockId);
			GridStorageBlock block = ent.GameLogic as GridStorageBlock;
			block.GridsToPlace = preview.Prefab.UnpackGrids();
		}


		private void Place_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{

			if (TimeSpan.FromTicks(timestamp.Ticks - placementTime.Ticks).Milliseconds < 100)
			{
				MyLog.Default.Warning($"[Grid Storage] User {steamId} attempted to place a grid too soon.");
				return;
			}
			else
			{
				MyLog.Default.Info($"[Grid Storage] User {steamId} attempting to place grid.");
				placementTime = timestamp;
			}

			PlaceGridData place = MyAPIGateway.Utilities.SerializeFromBinary<PlaceGridData>(data);
			Prefab fab = LoadPrefab(place.BlockId, place.GridName);

			IMyEntity ent = MyAPIGateway.Entities.GetEntityById(place.BlockId);
			GridStorageBlock block = ent.GameLogic as GridStorageBlock;

			if (fab == null)
			{
				MyLog.Default.Warning($"[Grid Storage] Failed to find grid \"{place.GridName}\"");		
			}
			else
			{
				PlacePrefab(fab.UnpackGrids(), place.Position, $"{place.BlockId}_{place.GridName}");
				block.RemoveGridFromList(place.GridName);

			}
		}

		public static string SavePrefab(long gridId, long storageBlockId)
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
				while (MyAPIGateway.Utilities.FileExistsInWorldStorage($"{storageBlockId}_{prefabName}", t));

				MyLog.Default.Info($"[Grid Storage] Attempting to save \"{prefabName}\"");

				// write prefab to file

				string data = MyAPIGateway.Utilities.SerializeToXML(prefab);

				TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage($"{storageBlockId}_{prefabName}", typeof(Prefab));
				writer.Write(data);
				writer.Close();

				// this is a work around because keens DeleteFileInWorldStorage is using a local storage check instead of a world storage check.
				// i created this dumby file to be a stand in
				writer = MyAPIGateway.Utilities.WriteFileInLocalStorage($"{storageBlockId}_{prefabName}", typeof(Prefab));
				writer.Write("");
				writer.Close();

				// remove existing grid from world
				MyAPIGateway.Entities.MarkForClose(selectedGrid);

				foreach (IMyCubeGrid grid in grids)
				{
					MyAPIGateway.Entities.MarkForClose(grid);
				}

				return prefabName;

			}
			catch (Exception e)
			{
				MyLog.Default.Error(e.ToString());
			}

			return null;
		}

		public static Prefab LoadPrefab(long entityId, string gridName)
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

		public static void PlacePrefab(List<MyObjectBuilder_CubeGrid> grids, Vector3D position, string filename)
		{
			if (grids == null || grids.Count == 0)
			{
				return;
			}

			MyAPIGateway.Entities.RemapObjectBuilderCollection(grids);
			foreach (MyObjectBuilder_CubeGrid grid in grids)
			{
				grid.AngularVelocity = new SerializableVector3();
				grid.LinearVelocity = new SerializableVector3();
				grid.XMirroxPlane = null;
				grid.YMirroxPlane = null;
				grid.ZMirroxPlane = null;
				grid.IsStatic = false;
				grid.CreatePhysics = true;
				grid.IsRespawnGrid = false;
			}

			Vector3D parentPosition = grids[0].PositionAndOrientation.Value.Position;

			foreach (MyObjectBuilder_CubeGrid grid in grids)
			{
				Vector3D offset = parentPosition - grid.PositionAndOrientation.Value.Position;
				grid.PositionAndOrientation = new MyPositionAndOrientation((position - offset), grid.PositionAndOrientation.Value.Forward, grid.PositionAndOrientation.Value.Up);


				MyCubeGrid childGrid = (MyCubeGrid)MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(grid);
				childGrid.Render.Visible = true;
			}

			MyAPIGateway.Utilities.DeleteFileInWorldStorage(filename, typeof(Prefab));

		}

		protected override void UnloadData()
		{
			NetworkAPI.Dispose();
		}

	}
}
