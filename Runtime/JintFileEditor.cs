#if UNITY_EDITOR
using Nox.CCK.Jint;
using UnityEditor;
using UnityEngine;

namespace Nox.Jint.Runtime {
	[CustomEditor(typeof(JintFile))]
	public class JintFileEditor : Editor {
		public override void OnInspectorGUI() {
			var icon = Resources.Load<Texture2D>("jint-icon");
			if (icon) EditorGUIUtility.SetIconForObject(target, icon);
			var file = (JintFile)target;
			EditorGUILayout.TextArea(file.text, GUILayout.MinHeight(200));
		}
	}
}
#endif