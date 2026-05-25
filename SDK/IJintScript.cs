using System.Collections.Generic;

namespace Nox.Jint {
	public interface IJintScript {
		public string GetContent();

		public Dictionary<string, object> GetExports();

		#if UNITY_EDITOR
		public void SetExports(Dictionary<string, object> exports);
		#endif
	}
}