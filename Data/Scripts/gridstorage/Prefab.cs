using ProtoBuf;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.IO;
using VRage;
using VRage.Game;
using VRage.Utils;

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

			foreach (MyObjectBuilder_CubeGrid grid in list)
			{
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
		public long GarageEntityId;

		[ProtoMember(2)]
		public string GarageGuid;

		[ProtoMember(3)]
		public long TargetId;
	}

	[ProtoContract]
	public class PreviewGridData
	{
		[ProtoMember(1)]
		public long GarageEntityId;

		[ProtoMember(2)]
		public string GarageGuid;

		[ProtoMember(3)]
		public string GridName;

		[ProtoMember(4)]
		public Prefab Prefab;
	}

	[ProtoContract]
	public class PlaceGridData
	{
		[ProtoMember(1)]
		public long GarageEntityId;

		[ProtoMember(2)]
		public string GarageGuid;

		[ProtoMember(3)]
		public string GridName;

		[ProtoMember(4)]
		public long NewOwner;

		[ProtoMember(5)]
		public SerializableVector3D Position;
	}

	[ProtoContract]
	public class StorageDescription
	{
		[ProtoMember(1)]
		public string Id { get; set; }

		[ProtoMember(2)]
		public List<string> GridNames = new List<string>();
	}

	/// <summary>
	/// The file index class holds a list of filenames for all stored grids
	/// its primary use is to varify that the grids registered as stored in the garage block has a physical file on the server to match
	/// </summary>
	[ProtoContract]
	public class FileIndex 
	{
		[ProtoMember(1)]
		public List<string> FileNames = new List<string>();

		public static FileIndex GetFileIndex()
		{
			try
			{
				//the file index is a list of all stored grid filenames
				TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage("index", typeof(FileIndex));
				string text = reader.ReadToEnd();
				reader.Close();

				return MyAPIGateway.Utilities.SerializeFromXML<FileIndex>(text);
			}
			catch (FileNotFoundException e) 
			{
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed to load index file:\n{e.ToString()}");
			}

			MyLog.Default.Info("[Grid Garage] Creating a new index file");
			return new FileIndex();
		}

		public void Save() 
		{
			try
			{
				TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage("index", typeof(FileIndex));
				writer.Write(MyAPIGateway.Utilities.SerializeToXML(this));
				writer.Close();	
			}
			catch (Exception e) 
			{
				MyLog.Default.Error($"[Grid Garage] Failed to save index file:\n{e.ToString()}");
			}
		}
	}
}
