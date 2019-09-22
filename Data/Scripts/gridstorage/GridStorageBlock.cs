﻿using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using VRage.Input;
using System.IO;
using VRage;
using VRage.Game.Entity;
using Sandbox.Game.World;
using VRage.Game.ModAPI.Interfaces;

namespace GridStorage
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true, "GridStorageBlock")]
	public class GridStorageBlock : MyGameLogicComponent
	{
		public const float GridStorageDistance = 2000; // meters

		private static readonly Guid StorageGuid = new Guid("B7AF750E-0077-4826-BD0E-EE5BF36BA3E5");

		public IMyCubeGrid Grid;
		public IMyTerminalBlock ModBlock;
		public MyCubeBlock CubeBlock;

		// used in terminal menu
		private List<string> StoredGrids = new List<string>();
		private string SelectedGrid = null;

		// used when selecting from spectator mode
		private bool GridSelectionMode = true;
		private IMyCubeGrid SelectedGridEntity = null;
		private float PlacementDistance = 200;

		// used when placing grids
		private List<MyObjectBuilder_CubeGrid> GridsToPlace = null;

		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{

			ModBlock = Entity as IMyTerminalBlock;
			Grid = ModBlock.CubeGrid;
			CubeBlock = (MyCubeBlock)ModBlock;

			NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
		}

		private void StartSpectatorView()
		{
			Vector3D blockPosition = Entity.GetPosition();
			MatrixD matrix = Entity.WorldMatrix;

			Vector3D position = blockPosition + (matrix.Backward * 10) + (matrix.Up * 10);

			MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Spectator, null, position);

			((MySession)MyAPIGateway.Session).CameraAttachedToChanged += ViewChange;

			NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
		}

		private void ViewChange(IMyCameraController old, IMyCameraController current)
		{
			CancelSpectorView();
		}

		private void EndSpectatorView()
		{
			((MySession)MyAPIGateway.Session).CameraAttachedToChanged -= ViewChange;

			if (GridSelectionMode)
			{
				SavePrefab();
			}
			else
			{
				PlacePrefab();
			}

			MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Entity, MyAPIGateway.Session.LocalHumanPlayer.Character);
			GridsToPlace = null;
			SelectedGridEntity = null;
			NeedsUpdate = MyEntityUpdateEnum.NONE;
		}

		private void CancelSpectorView()
		{
			((MySession)MyAPIGateway.Session).CameraAttachedToChanged -= ViewChange;

			//if (!GridSelectionMode)
			//{
			//	if (SelectedGridEntity != null)
			//	{
			//		MyAPIGateway.Entities.MarkForClose(SelectedGridEntity);
			//	}
			//}

			MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Entity, MyAPIGateway.Session.LocalHumanPlayer.Character);
			GridsToPlace = null;
			SelectedGridEntity = null;
			NeedsUpdate = MyEntityUpdateEnum.NONE;
		}

		/// <summary>
		/// Update visual while selecting
		/// </summary>
		public override void UpdateBeforeSimulation()
		{
			if (GridSelectionMode)
			{
				GridSelect();
			}
			else
			{
				GridPlacement();
			}

			// bind camera to a 1000m sphere
			Vector3D gridToCamera = (Grid.WorldAABB.Center - MyAPIGateway.Session.Camera.WorldMatrix.Translation);
			if (gridToCamera.LengthSquared() > 1000000)
			{
				EndSpectatorView();
			}

			DisplayNotification($"orbit: {(Grid.WorldAABB.Center - MyAPIGateway.Session.Camera.WorldMatrix.Translation).Length().ToString("n0")}", 1, "White");
		}

		private void SavePrefab()
		{
			try
			{
				if (SelectedGridEntity == null)
					return;

				// create prefab from parent and subgrids

				Prefab prefab = new Prefab();
				// forced into doing strings cause the dumb serializer is erroring out otherwise.
				prefab.Grids.Add(MyAPIGateway.Utilities.SerializeToXML((MyObjectBuilder_CubeGrid)SelectedGridEntity.GetObjectBuilder()));

				List<IMyCubeGrid> grids = MyAPIGateway.GridGroups.GetGroup(SelectedGridEntity, GridLinkTypeEnum.Mechanical);
				MyLog.Default.Info($"[Grid Storage] Getting subgrids {grids.Count}");
				grids.Remove(SelectedGridEntity);

				for (int i = 0; i < grids.Count; i++)
				{
					prefab.Grids.Add(MyAPIGateway.Utilities.SerializeToXML((MyObjectBuilder_CubeGrid)grids[i].GetObjectBuilder()));
				}

				// update terminal display

				string baseName = SelectedGridEntity.DisplayName;
				int index = 0;
				string prefabName;

				do
				{
					index++;
					prefabName = $"{baseName}{((index > 1) ? $"_{index}" : "")}";
				}
				while (StoredGrids.Contains(prefabName));

				StoredGrids.Add(prefabName);
				SelectedGrid = prefabName;

				MyLog.Default.Info($"[Grid Storage] Attempting to save \"{prefabName}\"");

				// write prefab to file

				string data = MyAPIGateway.Utilities.SerializeToXML(prefab);

				TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage($"{Entity.EntityId}_{prefabName}", typeof(Prefab));
				writer.Write(data);
				writer.Close();

				MyLog.Default.Info($"[Grid Storage] Removing grids");

				// remove existing grid from world
				MyAPIGateway.Entities.MarkForClose(SelectedGridEntity);

				foreach (IMyCubeGrid grid in grids)
				{
					MyAPIGateway.Entities.MarkForClose(grid);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error(e.ToString());
			}
		}

		private List<MyObjectBuilder_CubeGrid> LoadPrefab()
		{
			try
			{
				if (SelectedGrid == null)
					return null;

				string filename = $"{Entity.EntityId}_{SelectedGrid}";

				if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(Prefab)))
				{
					EndSpectatorView();
					return null;
				}

				TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(Prefab));
				string text = reader.ReadToEnd();
				reader.Close();

				Prefab fab = MyAPIGateway.Utilities.SerializeFromXML<Prefab>(text);
				List<MyObjectBuilder_CubeGrid> grids = new List<MyObjectBuilder_CubeGrid>();

				foreach (string gridText in fab.Grids)
				{
					grids.Add(MyAPIGateway.Utilities.SerializeFromXML<MyObjectBuilder_CubeGrid>(gridText));
				}

				return grids;

			}
			catch (Exception e)
			{
				MyLog.Default.Error(e.ToString());
				return null;
			}
		}

		private void PlacePrefab()
		{
			if (SelectedGridEntity != null)
			{
				MyAPIGateway.Entities.MarkForClose(SelectedGridEntity);
			}

			MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;
			matrix.Translation += (matrix.Forward * PlacementDistance);

			CreatePhysicalGrid(GridsToPlace, matrix);
		}

		private IMyCubeGrid CreateGridProjection(List<MyObjectBuilder_CubeGrid> grids)
		{
			if (grids == null || grids.Count == 0)
			{
				return null;
			}

			foreach (MyObjectBuilder_CubeGrid grid in grids)
			{
				grid.AngularVelocity = new SerializableVector3();
				grid.LinearVelocity = new SerializableVector3();
				grid.XMirroxPlane = null;
				grid.YMirroxPlane = null;
				grid.ZMirroxPlane = null;
				grid.IsStatic = false;
				grid.CreatePhysics = false;
				grid.IsRespawnGrid = false;

				if (MyAPIGateway.Entities.GetEntityById(grid.EntityId) != null)
				{
					grid.EntityId = 0;
				}

				foreach (MyObjectBuilder_CubeBlock block in grid.CubeBlocks)
				{
					if (MyAPIGateway.Entities.GetEntityById(block.EntityId) != null)
					{
						block.EntityId = 0;
						block.Owner = ModBlock.OwnerId;
					}
				}
			}

			IMyCubeGrid returnGrid = null;
			foreach (MyObjectBuilder_CubeGrid grid in grids)
			{
				MyEntity ent = (MyEntity)MyAPIGateway.Entities.CreateFromObjectBuilder(grid);
				ent.IsPreview = true;
				ent.Flags &= ~EntityFlags.Save;
				ent.Render.Visible = true;
				MyAPIGateway.Entities.AddEntity(ent);

				if (returnGrid == null)
					returnGrid = (IMyCubeGrid)ent;
			}

			return returnGrid;

		}

		private void CreatePhysicalGrid(List<MyObjectBuilder_CubeGrid> grids, MatrixD position)
		{
			if (grids == null || grids.Count == 0)
			{
				return;
			}

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

				if (MyAPIGateway.Entities.GetEntityById(grid.EntityId) != null)
				{
					grid.EntityId = 0;
				}

				foreach (MyObjectBuilder_CubeBlock block in grid.CubeBlocks)
				{
					if (MyAPIGateway.Entities.GetEntityById(block.EntityId) != null)
					{
						block.EntityId = 0;
						block.Owner = ModBlock.OwnerId;
					}
				}
			}

			IMyCubeGrid returnGrid = null;
			foreach (MyObjectBuilder_CubeGrid grid in grids)
			{
				MyEntity ent = (MyEntity)MyAPIGateway.Entities.CreateFromObjectBuilder(grid);
				ent.Flags &= ~EntityFlags.Save;
				ent.Render.Visible = true;
				MyAPIGateway.Entities.AddEntity(ent);

				if (returnGrid == null)
					returnGrid = (IMyCubeGrid)ent;
			}

			returnGrid.Teleport(position);
		}

		private void StoreSelectedGridAction(IMyTerminalBlock block)
		{
			GridSelectionMode = true;
			StartSpectatorView();
		}

		private void SpawnSelectedGridAction(IMyTerminalBlock block)
		{
			GridsToPlace = LoadPrefab();
			SelectedGridEntity = CreateGridProjection(GridsToPlace);


			if (SelectedGridEntity != null)
			{
				MyAPIGateway.Entities.EnableEntityBoundingBoxDraw(SelectedGridEntity, true, Color.LightGreen.ToVector4());
				GridSelectionMode = false;
				StartSpectatorView();
			}
		}

		private void GridSelect()
		{
			List<MyMouseButtonsEnum> buttons = new List<MyMouseButtonsEnum>();
			MyAPIGateway.Input.GetListOfPressedMouseButtons(buttons);

			if (buttons.Contains(MyMouseButtonsEnum.Right))
			{
				CancelSpectorView();
				return;
			}

			MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;

			// show highlighted grid
			Vector3D start = matrix.Translation;
			Vector3D end = start + (matrix.Forward * 500);

			IHitInfo info;
			MyAPIGateway.Physics.CastRay(start, end, out info);

			IMyCubeGrid grid = info?.HitEntity as IMyCubeGrid;

			if (grid != null)
			{
				DisplayNotification($"target: \"{grid.DisplayName}\" distance: {(start - info.Position).Length().ToString("n0")}", 1, "White");

				MatrixD world = grid.WorldMatrix;
				BoundingBoxD box = grid.LocalAABB;
				Color color = Color.LightGreen;

				MySimpleObjectDraw.DrawTransparentBox(ref world, ref box, ref color, MySimpleObjectRasterizer.Solid, 5);

				if (buttons.Contains(MyMouseButtonsEnum.Left))
				{
					SelectedGridEntity = grid;
					EndSpectatorView();
					return;
				}
			}
		}

		private void GridPlacement()
		{
			PlacementDistance += ((float)MyAPIGateway.Input.DeltaMouseScrollWheelValue()/4f);

			if (PlacementDistance < 0)
			{
				PlacementDistance = 0;
			}
			else if (PlacementDistance > 500)
			{
				PlacementDistance = 500;
			}

			MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;
			matrix.Translation += (matrix.Forward * PlacementDistance);

			SelectedGridEntity.Teleport(matrix);

			List<MyMouseButtonsEnum> buttons = new List<MyMouseButtonsEnum>();
			MyAPIGateway.Input.GetListOfPressedMouseButtons(buttons);

			if (buttons.Contains(MyMouseButtonsEnum.Right))
			{
				CancelSpectorView();
				return;
			}

			if (buttons.Contains(MyMouseButtonsEnum.Left))
			{
				EndSpectatorView();
				return;
			}
		}

		#region create controls
		public override void UpdateOnceBeforeFrame()
		{
			// do not run the rest on dedicated servers
			if (MyAPIGateway.Utilities.IsDedicated)
				return;

			CreateControls();
		}

		private void CreateControls()
		{
			MyLog.Default.Info("[Grid Storage] Creating controls");

			Tools.CreateControlButton("GridStorage_Store", "Store Grid", "Lets you select a grid to store", ControlsVisible_Basic, ControlsEnabled_Basic, StoreSelectedGridAction);

			Tools.CreateControlListbox("BlinkDrive_GPSList", "GPS Locations", "Select a location for ranged jumps", 8, ControlsVisible_Basic, ControlsEnabled_Basic,
			(block, items, selected) => {
				List<IMyGps> list = new List<IMyGps>();
				MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.LocalHumanPlayer.IdentityId, list);

				GridStorageBlock drive = block.GameLogic?.GetAs<GridStorageBlock>();

				foreach (string filename in drive.StoredGrids)
				{
					MyTerminalControlListBoxItem listItem = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(filename), MyStringId.GetOrCompute(""), filename);
					items.Add(listItem);

					if (drive.SelectedGrid == filename)
					{
						selected.Add(listItem);
					}
				}
			},
			(block, items) => {

				GridStorageBlock drive = block.GameLogic?.GetAs<GridStorageBlock>();

				drive.SelectedGrid = (string)items[0].UserData;
			});

			Tools.CreateControlButton("GridStorage_Spawn", "Spawn Grid", "Spawns the grid selected in the listbox", ControlsVisible_Basic, ControlsEnabled_Basic, SpawnSelectedGridAction);
		}

		public static bool ControlsEnabled_Basic(IMyTerminalBlock block)
		{
			GridStorageBlock drive = block.GameLogic.GetAs<GridStorageBlock>();
			return drive != null && block.IsFunctional && block.IsWorking;
		}

		public static bool ControlsVisible_Basic(IMyTerminalBlock block)
		{
			return block.GameLogic.GetAs<GridStorageBlock>() != null;
		}

		#endregion

		private void Save()
		{
			MyModStorageComponentBase storage = GetStorage(Entity);

			if (storage.ContainsKey(StorageGuid))
			{
				storage[StorageGuid] = MyAPIGateway.Utilities.SerializeToXML(StoredGrids);
			}
			else
			{
				MyLog.Default.Info($"[Grid Storage] {Entity.EntityId}: Saved new Data");
				storage.Add(new KeyValuePair<Guid, string>(StorageGuid, MyAPIGateway.Utilities.SerializeToXML(StoredGrids)));
			}
		}

		private void Load()
		{
			MyModStorageComponentBase storage = GetStorage(Entity);

			if (storage.ContainsKey(StorageGuid))
			{
				StoredGrids = MyAPIGateway.Utilities.SerializeFromXML<List<string>>(storage[StorageGuid]);
			}
			else
			{
				MyLog.Default.Info($"[BlinkDrive] No data saved for:{Entity.EntityId}. Loading Defaults");
				StoredGrids = new List<string>();
			}
		}

		public static MyModStorageComponentBase GetStorage(IMyEntity entity)
		{
			return entity.Storage ?? (entity.Storage = new MyModStorageComponent());
		}

		private void DisplayNotification(string text, int lifetime, string color)
		{

			if (MyAPIGateway.Utilities.IsDedicated)
			{
				MyLog.Default.Info($"[BlinkDrive] {ModBlock.EntityId} msg: {text}");
				return;
			}

			MyAPIGateway.Utilities.ShowNotification(text, lifetime, color);
		}
	}
}
