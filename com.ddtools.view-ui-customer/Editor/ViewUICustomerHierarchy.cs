using System.Collections.Generic;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Core.EditorTools.ViewUICustomer
{
	internal sealed class ViewUICustomerHierarchy
	{
		private readonly HashSet<int> _expandedObjects = new();
		private Vector2 _scroll;
		private string _search = string.Empty;
		private GameObject _renameTarget;
		private string _renameValue;
		private Type _helperType;
		private Type _elementType;
		private GameObject _root;
		private Action _helpersChanged;
		private Func<ViewUICustomerPresetData> _stateDataProvider;
		private Action<GameObject, ViewUICustomerElementData> _elementStateApplied;
		private GameObject _selectionAnchor;

		public void Configure(
			Type helperType,
			Type elementType,
			Action helpersChanged,
			Func<ViewUICustomerPresetData> stateDataProvider,
			Action<GameObject, ViewUICustomerElementData> elementStateApplied)
		{
			_helperType = helperType;
			_elementType = elementType;
			_helpersChanged = helpersChanged;
			_stateDataProvider = stateDataProvider;
			_elementStateApplied = elementStateApplied;
		}

		public void SetRoot(GameObject root)
		{
			_root = root;
			_selectionAnchor = root;
			_expandedObjects.Clear();

			if (root != null)
				ExpandRecursive(root.transform);
		}

		public void Draw(Rect rect, GameObject root)
		{
			GUI.BeginGroup(rect);
			GUILayout.BeginArea(new Rect(0f, 0f, rect.width, rect.height));
			DrawToolbar(root);

			_scroll = EditorGUILayout.BeginScrollView(_scroll, false, true);

			if (root == null)
				EditorGUILayout.HelpBox("Preview hierarchy is empty.", MessageType.Info);
			else
			{
				var valueUsage = ViewUICustomerHelperUtility.GetValueUsage(root, _helperType);
				DrawNode(root, 0, root, valueUsage);
			}

			EditorGUILayout.EndScrollView();
			HandleKeyboard(root);
			GUILayout.EndArea();
			GUI.EndGroup();
		}

		private void DrawToolbar(GameObject root)
		{
			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
			{
				_search = GUILayout.TextField(_search, GUI.skin.FindStyle("ToolbarSearchTextField"), GUILayout.ExpandWidth(true));

				using (new EditorGUI.DisabledScope(root == null || _helperType == null))
				{
					if (GUILayout.Button(new GUIContent("H+", "Добавить GetHelper и отдельное значение ElementType всем немаркированным визуальным объектам"), EditorStyles.toolbarButton, GUILayout.Width(30f)))
						AddHelpersToAllMissingObjects();
				}

				using (new EditorGUI.DisabledScope(root == null))
				{
					if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(24f)))
						CreateChild(IsInRoot(Selection.activeGameObject, root) ? Selection.activeGameObject : root);
				}
			}
		}

		private bool DrawNode(GameObject gameObject, int depth, GameObject root, Dictionary<int, int> valueUsage)
		{
			bool matchesSearch = MatchesSearch(gameObject);
			bool childMatchesSearch = HasMatchingDescendant(gameObject.transform);

			if (matchesSearch == false && childMatchesSearch == false)
				return false;

			var rowRect = EditorGUILayout.GetControlRect(false, 20f);
			bool isSelected = Selection.gameObjects.Contains(gameObject);

			if (isSelected && Event.current.type == EventType.Repaint)
				EditorGUI.DrawRect(rowRect, new Color(0.18f, 0.38f, 0.58f, 0.8f));

			float indent = depth * 14f;
			var foldoutRect = new Rect(rowRect.x + indent, rowRect.y + 2f, 16f, 16f);
			var activeRect = new Rect(foldoutRect.xMax + 1f, rowRect.y + 2f, 16f, 16f);
			var labelRect = new Rect(activeRect.xMax + 3f, rowRect.y, Mathf.Max(20f, rowRect.xMax - activeRect.xMax - 103f), rowRect.height);
			var badgeRect = new Rect(rowRect.xMax - 98f, rowRect.y + 2f, 96f, rowRect.height - 4f);
			bool hasChildren = gameObject.transform.childCount > 0;
			bool isExpanded = _expandedObjects.Contains(gameObject.GetInstanceID()) || string.IsNullOrEmpty(_search) == false;

			if (hasChildren)
			{
				bool newExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, GUIContent.none);

				if (newExpanded)
					_expandedObjects.Add(gameObject.GetInstanceID());
				else
					_expandedObjects.Remove(gameObject.GetInstanceID());

				isExpanded = newExpanded;
			}

			EditorGUI.BeginChangeCheck();
			bool isActive = EditorGUI.Toggle(activeRect, gameObject.activeSelf);

			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(gameObject, "Toggle preview object");
				gameObject.SetActive(isActive);
			}

			if (_renameTarget == gameObject)
			{
				GUI.SetNextControlName("ViewUICustomerHierarchyRename");
				_renameValue = EditorGUI.TextField(labelRect, _renameValue);
				EditorGUI.FocusTextInControl("ViewUICustomerHierarchyRename");

				if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
				{
					CommitRename();
					Event.current.Use();
				}
			}
			else
			{
				var icon = EditorGUIUtility.ObjectContent(gameObject, typeof(GameObject)).image;
				var content = new GUIContent(gameObject.name, icon);
				GUI.Label(labelRect, content);
			}

			DrawHelperBadge(gameObject, badgeRect, valueUsage);

			HandleRowEvents(rowRect, gameObject, root);

			if (isExpanded)
			{
				for (int childIndex = 0; childIndex < gameObject.transform.childCount; childIndex++)
					DrawNode(gameObject.transform.GetChild(childIndex).gameObject, depth + 1, root, valueUsage);
			}

			return true;
		}

		private void HandleRowEvents(Rect rowRect, GameObject gameObject, GameObject root)
		{
			var currentEvent = Event.current;

			if (rowRect.Contains(currentEvent.mousePosition) && currentEvent.type == EventType.MouseDown)
			{
				if (currentEvent.button == 0)
				{
					SelectObject(gameObject, root, currentEvent.control || currentEvent.command, currentEvent.shift);

					if (currentEvent.clickCount == 2)
						BeginRename(gameObject);

					currentEvent.Use();
				}
				else if (currentEvent.button == 1)
				{
					if (Selection.gameObjects.Contains(gameObject) == false)
						SelectObject(gameObject, root, false, false);

					ShowContextMenu(gameObject, root);
					currentEvent.Use();
				}
			}

			if (rowRect.Contains(currentEvent.mousePosition) && currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
			{
				DragAndDrop.PrepareStartDrag();
				DragAndDrop.objectReferences = new UnityEngine.Object[] { gameObject };
				DragAndDrop.StartDrag(gameObject.name);
				currentEvent.Use();
			}

			if (rowRect.Contains(currentEvent.mousePosition) && (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform))
			{
				var draggedObject = DragAndDrop.objectReferences.Length == 1 ? DragAndDrop.objectReferences[0] as GameObject : null;

				if (draggedObject == null || IsInRoot(draggedObject, root) == false || draggedObject == root || draggedObject == gameObject || gameObject.transform.IsChildOf(draggedObject.transform))
					return;

				DragAndDrop.visualMode = DragAndDropVisualMode.Move;

				if (currentEvent.type == EventType.DragPerform)
				{
					DragAndDrop.AcceptDrag();
					Undo.SetTransformParent(draggedObject.transform, gameObject.transform, "Reparent preview object");
				}

				currentEvent.Use();
			}
		}

		private void SelectObject(GameObject gameObject, GameObject root, bool additive, bool range)
		{
			if (range && _selectionAnchor != null && IsInRoot(_selectionAnchor, root))
			{
				var objects = GetHierarchyObjects(root);
				int anchorIndex = objects.IndexOf(_selectionAnchor);
				int targetIndex = objects.IndexOf(gameObject);

				if (anchorIndex >= 0 && targetIndex >= 0)
				{
					int start = Mathf.Min(anchorIndex, targetIndex);
					int count = Mathf.Abs(anchorIndex - targetIndex) + 1;
					var rangeSelection = objects.GetRange(start, count);
					rangeSelection.Remove(gameObject);
					rangeSelection.Insert(0, gameObject);
					Selection.objects = rangeSelection.Cast<UnityEngine.Object>().ToArray();
					return;
				}
			}

			if (additive)
			{
				var selected = Selection.gameObjects.Where(selectedObject => IsInRoot(selectedObject, root)).ToList();

				if (selected.Contains(gameObject))
					selected.Remove(gameObject);
				else
					selected.Insert(0, gameObject);

				Selection.objects = selected.Cast<UnityEngine.Object>().ToArray();
			}
			else
			{
				Selection.activeGameObject = gameObject;
			}

			_selectionAnchor = gameObject;
		}

		private static List<GameObject> GetHierarchyObjects(GameObject root)
		{
			return root == null
				? new List<GameObject>()
				: root.GetComponentsInChildren<Transform>(true).Select(transform => transform.gameObject).ToList();
		}

		private void ShowContextMenu(GameObject gameObject, GameObject root)
		{
			var menu = new GenericMenu();
			menu.AddItem(new GUIContent("Create Empty Child"), false, () => CreateChild(gameObject));
			menu.AddItem(new GUIContent("Duplicate"), false, () => Duplicate(gameObject));
			menu.AddItem(new GUIContent("Rename"), false, () => BeginRename(gameObject));
			menu.AddSeparator(string.Empty);
			AddHelperMenuItems(menu, gameObject);
			AddStateCopyMenuItems(menu, gameObject);
			menu.AddSeparator(string.Empty);

			if (gameObject != root)
				menu.AddItem(new GUIContent("Delete"), false, () => Undo.DestroyObjectImmediate(gameObject));
			else
				menu.AddDisabledItem(new GUIContent("Delete"));

			menu.ShowAsContext();
		}

		private void DrawHelperBadge(GameObject gameObject, Rect rect, Dictionary<int, int> valueUsage)
		{
			if (_helperType == null)
				return;

			var helper = ViewUICustomerHelperUtility.GetHelper(gameObject, _helperType);

			if (helper == null)
			{
				if (ViewUICustomerHelperUtility.NeedsHelper(gameObject))
				{
					EditorGUI.DrawRect(rect, new Color(0.55f, 0.32f, 0.05f, 0.85f));
					GUI.Label(rect, "! GetHelper", EditorStyles.centeredGreyMiniLabel);
				}

				return;
			}

			int value = ViewUICustomerHelperUtility.GetElementTypeValue(helper);
			bool duplicate = valueUsage.TryGetValue(value, out var usage) && usage > 1;
			EditorGUI.DrawRect(rect, duplicate
				? new Color(0.58f, 0.12f, 0.12f, 0.9f)
				: new Color(0.12f, 0.42f, 0.22f, 0.9f));
			GUI.Label(rect, duplicate
				? $"! {ViewUICustomerHelperUtility.GetElementTypeName(helper)}"
				: ViewUICustomerHelperUtility.GetElementTypeName(helper), EditorStyles.centeredGreyMiniLabel);
		}

		private void AddHelperMenuItems(GenericMenu menu, GameObject gameObject)
		{
			if (_helperType == null || _elementType == null)
			{
				menu.AddDisabledItem(new GUIContent("GetHelper/Generated helper type was not found"));
				return;
			}

			var helper = ViewUICustomerHelperUtility.GetHelper(gameObject, _helperType);
			menu.AddItem(new GUIContent("GetHelper/Add to all unmarked visual objects"), false, AddHelpersToAllMissingObjects);

			if (helper == null)
			{
				menu.AddItem(new GUIContent("GetHelper/Add and generate ElementType"), false, () => AddHelper(gameObject, true));

				return;
			}

			foreach (var enumValue in Enum.GetValues(_elementType))
			{
				int value = Convert.ToInt32(enumValue);

				if (value < 0)
					continue;

				string name = Enum.GetName(_elementType, enumValue);
				menu.AddItem(
					new GUIContent($"GetHelper/Element Type/{name}"),
					ViewUICustomerHelperUtility.GetElementTypeValue(helper) == value,
					() =>
					{
						ViewUICustomerHelperUtility.SetElementType(helper, value);
						_helpersChanged?.Invoke();
					});
			}

			menu.AddItem(new GUIContent("GetHelper/Remove"), false, () =>
			{
				Undo.DestroyObjectImmediate(helper);
				_helpersChanged?.Invoke();
			});
		}

		private void AddStateCopyMenuItems(GenericMenu menu, GameObject gameObject)
		{
			var helper = ViewUICustomerHelperUtility.GetHelper(gameObject, _helperType);

			if (helper == null || _stateDataProvider == null || _elementStateApplied == null)
			{
				menu.AddDisabledItem(new GUIContent("Copy Element From State/GetHelper is required"));
				return;
			}

			var data = _stateDataProvider.Invoke();
			string elementTypeName = ViewUICustomerHelperUtility.GetElementTypeName(helper);
			var addedPaths = new HashSet<string>();

			if (data?.States != null)
			{
				foreach (var state in data.States)
				{
					if (state?.Groups == null)
						continue;

					foreach (var group in state.Groups)
					{
						var element = group?.Elements?.Find(item => item.ElementType == elementTypeName);

						if (element == null)
							continue;

						string stateName = Enum.IsDefined(typeof(Core.Scripts.UI.Universal.ViewUICustomer.ViewUIStateType), group.StateType)
							? ((Core.Scripts.UI.Universal.ViewUICustomer.ViewUIStateType)group.StateType).ToString()
							: group.StateType.ToString();
						string menuPath = $"Copy Element From State/{state.ViewType}/{stateName}";

						if (addedPaths.Add(menuPath))
							menu.AddItem(new GUIContent(menuPath), false, () => ApplyElementStateToSelection(gameObject, group));
					}
				}
			}

			if (addedPaths.Count == 0)
				menu.AddDisabledItem(new GUIContent("Copy Element From State/No saved states for this element"));
		}

		private void ApplyElementStateToSelection(GameObject contextObject, ViewUICustomerGroupData sourceGroup)
		{
			var selectedObjects = Selection.gameObjects
				.Where(gameObject => IsInRoot(gameObject, _root))
				.ToArray();

			if (selectedObjects.Contains(contextObject) == false)
				selectedObjects = new[] { contextObject };

			foreach (var gameObject in selectedObjects)
			{
				var helper = ViewUICustomerHelperUtility.GetHelper(gameObject, _helperType);

				if (helper == null)
					continue;

				string elementTypeName = ViewUICustomerHelperUtility.GetElementTypeName(helper);
				var element = sourceGroup.Elements.Find(item => item.ElementType == elementTypeName);

				if (element != null)
					_elementStateApplied.Invoke(gameObject, element);
			}
		}

		private bool AddHelper(GameObject gameObject, bool notify)
		{
			if (ViewUICustomerCodeGenerator.TryAddElementEnumValue(
				_elementType,
				gameObject.name,
				out var value,
				out _,
				out var error) == false)
			{
				EditorUtility.DisplayDialog("GetHelper generation failed", error, "OK");
				return false;
			}

			var helper = Undo.AddComponent(gameObject, _helperType);
			ViewUICustomerHelperUtility.SetElementType(helper, value);

			if (notify)
			{
				_helpersChanged?.Invoke();
				RequestScriptRefresh();
			}

			return true;
		}

		private void AddHelpersToAllMissingObjects()
		{
			bool addedAny = false;

			foreach (var transform in _root.GetComponentsInChildren<Transform>(true))
			{
				if (ViewUICustomerHelperUtility.NeedsHelper(transform.gameObject) == false
					|| ViewUICustomerHelperUtility.GetHelper(transform.gameObject, _helperType) != null)
					continue;

				if (AddHelper(transform.gameObject, false) == false)
					break;

				addedAny = true;
			}

			if (addedAny)
			{
				_helpersChanged?.Invoke();
				RequestScriptRefresh();
			}
		}

		private static void RequestScriptRefresh()
		{
			EditorApplication.delayCall += AssetDatabase.Refresh;
		}

		private void HandleKeyboard(GameObject root)
		{
			var currentEvent = Event.current;
			var selectedObject = Selection.activeGameObject;

			if (currentEvent.type != EventType.KeyDown || selectedObject == null || IsInRoot(selectedObject, root) == false)
				return;

			if (currentEvent.keyCode == KeyCode.F2)
			{
				BeginRename(selectedObject);
				currentEvent.Use();
			}
			else if (currentEvent.keyCode == KeyCode.Delete && selectedObject != root)
			{
				Undo.DestroyObjectImmediate(selectedObject);
				currentEvent.Use();
			}
			else if (currentEvent.control && currentEvent.keyCode == KeyCode.D)
			{
				Duplicate(selectedObject);
				currentEvent.Use();
			}
		}

		private void BeginRename(GameObject gameObject)
		{
			_renameTarget = gameObject;
			_renameValue = gameObject.name;
		}

		private void CommitRename()
		{
			if (_renameTarget != null && string.IsNullOrWhiteSpace(_renameValue) == false)
			{
				Undo.RecordObject(_renameTarget, "Rename preview object");
				_renameTarget.name = _renameValue.Trim();
			}

			_renameTarget = null;
			_renameValue = null;
		}

		private static void CreateChild(GameObject parent)
		{
			if (parent == null)
				return;

			var child = new GameObject("GameObject", typeof(RectTransform));
			Undo.RegisterCreatedObjectUndo(child, "Create preview object");
			Undo.SetTransformParent(child.transform, parent.transform, "Parent preview object");
			Selection.activeGameObject = child;
		}

		private static void Duplicate(GameObject gameObject)
		{
			var duplicate = UnityEngine.Object.Instantiate(gameObject, gameObject.transform.parent);
			duplicate.name = gameObject.name;
			Undo.RegisterCreatedObjectUndo(duplicate, "Duplicate preview object");
			duplicate.transform.SetSiblingIndex(gameObject.transform.GetSiblingIndex() + 1);
			Selection.activeGameObject = duplicate;
		}

		private bool MatchesSearch(GameObject gameObject)
		{
			return string.IsNullOrEmpty(_search) || gameObject.name.ToLowerInvariant().Contains(_search.ToLowerInvariant());
		}

		private bool HasMatchingDescendant(Transform transform)
		{
			if (string.IsNullOrEmpty(_search))
				return true;

			for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
			{
				var child = transform.GetChild(childIndex);

				if (MatchesSearch(child.gameObject) || HasMatchingDescendant(child))
					return true;
			}

			return false;
		}

		private void ExpandRecursive(Transform transform)
		{
			_expandedObjects.Add(transform.gameObject.GetInstanceID());

			for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
				ExpandRecursive(transform.GetChild(childIndex));
		}

		private static bool IsInRoot(GameObject gameObject, GameObject root)
		{
			return gameObject != null && root != null && (gameObject == root || gameObject.transform.IsChildOf(root.transform));
		}
	}
}
