using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
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
	public class GridStorageBlock : MyGameLogicComponent
	{
		public const float GridSelectLineSize = 0.2f;

		private static readonly Guid StorageGuid = new Guid("19906e82-9e21-458d-8f02-7bf7030ed604");

		public MyCubeGrid Grid;
		public IMyTerminalBlock ModBlock;
		public MyCubeBlock CubeBlock;

		public List<Prefab> GridList = new List<Prefab>();

		public NetSync<List<string>> GridNames;
		public NetSync<bool> DisplayHologram;
		public NetSync<Prefab> HologramPrefab;
		private NetSync<DateTime> StorageCooldown;
		private NetSync<DateTime> SpawnCooldown;
		private NetSync<int> SelectedGridIndex;

		private IMyCameraController CameraController = null;
		private bool GridSelectionMode = true;
		string GridSelectErrorMessage = null;
		private float PlacementDistance = 200;

		private bool IsSpectating = false;

		private IMyCubeGrid SelectedGridEntity = null;
		public Prefab PlaceGridPrefab = null;
		private List<MyCubeGrid> PlaceGrids = null;
		private List<MyCubeGrid> HoloGrids = null;
		private float HologramOffset;
		private float HologramScale;

		private CancelToken CancelHoloGridJob = null;
		private CancelToken CancelPlaceGridJob = null;

		private int WaitingCounter = 0;
		private int LoadingCounter = 0;

		internal class CancelToken
		{
			public bool IsCancelRequested = false;

			public void Cancel() { IsCancelRequested = true; }
		}

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
				HologramPrefab = new NetSync<Prefab>(this, TransferType.ServerToClient, new Prefab());
				HologramPrefab.ValueChanged += HologramPrefabChanged;

				StorageCooldown = new NetSync<DateTime>(this, TransferType.Both, DateTime.MinValue);
				SpawnCooldown = new NetSync<DateTime>(this, TransferType.Both, DateTime.MinValue);
				SelectedGridIndex = new NetSync<int>(this, TransferType.Both, -1);
				SelectedGridIndex.ValueChanged += SelectedGridIndexChanged;

				if (MyAPIGateway.Multiplayer.IsServer)
				{
					DisplayHologram.ValueChanged += EnableHologramChanged;
					Load();
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Failed on Init\n{e.ToString()}");
			}

			NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_FRAME;
		}

		private void EnableHologramChanged(bool o, bool n)
		{
			if (!n)
			{
				HologramPrefab.Value = new Prefab();
			}
			else if (SelectedGridIndex.Value != -1)
			{
				if (MyAPIGateway.Multiplayer.IsServer)
				{
					HologramPrefab.Value = GridList[SelectedGridIndex.Value];
				}
			}
		}

		private void SelectedGridIndexChanged(int o, int n)
		{
			if (!DisplayHologram.Value || o == n)
				return;

			if (n == -1)
			{
				HologramPrefab.Value = new Prefab();
			}
			else
			{
				if (MyAPIGateway.Multiplayer.IsServer)
				{
					HologramPrefab.Value = GridList[n];
				}
			}
		}

		private void HologramPrefabChanged(Prefab o, Prefab n)
		{

			if (HoloGrids != null)
			{
				foreach (var g in HoloGrids)
				{
					MyAPIGateway.Entities.MarkForClose(g);
				}

				HoloGrids = null;
			}

			if (n.Grids.Count > 0)
			{
				CreateHoloProjection(n);
			}
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

				IsSpectating = true;
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
					if (PlaceGrids != null)
					{
						foreach (var grid in PlaceGrids)
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

				if (PlaceGrids != null && PlaceGrids.Count > 0)
				{
					ShowHideBoundingBoxGridGroup(PlaceGrids[0], false);
				}

				PlaceGridPrefab = null;
				SelectedGridEntity = null;
				PlaceGrids = null;
				IsSpectating = false;
				LoadingCounter = 0;
				WaitingCounter = 0;
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
					if (MyAPIGateway.Multiplayer.IsServer)
					{
						if (gsb.SelectedGridIndex.Value != -1)
						{
							gsb.PlaceGridPrefab = gsb.GridList[gsb.SelectedGridIndex.Value];
						}
					}
					else
					{
						if (gsb.SelectedGridIndex.Value != -1)
						{
							NetworkAPI.Instance.SendCommand(Core.Command_Preview, data: MyAPIGateway.Utilities.SerializeToBinary(new PreviewGridData() { GarageId = gsb.Entity.EntityId, Index = gsb.SelectedGridIndex.Value }));
						}
					}

					if (gsb.SelectedGridIndex.Value != -1)
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

		public override void UpdateBeforeSimulation()
		{
			if (!IsSpectating)
				return;

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

			MyAPIGateway.Utilities.ShowNotification($"Select (LMB) - Cancel (RMB) - Orbit ({gridToCamera.Length().ToString("n0")}/{Core.Config.CameraOrbitDistance}) - Range ({Core.Config.CameraPlacementDistance})", 1, "White");

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

		public override void UpdateAfterSimulation()
		{
			AnimateHologram();
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
						List<IMyCubeGrid> subgrids = new List<IMyCubeGrid>();
						MyAPIGateway.GridGroups.GetGroup(hitGrid, GridLinkTypeEnum.Mechanical, subgrids);

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
							else if (Core.Config.MaxGridCount != 0 && GridList.Count >= Core.Config.MaxGridCount)
							{
								GridSelectErrorMessage = $"Grid Garage is full. Maximum grid count: {Core.Config.MaxGridCount}";
								isValid = false;
							}
							else if (!Core.Config.CanStoreUnownedGrids && grid.BigOwners.Count == 0)
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

							NetworkAPI.Instance.SendCommand(Core.Command_Store, data: MyAPIGateway.Utilities.SerializeToBinary(data));
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
				if (!GridSelectionMode && PlaceGridPrefab == null)
				{
					WaitingCounter++;

					string s = new string('.', WaitingCounter / 60);

					MyAPIGateway.Utilities.ShowNotification(s + "WAITING FOR DATA" + s, 1, "White");
					return;
				}

				if (CancelPlaceGridJob != null)
				{
					LoadingCounter++;

					string s = new string('.', LoadingCounter / 60);

					MyAPIGateway.Utilities.ShowNotification(s + "LOADING" + s, 1, "White");
					return;
				}

				if (PlaceGrids == null && CancelPlaceGridJob == null)
				{
					CreateGridProjection(PlaceGridPrefab);
					return;
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

				MatrixD camMatrix = MyAPIGateway.Session.Camera.WorldMatrix;

				List<MyKeys> keys = new List<MyKeys>();
				MyAPIGateway.Input.GetListOfPressedKeys(keys);

				MatrixD rotation = MatrixD.Identity;
				if (keys.Contains(MyKeys.PageUp))
				{
					rotation *= MatrixD.CreateFromAxisAngle(camMatrix.Forward, 0.01);
				}

				if (keys.Contains(MyKeys.PageDown))
				{
					rotation *= MatrixD.CreateFromAxisAngle(camMatrix.Up, 0.01);
				}

				if (keys.Contains(MyKeys.Home))
				{
					rotation *= MatrixD.CreateFromAxisAngle(camMatrix.Left, 0.01);
				}

				if (keys.Contains(MyKeys.End))
				{
					rotation *= MatrixD.CreateFromAxisAngle(camMatrix.Left, -0.01);
				}

				if (keys.Contains(MyKeys.Insert))
				{
					rotation *= MatrixD.CreateFromAxisAngle(camMatrix.Forward, -0.01);
				}

				if (keys.Contains(MyKeys.Delete))
				{
					rotation *= MatrixD.CreateFromAxisAngle(camMatrix.Up, -0.01);
				}

				Vector3D parentGridPosition = PlaceGrids[0].WorldMatrix.Translation;
				foreach (MyCubeGrid grid in PlaceGrids)
				{
					MatrixD gridMatrix = grid.WorldMatrix;

					Vector3D hingePoint = camMatrix.Translation + (camMatrix.Forward * PlacementDistance);
					MatrixD nagative = MatrixD.CreateTranslation(-hingePoint);
					MatrixD positive = MatrixD.CreateTranslation(hingePoint);

					gridMatrix = gridMatrix * (nagative * rotation * positive);

					Vector3D offset = (parentGridPosition - gridMatrix.Translation);

					gridMatrix.Translation = hingePoint - offset;

					grid.WorldMatrix = gridMatrix;
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
					BoundingBoxD box = PlaceGrids[0].GetPhysicalGroupAABB();
					List<IMyEntity> entities = MyAPIGateway.Entities.GetEntitiesInAABB(ref box);
					entities.RemoveAll((e) => !(e is MyVoxelBase || (e is IMyCubeBlock && !PlaceGrids.Contains((MyCubeGrid)(e as IMyCubeBlock).CubeGrid)) || e is IMyCharacter || e is IMyFloatingObject));

					foreach (IMyEntity ent in entities)
					{
						if (!isValid)
							break;

						if (ent is MyVoxelBase)
						{
							foreach (MyCubeGrid grid in PlaceGrids)
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
							flag = new MyOrientedBoundingBoxD(zone.PositionComp.LocalAABB, zone.WorldMatrix).Intersects(ref box);

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
					ShowHideBoundingBoxGridGroup(PlaceGrids[0], true, Color.LightGreen.ToVector4());
				}
				else
				{
					ShowHideBoundingBoxGridGroup(PlaceGrids[0], true, Color.Red.ToVector4());
				}

				// place grid
				if (isValid && buttons.Contains(MyMouseButtonsEnum.Left))
				{
					if (PlaceGrids != null)
					{
						foreach (var grid in PlaceGrids)
						{
							MyAPIGateway.Entities.MarkForClose(grid);
						}
					}

					//MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;
					//matrix.Translation += (matrix.Forward * PlacementDistance);

					List<MyPositionAndOrientation> matrixData = new List<MyPositionAndOrientation>();
					foreach (MyCubeGrid grid in PlaceGrids)
					{
						matrixData.Add(new MyPositionAndOrientation(grid.WorldMatrix));
					}

					long playerId = (MyAPIGateway.Session.Player == null) ? 0 : MyAPIGateway.Session.Player.IdentityId;

					if (MyAPIGateway.Multiplayer.IsServer)
					{
						PlacePrefab(GridList[SelectedGridIndex.Value], matrixData, playerId);
					}
					else
					{
						PlaceGridData data = new PlaceGridData() {
							GarageId = Entity.EntityId,
							GridIndex = SelectedGridIndex.Value,
							GridName = GridNames.Value[SelectedGridIndex.Value],
							NewOwner = playerId,
							MatrixData = matrixData
						};

						NetworkAPI.Instance.SendCommand(Core.Command_Place, null, MyAPIGateway.Utilities.SerializeToBinary(data));
					}

					SelectedGridIndex.Value = -1;
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

		private void CreateHoloProjection(Prefab fab)
		{
			CancelHoloGridJob?.Cancel(); // cancel any previous jobs
			SetLoadingProjection();

			MyAPIGateway.Parallel.StartBackground(() => {
				CancelToken token = new CancelToken();
				CancelHoloGridJob = token;

				List<MyCubeGrid> cubeGrids = new List<MyCubeGrid>();
				List<MyObjectBuilder_CubeGrid> grids = fab.UnpackGrids();
				MyAPIGateway.Entities.RemapObjectBuilderCollection(grids);

				foreach (MyObjectBuilder_CubeGrid gridBuilder in grids)
				{
					gridBuilder.AngularVelocity = new SerializableVector3();
					gridBuilder.LinearVelocity = new SerializableVector3();
					gridBuilder.XMirroxPlane = null;
					gridBuilder.YMirroxPlane = null;
					gridBuilder.ZMirroxPlane = null;
					gridBuilder.IsStatic = false;
					gridBuilder.DestructibleBlocks = false;
					gridBuilder.CreatePhysics = false;
					gridBuilder.IsRespawnGrid = false;
					gridBuilder.DampenersEnabled = false;
					gridBuilder.IsPowered = false;

					foreach (MyObjectBuilder_CubeBlock block in gridBuilder.CubeBlocks)
					{
						MyObjectBuilder_FunctionalBlock fblock = block as MyObjectBuilder_FunctionalBlock;
						if (fblock != null)
						{
							fblock.Enabled = false;
						}
					}

					if (token.IsCancelRequested)
						return;

					MyAPIGateway.Entities.CreateFromObjectBuilderParallel(gridBuilder, false, (e) => {
						MyCubeGrid g = e as MyCubeGrid;

						if (g != null)
						{
							lock (cubeGrids)
							{
								cubeGrids.Add(g);
							}

							if (cubeGrids.Count == grids.Count)
							{
								MyCubeGrid parent = cubeGrids[0];
								BoundingBoxD parentBoundingBox = parent.PositionComp.WorldAABB;
								BoundingBoxD groupBoundingBox = new BoundingBoxD(parentBoundingBox.Min, parentBoundingBox.Max);

								foreach (MyCubeGrid grid in cubeGrids)
								{
									grid.IsPreview = true;
									grid.SyncFlag = false;
									grid.Save = false;
									grid.Flags |= EntityFlags.IsNotGamePrunningStructureObject;
									grid.Render.CastShadows = false;

									groupBoundingBox.Include(grid.PositionComp.WorldAABB);
								}

								double longestSide = groupBoundingBox.Size.Max();
								HologramScale = 1f / (float)(longestSide / (CubeBlock.CubeGrid.GridSize * 0.45f));
								HologramOffset = (float)(0.5f + (longestSide * HologramScale) * 0.5f);

								Vector3D holoOrigin = CubeBlock.PositionComp.WorldAABB.Center + (CubeBlock.WorldMatrix.Up * HologramOffset);

								if (token.IsCancelRequested)
									return;

								foreach (var grid in cubeGrids)
								{
									MatrixD matrix = grid.WorldMatrix;

									Vector3D offset = groupBoundingBox.Center - matrix.Translation;
									Vector3D scaledOffset = offset * HologramScale;

									matrix.Translation = holoOrigin - scaledOffset;

									grid.WorldMatrix = matrix;
									grid.PositionComp.Scale = HologramScale;

									MyAPIGateway.Entities.AddEntity(grid);
								}

								foreach (MyCubeGrid cg in HoloGrids)
								{
									MyAPIGateway.Entities.MarkForClose(cg);
								}

								HoloGrids = cubeGrids;
								CancelHoloGridJob = null;
							}
						}
					});
				}
			});
		}

		private void CreateGridProjection(Prefab fab)
		{
			CancelPlaceGridJob?.Cancel();
			MyAPIGateway.Parallel.StartBackground(() => {

				CancelToken token = new CancelToken();
				CancelPlaceGridJob = token;

				List<MyCubeGrid> cubeGrids = new List<MyCubeGrid>();
				List<MyObjectBuilder_CubeGrid> grids = fab.UnpackGrids();
				MyAPIGateway.Entities.RemapObjectBuilderCollection(grids);

				foreach (MyObjectBuilder_CubeGrid gridBuilder in grids)
				{
					gridBuilder.AngularVelocity = new SerializableVector3();
					gridBuilder.LinearVelocity = new SerializableVector3();
					gridBuilder.XMirroxPlane = null;
					gridBuilder.YMirroxPlane = null;
					gridBuilder.ZMirroxPlane = null;
					gridBuilder.IsStatic = false;
					gridBuilder.DestructibleBlocks = false;
					gridBuilder.CreatePhysics = false;
					gridBuilder.IsRespawnGrid = false;
					gridBuilder.DampenersEnabled = false;
					gridBuilder.IsPowered = false;

					foreach (MyObjectBuilder_CubeBlock block in gridBuilder.CubeBlocks)
					{
						MyObjectBuilder_FunctionalBlock fblock = block as MyObjectBuilder_FunctionalBlock;
						if (fblock != null)
						{
							fblock.Enabled = false;
						}
					}

					if (token.IsCancelRequested)
						return;

					MyAPIGateway.Entities.CreateFromObjectBuilderParallel(gridBuilder, false, (e) => {
						MyCubeGrid g = e as MyCubeGrid;
						if (g != null)
						{
							cubeGrids.Add(g);

							if (cubeGrids.Count == grids.Count)
							{
								if (token.IsCancelRequested)
									return;

								foreach (MyCubeGrid grid in cubeGrids)
								{
									grid.IsPreview = true;
									grid.SyncFlag = false;
									grid.Save = false;
									grid.Flags |= EntityFlags.IsNotGamePrunningStructureObject;
									grid.Render.CastShadows = false;

									MyAPIGateway.Entities.AddEntity(grid);
								}

								PlaceGrids = cubeGrids;
								CancelPlaceGridJob = null;
							}
						}
					});
				}
			});
		}

		private MyCubeGrid CreateLoadingGrid()
		{
			var gridObjectBuilder = new MyObjectBuilder_CubeGrid() {
				EntityId = 0,
				Skeleton = new List<BoneInfo>(),
				ConveyorLines = new List<MyObjectBuilder_ConveyorLine>(),
				BlockGroups = new List<MyObjectBuilder_BlockGroup>(),
				Handbrake = false,
				XMirroxPlane = null,
				YMirroxPlane = null,
				ZMirroxPlane = null,
				PersistentFlags = MyPersistentEntityFlags2.CastShadows | MyPersistentEntityFlags2.InScene,
				GridSizeEnum = MyCubeSize.Large,
				IsStatic = false,
				Name = "HoloLoadingGrid",
				CreatePhysics = false,
				LinearVelocity = new SerializableVector3(),
				AngularVelocity = new SerializableVector3(),
				PositionAndOrientation = new MyPositionAndOrientation(),
				DisplayName = "HoloLoadingGrid",
				DestructibleBlocks = true,
			};

			MyObjectBuilder_CubeBlock l = new MyObjectBuilder_CubeBlock() {
				Min = new SerializableVector3I(0, 0, 0),
				SubtypeName = "LargeSymbolL",
				EntityId = 0,
				Owner = 0,
				BlockOrientation = new SerializableBlockOrientation(),
				ShareMode = MyOwnershipShareModeEnum.All,
				Name = "L",
			};
			gridObjectBuilder.CubeBlocks.Add(l);

			MyObjectBuilder_CubeBlock o = new MyObjectBuilder_CubeBlock() {
				Min = new SerializableVector3I(1, 0, 0),
				SubtypeName = "LargeSymbolO",
				EntityId = 0,
				Owner = 0,
				BlockOrientation = new SerializableBlockOrientation(),
				ShareMode = MyOwnershipShareModeEnum.All,
				Name = "L",
			};
			gridObjectBuilder.CubeBlocks.Add(o);

			MyObjectBuilder_CubeBlock a = new MyObjectBuilder_CubeBlock() {
				Min = new SerializableVector3I(2, 0, 0),
				SubtypeName = "LargeSymbolA",
				EntityId = 0,
				Owner = 0,
				BlockOrientation = new SerializableBlockOrientation(),
				ShareMode = MyOwnershipShareModeEnum.All,
				Name = "L",
			};
			gridObjectBuilder.CubeBlocks.Add(a);

			MyObjectBuilder_CubeBlock d = new MyObjectBuilder_CubeBlock() {
				Min = new SerializableVector3I(3, 0, 0),
				SubtypeName = "LargeSymbolD",
				EntityId = 0,
				Owner = 0,
				BlockOrientation = new SerializableBlockOrientation(),
				ShareMode = MyOwnershipShareModeEnum.All,
				Name = "L",
			};
			gridObjectBuilder.CubeBlocks.Add(d);

			MyObjectBuilder_CubeBlock i = new MyObjectBuilder_CubeBlock() {
				Min = new SerializableVector3I(4, 0, 0),
				SubtypeName = "LargeSymbolI",
				EntityId = 0,
				Owner = 0,
				BlockOrientation = new SerializableBlockOrientation(),
				ShareMode = MyOwnershipShareModeEnum.All,
				Name = "L",
			};
			gridObjectBuilder.CubeBlocks.Add(i);

			MyObjectBuilder_CubeBlock n = new MyObjectBuilder_CubeBlock() {
				Min = new SerializableVector3I(5, 0, 0),
				SubtypeName = "LargeSymbolN",
				EntityId = 0,
				Owner = 0,
				BlockOrientation = new SerializableBlockOrientation(),
				ShareMode = MyOwnershipShareModeEnum.All,
				Name = "L",
			};
			gridObjectBuilder.CubeBlocks.Add(n);

			MyObjectBuilder_CubeBlock g = new MyObjectBuilder_CubeBlock() {
				Min = new SerializableVector3I(6, 0, 0),
				SubtypeName = "LargeSymbolG",
				EntityId = 0,
				Owner = 0,
				BlockOrientation = new SerializableBlockOrientation(),
				ShareMode = MyOwnershipShareModeEnum.All,
				Name = "L",
			};
			gridObjectBuilder.CubeBlocks.Add(g);

			MyCubeGrid grid = (MyCubeGrid)MyAPIGateway.Entities.CreateFromObjectBuilder(gridObjectBuilder);
			grid.IsPreview = true;
			grid.SyncFlag = false;
			grid.Save = false;
			grid.Flags |= EntityFlags.IsNotGamePrunningStructureObject;
			grid.Render.CastShadows = false;
			MyAPIGateway.Entities.AddEntity(grid);

			var box = grid.PositionComp.WorldAABB;

			Vector3D size = new Vector3D() {
				X = Math.Abs(box.Min.X) + Math.Abs(box.Max.X),
				Y = Math.Abs(box.Min.Y) + Math.Abs(box.Max.Y),
				Z = Math.Abs(box.Min.Z) + Math.Abs(box.Max.Z)
			};

			double longestSide = (size.X > size.Y) ? size.X : size.Y;
			if (size.Z > longestSide)
			{
				longestSide = size.Z;
			}

			HologramScale = 1f / (float)(longestSide / (CubeBlock.CubeGrid.GridSize / 2f));
			HologramOffset = (float)(1.3f + (longestSide * HologramScale));

			MatrixD matrix = Entity.WorldMatrix;
			matrix.Translation = CubeBlock.PositionComp.WorldAABB.Center + (CubeBlock.WorldMatrix.Up * HologramOffset);
			grid.WorldMatrix = matrix;
			grid.PositionComp.Scale = HologramScale;
			MyAPIGateway.Entities.AddEntity(grid);

			return grid;
		}

		private void SetLoadingProjection()
		{
			HoloGrids = new List<MyCubeGrid>() { CreateLoadingGrid() };
		}

		private void AnimateHologram()
		{
			if (HoloGrids == null || HoloGrids.Count == 0)
				return;

			MyCubeGrid parent = HoloGrids[0];
			BoundingBoxD parentBoundingBox = parent.PositionComp.WorldAABB;
			BoundingBoxD groupBoundingBox = new BoundingBoxD(parentBoundingBox.Min, parentBoundingBox.Max);
			foreach (MyCubeGrid cg in HoloGrids)
			{
				groupBoundingBox.Include(cg.PositionComp.WorldAABB);
			}

			Vector3D groupCenter = groupBoundingBox.Center;
			
			MatrixD rotation = MatrixD.CreateFromAxisAngle(CubeBlock.WorldMatrix.Up, 0.005);
			
			Vector3D hingePoint = CubeBlock.PositionComp.WorldAABB.Center + (CubeBlock.WorldMatrix.Up * HologramOffset);
			MatrixD nagative = MatrixD.CreateTranslation(-hingePoint);
			MatrixD positive = MatrixD.CreateTranslation(hingePoint);

			foreach (var grid in HoloGrids)
			{
				MatrixD gridMatrix = grid.WorldMatrix;

				// rotate
				gridMatrix = gridMatrix * (nagative * rotation * positive);

				// re-align to grid garage
				Vector3D offset = groupCenter - gridMatrix.Translation;
				gridMatrix.Translation = hingePoint - offset;

				grid.WorldMatrix = gridMatrix;
			}
		}

		private static void ShowHideBoundingBoxGridGroup(IMyCubeGrid grid, bool enabled, Vector4? color = null)
		{
			try
			{
				List<IMyCubeGrid> subgrids = new List<IMyCubeGrid>();
				MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, subgrids);
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

					if (garage.SelectedGridIndex.Value == i)
					{
						selected.Add(listItem);
					}
				}
			},
			(block, items) => {
				GridStorageBlock garage = block.GameLogic?.GetAs<GridStorageBlock>();
				if (garage == null)
					return;

				garage.SelectedGridIndex.Value = (int)items[0].UserData;
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

				List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
				MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, grids);
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

		public void PlacePrefab(int prefabIndex, string prefabName, List<MyPositionAndOrientation> matrixData, long ownerId)
		{
			try
			{
				Prefab fab = GridList[prefabIndex];

				if (fab.Name == prefabName)
				{
					PlacePrefab(fab, matrixData, ownerId);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Error($"[Grid Garage] Error in function PlacePrefab: {e.ToString()}");
			}
		}

		public void PlacePrefab(Prefab fab, List<MyPositionAndOrientation> matrixData, long ownerId)
		{
			MyAPIGateway.Parallel.StartBackground(() => {
				try
				{
					List<MyObjectBuilder_CubeGrid> grids = fab.UnpackGrids();

					MyLog.Default.Info($"[Grid Garage] Spawning grid: {fab.Name}");

					if (grids == null || grids.Count == 0)
					{
						return;
					}

					MyAPIGateway.Entities.RemapObjectBuilderCollection(grids);
					for (int i = 0; i < grids.Count; i++)
					{
						MyObjectBuilder_CubeGrid grid = grids[i];
						grid.AngularVelocity = new SerializableVector3();
						grid.LinearVelocity = new SerializableVector3();
						grid.XMirroxPlane = null;
						grid.YMirroxPlane = null;
						grid.ZMirroxPlane = null;
						grid.IsStatic = false;
						grid.CreatePhysics = true;
						grid.IsRespawnGrid = false;

						grid.PositionAndOrientation = matrixData[i];

						foreach (MyObjectBuilder_CubeBlock cubeBlock in grid.CubeBlocks)
						{
							cubeBlock.Owner = ownerId;
						}

					}

					foreach (MyObjectBuilder_CubeGrid grid in grids)
					{
						MyAPIGateway.Entities.CreateFromObjectBuilderParallel(grid, true);
					}

					GridList.Remove(fab);
					UpdateGridNames();
				}
				catch (Exception e)
				{
					MyLog.Default.Error($"[Grid Garage] Failed to spawn grid:\n{e.ToString()}");
				}
			});
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

				byte[] bytes = MyAPIGateway.Utilities.SerializeToBinary(desc);
				byte[] compressed = MyCompression.Compress(bytes);
				string output = BitConverter.ToString(MyCompression.Compress(bytes)).Replace("-", "");

				if (storage.ContainsKey(StorageGuid))
				{
					storage[StorageGuid] = output;
				}
				else
				{
					storage.Add(new KeyValuePair<Guid, string>(StorageGuid, output));
				}

				MyLog.Default.Info($"[Grid Garage] Data Saved {Entity.EntityId} String size {output.Length},  General size: {bytes.Length} bytes, Compressed size: {compressed.Length} bytes");
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
						MyLog.Default.Warning($"[Grid Garage] {ModBlock.CubeGrid.CustomName}\\{Entity.EntityId} is below critical. Normally grid garage would remove grids within this block. Due to a bug this is being disabled.");
						//storage[StorageGuid] = string.Empty;
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
						byte[] bytes = MyCompression.Decompress(raw);
						data = MyAPIGateway.Utilities.SerializeFromBinary<StorageData>(bytes);

						MyLog.Default.Info($"[Grid Garage] Data Saved {Entity.EntityId} String size {hex.Length},  General size: {bytes.Length} bytes, Compressed size: {raw.Length} bytes");
					}
					catch
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