using System.Collections.Generic;

namespace Nox.Jint.Runtime {
	public class Manager {
		private List<JintBacking> _backings = new();

		private void InvokeBackingAdded(JintBacking backing) {
			Main.OnBackingAdded.Invoke(backing);
			Main.Instance.CoreAPI.EventAPI.Emit("jint_backing_added", backing);
		}

		private void InvokeBackingRemoved(JintBacking backing) {
			Main.OnBackingRemoved.Invoke(backing);
			Main.Instance.CoreAPI.EventAPI.Emit("jint_backing_removed", backing);
		}
	}
}