#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nox.Jint.Runtime {
	/// <summary>
	/// Searchable popup used to pick a base export type: a primitive, or any
	/// type derived from UnityEngine.Object found in the loaded assemblies.
	/// Array-ness is handled separately by the caller (see the "[]" toggle
	/// next to the type button in JintScriptEditor) so this window only ever
	/// deals with scalar types.
	/// </summary>
	public class TypeSearchWindow : AdvancedDropdown {
		private sealed class TypeItem : AdvancedDropdownItem {
			public readonly Type Type;

			public TypeItem(string name, Type type) : base(name)
				=> Type = type;
		}

		private static readonly Type[] PrimitiveTypes = {
			typeof(string),
			typeof(int),
			typeof(float),
			typeof(bool),
		};

		private readonly Action<Type> _onTypePicked;

		public TypeSearchWindow(AdvancedDropdownState state, Action<Type> onTypePicked) : base(state) {
			_onTypePicked = onTypePicked;
			minimumSize   = new Vector2(280, 350);
		}

		protected override AdvancedDropdownItem BuildRoot() {
			var root = new AdvancedDropdownItem("Type d'export");

			var primitives = new AdvancedDropdownItem("Primitifs");
			foreach (var type in PrimitiveTypes)
				primitives.AddChild(new TypeItem(type.Name, type));
			root.AddChild(primitives);

			var objectsRoot = new AdvancedDropdownItem("UnityEngine.Object");
			var byNamespace = new SortedDictionary<string, AdvancedDropdownItem>(StringComparer.Ordinal);

			var allObjectTypes = new[] { typeof(Object) }
				.Concat(
					TypeCache.GetTypesDerivedFrom<Object>()
						.Where(t => t.IsPublic && !t.IsAbstract && !t.IsGenericTypeDefinition)
				)
				.Distinct()
				.OrderBy(t => t.Name, StringComparer.Ordinal);

			foreach (var type in allObjectTypes) {
				var ns = string.IsNullOrEmpty(type.Namespace) ? "Autres" : type.Namespace;

				if (!byNamespace.TryGetValue(ns, out var nsItem)) {
					nsItem          = new AdvancedDropdownItem(ns);
					byNamespace[ns] = nsItem;
				}

				nsItem.AddChild(new TypeItem(type.Name, type));
			}

			foreach (var nsItem in byNamespace.Values)
				objectsRoot.AddChild(nsItem);

			root.AddChild(objectsRoot);
			return root;
		}

		protected override void ItemSelected(AdvancedDropdownItem item) {
			if (item is TypeItem typeItem)
				_onTypePicked?.Invoke(typeItem.Type);
		}
	}
}
#endif