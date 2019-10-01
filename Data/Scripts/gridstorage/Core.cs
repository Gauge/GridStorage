using SENetworkAPI;
using VRage.Game;
using VRage.Game.Components;

namespace GridStorage
{
	public enum NetworkCommands { BlockPropertiesUpdate };

	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public class Core : MySessionComponentBase
	{
		public static NetworkAPI Network => NetworkAPI.Instance;

		private const ushort ModId = 65489;
		private const string ModName = "Storage Block";

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			if (!NetworkAPI.IsInitialized)
			{
				NetworkAPI.Init(ModId, ModName);
			}
		}

		protected override void UnloadData()
		{
			NetworkAPI.Dispose();
		}
	}
}
