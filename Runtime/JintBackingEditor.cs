#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nox.Jint.Runtime {
	[CustomEditor(typeof(JintBacking))]
	public class JintBackingEditor : Editor {
		public override void OnInspectorGUI() {
			base.OnInspectorGUI();
			
			EditorGUILayout.Space();
			
			var backing = (JintBacking)target;
			foreach (var s in backing.GetProperties())
				EditorGUILayout.LabelField(s, backing.GetProperty(s)?.ToString() ?? "null");

			EditorGUILayout.Space();

			var logs = backing.Logger.Logs.ToArray().Reverse();
			foreach (var log in logs) {
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField(log.Time.ToString("HH:mm:ss.fff"), GUILayout.Width(100));
				EditorGUILayout.LabelField(log.Type.ToString(), GUILayout.Width(50));
				EditorGUILayout.LabelField(log.Message);
				EditorGUILayout.EndHorizontal();
			}
		}
	}
}
#endif