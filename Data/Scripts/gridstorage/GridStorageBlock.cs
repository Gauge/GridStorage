using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using System.IO;
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
        public NetSync<string> Id;
        public NetSync<List<string>> GridList;
        private NetSync<DateTime> StorageCooldown;
        private NetSync<DateTime> SpawnCooldown;
        public string SelectedGrid = null;
        private IMyCubeGrid SelectedGridEntity = null;

        private IMyTerminalControlLabel IdLabel = null;

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
            try
            {
                base.Init(objectBuilder);

                ModBlock = Entity as IMyTerminalBlock;
                Grid = ModBlock.CubeGrid;
                CubeBlock = (MyCubeBlock)ModBlock;
                Id = new NetSync<string>(this, TransferType.ServerToClient, string.Empty);
                GridList = new NetSync<List<string>>(this, TransferType.ServerToClient, new List<string>());
                StorageCooldown = new NetSync<DateTime>(this, TransferType.Both, DateTime.MinValue);
                SpawnCooldown = new NetSync<DateTime>(this, TransferType.Both, DateTime.MinValue);

                Id.ValueChanged += IdValueChanged;

                if (MyAPIGateway.Session.IsServer)
                {
                    Load();
                }
            }
            catch (Exception e) { MyLog.Default.Error($"[Grid Garage] Failed on Init\n{e.ToString()}"); }
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        private void IdValueChanged(string old, string value) 
        {
            if (IdLabel != null) 
            {
                IdLabel.Label = MyStringId.GetOrCompute($"Garage ID: {value}");
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

        public void RemoveGridFromList(string name)
        {
            try
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
            catch (Exception e)
            {
                MyLog.Default.Error($"[Grid Garage] Failed on RemoveGridFromList\n{e.ToString()}");
            }
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
            { MyLog.Default.Error($"[Grid Garage] Failed on CancelSpectorView\n{e.ToString()}"); }
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
                        Prefab fab = Core.LoadPrefab(gsb.Entity.EntityId, gsb.Id.Value, gsb.SelectedGrid);

                        if (fab != null)
                        {
                            gsb.GridsToPlace = fab.UnpackGrids();
                        }
                    }
                    else
                    {
                        PreviewGridData data = new PreviewGridData()
                        {
                            GarageEntityId = gsb.Entity.EntityId,
                            GarageGuid = gsb.Id.Value,
                            GridName = gsb.SelectedGrid
                        };

                        Network.SendCommand(Core.Command_Preview, null, MyAPIGateway.Utilities.SerializeToBinary(data));
                    }

                    gsb.GridSelectionMode = false;
                    gsb.StartSpectatorView();
                }
            }
            catch (Exception e)
            { MyLog.Default.Error($"[Grid Garage] Failed on SpawnSelectedGridAction\n{e.ToString()}"); }
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
                Vector3D gridToCamera = (Grid.WorldAABB.Center - MyAPIGateway.Session.Camera.WorldMatrix.Translation);
                if (gridToCamera.LengthSquared() > Core.Config.CameraOrbitDistance*Core.Config.CameraOrbitDistance)
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
            catch (Exception e) { MyLog.Default.Error($"[Grid Garage] Failed on UpdateBeforeSimulation\n{e.ToString()}"); }
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

                    if ((DateTime.Now - StorageCooldown.Value).TotalSeconds < Core.Config.StorageCooldown)
                    {
                        GridSelectErrorMessage = $"Storage is on Cooldown: {(Core.Config.StorageCooldown - ((DateTime.Now - StorageCooldown.Value).TotalMilliseconds / 1000)).ToString("n2")} seconds";
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
                                string name = Core.SavePrefab(SelectedGridEntity.EntityId, Id.Value);
                                GridList.Value.Add(name);
                                GridList.Push();
                            }
                        }
                        else
                        {
                            StoreGridData data = new StoreGridData() { GarageEntityId = Entity.EntityId, GarageGuid = Id.Value, TargetId = SelectedGridEntity.EntityId };

                            Network.SendCommand(Core.Command_Store, data: MyAPIGateway.Utilities.SerializeToBinary(data));
                        }

                        StorageCooldown.Value = DateTime.Now;
                    }
                }
            }
            catch (Exception e) { MyLog.Default.Error($"[Grid Garage] Failed on GridSelect\n{e.ToString()}"); ResetView(); }
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

                if ((DateTime.Now - SpawnCooldown.Value).TotalSeconds < Core.Config.SpawnCooldown) 
                {
                    MyAPIGateway.Utilities.ShowNotification($"Spawn is on Cooldown: {(Core.Config.SpawnCooldown - ((DateTime.Now - SpawnCooldown.Value).TotalMilliseconds / 1000)).ToString("n2")} seconds", 1, "Red");
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
                        Core.PlacePrefab(GridsToPlace, matrix.Translation, Tools.CreateFilename(Id.Value, SelectedGrid), playerId);
                        RemoveGridFromList(SelectedGrid);
                    }
                    else
                    {
                        PlaceGridData data = new PlaceGridData()
                        {
                            GarageGuid = Id.Value,
                            GridName = SelectedGrid,
                            NewOwner = playerId,
                            Position = new SerializableVector3D(matrix.Translation)
                        };

                        Network.SendCommand(Core.Command_Place, null, MyAPIGateway.Utilities.SerializeToBinary(data));
                    }

                    SpawnCooldown.Value = DateTime.Now;

                    ResetView();
                }
            }
            catch (Exception e) { MyLog.Default.Error($"[Grid Garage] Failed on GridPlacement\n{e.ToString()}"); ResetView(); }
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
            catch (Exception e) { MyLog.Default.Error($"[Grid Garage] Failed on CreateGridProjection\n{e.ToString()}"); return null; }
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
            catch (Exception e) { MyLog.Default.Error($"[Grid Garage] Failed on ShowHideBoundingBoxGridGroup\n{e.ToString()}"); }
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
            catch (Exception e) { MyLog.Default.Error($"[Grid Garage] Failed on GridHasNonFactionOwners\n{e.ToString()}"); return false; }
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

            IdLabel = Tools.CreateControlLabel("GridStorage_Label", $"Garage ID: {Id.Value}", ControlsVisible_Basic, ControlsEnabled_Basic);

            Tools.CreateControlButton("GridStorage_Store", "Store Grid", "Lets you select a grid to store", ControlsVisible_Basic, ControlsEnabled_Basic, StoreSelectedGridAction);

            Tools.CreateControlListbox("GridStorage_GridList", "Grid List", "Select a grid to spawn", 8, ControlsVisible_Basic, ControlsEnabled_Basic,
            (block, items, selected) =>
            {
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
            (block, items) =>
            {

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
            try
            {
                MyModStorageComponentBase storage = GetStorage(Entity);

                StorageDescription desc = new StorageDescription()
                {
                    Id = Id.Value,
                    GridNames = GridList.Value
                };

                string output = BitConverter.ToString(MyAPIGateway.Utilities.SerializeToBinary(desc));

                if (storage.ContainsKey(StorageGuid))
                {
                    storage[StorageGuid] = output;
                    MyLog.Default.Info($"[Grid Garage] Data Saved");
                }
                else
                {
                    MyLog.Default.Info($"[Grid Garage] {Entity.EntityId}: Saved new data");
                    storage.Add(new KeyValuePair<Guid, string>(StorageGuid, output));
                }
            }
            catch (Exception e)
            {
                MyLog.Default.Error($"[Grid Garage] Failed on Save \n{e.ToString()}");
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
                    StorageDescription data;
                    try
                    {
                        string hex = storage[StorageGuid].Replace("-", "");
                        byte[] raw = new byte[hex.Length / 2];
                        for (int i = 0; i < raw.Length; i++)
                        {
                            raw[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                        }

                        data = MyAPIGateway.Utilities.SerializeFromBinary<StorageDescription>(raw);
                    }
                    catch (Exception e)
                    {
                        MyLog.Default.Warning($"[Grid Garage] Failed to load block details. Checking depricated storage method");

                        string[] splitNames = storage[StorageGuid].Split(new string[] { "||//||" }, StringSplitOptions.RemoveEmptyEntries);
                        data = new StorageDescription()
                        {
                            Id = Id.Value,
                            GridNames = new List<string>(splitNames)
                        };
                    }

                    if (string.IsNullOrEmpty(data.Id) || string.IsNullOrWhiteSpace(data.Id))
                    {
                        Id.SetValue(Guid.NewGuid().ToString(), SyncType.None);
                        ConvertToNewSystem(data.GridNames);
                        MyLog.Default.Info($"[Grid Garage] Assigning new Garage Id: {Id.Value}");
                    }
                    else 
                    {
                        Id.SetValue(data.Id, SyncType.None);
                        MyLog.Default.Info($"[Grid Garage] Garage Id: {Id.Value}");
                    }

                    GridList.SetValue(data.GridNames, SyncType.None);

                    MyLog.Default.Info($"[Grid Garage] Data loaded: {GridList.Value.Count} grids");
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


        private void ConvertToNewSystem(List<string> gridnames)
        {
            FileIndex fileIndex = FileIndex.GetFileIndex();
            foreach (string name in gridnames) 
            {
                string oldFilenameFormat = Tools.CreateDepricatedFilename(Entity.EntityId, name);
                string newFilenameFormat = Tools.CreateFilename(Id.Value, name);
                try
                {
                    TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(oldFilenameFormat, typeof(Prefab));
                    string text = reader.ReadToEnd();
                    reader.Close();

                    TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(newFilenameFormat, typeof(Prefab));
                    writer.Write(text);
                    writer.Close();

                    // this can be removed when keen fixes their bug
                    writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(newFilenameFormat, typeof(Prefab));
                    writer.Write("");
                    writer.Close();

                    fileIndex.FileNames.Add(newFilenameFormat);

                    MyAPIGateway.Utilities.DeleteFileInWorldStorage(oldFilenameFormat, typeof(Prefab));
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(oldFilenameFormat, typeof(Prefab));

                } 
                catch (Exception e) 
                {
                    MyLog.Default.Error($"[Grid Garage] failed to convert grid {name} to the new storage method:\n{e.ToString()}");
                }
            }

            fileIndex.Save();
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