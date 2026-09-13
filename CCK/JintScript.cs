using System;
using System.Collections.Generic;
using System.Linq;
using Nox.Jint;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
using Object = UnityEngine.Object;

namespace Nox.CCK.Jint {
	[Serializable]
	public class ExportEntry {
		/// <summary>The export's key/name as seen from the script.</summary>
		public string key;

		/// <summary>
		/// JSON payload for non-Object values (raw text for strings, JSON for
		/// value types and for arrays of value types).
		/// </summary>
		public string value;

		/// <summary>
		/// Object reference(s). Length 1 for a scalar UnityEngine.Object-derived
		/// type, length N for an array of such a type.
		/// </summary>
		public Object[] references;

		/// <summary>AssemblyQualifiedName of the exported type (may itself be an array type).</summary>
		public string type;
	}

	[Serializable, DisallowMultipleComponent]
	public class JintScript : MonoBehaviour, IJintScript {
		/// <summary>
		/// The Jint script asset that this script is based on.
		/// Is a .js file.
		/// </summary>
		public JintFile asset;

		/// <summary>
		/// The exports of the Jint script, made available to the script by key.
		/// </summary>
		[SerializeField]
		private ExportEntry[] exports = Array.Empty<ExportEntry>();

		private IJintBacking _backing;

		// ReSharper disable Unity.PerformanceAnalysis
		public IJintBacking GetBacking()
			=> _backing ??= GetComponent<IJintBacking>();

		public void InvokeFunction(string functionName, params object[] args)
			=> GetBacking()?.Invoke(functionName, args);

		public object CallFunction(string functionName, params object[] args)
			=> GetBacking()?.Call(functionName, args);

		public T CallFunction<T>(string functionName, params object[] args)
			=> GetBacking() != null
				? GetBacking().Call<T>(functionName, args)
				: default;
		
		public void InvokeFunction(string functionName)
			=> InvokeFunction(functionName, Array.Empty<object>());
		
		public object CallFunction(string functionName)
			=> CallFunction(functionName, Array.Empty<object>());
		
		public T CallFunction<T>(string functionName)
			=> CallFunction<T>(functionName, Array.Empty<object>());
		
		public void Awake() => InvokeFunction("onAwake");
		public void Start() => InvokeFunction("onStart");
		public void Update() => InvokeFunction("onUpdate");
		public void FixedUpdate() => InvokeFunction("onFixedUpdate");
		public void LateUpdate() => InvokeFunction("onLateUpdate");
		public void OnDestroy() => InvokeFunction("onDestroy");
		public void OnEnable() => InvokeFunction("onEnable");
		public void OnDisable() => InvokeFunction("onDisable");
		public void OnValidate() => InvokeFunction("onValidate");
		public void Reset() => InvokeFunction("onReset");

		public void OnAnimatorIK(int layerIndex) => InvokeFunction("onAnimatorIK", layerIndex);
		public void OnAnimatorMove() => InvokeFunction("onAnimatorMove");
		public void OnApplicationFocus(bool hasFocus) => InvokeFunction("onApplicationFocus", hasFocus);
		public void OnApplicationPause(bool pauseStatus) => InvokeFunction("onApplicationPause", pauseStatus);
		public void OnApplicationQuit() => InvokeFunction("onApplicationQuit");
		public void OnAudioFilterRead(float[] data, int channels) => InvokeFunction("onAudioFilterRead", data, channels);
		public void OnBecameInvisible() => InvokeFunction("onBecameInvisible");
		public void OnBecameVisible() => InvokeFunction("onBecameVisible");
		public void OnCollisionEnter(Collision collision) => InvokeFunction("onCollisionEnter", collision);
		public void OnCollisionEnter2D(Collision2D collision) => InvokeFunction("onCollisionEnter2D", collision);
		public void OnCollisionExit(Collision collision) => InvokeFunction("onCollisionExit", collision);
		public void OnCollisionExit2D(Collision2D collision) => InvokeFunction("onCollisionExit2D", collision);
		public void OnCollisionStay(Collision collision) => InvokeFunction("onCollisionStay", collision);
		public void OnCollisionStay2D(Collision2D collision) => InvokeFunction("onCollisionStay2D", collision);
		public void OnConnectedToServer() => InvokeFunction("onConnectedToServer");
		public void OnControllerColliderHit(ControllerColliderHit hit) => InvokeFunction("onControllerColliderHit", hit);
		public void OnGUI() => InvokeFunction("onGUI");
		public void OnJointBreak(float breakForce) => InvokeFunction("onJointBreak", breakForce);
		public void OnJointBreak2D(Joint2D brokenJoint) => InvokeFunction("onJointBreak2D", brokenJoint);
		public void OnMouseDown() => InvokeFunction("onMouseDown");
		public void OnMouseDrag() => InvokeFunction("onMouseDrag");
		public void OnMouseEnter() => InvokeFunction("onMouseEnter");
		public void OnMouseExit() => InvokeFunction("onMouseExit");
		public void OnMouseOver() => InvokeFunction("onMouseOver");
		public void OnMouseUp() => InvokeFunction("onMouseUp");
		public void OnMouseUpAsButton() => InvokeFunction("onMouseUpAsButton");
		public void OnParticleCollision(GameObject other) => InvokeFunction("onParticleCollision", other);
		public void OnParticleSystemStopped() => InvokeFunction("onParticleSystemStopped");
		public void OnParticleTrigger() => InvokeFunction("onParticleTrigger");
		public void OnParticleUpdateJobScheduled() => InvokeFunction("onParticleUpdateJobScheduled");
		public void OnPostRender() => InvokeFunction("onPostRender");
		public void OnPreCull() => InvokeFunction("onPreCull");
		public void OnPreRender() => InvokeFunction("onPreRender");
		public void OnRenderImage(RenderTexture src, RenderTexture dest) => InvokeFunction("onRenderImage", src, dest);
		public void OnRenderObject() => InvokeFunction("onRenderObject");
		public void OnServerInitialized() => InvokeFunction("onServerInitialized");
		public void OnTransformChildrenChanged() => InvokeFunction("onTransformChildrenChanged");
		public void OnTransformParentChanged() => InvokeFunction("onTransformParentChanged");
		public void OnTriggerEnter(Collider other) => InvokeFunction("onTriggerEnter", other);
		public void OnTriggerEnter2D(Collider2D other) => InvokeFunction("onTriggerEnter2D", other);
		public void OnTriggerExit(Collider other) => InvokeFunction("onTriggerExit", other);
		public void OnTriggerExit2D(Collider2D other) => InvokeFunction("onTriggerExit2D", other);
		public void OnTriggerStay(Collider other) => InvokeFunction("onTriggerStay", other);
		public void OnTriggerStay2D(Collider2D other) => InvokeFunction("onTriggerStay2D", other);
		public void OnWillRenderObject() => InvokeFunction("onWillRenderObject");
		public void OnDrawGizmos() => InvokeFunction("onDrawGizmos");
		public void OnDrawGizmosSelected() => InvokeFunction("onDrawGizmosSelected");

		public string GetContent()
			=> asset ? asset.text : string.Empty;

		public Dictionary<string, object> GetExports() {
			var result = new Dictionary<string, object>();

			foreach (var entry in exports) {
				var (success, value) = TryDeserialize(entry);
				if (success) result[entry.key] = value;
			}

			return result;
		}

#if UNITY_EDITOR
		public void SetExports(Dictionary<string, object> exports) {
			this.exports = exports
				.Select(e => Serialize(e.Key, e.Value?.GetType() ?? typeof(object), e.Value))
				.ToArray();
		}
#endif

		private (bool success, object value) TryDeserialize(ExportEntry entry) {
			try {
				var type = Type.GetType(entry.type ?? string.Empty);
				if (type == null) return (true, null);

				if (type.IsArray) {
					var elementType = type.GetElementType();
					if (elementType == null) return (false, null);

					if (typeof(Object).IsAssignableFrom(elementType)) {
						var refs = entry.references ?? Array.Empty<Object>();
						var arr  = Array.CreateInstance(elementType, refs.Length);
						for (var i = 0; i < refs.Length; i++) arr.SetValue(refs[i], i);
						return (true, arr);
					}

					if (string.IsNullOrEmpty(entry.value))
						return (true, Array.CreateInstance(elementType, 0));

					return (true, JToken.Parse(entry.value).ToObject(type));
				}

				if (typeof(Object).IsAssignableFrom(type))
					return (true, entry.references is { Length: > 0 } ? entry.references[0] : null);

				if (type == typeof(string))
					return (true, entry.value);

				if (!string.IsNullOrEmpty(entry.value))
					return (true, JToken.Parse(entry.value).ToObject(type));

				return (true, type.IsValueType ? Activator.CreateInstance(type) : null);
			}
			catch {
				Logger.LogWarning(
					$"'{entry.value}' (key: {entry.key}) is not convertable to {entry.type}.",
					tag: nameof(JintScript),
					context: this
				);
				return (false, null);
			}
		}

#if UNITY_EDITOR
		public List<(string Key, Type Type, object Value)> GetExportsDetailed() {
			var list = new List<(string, Type, object)>();

			foreach (var entry in exports) {
				var type = Type.GetType(entry.type ?? string.Empty) ?? typeof(string);
				var (_, value) = TryDeserialize(entry);
				list.Add((entry.key, type, value));
			}

			return list;
		}

		public void SetExportsDetailed(IEnumerable<(string Key, Type Type, object Value)> ex)
			=> exports = ex.Select(e => Serialize(e.Key, e.Type, e.Value)).ToArray();

		private static ExportEntry Serialize(string key, Type type, object value) {
			var entry = new ExportEntry { key = key, type = type?.AssemblyQualifiedName ?? "null" };
			if (type == null) return entry;

			if (type.IsArray) {
				var elementType = type.GetElementType() ?? typeof(object);
				var array       = value as Array ?? Array.CreateInstance(elementType, 0);

				if (typeof(Object).IsAssignableFrom(elementType))
					entry.references = array.Cast<Object>().ToArray();
				else
					entry.value = JToken.FromObject(array).ToString();

				return entry;
			}

			if (typeof(Object).IsAssignableFrom(type)) {
				entry.references = value is Object obj ? new[] { obj } : Array.Empty<Object>();
				return entry;
			}

			if (type == typeof(string)) {
				entry.value = value as string ?? string.Empty;
				return entry;
			}

			entry.value = JToken.FromObject(value ?? Activator.CreateInstance(type)).ToString();
			return entry;
		}
#endif
	}
}