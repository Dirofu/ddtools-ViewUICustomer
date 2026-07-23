using System;
using System.Collections.Generic;
using Core.Scripts.UI.Universal.ViewUICustomer;
using UnityEditor;
using UnityEngine;

namespace Core.EditorTools.ViewUICustomer
{
	internal static class ViewUICustomerPresetSerializer
	{
		private const string StatesProperty = "_states";

		[Serializable]
		private sealed class CapturedPropertyValue
		{
			public long Integer;
			public bool Boolean;
			public double Float;
			public string String;
			public Color Color;
			public Vector4 Vector;
			public Rect Rect;
			public AnimationCurve Curve;
			public Bounds Bounds;
			public Quaternion Quaternion;
			public Vector3Int VectorInt;
			public RectInt RectInt;
			public BoundsInt BoundsInt;
			public Gradient Gradient;
			public Hash128 Hash128;
		}

		public static ViewUICustomerPresetData Capture(MonoBehaviour component, IViewUICustomerEditor customer)
		{
			var data = new ViewUICustomerPresetData
			{
				ViewTypeName = customer.EditorViewType.FullName,
				ElementTypeName = customer.EditorElementType.FullName,
			};

			var serializedObject = new SerializedObject(component);
			var states = serializedObject.FindProperty(StatesProperty);

			if (states == null || states.isArray == false)
				throw new InvalidOperationException($"Serialized property [{StatesProperty}] was not found.");

			for (int stateIndex = 0; stateIndex < states.arraySize; stateIndex++)
			{
				var stateProperty = states.GetArrayElementAtIndex(stateIndex);
				var stateData = new ViewUICustomerStateData
				{
					ViewType = Enum.GetName(customer.EditorViewType, stateProperty.FindPropertyRelative("_stateType").intValue),
				};

				var groups = stateProperty.FindPropertyRelative("_groups");

				for (int groupIndex = 0; groupIndex < groups.arraySize; groupIndex++)
				{
					var groupProperty = groups.GetArrayElementAtIndex(groupIndex);
					var groupData = new ViewUICustomerGroupData
					{
						StateType = groupProperty.FindPropertyRelative("_type").intValue,
					};

					var elements = groupProperty.FindPropertyRelative("_elements");

					for (int elementIndex = 0; elementIndex < elements.arraySize; elementIndex++)
					{
						var elementProperty = elements.GetArrayElementAtIndex(elementIndex);
						var elementObject = elementProperty.FindPropertyRelative("_element").objectReferenceValue as GameObject;
						var material = elementProperty.FindPropertyRelative("_material").objectReferenceValue as Material;
						var materialPath = AssetDatabase.GetAssetPath(material);

						var elementData = new ViewUICustomerElementData
						{
							ElementType = Enum.GetName(customer.EditorElementType, elementProperty.FindPropertyRelative("_slotType").intValue),
							ElementPath = elementObject == null
								? string.Empty
								: AnimationUtility.CalculateTransformPath(elementObject.transform, component.transform),
							IsActive = elementProperty.FindPropertyRelative("_isActive").boolValue,
							Color = elementProperty.FindPropertyRelative("_color").colorValue,
							MaterialGuid = string.IsNullOrEmpty(materialPath)
								? string.Empty
								: AssetDatabase.AssetPathToGUID(materialPath),
							LocalPosition = elementProperty.FindPropertyRelative("_rect").vector3Value,
							UsesAnchoredPosition = elementProperty.FindPropertyRelative("_usesAnchoredPosition")?.boolValue == true,
							SizeDelta = elementProperty.FindPropertyRelative("_sizeDelta").vector2Value,
						};

						CaptureProperties(component, elementProperty, elementData);
						groupData.Elements.Add(elementData);
					}

					stateData.Groups.Add(groupData);
				}

				data.States.Add(stateData);
			}

			return data;
		}

		public static bool Apply(
			MonoBehaviour component,
			IViewUICustomerEditor customer,
			ViewUICustomerPresetData data,
			out string error)
		{
			error = null;

			if (data == null)
			{
				error = "Preset has no data.";
				return false;
			}

			if (data.ViewTypeName != customer.EditorViewType.FullName || data.ElementTypeName != customer.EditorElementType.FullName)
			{
				error = $"Preset types do not match the target. Expected {customer.EditorViewType.Name}/{customer.EditorElementType.Name}.";
				return false;
			}

			var serializedObject = new SerializedObject(component);
			var states = serializedObject.FindProperty(StatesProperty);

			if (states == null || states.isArray == false)
			{
				error = $"Serialized property [{StatesProperty}] was not found.";
				return false;
			}

			Undo.RecordObject(component, "Apply View UI Customer preset");
			serializedObject.Update();
			states.arraySize = data.States.Count;
			int missingElements = 0;

			for (int stateIndex = 0; stateIndex < data.States.Count; stateIndex++)
			{
				var stateData = data.States[stateIndex];
				var stateProperty = states.GetArrayElementAtIndex(stateIndex);
				stateProperty.FindPropertyRelative("_stateType").intValue = Convert.ToInt32(Enum.Parse(customer.EditorViewType, stateData.ViewType));
				var groups = stateProperty.FindPropertyRelative("_groups");
				groups.arraySize = stateData.Groups.Count;

				for (int groupIndex = 0; groupIndex < stateData.Groups.Count; groupIndex++)
				{
					var groupData = stateData.Groups[groupIndex];
					var groupProperty = groups.GetArrayElementAtIndex(groupIndex);
					groupProperty.FindPropertyRelative("_type").intValue = groupData.StateType;
					var elements = groupProperty.FindPropertyRelative("_elements");
					elements.arraySize = groupData.Elements.Count;

					for (int elementIndex = 0; elementIndex < groupData.Elements.Count; elementIndex++)
					{
						var elementData = groupData.Elements[elementIndex];
						var elementProperty = elements.GetArrayElementAtIndex(elementIndex);
						elementProperty.FindPropertyRelative("_slotType").intValue = Convert.ToInt32(Enum.Parse(customer.EditorElementType, elementData.ElementType));
						var elementTransform = string.IsNullOrEmpty(elementData.ElementPath)
							? component.transform
							: component.transform.Find(elementData.ElementPath);

						if (elementTransform == null)
							missingElements++;

						elementProperty.FindPropertyRelative("_element").objectReferenceValue = elementTransform == null
							? null
							: elementTransform.gameObject;
						elementProperty.FindPropertyRelative("_isActive").boolValue = elementData.IsActive;
						elementProperty.FindPropertyRelative("_color").colorValue = elementData.Color;
						elementProperty.FindPropertyRelative("_rect").vector3Value = elementData.LocalPosition;
						var positionMode = elementProperty.FindPropertyRelative("_usesAnchoredPosition");

						if (positionMode != null)
							positionMode.boolValue = elementData.UsesAnchoredPosition;

						elementProperty.FindPropertyRelative("_sizeDelta").vector2Value = elementData.SizeDelta;

						var materialPath = string.IsNullOrEmpty(elementData.MaterialGuid)
							? string.Empty
							: AssetDatabase.GUIDToAssetPath(elementData.MaterialGuid);
						elementProperty.FindPropertyRelative("_material").objectReferenceValue = string.IsNullOrEmpty(materialPath)
							? null
							: AssetDatabase.LoadAssetAtPath<Material>(materialPath);

						ApplyProperties(component, elementProperty, elementData);
					}
				}
			}

			serializedObject.ApplyModifiedProperties();
			customer.EditorRefreshStates();
			EditorUtility.SetDirty(component);
			PrefabUtility.RecordPrefabInstancePropertyModifications(component);

			if (missingElements > 0)
				error = $"Preset was applied, but {missingElements} hierarchy references could not be resolved.";

			return true;
		}

		private static void CaptureProperties(MonoBehaviour root, SerializedProperty elementProperty, ViewUICustomerElementData elementData)
		{
			var properties = elementProperty.FindPropertyRelative("_properties");
			if (properties == null || properties.isArray == false)
				return;

			for (int i = 0; i < properties.arraySize; i++)
			{
				var property = properties.GetArrayElementAtIndex(i);
				var componentTypeName = property.FindPropertyRelative("_componentType").stringValue;
				var propertyPath = property.FindPropertyRelative("_propertyPath").stringValue;

				if (IsTextContentProperty(Type.GetType(componentTypeName), propertyPath))
					continue;

				var objectValue = property.FindPropertyRelative("_objectValue").objectReferenceValue;
				var data = new ViewUICustomerPropertyData
				{
					ComponentType = componentTypeName,
					ComponentIndex = property.FindPropertyRelative("_componentIndex").intValue,
					PropertyPath = propertyPath,
					ValueType = property.FindPropertyRelative("_valueType").intValue,
					ValueJson = property.FindPropertyRelative("_valueJson").stringValue,
					ObjectComponentIndex = -1,
				};

				CaptureObjectReference(root.transform, objectValue, data);
				elementData.Properties.Add(data);
			}
		}

		private static void CaptureObjectReference(Transform root, UnityEngine.Object value, ViewUICustomerPropertyData data)
		{
			if (value == null)
				return;

			Transform targetTransform = null;
			if (value is GameObject gameObject)
				targetTransform = gameObject.transform;
			else if (value is Component component)
				targetTransform = component.transform;

			if (targetTransform != null && (targetTransform == root || targetTransform.IsChildOf(root)))
			{
				data.ObjectPath = AnimationUtility.CalculateTransformPath(targetTransform, root);
				data.ObjectType = value.GetType().AssemblyQualifiedName;

				if (value is Component targetComponent)
				{
					var sameTypeComponents = targetTransform.GetComponents(targetComponent.GetType());
					data.ObjectComponentIndex = Array.IndexOf(sameTypeComponents, targetComponent);
				}
				return;
			}

			data.ObjectGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
		}

		private static void ApplyProperties(MonoBehaviour root, SerializedProperty elementProperty, ViewUICustomerElementData elementData)
		{
			var properties = elementProperty.FindPropertyRelative("_properties");
			if (properties == null)
				return;

			var sourceProperties = (elementData.Properties ?? new List<ViewUICustomerPropertyData>())
				.FindAll(data => IsTextContentProperty(Type.GetType(data.ComponentType), data.PropertyPath) == false);
			properties.arraySize = sourceProperties.Count;

			for (int i = 0; i < sourceProperties.Count; i++)
			{
				var data = sourceProperties[i];
				var property = properties.GetArrayElementAtIndex(i);
				property.FindPropertyRelative("_componentType").stringValue = data.ComponentType;
				property.FindPropertyRelative("_componentIndex").intValue = data.ComponentIndex;
				property.FindPropertyRelative("_propertyPath").stringValue = data.PropertyPath;
				property.FindPropertyRelative("_valueType").intValue = data.ValueType;
				property.FindPropertyRelative("_valueJson").stringValue = data.ValueJson;
				property.FindPropertyRelative("_objectValue").objectReferenceValue = ResolveObjectReference(root.transform, data);
			}
		}

		private static UnityEngine.Object ResolveObjectReference(Transform root, ViewUICustomerPropertyData data)
		{
			if (string.IsNullOrEmpty(data.ObjectPath) == false || string.IsNullOrEmpty(data.ObjectType) == false)
			{
				var target = string.IsNullOrEmpty(data.ObjectPath) ? root : root.Find(data.ObjectPath);
				if (target == null)
					return null;

				var objectType = Type.GetType(data.ObjectType);
				if (objectType == null)
					return null;
				if (objectType == typeof(GameObject))
					return target.gameObject;
				if (objectType == typeof(Transform) || objectType == typeof(RectTransform))
					return target.GetComponent(objectType);

				var components = target.GetComponents(objectType);
				return data.ObjectComponentIndex >= 0 && data.ObjectComponentIndex < components.Length
					? components[data.ObjectComponentIndex]
					: null;
			}

			if (string.IsNullOrEmpty(data.ObjectGlobalId) == false
				&& GlobalObjectId.TryParse(data.ObjectGlobalId, out var globalObjectId))
				return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId);

			return null;
		}

		internal static void ApplyElementProperties(MonoBehaviour root, GameObject target, ViewUICustomerElementData elementData)
		{
			if (root == null || target == null || elementData?.Properties == null)
				return;

			foreach (var data in elementData.Properties)
			{
				var componentType = Type.GetType(data.ComponentType);
				if (componentType == null)
					continue;
				if (IsTextContentProperty(componentType, data.PropertyPath))
					continue;

				var components = target.GetComponents(componentType);
				if (data.ComponentIndex < 0 || data.ComponentIndex >= components.Length)
					continue;

				var serializedObject = new SerializedObject(components[data.ComponentIndex]);
				var property = serializedObject.FindProperty(data.PropertyPath);
				if (property == null)
					continue;

				var value = JsonUtility.FromJson<CapturedPropertyValue>(data.ValueJson) ?? new CapturedPropertyValue();
				if (TrySetSerializedValue(property, value, ResolveObjectReference(root.transform, data)) == false)
					continue;

				Undo.RecordObject(components[data.ComponentIndex], "Copy View UI element property");
				serializedObject.ApplyModifiedProperties();
			}
		}

		private static bool IsTextContentProperty(Type componentType, string propertyPath)
		{
			if (propertyPath != "m_text")
				return false;

			while (componentType != null)
			{
				if (componentType.FullName == "TMPro.TMP_Text")
					return true;

				componentType = componentType.BaseType;
			}

			return false;
		}

		private static bool TrySetSerializedValue(SerializedProperty property, CapturedPropertyValue value, UnityEngine.Object objectValue)
		{
			switch (property.propertyType)
			{
				case SerializedPropertyType.Integer:
				case SerializedPropertyType.Enum:
				case SerializedPropertyType.LayerMask:
				case SerializedPropertyType.Character: property.longValue = value.Integer; return true;
				case SerializedPropertyType.Boolean: property.boolValue = value.Boolean; return true;
				case SerializedPropertyType.Float: property.doubleValue = value.Float; return true;
				case SerializedPropertyType.String: property.stringValue = value.String; return true;
				case SerializedPropertyType.Color: property.colorValue = value.Color; return true;
				case SerializedPropertyType.ObjectReference: property.objectReferenceValue = objectValue; return true;
				case SerializedPropertyType.Vector2: property.vector2Value = value.Vector; return true;
				case SerializedPropertyType.Vector3: property.vector3Value = value.Vector; return true;
				case SerializedPropertyType.Vector4: property.vector4Value = value.Vector; return true;
				case SerializedPropertyType.Rect: property.rectValue = value.Rect; return true;
				case SerializedPropertyType.AnimationCurve: property.animationCurveValue = value.Curve; return true;
				case SerializedPropertyType.Bounds: property.boundsValue = value.Bounds; return true;
				case SerializedPropertyType.Quaternion: property.quaternionValue = value.Quaternion; return true;
				case SerializedPropertyType.Vector2Int: property.vector2IntValue = new Vector2Int(value.VectorInt.x, value.VectorInt.y); return true;
				case SerializedPropertyType.Vector3Int: property.vector3IntValue = value.VectorInt; return true;
				case SerializedPropertyType.RectInt: property.rectIntValue = value.RectInt; return true;
				case SerializedPropertyType.BoundsInt: property.boundsIntValue = value.BoundsInt; return true;
				case SerializedPropertyType.Gradient: property.gradientValue = value.Gradient; return true;
				case SerializedPropertyType.Hash128: property.hash128Value = value.Hash128; return true;
				default: return false;
			}
		}
	}
}
