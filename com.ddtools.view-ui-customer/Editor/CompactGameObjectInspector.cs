using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Core.EditorTools.ViewUICustomer
{
	internal sealed class CompactGameObjectInspector
	{
		private readonly Dictionary<int, bool> _componentFoldouts = new();
		private GameObject[] _targets = System.Array.Empty<GameObject>();
		private GameObject _target;
		private Vector2 _scroll;

		public void SetTarget(GameObject target)
		{
			SetTargets(target == null ? System.Array.Empty<GameObject>() : new[] { target });
		}

		public void SetTargets(IEnumerable<GameObject> targets)
		{
			var newTargets = targets?.Where(target => target != null).Distinct().ToArray() ?? System.Array.Empty<GameObject>();

			if (_targets.SequenceEqual(newTargets))
				return;

			_targets = newTargets;
			_target = _targets.FirstOrDefault();
			_scroll = Vector2.zero;
		}

		public void Draw(float height, GameObject previewRoot)
		{
			_targets = _targets
				.Where(target => target != null && previewRoot != null && (target == previewRoot || target.transform.IsChildOf(previewRoot.transform)))
				.ToArray();
			_target = _targets.FirstOrDefault();

			if (_target == null)
			{
				EditorGUILayout.HelpBox("Select a preview object in the hierarchy or directly in Preview.", MessageType.Info);
				return;
			}

			var previousWideMode = EditorGUIUtility.wideMode;
			var previousHierarchyMode = EditorGUIUtility.hierarchyMode;
			var previousLabelWidth = EditorGUIUtility.labelWidth;
			EditorGUIUtility.wideMode = false;
			EditorGUIUtility.hierarchyMode = true;
			EditorGUIUtility.labelWidth = 110f;

			_scroll = EditorGUILayout.BeginScrollView(
				_scroll,
				false,
				true,
				GUIStyle.none,
				GUI.skin.verticalScrollbar,
				GUI.skin.scrollView,
				GUILayout.Height(height));

			DrawGameObjectSettings();

			if (_targets.Length > 1)
				EditorGUILayout.LabelField($"Selected: {_targets.Length}", EditorStyles.miniBoldLabel);

			foreach (var component in _target.GetComponents<Component>())
			{
				if (component == null)
				{
					EditorGUILayout.HelpBox("Missing script", MessageType.Error);
					continue;
				}

				var components = _targets
					.Select(target => target.GetComponent(component.GetType()))
					.Where(targetComponent => targetComponent != null)
					.ToArray();

				if (components.Length == _targets.Length)
					DrawComponent(components, _targets.Any(target => target == previewRoot));
			}

			if (GUILayout.Button("Add Component"))
				ShowAddComponentWindow(_targets);

			EditorGUILayout.EndScrollView();
			EditorGUIUtility.wideMode = previousWideMode;
			EditorGUIUtility.hierarchyMode = previousHierarchyMode;
			EditorGUIUtility.labelWidth = previousLabelWidth;
		}

		private void DrawGameObjectSettings()
		{
			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				bool multiple = _targets.Length > 1;
				bool sameActive = _targets.All(target => target.activeSelf == _target.activeSelf);
				bool sameTag = _targets.All(target => target.tag == _target.tag);
				bool sameLayer = _targets.All(target => target.layer == _target.layer);

				EditorGUI.BeginChangeCheck();
				EditorGUI.showMixedValue = sameActive == false;
				bool isActive = EditorGUILayout.ToggleLeft("Active", _target.activeSelf);
				EditorGUI.showMixedValue = false;
				string objectName;

				using (new EditorGUI.DisabledScope(multiple))
					objectName = EditorGUILayout.TextField("Name", multiple ? "— Multiple Objects —" : _target.name);

				EditorGUI.showMixedValue = sameTag == false;
				string tag = EditorGUILayout.TagField("Tag", _target.tag);
				EditorGUI.showMixedValue = sameLayer == false;
				int layer = EditorGUILayout.LayerField("Layer", _target.layer);
				EditorGUI.showMixedValue = false;

				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObjects(_targets, "Edit preview GameObjects");

					foreach (var target in _targets)
					{
						target.SetActive(isActive);

						if (multiple == false)
							target.name = objectName;

						target.tag = tag;
						target.layer = layer;
						EditorUtility.SetDirty(target);
					}
				}
			}
		}

		private void DrawComponent(Component[] components, bool isRoot)
		{
			var component = components[0];
			int instanceId = component.GetInstanceID();
			bool expanded = _componentFoldouts.TryGetValue(instanceId, out var currentExpanded) == false || currentExpanded;
			var headerRect = EditorGUILayout.GetControlRect(false, 22f);
			EditorGUI.DrawRect(headerRect, new Color(0.20f, 0.20f, 0.20f, 1f));
			var foldoutRect = new Rect(headerRect.x + 4f, headerRect.y + 3f, 16f, 16f);
			expanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none);
			_componentFoldouts[instanceId] = expanded;

			var serializedObject = new SerializedObject(components);
			var enabledProperty = serializedObject.FindProperty("m_Enabled");
			float contentX = foldoutRect.xMax + 2f;

			if (enabledProperty != null)
			{
				var enabledRect = new Rect(contentX, headerRect.y + 3f, 16f, 16f);
				serializedObject.UpdateIfRequiredOrScript();
				EditorGUI.showMixedValue = enabledProperty.hasMultipleDifferentValues;
				EditorGUI.BeginChangeCheck();
				bool enabled = EditorGUI.Toggle(enabledRect, enabledProperty.boolValue);

				if (EditorGUI.EndChangeCheck())
				{
					enabledProperty.boolValue = enabled;
					serializedObject.ApplyModifiedProperties();
				}

				EditorGUI.showMixedValue = false;
				contentX = enabledRect.xMax + 2f;
			}

			var icon = EditorGUIUtility.ObjectContent(component, component.GetType()).image;
			var labelRect = new Rect(contentX, headerRect.y, Mathf.Max(20f, headerRect.xMax - contentX - 24f), headerRect.height);
			GUI.Label(labelRect, new GUIContent(ObjectNames.NicifyVariableName(component.GetType().Name), icon), EditorStyles.boldLabel);
			var menuRect = new Rect(headerRect.xMax - 22f, headerRect.y + 1f, 20f, 20f);

			if (GUI.Button(menuRect, "⋮", EditorStyles.miniButton))
				ShowComponentMenu(component);

			if (Event.current.type == EventType.MouseDown && labelRect.Contains(Event.current.mousePosition))
			{
				_componentFoldouts[instanceId] = !expanded;
				Event.current.Use();
			}

			if (expanded == false)
				return;

			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
			{
				if (component is RectTransform)
					DrawRectTransform(serializedObject, components, isRoot);
				else if (component is Transform)
					DrawTransform(serializedObject, components, isRoot);
				else
					DrawSerializedProperties(serializedObject);
			}
		}

		private static void DrawRectTransform(SerializedObject serializedObject, Component[] components, bool lockPosition)
		{
			serializedObject.UpdateIfRequiredOrScript();
			using (new EditorGUI.DisabledScope(lockPosition))
				DrawProperty(serializedObject, "m_AnchoredPosition", "Position");
			DrawProperty(serializedObject, "m_SizeDelta", "Size");
			DrawProperty(serializedObject, "m_AnchorMin", "Anchor Min");
			DrawProperty(serializedObject, "m_AnchorMax", "Anchor Max");
			DrawProperty(serializedObject, "m_Pivot", "Pivot");
			DrawProperty(serializedObject, "m_LocalEulerAnglesHint", "Rotation");
			DrawProperty(serializedObject, "m_LocalScale", "Scale");
			serializedObject.ApplyModifiedProperties();

			if (lockPosition)
			{
				foreach (var component in components)
				{
					if (component is RectTransform rectTransform)
						rectTransform.anchoredPosition3D = Vector3.zero;
				}
			}
		}

		private static void DrawTransform(SerializedObject serializedObject, Component[] components, bool lockPosition)
		{
			serializedObject.UpdateIfRequiredOrScript();
			using (new EditorGUI.DisabledScope(lockPosition))
				DrawProperty(serializedObject, "m_LocalPosition", "Position");
			DrawProperty(serializedObject, "m_LocalEulerAnglesHint", "Rotation");
			DrawProperty(serializedObject, "m_LocalScale", "Scale");
			serializedObject.ApplyModifiedProperties();

			if (lockPosition)
			{
				foreach (var component in components)
				{
					if (component is Transform transform)
						transform.localPosition = Vector3.zero;
				}
			}
		}

		private static void DrawProperty(SerializedObject serializedObject, string propertyName, string label)
		{
			var property = serializedObject.FindProperty(propertyName);

			if (property != null)
				EditorGUILayout.PropertyField(property, new GUIContent(label), true);
		}

		private static void DrawSerializedProperties(SerializedObject serializedObject)
		{
			serializedObject.UpdateIfRequiredOrScript();
			var property = serializedObject.GetIterator();
			bool enterChildren = true;

			while (property.NextVisible(enterChildren))
			{
				enterChildren = false;

				if (property.propertyPath == "m_Script" || property.propertyPath == "m_Enabled")
					continue;

				EditorGUILayout.PropertyField(property, true);
			}

			serializedObject.ApplyModifiedProperties();
		}

		private static void ShowComponentMenu(Component component)
		{
			var menu = new GenericMenu();
			menu.AddItem(new GUIContent("Copy"), false, () => ComponentUtility.CopyComponent(component));

			menu.AddItem(new GUIContent("Paste Values"), false, () => ComponentUtility.PasteComponentValues(component));
			menu.AddSeparator(string.Empty);

			if (component is Transform)
				menu.AddDisabledItem(new GUIContent("Remove Component"));
			else
				menu.AddItem(new GUIContent("Remove Component"), false, () => Undo.DestroyObjectImmediate(component));

			menu.ShowAsContext();
		}

		private static void ShowAddComponentWindow(GameObject[] gameObjects)
		{
			var editorAssembly = typeof(EditorWindow).Assembly;
			var windowType = editorAssembly.GetType("UnityEditor.AddComponent.AddComponentWindow")
				?? editorAssembly.GetType("UnityEditor.AddComponentWindow");

			if (windowType == null)
				return;

			foreach (var method in windowType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
			{
				var parameters = method.GetParameters();

				if (method.Name != "Show" || parameters.Length != 2 || parameters[0].ParameterType != typeof(Rect))
					continue;

				var buttonRect = GUILayoutUtility.GetLastRect();
				buttonRect.position = GUIUtility.GUIToScreenPoint(buttonRect.position);

				if (parameters[1].ParameterType == typeof(GameObject[]))
					method.Invoke(null, new object[] { buttonRect, gameObjects });
				else if (parameters[1].ParameterType == typeof(Object[]))
					method.Invoke(null, new object[] { buttonRect, gameObjects.Cast<Object>().ToArray() });

				return;
			}
		}
	}
}
