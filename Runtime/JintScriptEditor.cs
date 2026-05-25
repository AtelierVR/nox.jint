#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Jint;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Nox.Jint.Runtime {
	public class ExportEntry {
		public string Key;
		public object Value;
		public Type   Type;
	}

	[CustomEditor(typeof(JintScript))]
	public class JintScriptEditor : Editor {
		public JintScript Module
			=> (JintScript)target;

		private MultiColumnListView _exportsListView;
		private List<ExportEntry>   _entries = new();

		public override VisualElement CreateInspectorGUI() {
			var iconAsset = Resources.Load<Texture2D>("api.nox.jint.jintscript");
			if (iconAsset) EditorGUIUtility.SetIconForObject(target, iconAsset);
			var inspectorAsset = Resources.Load<VisualTreeAsset>("api.nox.jint.jintscript");
			if (!inspectorAsset) return new VisualElement();
			var root = inspectorAsset.CloneTree();
			if (!Module) return root;

			// Setup script field
			var sc = root.Q<ObjectField>("script");
			sc.value = Module.asset;
			sc.RegisterValueChangedCallback(
				evt => {
					if (evt.newValue is not JintFile newScript) return;
					Module.asset = newScript;
					EditorUtility.SetDirty(Module);
					Repaint();
				}
			);

			// Setup exports editor
			SetupExportsEditor(root);

			return root;
		}

		private void SetupExportsEditor(VisualElement root) {
			_exportsListView = root.Q<MultiColumnListView>("exports-list");

			// Setup columns
			SetupColumns();

			// Setup add/remove callbacks for the integrated buttons
			_exportsListView.itemsAdded   += OnItemsAdded;
			_exportsListView.itemsRemoved += OnItemsRemoved;

			// Populate existing exports
			RefreshExportsList();
		}

		private void SetupColumns() {
			// Key column
			var keyColumn = new Column {
				name        = "key",
				title       = "Key",
				width       = 120,
				minWidth    = 80,
				stretchable = false,
				sortable    = true,
				makeCell = () => {
					var field = new TextField {
						style = {
							marginRight = 5,
							marginTop   = 4
						}
					};
					field.RegisterValueChangedCallback(OnKeyChanged);
					return field;
				},
				bindCell = (element, index) => {
					if (_entries == null || index < 0 || index >= _entries.Count) return;
					var item = _entries[index];
					if (item?.Key == null) return;
					var field = (TextField)element;
					field.SetValueWithoutNotify(item.Key);
					field.userData = index;
				}
			};


			var typeValues = new List<Type> {
				typeof(string),
				typeof(int),
				typeof(float),
				typeof(bool),
				typeof(Object),
				typeof(GameObject),
				typeof(Transform),
				typeof(TMPro.TextMeshProUGUI),
				typeof(Collider),
			};

			var typeColumn = new Column {
				name        = "type",
				title       = "Type",
				width       = 120,
				minWidth    = 80,
				stretchable = false,
				sortable    = true,
				makeCell = () => {
					var dropdown = new DropdownField {
						choices = typeValues.Select(t => t.Name).ToList(),
						style = {
							marginRight = 5,
							marginTop   = 4
						}
					};
					dropdown.RegisterValueChangedCallback(
						evt => {
							var dropdownElement = (DropdownField)evt.target;
							var selectedIndex   = dropdownElement.choices.IndexOf(evt.newValue);
							if (selectedIndex < 0 || selectedIndex >= typeValues.Count) return;
							var actualValue = typeValues[selectedIndex];
							var changeEvent = ChangeEvent<string>.GetPooled(evt.previousValue, actualValue.AssemblyQualifiedName);
							changeEvent.target = evt.target;
							OnTypeChanged(changeEvent);
						}
					);
					return dropdown;
				},
				bindCell = (element, index) => {
					if (_entries == null || index < 0 || index >= _entries.Count) return;
					var item = _entries[index];
					if (item?.Type == null) return;
					var dropdown = (DropdownField)element;

					var typeIndex = typeValues.FindIndex(t => t == item.Type);
					if (typeIndex >= 0 && typeIndex < dropdown.choices.Count)
						dropdown.SetValueWithoutNotify(dropdown.choices[typeIndex]);
					else dropdown.SetValueWithoutNotify(nameof(Object));

					dropdown.userData = index;
				}
			};

			// Value column
			var valueColumn = new Column {
				name        = "value",
				title       = "Value",
				width       = 150,
				minWidth    = 100,
				stretchable = true,
				sortable    = false,
				makeCell = () => {
					var container = new VisualElement {
						style = {
							marginRight = 5,
							marginTop   = 4
						}
					};
					return container;
				},
				bindCell = (element, index) => {
					if (_entries == null || index < 0 || index >= _entries.Count) return;
					var item = _entries[index];
					if (item == null) return;
					element.Clear();
					element.Add(CreateValueField(item.Value, item.Type, index));
				}
			};

			_exportsListView.columns.Clear();
			_exportsListView.columns.Add(keyColumn);
			_exportsListView.columns.Add(typeColumn);
			_exportsListView.columns.Add(valueColumn);

			_exportsListView.itemsSource = _entries;
		}

		private void OnKeyChanged(ChangeEvent<string> evt) {
			var field = (TextField)evt.target;
			var index = (int)field.userData;

			if (_entries == null || index < 0 || index >= _entries.Count) return;

			if (string.IsNullOrWhiteSpace(evt.newValue) || _entries.Any(item => item.Key == evt.newValue && _entries.IndexOf(item) != index)) {
				field.SetValueWithoutNotify(evt.previousValue);
				return;
			}

			var oldKey = _entries[index].Key;
			_entries[index].Key = evt.newValue;

			// Update dictionary
			var dict = Module.GetExports();
			dict.Remove(oldKey);
			dict[evt.newValue] = _entries[index].Value;
			Module.SetExports(dict);

			EditorUtility.SetDirty(Module);
		}

		private void OnTypeChanged(ChangeEvent<string> evt) {
			var dropdown = (DropdownField)evt.target;
			var index    = (int)dropdown.userData;

			if (_entries == null || index < 0 || index >= _entries.Count) return;

			var newType = Type.GetType(evt.newValue) ?? typeof(string);

			_entries[index].Type = newType;
			_entries[index].Value = newType == typeof(string)
				? string.Empty
				: newType.IsValueType
					? Activator.CreateInstance(newType)
					: null;

			var dict = Module.GetExports();
			dict[_entries[index].Key] = _entries[index].Value;


			Module.SetExports(dict);
			EditorUtility.SetDirty(Module);
			_exportsListView.RefreshItem(index);
		}

		private void OnValueChanged(int index, object newValue) {
			if (_entries == null || index < 0 || index >= _entries.Count) return;

			_entries[index].Value = newValue;
			var dict = Module.GetExports();
			dict[_entries[index].Key] = newValue;

			Module.SetExports(dict);
			EditorUtility.SetDirty(Module);
			Repaint();
		}


		private VisualElement CreateValueField(object value, Type type, int index) {
			if (typeof(string) == type) {
				var stringField = new TextField { value = value as string ?? string.Empty };
				stringField.RegisterValueChangedCallback(evt => OnValueChanged(index, evt.newValue));
				return stringField;
			}

			if (typeof(int) == type) {
				var intField = new IntegerField { value = value is int intValue ? intValue : 0 };
				intField.RegisterValueChangedCallback(evt => OnValueChanged(index, evt.newValue));
				return intField;
			}

			if (typeof(float) == type) {
				var floatField = new FloatField { value = value is float floatValue ? floatValue : 0f };
				floatField.RegisterValueChangedCallback(evt => OnValueChanged(index, evt.newValue));
				return floatField;
			}

			if (typeof(bool) == type) {
				var boolField = new Toggle { value = value is true };
				boolField.RegisterValueChangedCallback(evt => OnValueChanged(index, evt.newValue));
				return boolField;
			}


			if (typeof(Object).IsAssignableFrom(type) || type == typeof(Object)) {
				var objectField = new ObjectField { objectType = type, value = value as Object };
				objectField.RegisterValueChangedCallback(evt => OnValueChanged(index, evt.newValue));
				return objectField;
			}

			// Fallback to string field for unsupported types
			var fallbackField = new Label { text = $"Unsupported type: {type?.Name ?? "null"}" };
			return fallbackField;
		}


		private void RefreshExportsList() {
			_entries.Clear();

			var exports = Module.GetExports();
			foreach (var kv in exports)
				_entries.Add(
					new ExportEntry {
						Key   = kv.Key,
						Value = kv.Value,
						Type  = kv.Value?.GetType() ?? typeof(string)
					}
				);

			_exportsListView?.RefreshItems();
		}

		private void OnItemsAdded(IEnumerable<int> indices) {
			var sortedIndices = indices.OrderBy(x => x).ToList();
			var exports       = Module.GetExports();

			foreach (var index in sortedIndices) {
				var newKey  = "new_export";
				var counter = 1;

				// Find unique key
				while (exports.ContainsKey(newKey)) {
					newKey = $"new_export_{counter}";
					counter++;
				}

				var newItem = new ExportEntry {
					Key   = newKey,
					Type  = typeof(string),
					Value = ""
				};

				// Ensure the index is within bounds
				var insertIndex = Math.Min(index, _entries.Count);
				_entries.Insert(insertIndex, newItem);
				exports[newKey] = "";
			}

			Module.SetExports(exports);
			EditorUtility.SetDirty(Module);

			// Refresh the list view to ensure proper binding
			_exportsListView?.RefreshItems();
		}

		private void OnItemsRemoved(IEnumerable<int> indices) {
			var sortedIndices = indices.OrderByDescending(x => x).ToList();
			var exports       = Module.GetExports();

			foreach (var index in sortedIndices) {
				if (index < 0 || index >= _entries.Count) continue;
				exports.Remove(_entries[index].Key);
				_entries.RemoveAt(index);
			}

			Module.SetExports(exports);
			EditorUtility.SetDirty(Module);
		}
	}
}
#endif