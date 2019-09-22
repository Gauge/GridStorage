using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.IO;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace GridStorage
{
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public class Core : MySessionComponentBase
	{

		//MyObjectBuilder_CubeGrid grid;

		//bool spawned = false;

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			//MyAPIGateway.Entities.OnEntityAdd += EntityAdded;

		}

		//public override void UpdateBeforeSimulation()
		//{
		//	if (spawned)
		//		return;


		//	var ent = (MyEntity)MyAPIGateway.Entities.CreateFromObjectBuilder(grid);
		//	ent.Flags &= ~EntityFlags.Save;
		//	ent.Render.Visible = true;
		//	MyAPIGateway.Entities.AddEntity(ent);

		//	spawned = true;
		//}

		//public void EntityAdded(IMyEntity ent)
		//{
		//	if (ent is IMyCubeGrid)
		//	{
		//		MyLog.Default.Info("[Grid Storage] storing block!");

		//		grid = (MyObjectBuilder_CubeGrid)(ent as MyCubeGrid).GetObjectBuilder();

		//		grid.EntityId = 0;
		//		grid.AngularVelocity = new SerializableVector3();
		//		grid.LinearVelocity = new SerializableVector3();
		//		grid.PositionAndOrientation = new MyPositionAndOrientation(new Vector3D(), new Vector3(0, 0, 1), new Vector3(0, 1, 0));
		//		grid.XMirroxPlane = null;
		//		grid.YMirroxPlane = null;
		//		grid.ZMirroxPlane = null;
		//		grid.IsStatic = false;
		//		grid.CreatePhysics = true;
		//		grid.IsRespawnGrid = false;

		//		foreach (var block in grid.CubeBlocks)
		//		{
		//			block.EntityId = 0;
		//		}

		//		string data = MyAPIGateway.Utilities.SerializeToXML(grid);

		//		TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(ent.DisplayName + "-test", typeof(MyObjectBuilder_CubeGrid));
		//		writer.Write(data);
		//		writer.Close();

		//		MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;
		//	}
		//}
	}
}
