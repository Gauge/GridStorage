using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using System.IO;
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

		public MyCubeGrid Grid;
		public IMyTerminalBlock ModBlock;
		public MyCubeBlock CubeBlock;

		public NetSync<List<string>> GridNames;
		public NetSync<bool> DisplayHologram;
		private NetSync<DateTime> StorageCooldown;
		private NetSync<DateTime> SpawnCooldown;

		private IMyCameraController CameraController = null;
		private bool GridSelectionMode = true;
		string GridSelectErrorMessage = null;
		private float PlacementDistance = 200;


		public List<Prefab> GridList = new List<Prefab>();
		int SelectedGridIndex = -1;

		private IMyCubeGrid SelectedGridEntity = null;
		public List<MyObjectBuilder_CubeGrid> GridsToPlace = null;
		private List<MyCubeGrid> CubeGridsToPlace = null;
		private List<MyCubeGrid> HologramGrids = new List<MyCubeGrid>();

		/// <summary>
		/// General initialize
		/// </summary>
		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			try
			{
				base.Init(objectBuilder);

				ModBlock = Entity as IMyTerminalBlock;
				Grid = (MyCubeGrid)ModBlock.CubeGrid;
				CubeBlock = (MyCubeBlock)ModBlock;

				GridNames = new NetSync<List<string>>(this, TransferType.ServerToClient, new List<string>());
				DisplayHologram = new NetSync<bool>(this, TransferType.Both, false);
				StorageCooldown = new NetSync<DateTime>(this, TransferType.Both, DateTime.MinValue);
				SpawnCooldown = new NetSync<DateTime>(this, TransferType.Both, DateTime.MinValue);

				if (MyAPIGateway.Multiplayer.IsServer)
				{
					Load();
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on Init\n{e.ToString()}");
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

		private void StartSpectatorView()
		{
			try
			{
				Vector3D blockPosition = Entity.GetPosition();
				MatrixD matrix = Entity.WorldMatrix;

				Vector3D position = blockPosition + (matrix.Backward * 10) + (matrix.Up * 10);

				MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Spectator, null, position);
				CameraController = MyAPIGateway.Session.CameraController;

				NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on StartSpectatorView\n{e.ToString()}");
			}
		}

		private void CancelSpectorView()
		{
			try
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
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on CancelSpectorView\n{e.ToString()}");
			}
		}

		private void ResetView()
		{
			try
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
				NeedsUpdate &= ~MyEntityUpdateEnum.EACH_FRAME;
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on ResetView\n{e.ToString()}");
			}
		}

		private void StoreSelectedGridAction(IMyTerminalBlock block)
		{
			try
			{
				GridStorageBlock gsb = block.GameLogic.GetAs<GridStorageBlock>();
				if (gsb != null)
				{
					gsb.GridSelectionMode = true;
					gsb.StartSpectatorView();
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on StoreSelectedGridAction\n{e.ToString()}");
			}
		}

		private void SpawnSelectedGridAction(IMyTerminalBlock block)
		{
			try
			{
				GridStorageBlock gsb = block.GameLogic.GetAs<GridStorageBlock>();
				if (gsb != null)
				{
					gsb.GridsToPlace = null;
					gsb.CubeGridsToPlace = null;

					if (MyAPIGateway.Multiplayer.IsServer)
					{
						if (gsb.SelectedGridIndex != -1)
						{
							gsb.GridsToPlace = gsb.GridList[gsb.SelectedGridIndex].UnpackGrids();
						}
					}
					else
					{
						if (gsb.SelectedGridIndex != -1)
						{
							Network.SendCommand(Core.Command_Preview, data: MyAPIGateway.Utilities.SerializeToBinary(new PreviewGridData() { GarageId = gsb.Entity.EntityId, Index = gsb.SelectedGridIndex }));
						}
					}

					if (gsb.SelectedGridIndex != -1)
					{
						gsb.GridSelectionMode = false;
						gsb.StartSpectatorView();
					}
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on SpawnSelectedGridAction\n{e.ToString()}");
			}
		}

		/// <summary>
		/// Update visual while selecting
		/// </summary>
		public override void UpdateBeforeSimulation()
		{
			try
			{
				// keep users from placing blocks in spectator
				if (MyCubeBuilder.Static.BlockCreationIsActivated)
				{
					MyCubeBuilder.Static.DeactivateBlockCreation();
				}

				// bind camera to a 1000m sphere
				Vector3D gridToCamera = (ModBlock.CubeGrid.WorldAABB.Center - MyAPIGateway.Session.Camera.WorldMatrix.Translation);
				if (gridToCamera.LengthSquared() > Core.Config.CameraOrbitDistance * Core.Config.CameraOrbitDistance)
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
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on UpdateBeforeSimulation\n{e.ToString()}");
			}
		}

		private void GridSelect(List<MyMouseButtonsEnum> buttons)
		{
			try
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
					bool isValid = true;

					if ((DateTime.UtcNow - StorageCooldown.Value).TotalSeconds < Core.Config.StorageCooldown)
					{
						GridSelectErrorMessage = $"Storage is on Cooldown: {(Core.Config.StorageCooldown - ((DateTime.UtcNow - StorageCooldown.Value).TotalMilliseconds / 1000)).ToString("n2")} seconds";
						isValid = false;
					}

					if (SelectedGridEntity != hitGrid)
					{
						SelectedGridEntity = hitGrid;
						GridSelectErrorMessage = null;
						List<IMyCubeGrid> subgrids = MyAPIGateway.GridGroups.GetGroup(hitGrid, GridLinkTypeEnum.Mechanical);

						foreach (MyCubeGrid grid in subgrids)
						{
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
							else if (MyAPIGateway.Players.GetPlayerControllingEntity(grid) != null)
							{
								GridSelectErrorMessage = $"Someone is controlling this grid";
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
								StorePrefab(SelectedGridEntity);
							}
						}
						else
						{
							StoreGridData data = new StoreGridData() { GarageId = Entity.EntityId, TargetId = SelectedGridEntity.EntityId };

							Network.SendCommand(Core.Command_Store, data: MyAPIGateway.Utilities.SerializeToBinary(data));
						}

						StorageCooldown.Value = DateTime.UtcNow;
					}
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on GridSelect\n{e.ToString()}");
				ResetView();
			}
		}

		private void GridPlacement(List<MyMouseButtonsEnum> buttons)
		{
			try
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
				else if (PlacementDistance > Core.Config.CameraPlacementDistance)
				{
					PlacementDistance = Core.Config.CameraPlacementDistance;
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

				if ((DateTime.UtcNow - SpawnCooldown.Value).TotalSeconds < Core.Config.SpawnCooldown)
				{
					MyAPIGateway.Utilities.ShowNotification($"Spawn is on Cooldown: {(Core.Config.SpawnCooldown - ((DateTime.UtcNow - SpawnCooldown.Value).TotalMilliseconds / 1000)).ToString("n2")} seconds", 1, "Red");
					isValid = false;
				}

				if (isValid)
				{
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
						else if (ent is IMyCharacter)
						{
							IMyCharacter character = ent as IMyCharacter;

							if (character.ControllerInfo == null || character.ControllerInfo.ControllingIdentityId == 0)
							{
								MyAPIGateway.Utilities.ShowNotification($"WARNING! Uncontrolled Player Obstruction: {ent.DisplayName}", 1, "Red");
							}
							else
							{
								isValid = false;
							}
						}
						else
						{
							MyAPIGateway.Utilities.ShowNotification($"Obstruction {ent.GetType().Name} {ent.DisplayName}", 1, "Red");
							isValid = false;
						}
					}

					if (isValid)
					{
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

					long playerId = (MyAPIGateway.Session.Player == null) ? 0 : MyAPIGateway.Session.Player.IdentityId;

					if (MyAPIGateway.Multiplayer.IsServer)
					{
						PlacePrefab(GridList[SelectedGridIndex], matrix.Translation, playerId);
					}
					else
					{
						PlaceGridData data = new PlaceGridData() {
							GarageId = Entity.EntityId,
							GridIndex = SelectedGridIndex,
							GridName = GridNames.Value[SelectedGridIndex],
							NewOwner = playerId,
							Position = new SerializableVector3D(matrix.Translation)
						};

						Network.SendCommand(Core.Command_Place, null, MyAPIGateway.Utilities.SerializeToBinary(data));
					}

					SpawnCooldown.Value = DateTime.UtcNow;

					ResetView();
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on GridPlacement\n{e.ToString()}");
				ResetView();
			}
		}

		private List<MyCubeGrid> CreateGridProjection(List<MyObjectBuilder_CubeGrid> grids)
		{
			try
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
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on CreateGridProjection\n{e.ToString()}");
				return null;
			}
		}

		private static void ShowHideBoundingBoxGridGroup(IMyCubeGrid grid, bool enabled, Vector4? color = null)
		{
			try
			{
				List<IMyCubeGrid> subgrids = MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical);
				foreach (MyCubeGrid sub in subgrids)
				{
					MyAPIGateway.Entities.EnableEntityBoundingBoxDraw(sub, enabled, color, GridSelectLineSize);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on ShowHideBoundingBoxGridGroup\n{e.ToString()}");
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
			MyLog.Default.Info("[Grid Garage] Creating controls");

			Tools.CreateControlCheckbox("GridGarage_DisplayHologram", "Display Hologram", "Shows the hologram of the selected ship", ControlsVisible_Basic, ControlsEnabled_Basic,
			(block) => {
				GridStorageBlock garage = block.GameLogic?.GetAs<GridStorageBlock>();

				if (garage != null)
				{
					return garage.DisplayHologram.Value;
				}

				return false;
			},
			(block, value) => {
				GridStorageBlock garage = block.GameLogic?.GetAs<GridStorageBlock>();
				if (garage == null)
					return;

				garage.DisplayHologram.Value = value;
			});

			Tools.CreateControlButton("GridGarage_Store", "Store Grid", "Lets you select a grid to store", ControlsVisible_Basic, ControlsEnabled_Basic, StoreSelectedGridAction);

			Tools.CreateControlListbox("GridGarage_GridList", "Grid List", "Select a grid to spawn", 8, ControlsVisible_Basic, ControlsEnabled_Basic,
			(block, items, selected) => {
				GridStorageBlock garage = block.GameLogic?.GetAs<GridStorageBlock>();
				if (garage == null)
					return;

				for (int i = 0; i < garage.GridNames.Value.Count; i++)
				{
					string name = garage.GridNames.Value[i];
					MyTerminalControlListBoxItem listItem = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(name), MyStringId.GetOrCompute(""), i);
					items.Add(listItem);

					if (garage.SelectedGridIndex == i)
					{
						selected.Add(listItem);
					}
				}
			},
			(block, items) => {
				GridStorageBlock garage = block.GameLogic?.GetAs<GridStorageBlock>();
				if (garage == null)
					return;

				garage.SelectedGridIndex = (int)items[0].UserData;
			});

			Tools.CreateControlButton("GridGarage_Spawn", "Spawn Grid", "Spawns the grid selected in the listbox", ControlsVisible_Basic, ControlsEnabled_Basic, SpawnSelectedGridAction);
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

		public void StorePrefab(long gridId)
		{
			StorePrefab(MyAPIGateway.Entities.GetEntityById(gridId) as IMyCubeGrid);
		}

		public void StorePrefab(IMyCubeGrid grid)
		{
			try
			{
				// create prefab from parent and subgrids
				Prefab prefab = new Prefab();
				prefab.Name = grid.DisplayName;
				// forced into doing strings cause the dumb serializer is erroring out otherwise.
				prefab.Grids.Add(MyAPIGateway.Utilities.SerializeToXML((MyObjectBuilder_CubeGrid)grid.GetObjectBuilder()));

				List<IMyCubeGrid> grids = MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical);
				grids.Remove(grid);

				for (int i = 0; i < grids.Count; i++)
				{
					prefab.Grids.Add(MyAPIGateway.Utilities.SerializeToXML((MyObjectBuilder_CubeGrid)grids[i].GetObjectBuilder()));
				}

				// GridList is saved when the game is.
				GridList.Add(prefab);
				UpdateGridNames();

				// remove existing grid from world
				MyAPIGateway.Entities.MarkForClose(grid);
				foreach (IMyCubeGrid subs in grids)
				{
					MyAPIGateway.Entities.MarkForClose(subs);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed to save grid prefab:\n{e.ToString()}");
			}
		}

		public void PlacePrefab(int prefabIndex, string prefabName, Vector3D position, long ownerId)
		{
			try
			{
				Prefab fab = GridList[prefabIndex];

				if (fab.Name == prefabName)
				{
					PlacePrefab(fab, position, ownerId);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Error in function PlacePrefab: {e.ToString()}");
			}
		}

		public void PlacePrefab(Prefab fab, Vector3D position, long ownerId)
		{
			try
			{
				List<MyObjectBuilder_CubeGrid> grids = fab.UnpackGrids();

				MyLog.Default.Info($"[Grid Garage] Spawning grid: {fab.Name}");

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

				GridList.Remove(fab);
				UpdateGridNames();
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed to spawn grid:\n{e.ToString()}");
			}
		}

		/// <summary>
		/// Save prefab list to world file
		/// </summary>
		private void Save()
		{
			try
			{
				MyModStorageComponentBase storage = GetStorage(Entity);

				StorageData desc = new StorageData() {
					StoredGrids = GridList
				};

				string output = BitConverter.ToString(MyCompression.Compress(MyAPIGateway.Utilities.SerializeToBinary(desc))).Replace("-", "");

				if (storage.ContainsKey(StorageGuid))
				{
					storage[StorageGuid] = output;
				}
				else
				{
					storage.Add(new KeyValuePair<Guid, string>(StorageGuid, output));
				}

				MyLog.Default.Info($"[GridGarage] {Entity.EntityId}: Data Saved. Size: {output.Length}");
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on Save \n{e.ToString()}");
			}
		}

		/// <summary>
		/// Load prefab list from world file
		/// </summary>
		private void Load()
		{
			try
			{
				MyModStorageComponentBase storage = GetStorage(Entity);
				if (storage.ContainsKey(StorageGuid))
				{
					// Remove stored grids if block is not fully built
					if (ModBlock.SlimBlock.BuildLevelRatio <= 0.2f)
					{
						storage[StorageGuid] = string.Empty;
					}

					StorageData data;

					string hex = storage[StorageGuid];
					byte[] raw = new byte[hex.Length / 2];
					for (int i = 0; i < raw.Length; i++)
					{
						raw[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
					}

					try
					{
						data = MyAPIGateway.Utilities.SerializeFromBinary<StorageData>(MyCompression.Decompress(raw));
					}
					catch (Exception e)
					{
						MyLog.Default.Warning($"[Grid Garage] Failed to load block details. Trying non compression method");
						data = MyAPIGateway.Utilities.SerializeFromBinary<StorageData>(raw);
					}

					GridList = data.StoredGrids;
					UpdateGridNames();
				}
				else
				{
					MyLog.Default.Info($"[Grid Garage] No data saved for: {Entity.EntityId}");
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Loading Error: {e.ToString()}");
			}
		}

		private void UpdateGridNames()
		{
			GridNames.Value.Clear();
			for (int i = 0; i < GridList.Count; i++)
			{
				GridNames.Value.Add(GridList[i].Name);
			}

			GridNames.Push();
		}

		public static bool GridHasNonFactionOwners(IMyCubeGrid grid)
		{
			try
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
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on GridHasNonFactionOwners\n{e.ToString()}");
				return false;
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