using System.IO;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Initializers;
using Nox.CCK.Utils;
using Nox.Jint;
using Nox.Scripting;
using UnityEngine.Events;

namespace Nox.Jint.Runtime {
	public class Main : IMainModInitializer, IJintAPI {
		public        Manager     Manager;
		public static Main        Instance;
		public        IModCoreAPI CoreAPI;

		public static readonly UnityEvent<JintBacking> OnBackingAdded   = new();
		public static readonly UnityEvent<JintBacking> OnBackingRemoved = new();

		/// <summary>Scripting registry, resolved lazily via the mod loader.</summary>
		public static IScriptingAPI ScriptingAPI
			=> Instance?.CoreAPI.ModAPI.GetMod("scripting")?.GetInstance<IScriptingAPI>();

		public string GetModulesPath() {
			var folder = Path.Combine(Constants.ConfigPath, "jint_modules");
			if (!Directory.Exists(folder))
				Directory.CreateDirectory(folder);
			return folder;
		}

		public void OnInitializeMain(IMainModCoreAPI api) {
			CoreAPI  = api;
			Instance = this;
			Manager  = new Manager();
		}

		public void OnDisposeMain() {
			Instance = null;
		}
	}
}