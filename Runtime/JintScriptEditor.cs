#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Jint;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
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
					var item  = _entries[index];
					var field = (TextField)element;
					field.SetValueWithoutNotify(item.Key ?? string.Empty);
					field.userData = index;
				}
			};

			// Type column
			var typeColumn = new Column {
				name        = "type",
				title       = "Type",
				width       = 160,
				minWidth    = 120,
				stretchable = false,
				sortable    = true,
				makeCell = () => {
					var row = new VisualElement {
						style = {
							flexDirection = FlexDirection.Row,
							alignItems    = Align.Center,
							marginRight   = 5,
							marginTop     = 4
						}
					};

					var typeButton = new Button {
						style = {
							flexGrow      = 1,
							unityTextAlign = TextAnchor.MiddleLeft
						}
					};
					var arrayToggle = new Toggle {
						tooltip = "Exporter un tableau (array) de ce type",
						style   = { marginLeft = 4 }
					};

					typeButton.clicked += () => {
						if (row.userData is int idx) OpenTypeSearchWindow(typeButton, idx);
					};
					arrayToggle.RegisterValueChangedCallback(evt => {
						if (row.userData is int idx) SetArrayFlag(idx, evt.newValue);
					});

					row.Add(typeButton);
					row.Add(arrayToggle);
					return row;
				},
				bindCell = (element, index) => {
					if (_entries == null || index < 0 || index >= _entries.Count) return;
					var item = _entries[index];
					element.userData = index;

					var typeButton  = element.Q<Button>();
					var arrayToggle = element.Q<Toggle>();

					var isArray     = item.Type is { IsArray: true };
					var elementType = isArray ? item.Type.GetElementType() : item.Type;

					typeButton.text = elementType?.Name ?? "<null>";
					arrayToggle.SetValueWithoutNotify(isArray);
				}
			};

			// Value column
			var valueColumn = new Column {
				name        = "value",
				title       = "Value",
				width       = 220,
				minWidth    = 140,
				stretchable = true,
				sortable    = false,
				makeCell = () => new VisualElement {
					style = {
						marginRight = 5,
						marginTop   = 4
					}
				},
				bindCell = (element, index) => {
					if (_entries == null || index < 0 || index >= _entries.Count) return;
					var item = _entries[index];
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
			if (field.userData is not int index || _entries == null || index < 0 || index >= _entries.Count) return;

			var newKey      = evt.newValue?.Trim();
			var isDuplicate = _entries.Where((_, i) => i != index).Any(e => e.Key == newKey);

			if (string.IsNullOrEmpty(newKey) || isDuplicate) {
				field.SetValueWithoutNotify(evt.previousValue);
				return;
			}

			_entries[index].Key = newKey;
			PersistEntries();
		}

		private void OpenTypeSearchWindow(VisualElement anchor, int index) {
			var window = new TypeSearchWindow(
				new AdvancedDropdownState(),
				selectedType => SetEntryElementType(index, selectedType)
			);
			window.Show(anchor.worldBound);
		}

		private void SetEntryElementType(int index, Type newElementType) {
			if (_entries == null || index < 0 || index >= _entries.Count) return;

			var wasArray = _entries[index].Type is { IsArray: true };
			var newType  = wasArray ? newElementType.MakeArrayType() : newElementType;

			_entries[index].Type  = newType;
			_entries[index].Value = CreateDefaultValue(newType);

			PersistEntries();
			_exportsListView?.RefreshItem(index);
		}

		private void SetArrayFlag(int index, bool isArray) {
			if (_entries == null || index < 0 || index >= _entries.Count) return;

			var currentType = _entries[index].Type;
			var wasArray    = currentType is { IsArray: true };
			
			if (wasArray == isArray) return;

			var elementType = wasArray ? currentType.GetElementType() : currentType;
			elementType ??= typeof(string);

			var newType       = isArray ? elementType.MakeArrayType() : elementType;
			var currentValue  = _entries[index].Value;
			object newValue;

			if (isArray) {
				var arr = Array.CreateInstance(elementType, 1);
				arr.SetValue(currentValue ?? CreateDefaultScalarValue(elementType), 0);
				newValue = arr;
			} else {
				if (currentValue is Array { Length: > 0 } existingArray) {
					newValue = existingArray.GetValue(0);
				} else {
					newValue = CreateDefaultScalarValue(elementType);
				}
			}

			_entries[index].Type  = newType;
			_entries[index].Value = newValue;

			PersistEntries();
			_exportsListView?.RefreshItem(index);
		}

		private void OnValueChanged(int index, object newValue) {
			if (_entries == null || index < 0 || index >= _entries.Count) return;

			_entries[index].Value = newValue;
			PersistEntries();
		}

		private VisualElement CreateValueField(object value, Type type, int index) {
			if (type == null) return new Label { text = "—" };

			return type.IsArray
				? CreateArrayValueField(value as Array, type.GetElementType() ?? typeof(object), index)
				: CreateScalarValueField(value, type, newValue => OnValueChanged(index, newValue));
		}

		private VisualElement CreateScalarValueField(object value, Type type, Action<object> onChanged) {
			if (type == typeof(string)) {
				var field = new TextField {
					value     = value as string ?? string.Empty,
					multiline = true,
					style = {
						whiteSpace = WhiteSpace.Normal,
						minHeight  = 20
					}
				};
				field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
				return field;
			}

			if (type == typeof(int)) {
				var field = new IntegerField { value = value is int intValue ? intValue : 0 };
				field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
				return field;
			}

			if (type == typeof(float)) {
				var field = new FloatField { value = value is float floatValue ? floatValue : 0f };
				field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
				return field;
			}

			if (type == typeof(bool)) {
				var field = new Toggle { value = value is true };
				field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
				return field;
			}

			if (typeof(Object).IsAssignableFrom(type)) {
				var field = new ObjectField { objectType = type, value = value as Object };
				field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
				return field;
			}

			return new Label { text = $"Type non supporté : {type.Name}" };
		}

		private VisualElement CreateArrayValueField(Array currentArray, Type elementType, int index) {
			var list = currentArray != null ? currentArray.Cast<object>().ToList() : new List<object>();

			var container = new VisualElement();
			var header = new VisualElement {
				style = {
					flexDirection = FlexDirection.Row,
					alignItems    = Align.Center,
					marginBottom  = 2
				}
			};
			var countLabel = new Label {
				style = {
					flexGrow              = 1,
					unityFontStyleAndWeight = FontStyle.Italic,
					fontSize              = 10
				}
			};
			var addButton = new Button { text = "+", style = { width = 20 } };

			header.Add(countLabel);
			header.Add(addButton);

			var rowsContainer = new VisualElement();
			container.Add(header);
			container.Add(rowsContainer);

			void Commit() {
				var array = Array.CreateInstance(elementType, list.Count);
				for (var i = 0; i < list.Count; i++) array.SetValue(list[i], i);
				countLabel.text = $"{list.Count} élément(s)";
				OnValueChanged(index, array);
			}

			void RebuildRows() {
				rowsContainer.Clear();

				for (var i = 0; i < list.Count; i++) {
					var elementIndex = i;
					var row = new VisualElement {
						style = {
							flexDirection = FlexDirection.Row,
							alignItems    = Align.Center,
							marginLeft    = 4,
							marginBottom  = 1
						}
					};

					var fieldSlot = new VisualElement { style = { flexGrow = 1 } };
					fieldSlot.Add(
						CreateScalarValueField(
							list[elementIndex],
							elementType,
							newVal => {
								list[elementIndex] = newVal;
								Commit();
							}
						)
					);

					var removeButton = new Button(() => {
						list.RemoveAt(elementIndex);
						Commit();
						RebuildRows();
					}) { text = "-", style = { width = 20 } };

					row.Add(fieldSlot);
					row.Add(removeButton);
					rowsContainer.Add(row);
				}
			}

			addButton.clicked += () => {
				list.Add(CreateDefaultScalarValue(elementType));
				Commit();
				RebuildRows();
			};

			countLabel.text = $"{list.Count} élément(s)";
			RebuildRows();

			return container;
		}

		private static object CreateDefaultScalarValue(Type type) {
			if (type == typeof(string)) return string.Empty;
			if (typeof(Object).IsAssignableFrom(type)) return null;
			if (type.IsValueType) return Activator.CreateInstance(type);
			return null;
		}

		private static object CreateDefaultValue(Type type) {
			if (type == null) return null;
			if (type.IsArray) return Array.CreateInstance(type.GetElementType() ?? typeof(object), 0);
			return CreateDefaultScalarValue(type);
		}

		private void PersistEntries() {
			Module.SetExportsDetailed(_entries.Select(e => (e.Key, e.Type, e.Value)));
			EditorUtility.SetDirty(Module);
		}

		private void RefreshExportsList() {
			_entries.Clear();

			foreach (var (key, type, value) in Module.GetExportsDetailed())
				_entries.Add(new ExportEntry { Key = key, Type = type, Value = value });

			_exportsListView?.RefreshItems();
		}

		private void OnItemsAdded(IEnumerable<int> indices) {
			foreach (var index in indices.OrderBy(x => x)) {
				if (index < 0 || index >= _entries.Count) continue;

				var reservedKeys = _entries
					.Where((_, i) => i != index)
					.Select(e => e.Key)
					.Where(k => k != null);

				_entries[index] = new ExportEntry {
					Key   = GenerateUniqueKey("new_export", reservedKeys),
					Type  = typeof(string),
					Value = string.Empty
				};
			}

			PersistEntries();
			_exportsListView?.RefreshItems();
		}

		private void OnItemsRemoved(IEnumerable<int> indices) {
			PersistEntries();
		}

		private static string GenerateUniqueKey(string baseKey, IEnumerable<string> existingKeys) {
			var keys = existingKeys as ICollection<string> ?? existingKeys.ToList();
			if (!keys.Contains(baseKey)) return baseKey;

			var counter = 1;
			string candidate;
			do candidate = $"{baseKey}_{counter++}";
			while (keys.Contains(candidate));

			return candidate;
		}
	}
}
#endif