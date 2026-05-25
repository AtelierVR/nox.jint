using System;
using System.Collections.Generic;
using System.Linq;
using Jint;
using Jint.Native.Object;
using Jint.Runtime.Interop;
using Nox.CCK.Build;
using Nox.CCK.Jint;
using Nox.CCK.Network;
using Nox.CCK.Utils;
using Nox.Jint;
using UnityEngine;
using Engine = Jint.Engine;
using LogType = UnityEngine.LogType;
using Transform = UnityEngine.Transform;
using NoxLogger = Nox.CCK.Utils.Logger;

namespace Nox.Jint.Runtime {
	[RequireComponent(typeof(JintScript))]
	public class JintBacking : MonoBehaviour, IJintBacking, ICompilable {
		public Engine Engine;
		public JintScript script;
		public ObjectInstance ExecutionContext;
		public Logger Logger;

		public void Compile()
			=> DestroyImmediate(this);

		public string[] GetProperties()
			=> ExecutionContext != null
				? ExecutionContext.GetOwnPropertyKeys()
					.Select(kvp => kvp.ToString())
					.ToArray()
				: Array.Empty<string>();

		public object GetProperty(string propertyName) {
			if (ExecutionContext == null)
				return null;
			try {
				var prop = ExecutionContext.Get(propertyName);
				return prop.IsUndefined() ? null : prop.ToObject();
			} catch (Exception e) {
				NoxLogger.LogError($"Error getting property {propertyName}: {e.Message}", this);
				return null;
			}
		}

		private void OnValidate() {
			script ??= GetComponent<JintScript>();
			if (Engine == null)
				Prepare();
		}

		// ReSharper disable Unity.PerformanceAnalysis
		public void Prepare() {
			script ??= GetComponent<JintScript>();
			Logger ??= new Logger();

			NoxLogger.Log("Prepare");
			Engine = new Engine(
				ctx => {
					ctx.LimitMemory(4_194_304);
					ctx.LimitRecursion(1024);
				}
			);

			Engine.SetValue("GameObject", TypeReference.CreateTypeReference(Engine, typeof(GameObject)));
			Engine.SetValue("Vector3", TypeReference.CreateTypeReference(Engine, typeof(Vector3)));
			Engine.SetValue("Vector2", TypeReference.CreateTypeReference(Engine, typeof(Vector2)));
			Engine.SetValue("Quaternion", TypeReference.CreateTypeReference(Engine, typeof(Quaternion)));
			Engine.SetValue("Transform", TypeReference.CreateTypeReference(Engine, typeof(Transform)));

			// import json of Script.exports
			try {
				Engine.SetValue("exports", ObjectWrapper.Create(Engine, script.GetExports(), typeof(Dictionary<string, object>)));
			} catch (Exception e) {
				NoxLogger.LogError($"Error parsing exports: {e.Message}", this);
			}

			Engine.Modules.Add(
				"console", builder => builder
					.ExportFunction("log", objets => Logger.Log(LogType.Log, string.Join(" ", objets.Select(e => e.ToString()))))
					.ExportFunction("warn", objets => Logger.Log(LogType.Warning, string.Join(" ", objets.Select(e => e.ToString()))))
					.ExportFunction("error", objets => Logger.Log(LogType.Error, string.Join(" ", objets.Select(e => e.ToString()))))
			);

			Engine.Modules.Add(
				"behaviour", builder => builder
					.ExportObject("transform", ObjectWrapper.Create(Engine, transform, typeof(Transform)))
					.ExportObject("gameObject", ObjectWrapper.Create(Engine, gameObject, typeof(GameObject)))
			);

			// add Buffer of nodejs
			Engine.Modules.Add("buffer", builder => builder.ExportType<NodeBuffer>("Buffer"));
			Engine.SetValue("Buffer", TypeReference.CreateTypeReference(Engine, typeof(NodeBufferImpl)));

			// add Hash of nodejs
			Engine.Modules.Add("hash", builder => builder.ExportType<NodeHash>("Hash"));
			Engine.SetValue("Hash", TypeReference.CreateTypeReference(Engine, typeof(NodeHashImpl)));

			try {
				NoxLogger.LogDebug($"script: {script}");
				NoxLogger.LogDebug($"script.asset: {script.asset}");
				NoxLogger.LogDebug($"script.asset.text: {script.asset.text}");

				Engine.Modules.Add("__main__", script.asset.text);
				ExecutionContext = Engine.Modules.Import("__main__");
				Invoke("onPrepare");
			} catch (Exception e) {
				NoxLogger.LogError($"Error executing onPrepare function: {e.Message}", this);
				NoxLogger.LogError(e, this);
				Engine           = null;
				ExecutionContext = null;
			}
		}

		private void OnDestroy() {
			if (Engine == null)
				return;
			try {
				Invoke("onDestroy");
			} catch (Exception e) {
				NoxLogger.LogError($"Error executing onDestroy function: {e.Message}", this);
			}

			Engine.Dispose();
			Engine           = null;
			ExecutionContext = null;
		}

		public void Invoke(string methodName, params object[] args) {
			if (Engine == null)
				Prepare();
			if (Engine == null)
				return;
			try {
				var method = ExecutionContext.Get(methodName);
				if (method.IsUndefined())
					return;
				Engine.Invoke(method, args);
			} catch (Exception e) {
				NoxLogger.LogError($"Error executing {methodName} function: {e.Message}", this);
			}
		}

		public object Call(string functionName, object[] args) {
			if (Engine == null)
				Prepare();
			if (Engine == null)
				return null;
			try {
				var method = ExecutionContext.Get(functionName);
				return !method.IsUndefined()
					? Engine.Invoke(method, args)
					: null;
			} catch (Exception e) {
				NoxLogger.LogError($"Error executing {functionName} function: {e.Message}", this);
				return null;
			}
		}

		public T Call<T>(string functionName, object[] args) {
			if (Engine == null)
				Prepare();
			if (Engine == null)
				return default;
			try {
				var method = ExecutionContext.Get(functionName);
				if (method.IsUndefined())
					return default;
				var result = Engine.Invoke(method, args);
				return (T)result.ToObject();
			} catch (Exception e) {
				NoxLogger.LogError($"Error executing {functionName} function: {e.Message}", this);
				return default;
			}
		}
	}
	public interface NodeBuffer {
		byte[] from(string data);
		byte[] from(string data, string encoding);
		string toString();
		string toString(string encoding);
	}

	public interface NodeHash {
		int crc32(byte[]  data);
		int crc32(string  data);
		long crc64(byte[] data);
		long crc64(string data);
	}

	public static class NodeBufferImpl {
		public static byte[] from(string data)
			=> from(data, "utf8");

		public static byte[] from(string data, string encoding) {
			return encoding.ToLower() switch {
				"utf8"    => System.Text.Encoding.UTF8.GetBytes(data),
				"ascii"   => System.Text.Encoding.ASCII.GetBytes(data),
				"unicode" => System.Text.Encoding.Unicode.GetBytes(data),
				"base64"  => Convert.FromBase64String(data),
				"hex" => Enumerable.Range(0, data.Length / 2)
					.Select(x => Convert.ToByte(data.Substring(x * 2, 2), 16))
					.ToArray(),
				_ => throw new NotSupportedException($"Encoding '{encoding}' is not supported"),
			};
		}

		public static string toString(byte[] buffer)
			=> toString(buffer, "utf8");

		public static string toString(byte[] buffer, string encoding) {
			return encoding.ToLower() switch {
				"utf8"    => System.Text.Encoding.UTF8.GetString(buffer),
				"ascii"   => System.Text.Encoding.ASCII.GetString(buffer),
				"unicode" => System.Text.Encoding.Unicode.GetString(buffer),
				"base64"  => Convert.ToBase64String(buffer),
				"hex"     => BitConverter.ToString(buffer).Replace("-", "").ToLower(),
				_         => throw new NotSupportedException($"Encoding '{encoding}' is not supported"),
			};
		}
	}

	public static class NodeHashImpl {
		public static int crc32(byte[] data)
			=> Hash.CRC32(data);

		public static int crc32(string data)
			=> Hash.CRC32(data);

		public static long crc64(byte[] data)
			=> Hash.CRC64(data);

		public static long crc64(string data)
			=> Hash.CRC64(data);
	}
}