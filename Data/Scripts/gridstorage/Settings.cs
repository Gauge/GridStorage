using ProtoBuf;
using Sandbox.ModAPI;
using System;
using System.IO;
using VRage.Utils;

namespace GridStorage
{
	[ProtoContract]
	public class Settings
	{
		public const string Filename = "settings.cfg";

		[ProtoMember(1)]
		public int Version;

		[ProtoMember(2)]
		public int StorageCooldown;

		[ProtoMember(3)]
		public int SpawnCooldown;

		[ProtoMember(4)]
		public int CameraOrbitDistance;

		[ProtoMember(5)]
		public int CameraPlacementDistance;

		[ProtoMember(6)]
		public int MaxGridCount;

		[ProtoMember(7)]
		public bool CanStoreUnownedGrids;

		public static Settings GetDefaults()
		{
			return new Settings {
				Version = 2,
				StorageCooldown = 30,
				SpawnCooldown = 60,
				CameraOrbitDistance = 1000,
				CameraPlacementDistance = 500,
				MaxGridCount = 0,
				CanStoreUnownedGrids = true
			};
		}

		public static Settings Load()
		{
			Settings defaults = GetDefaults();
			Settings settings = defaults;
			try
			{
				if (MyAPIGateway.Utilities.FileExistsInWorldStorage(Filename, typeof(Settings)))
				{
					MyLog.Default.Info("[Grid Garage] Loading saved settings");
					TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(Filename, typeof(Settings));
					string text = reader.ReadToEnd();
					reader.Close();

					settings = MyAPIGateway.Utilities.SerializeFromXML<Settings>(text);

					if (settings.Version != defaults.Version)
					{
						MyLog.Default.Info($"[Grid Garage] Old version updating config {settings.Version}->{defaults.Version}");
						settings = GetDefaults();
						Save(settings);
					}
				}
				else
				{
					MyLog.Default.Info("[Grid Garage] Config file not found. Loading default settings");
					Save(settings);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Info($"[Grid Garage] Failed to load saved configuration. Loading defaults\n {e.ToString()}");
				Save(settings);
			}

			return settings;
		}

		public static void Save(Settings settings)
		{
			try
			{
				MyLog.Default.Info($"[Grid Garage] Saving Settings");
				TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(Filename, typeof(Settings));
				writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
				writer.Close();
			}
			catch (Exception e)
			{
				MyLog.Default.Info($"[Grid Garage] Failed to save settings\n{e.ToString()}");
			}
		}

	}
}