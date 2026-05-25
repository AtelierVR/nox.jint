using Nox.Jint;
using UnityEngine;

namespace Nox.CCK.Jint {
	[CreateAssetMenu(fileName = "JintFile", menuName = "Nox/Jint File", order = 1)]
	public class JintFile : ScriptableObject {
		[SerializeField]
		public string text;

		public string GetText()
			=> text;

		public override string ToString()
			=> $"{GetType().Name}[]";
	}
}