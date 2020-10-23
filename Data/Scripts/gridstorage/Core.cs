using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace GridStorage
{
	public enum NetworkCommands { BlockPropertiesUpdate };

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class Core : MySessionComponentBase
	{
		public static NetworkAPI Network => NetworkAPI.Instance;

		private const ushort ModId = 65489;
		private const string ModName = "Grid Garage";

		public const string Command_Error = "error";
		public const string Command_Store = "store";
		public const string Command_Place = "place";
		public const string Command_Settings = "settings";
		public const string Command_Preview = "preview";

		public int waitInterval = 0;
		bool SettingsUpdated = false;

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
				SettingsUpdated = true;
				Config = Settings.Load();
				Network.RegisterNetworkCommand(Command_Store, Store_Server);
				Network.RegisterNetworkCommand(Command_Place, Place_Server);
				Network.RegisterNetworkCommand(Command_Settings, Settings_Server);
				Network.RegisterNetworkCommand(Command_Preview, Preview_Server);
			}
			else
			{
				Config = Settings.GetDefaults();
				Network.RegisterNetworkCommand(Command_Settings, Settings_Client);
				Network.RegisterNetworkCommand(Command_Error, Error_Client);
				Network.RegisterNetworkCommand(Command_Preview, Preview_Client);
			}
		}



		public override void UpdateBeforeSimulation()
		{
			if (SettingsUpdated)
				return;

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
				MyLog.Default.Error($"[Grid Garage] Error in function Settings_Server: {e.ToString()}");
			}
		}
		private void Settings_Client(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			try
			{
				Config = MyAPIGateway.Utilities.SerializeFromBinary<Settings>(data);
				SettingsUpdated = true;

				if (NetworkAPI.LogNetworkTraffic)
				{
					MyLog.Default.Info($"Recived Config, Spawn: {Config.SpawnCooldown}, Storage: {Config.StorageCooldown}");
				}

			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Error in function Settings_Client: {e.ToString()}");
			}
		}

		private void Preview_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			try
			{
				PreviewGridData preview = MyAPIGateway.Utilities.SerializeFromBinary<PreviewGridData>(data);
				GridStorageBlock block = MyAPIGateway.Entities.GetEntityById(preview.GarageId).GameLogic.GetAs<GridStorageBlock>();

				if (preview.Index > -1 && preview.Index < block.GridList.Count)
				{
					preview.Prefab = block.GridList[preview.Index];
					Network.SendCommand(Command_Preview, data: MyAPIGateway.Utilities.SerializeToBinary(preview), steamId: steamId);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Error in function Preview_Server: {e.ToString()}");
			}
		}

		private void Store_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			try
			{
				if ((timestamp - storeTime).TotalMilliseconds < 200)
				{
					MyLog.Default.Warning($"[Grid Garage] User {steamId} attempted to store a grid too soon");
					return;
				}
				else
				{
					MyLog.Default.Info($"[Grid Garage] User {steamId} attempting to store grid.");
					storeTime = timestamp;
				}

				StoreGridData store = MyAPIGateway.Utilities.SerializeFromBinary<StoreGridData>(data);
				GridStorageBlock block = MyAPIGateway.Entities.GetEntityById(store.GarageId).GameLogic.GetAs<GridStorageBlock>();
				block.StorePrefab(store.TargetId);
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Error in function Store_Server: {e.ToString()}");
			}
		}

		private void Place_Server(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			try
			{
				if (TimeSpan.FromTicks(timestamp.Ticks - placementTime.Ticks).Milliseconds < 200)
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
				GridStorageBlock block = MyAPIGateway.Entities.GetEntityById(place.GarageId).GameLogic.GetAs<GridStorageBlock>();
				block.PlacePrefab(place.GridIndex, place.GridName, place.Position, place.NewOwner);
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Error in function Place_Server: {e.ToString()}");
			}
		}

		private void Error_Client(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			try
			{
				string message = MyAPIGateway.Utilities.SerializeFromBinary<string>(data);

				MyAPIGateway.Utilities.ShowNotification(message, 2000, "Red");

			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Error in function Error_Client: {e.ToString()}");
			}
		}

		private void Preview_Client(ulong steamId, string command, byte[] data, DateTime timestamp)
		{
			try
			{
				PreviewGridData preview = MyAPIGateway.Utilities.SerializeFromBinary<PreviewGridData>(data);
				GridStorageBlock block = MyAPIGateway.Entities.GetEntityById(preview.GarageId).GameLogic.GetAs<GridStorageBlock>();
				block.GridsToPlace = preview.Prefab.UnpackGrids();
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Error in function Preview_Client: {e.ToString()}");
			}
		}
	}
}
