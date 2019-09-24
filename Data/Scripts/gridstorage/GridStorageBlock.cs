using Sandbox.Common.ObjectBuilders;
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
using VRage.Game.ModAPI.Interfaces;

namespace GridStorage
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true, "GridStorageBlock")]
	public class GridStorageBlock : MyGameLogicComponent
	{
		public const float GridSelectLineSize = 0.2f;

		private static readonly Guid StorageGuid = new Guid("B7AF750E-0077-4826-BD0E-EE5BF36BA3E5");

		public IMyCubeGrid Grid;
		public IMyTerminalBlock ModBlock;
		public MyCubeBlock CubeBlock;

		// used in terminal menu
		private List<string> StoredGrids = new List<string>();
		private string SelectedGrid = null;

		// used when selecting from spectator mode
		private bool GridSelectionMode = true;
		string GridSelectErrorMessage = null;
		private IMyCubeGrid SelectedGridEntity = null;
		private float PlacementDistance = 200;
		private IMyCameraController CameraController = null;

		// used when placing grids
		private List<MyObjectBuilder_CubeGrid> GridsToPlace = null;
		private List<MyCubeGrid> CubeGridsToPlace = null;

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
			CameraController = MyAPIGateway.Session.CameraController;

			NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
		}

		private void EndSpectatorView()
		{
			if (GridSelectionMode)
			{
				SavePrefab();
			}
			else
			{
				PlacePrefab();
			}

			ResetView();
		}

		private void CancelSpectorView()
		{
			if (!GridSelectionMode)
			{
				if (CubeGridsToPlace != null)
				{
					foreach (var grid in CubeGridsToPlace)
					{
						MyAPIGateway.Entities.MarkForClose(grid);
					}
				}
			}

			ResetView();
		}

		private void ResetView()
		{
			if (MyAPIGateway.Session.ControlledObject?.Entity is IMyTerminalBlock)
			{
				MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Entity, MyAPIGateway.Session.ControlledObject.Entity);
			}
			else
			{
				MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Entity, MyAPIGateway.Session.LocalHumanPlayer.Character);
			}

			if (SelectedGridEntity != null)
			{
				ShowHideBoundingBoxGridGroup(SelectedGridEntity, false);
			}

			if (CubeGridsToPlace != null && CubeGridsToPlace.Count > 0)
			{
				ShowHideBoundingBoxGridGroup(CubeGridsToPlace[0], false);
			}

			GridsToPlace = null;
			SelectedGridEntity = null;
			CubeGridsToPlace = null;
			NeedsUpdate = MyEntityUpdateEnum.NONE;
		}

		/// <summary>
		/// Update visual while selecting
		/// </summary>
		public override void UpdateBeforeSimulation()
		{
			// keep users from placing blocks in spectator
			if (MyCubeBuilder.Static.BlockCreationIsActivated)
			{
				MyCubeBuilder.Static.DeactivateBlockCreation();
			}

			// bind camera to a 1000m sphere
			Vector3D gridToCamera = (Grid.WorldAABB.Center - MyAPIGateway.Session.Camera.WorldMatrix.Translation);
			if (gridToCamera.LengthSquared() > 1000000)
			{
				CancelSpectorView();
			}

			DisplayNotification($"Select (LMB) - Cancel (RMB) - Camera Orbit ({gridToCamera.Length().ToString("n0")}/1000) - Range (500)", 1, "White");

			if (CameraController != MyAPIGateway.Session.CameraController)
			{
				CancelSpectorView();
				return;
			}

			if (GridSelectionMode)
			{
				GridSelect();
			}
			else
			{
				GridPlacement();
			}
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
			if (CubeGridsToPlace != null)
			{
				foreach (var grid in CubeGridsToPlace)
				{
					MyAPIGateway.Entities.MarkForClose(grid);
				}
			}

			MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;
			matrix.Translation += (matrix.Forward * PlacementDistance);

			CreatePhysicalGrid(GridsToPlace, matrix);

			StoredGrids.Remove(SelectedGrid);
			if (StoredGrids.Count > 0)
			{
				SelectedGrid = StoredGrids[0];
			}

		}

		private List<MyCubeGrid> CreateGridProjection(List<MyObjectBuilder_CubeGrid> grids)
		{
			if (grids == null || grids.Count == 0)
			{
				return null;
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
				grid.CreatePhysics = false;
				grid.IsRespawnGrid = false;
			}

			List<MyCubeGrid> cubeGrids = new List<MyCubeGrid>();
			foreach (MyObjectBuilder_CubeGrid grid in grids)
			{
				MyCubeGrid childGrid = (MyCubeGrid)MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(grid);
				childGrid.IsPreview = true;

				cubeGrids.Add(childGrid);
			}

			return cubeGrids;

		}

		private void CreatePhysicalGrid(List<MyObjectBuilder_CubeGrid> grids, MatrixD position)
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
				grid.PositionAndOrientation = new MyPositionAndOrientation((position.Translation - offset), grid.PositionAndOrientation.Value.Forward, grid.PositionAndOrientation.Value.Up);


				MyCubeGrid childGrid = (MyCubeGrid)MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(grid);
				childGrid.Render.Visible = true;

			}
		}

		private void StoreSelectedGridAction(IMyTerminalBlock block)
		{
			GridStorageBlock gsb = block.GameLogic.GetAs<GridStorageBlock>();
			if (gsb != null)
			{
				gsb.GridSelectionMode = true;
				gsb.StartSpectatorView();
			}
		}

		private void SpawnSelectedGridAction(IMyTerminalBlock block)
		{
			GridStorageBlock gsb = block.GameLogic.GetAs<GridStorageBlock>();
			if (gsb != null)
			{
				gsb.GridsToPlace = gsb.LoadPrefab();
				gsb.CubeGridsToPlace = gsb.CreateGridProjection(gsb.GridsToPlace);

				if (gsb.CubeGridsToPlace != null)
				{
					gsb.GridSelectionMode = false;
					gsb.StartSpectatorView();
				}
			}
		}

		private static void ShowHideBoundingBoxGridGroup(IMyCubeGrid grid, bool enabled, Vector4? color = null)
		{
			List<IMyCubeGrid> subgrids = MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical);
			foreach (MyCubeGrid sub in subgrids)
			{
				MyAPIGateway.Entities.EnableEntityBoundingBoxDraw(sub, enabled, color, GridSelectLineSize);
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
			Vector3D start = matrix.Translation;
			Vector3D end = start + (matrix.Forward * 500);

			IHitInfo info;
			MyAPIGateway.Physics.CastRay(start, end, out info);

			MyCubeGrid hitGrid = info?.HitEntity as MyCubeGrid;

			if (SelectedGridEntity != null && hitGrid == null || // just stopped looking at a grid
				SelectedGridEntity != null && hitGrid != null && SelectedGridEntity != hitGrid) // switched from one grid to another
			{
				ShowHideBoundingBoxGridGroup(SelectedGridEntity, false);

				SelectedGridEntity = null;
			}

			if (hitGrid != null)
			{
				if (SelectedGridEntity != hitGrid)
				{
					SelectedGridEntity = hitGrid;
					GridSelectErrorMessage = null;
					List<IMyCubeGrid> subgrids = MyAPIGateway.GridGroups.GetGroup(hitGrid, GridLinkTypeEnum.Mechanical);

					foreach (MyCubeGrid grid in subgrids)
					{
						bool isValid = true;
						if (grid.EntityId == Grid.EntityId)
						{
							GridSelectErrorMessage = $"Cannot store parent grid";
							isValid = false;
						}
						else if (grid.IsStatic)
						{
							GridSelectErrorMessage = $"Cannot store static grids";
							isValid = false;
						}
						else if (grid.BigOwners.Count == 0)
						{
							GridSelectErrorMessage = $"Cannot store unowned grid";
							isValid = false;
						}
						else if (GridHasNonFactionOwners(grid))
						{
							GridSelectErrorMessage = $"Some blocks owned by other factions";
							isValid = false;
						}

						if (!isValid)
						{
							MyAPIGateway.Entities.EnableEntityBoundingBoxDraw(grid, true, Color.Red.ToVector4(), GridSelectLineSize);
						}
						else
						{
							MyAPIGateway.Entities.EnableEntityBoundingBoxDraw(grid, true, Color.Green.ToVector4(), GridSelectLineSize);
						}
					}
				}

				if (GridSelectErrorMessage != null)
				{
					DisplayNotification(GridSelectErrorMessage, 1, "Red");

				}
				else if (buttons.Contains(MyMouseButtonsEnum.Left))
				{
					EndSpectatorView();
					return;
				}
			}
		}

		private void GridPlacement()
		{
			PlacementDistance += ((float)MyAPIGateway.Input.DeltaMouseScrollWheelValue() / 4f);

			if (PlacementDistance < 0)
			{
				PlacementDistance = 0;
			}
			else if (PlacementDistance > 500)
			{
				PlacementDistance = 500;
			}

			MatrixD parentMatrix = CubeGridsToPlace[0].WorldMatrix;

			foreach (MyCubeGrid grid in CubeGridsToPlace)
			{
				Vector3D offset = parentMatrix.Translation - grid.WorldMatrix.Translation;

				MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;
				matrix.Translation -= offset;
				matrix.Translation += (matrix.Forward * PlacementDistance);

				grid.Teleport(matrix);
			}
			
			bool isValid = true;

			BoundingBoxD box = CubeGridsToPlace[0].GetPhysicalGroupAABB();
			List <IMyEntity> entities = MyAPIGateway.Entities.GetEntitiesInAABB(ref box);
			entities.RemoveAll((e) => !(e is MyVoxelBase || (e is IMyCubeBlock && !CubeGridsToPlace.Contains((MyCubeGrid)(e as IMyCubeBlock).CubeGrid)) || e is IMyCharacter || e is IMyFloatingObject));

			foreach (IMyEntity ent in entities)
			{
				if (!isValid)
					break;

				if (ent is MyVoxelBase)
				{
					foreach (MyCubeGrid grid in CubeGridsToPlace)
					{
						MyTuple<float, float> voxelcheck = (ent as MyVoxelBase).GetVoxelContentInBoundingBox_Fast(grid.PositionComp.LocalAABB, grid.WorldMatrix);
						if (!float.IsNaN(voxelcheck.Item2) && voxelcheck.Item2 > 0.1f)
						{
							DisplayNotification($"Voxel Obstruction {(voxelcheck.Item2*100).ToString("n2")}%", 1, "Red");
							isValid = false;
							break;
						}
					}
				}
				else
				{
					DisplayNotification($"Obstruction {ent.GetType().Name} {ent.DisplayName}", 1, "Red");
					isValid = false;
				}
			}

			if (isValid)
			{
				ShowHideBoundingBoxGridGroup(CubeGridsToPlace[0], true, Color.LightGreen.ToVector4());
			}
			else
			{
				ShowHideBoundingBoxGridGroup(CubeGridsToPlace[0], true, Color.Red.ToVector4());
			}

			List <MyMouseButtonsEnum> buttons = new List<MyMouseButtonsEnum>();
			MyAPIGateway.Input.GetListOfPressedMouseButtons(buttons);

			if (buttons.Contains(MyMouseButtonsEnum.Right))
			{
				CancelSpectorView();
				return;
			}

			if (isValid && buttons.Contains(MyMouseButtonsEnum.Left))
			{
				EndSpectatorView();
				return;
			}
		}

		public static bool GridHasNonFactionOwners(IMyCubeGrid grid)
		{
			var gridOwners = grid.BigOwners;
			foreach (var pid in gridOwners)
			{
				MyRelationsBetweenPlayerAndBlock relation = MyAPIGateway.Session.Player.GetRelationTo(pid);
				if (relation == MyRelationsBetweenPlayerAndBlock.Enemies || relation == MyRelationsBetweenPlayerAndBlock.Neutral)
				{
					return true;
				}
			}
			return false;
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

			Tools.CreateControlListbox("GridStorage_GridList", "Grid List", "Select a grid to spawn", 8, ControlsVisible_Basic, ControlsEnabled_Basic,
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
