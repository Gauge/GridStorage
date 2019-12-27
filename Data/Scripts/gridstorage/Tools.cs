using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace GridStorage
{
	public static class Tools
	{

		public static MyEntityUpdateEnum Add(this MyEntityUpdateEnum me, MyEntityUpdateEnum toAdd)
		{
			return me | toAdd;
		}

		public static MyEntityUpdateEnum Remove(this MyEntityUpdateEnum me, MyEntityUpdateEnum toRemove)
		{
			return me & ~toRemove;
		}

		public static string CreateFilename(string garageGuid, string gridName) 
		{
			return $"{garageGuid}_{gridName}";
		}

		public static string CreateDepricatedFilename(long garageBlockId, string gridName) 
		{
			return $"{garageBlockId}_{gridName}";
		}

		public static MyObjectBuilder_CubeGrid CleanGrid(IMyCubeGrid ent)
		{

			MyObjectBuilder_CubeGrid grid = (MyObjectBuilder_CubeGrid)ent.GetObjectBuilder();

			grid.EntityId = 0;
			grid.AngularVelocity = new SerializableVector3();
			grid.LinearVelocity = new SerializableVector3();
			grid.PositionAndOrientation = new MyPositionAndOrientation(new Vector3D(), new Vector3(0, 0, 1), new Vector3(0, 1, 0));
			grid.XMirroxPlane = null;
			grid.YMirroxPlane = null;
			grid.ZMirroxPlane = null;
			grid.IsStatic = false;
			grid.CreatePhysics = true;
			grid.IsRespawnGrid = false;

			foreach (var block in grid.CubeBlocks)
			{
				block.EntityId = 0;
			}

			return grid;
		}

		public static BoundingBoxD CalculateBoundingBox(List<IMyCubeGrid> grids)
		{
			BoundingBoxD box = new BoundingBoxD();

			foreach (IMyCubeGrid grid in grids)
			{
				Vector3D gMin = grid.WorldAABB.Min;
				Vector3D gMax = grid.WorldAABB.Max;

				if (gMin.X < box.Min.X)
				{
					box.Min.X = gMin.X;
				}

				if (gMin.Y < box.Min.Y)
				{
					box.Min.Y = gMin.Y;
				}

				if (gMin.Z < box.Min.Z)
				{
					box.Min.Z = gMin.Z;
				}

				if (gMax.X > box.Max.X)
				{
					box.Max.X = gMax.X;
				}

				if (gMax.Z > box.Max.Z)
				{
					box.Max.Z = gMax.Z;
				}

				if (gMax.Z > box.Max.Z)
				{
					box.Max.Z = gMax.Z;
				}
			}

			return box;
		}

		private static IMyTerminalControl ControlIdExists(string id)
		{
			List<IMyTerminalControl> controls = new List<IMyTerminalControl>();
			MyAPIGateway.TerminalControls.GetControls<IMyUpgradeModule>(out controls);

			IMyTerminalControl control = controls.Find(x => x.Id == id);

			return control;
		}

		private static IMyTerminalAction ActionIdExists(string id)
		{
			List<IMyTerminalAction> actions = new List<IMyTerminalAction>();
			MyAPIGateway.TerminalControls.GetActions<IMyUpgradeModule>(out actions);

			IMyTerminalAction action = actions.Find(x => x.Id == id);

			return action;
		}


		public static IMyTerminalControlLabel CreateControlLabel(string id, string labelText, Func<IMyTerminalBlock, bool> visible, Func<IMyTerminalBlock, bool> enabled)
		{
			if (ControlIdExists(id) != null)
				return null;

			IMyTerminalControlLabel label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyUpgradeModule>(id);
			label.Enabled = enabled;
			label.Visible = visible;
			label.SupportsMultipleBlocks = false;

			label.Label = MyStringId.GetOrCompute(labelText);

			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(label);
			return label;
		}

		public static void CreateControlButton(string id, string title, string tooltip, Func<IMyTerminalBlock, bool> visible, Func<IMyTerminalBlock, bool> enabled, Action<IMyTerminalBlock> action)
		{
			if (ControlIdExists(id) != null)
				return;

			IMyTerminalControlButton button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyUpgradeModule>(id);
			button.Enabled = enabled;
			button.Visible = visible;
			button.SupportsMultipleBlocks = false;

			if (title != null)
			{
				button.Title = MyStringId.GetOrCompute(title);
			}
			if (tooltip != null)
			{
				button.Tooltip = MyStringId.GetOrCompute(tooltip);
			}

			button.Action = action;

			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(button);
		}

		public static void CreateControlCheckbox(string id, string title, string tooltip, Func<IMyTerminalBlock, bool> visible, Func<IMyTerminalBlock, bool> enabled, Func<IMyTerminalBlock, bool> getter, Action<IMyTerminalBlock, bool> setter)
		{
			if (ControlIdExists(id) != null)
				return;

			IMyTerminalControlCheckbox checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyUpgradeModule>(id);
			checkbox.Enabled = enabled;
			checkbox.Visible = visible;
			checkbox.SupportsMultipleBlocks = false;

			if (title != null)
			{
				checkbox.Title = MyStringId.GetOrCompute(title);
			}
			if (tooltip != null)
			{
				checkbox.Tooltip = MyStringId.GetOrCompute(tooltip);
			}

			checkbox.Getter = getter;
			checkbox.Setter = setter;

			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(checkbox);
		}

		public static void CreateControlOnOff(string id, string title, string tooltip, string OnText, string OffText, Func<IMyTerminalBlock, bool> visible, Func<IMyTerminalBlock, bool> enabled, Func<IMyTerminalBlock, bool> getter, Action<IMyTerminalBlock, bool> setter)
		{
			if (ControlIdExists(id) != null)
				return;

			IMyTerminalControlOnOffSwitch mode = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyUpgradeModule>(id);
			mode.Enabled = enabled;
			mode.Visible = visible;
			mode.SupportsMultipleBlocks = false;

			if (title != null)
			{
				mode.Title = MyStringId.GetOrCompute(title);
			}
			if (tooltip != null)
			{
				mode.Tooltip = MyStringId.GetOrCompute(tooltip);
			}

			mode.OnText = MyStringId.GetOrCompute(OnText);
			mode.OffText = MyStringId.GetOrCompute(OffText);

			mode.Getter = getter;
			mode.Setter = setter;

			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(mode);
		}

		public static void CreateControlTextbox(string id, string title, string tooltip, Func<IMyTerminalBlock, bool> visible, Func<IMyTerminalBlock, bool> enabled, Func<IMyTerminalBlock, StringBuilder> getter, Action<IMyTerminalBlock, StringBuilder> setter)
		{
			if (ControlIdExists(id) != null)
				return;

			IMyTerminalControlTextbox textbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyUpgradeModule>(id);
			textbox.Enabled = enabled;
			textbox.Visible = visible;
			textbox.SupportsMultipleBlocks = false;

			if (title != null)
			{
				textbox.Title = MyStringId.GetOrCompute(title);
			}
			if (tooltip != null)
			{
				textbox.Tooltip = MyStringId.GetOrCompute(tooltip);
			}

			textbox.Getter = getter;

			if (setter != null)
			{
				textbox.Setter = setter;
			}

			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(textbox);
		}

		public static void CreateControlListbox(string id, string title, string tooltip, int rowCount, Func<IMyTerminalBlock, bool> visible, Func<IMyTerminalBlock, bool> enabled, Action<IMyTerminalBlock, List<MyTerminalControlListBoxItem>, List<MyTerminalControlListBoxItem>> populate, Action<IMyTerminalBlock, List<MyTerminalControlListBoxItem>> selectItem)
		{
			if (ControlIdExists(id) != null)
				return;

			IMyTerminalControlListbox listbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyUpgradeModule>(id);

			listbox.Enabled = enabled;
			listbox.Visible = visible;
			listbox.Multiselect = false;
			listbox.SupportsMultipleBlocks = false;

			if (title != null)
			{
				listbox.Title = MyStringId.GetOrCompute(title);
			}
			if (tooltip != null)
			{
				listbox.Tooltip = MyStringId.GetOrCompute(tooltip);
			}

			listbox.VisibleRowsCount = rowCount;
			listbox.ListContent = populate;
			listbox.ItemSelected = selectItem;

			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(listbox);
		}

		public static IMyTerminalControlSlider CreateControlSilder(string id, string title, string tooltip, float min, float max, Func<IMyTerminalBlock, bool> visible, Func<IMyTerminalBlock, bool> enabled, Func<IMyTerminalBlock, float> getter, Action<IMyTerminalBlock, float> setter, Action<IMyTerminalBlock, StringBuilder> writer)
		{
			IMyTerminalControlSlider slider = ControlIdExists(id) as IMyTerminalControlSlider;

			if (slider != null)
			{
				return slider;
			}

			slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyUpgradeModule>(id);

			slider.Enabled = enabled;
			slider.Visible = visible;
			slider.SupportsMultipleBlocks = false;

			if (title != null)
			{
				slider.Title = MyStringId.GetOrCompute(title);
			}
			if (tooltip != null)
			{
				slider.Tooltip = MyStringId.GetOrCompute(tooltip);
			}

			slider.SetLimits(min, max);

			slider.Writer = writer;
			slider.Getter = getter;
			slider.Setter = setter;

			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(slider);

			return slider;
		}

		public static void CreateControlSeperator(string id, Func<IMyTerminalBlock, bool> visible)
		{
			if (ControlIdExists(id) != null)
				return;

			IMyTerminalControlSeparator seperator = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyUpgradeModule>(id);
			seperator.Visible = visible;
			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(seperator);
		}

		public static void CreateActionButton(string id, string name, Func<IMyTerminalBlock, bool> enabled, Action<IMyTerminalBlock, StringBuilder> writer, Action<IMyTerminalBlock> action)
		{
			if (ActionIdExists(id) != null)
				return;

			IMyTerminalAction button = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>(id);
			button.Enabled = enabled;

			button.Name = new StringBuilder(name);

			button.Writer = writer;
			button.Action = action;

			MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(button);
		}

	}
}
