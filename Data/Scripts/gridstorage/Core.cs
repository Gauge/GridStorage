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
		public const string Command_Settings = "settings";

		public int waitInterval = 0;

		private static DateTime storeTime = DateTime.MinValue;
		private static DateTime placementTime = DateTime.MinValue;

		public static Settings Config;

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			NetworkAPI.LogNetworkTraffic = true;

			if (!NetworkAPI.IsInitialized)
			{
				NetworkAPI.Init(ModId, ModName);
			}

			if (MyAPIGateway.Multiplayer.IsServer)
			{
				Config = Settings.Load();
				Network.RegisterNetworkCommand(Command_Store, Store_Server);
				Network.RegisterNetworkCommand(Command_Preview, Preview_Server);
				Network.RegisterNetworkCommand(Command_Place, Place_Server);
				Network.RegisterNetworkCommand(Command_Settings, Settings_Server);

			}
			else
			{
				SetUpdateOrder(MyUpdateOrder.BeforeSimulation);
				Config = Settings.GetDefaults();
				Network.RegisterNetworkCommand(Command_Preview, Preview_Client);
				Network.RegisterNetworkCommand(Command_Settings, Settings_Client);
			}
		}

		public override void UpdateBeforeSimulation()
		{
			waitInterval++;

			if (waitInterval == 120) 
			{
				MyLog.Default.Info($"[Grid Garage] requesting server config file");
				Network.SendCommand(Command_Settings);
				waitInterval = 0;
			}
		}


		private void Settings_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			try 
			{
				Network.SendCommand(Command_Settings, null, MyAPIGateway.Utilities.SerializeToBinary(Config), steamId: steamId);
			}
			catch (Exception e)
			{
				MyLog.Default.Error(e.ToString());
			}
		}

		private void Store_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			try
			{
				if (TimeSpan.FromTicks(timestamp.Ticks - storeTime.Ticks).Milliseconds < 100)
				{
					MyLog.Default.Warning($"[Grid Garage] User {steamId} attempted to store a grid too soon");
					return;
				}
				else
				{
					MyLog.Default.Info($"[Grid Garage] User {steamId} attempting to store grid.");
					storeTime = timestamp;
				}

				StoreGridData storeData = MyAPIGateway.Utilities.SerializeFromBinary<StoreGridData>(data);
				string prefabName = SavePrefab(storeData.TargetId, storeData.GarageGuid);

				IMyEntity ent = MyAPIGateway.Entities.GetEntityById(storeData.GarageEntityId);
				GridStorageBlock block = ent.GameLogic as GridStorageBlock;
				block.GridList.Value.Add(prefabName);
				block.GridList.Push();
			}
			catch (Exception e)
			{
				MyLog.Default.Error(e.ToString());
			}
		}

		private void Preview_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			try
			{
				PreviewGridData preview = MyAPIGateway.Utilities.SerializeFromBinary<PreviewGridData>(data);

				Prefab fab = LoadPrefab(preview.GarageEntityId, preview.GarageGuid, preview.GridName);
				preview.Prefab = fab;

				if (fab == null)
				{
					MyLog.Default.Warning($"[Grid Garage] Failed to find grid \"{preview.GridName}\"");

					IMyEntity ent = MyAPIGateway.Entities.GetEntityById(preview.GarageEntityId);
					GridStorageBlock block = ent.GameLogic as GridStorageBlock;
					block.RemoveGridFromList(preview.GridName);
					block.GridList.Push();
					return;
				}

				Network.SendCommand(Command_Preview, null, MyAPIGateway.Utilities.SerializeToBinary(preview), steamId: steamId);
			}
			catch (Exception e)
			{
				MyLog.Default.Error(e.ToString());
			}
		}

		private void Settings_Client(ulong steamId, string command, byte[] data, DateTime timestamp) 
		{
			try
			{
				Config = MyAPIGateway.Utilities.SerializeFromBinary<Settings>(data);
				SetUpdateOrder(MyUpdateOrder.NoUpdate);
			}
			catch (Exception e)
			{
				MyLog.Default.Error(e.ToString());
			}
		}

		private void Preview_Client(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			try
			{
				PreviewGridData preview = MyAPIGateway.Utilities.SerializeFromBinary<PreviewGridData>(data);

				if (preview.Prefab == null)
				{
					MyAPIGateway.Utilities.ShowNotification("Grid could not be found", 2000, "Red");
				}
				else 
				{
					IMyEntity ent = MyAPIGateway.Entities.GetEntityById(preview.GarageEntityId);
					GridStorageBlock block = ent.GameLogic as GridStorageBlock;
					block.GridsToPlace = preview.Prefab.UnpackGrids();
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error(e.ToString());
			}
		}

		private void Place_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			try
			{
				if (TimeSpan.FromTicks(timestamp.Ticks - placementTime.Ticks).Milliseconds < 100)
				{
					MyLog.Default.Warning($"[Grid Garage] User {steamId} attempted to place a grid too soon.");		
					return;
				}
				else
				{
					MyLog.Default.Info($"[Grid Garage] User {steamId} attempting to place grid.");
				}

				placementTime = timestamp;

				PlaceGridData place = MyAPIGateway.Utilities.SerializeFromBinary<PlaceGridData>(data);
				Prefab fab = LoadPrefab(place.GarageEntityId, place.GarageGuid, place.GridName);
				IMyEntity ent = MyAPIGateway.Entities.GetEntityById(place.GarageEntityId);
				GridStorageBlock block = ent.GameLogic as GridStorageBlock;

				if (fab != null)
				{
					PlacePrefab(fab.UnpackGrids(), place.Position, Tools.CreateFilename(place.GarageGuid, place.GridName), place.NewOwner);
					block.RemoveGridFromList(place.GridName);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error(e.ToString());
			}
		}

		public static string SavePrefab(long gridId, string garageGuid)
		{
			try
			{
				// prep data to store
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

				// find free filename
				FileIndex fileIndex = FileIndex.GetFileIndex();
				string baseName = selectedGrid.DisplayName;
				int index = 0;
				string prefabName;
				Type t = typeof(Prefab);

				// some grids have the same name. this adds a number to the end of identically named ships
				do
				{
					index++;
					prefabName = $"{baseName}{((index > 1) ? $"_{index}" : "")}";
				}
				while (fileIndex.FileNames.Contains(prefabName));

				// write prefab to file
				MyLog.Default.Info($"[Grid Garage] Attempting to save \"{prefabName}\"");

				string filename = Tools.CreateFilename(garageGuid, prefabName);
				string data = MyAPIGateway.Utilities.SerializeToXML(prefab);
				TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(filename, typeof(Prefab));
				writer.Write(data);
				writer.Close();

				// this can be removed when keen fixes their bug.
				writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(filename, typeof(Prefab));
				writer.Write("");
				writer.Close();

				fileIndex.FileNames.Add(filename);
				fileIndex.Save();

				MyLog.Default.Info($"[Grid Garage] Prefab {prefabName} stored successfully");

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
				MyLog.Default.Error($"[Grid Garage] Failed to save grid prefab:\n{e.ToString()}");
				return null;
			}
		}

		public static Prefab LoadPrefab(long garageEntityId, string garageGuid, string gridName)
		{
			string filename = Tools.CreateFilename(garageGuid, gridName);
			MyLog.Default.Info($"[Grid Garage] Loading prefab file: {filename}");

			FileIndex fileIndex = FileIndex.GetFileIndex();
			if (!fileIndex.FileNames.Contains(filename))
			{
				MyLog.Default.Warning($"[Grid Garage] File could not be found.");
				return null;
			}

			try
			{
				TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(Prefab));
				string text = reader.ReadToEnd();
				reader.Close();

				Prefab prefab = MyAPIGateway.Utilities.SerializeFromXML<Prefab>(text);
				return prefab;
			}
			catch (FileNotFoundException e) 
			{
				MyLog.Default.Info($"[Grid Garage] No file found matching the filename: {filename}");
				fileIndex.FileNames.Remove(filename);

				try
				{
					IMyEntity ent = MyAPIGateway.Entities.GetEntityById(garageEntityId);
					GridStorageBlock block = ent.GameLogic as GridStorageBlock;
					block.RemoveGridFromList(filename);
					block.GridList.Push();
				}
				catch (Exception err) 
				{
					MyLog.Default.Error($"[Grid Storage] Failed to update grid list: {err.ToString()}");
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed to load grid prefab:\n{e.ToString()}");
			}

			return null;
		}

		public static void PlacePrefab(List<MyObjectBuilder_CubeGrid> grids, Vector3D position, string filename, long ownerId)
		{
			try
			{
				MyLog.Default.Info($"[Grid Garage] Spawning grid from: {filename}");

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

					foreach (MyObjectBuilder_CubeBlock cubeBlock in grid.CubeBlocks)
					{
						cubeBlock.Owner = ownerId;
					}

					MyCubeGrid childGrid = (MyCubeGrid)MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(grid);
					childGrid.Render.Visible = true;

				}

				FileIndex fileIndex = FileIndex.GetFileIndex();
				fileIndex.FileNames.Remove(filename);
				fileIndex.Save();

				MyAPIGateway.Utilities.DeleteFileInWorldStorage(filename, typeof(Prefab));
				// this can be removed when keen fixes their bug.
				MyAPIGateway.Utilities.DeleteFileInLocalStorage(filename, typeof(Prefab));
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed to spawn grid:\n{e.ToString()}");
			}
		}

		protected override void UnloadData()
		{
			NetworkAPI.Dispose();
		}

	}
}
