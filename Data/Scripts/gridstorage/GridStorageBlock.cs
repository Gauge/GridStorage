using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace GridStorage
{

	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true, "GridStorageBlock")]
	public class GridStorageBlock : MyNetworkAPIGameLogicComponent
	{
		public const float GridSelectLineSize = 0.2f;

		private static readonly Guid StorageGuid = new Guid("19906e82-9e21-458d-8f02-7bf7030ed604");

		public IMyCubeGrid Grid;
		public IMyTerminalBlock ModBlock;
		public MyCubeBlock CubeBlock;

		// used in terminal menu
		public NetSync<List<string>> GridList;
		public string SelectedGrid = null;
		private IMyCubeGrid SelectedGridEntity = null;

		// used when selecting from spectator mode
		private bool GridSelectionMode = true;
		string GridSelectErrorMessage = null;
		private float PlacementDistance = 200;
		private IMyCameraController CameraController = null;

		// used when placing grids
		public List<MyObjectBuilder_CubeGrid> GridsToPlace = null;
		private List<MyCubeGrid> CubeGridsToPlace = null;

		/// <summary>
		/// General initialize
		/// </summary>
		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			base.Init(objectBuilder);

			ModBlock = Entity as IMyTerminalBlock;
			Grid = ModBlock.CubeGrid;
			CubeBlock = (MyCubeBlock)ModBlock;
			GridList = new NetSync<List<string>>(this, TransferType.Both, new List<string>());

			if (MyAPIGateway.Session.IsServer)
			{
				Load();
			}

			NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
		}

		

		/// <summary>
		/// Save stored grid information on world save.
		/// </summary>
		public override bool IsSerialized()
		{
			Save();
			return base.IsSerialized();
		}

		public void RemoveGridFromList(string name)
		{
			GridList.Value.Remove(name);
			if (GridList.Value.Count > 0)
			{
				SelectedGrid = GridList.Value[0];
			}
			else
			{
				SelectedGrid = null;
			}

			GridList.Push();
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
				gsb.GridsToPlace = null;
				gsb.CubeGridsToPlace = null;

				if (MyAPIGateway.Multiplayer.IsServer)
				{
					Prefab fab = Core.LoadPrefab(gsb.Entity.EntityId, gsb.SelectedGrid);

					if (fab != null)
					{
						gsb.GridsToPlace = fab.UnpackGrids();
					}
				}
				else
				{
					PreviewGridData data = new PreviewGridData() {
						BlockId = gsb.Entity.EntityId,
						GridName = gsb.SelectedGrid
					};

					Network.SendCommand(Core.Command_Preview, null, MyAPIGateway.Utilities.SerializeToBinary(data));
				}

				gsb.GridSelectionMode = false;
				gsb.StartSpectatorView();
			}
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

			MyAPIGateway.Utilities.ShowNotification($"Select (LMB) - Cancel (RMB) - Camera Orbit ({gridToCamera.Length().ToString("n0")}/1000) - Range (500)", 1, "White");

			List<MyMouseButtonsEnum> buttons = new List<MyMouseButtonsEnum>();
			MyAPIGateway.Input.GetListOfPressedMouseButtons(buttons);

			if (buttons.Contains(MyMouseButtonsEnum.Right))
			{
				CancelSpectorView();
				return;
			}

			if (CameraController != MyAPIGateway.Session.CameraController)
			{
				CancelSpectorView();
				return;
			}

			if (GridSelectionMode)
			{
				GridSelect(buttons);
			}
			else
			{
				GridPlacement(buttons);
			}
		}

		private void GridSelect(List<MyMouseButtonsEnum> buttons)
		{
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
					MyAPIGateway.Utilities.ShowNotification(GridSelectErrorMessage, 1, "Red");

				}
				else if (buttons.Contains(MyMouseButtonsEnum.Left))
				{
					if (MyAPIGateway.Multiplayer.IsServer)
					{
						if (SelectedGridEntity != null)
						{
							string name = Core.SavePrefab(SelectedGridEntity.EntityId, Entity.EntityId);
							GridList.Value.Add(name);
							GridList.Push();
						}
					}
					else
					{
						StoreGridData data = new StoreGridData() { BlockId = Entity.EntityId, Target = SelectedGridEntity.EntityId };

						Network.SendCommand(Core.Command_Store, data: MyAPIGateway.Utilities.SerializeToBinary(data));
					}

					ResetView();
				}
			}
		}

		private void GridPlacement(List<MyMouseButtonsEnum> buttons)
		{
			// initialize preview grid once it is available
			if (!GridSelectionMode && GridsToPlace == null)
			{
				return;
			}
			else if (CubeGridsToPlace == null)
			{
				CubeGridsToPlace = CreateGridProjection(GridsToPlace);
			}

			// limit placement distance
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

			// validate grid placement position
			bool isValid = true;
			BoundingBoxD box = CubeGridsToPlace[0].GetPhysicalGroupAABB();
			List<IMyEntity> entities = MyAPIGateway.Entities.GetEntitiesInAABB(ref box);
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
							MyAPIGateway.Utilities.ShowNotification($"Voxel Obstruction {(voxelcheck.Item2 * 100).ToString("n2")}%", 1, "Red");
							isValid = false;
							break;
						}
					}
				}
				else
				{
					MyAPIGateway.Utilities.ShowNotification($"Obstruction {ent.GetType().Name} {ent.DisplayName}", 1, "Red");
					isValid = false;
				}
			}

			ulong steamId = MyAPIGateway.Session.Player.SteamUserId;
			long userId = MyAPIGateway.Session.Player.IdentityId;

			foreach (MySafeZone zone in MySessionComponentSafeZones.SafeZones)
			{
				bool flag = false;
				if (zone.Shape == MySafeZoneShape.Sphere)
				{
					flag = new BoundingSphereD(zone.PositionComp.GetPosition(), zone.Radius).Intersects(box);
				}
				flag = new MyOrientedBoundingBoxD(zone.PositionComp.LocalAABB, zone.PositionComp.WorldMatrix).Intersects(ref box);

				if (flag)
				{
					if (steamId != 0L && MySafeZone.CheckAdminIgnoreSafezones(steamId))
					{
						continue;
					}

					if (zone.AccessTypePlayers == MySafeZoneAccess.Whitelist)
					{
						if (zone.Players.Contains(userId))
						{
							continue;
						}
					}
					else if (zone.Players.Contains(userId))
					{
						MyAPIGateway.Utilities.ShowNotification($"Player is blacklisted. Can not spawn in this safezone", 1, "Red");
						isValid = false;
						break;
					}

					IMyFaction myFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(userId);
					if (myFaction != null)
					{
						if (zone.AccessTypeFactions == MySafeZoneAccess.Whitelist)
						{
							if (zone.Factions.Find(x => x == myFaction) != null)
							{
								continue;
							}
						}
						else if (zone.Factions.Find(x => x == myFaction) != null)
						{
							MyAPIGateway.Utilities.ShowNotification($"Your faction is blacklisted. Can not spawn in this safezone", 1, "Red");
							isValid = false;
							break;
						}
					}

					if (zone.AccessTypeFactions == MySafeZoneAccess.Whitelist)
					{
						MyAPIGateway.Utilities.ShowNotification($"Can not spawn in this safezone", 1, "Red");
						isValid = false;
						break;
					}
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

			// place grid
			if (isValid && buttons.Contains(MyMouseButtonsEnum.Left))
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

				if (MyAPIGateway.Multiplayer.IsServer)
				{
					Core.PlacePrefab(GridsToPlace, matrix.Translation, $"{Entity.EntityId}_{SelectedGrid}");
					RemoveGridFromList(SelectedGrid);
				}
				else
				{
					PlaceGridData data = new PlaceGridData() {
						BlockId = Entity.EntityId,
						GridName = SelectedGrid,
						Position = new SerializableVector3D(matrix.Translation)
					};

					Network.SendCommand(Core.Command_Place, null, MyAPIGateway.Utilities.SerializeToBinary(data));
				}

				ResetView();
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

		private static void ShowHideBoundingBoxGridGroup(IMyCubeGrid grid, bool enabled, Vector4? color = null)
		{
			List<IMyCubeGrid> subgrids = MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical);
			foreach (MyCubeGrid sub in subgrids)
			{
				MyAPIGateway.Entities.EnableEntityBoundingBoxDraw(sub, enabled, color, GridSelectLineSize);
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

		public void ValidateGridList()
		{
			bool flag = false;
			foreach (string value in GridList.Value)
			{
				if (!MyAPIGateway.Utilities.FileExistsInWorldStorage($"{Entity.EntityId}_{value}", typeof(Prefab)))
				{
					flag = true;
					GridList.Value.Remove(value);
				}
			}

			if (flag)
			{
				GridList.Push();
			}
		}

		#region create controls
		public override void UpdateOnceBeforeFrame()
		{
			if (MyAPIGateway.Multiplayer.IsServer)
			{
				ValidateGridList();
			}

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
				GridStorageBlock drive = block.GameLogic?.GetAs<GridStorageBlock>();

				foreach (string filename in drive.GridList.Value)
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

		/// <summary>
		/// Save grid list to world save file
		/// </summary>
		private void Save()
		{
			MyModStorageComponentBase storage = GetStorage(Entity);

			string value = string.Join("||//||", GridList.Value);
			if (storage.ContainsKey(StorageGuid))
			{
				storage[StorageGuid] = value;
				MyLog.Default.Info($"[Grid Storage] Data Saved");
			}
			else
			{
				MyLog.Default.Info($"[Grid Storage] {Entity.EntityId}: Saved new data");

				storage.Add(new KeyValuePair<Guid, string>(StorageGuid, value));
			}
		}

		/// <summary>
		/// Load grid list from world save file
		/// </summary>
		private void Load()
		{
			try
			{
				MyModStorageComponentBase storage = GetStorage(Entity);

				if (storage.ContainsKey(StorageGuid))
				{
					List<string> names = new List<string>(storage[StorageGuid].Split(new string[] { "||//||" }, StringSplitOptions.RemoveEmptyEntries));

					foreach (string name in names)
					{

					}

					GridList.SetValue(names, SyncType.None);

					MyLog.Default.Info($"[Grid Storage] Data loaded: {GridList.Value.Count} grids");
				}
				else
				{
					MyLog.Default.Info($"[Grid Storage] No data saved for: {Entity.EntityId}");
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Storage] {e.ToString()}");
			}
		}

		/// <summary>
		/// Ensures the entities storage component exists for saving reasons
		/// </summary>
		public static MyModStorageComponentBase GetStorage(IMyEntity entity)
		{
			return entity.Storage ?? (entity.Storage = new MyModStorageComponent());
		}
	}
}