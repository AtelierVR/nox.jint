using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using Nox.CCK.Scripting;
using Nox.CCK.Utils;
using Nox.Jint;
using Nox.Scripting;
using JintEngine = Jint.Engine;
using NoxLogger = Nox.CCK.Utils.Logger;

namespace api.nox.jint {
	/// <summary>
	/// Adapts <see cref="IScriptingModuleDefinition"/> instances from the
	/// scripting registry into Jint engine modules.
	///
	/// Call <see cref="BindAllModules"/> once per engine to wire up every
	/// currently registered module, or <see cref="BindModule"/> to add a
	/// single module on-demand.
	/// </summary>
	public static class JintModuleAdapter {

		public static bool ModuleMatchesTags(IScriptingModuleDefinition module, IReadOnlyList<string> backendTags) {
			if (module.Tags.Count == 0 || backendTags == null || backendTags.Count == 0)
				return true;
			return module.Tags.Any(backendTags.Contains);
		}

		// ── Helpers ──────────────────────────────────────────────────────────

		/// <summary>
		/// Depth-limited formatter for script values — equivalent to Node.js
		/// <c>util.inspect(value, { depth })</c>.
		/// Avoids calling <c>ObjectInstance.ToString()</c> in C#, which internally
		/// calls <c>ToObject(ObjectTraverseStack)</c> and recurses infinitely into
		/// self-typed properties (e.g. <c>Vector3.normalized → Vector3 → …</c>).
		/// </summary>
		public static string FormatArg(object arg, int maxDepth = 3, int currentDepth = 0) {
			switch (arg) {
				case null:
					return "null";
				case bool b:
					return b ? "true" : "false";
				case string s:
					return s;
				case double d:
					return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
				case float f:
					return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
				case int i:
					return i.ToString();
				case object[] arr: {
					if (currentDepth >= maxDepth)
						return "[ ... ]";
					var items = arr.Take(20)
						.Select(x => FormatArg(x, maxDepth, currentDepth + 1))
						.ToList();
					if (arr.Length > 20)
						items.Add("...");
					return "[ " + string.Join(", ", items) + " ]";
				}
				case ObjectInstance jsObj: {
					if (currentDepth >= maxDepth)
						return "{ ... }";
					var parts = new List<string>();
					foreach (var kv in jsObj.GetOwnProperties()) {
						if (parts.Count >= 20) {
							parts.Add("...");
							break;
						}
						var    key = kv.Key.ToString();
						string valStr;
						try {
							if (kv.Value.IsDataDescriptor()) {
								// Data property — value already computed, safe to read
								valStr = FormatArg(JintTypeAdapter.FromJsValue(kv.Value.Value), maxDepth, currentDepth + 1);
							} else {
								// Accessor — never invoke the getter during inspection
								valStr = $"[getter {key}]";
							}
						} catch { valStr = "..."; }
						parts.Add(key + ": " + valStr);
					}
					return parts.Count == 0 ? "{}" : "{ " + string.Join(", ", parts) + " }";
				}
				default:
					return arg.ToString();
			}
		}
	}
}