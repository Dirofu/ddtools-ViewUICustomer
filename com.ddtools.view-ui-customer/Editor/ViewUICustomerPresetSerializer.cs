using System;
using Core.Scripts.UI.Universal.ViewUICustomer;
using UnityEditor;
using UnityEngine;

namespace Core.EditorTools.ViewUICustomer
{
	internal static class ViewUICustomerPresetSerializer
	{
		private const string StatesProperty = "_states";

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

						groupData.Elements.Add(new ViewUICustomerElementData
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
						});
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
	}
}
