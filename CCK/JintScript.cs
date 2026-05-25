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
		public string key;
		public string value;
		public Object reference;
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
		/// Is a Json string that contains the exports of the Jint script.
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
		
		public void Awake()
			=> InvokeFunction("onAwake");

		public void Start()
			=> InvokeFunction("onStart");

		public void Update()
			=> InvokeFunction("onUpdate");

		public void FixedUpdate()
			=> InvokeFunction("onFixedUpdate");

		public void LateUpdate()
			=> InvokeFunction("onLateUpdate");

		public void OnDestroy()
			=> InvokeFunction("onDestroy");

		public void OnEnable()
			=> InvokeFunction("onEnable");

		public void OnDisable()
			=> InvokeFunction("onDisable");

		public void OnValidate()
			=> InvokeFunction("onValidate");

		public void Reset()
			=> InvokeFunction("onReset");

		public void OnAnimatorIK(int layerIndex)
			=> InvokeFunction("onAnimatorIK", layerIndex);

		public void OnAnimatorMove()
			=> InvokeFunction("onAnimatorMove");

		public void OnApplicationFocus(bool hasFocus)
			=> InvokeFunction("onApplicationFocus", hasFocus);

		public void OnApplicationPause(bool pauseStatus)
			=> InvokeFunction("onApplicationPause", pauseStatus);

		public void OnApplicationQuit()
			=> InvokeFunction("onApplicationQuit");

		public void OnAudioFilterRead(float[] data, int channels)
			=> InvokeFunction("onAudioFilterRead", data, channels);

		public void OnBecameInvisible()
			=> InvokeFunction("onBecameInvisible");

		public void OnBecameVisible()
			=> InvokeFunction("onBecameVisible");

		public void OnCollisionEnter(Collision collision)
			=> InvokeFunction("onCollisionEnter", collision);

		public void OnCollisionEnter2D(Collision2D collision)
			=> InvokeFunction("onCollisionEnter2D", collision);

		public void OnCollisionExit(Collision collision)
			=> InvokeFunction("onCollisionExit", collision);

		public void OnCollisionExit2D(Collision2D collision)
			=> InvokeFunction("onCollisionExit2D", collision);

		public void OnCollisionStay(Collision collision)
			=> InvokeFunction("onCollisionStay", collision);

		public void OnCollisionStay2D(Collision2D collision)
			=> InvokeFunction("onCollisionStay2D", collision);

		public void OnConnectedToServer()
			=> InvokeFunction("onConnectedToServer");

		public void OnControllerColliderHit(ControllerColliderHit hit)
			=> InvokeFunction("onControllerColliderHit", hit);

		public void OnGUI()
			=> InvokeFunction("onGUI");

		public void OnJointBreak(float breakForce)
			=> InvokeFunction("onJointBreak", breakForce);

		public void OnJointBreak2D(Joint2D brokenJoint)
			=> InvokeFunction("onJointBreak2D", brokenJoint);

		public void OnMouseDown()
			=> InvokeFunction("onMouseDown");

		public void OnMouseDrag()
			=> InvokeFunction("onMouseDrag");

		public void OnMouseEnter()
			=> InvokeFunction("onMouseEnter");

		public void OnMouseExit()
			=> InvokeFunction("onMouseExit");

		public void OnMouseOver()
			=> InvokeFunction("onMouseOver");

		public void OnMouseUp()
			=> InvokeFunction("onMouseUp");

		public void OnMouseUpAsButton()
			=> InvokeFunction("onMouseUpAsButton");

		public void OnParticleCollision(GameObject other)
			=> InvokeFunction("onParticleCollision", other);

		public void OnParticleSystemStopped()
			=> InvokeFunction("onParticleSystemStopped");

		public void OnParticleTrigger()
			=> InvokeFunction("onParticleTrigger");

		public void OnParticleUpdateJobScheduled()
			=> InvokeFunction("onParticleUpdateJobScheduled");

		public void OnPostRender()
			=> InvokeFunction("onPostRender");

		public void OnPreCull()
			=> InvokeFunction("onPreCull");

		public void OnPreRender()
			=> InvokeFunction("onPreRender");

		public void OnRenderImage(RenderTexture src, RenderTexture dest)
			=> InvokeFunction("onRenderImage", src, dest);

		public void OnRenderObject()
			=> InvokeFunction("onRenderObject");

		public void OnServerInitialized()
			=> InvokeFunction("onServerInitialized");

		public void OnTransformChildrenChanged()
			=> InvokeFunction("onTransformChildrenChanged");

		public void OnTransformParentChanged()
			=> InvokeFunction("onTransformParentChanged");

		public void OnTriggerEnter(Collider other)
			=> InvokeFunction("onTriggerEnter", other);

		public void OnTriggerEnter2D(Collider2D other)
			=> InvokeFunction("onTriggerEnter2D", other);

		public void OnTriggerExit(Collider other)
			=> InvokeFunction("onTriggerExit", other);

		public void OnTriggerExit2D(Collider2D other)
			=> InvokeFunction("onTriggerExit2D", other);

		public void OnTriggerStay(Collider other)
			=> InvokeFunction("onTriggerStay", other);

		public void OnTriggerStay2D(Collider2D other)
			=> InvokeFunction("onTriggerStay2D", other);

		public void OnWillRenderObject()
			=> InvokeFunction("onWillRenderObject");

		public void OnDrawGizmos()
			=> InvokeFunction("onDrawGizmos");

		public void OnDrawGizmosSelected()
			=> InvokeFunction("onDrawGizmosSelected");

		public string GetContent()
			=> asset ? asset.text : string.Empty;

		public Dictionary<string, object> GetExports()
			=> exports.ToDictionary(
				entry => entry.key,
				entry => {
					try {
						var type = Type.GetType(entry.type);
						if (type == null) return null;
						if (type == typeof(Object))
							return entry.reference;
						if (typeof(Object).IsAssignableFrom(type))
						return entry.reference; // runtime type is already the correct subclass
						if (!string.IsNullOrEmpty(entry.value))
							return JToken.Parse(entry.value).ToObject(type);
					} catch {
						// ignored
					}

					return null;
				}
			);

		#if UNITY_EDITOR
		public void SetExports(Dictionary<string, object> ex)
			=> exports = ex.Select(
					kv => new ExportEntry {
						key       = kv.Key,
						type      = kv.Value?.GetType().AssemblyQualifiedName ?? "null",
						value     = kv.Value is null or Object || typeof(Object).IsAssignableFrom(kv.Value.GetType()) ? null : JToken.FromObject(kv.Value).ToString(),
						reference = kv.Value is Object         || (kv.Value is not null && typeof(Object).IsAssignableFrom(kv.Value.GetType())) ? (Object)kv.Value : null
					}
				)
				.ToArray();
		#endif
	}
}